using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LobbyNetworking;
using UnityEngine;

namespace BigChoppa;

// The Little Bird's outside bench spots. Each is one of the game's own PlayerPoses (what chairlift and train seats
// are), so you aim at it and use it like any seat in the game, and the game syncs who sits there. Not mod seats:
// nobody needs the pilot's permission, and you can sit on a parked choppa. Poses are addressed by ticket like
// the pockets; the ticket comes from the choppa id so every PC maps it to its own copy.
internal sealed class ChoppaBenches
{
    public const int MaxSpots = 4;
    const int TicketOffset = ChoppaPockets.Count; // after the pockets in the choppa's ticket block

    // Copied from one of the game's seat poses the first time we need it.
    static bool templateSearched;
    static PlayerPose template;
    static int castLayer = -1;
    static bool castTrigger;

    Helicopter heli;
    readonly List<PlayerPose> poses = new();
    readonly List<PlayerCharacter> seen = new();

    public int RiderCount
    {
        get
        {
            int n = 0;
            foreach (var p in poses) if (p != null && p.occupant != null) n++;
            return n;
        }
    }

    public bool Owns(PlayerPose pose)
    {
        if (pose == null) return false;
        foreach (var p in poses) if (p != null && p.Pointer == pose.Pointer) return true;
        return false;
    }

