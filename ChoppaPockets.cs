using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LobbyNetworking;
using Mirror;
using UnityEngine;

namespace BigChoppa;

// Six side pockets (three a side) that hold the same items a backpack does. Each pocket is one of the game's own
// PropHomes, so stowing and grabbing use the game's normal place/pick-up and its networking: the host pins the item
// and everyone else resolves the home by ticket. The ticket comes from the choppa id, so every PC maps it to its own
// copy of the same pocket. The game doesn't count pocketed items as held, so they aren't saved.
internal sealed class ChoppaPockets
{
    public const int Count = 6;
    const int TicketBase = 60000, Slots = 900; // tickets 60000..65399
    public static int Slot(uint id) => (int)(id % Slots);
    static ushort Ticket(uint id, int i) => (ushort)(TicketBase + Slot(id) * Count + i);

    // Copied from one of the game's backpack pockets the first time we need it.
    static bool templateSearched;
    static PropGroup acceptGroup = PropGroup.GoesInBackpack;
    static int castLayer = -1;
    static bool castTrigger;
    static AudioAsset placeSound, removeSound;

    Helicopter heli;
    readonly PropHome[] homes = new PropHome[Count];
    readonly Prop[] seen = new Prop[Count];
    float rebindUntil, nextRebind;

    public static ChoppaPockets Build(Transform model, Helicopter heli)
    {
        var p = new ChoppaPockets { heli = heli };
        var pouch = new Color(0.2f, 0.55f, 0.95f);
        var flap = new Color(0.12f, 0.35f, 0.7f);
        int i = 0;
        foreach (float side in new[] { -1f, 1f })
        foreach (float z in new[] { 0.95f, 0.25f, -0.45f })
        {
            HeliModel.Part(model, PrimitiveType.Cube, "Pocket", new(side * 1.05f, 0.8f, z), Vector3.zero, new(0.12f, 0.45f, 0.5f), pouch, false);
            HeliModel.Part(model, PrimitiveType.Cube, "PocketFlap", new(side * 1.09f, 1.0f, z), new(0f, 0f, side * -12f), new(0.1f, 0.12f, 0.54f), flap, false);

            // Built inactive so PropHome wakes up with our ticket already set (see Activate).
            var go = new GameObject($"PocketHome{i}");
            go.SetActive(false);
            go.transform.SetParent(model, false);
            go.transform.localPosition = new(side * 1.32f, 0.8f, z); // where the item hangs, just outside the pouch
            var box = go.AddComponent<BoxCollider>();
            box.center = new(side * -0.27f, 0f, 0f); // the pouch itself, so the hanging item stays grabbable
            box.size = new(0.2f, 0.5f, 0.55f);
            p.homes[i++] = go.AddComponent<PropHome>();
        }
        return p;
    }

