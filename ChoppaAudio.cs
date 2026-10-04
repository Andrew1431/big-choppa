using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Audio;

namespace BigChoppa;

// Every sound is synthesised at startup: no asset files to ship. Loops are built from whole cycles so they
// repeat seamlessly, then pitched up/down with rotor speed.
internal static class ChoppaAudio
{
    const int Rate = 44100;
    static AudioClip rotorClip, turbineClip;
    static AudioClip[] bonkClips;
    static AudioMixerGroup mixerGroup;
    static bool mixerSearched;

    // ---------- per-choppa loops ----------

    public static void Attach(Helicopter heli)
    {
        if (!EnsureClips()) return;
        var go = new GameObject("Audio");
        go.transform.SetParent(heli.transform, false);
        go.transform.localPosition = new Vector3(0f, 2.5f, 0f) * ChoppaConfig.HeliScale.Value;
        heli.RotorAudio = Loop(go, rotorClip, 12f);
        heli.TurbineAudio = Loop(go, turbineClip, 6f);
    }

    static AudioSource Loop(GameObject go, AudioClip clip, float minDistance)
    {
        var s = go.AddComponent<AudioSource>();
        Setup(s, minDistance);
        s.clip = clip;
        s.loop = true;
        s.volume = 0f;
        return s;
    }

    static void Setup(AudioSource s, float minDistance)
    {
        s.playOnAwake = false;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Logarithmic;
        s.minDistance = minDistance * ChoppaConfig.HeliScale.Value;
        s.maxDistance = ChoppaConfig.AudioMaxDistance.Value;
        s.dopplerLevel = 0.6f;
        s.spread = 60f;
        s.priority = 64;
        var group = MixerGroup();
        if (group != null) s.outputAudioMixerGroup = group;
    }

    // Called every frame from Helicopter.Update. Rotor speed drives pitch, collective adds "bite".
    public static void Drive(Helicopter h)
    {
        if (h.RotorAudio == null || h.TurbineAudio == null) return;
        float vol = ChoppaConfig.AudioVolume.Value;
        float spin = h.Broken ? 0f : h.RotorSpin;
        float load = h.Collective;

        bool on = spin > 0.005f && vol > 0f;
        foreach (var src in new[] { h.RotorAudio, h.TurbineAudio })
        {
            if (on && !src.isPlaying)
            {
                src.time = UnityEngine.Random.value * src.clip.length * 0.99f;
                src.Play();
            }
            else if (!on && src.isPlaying) src.Stop();
        }
        if (!on) return;

        h.RotorAudio.volume = vol * Mathf.Pow(spin, 1.3f) * (0.55f + 0.45f * load);
        h.RotorAudio.pitch = 0.35f + 0.65f * spin + 0.1f * (load - 0.5f) * spin;
        h.TurbineAudio.volume = vol * 0.12f * Mathf.Pow(spin, 0.7f);
        h.TurbineAudio.pitch = 0.4f + 0.6f * spin + 0.1f * load;
    }

    // ---------- one-shots ----------

    // Hollow plastic "bonk". size 0..1: bigger = lower and louder. Used for debris and hard landings.
    public static void Bonk(AudioSource src, float impactSpeed, float size, float volume = 1f)
    {
        float vol = ChoppaConfig.AudioVolume.Value;
        if (vol <= 0f || src == null || !EnsureClips()) return;
        float loud = Mathf.Clamp01((impactSpeed - 0.5f) / 8f);
        if (loud <= 0.02f) return;
        src.pitch = Mathf.Lerp(1.35f, 0.55f, size) * UnityEngine.Random.Range(0.9f, 1.1f);
        src.PlayOneShot(bonkClips[UnityEngine.Random.Range(0, bonkClips.Length)], vol * volume * loud * Mathf.Lerp(0.6f, 1f, size));
    }

    public static AudioSource OneShotSource(GameObject go, float minDistance)
    {
        var s = go.AddComponent<AudioSource>();
        Setup(s, minDistance);
        s.loop = false;
        return s;
    }

    // The big initial BONK where the choppa hit; the pieces add their own as they bounce.
    public static void PlayCrash(Vector3 position)
    {
        if (ChoppaConfig.AudioVolume.Value <= 0f || !EnsureClips()) return;
        var go = new GameObject("ChoppaCrashSound");
        go.transform.position = position;
        var s = OneShotSource(go, 15f);
        Bonk(s, 20f, 1f);
        UnityEngine.Object.Destroy(go, 2f);
    }

    // ---------- mixer ----------