    // spots: where the rider's bottom goes, facing the way they look.
    public static ChoppaBenches Build(Transform model, Helicopter heli, Transform[] spots)
    {
        var b = new ChoppaBenches { heli = heli };
        for (int i = 0; i < spots.Length && i < MaxSpots; i++)
        {
            // Built inactive so the pose wakes up with our ticket already set (see Activate).
            var go = new GameObject($"BenchSpot{i}");
            go.SetActive(false);
            go.transform.SetParent(model, false);
            go.transform.localPosition = spots[i].localPosition;
            go.transform.localRotation = spots[i].localRotation;
            // What the crosshair hits: the stretch of bench under the rider.
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.15f, 0f);
            box.size = new Vector3(0.45f, 0.35f, 0.4f);
            b.poses.Add(go.AddComponent<PlayerPose>());
            b.seen.Add(null);
        }
        return b;
    }

    // Called once the choppa's layers and physics are set up (they'd overwrite the spots' layer otherwise).
    public void Activate()
    {
        FindTemplate();
        var office = TicketOffice.instance;
        for (int i = 0; i < poses.Count; i++)
        {
            var pose = poses[i];
            ushort ticket = ChoppaPockets.Ticket(heli.Id, TicketOffset + i);
            try
            {
                if (template == null || !ChoppaPockets.FreeTicket(office, ticket))
                {
                    Plugin.L.LogWarning(template == null
                        ? "Bench spots: no game seat to copy; benches disabled."
                        : $"Bench spot {i} of choppa {heli.Id:X8}: ticket {ticket} is already used by the game; spot disabled.");
                    UnityEngine.Object.Destroy(pose.gameObject);
                    poses[i] = null;
                    continue;
                }
                var go = pose.gameObject;
                if (castLayer >= 0) go.layer = castLayer;
                go.GetComponent<BoxCollider>().isTrigger = castTrigger;

                CopyFrom(template, pose);
                pose._ticket_k__BackingField = ticket;
                pose.shellReference = new SeaShell.ShellReference(ticket);

                var target = go.AddComponent<CastableTarget>();
                var outcomes = new Il2CppReferenceArray<CastableOutcome>(1);
                outcomes[0] = new CastableOutcome { playerPose = pose };
                target.outcomes = outcomes;

                go.SetActive(true);
                if (pose.ticket != ticket) pose._ticket_k__BackingField = ticket; // in case OnEnable assigned its own
                if (!office.tickets.ContainsKey(ticket)) TicketOffice.AddTicket(new ITicketed(pose.Pointer));
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"Bench spot {i} setup failed: {e}");
                if (pose != null) UnityEngine.Object.Destroy(pose.gameObject);
                poses[i] = null;
            }
        }
    }

    static void CopyFrom(PlayerPose t, PlayerPose p)
    {
        p.animatorPoseId = t.animatorPoseId;
        p.noCrouching = t.noCrouching;
        p.noHolding = t.noHolding;
        p.allowSitting = t.allowSitting;
        p.hasFootsteps = t.hasFootsteps;
        p.noPlacingOthers = t.noPlacingOthers;
        p.allowKicking = t.allowKicking;
        p.useFullRetractions = t.useFullRetractions;
        p.leaveWithUseAction = t.leaveWithUseAction;
        p.leaveWithJump = t.leaveWithJump;
        p.eyeMood = t.eyeMood;
        p.hasDifferentRightEye = t.hasDifferentRightEye;
        p.seperateRightEye = t.seperateRightEye;
        p.cameraOffset = t.cameraOffset;
        p.cameraOffsetCrouching = t.cameraOffsetCrouching;
        p.noCameraDampening = t.noCameraDampening;
        p.defaultPropLimits = t.defaultPropLimits;
        p.taggedPropLimits = t.taggedPropLimits;
        p.localBumOffset = t.localBumOffset;
        p.localBumOffsetSitting = t.localBumOffsetSitting;
        p.hasCustomColliderState = t.hasCustomColliderState;
        p.colliderState = t.colliderState;
        p.hasCustomCrouchingColliderState = t.hasCustomCrouchingColliderState;
        p.crouchingColliderState = t.crouchingColliderState;
        p.hasCustomSittingColliderState = t.hasCustomSittingColliderState;
        p.sittingColliderState = t.sittingColliderState;
        p.audioIdentifier = t.audioIdentifier;
        p.enterSound = t.enterSound;
        p.exitSound = t.exitSound;
        p.slideSound = t.slideSound;
        p.dropSound = t.dropSound;
        p.legWiggleSound = t.legWiggleSound;
        p.idleSound = t.idleSound;
        // A choppa banks and flips; riders hang on regardless, and keep their pockets.
        p.leaveIfNotUpright = false;
        p.entryIsBlocked = false;
        p.emptyAllPockets = false;
        p.emptyNonBlindfoldPockets = false;
        p.lockPockets = false;
    }

    public void Tick()
    {
        for (int i = 0; i < poses.Count; i++)
        {
            var pose = poses[i];
            if (pose == null) continue;
            var who = pose.occupant;
            if (who == seen[i]) continue;
            // A rider's legs hang right by the skids; touching our own colliders would shove the choppa around.
            if (seen[i] != null) IgnoreCollisions(seen[i], false);
            if (who != null) IgnoreCollisions(who, true);
            seen[i] = who;
            Plugin.Verbose($"Choppa {heli.Id:X8} bench spot {i}: {(who != null ? $"player {who.netId}" : "empty")}");
        }
    }

    void IgnoreCollisions(PlayerCharacter pc, bool ignore)
    {
        if (pc == null || heli == null) return;
        var playerCols = pc.GetComponentsInChildren<Collider>(true);
        foreach (var hc in heli.GetComponentsInChildren<Collider>(true))
        {
            if (hc == null) continue;
            foreach (var c in playerCols)
                if (c != null) Physics.IgnoreCollision(hc, c, ignore);
        }
    }

    // Before the choppa goes away: the local player hops off (everyone does their own), tickets are freed.
    public void Release(PlayerCharacter local)
    {
        foreach (var pose in poses)
        {
            if (pose == null) continue;
            try
            {
                if (local != null && pose.occupant != null && pose.occupant.Pointer == local.Pointer) local.poser.ExitPose(null);
            }
            catch (Exception e) { Plugin.L.LogWarning($"Leaving bench spot: {e.Message}"); }
        }
        for (int i = 0; i < seen.Count; i++)
        {
            if (seen[i] != null) IgnoreCollisions(seen[i], false);
            seen[i] = null;
        }
        var office = TicketOffice.instance;
        foreach (var pose in poses)
            if (pose != null && office != null && office.tickets.ContainsKey(pose.ticket))
                office.tickets.Remove(pose.ticket);
    }

    // Big Walk's chairlift and train seats are PlayerPoses; copy the first one that looks like a seat.
    static void FindTemplate()
    {
        if (templateSearched) return;
        templateSearched = true;
        try
        {
            var all = Resources.FindObjectsOfTypeAll<PlayerPose>();
            PlayerPose best = null;
            int bestRank = int.MaxValue;
            string[] keywords = { "chair", "lift", "train", "seat", "bench", "sit" };
            foreach (var p in all)
            {
                if (p == null || p.isCarryPoseOfCharacter != null) continue;
                string path = Path(p.transform).ToLowerInvariant();
                int rank = Array.FindIndex(keywords, k => path.Contains(k));
                if (rank < 0) rank = keywords.Length;
#if DEVBUILD
                Plugin.L.LogInfo($"Bench spots (dev): pose '{Path(p.transform)}' anim {p.animatorPoseId}, sitting {p.allowSitting}, jump {p.leaveWithJump}, use {p.leaveWithUseAction}, upright {p.leaveIfNotUpright}, bum {p.localBumOffset}/{p.localBumOffsetSitting}, cam {p.cameraOffset}.");
#endif
                if (rank < bestRank) { bestRank = rank; best = p; }
            }
            if (best == null || bestRank >= keywords.Length)
            {
                Plugin.L.LogWarning($"Bench spots: none of the game's {all.Length} poses looks like a seat.");
                if (best == null) return;
            }
            template = best;

            foreach (var ct in Resources.FindObjectsOfTypeAll<CastableTarget>())
            {
                var outs = ct.outcomes;
                if (outs == null) continue;
                bool match = false;
                for (int i = 0; i < outs.Length && !match; i++) match = outs[i]?.playerPose != null && outs[i].playerPose.Pointer == best.Pointer;
                if (!match) continue;
                var col = ct.GetComponent<Collider>() ?? ct.GetComponentInChildren<Collider>(true);
                castLayer = col != null ? col.gameObject.layer : ct.gameObject.layer;
                castTrigger = col != null && col.isTrigger;
                break;
            }
            Plugin.L.LogInfo($"Bench spots: copying seat '{Path(best.transform)}' (anim {best.animatorPoseId}); crosshair layer {castLayer}, trigger {castTrigger}.");
        }
        catch (Exception e) { Plugin.L.LogWarning($"Bench spots: template search failed: {e}"); }
    }

    static string Path(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
