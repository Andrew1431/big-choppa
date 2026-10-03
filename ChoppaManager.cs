using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigChoppa;

// Persistent controller: finds the local player, mirrors the host's list of choppas, seats the local player,
// drives the camera, and keeps remote passengers glued to their seats on this screen.
public class ChoppaManager : MonoBehaviour
{
    public ChoppaManager(IntPtr ptr) : base(ptr) { }

    enum CamMode { Chase, Cockpit }

    PlayerCharacter local;
    float nextPlayerSearch;
    uint Me => ChoppaNet.LocalNetId;

    readonly Dictionary<uint, Helicopter> helis = new();
    readonly Dictionary<uint, PlayerCharacter> players = new();
    readonly Dictionary<uint, float> sendTimers = new();
    float nextPlayerScan;
    int choppaLayer = -1;

    // Local player's seat.
    bool seated;
    Helicopter seatHeli;
    int seatIndex;
    uint pendingBoardId;
    float pendingBoardUntil;
    uint pendingSpawnId;

    Rigidbody playerBody;
    Transform playerRoot;
    Collider[] playerColliders = Array.Empty<Collider>();
    bool savedKinematic, savedDetect, savedBypassUpdate, savedBypassFixed, savedBypassLate;
    RigidbodyInterpolation savedInterp;
    // The visible body hangs off the kernal; drive its world rotation while seated in case it kept a tilt.
    Transform kernal;
    Quaternion savedKernalLocalRot;

    Camera cam;
    Transform camParent;
    Vector3 camLocalPos;
    Quaternion camLocalRot;
    Vector3 eyeOffset; // camera position relative to the player root at boarding

    CamMode camMode = CamMode.Chase;
    Vector2 smoothedCyclic; // deg/s, x = pitch, y = roll
    float lookYaw, lookPitch;
    Vector3 chaseVel;
    bool showHud;
    string lastHint;

    void Start()
    {
        showHud = ChoppaConfig.ShowHud.Value;
        Helicopter.Crashed += OnOwnCrash;
        ChoppaNet.ServerReceived = ChoppaServer.OnMessage;
        ChoppaNet.ClientReceived = OnClientMessage;
        ChoppaNet.ModeChanged = ClearWorld;
    }

    void OnDestroy() => Helicopter.Crashed -= OnOwnCrash;

    void OnApplicationQuit() => ChoppaLogbook.FlushNow();

    void Update()
    {
        try { Tick(); }
        catch (Exception e) { Plugin.L.LogError($"ChoppaManager.Update: {e}"); }
    }

    void Tick()
    {
        ChoppaConfig.Tick();
#if DEVBUILD
        DevAutoHost.Tick();
#endif
        ChoppaInput.Tick();
#if DEVBUILD
        DevTime.Tick();
#endif
        ChoppaNet.Tick();
        if (!FindLocalPlayer())
        {
            if (seated) ForceUnseat("local player disappeared");
            return;
        }
        ChoppaNet.LocalNetId = local.netId != 0 ? local.netId : 1u;

        PruneDead();
        if (Time.unscaledTime >= nextPlayerScan) ScanPlayers();

        bool inputAllowed = !ChoppaConfig.RequireCursorLock.Value || Cursor.lockState == CursorLockMode.Locked;

        if (inputAllowed && ChoppaInput.Pressed(ChoppaConfig.HudKey.Value)) showHud = !showHud;
        if (inputAllowed && ChoppaInput.Pressed(ChoppaConfig.SpawnKey.Value) && !seated) RequestSpawn();
        if (inputAllowed && ChoppaInput.Pressed(ChoppaConfig.ResetKey.Value)) ResetMine();
        if (inputAllowed && ChoppaInput.Pressed(ChoppaConfig.EnterExitKey.Value))
        {
            if (seated) Leave();
            else TryBoard();
        }
        if (pendingBoardId != 0 && Time.time > pendingBoardUntil)
        {
            pendingBoardId = 0;
            Hint("Couldn't board - the seat was taken or the host didn't answer.");
        }

        CheckSpawnSettled();

        foreach (var h in helis.Values)
            if (!h.IsProxy && !(seated && seatHeli == h && seatIndex == 0))
                h.CommandedRates = Vector3.zero;

        if (seated && seatIndex == 0 && !seatHeli.IsProxy)
        {
            FlyInput(inputAllowed);
            TrackFlight(seatHeli);
        }
        SendStates();
        ChoppaLogbook.Tick();
    }

    void FixedUpdate()
    {
        if (seated) PinPlayer();
        PinRemotePassengers();
    }

    void LateUpdate()
    {
        try
        {
            PinRemotePassengers();
            if (!seated) return;
            PinPlayer();
            UpdateCamera();
        }
        catch (Exception e) { Plugin.L.LogError($"ChoppaManager.LateUpdate: {e}"); }
    }

    // ---------- players ----------