    // Route through the game's sound-effects mixer so its volume slider applies. Guessed by name from the
    // groups the game's own AudioSources use.
    static AudioMixerGroup MixerGroup()
    {
        if (mixerGroup != null && !mixerGroup.WasCollected) return mixerGroup;
        if (mixerSearched) return null;
        mixerSearched = true;
        try
        {
            var counts = new Dictionary<string, (AudioMixerGroup g, int n)>();
            foreach (var src in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var g = src.outputAudioMixerGroup;
                if (g == null) continue;
                counts[g.name] = counts.TryGetValue(g.name, out var e) ? (g, e.n + 1) : (g, 1);
            }
            Plugin.Verbose($"Game mixer groups: {string.Join(", ", System.Linq.Enumerable.Select(counts, kv => $"{kv.Key}x{kv.Value.n}"))}");

            int Score(string name)
            {
                string n = name.ToLowerInvariant();
                if (n.Contains("music") || n.Contains("ui") || n.Contains("voice") || n.Contains("amb")) return -1;
                if (n.Contains("sfx") || n.Contains("effect")) return 3;
                if (n.Contains("world") || n.Contains("game") || n.Contains("prop")) return 2;
                return 1;
            }
            string best = null;
            foreach (var kv in counts)
            {
                if (Score(kv.Key) < 0) continue;
                if (best == null || Score(kv.Key) > Score(best) || (Score(kv.Key) == Score(best) && kv.Value.n > counts[best].n)) best = kv.Key;
            }
            if (best != null)
            {
                mixerGroup = counts[best].g;
                Plugin.L.LogInfo($"Choppa audio routed to mixer group '{best}'.");
            }
            else Plugin.L.LogInfo("No game mixer group found; choppa audio plays direct.");
        }
        catch (Exception e) { Plugin.L.LogWarning($"Mixer group search failed: {e.Message}"); }
        return mixerGroup;
    }

    // ---------- synthesis ----------

    static bool EnsureClips()
    {
        try
        {
            if (Dead(rotorClip)) rotorClip = MakeClip("ChoppaRotor", SynthRotor());
            if (Dead(turbineClip)) turbineClip = MakeClip("ChoppaTurbine", SynthTurbine());
            if (bonkClips == null || Array.Exists(bonkClips, Dead))
            {
                bonkClips = new AudioClip[4];
                for (int i = 0; i < bonkClips.Length; i++) bonkClips[i] = MakeClip($"ChoppaBonk{i}", SynthBonk(i));
            }
            return true;
        }
        catch (Exception e)
        {
            Plugin.L.LogError($"Choppa audio synthesis failed: {e}");
            return false;
        }
    }

    static bool Dead(AudioClip c) => c == null || c.WasCollected;

    static AudioClip MakeClip(string name, float[] data)
    {
        Normalize(data, 0.9f);
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData((Il2CppStructArray<float>)data, 0);
        clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return clip;
    }

    // Two-blade rotor "whup-whup": 10 thumps per second at pitch 1 over a low rumble.
    static float[] SynthRotor()
    {
        int n = Rate; // 1 s, 10 whole thumps -> seamless loop
        var rng = new System.Random(1234);
        var noise = LowPassLoop(WhiteNoise(n, rng), 420f);
        var rumble = LowPassLoop(WhiteNoise(n, rng), 120f);
        var o = new float[n];
        const int thumps = 10;
        int period = n / thumps;
        for (int i = 0; i < n; i++)
        {
            float tp = (i % period) / (float)Rate; // time since this thump
            float env = Mathf.Exp(-tp / 0.035f) * Mathf.Clamp01(tp / 0.003f);
            float body = Mathf.Sin(2f * Mathf.PI * 70f * tp) * Mathf.Exp(-tp / 0.05f);
            o[i] = env * noise[i] * 2.2f + body * 0.7f + rumble[i] * 1.2f;
        }
        return o;
    }

    // Fluttery tail-rotor whine: a 620 Hz tone pulsed 34 times a second. Whole-Hz frequencies so the 1 s loop
    // repeats cleanly.
    static float[] SynthTurbine()
    {
        int n = Rate;
        var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float flutter = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 34f * t);
            o[i] = Mathf.Sin(2f * Mathf.PI * 620f * t) * flutter;
        }
        return o;
    }

    // Hollow plastic toy hit: a quick downward pitch blip with an inharmonic overtone and a tick of noise.
    static float[] SynthBonk(int variant)
    {
        int n = (int)(Rate * 0.35f);
        var rng = new System.Random(500 + variant);
        float f0 = 260f + variant * 70f;
        var click = LowPass(WhiteNoise(n, rng), 3000f);
        var o = new float[n];
        double p1 = 0, p2 = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = f0 * (1f + 0.6f * Mathf.Exp(-t / 0.012f));
            p1 += 2.0 * Math.PI * f / Rate;
            p2 += 2.0 * Math.PI * f * 2.31 / Rate;
            float env = Mathf.Exp(-t / 0.07f) * Mathf.Clamp01(t / 0.001f);
            o[i] = env * ((float)Math.Sin(p1) + 0.35f * (float)Math.Sin(p2) * Mathf.Exp(-t / 0.03f))
                 + click[i] * 1.5f * Mathf.Exp(-t / 0.004f);
        }
        return o;
    }

    static float[] WhiteNoise(int n, System.Random rng)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++) a[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        return a;
    }

    static float[] LowPass(float[] x, float cutoff)
    {
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
        var y = new float[x.Length];
        float s = 0f;
        for (int i = 0; i < x.Length; i++) y[i] = s += a * (x[i] - s);
        return y;
    }

    // Runs the filter around the loop twice so the end flows into the start without a click.
    static float[] LowPassLoop(float[] x, float cutoff)
    {
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
        var y = new float[x.Length];
        float s = 0f;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < x.Length; i++)
                y[i] = s += a * (x[i] - s);
        return y;
    }

    static void Normalize(float[] d, float peak)
    {
        float max = 1e-6f;
        foreach (var v in d) max = Mathf.Max(max, Mathf.Abs(v));
        float k = peak / max;
        for (int i = 0; i < d.Length; i++) d[i] *= k;
    }
}
