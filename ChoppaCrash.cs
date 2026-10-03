using System.Collections.Generic;
using UnityEngine;

namespace BigChoppa;

// Turns the choppa into a pile of bouncing toy parts. The rotors come off whole and keep spinning.
internal static class ChoppaCrash
{
    // Same seed on every PC so everyone sees roughly the same pieces fly the same way.
    public static void Break(Helicopter heli, int seed)
    {
        var saved = Random.state;
        Random.InitState(seed);
        try { BreakSeeded(heli); }
        finally { Random.state = saved; }
    }

    static void BreakSeeded(Helicopter heli)
    {
        var model = heli.transform.Find("Model");
        Vector3 velocity = heli.ImpactVelocity;
        heli.Body.isKinematic = true; // stop a proxy's interpolation fighting the debris
        Vector3 center = heli.transform.TransformPoint(heli.Body.centerOfMass);
        float s = ChoppaConfig.HeliScale.Value;
        float life = ChoppaConfig.DebrisSeconds.Value;

        ChoppaAudio.PlayCrash(center);

        var pieces = new List<Transform>();
        if (heli.MainRotor != null) pieces.Add(heli.MainRotor);
        if (heli.TailRotor != null) pieces.Add(heli.TailRotor);
        if (model != null)
        {
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var t = r.transform;
                if (heli.MainRotor != null && t.IsChildOf(heli.MainRotor)) continue;
                if (heli.TailRotor != null && t.IsChildOf(heli.TailRotor)) continue;
                pieces.Add(t);
            }
        }

        foreach (var t in pieces)
        {
            t.SetParent(null, true);
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
                if (r.GetComponent<Collider>() == null) r.gameObject.AddComponent<BoxCollider>();

            var rb = t.gameObject.AddComponent<Rigidbody>();
            bool isRotor = t == heli.MainRotor || t == heli.TailRotor;
            rb.mass = isRotor ? 40f : 15f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.maxAngularVelocity = 60f;
            rb.angularDamping = 0.3f;

            Vector3 away = t.position - center;
            away = away.sqrMagnitude < 0.01f ? Random.onUnitSphere : away.normalized;
            float blast = ChoppaConfig.CrashBlast.Value;
            rb.linearVelocity = velocity * 0.5f + away * Random.Range(0.4f, 1f) * blast * s + Vector3.up * Random.Range(0.3f, 0.8f) * blast * s;
            rb.angularVelocity = Random.insideUnitSphere * 12f;

            ChoppaPhysics.CollideWithEverything(t.gameObject);
            ChoppaBonker.Add(t.gameObject, isRotor ? 0.8f : Random.Range(0.1f, 0.6f), 1.2f);

            if (t == heli.MainRotor)
            {
                rb.linearVelocity += heli.transform.up * (blast * 0.8f * s);
                rb.angularVelocity = heli.transform.up * (heli.RotorSpin * 40f + 5f);
            }

            if (life > 0f) Object.Destroy(t.gameObject, life + Random.Range(0f, 3f));
        }

        Object.Destroy(heli.gameObject);
    }
}