    // Called once the choppa's layers and physics are set up (they'd overwrite the pockets' layer otherwise).
    public void Activate()
    {
        FindTemplate();
        var office = TicketOffice.instance;
        for (int i = 0; i < Count; i++)
        {
            var home = homes[i];
            ushort ticket = Ticket(heli.Id, i);
            try
            {
                if (!FreeTicket(office, ticket))
                {
                    Plugin.L.LogWarning($"Pocket {i} of choppa {heli.Id:X8}: ticket {ticket} is already used by the game; pocket disabled.");
                    UnityEngine.Object.Destroy(home.gameObject);
                    homes[i] = null;
                    continue;
                }
                var go = home.gameObject;
                if (castLayer >= 0) go.layer = castLayer;
                var box = go.GetComponent<BoxCollider>();
                box.isTrigger = castTrigger;

                home.pinGroup = acceptGroup;
                home.isInventory = false;
                home.saveableHomeName = SaveableHomeName.notSavable;
                if (placeSound != null) home.propPlaceSoundOverride = placeSound;
                if (removeSound != null) home.propRemoveSoundOverride = removeSound;
                home._ticket_k__BackingField = ticket;
                home.shellReference = new SeaShell.ShellReference(ticket);

                // What the crosshair looks for: "holding something that fits? place it here".
                var target = go.AddComponent<CastableTarget>();
                var outcome = new CastableOutcome { propHome = home };
                var outcomes = new Il2CppReferenceArray<CastableOutcome>(1);
                outcomes[0] = outcome;
                target.outcomes = outcomes;

                go.SetActive(true);
                if (home.ticket != ticket) home._ticket_k__BackingField = ticket; // in case Awake assigned its own
                if (!office.tickets.ContainsKey(ticket)) TicketOffice.AddTicket(new ITicketed(home.Pointer));
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"Pocket {i} setup failed: {e}");
                if (home != null) UnityEngine.Object.Destroy(home.gameObject);
                homes[i] = null;
            }
        }
        rebindUntil = Time.time + 15f;
    }

    // A ticket left behind by a destroyed choppa is fine to reuse; one the game uses is not.
    static bool FreeTicket(TicketOffice office, ushort ticket)
    {
        if (office == null || !office.tickets.ContainsKey(ticket)) return true;
        var existing = office.tickets[ticket];
        var home = existing?.TryCast<PropHome>();
        if (existing != null && home != null) return false;
        office.tickets.Remove(ticket);
        return true;
    }

    public void Tick()
    {
        for (int i = 0; i < Count; i++)
        {
            var home = homes[i];
            if (home == null) continue;
            var prop = home.pinnedProp;
            if (prop == seen[i]) continue;
            if (seen[i] != null) IgnoreCollisions(seen[i], false);
            if (prop != null) IgnoreCollisions(prop, true);
            seen[i] = prop;
            Plugin.Verbose($"Choppa {heli.Id:X8} pocket {i}: {(prop != null ? prop.name : "empty")}");
        }

        // Someone joining late learns an item's pocket before our choppa exists, so put it back once it does.
        if (Time.time < rebindUntil && Time.time >= nextRebind)
        {
            nextRebind = Time.time + 1f;
            Rebind();
        }
    }

    void Rebind()
    {
        var all = Prop.allProps;
        if (all == null) return;
        ushort first = Ticket(heli.Id, 0);
        for (int n = 0; n < all.Count; n++)
        {
            var prop = all[n];
            if (prop == null) continue;
            int i = prop.propHomeShellReference.ticket - first;
            if (i < 0 || i >= Count || homes[i] == null || prop.currentHome == homes[i]) continue;
            try
            {
                prop.LocallySetPinned(homes[i], false);
                Plugin.L.LogInfo($"Put {prop.name} back in pocket {i} of choppa {heli.Id:X8}.");
            }
            catch (Exception e) { Plugin.L.LogWarning($"Re-pocketing {prop.name}: {e.Message}"); }
        }
    }

    // Drop everything before the choppa goes away, or the items would be destroyed along with it.
    public void ReleaseAll(Vector3 velocity)
    {
        for (int i = 0; i < Count; i++)
        {
            var home = homes[i];
            if (home == null) continue;
            var prop = home.pinnedProp;
            if (prop == null) continue;
            try
            {
                IgnoreCollisions(prop, false);
                if (NetworkServer.active) prop.ServerSetUnpinned();
                else prop.LocalUnpin(home); // the host's unpin arrives shortly; just don't lose the item meanwhile
                if (prop.transform.IsChildOf(heli.transform)) prop.transform.SetParent(prop.originalParent, true);
                var rb = prop.rb;
                if (NetworkServer.active && rb != null && !rb.isKinematic)
                    rb.linearVelocity = velocity * 0.5f + UnityEngine.Random.insideUnitSphere * 2f + Vector3.up * 3f;
            }
            catch (Exception e) { Plugin.L.LogWarning($"Emptying pocket {i}: {e.Message}"); }
            seen[i] = null;
        }
        var office = TicketOffice.instance;
        foreach (var home in homes)
            if (home != null && office != null && office.tickets.ContainsKey(home.ticket))
                office.tickets.Remove(home.ticket);
    }

    // Pinned items are kinematic children of our moving body; if they touched the hull they'd shove it around.
    void IgnoreCollisions(Prop prop, bool ignore)
    {
        if (prop == null || heli == null) return;
        var propCols = prop.GetComponentsInChildren<Collider>(true);
        foreach (var hc in heli.GetComponentsInChildren<Collider>(true))
        {
            if (hc == null || hc.attachedRigidbody != heli.Body) continue;
            foreach (var pc in propCols)
                if (pc != null && pc.attachedRigidbody != heli.Body) Physics.IgnoreCollision(hc, pc, ignore);
        }
    }

    // Copy a real backpack pocket's settings (what fits, crosshair layer, sounds) and log what we found.
    static void FindTemplate()
    {
        if (templateSearched) return;
        templateSearched = true;
        try
        {
            var office = TicketOffice.instance;
            int maxTicket = 0;
            if (office != null)
                foreach (var kv in office.tickets) if (kv.Key < TicketBase && kv.Key > maxTicket) maxTicket = kv.Key;
            Plugin.L.LogInfo($"Pockets: {office?.tickets.Count ?? -1} game tickets, highest {maxTicket} (ours start at {TicketBase}).");

            CastableTarget target = null;
            PropHome template = null;
            foreach (var ct in Resources.FindObjectsOfTypeAll<CastableTarget>())
            {
                var outs = ct.outcomes;
                if (outs == null) continue;
                for (int i = 0; i < outs.Length && template == null; i++)
                {
                    var h = outs[i]?.propHome;
                    if (h != null && h.pinGroup == PropGroup.GoesInBackpack && h.parentCharacter == null) { template = h; target = ct; }
                }
                if (template != null) break;
            }
            if (template == null)
            {
                Plugin.L.LogWarning("Pockets: no backpack pocket found to copy; using defaults (they may not be targetable).");
                return;
            }
            acceptGroup = template.pinGroup;
            placeSound = template.propPlaceSoundOverride;
            removeSound = template.propRemoveSoundOverride;
            var col = target.GetComponent<Collider>() ?? target.GetComponentInChildren<Collider>(true);
            castLayer = col != null ? col.gameObject.layer : target.gameObject.layer;
            castTrigger = col != null && col.isTrigger;
            Plugin.L.LogInfo($"Pockets: copying '{Path(template.transform)}' (group {template.pinGroup}, position group {(template.hasPositionGroup ? template.positionGroup.ToString() : "none")}, inventory {template.isInventory}); " +
                             $"crosshair target '{Path(target.transform)}' layer {castLayer} ({LayerMask.LayerToName(castLayer)}), trigger {castTrigger}, collider {(col != null ? col.GetIl2CppType().Name : "none")}.");
        }
        catch (Exception e) { Plugin.L.LogWarning($"Pockets: template search failed: {e}"); }
    }

    static string Path(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
