using System.Collections.Generic;
using UnityEngine;

namespace BigChoppa;

// The game renders with URP, so the built-in default material shows up magenta. Try URP shaders, then fall back to
// cloning one of the game's own materials, which is guaranteed to be compatible with its pipeline.
internal static class ChoppaMaterials
{
    static readonly Dictionary<Color, Material> cache = new(), glowCache = new();
    static Shader shader;
    static Material template;
    static bool resolved;

    public static Material Get(Color color)
    {
        if (cache.TryGetValue(color, out var existing) && existing != null) return existing;
        Resolve();

        Material mat;
        if (shader != null) mat = new Material(shader);
        else if (template != null) mat = new Material(template);
        else return null;

        mat.name = $"BigChoppa_{ColorUtility.ToHtmlStringRGB(color)}";
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", null);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
        mat.color = color;
        cache[color] = mat;
        return mat;
    }

    // Lamp lenses: unlit so they read as glowing at night. Falls back to an emissive lit material.
    public static Material GetGlow(Color color)
    {
        if (glowCache.TryGetValue(color, out var existing) && existing != null) return existing;
        Material mat;
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit != null)
        {
            mat = new Material(unlit);
            mat.SetColor("_BaseColor", color * 1.5f);
        }
        else
        {
            var lit = Get(color);
            if (lit == null) return null;
            mat = new Material(lit);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2f);
        }
        mat.name = $"BigChoppa_Glow_{ColorUtility.ToHtmlStringRGB(color)}";
        glowCache[color] = mat;
        return mat;
    }

    static void Resolve()
    {
        if (resolved) return;
        resolved = true;

        foreach (var name in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit" })
        {
            shader = Shader.Find(name);
            if (shader != null)
            {
                Plugin.L.LogInfo($"Choppa material shader: {name}");
                return;
            }
        }

        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            var m = r.sharedMaterial;
            if (m == null || m.shader == null) continue;
            if (!m.HasProperty("_BaseColor") && !m.HasProperty("_Color")) continue;
            template = m;
            Plugin.L.LogInfo($"No URP shader found by name; cloning game material '{m.name}' (shader '{m.shader.name}').");
            return;
        }

        Plugin.L.LogWarning("Could not find any usable shader or material; the choppa will probably render magenta.");
    }
}
