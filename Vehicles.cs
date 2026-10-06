using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BigChoppa;

// Which airframe a choppa is. Sent in the Spawn message so every PC builds the same one; never reorder (wire value).
public enum Vehicle : byte { Classic = 0, LittleBird = 1 }

internal static class Vehicles
{
    public static Vehicle Parse(byte b) => Enum.IsDefined(typeof(Vehicle), b) ? (Vehicle)b : Vehicle.Classic;

    public static int SeatCount(Vehicle v) => v == Vehicle.LittleBird ? LittleBirdModel.SeatNames.Length : 3;

    // The Little Bird is modelled at real size; at the default Scale (0.6, tuned for the classic) this makes it
    // 1:1, which also puts its seats at the same height as the classic's.
    public static float Scale(Vehicle v) => ChoppaConfig.HeliScale.Value * (v == Vehicle.LittleBird ? 1f / 0.6f : 1f);

    public static string Id(Vehicle v) => v == Vehicle.LittleBird ? "little_bird" : "classic";

    public static void Build(Helicopter heli)
    {
        if (heli.Vehicle == Vehicle.LittleBird)
        {
            try { LittleBirdModel.Build(heli, heli.Scale); return; }
            catch (Exception e)
            {
                Plugin.L.LogError($"Building the Little Bird failed, using the classic choppa instead: {e}");
                var model = heli.transform.Find("Model");
                if (model != null) UnityEngine.Object.DestroyImmediate(model.gameObject);
                heli.Scale = Scale(Vehicle.Classic);
            }
        }
        HeliModel.Build(heli, heli.Scale);
    }
}

// The toy MH-6 from model/mh6.blend (exported to Resources/mh6.bin). Empties in the model mark seats, exits, lights,
// pocket hang points and body colliders; see model/PLAN-3.0.0.md for the naming.
internal static class LittleBirdModel
{
    const string Resource = "BigChoppa.mh6.bin";

    // Seat 0 is the pilot. Exits share the suffix.
    public static readonly string[] SeatNames = { "Pilot", "Copilot", "BenchL_Front", "BenchL_Rear", "BenchR_Front", "BenchR_Rear" };

    public static void Build(Helicopter heli, float scale)
    {
        var data = MeshModel.Load(Resource);

        var model = new GameObject("Model").transform;
        model.SetParent(heli.transform, false);
        model.localScale = Vector3.one * scale;

        var parts = data.Instantiate(model, n => n.Name.StartsWith("Col_") || n.Name.StartsWith("Lens_") || n.Name.StartsWith("Light_"));
        Transform Need(string name) => parts.TryGetValue(name, out var t) ? t : throw new Exception($"model has no '{name}'");

        heli.MainRotor = Need("MH6_MainRotor");
        heli.TailRotor = Need("MH6_TailRotor");

        // Seat offsets from the config move you in the seat's own frame (up, forward), so side-facing bench seats
        // slide outward rather than along the bench.
        var offset = new Vector3(0f, ChoppaConfig.SeatHeightOffset.Value, ChoppaConfig.SeatForwardOffset.Value);
        heli.Seats = SeatNames.Select(s =>
        {
            var t = Need("Seat_" + s);
            t.localPosition += t.localRotation * offset;
            return t;
        }).ToArray();
        heli.Exits = SeatNames.Select(s => Need("Exit_" + s)).ToArray();

        foreach (var n in data.Nodes)
            if (n.Name.StartsWith("Col_")) AddCollider(model, n);

        heli.Lights = ChoppaLights.Build(model, heli.Id, scale, Lights(data));
        heli.Pockets = ChoppaPockets.Build(model, heli, Pockets(data));
    }

    // Col_* empties are boxes whose scale is the half extents; "capsule" ones run along their longest side.
    static void AddCollider(Transform model, MeshModel.Node n)
    {
        var go = new GameObject(n.Name);
        go.transform.SetParent(model, false);
        go.transform.localPosition = n.Pos;
        go.transform.localRotation = n.Rot;
        Vector3 size = n.Scale * 2f;
        if (n.String("collider") == "capsule")
        {
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var c = go.AddComponent<CapsuleCollider>();
            c.direction = axis;
            c.height = size[axis];
            c.radius = Mathf.Max(size[(axis + 1) % 3], size[(axis + 2) % 3]) * 0.5f;
        }
        else go.AddComponent<BoxCollider>().size = size;
    }

    static ChoppaLights.Layout Lights(MeshModel data)
    {
        var l = new ChoppaLights.Layout();
        foreach (var n in data.Nodes)
        {
            if (n.Name.StartsWith("Lens_"))
            {
                var kind = n.Name.StartsWith("Lens_Beacon") ? ChoppaLights.LensKind.Beacon
                    : n.Name.StartsWith("Lens_Strobe") ? ChoppaLights.LensKind.Strobe : ChoppaLights.LensKind.Steady;
                l.Lenses.Add(new ChoppaLights.LensSpec(n.Name, n.Pos, n.Float("lens_size", 0.15f), n.Color("lens_color", Color.white), kind));
            }
            else if (n.Name == "Light_Head") l.Headlight = n.Pos;
            else if (n.Name == "Light_Cabin") l.Glow = n.Pos;
            else if (n.Name == "Light_Beacon") l.BeaconGlow = n.Pos;
        }
        return l;
    }

    // Each Pocket_<side><n> hang point pairs with the MH6_Pocket<side><n> pouch mesh, which is what you aim at.
    static ChoppaPockets.PocketSpec[] Pockets(MeshModel data)
    {
        var specs = new List<ChoppaPockets.PocketSpec>();
        foreach (var n in data.Nodes)
        {
            if (!n.Name.StartsWith("Pocket_")) continue;
            var pouch = data.Find("MH6_Pocket" + n.Name.Substring("Pocket_".Length));
            if (pouch == null || !pouch.HasMesh) continue;
            var b = MeshModel.ModelBounds(pouch);
            specs.Add(new ChoppaPockets.PocketSpec(n.Pos, b.center - n.Pos, b.size + Vector3.one * 0.06f));
        }
        return specs.ToArray();
    }
}
