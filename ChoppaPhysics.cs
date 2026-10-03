using UnityEngine;

namespace BigChoppa;

// Big Walk's layer collision matrix has nothing colliding with its "World" layer; the game opts things in
// per-collider instead. Force our colliders to include every layer and win any include/exclude disagreement.
internal static class ChoppaPhysics
{
    public static void CollideWithEverything(GameObject root)
    {
        LayerMask all = ~0;
        LayerMask none = 0;
        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.includeLayers = all;
            rb.excludeLayers = none;
        }
        foreach (var c in root.GetComponentsInChildren<Collider>(true))
        {
            c.includeLayers = all;
            c.excludeLayers = none;
            c.layerOverridePriority = 1000;
        }
    }

    public static string Describe(Rigidbody rb)
    {
        if (rb == null) return "<no rigidbody>";
        var sb = new System.Text.StringBuilder($"rb '{rb.name}' layer {rb.gameObject.layer} ({LayerMask.LayerToName(rb.gameObject.layer)}) include={rb.includeLayers.value:X8} exclude={rb.excludeLayers.value:X8} kinematic={rb.isKinematic}");
        foreach (var c in rb.GetComponentsInChildren<Collider>(true))
        {
            if (c.attachedRigidbody != rb) continue;
            sb.Append($"\n    collider '{c.name}' {c.GetIl2CppType().Name} layer {c.gameObject.layer} include={c.includeLayers.value:X8} exclude={c.excludeLayers.value:X8} prio={c.layerOverridePriority} trigger={c.isTrigger} enabled={c.enabled}");
        }
        return sb.ToString();
    }
}