    bool FindLocalPlayer()
    {
        if (local != null && Alive(local)) return true;
        local = null;
        if (Time.unscaledTime < nextPlayerSearch) return false;
        nextPlayerSearch = Time.unscaledTime + 1f;

        foreach (var pc in UnityEngine.Object.FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.None))
        {
            if (pc == null || !pc.isLocalPlayer) continue;
            local = pc;
            Plugin.L.LogInfo($"Found local player '{pc.gameObject.name}' netId {pc.netId}.");
            return true;
        }
        return false;
    }

    void ScanPlayers()
    {
        nextPlayerScan = Time.unscaledTime + 1f;
        players.Clear();
        foreach (var pc in UnityEngine.Object.FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.None))
            if (pc != null && pc.netId != 0) players[pc.netId] = pc;
        if (local != null) players[Me] = local;

        if (ChoppaNet.Mode == ChoppaNet.NetMode.Host) ChoppaServer.Prune(ChoppaNet.ConnectedPlayers(), Me);
        else if (ChoppaNet.Mode == ChoppaNet.NetMode.Offline) ChoppaServer.Prune(new HashSet<uint> { Me }, Me);
    }

    PlayerCharacter Player(uint netId)
    {
        if (netId == 0) return null;
        if (players.TryGetValue(netId, out var pc) && Alive(pc)) return pc;
        return null;
    }

    string NameOf(uint netId)
    {
        if (netId == Me) return "you";
        try
        {
            var n = Player(netId)?.playerNetworking?.username;
            if (!string.IsNullOrEmpty(n)) return n;
        }
        catch { }
        return $"player {netId}";
    }

    // ---------- world sync (client side) ----------

    void OnClientMessage(Msg type, BinaryReader r)
    {
        switch (type)
        {
            case Msg.Spawn:
            {
                uint id = r.ReadUInt32(), owner = r.ReadUInt32();
                OnSpawned(id, owner, r.ReadVector3(), r.ReadQuaternion());
                break;
            }
            case Msg.State:
                if (helis.TryGetValue(r.ReadUInt32(), out var hs)) hs.ReadState(r);
                break;
            case Msg.Seats:
            {
                uint id = r.ReadUInt32();
                int n = r.ReadByte();
                var occ = new uint[Helicopter.SeatCount];
                for (int i = 0; i < n; i++)
                {
                    uint o = r.ReadUInt32();
                    if (i < occ.Length) occ[i] = o;
                }
                if (helis.TryGetValue(id, out var h)) OnSeats(h, occ);
                break;
            }
            case Msg.Owner:
            {
                uint id = r.ReadUInt32(), owner = r.ReadUInt32();
                if (!helis.TryGetValue(id, out var h)) break;
                h.Owner = owner;
                h.SetProxy(owner != Me);
                Plugin.Verbose($"Choppa {id:X8} now simulated by {NameOf(owner)}.");
                break;
            }
            case Msg.Crash:
            {
                uint id = r.ReadUInt32();
                Vector3 impact = r.ReadVector3();
                int seed = r.ReadInt32();
                if (helis.TryGetValue(id, out var h)) HandleCrash(h, impact, seed);
                break;
            }
            case Msg.Despawn:
            {
                uint id = r.ReadUInt32();
                if (!helis.TryGetValue(id, out var h)) break;
                if (seated && seatHeli == h) ForceUnseat("choppa removed");
                helis.Remove(id);
                Discard(h);
                break;
            }
        }
    }

    void OnSpawned(uint id, uint owner, Vector3 pos, Quaternion rot)
    {
        if (helis.ContainsKey(id) || local == null) return;
        if (choppaLayer < 0) choppaLayer = PickLayer(GroundBelow(pos + Vector3.up * 2f, 10f, out _));
        var h = Helicopter.Build(id, pos, rot, choppaLayer, local.gameObject.scene);
        h.Owner = owner;
        h.SetProxy(owner != Me);
        helis[id] = h;
        Plugin.L.LogInfo($"Choppa {id:X8} spawned at {pos} by {NameOf(owner)}{(h.IsProxy ? " (remote)" : "")}.");
        if (id == pendingSpawnId)
        {
            pendingSpawnId = 0;
            spawnHeli = h;
            ChoppaLogbook.Spawned();
            spawnY = pos.y;
            spawnCheckAt = Time.time + 2f;
        }
    }

    void OnSeats(Helicopter h, uint[] occ)
    {
        var old = h.Occupants;
        for (int i = 0; i < occ.Length; i++)
        {
            if (old[i] == occ[i]) continue;
            if (old[i] != 0 && old[i] != Me) IgnoreRemote(h, old[i], false);
            if (occ[i] != 0 && occ[i] != Me) IgnoreRemote(h, occ[i], true);
        }
        h.Occupants = occ;
        if (!h.IsProxy) h.EngineOn = occ[0] != 0;

        int mine = Array.IndexOf(occ, Me);
        if (seated && seatHeli == h && mine != seatIndex)
        {
            if (mine < 0) ForceUnseat("host removed us from the seat");
            else seatIndex = mine;
        }
        else if (!seated && mine >= 0 && pendingBoardId == h.Id)
        {
            pendingBoardId = 0;
            Seat(h, mine);
        }
    }

    void IgnoreRemote(Helicopter h, uint netId, bool ignore)
    {
        var pc = Player(netId);
        if (pc == null) return;
        foreach (var hc in h.GetComponentsInChildren<Collider>(true))
        foreach (var c in pc.GetComponentsInChildren<Collider>(true))
            if (hc != null && c != null) Physics.IgnoreCollision(hc, c, ignore);
    }

    // Other players' clients pin themselves to their seat, but their synced position lags our copy of the
    // choppa differently, so on our screen we snap them onto our copy's seat.
    void PinRemotePassengers()
    {
        foreach (var h in helis.Values)
        {
            if (h == null || h.Broken) continue;
            for (int i = 0; i < Helicopter.SeatCount; i++)
            {
                uint occ = h.Occupants[i];
                if (occ == 0 || occ == Me) continue;
                var pc = Player(occ);
                if (pc == null) continue;
                var seat = h.Seats[i];
                pc.transform.SetPositionAndRotation(seat.position, seat.rotation);
            }
        }
    }

    void SendStates()
    {
        if (ChoppaNet.Mode is not (ChoppaNet.NetMode.Client or ChoppaNet.NetMode.Host) || !ChoppaNet.Ready) return;
        foreach (var h in helis.Values)
        {
            if (h.IsProxy || h.Broken || h.Owner != Me) continue;
            bool busy = h.Occupants.Any(o => o != 0) || h.Body.linearVelocity.sqrMagnitude > 0.01f;
            float interval = 1f / Mathf.Max(1f, busy ? ChoppaConfig.NetSendRate.Value : 2f);
            sendTimers.TryGetValue(h.Id, out float t);
            t -= Time.deltaTime;
            if (t <= 0f)
            {
                t += interval;
                if (t < 0f) t = interval;
                ChoppaNet.ToServer(ChoppaNet.Write(Msg.State, h.WriteState), false);
            }
            sendTimers[h.Id] = t;
        }
    }

    void PruneDead()
    {
        List<uint> dead = null;
        foreach (var kv in helis)
            if (!Alive(kv.Value)) (dead ??= new()).Add(kv.Key);
        if (dead == null) return;
        foreach (var id in dead)
        {
            if (seated && seatHeli != null && !Alive(seatHeli)) ForceUnseat("choppa destroyed");
            helis.Remove(id);
            sendTimers.Remove(id);
        }
    }

    // Network session started/ended: everything we had belonged to the old session.
    void ClearWorld()
    {
        if (seated) ForceUnseat("network session changed");
        foreach (var h in helis.Values) Discard(h);
        helis.Clear();
        sendTimers.Clear();
        pendingBoardId = pendingSpawnId = 0;
        ChoppaServer.Reset();
    }

    // Pocketed items drop to the ground instead of vanishing with the choppa.
    static void Discard(Helicopter h)
    {
        if (!Alive(h)) return;
        try { h.Pockets?.ReleaseAll(Vector3.zero); }
        catch (Exception e) { Plugin.L.LogError($"Emptying pockets: {e}"); }
        Destroy(h.gameObject);
    }

    // ---------- spawn / board ----------

    void RequestSpawn()
    {
        if (!ChoppaNet.Ready) { Hint("Waiting for the host's Big Choppa to answer (does the host have the mod?)."); return; }

        var root = local.transform;
        var fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        float s = ChoppaConfig.HeliScale.Value;

        Vector3 pos = root.position + fwd * (6f * s);
        Collider ground = GroundBelow(pos + Vector3.up * 30f, 80f, out var groundPoint);
        if (ground != null) pos = groundPoint + Vector3.up * 0.15f;
        if (choppaLayer < 0) choppaLayer = PickLayer(ground);

        // Side-on to the player so they walk up to the pilot's door.
        var rot = Quaternion.LookRotation(Vector3.Cross(Vector3.up, fwd), Vector3.up);

        // One parked choppa per player: replace any of ours nobody is sitting in.
        foreach (var h in helis.Values)
            if (h.Owner == Me && h.Occupants.All(o => o == 0))
            {
                uint oldId = h.Id;
                ChoppaNet.ToServer(ChoppaNet.Write(Msg.Despawn, w => w.Write(oldId)), true);
            }

        uint id;
        do id = (uint)UnityEngine.Random.Range(1, int.MaxValue) ^ ((uint)UnityEngine.Random.Range(0, 2) << 31);
        while (id == 0 || helis.Keys.Any(k => ChoppaPockets.Slot(k) == ChoppaPockets.Slot(id))); // pockets need a free ticket slot
        pendingSpawnId = id;
        ChoppaNet.ToServer(ChoppaNet.Write(Msg.Spawn, w => { w.Write(id); w.Write(pos); w.Write(rot); }), true);
    }

    Helicopter spawnHeli;
    float spawnY, spawnCheckAt = -1f;

    void CheckSpawnSettled()
    {
        if (spawnCheckAt < 0f || Time.time < spawnCheckAt) return;
        spawnCheckAt = -1f;
        if (spawnHeli == null || !Alive(spawnHeli)) return;
        float drop = spawnY - spawnHeli.transform.position.y;
        if (drop > 1f) Plugin.L.LogWarning($"Choppa fell {drop:0.0}m in 2s after spawning - still not colliding with the ground.");
        else Plugin.Verbose($"Choppa settled (moved {drop:0.00}m vertically). Grounded={spawnHeli.Grounded}");
    }

    void ResetMine()
    {
        Helicopter target = null;
        if (seated && !seatHeli.IsProxy) target = seatHeli;
        else
        {
            float best = ChoppaConfig.EnterDistance.Value * ChoppaConfig.HeliScale.Value * 3f;
            foreach (var h in helis.Values)
            {
                if (h.IsProxy) continue;
                float d = DistanceTo(h);
                if (d < best) { best = d; target = h; }
            }
        }
        if (target != null)
        {
            ChoppaLogbook.FlippedUpright(seated);
            target.ResetUpright();
        }
        else Hint("Get in or next to a choppa you're flying to flip it upright.");
    }

    Collider GroundBelow(Vector3 origin, float distance, out Vector3 point)
    {
        point = default;
        Collider best = null;
        float bestDist = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            var c = hit.collider;
            if (c == null || hit.distance >= bestDist) continue;
            if (c.transform.IsChildOf(local.transform)) continue;
            if (c.GetComponentInParent<Helicopter>() != null) continue;
            best = c;
            bestDist = hit.distance;
            point = hit.point;
        }
        return best;
    }

    // Big Walk has a custom layer collision matrix, so Default may not touch the ground. Prefer a layer that the
    // game's own loose physics props use, as long as it collides with whatever the player is standing on.
    int PickLayer(Collider rayGround)
    {
        int forced = ChoppaConfig.ChoppaLayer.Value;
        if (forced >= 0 && forced < 32) return forced;

        int groundLayer = -1;
        try
        {
            var stoodOn = local.ground?.groundCollider;
            if (stoodOn != null) groundLayer = stoodOn.gameObject.layer;
        }
        catch (Exception e) { Plugin.Verbose($"PlayerGround lookup failed: {e.Message}"); }
        if (groundLayer < 0 && rayGround != null) groundLayer = rayGround.gameObject.layer;
        if (groundLayer < 0) groundLayer = 0;

        var counts = new int[32];
        foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (rb == null || rb.isKinematic || rb.GetComponentInParent<PlayerCharacter>() != null) continue;
            if (rb.GetComponentInParent<Helicopter>() != null) continue;
            int l = rb.gameObject.layer;
            if (!Physics.GetIgnoreLayerCollision(l, groundLayer)) counts[l]++;
        }
        int bestProp = -1;
        for (int l = 0; l < 32; l++)
            if (counts[l] > 0 && (bestProp < 0 || counts[l] > counts[bestProp])) bestProp = l;
        if (bestProp >= 0) return bestProp;

        int playerLayer = (local.rb != null ? local.rb.gameObject : local.gameObject).layer;
        if (!Physics.GetIgnoreLayerCollision(playerLayer, groundLayer)) return playerLayer;
        for (int l = 0; l < 32; l++)
            if (!Physics.GetIgnoreLayerCollision(l, groundLayer)) return l;
        return 0;
    }

    // Distance from the player to the nearest solid part, so boarding works from any side of a long airframe.
    float DistanceTo(Helicopter h)
    {
        Vector3 p = local.transform.position;
        float best = Vector3.Distance(p, h.transform.position);
        foreach (var c in h.GetComponentsInChildren<Collider>())
            best = Mathf.Min(best, Vector3.Distance(p, c.ClosestPoint(p)));
        return best;
    }

    Helicopter NearestBoardable(out float dist)
    {
        Helicopter best = null;
        dist = float.MaxValue;
        float max = ChoppaConfig.EnterDistance.Value * ChoppaConfig.HeliScale.Value;
        foreach (var h in helis.Values)
        {
            if (h == null || h.Broken) continue;
            float d = DistanceTo(h);
            if (d < dist) { dist = d; best = h; }
        }
        return dist <= max ? best : null;
    }

    static int FreeSeat(Helicopter h)
    {
        for (int i = 0; i < Helicopter.SeatCount; i++)
            if (h.Occupants[i] == 0) return i;
        return -1;
    }

    void TryBoard()
    {
        if (helis.Count == 0) { Hint($"No choppa yet - press {ChoppaConfig.SpawnKey.Value} to spawn one."); return; }
        var h = NearestBoardable(out float dist);
        if (h == null) { Hint($"Too far from a choppa ({dist:0.0}m, need {ChoppaConfig.EnterDistance.Value * ChoppaConfig.HeliScale.Value:0.0}m)."); return; }
        int seat = FreeSeat(h);
        if (seat < 0) { Hint("That choppa is full."); return; }

        pendingBoardId = h.Id;
        pendingBoardUntil = Time.time + 3f;
        byte s = (byte)seat;
        uint id = h.Id;
        ChoppaNet.ToServer(ChoppaNet.Write(Msg.Board, w => { w.Write(id); w.Write(s); }), true);
    }

    void Seat(Helicopter h, int index)
    {
        seatHeli = h;
        seatIndex = index;
        playerBody = local.rb;
        playerRoot = playerBody != null ? playerBody.transform : local.transform;

        savedBypassUpdate = local.bypassUpdate;
        savedBypassFixed = local.bypassFixedUpdate;
        savedBypassLate = local.bypassLateUpdate;
        local.bypassUpdate = true;
        local.bypassFixedUpdate = true;
        local.bypassLateUpdate = true;

        if (playerBody != null)
        {
            savedKinematic = playerBody.isKinematic;
            savedDetect = playerBody.detectCollisions;
            savedInterp = playerBody.interpolation;
            playerBody.linearVelocity = Vector3.zero;
            playerBody.isKinematic = true;
            playerBody.detectCollisions = false;
            playerBody.interpolation = RigidbodyInterpolation.None;
        }

        playerColliders = local.GetComponentsInChildren<Collider>(true).ToArray();
        foreach (var hc in h.GetComponentsInChildren<Collider>(true))
        foreach (var pc in playerColliders)
            Physics.IgnoreCollision(hc, pc, true);

        if (ChoppaConfig.SitWhileFlying.Value) SetSitting(true);

        kernal = local.kernal;
        if (kernal != null) savedKernalLocalRot = kernal.localRotation;

        cam = null;
        try { cam = local.cameraMinder?.playerCameraReferences?.playerCamera; } catch (Exception e) { Plugin.Verbose($"cameraMinder lookup failed: {e.Message}"); }
        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            eyeOffset = playerRoot.InverseTransformPoint(cam.transform.position);
            camParent = cam.transform.parent;
            camLocalPos = cam.transform.localPosition;
            camLocalRot = cam.transform.localRotation;
            cam.transform.SetParent(null, true);
        }
        else Plugin.L.LogWarning("No camera found to take over; flying with whatever the game does.");

        seated = true;
        flightStart = -1f;
        rideStart = index == 0 ? -1f : Time.time;
        ChoppaLogbook.Boarded(index == 0);
        if (index == 0 && !h.IsProxy)
        {
            h.EngineOn = true;
            h.Collective = 0f;
        }
        smoothedCyclic = Vector2.zero;
        lookYaw = lookPitch = 0f;
        PinPlayer();
        Plugin.L.LogInfo(index == 0 ? "Boarded choppa as pilot. Engine starting." : $"Boarded choppa as passenger (seat {index}).");
    }

    void Leave()
    {
        var h = seatHeli;
        int index = seatIndex;
        Vector3 exitPos = h.Exits[index].position;
        Vector3 vel = h.Velocity;
        Quaternion exitRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(h.transform.forward, Vector3.up).normalized + Vector3.forward * 1e-4f, Vector3.up);
        if (index == 0 && !h.IsProxy)
        {
            h.EngineOn = false;
            h.CommandedRates = Vector3.zero;
        }
        float height = Altitude(h);
        if (height > 1.5f) ChoppaLogbook.JumpedOut(index == 0, height, vel.magnitude * 3.6f);
        if (index == 0) EndFlight(h, h.Grounded ? "landed" : "bailed", vel.magnitude * 3.6f);
        else EndRide(height > 1.5f ? "jumped out" : "got out");
        uint id = h.Id;
        ChoppaNet.ToServer(ChoppaNet.Write(Msg.Leave, w => w.Write(id)), true);
        Restore(exitPos, exitRot, vel);
        Plugin.L.LogInfo("Left choppa.");
    }

    void ForceUnseat(string why)
    {
        Plugin.L.LogWarning($"Forced out of choppa: {why}");
        try
        {
            if (local != null && Alive(local)) Restore(playerRoot.position, Quaternion.Euler(0f, playerRoot.eulerAngles.y, 0f), Vector3.zero);
            else RestoreCamera();
        }
        catch (Exception e) { Plugin.L.LogError(e); }
        seated = false;
        seatHeli = null;
    }

    void Restore(Vector3 pos, Quaternion rot, Vector3 vel)
    {
        seated = false;

        if (ChoppaConfig.SitWhileFlying.Value) SetSitting(false);

        playerRoot.SetPositionAndRotation(pos, rot);
        if (playerBody != null)
        {
            playerBody.position = pos;
            playerBody.rotation = rot;
            playerBody.isKinematic = savedKinematic;
            playerBody.detectCollisions = savedDetect;
            playerBody.interpolation = savedInterp;
            if (!playerBody.isKinematic) playerBody.linearVelocity = vel;
        }

        if (kernal != null && Alive(kernal)) kernal.localRotation = savedKernalLocalRot;
        kernal = null;

        local.bypassUpdate = savedBypassUpdate;
        local.bypassFixedUpdate = savedBypassFixed;
        local.bypassLateUpdate = savedBypassLate;

        if (seatHeli != null && Alive(seatHeli))
            foreach (var hc in seatHeli.GetComponentsInChildren<Collider>(true))
            foreach (var pc in playerColliders)
                if (hc != null && pc != null) Physics.IgnoreCollision(hc, pc, false);

        seatHeli = null;
        RestoreCamera();
    }

    void RestoreCamera()
    {
        if (cam == null || !Alive(cam)) return;
        cam.transform.SetParent(camParent, false);
        cam.transform.localPosition = camLocalPos;
        cam.transform.localRotation = camLocalRot;
        cam = null;
    }

    void SetSitting(bool sitting)
    {
        try { local.sitter?.SetSittingLocal(sitting); }
        catch (Exception e) { Plugin.Verbose($"SetSittingLocal({sitting}) failed: {e.Message}"); }
        try { local.playerNetworking?.CmdSetSitting(sitting); }
        catch (Exception e) { Plugin.Verbose($"CmdSetSitting({sitting}) failed: {e.Message}"); }
    }

    void PinPlayer()
    {
        if (seatHeli == null || !Alive(seatHeli) || playerRoot == null) return;
        var seat = seatHeli.Seats[seatIndex];
        playerRoot.SetPositionAndRotation(seat.position, seat.rotation);
        if (playerBody != null)
        {
            playerBody.position = seat.position;
            playerBody.rotation = seat.rotation;
        }
        if (kernal != null) kernal.rotation = seat.rotation;
    }

    // ---------- flying ----------

    void FlyInput(bool inputAllowed)
    {
        var heli = seatHeli;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector2 mouse = inputAllowed ? ChoppaInput.MouseDelta() : Vector2.zero;
        bool freeLook = inputAllowed && ChoppaInput.Held(ChoppaConfig.FreeLookKey.Value);

        if (inputAllowed && ChoppaInput.Pressed(ChoppaConfig.CameraKey.Value))
            camMode = camMode == CamMode.Chase ? CamMode.Cockpit : CamMode.Chase;

        float collectiveIn = inputAllowed ? ChoppaInput.Axis(ChoppaConfig.CollectiveDownKey.Value, ChoppaConfig.CollectiveUpKey.Value) : 0f;
        if (!ChoppaConfig.CollectiveSpringBack.Value)
            heli.Collective = Mathf.Clamp01(heli.Collective + collectiveIn * ChoppaConfig.CollectiveRate.Value * dt);
        else if (collectiveIn != 0f)
            heli.Collective = Mathf.MoveTowards(heli.Collective, collectiveIn > 0f ? 1f : 0f, ChoppaConfig.CollectiveRate.Value * dt);
        else
        {
            // Sitting on the skids it spools down to idle so it doesn't skate around weightless; airborne it settles at hover.
            float rest = 0f;
            if (!heli.Grounded)
            {
                rest = heli.HoverCollective;
                if (ChoppaConfig.HoverTiltCompensation.Value)
                    rest /= Mathf.Max(0.75f, Vector3.Dot(heli.transform.up, Vector3.up));
            }
            heli.Collective = Mathf.MoveTowards(heli.Collective, Mathf.Clamp01(rest), ChoppaConfig.CollectiveReturnRate.Value * dt);
        }

        float pedal = inputAllowed ? ChoppaInput.Axis(ChoppaConfig.PedalLeftKey.Value, ChoppaConfig.PedalRightKey.Value) : 0f;

        Vector2 cyclicTarget = Vector2.zero;
        if (freeLook)
        {
            lookYaw = Mathf.Clamp(lookYaw + mouse.x * 2f, -160f, 160f);
            lookPitch = Mathf.Clamp(lookPitch - mouse.y * 2f, -70f, 70f);
        }
        else
        {
            float k = ChoppaConfig.MouseSensitivity.Value / dt;
            float pitch = mouse.y * k * (ChoppaConfig.InvertPitch.Value ? -1f : 1f);
            float roll = -mouse.x * k * (ChoppaConfig.InvertRoll.Value ? -1f : 1f);
            float max = ChoppaConfig.MaxPitchRollRate.Value;
            cyclicTarget = new Vector2(Mathf.Clamp(pitch, -max, max), Mathf.Clamp(roll, -max, max));
            float back = 1f - Mathf.Exp(-dt * 8f);
            lookYaw = Mathf.Lerp(lookYaw, 0f, back);
            lookPitch = Mathf.Lerp(lookPitch, 0f, back);
        }

        float smooth = ChoppaConfig.MouseSmoothing.Value;
        float a = smooth <= 0f ? 1f : 1f - Mathf.Exp(-dt / smooth);
        smoothedCyclic = Vector2.Lerp(smoothedCyclic, cyclicTarget, a);

        heli.CommandedRates = new Vector3(smoothedCyclic.x, pedal * ChoppaConfig.YawRate.Value, smoothedCyclic.y);
    }

    void UpdateCamera()
    {
        if (cam == null || !Alive(cam) || seatHeli == null || !Alive(seatHeli)) return;
        var ht = seatHeli.transform;

        // Passengers can look around freely with the mouse and switch cameras too.
        bool pilot = seatIndex == 0 && !seatHeli.IsProxy;
        bool inputAllowed = !ChoppaConfig.RequireCursorLock.Value || Cursor.lockState == CursorLockMode.Locked;
        if (!pilot && inputAllowed)
        {
            if (ChoppaInput.Pressed(ChoppaConfig.CameraKey.Value))
                camMode = camMode == CamMode.Chase ? CamMode.Cockpit : CamMode.Chase;
            Vector2 m = ChoppaInput.MouseDelta();
            lookYaw = Mathf.Clamp(lookYaw + m.x * 2f, -170f, 170f);
            lookPitch = Mathf.Clamp(lookPitch - m.y * 2f, -70f, 70f);
        }

        if (camMode == CamMode.Cockpit)
        {
            Vector3 eye = seatHeli.Seats[seatIndex].TransformPoint(eyeOffset) + ht.up * ChoppaConfig.CockpitEyeHeight.Value;
            cam.transform.SetPositionAndRotation(eye, ht.rotation * Quaternion.Euler(lookPitch, lookYaw, 0f));
            return;
        }

        float s = ChoppaConfig.HeliScale.Value;
        float yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(ht.forward, Vector3.up).normalized + Vector3.forward * 1e-4f).eulerAngles.y;
        var orbit = Quaternion.Euler(10f + lookPitch, yaw + lookYaw, 0f);
        Vector3 focus = ht.position + Vector3.up * (2f * s);
        Vector3 desired = focus + orbit * new Vector3(0f, 0f, -ChoppaConfig.ChaseDistance.Value * s) + Vector3.up * (ChoppaConfig.ChaseHeight.Value * s - 2f * s);
        cam.transform.position = Vector3.SmoothDamp(cam.transform.position, desired, ref chaseVel, 0.12f);
        cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position, Vector3.up);
    }

    // ---------- pilot's logbook ----------

    float flightStart = -1f, flightMaxAlt, flightTopSpeed, flightDistance, groundedSince = -1f;
    float flightUpsideDown, flightCockpitTime, flightRollAccum, flightPitchAccum, rideStart = -1f;
    int flightRolls, flightLoops;
    Vector3 flightLastPos;

    // Height above whatever is below, ignoring the choppa's own parts. Works for proxies too (no Grounded there).
    static float Altitude(Helicopter h)
    {
        float best = -1f;
        foreach (var hit in Physics.RaycastAll(h.transform.position + Vector3.up * 0.5f, Vector3.down, 2000f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(h.transform)) continue;
            if (hit.collider.GetComponentInParent<PlayerCharacter>() != null) continue;
            if (best < 0f || hit.distance < best) best = hit.distance;
        }
        return best < 0f ? -1f : Mathf.Max(0f, best - 0.5f);
    }

    // A flight runs from lift-off until 2 s back on the ground, the pilot hopping out, or... the other thing.
    void TrackFlight(Helicopter h)
    {
        if (!h.Grounded)
        {
            groundedSince = -1f;
            Vector3 pos = h.transform.position;
            if (flightStart < 0f)
            {
                flightStart = Time.time;
                flightMaxAlt = flightTopSpeed = flightDistance = 0f;
                flightUpsideDown = flightCockpitTime = flightRollAccum = flightPitchAccum = 0f;
                flightRolls = flightLoops = 0;
                flightLastPos = pos;
            }
            float dt = Time.deltaTime;
            if (Vector3.Dot(h.transform.up, Vector3.up) < 0f) flightUpsideDown += dt;
            if (camMode == CamMode.Cockpit) flightCockpitTime += dt;

            // Integrate rotation about the choppa's own axes; a full 360 one way counts as a roll or loop.
            // Wobbling back and forth cancels out.
            Vector3 w = h.transform.InverseTransformDirection(h.Body.angularVelocity) * (Mathf.Rad2Deg * dt);
            flightRollAccum += w.z;
            flightPitchAccum += w.x;
            while (Mathf.Abs(flightRollAccum) >= 360f) { flightRolls++; flightRollAccum -= Mathf.Sign(flightRollAccum) * 360f; }
            while (Mathf.Abs(flightPitchAccum) >= 360f) { flightLoops++; flightPitchAccum -= Mathf.Sign(flightPitchAccum) * 360f; }
            flightDistance += Vector3.Distance(pos, flightLastPos);
            flightLastPos = pos;
            flightMaxAlt = Mathf.Max(flightMaxAlt, Altitude(h));
            flightTopSpeed = Mathf.Max(flightTopSpeed, h.Body.linearVelocity.magnitude * 3.6f);
        }
        else if (flightStart >= 0f)
        {
            if (groundedSince < 0f) groundedSince = Time.time;
            else if (Time.time - groundedSince > 2f) EndFlight(h, "landed", h.Velocity.magnitude * 3.6f);
        }
    }

    void EndFlight(Helicopter h, string how, float endSpeedKmh)
    {
        if (flightStart < 0f) return;
        float duration = Time.time - flightStart;
        flightStart = -1f;
        groundedSince = -1f;
        if (duration < 1f) return;
        ChoppaLogbook.FlightEnded(new ChoppaLogbook.Flight
        {
            Seconds = duration,
            Distance = flightDistance,
            MaxAltitude = flightMaxAlt,
            TopSpeedKmh = flightTopSpeed,
            EndSpeedKmh = endSpeedKmh,
            How = how,
            Riders = h.Occupants.Count(o => o != 0),
            Rolls = flightRolls,
            Loops = flightLoops,
            UpsideDownSeconds = flightUpsideDown,
            CockpitPercent = 100f * flightCockpitTime / duration,
        });
    }

    void EndRide(string how)
    {
        if (rideStart < 0f) return;
        float duration = Time.time - rideStart;
        rideStart = -1f;
        ChoppaLogbook.RideEnded(duration, how);
    }

    // ---------- crashing ----------

    // Our PC simulates this choppa and it just hit something hard: tell everyone, then break our copy.
    void OnOwnCrash(Helicopter crashed, float deltaV)
    {
        int seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        uint id = crashed.Id;
        Vector3 impact = crashed.ImpactVelocity;
        try { ChoppaNet.ToServer(ChoppaNet.Write(Msg.Crash, w => { w.Write(id); w.Write(impact); w.Write(seed); }), true); }
        catch (Exception e) { Plugin.L.LogError($"Sending crash: {e}"); }
        HandleCrash(crashed, impact, seed);
    }

    void HandleCrash(Helicopter h, Vector3 impact, int seed)
    {
        h.Broken = true;
        h.ImpactVelocity = impact;
        try
        {
            Vector3 crashPos = h.transform.position;
            if (seated && seatHeli == h)
            {
                if (seatIndex == 0) EndFlight(h, "ended abruptly", impact.magnitude * 3.6f);
                else EndRide("ended abruptly");
                Vector3 seatPos = h.Seats[seatIndex].position + Vector3.up * (1.0f * ChoppaConfig.HeliScale.Value);
                Vector3 fling = impact * 0.4f + Vector3.up * 9f + UnityEngine.Random.insideUnitSphere * 3f;
                var yaw = Quaternion.Euler(0f, h.transform.eulerAngles.y, 0f);
                h.EngineOn = false;
                Restore(seatPos, yaw, fling);
                Stun("ejected in a crash");
            }
            else if (local != null && Alive(local))
            {
                Vector3 toPlayer = local.transform.position - crashPos;
                float radius = ChoppaConfig.CrashStunRadius.Value * ChoppaConfig.HeliScale.Value;
                if (toPlayer.magnitude < radius)
                {
                    var rb = local.rb;
                    if (rb != null && !rb.isKinematic)
                        rb.linearVelocity += (Vector3.ProjectOnPlane(toPlayer, Vector3.up).normalized + Vector3.up) * 7f;
                    Stun("too close to a crash");
                }
            }
        }
        catch (Exception e) { Plugin.L.LogError($"Crash handling: {e}"); }

        helis.Remove(h.Id);
        sendTimers.Remove(h.Id);
        h.Pockets?.ReleaseAll(impact);
        ChoppaCrash.Break(h, seed);
        Hint($"Choppa destroyed! Press {ChoppaConfig.SpawnKey.Value} for a new one.");
    }

    void Stun(string why)
    {
        try
        {
            var faller = local.faller;
            if (faller == null) { Plugin.L.LogWarning("No PlayerFaller on local player; can't stun."); return; }
            faller.TriggerFall();
            Plugin.Verbose($"Stun ({why}): TriggerFall() -> isDazed={faller.isDazed}");
        }
        catch (Exception e) { Plugin.L.LogWarning($"Stun failed: {e.Message}"); }
    }

    // ---------- HUD ----------

    void OnGUI()
    {
        if (!showHud) return;
        try
        {
            if (seated && seatHeli != null) DrawFlightHud();
            else if (local != null && Alive(local))
            {
                var h = NearestBoardable(out _);
                if (h != null)
                {
                    int seat = FreeSeat(h);
                    string what = seat < 0 ? "Choppa full" : seat == 0 ? $"[{ChoppaConfig.EnterExitKey.Value}] Fly choppa" : $"[{ChoppaConfig.EnterExitKey.Value}] Ride with {NameOf(h.Occupants[0])}";
                    GUI.Label(new Rect(Screen.width / 2f - 150f, Screen.height * 0.65f, 300f, 24f), what);
                }
            }
            if (lastHint != null && Time.unscaledTime < hintUntil)
                GUI.Label(new Rect(Screen.width / 2f - 250f, Screen.height * 0.70f, 500f, 24f), lastHint);
            if (ChoppaConfig.VerboseLogging.Value)
                GUI.Label(new Rect(10f, Screen.height - 24f, 600f, 22f), $"Big Choppa net: {ChoppaNet.Mode} ready={ChoppaNet.Ready} me={Me} choppas={helis.Count}");
        }
        catch (Exception e) { Plugin.L.LogError($"OnGUI: {e.Message}"); showHud = false; }
    }

    void DrawFlightHud()
    {
        var heli = seatHeli;
        float alt = -1f;
        if (Physics.Raycast(heli.transform.position, Vector3.down, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
            alt = hit.distance;
        Vector3 v = heli.Velocity;
        bool pilot = seatIndex == 0;

        var sb = new StringBuilder();
        sb.AppendLine($"BIG CHOPPA   rotor {heli.RotorSpin * 100f:0}%   {(pilot ? "PILOT" : $"passenger - pilot: {(heli.Occupants[0] != 0 ? NameOf(heli.Occupants[0]) : "nobody!")}")}");
        if (pilot) sb.AppendLine($"Collective {heli.Collective * 100f:0}%  (hover ~{heli.HoverCollective * 100f:0}%)  {(heli.Grounded ? "ON GROUND" : "airborne")}");
        sb.AppendLine($"Alt {(alt < 0 ? "--" : alt.ToString("0.0"))} m   Spd {new Vector2(v.x, v.z).magnitude * 3.6f:0} km/h   VS {v.y:+0.0;-0.0} m/s");
        var e = heli.transform.eulerAngles;
        sb.AppendLine($"Pitch {Signed(e.x):0}°  Roll {Signed(e.z):0}°  Hdg {e.y:000}°   Cam {camMode}");
        if (pilot && ChoppaConfig.VerboseLogging.Value)
        {
            Vector2 m = ChoppaInput.MouseDelta();
            sb.AppendLine($"input={ChoppaInput.ActiveBackend} mouse=({m.x:0.00},{m.y:0.00}) cmd={heli.CommandedRates.x:0},{heli.CommandedRates.y:0},{heli.CommandedRates.z:0} lock={Cursor.lockState}");
        }
        if (pilot)
            sb.Append($"[{ChoppaConfig.CollectiveUpKey.Value}/{ChoppaConfig.CollectiveDownKey.Value}] collective  [{ChoppaConfig.PedalLeftKey.Value}/{ChoppaConfig.PedalRightKey.Value}] pedals  [Mouse] cyclic  " +
                      $"[{ChoppaConfig.FreeLookKey.Value}] look  [{ChoppaConfig.CameraKey.Value}] camera  [{ChoppaConfig.ResetKey.Value}] upright  [{ChoppaConfig.EnterExitKey.Value}] exit");
        else
            sb.Append($"[Mouse] look  [{ChoppaConfig.CameraKey.Value}] camera  [{ChoppaConfig.EnterExitKey.Value}] exit");

        float lines = sb.ToString().Split('\n').Length;
        GUI.Box(new Rect(10f, 10f, 620f, 10f + lines * 20f), sb.ToString());
    }

    // ---------- helpers ----------

    float hintUntil;

    void Hint(string msg)
    {
        bool repeat = msg == lastHint && Time.unscaledTime < hintUntil;
        lastHint = msg;
        hintUntil = Time.unscaledTime + 4f;
        if (!repeat) Plugin.L.LogInfo(msg);
    }

    static float Signed(float deg) => deg > 180f ? deg - 360f : deg;

    static bool Alive(UnityEngine.Object o) => o != null && !o.WasCollected;
}
