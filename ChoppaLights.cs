using UnityEngine;

namespace BigChoppa;

// Night lights. Steady nav lights (red left, green right, white tail), flashing red beacons on the roof and belly,
// white strobes on the stabilizer tips and a long-throw headlight all run while the choppa is powered. A faint cabin
// glow is always on so a parked choppa can still be found in the dark.
// Lenses are unlit primitives; only the headlight, glow and roof beacon are real lights (they cost per pixel).
internal sealed class ChoppaLights
{
    static readonly Color NavRed = new(1f, 0.08f, 0.05f);
    static readonly Color NavGreen = new(0.1f, 1f, 0.25f);
    static readonly Color White = new(1f, 0.97f, 0.9f);
    static readonly Color Warm = new(1f, 0.85f, 0.6f);

    struct Lens
    {
        public MeshRenderer Renderer;
        public Material On, Off;
        public void Set(bool on) { if (Renderer != null) Renderer.sharedMaterial = on ? On : Off; }
    }

    Light headlight, glow, beaconLight;
    Lens[] steady, beacons, strobes;
    float phase;
    bool wasPowered = true; // forces the first Drive to apply the off state

    public static ChoppaLights Build(Transform model, uint id)
    {
        var l = new ChoppaLights { phase = (id % 1000) * 0.137f };

        l.steady = new[]
        {
            MakeLens(model, "NavLeft", new(-1.03f, 1.2f, 1.0f), 0.16f, NavRed),
            MakeLens(model, "NavRight", new(1.03f, 1.2f, 1.0f), 0.16f, NavGreen),
            MakeLens(model, "TailLight", new(0f, 1.15f, -4.85f), 0.14f, White),
            MakeLens(model, "HeadlightLeft", new(-0.5f, 0.75f, 1.92f), 0.24f, White),
            MakeLens(model, "HeadlightRight", new(0.5f, 0.75f, 1.92f), 0.24f, White),
        };
        l.beacons = new[]
        {
            MakeLens(model, "BeaconTop", new(0f, 3.25f, -0.95f), 0.22f, NavRed),
            MakeLens(model, "BeaconBelly", new(0f, 0.48f, -0.5f), 0.2f, NavRed),
        };
        l.strobes = new[]
        {
            MakeLens(model, "StrobeLeft", new(-0.72f, 1.2f, -4.3f), 0.12f, White),
            MakeLens(model, "StrobeRight", new(0.72f, 1.2f, -4.3f), 0.12f, White),
        };

        // Just in front of the nose so the beam doesn't start inside it. Tilted down a touch to light the ground ahead.
        l.headlight = MakeLight(model, "Headlight", new(0f, 0.75f, 2.15f), LightType.Spot, White);
        l.headlight.renderMode = LightRenderMode.ForcePixel; // never drop it to the per-object light limit

        float s = ChoppaConfig.HeliScale.Value;
        l.glow = MakeLight(model, "CabinGlow", new(0f, 2.4f, 0.1f), LightType.Point, Warm);
        l.glow.range = 6f * s;

        l.beaconLight = MakeLight(model, "BeaconGlow", new(0f, 3.45f, -0.95f), LightType.Point, NavRed);
        l.beaconLight.range = 10f * s;
        l.beaconLight.intensity = 3f;
        return l;
    }

    public void Drive(bool powered)
    {
        // Read config every frame so live config edits show up straight away.
        headlight.transform.localRotation = Quaternion.Euler(ChoppaConfig.HeadlightTilt.Value, 0f, 0f);
        headlight.range = ChoppaConfig.HeadlightRange.Value;
        headlight.spotAngle = ChoppaConfig.HeadlightAngle.Value;
        headlight.innerSpotAngle = ChoppaConfig.HeadlightAngle.Value * 0.5f;
        headlight.intensity = ChoppaConfig.HeadlightIntensity.Value;
        headlight.enabled = powered && headlight.intensity > 0f;
        glow.intensity = ChoppaConfig.GlowIntensity.Value;
        glow.enabled = glow.intensity > 0f;

        if (powered != wasPowered)
        {
            wasPowered = powered;
            foreach (var lens in steady) lens.Set(powered);
            if (!powered)
            {
                beaconLight.enabled = false;
                foreach (var lens in beacons) lens.Set(false);
                foreach (var lens in strobes) lens.Set(false);
            }
        }
        if (!powered) return;

        float t = Time.time + phase;
        bool beaconOn = Mathf.Repeat(t, 1f) < 0.12f;
        float st = Mathf.Repeat(t * 0.83f, 1.2f);
        bool strobeOn = st < 0.05f || (st > 0.15f && st < 0.2f); // double flash
        beaconLight.enabled = beaconOn;
        foreach (var lens in beacons) lens.Set(beaconOn);
        foreach (var lens in strobes) lens.Set(strobeOn);
    }

    static Lens MakeLens(Transform model, string name, Vector3 pos, float size, Color color)
    {
        var t = HeliModel.Part(model, PrimitiveType.Sphere, name, pos, Vector3.zero, Vector3.one * size, color, false);
        return new Lens
        {
            Renderer = t.GetComponent<MeshRenderer>(),
            On = ChoppaMaterials.GetGlow(color),
            Off = ChoppaMaterials.Get(new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f)),
        };
    }

    static Light MakeLight(Transform model, string name, Vector3 pos, LightType type, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(model, false);
        go.transform.localPosition = pos;
        var light = go.AddComponent<Light>();
        light.type = type;
        light.color = color;
        light.shadows = LightShadows.None;
        return light;
    }
}
