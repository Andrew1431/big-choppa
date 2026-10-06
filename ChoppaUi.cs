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

        // 9-slice: corners r+1, gradient down the stretched middle. A transparent margin of AaPad pixels (drawn
        // outside the rect via `overflow`) keeps the anti-aliased edge inside the quad, so straight edges stay smooth
        // when the GUI matrix rotates them; without it the quad's own edge is the shape's edge and aliases.
        int iw = r * 2 + 4, ih = r * 2 + 64, w = iw + AaPad * 2, h = ih + AaPad * 2;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = Mathf.Clamp01(1f - (y - AaPad + 0.5f) / ih); // 0 at the top (texture rows go bottom-up)
            var fill = Color.Lerp(top, bottom, v);
            if (gloss > 0f) fill = Color.Lerp(fill, new Color(1f, 1f, 1f, fill.a), gloss * Mathf.Clamp01(1f - v * 2.2f));
            for (int x = 0; x < w; x++)
            {
                float d = RoundedDistance(x - AaPad + 0.5f, y - AaPad + 0.5f, iw, ih, r);
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
        int b = r + 1 + AaPad;
        s = Style(Tex(px, w, h), b, b, b, b);
        s.overflow = new RectOffset(AaPad, AaPad, AaPad, AaPad);
        boxes[key] = s;
        return s;
    }

    const int AaPad = 2;

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

    // Seamless polka-dot tile, 4×4 cells of 32 px: big dots on the cell corners, small ones in the middle (radii in
    // cell pixels). Pan it with Tile(); several cells per texture keeps the number of draws down.
    public static Texture2D Dots(Color bg, Color big, Color small, float bigR, float smallR)
    {
        string key = $"dots{bg}|{big}|{small}|{bigR}|{smallR}";
        if (textures.TryGetValue(key, out var t) && t != null) return t;
        const int n = 32, size = n * 4;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x % n + 0.5f, fy = y % n + 0.5f;
                float cx = Mathf.Min(fx, n - fx), cy = Mathf.Min(fy, n - fy); // to the nearest corner, wrapping
                float dBig = Mathf.Sqrt(cx * cx + cy * cy) - bigR;
                float mx = fx - n / 2f, my = fy - n / 2f;
                float dSmall = Mathf.Sqrt(mx * mx + my * my) - smallR;
                var c = Color.Lerp(bg, small, Mathf.Clamp01(0.5f - dSmall));
                px[y * size + x] = Color.Lerp(c, big, Mathf.Clamp01(0.5f - dBig));
            }
        t = Tex(px, size, size, TextureWrapMode.Repeat);
        textures[key] = t;
        return t;
    }

    // Seamless 45° stripes, half `a`, half `b`: two 64 px periods across a 128 px tile.
    public static Texture2D Stripes(Color a, Color b)
    {
        string key = $"stripes{a}|{b}";
        if (textures.TryGetValue(key, out var t) && t != null) return t;
        const int n = 64, size = 128;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float s = (x + y + 1f) % n; // diagonal position within one period
                float toEdge = Mathf.Min(Mathf.Abs(s - n / 2f), Mathf.Min(s, n - s)) / 1.414f;
                float inB = s < n / 2f ? 0f : 1f;
                float aa = Mathf.Clamp01(toEdge + 0.5f);
                px[y * size + x] = Color.Lerp(Color.Lerp(a, b, 0.5f), Color.Lerp(a, b, inB), aa);
            }
        t = Tex(px, size, size, TextureWrapMode.Repeat);
        textures[key] = t;
        return t;
    }

    // Draws a repeating tile over `r`, `tilePx` screen pixels per tile, shifted by `offsetPx` (animate it to pan).
    // A grid of plain DrawTextures in a clip group: GUI.DrawTextureWithTexCoords crashes the runtime under IL2CPP.
    public static void Tile(Rect r, Texture2D t, float tilePx, Vector2 offsetPx)
    {
        GUI.BeginGroup(r);
        float ox = Mathf.Repeat(offsetPx.x, tilePx) - tilePx, oy = Mathf.Repeat(offsetPx.y, tilePx) - tilePx;
        for (float y = oy; y < r.height; y += tilePx)
            for (float x = ox; x < r.width; x += tilePx)
                GUI.DrawTexture(new Rect(x, y, tilePx, tilePx), t);
        GUI.EndGroup();
    }

    // Downward-pointing triangle with an outline, for the "this one" pointer.
    public static Texture2D Triangle(Color fill, Color border, float borderWidth)
    {
        string key = $"tri{fill}|{border}|{borderWidth}";
        if (textures.TryGetValue(key, out var t) && t != null) return t;
        const int n = 96;
        var px = new Color[n * n];
        var a = new Vector2(4f, 10f); var b = new Vector2(n - 4f, 10f); var c = new Vector2(n / 2f, n - 8f); // y down
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2(x + 0.5f, n - 1 - y + 0.5f);
                float d = Mathf.Max(EdgeOut(p, a, b), Mathf.Max(EdgeOut(p, b, c), EdgeOut(p, c, a))) - 4f; // a little rounding
                float inside = Mathf.Clamp01(0.5f - d);
                var col = Color.Lerp(fill, border, Mathf.Clamp01(d + borderWidth + 0.5f));
                col.a *= inside;
                px[y * n + x] = col;
            }
        t = Tex(px, n, n);
        textures[key] = t;
        return t;
    }

    // Signed distance outside the edge a->b of a clockwise (screen space) triangle.
    static float EdgeOut(Vector2 p, Vector2 a, Vector2 b)
    {
        var e = (b - a).normalized;
        return (p.x - a.x) * e.y - (p.y - a.y) * e.x;
    }

    // Text with a thick outline, faked by stamping the label around a circle in the outline colour first.
    public static void OutlinedLabel(Rect r, string s, GUIStyle style, Color fill, Color outline, float width)
    {
        var keep = style.normal.textColor;
        style.normal.textColor = outline;
        int steps = width > 2.5f ? 16 : 8;
        for (int i = 0; i < steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            GUI.Label(new Rect(r.x + Mathf.Cos(a) * width, r.y + Mathf.Sin(a) * width, r.width, r.height), s, style);
        }
        style.normal.textColor = fill;
        GUI.Label(r, s, style);
        style.normal.textColor = keep;
    }

    // Overshooting ease for bouncy pops (goes a little past 1 before settling).
    public static float Back(float t)
    {
        t = Mathf.Clamp01(t) - 1f;
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * t * t * t + c1 * t * t;
    }

    // Fully rounded ends for a pill `height` tall; a hair under half so the 9-slice corners never overlap (they'd
    // smear a line through the middle).
    public static float PillRadius(float height) => Mathf.Floor(height / 2f) - 2f;

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

    // A font the game has loaded itself (e.g. the source font of a TextMeshPro asset), by name fragment, in order of
    // preference. Only hits are cached: the game may load its fonts after our first look.
    public static Font LoadedFont(params string[] fragments)
    {
        string key = "loaded|" + string.Join("|", fragments);
        if (fonts.TryGetValue(key, out var f) && f != null) return f;
        try
        {
            var all = Resources.FindObjectsOfTypeAll<Font>();
            foreach (var frag in fragments)
                foreach (var font in all)
                    if (font != null && font.name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        fonts[key] = font;
                        return font;
                    }
        }
        catch (Exception e) { Plugin.Verbose($"Loaded fonts unavailable: {e.Message}"); }
        return null;
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

    static Texture2D Tex(Color[] px, int w, int h, TextureWrapMode wrap = TextureWrapMode.Clamp)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = wrap,
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
