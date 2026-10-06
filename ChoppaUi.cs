using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigChoppa;

// Procedural textures and fonts for the IMGUI menus. Shapes are anti-aliased signed-distance rounded rects, drawn as
// 9-slice styles so corners stay crisp at any size; gradients run down the stretched middle.
internal static class ChoppaUi
{
    static readonly Dictionary<string, GUIStyle> boxes = new();
    static readonly Dictionary<string, Texture2D> textures = new();

    // Rounded rect, vertical gradient top -> bottom, optional border and glossy top highlight.
    public static GUIStyle Box(float radius, Color top, Color bottom, Color border = default, float borderWidth = 0f, float gloss = 0f)
    {
        int r = Mathf.Max(1, Mathf.RoundToInt(radius));
        string key = $"box{r}|{top}|{bottom}|{border}|{borderWidth}|{gloss}";
        if (boxes.TryGetValue(key, out var s) && s.normal.background != null) return s;

        int w = r * 2 + 4, h = r * 2 + 64; // 9-slice: corners r+1, gradient down the stretched middle
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = 1f - (y + 0.5f) / h; // 0 at the top (texture rows go bottom-up)
            var fill = Color.Lerp(top, bottom, v);
            if (gloss > 0f) fill = Color.Lerp(fill, new Color(1f, 1f, 1f, fill.a), gloss * Mathf.Clamp01(1f - v * 2.2f));
            for (int x = 0; x < w; x++)
            {
                float d = RoundedDistance(x + 0.5f, y + 0.5f, w, h, r);
                float inside = Mathf.Clamp01(0.5f - d);
                Color c = fill;
                if (borderWidth > 0f)
                {
                    float edge = Mathf.Clamp01(d + borderWidth + 0.5f); // 1 within borderWidth of the edge
                    c = Color.Lerp(fill, Blend(fill, border), edge);
                }
                c.a *= inside;
                px[y * w + x] = c;
            }
        }
        s = Style(Tex(px, w, h), r + 1, r + 1, r + 1, r + 1);
        boxes[key] = s;
        return s;
    }

    // Soft blurred rounded rect for drop shadows and glows; draw it `spread` larger than the thing it sits under.
    public static GUIStyle Shadow(float radius, float spread, Color color)
    {
        int r = Mathf.RoundToInt(radius), sp = Mathf.Max(2, Mathf.RoundToInt(spread));
        string key = $"shadow{r}|{sp}|{color}";
        if (boxes.TryGetValue(key, out var s) && s.normal.background != null) return s;
        int b = r + sp, w = b * 2 + 4, h = b * 2 + 4;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = RoundedDistance(x + 0.5f, y + 0.5f, w, h, b) + sp; // 0 at the casting shape's edge
                float a = d <= 0f ? 1f : Mathf.Clamp01(1f - d / sp);
                a = a * a * (3f - 2f * a);
                var c = color; c.a *= a;
                px[y * w + x] = c;
            }
        s = Style(Tex(px, w, h), b + 2, b + 2, b + 2, b + 2);
        boxes[key] = s;
        return s;
    }

    // Radial glow (centre colour fading to edge colour), stretched over whatever rect it's drawn in.
    public static Texture2D Radial(Color centre, Color edge, float falloff = 1f)
    {
        string key = $"radial{centre}|{edge}|{falloff}";
        if (textures.TryGetValue(key, out var t) && t != null) return t;
        const int n = 128;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                px[y * n + x] = Color.Lerp(centre, edge, Mathf.Pow(d, falloff));
            }
        t = Tex(px, n, n);
        textures[key] = t;
        return t;
    }

    // Horizontal gradient (for divider lines and sheens).
    public static Texture2D HGradient(Color left, Color right)
    {
        string key = $"hgrad{left}|{right}";
        if (textures.TryGetValue(key, out var t) && t != null) return t;
        const int n = 64;
        var px = new Color[n];
        for (int x = 0; x < n; x++) px[x] = Color.Lerp(left, right, x / (n - 1f));
        t = Tex(px, n, 1);
        textures[key] = t;
        return t;
    }

    public static Texture2D Solid(Color c)
    {
        string key = $"solid{c}";
        if (textures.TryGetValue(key, out var t) && t != null) return t;
        t = Tex(new[] { c }, 1, 1);
        textures[key] = t;
        return t;
    }

    public static void Draw(Rect r, GUIStyle s) => GUI.Label(r, "", s);

    public static Rect Grow(Rect r, float by) => new(r.x - by, r.y - by, r.width + by * 2f, r.height + by * 2f);

    public static float Ease(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }

    // Dark text on light accents, white on dark ones.
    public static Color TextOn(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f > 0.6f ? new Color(0.07f, 0.08f, 0.12f) : Color.white;

    public static Color A(Color c, float a) { c.a = a; return c; }

    // ---------- fonts ----------

    static readonly Dictionary<string, Font> fonts = new();

    // Windows fonts that give the menu its look; anything missing falls back to Unity's default font.
    public static Font OsFont(params string[] names)
    {
        string key = string.Join("|", names);
        if (fonts.TryGetValue(key, out var f)) return f;
        f = null;
        try
        {
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var n in names)
                if (installed.Contains(n)) { f = Font.CreateDynamicFontFromOSFont(n, 32); break; }
        }
        catch (Exception e) { Plugin.Verbose($"OS fonts unavailable: {e.Message}"); }
        if (f != null) f.hideFlags = HideFlags.HideAndDontSave;
        fonts[key] = f;
        return f;
    }

    // ---------- internals ----------

    // Signed distance to a rounded rect filling (0,0)-(w,h): negative inside.
    static float RoundedDistance(float x, float y, float w, float h, float r)
    {
        float qx = Mathf.Abs(x - w / 2f) - (w / 2f - r);
        float qy = Mathf.Abs(y - h / 2f) - (h / 2f - r);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    static Color Blend(Color under, Color over) =>
        new(Mathf.Lerp(under.r, over.r, over.a), Mathf.Lerp(under.g, over.g, over.a), Mathf.Lerp(under.b, over.b, over.a),
            Mathf.Max(under.a, over.a));

    static Texture2D Tex(Color[] px, int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    static GUIStyle Style(Texture2D tex, int l, int r, int t, int b)
    {
        var s = new GUIStyle { border = new RectOffset(l, r, t, b) };
        s.normal.background = tex;
        return s;
    }
}
