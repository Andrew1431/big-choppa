using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigChoppa;

// Persistent controller: finds the local player, spawns their choppa, seats them and drives the camera.
// Everything here is local-only; other players just see your character flying around in a sitting pose.
public class ChoppaManager : MonoBehaviour
{
    public ChoppaManager(IntPtr ptr) : base(ptr) { }

    enum CamMode { Chase, Cockpit }

    PlayerCharacter local;
    float nextPlayerSearch;
    Helicopter heli;

    bool seated;
    Rigidbody playerBody;
    Transform playerRoot;
    Collider[] playerColliders = Array.Empty<Collider>();
    bool savedKinematic, savedDetect, savedBypassUpdate, savedBypassFixed, savedBypassLate;
    RigidbodyInterpolation savedInterp;

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
        Helicopter.Crashed += OnCrash;
    }

    void OnDestroy() => Helicopter.Crashed -= OnCrash;

    void Update()
    {
        try { Tick(); }
        catch (Exception e) { Plugin.L.LogError($"ChoppaManager.Update: {e}"); }
    }

    void Tick()
    {
        ChoppaInput.Tick();
        if (!FindLocalPlayer())
        {
            if (seated) ForceUnseat("local player disappeared");
            return;
        }
        if (heli != null && !Alive(heli)) { heli = null; if (seated) ForceUnseat("choppa destroyed"); }

        bool inputAllowed = !ChoppaConfig.RequireCursorLock.Value || Cursor.lockState == CursorLockMode.Locked;

        if (inputAllowed && ChoppaInput.Pressed(ChoppaConfig.HudKey.Value)) showHud = !showHud;
        if (inputAllowed && ChoppaInput.Pressed(ChoppaConfig.SpawnKey.Value) && !seated) Spawn();
        if (inputAllowed && ChoppaInput.Pressed(ChoppaConfig.ResetKey.Value) && heli != null) heli.ResetUpright();
        if (inputAllowed && ChoppaInput.Pressed(ChoppaConfig.EnterExitKey.Value))
        {
            if (seated) Unseat();
            else TryBoard();
        }

        CheckSpawnSettled();
        if (seated) FlyInput(inputAllowed);
    }

    void FixedUpdate()
    {
        if (seated) PinPlayer();
    }

    void LateUpdate()
    {
        if (!seated) return;
        try
        {
            PinPlayer();
            UpdateCamera();
        }
        catch (Exception e) { Plugin.L.LogError($"ChoppaManager.LateUpdate: {e}"); }
    }

    // ---------- player ----------

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
            Plugin.L.LogInfo($"Found local player '{pc.gameObject.name}'.");
            return true;
        }
        return false;
    }

    // ---------- spawn / board ----------

    void Spawn()
    {
        var root = local.transform;
        var fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        float s = ChoppaConfig.HeliScale.Value;

        Vector3 pos = root.position + fwd * (6f * s);
        Collider ground = GroundBelow(pos + Vector3.up * 30f, 80f, out var groundPoint);
        if (ground != null) pos = groundPoint + Vector3.up * 0.15f;
        Plugin.Verbose($"Spawn ground ray hit: {(ground != null ? $"'{Path(ground.transform)}' layer {ground.gameObject.layer} ({LayerMask.LayerToName(ground.gameObject.layer)}) {ground.GetIl2CppType().Name}" : "<nothing>")}");

        // Side-on to the player so they walk up to the pilot's door.
        var rot = Quaternion.LookRotation(Vector3.Cross(Vector3.up, fwd), Vector3.up);

        if (heli == null)
        {
            int layer = PickLayer(ground);
            heli = Helicopter.Build(pos, rot, layer, local.gameObject.scene);
            Plugin.L.LogInfo($"Spawned choppa at {pos} on layer {layer} ({LayerMask.LayerToName(layer)}) in scene '{heli.gameObject.scene.name}' (active scene '{SceneManager.GetActiveScene().name}').");
            LogPhysicsDiagnostics(ground);
        }
        else
        {
            heli.Body.linearVelocity = Vector3.zero;
            heli.Body.angularVelocity = Vector3.zero;
            heli.Body.position = pos;
            heli.Body.rotation = rot;
            heli.transform.SetPositionAndRotation(pos, rot);
            heli.ClearImpactHistory();
            Plugin.L.LogInfo($"Moved choppa to {pos}.");
        }
        spawnY = pos.y;
        spawnCheckAt = Time.time + 2f;
    }

    float spawnY, spawnCheckAt = -1f;

    void CheckSpawnSettled()
    {
        if (spawnCheckAt < 0f || Time.time < spawnCheckAt || heli == null) return;
        spawnCheckAt = -1f;
        float drop = spawnY - heli.transform.position.y;
        if (drop > 1f) Plugin.L.LogWarning($"Choppa fell {drop:0.0}m in 2s after spawning - still not colliding with the ground.");
        else Plugin.L.LogInfo($"Choppa settled (moved {drop:0.00}m vertically). Grounded={heli.Grounded}");
    }

    void LogPhysicsDiagnostics(Collider ground)
    {
        if (!ChoppaConfig.VerboseLogging.Value) return;
        try
        {
            if (ground != null)
                Plugin.L.LogInfo($"Ground collider '{ground.name}' include={ground.includeLayers.value:X8} exclude={ground.excludeLayers.value:X8} prio={ground.layerOverridePriority} rb={(ground.attachedRigidbody != null ? ground.attachedRigidbody.name : "none")}");
            Plugin.L.LogInfo($"Player: {ChoppaPhysics.Describe(local.rb)}");
            int shown = 0;
            foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                if (rb == null || rb.isKinematic || rb.GetComponentInParent<PlayerCharacter>() != null) continue;
                if (heli != null && rb.transform.IsChildOf(heli.transform)) continue;
                Plugin.L.LogInfo($"Game prop: {ChoppaPhysics.Describe(rb)}");
                if (++shown >= 2) break;
            }
            Plugin.L.LogInfo($"Choppa: {ChoppaPhysics.Describe(heli.Body)}");
        }
        catch (Exception e) { Plugin.L.LogWarning($"Physics diagnostics failed: {e.Message}"); }
    }

    Collider GroundBelow(Vector3 origin, float distance, out Vector3 point)
    {
        point = default;
        Collider best = null;
        float bestDist = float.MaxValue;
        foreach (var h in Physics.RaycastAll(origin, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            var c = h.collider;
            if (c == null || h.distance >= bestDist) continue;
            if (c.transform.IsChildOf(local.transform)) continue;
            if (heli != null && c.transform.IsChildOf(heli.transform)) continue;
            best = c;
            bestDist = h.distance;
            point = h.point;
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
            Plugin.Verbose($"Player ground collider: {(stoodOn != null ? $"'{Path(stoodOn.transform)}' layer {groundLayer} ({LayerMask.LayerToName(groundLayer)})" : "<none>")}");
        }
        catch (Exception e) { Plugin.Verbose($"PlayerGround lookup failed: {e.Message}"); }
        if (groundLayer < 0 && rayGround != null) groundLayer = rayGround.gameObject.layer;
        if (groundLayer < 0) groundLayer = 0;

        var sb = new StringBuilder($"Layers colliding with ground layer {groundLayer}:");
        for (int l = 0; l < 32; l++)
            if (!Physics.GetIgnoreLayerCollision(l, groundLayer) && LayerMask.LayerToName(l).Length > 0)
                sb.Append($" {l}({LayerMask.LayerToName(l)})");
        Plugin.Verbose(sb.ToString());

        var counts = new int[32];
        foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (rb == null || rb.isKinematic || rb.transform.IsChildOf(local.transform)) continue;
            if (rb.GetComponentInParent<PlayerCharacter>() != null) continue;
            int l = rb.gameObject.layer;
            if (!Physics.GetIgnoreLayerCollision(l, groundLayer)) counts[l]++;
        }
        int bestProp = -1;
        for (int l = 0; l < 32; l++)
            if (counts[l] > 0 && (bestProp < 0 || counts[l] > counts[bestProp])) bestProp = l;
        if (bestProp >= 0)
        {
            Plugin.Verbose($"Using physics-prop layer {bestProp} ({LayerMask.LayerToName(bestProp)}), {counts[bestProp]} props on it.");
            return bestProp;
        }

        int playerLayer = (local.rb != null ? local.rb.gameObject : local.gameObject).layer;
        if (!Physics.GetIgnoreLayerCollision(playerLayer, groundLayer))
        {
            Plugin.Verbose($"No props found; using player layer {playerLayer} ({LayerMask.LayerToName(playerLayer)}).");
            return playerLayer;
        }
        for (int l = 0; l < 32; l++)
            if (!Physics.GetIgnoreLayerCollision(l, groundLayer)) return l;
        return 0;
    }

    // Distance from the player to the nearest solid part, so boarding works from any side of a long airframe.
    float DistanceToHeli()
    {
        Vector3 p = local.transform.position;
        float best = Vector3.Distance(p, heli.transform.position);
        foreach (var c in heli.GetComponentsInChildren<Collider>())
            best = Mathf.Min(best, Vector3.Distance(p, c.ClosestPoint(p)));
        return best;
    }

    void TryBoard()
    {
        if (heli == null) { Hint($"No choppa yet - press {ChoppaConfig.SpawnKey.Value} to spawn one."); return; }
        float dist = DistanceToHeli();
        float max = ChoppaConfig.EnterDistance.Value * ChoppaConfig.HeliScale.Value;
        if (dist > max) { Hint($"Too far from the choppa ({dist:0.0}m, need {max:0.0}m)."); return; }
        Seat();
    }

    void Seat()
    {
        playerBody = local.rb;
        playerRoot = playerBody != null ? playerBody.transform : local.transform;
        Plugin.Verbose($"Seating: pc='{Path(local.transform)}' rbObj='{(playerBody != null ? Path(playerBody.transform) : "<none>")}' kernal='{(local.kernal != null ? Path(local.kernal) : "<none>")}'");

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
        foreach (var hc in heli.GetComponentsInChildren<Collider>(true))
        foreach (var pc in playerColliders)
            Physics.IgnoreCollision(hc, pc, true);

        if (ChoppaConfig.SitWhileFlying.Value) SetSitting(true);

        cam = null;
        try { cam = local.cameraMinder?.playerCameraReferences?.playerCamera; } catch (Exception e) { Plugin.Verbose($"cameraMinder lookup failed: {e.Message}"); }
        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            eyeOffset = playerRoot.InverseTransformPoint(cam.transform.position);
            camParent = cam.transform.parent;
            camLocalPos = cam.transform.localPosition;
            camLocalRot = cam.transform.localRotation;
            Plugin.Verbose($"Camera '{Path(cam.transform)}' eyeOffset={eyeOffset}");
            cam.transform.SetParent(null, true);
        }
        else Plugin.L.LogWarning("No camera found to take over; flying with whatever the game does.");

        seated = true;
        heli.EngineOn = true;
        heli.Collective = 0f;
        smoothedCyclic = Vector2.zero;
        lookYaw = lookPitch = 0f;
        PinPlayer();
        Plugin.L.LogInfo("Boarded choppa. Engine starting.");
    }

    void Unseat()
    {
        Vector3 exitPos = heli.ExitPoint.position;
        Vector3 vel = heli.Body.linearVelocity;
        Quaternion exitRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(heli.transform.forward, Vector3.up).normalized + Vector3.forward * 1e-4f, Vector3.up);
        heli.EngineOn = false;
        heli.CommandedRates = Vector3.zero;
        Restore(exitPos, exitRot, vel);
        Plugin.L.LogInfo("Left choppa.");
    }

    void ForceUnseat(string why)
    {
        Plugin.L.LogWarning($"Forced out of choppa: {why}");
        try
        {
            if (local != null && Alive(local)) Restore(playerRoot.position, playerRoot.rotation, Vector3.zero);
            else RestoreCamera();
        }
        catch (Exception e) { Plugin.L.LogError(e); }
        seated = false;
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

        local.bypassUpdate = savedBypassUpdate;
        local.bypassFixedUpdate = savedBypassFixed;
        local.bypassLateUpdate = savedBypassLate;

        if (heli != null)
            foreach (var hc in heli.GetComponentsInChildren<Collider>(true))
            foreach (var pc in playerColliders)
                if (hc != null && pc != null) Physics.IgnoreCollision(hc, pc, false);

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
        if (heli == null || playerRoot == null) return;
        var seat = heli.PilotSeat;
        playerRoot.SetPositionAndRotation(seat.position, seat.rotation);
        if (playerBody != null)
        {
            playerBody.position = seat.position;
            playerBody.rotation = seat.rotation;
        }
    }

    // ---------- flying ----------

    void FlyInput(bool inputAllowed)
    {
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
        if (cam == null || !Alive(cam)) return;
        var ht = heli.transform;
        if (camMode == CamMode.Cockpit)
        {
            Vector3 eye = heli.PilotSeat.TransformPoint(eyeOffset) + ht.up * ChoppaConfig.CockpitEyeHeight.Value;
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

    // ---------- crashing ----------

    void OnCrash(Helicopter crashed, float deltaV)
    {
        if (crashed != heli) { ChoppaCrash.Break(crashed); return; }
        try
        {
            Vector3 crashPos = heli.transform.position;
            if (seated)
            {
                Vector3 seatPos = heli.PilotSeat.position + Vector3.up * (1.0f * ChoppaConfig.HeliScale.Value);
                Vector3 fling = heli.ImpactVelocity * 0.4f + Vector3.up * 9f + UnityEngine.Random.insideUnitSphere * 3f;
                var yaw = Quaternion.Euler(0f, heli.transform.eulerAngles.y, 0f);
                heli.EngineOn = false;
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

        ChoppaCrash.Break(heli);
        heli = null;
        Hint($"Choppa destroyed! Press {ChoppaConfig.SpawnKey.Value} for a new one.");
    }

    void Stun(string why)
    {
        try
        {
            var faller = local.faller;
            if (faller == null) { Plugin.L.LogWarning("No PlayerFaller on local player; can't stun."); return; }
            faller.TriggerFall();
            Plugin.Verbose($"Stun ({why}): TriggerFall() -> isDazed={faller.isDazed} isInDanger={faller.isInDanger}");
        }
        catch (Exception e) { Plugin.L.LogWarning($"Stun failed: {e.Message}"); }
    }

    // ---------- HUD ----------

    void OnGUI()
    {
        if (!showHud) return;
        try
        {
            if (seated && heli != null) DrawFlightHud();
            else if (heli != null && local != null && Alive(local))
            {
                float dist = DistanceToHeli();
                if (dist <= ChoppaConfig.EnterDistance.Value * ChoppaConfig.HeliScale.Value)
                    GUI.Label(new Rect(Screen.width / 2f - 120f, Screen.height * 0.65f, 240f, 24f), $"[{ChoppaConfig.EnterExitKey.Value}] Board choppa");
            }
        }
        catch (Exception e) { Plugin.L.LogError($"OnGUI: {e.Message}"); showHud = false; }
    }

    void DrawFlightHud()
    {
        var b = heli.Body;
        float alt = -1f;
        if (Physics.Raycast(heli.transform.position, Vector3.down, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
            alt = hit.distance;
        Vector3 v = b.linearVelocity;
        float hover = heli.HoverCollective;

        var sb = new StringBuilder();
        sb.AppendLine($"BIG CHOPPA   rotor {heli.RotorSpin * 100f:0}%");
        sb.AppendLine($"Collective {heli.Collective * 100f:0}%  (hover ~{hover * 100f:0}%)  {(heli.Grounded ? "ON GROUND" : "airborne")}");
        sb.AppendLine($"Alt {(alt < 0 ? "--" : alt.ToString("0.0"))} m   Spd {new Vector2(v.x, v.z).magnitude * 3.6f:0} km/h   VS {v.y:+0.0;-0.0} m/s");
        var e = heli.transform.eulerAngles;
        sb.AppendLine($"Pitch {Signed(e.x):0}°  Roll {Signed(e.z):0}°  Hdg {e.y:000}°   Cam {camMode}");
        if (ChoppaConfig.VerboseLogging.Value)
        {
            Vector2 m = ChoppaInput.MouseDelta();
            sb.AppendLine($"input={ChoppaInput.ActiveBackend} mouse=({m.x:0.00},{m.y:0.00}) cmd={heli.CommandedRates.x:0},{heli.CommandedRates.y:0},{heli.CommandedRates.z:0} lock={Cursor.lockState}");
        }
        sb.Append($"[{ChoppaConfig.CollectiveUpKey.Value}/{ChoppaConfig.CollectiveDownKey.Value}] collective  [{ChoppaConfig.PedalLeftKey.Value}/{ChoppaConfig.PedalRightKey.Value}] pedals  [Mouse] cyclic  " +
                  $"[{ChoppaConfig.FreeLookKey.Value}] look  [{ChoppaConfig.CameraKey.Value}] camera  [{ChoppaConfig.ResetKey.Value}] upright  [{ChoppaConfig.EnterExitKey.Value}] exit");

        GUI.Box(new Rect(10f, 10f, 620f, ChoppaConfig.VerboseLogging.Value ? 130f : 112f), sb.ToString());
    }

    // ---------- helpers ----------

    void Hint(string msg)
    {
        if (msg == lastHint) return;
        lastHint = msg;
        Plugin.L.LogInfo(msg);
    }

    static float Signed(float deg) => deg > 180f ? deg - 360f : deg;

    static bool Alive(UnityEngine.Object o) => o != null && !o.WasCollected;

    static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
