using System.Collections.Generic;
using UnityEngine;

namespace BigChoppa;

// Playground spring-rider helicopter made from primitives. Units are metres before scale; +Z is the nose.
internal static class HeliModel
{
    static readonly Color Red = new(0.93f, 0.22f, 0.20f);
    static readonly Color Yellow = new(1.00f, 0.80f, 0.15f);
    static readonly Color Blue = new(0.20f, 0.55f, 0.95f);
    static readonly Color Glass = new(0.65f, 0.90f, 1.00f);
    static readonly Color White = new(0.97f, 0.97f, 0.97f);
    static readonly Color Black = new(0.05f, 0.05f, 0.05f);
    static readonly Color Grey = new(0.35f, 0.35f, 0.40f);

    public static void Build(Helicopter heli, float scale)
    {
        var root = heli.transform;
        var model = new GameObject("Model").transform;
        model.SetParent(root, false);
        model.localScale = Vector3.one * scale;

        // Solid parts (keep colliders): tub, nose, skids, tail boom.
        Part(model, PrimitiveType.Cube, "Tub", new(0f, 0.95f, 0.2f), Vector3.zero, new(2.0f, 0.9f, 2.6f), Red, true);
        Part(model, PrimitiveType.Sphere, "Nose", new(0f, 1.0f, 1.5f), Vector3.zero, new(2.0f, 1.1f, 1.1f), Yellow, true);
        Part(model, PrimitiveType.Capsule, "TailBoom", new(0f, 1.15f, -2.9f), new(90f, 0f, 0f), new(0.45f, 1.9f, 0.45f), Red, true);
        foreach (float x in new[] { -0.95f, 0.95f })
        {
            Part(model, PrimitiveType.Capsule, "Skid", new(x, 0.12f, 0.2f), new(90f, 0f, 0f), new(0.24f, 1.7f, 0.24f), Grey, true);
            Part(model, PrimitiveType.Cylinder, "Strut", new(x, 0.32f, 1.0f), Vector3.zero, new(0.1f, 0.2f, 0.1f), Grey, false);
            Part(model, PrimitiveType.Cylinder, "Strut", new(x, 0.32f, -0.6f), Vector3.zero, new(0.1f, 0.2f, 0.1f), Grey, false);
        }

        // Seats: pilot up front, a bench behind for (future) passengers.
        Part(model, PrimitiveType.Cube, "PilotSeat", new(0f, 1.55f, 0.1f), Vector3.zero, new(0.9f, 0.3f, 0.7f), Blue, false);
        Part(model, PrimitiveType.Cube, "PilotBack", new(0f, 1.95f, -0.25f), new(-10f, 0f, 0f), new(0.9f, 0.8f, 0.15f), Blue, false);
        Part(model, PrimitiveType.Cube, "Bench", new(0f, 1.55f, -0.75f), Vector3.zero, new(1.8f, 0.3f, 0.6f), Blue, false);
        Part(model, PrimitiveType.Cube, "BenchBack", new(0f, 1.95f, -1.05f), new(-10f, 0f, 0f), new(1.8f, 0.8f, 0.15f), Blue, false);
        Part(model, PrimitiveType.Cube, "Windscreen", new(0f, 1.85f, 1.05f), new(-25f, 0f, 0f), new(1.7f, 0.7f, 0.06f), Glass, false);
        Part(model, PrimitiveType.Cylinder, "Stick", new(0f, 1.7f, 0.55f), new(15f, 0f, 0f), new(0.06f, 0.2f, 0.06f), Black, false);
        Part(model, PrimitiveType.Sphere, "StickKnob", new(0f, 1.9f, 0.6f), Vector3.zero, Vector3.one * 0.14f, Red, false);

        // Roof on four posts, rotor on top.
        foreach (float x in new[] { -0.92f, 0.92f })
        foreach (float z in new[] { 1.3f, -1.05f })
            Part(model, PrimitiveType.Cylinder, "Post", new(x, 2.25f, z), Vector3.zero, new(0.09f, 0.85f, 0.09f), Yellow, false);
        Part(model, PrimitiveType.Cube, "Roof", new(0f, 3.12f, 0.12f), Vector3.zero, new(2.0f, 0.14f, 2.5f), Yellow, false);
        Part(model, PrimitiveType.Cylinder, "Mast", new(0f, 3.35f, 0.12f), Vector3.zero, new(0.18f, 0.18f, 0.18f), Grey, false);

        var rotor = new GameObject("MainRotor").transform;
        rotor.SetParent(model, false);
        rotor.localPosition = new(0f, 3.55f, 0.12f);
        Part(rotor, PrimitiveType.Sphere, "Hub", Vector3.zero, Vector3.zero, new(0.45f, 0.25f, 0.45f), Red, false);
        Part(rotor, PrimitiveType.Cube, "Blade", new(0f, 0.05f, 0f), Vector3.zero, new(8.5f, 0.07f, 0.4f), Yellow, false);
        Part(rotor, PrimitiveType.Sphere, "Tip", new(4.25f, 0.05f, 0f), Vector3.zero, Vector3.one * 0.3f, Red, false);
        Part(rotor, PrimitiveType.Sphere, "Tip", new(-4.25f, 0.05f, 0f), Vector3.zero, Vector3.one * 0.3f, Red, false);
        heli.MainRotor = rotor;

        // Tail.
        Part(model, PrimitiveType.Cube, "Fin", new(0f, 1.75f, -4.55f), new(-15f, 0f, 0f), new(0.1f, 1.1f, 0.7f), Yellow, false);
        Part(model, PrimitiveType.Cube, "Stabilizer", new(0f, 1.2f, -4.3f), Vector3.zero, new(1.4f, 0.08f, 0.45f), Yellow, false);
        var tailRotor = new GameObject("TailRotor").transform;
        tailRotor.SetParent(model, false);
        tailRotor.localPosition = new(0.15f, 1.8f, -4.65f);
        Part(tailRotor, PrimitiveType.Cube, "TailBlade", Vector3.zero, Vector3.zero, new(0.05f, 1.3f, 0.14f), Red, false);
        Part(tailRotor, PrimitiveType.Cube, "TailBlade", Vector3.zero, new(90f, 0f, 0f), new(0.05f, 1.3f, 0.14f), Red, false);
        heli.TailRotor = tailRotor;

        // Googly eyes.
        var pupils = new List<Transform>();
        var rest = new List<Vector3>();
        foreach (float x in new[] { -0.42f, 0.42f })
        {
            Part(model, PrimitiveType.Sphere, "Eye", new(x, 1.3f, 1.85f), Vector3.zero, Vector3.one * 0.5f, White, false);
            var p = Part(model, PrimitiveType.Sphere, "Pupil", new(x, 1.3f, 2.08f), Vector3.zero, new(0.22f, 0.22f, 0.1f), Black, false);
            pupils.Add(p);
            rest.Add(p.localPosition);
        }
        heli.Pupils = pupils.ToArray();
        heli.PupilRest = rest.ToArray();

        // Anchors (children of the scaled model so they track scale).
        heli.PilotSeat = Anchor(model, "PilotAnchor",
            new(0f, 1.7f + ChoppaConfig.SeatHeightOffset.Value, 0.1f + ChoppaConfig.SeatForwardOffset.Value));
        heli.ExitPoint = Anchor(model, "ExitAnchor", new(-2.2f, 0.3f, 0.3f));
    }

    static Transform Anchor(Transform parent, string name, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }

    static Transform Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Color color, bool solid)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localEulerAngles = euler;
        t.localScale = scale;
        if (!solid)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
        }
        var r = go.GetComponent<MeshRenderer>();
        if (r != null) r.sharedMaterial = ChoppaMaterials.Get(color);
        return t;
    }
}
