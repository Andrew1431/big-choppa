using System;
using UnityEngine;

namespace BigChoppa;

// The F8 chooser: one card per entry in Vehicles.Catalog, each with Call In (flies in and lands) and Spawn (appears
// in front of you). Plain IMGUI with Rects only (GUILayout is unreliable under IL2CPP); cards scroll once there are
// more than fit on screen.
internal sealed class ChoppaMenu
{
    public bool IsOpen { get; private set; }

    readonly Action<Vehicle, bool> choose; // (vehicle, flyIn)
    Vector2 scroll;

    GUIStyle title, sub, name, seats, body, button, close;
    Texture2D panelTex, cardTex, accentTex;

    public ChoppaMenu(Action<Vehicle, bool> choose) => this.choose = choose;

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        scroll = Vector2.zero;
        // Menu mode stops the game's look/move input while the mouse is free.
        try { ControlsManager.SetMenuMode(true); } catch (Exception e) { Plugin.L.LogWarning($"Menu mode: {e.Message}"); }
        try { CursorManager.SetFree(); } catch { }
        FreeCursor();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        try { ControlsManager.SetMenuMode(false); } catch (Exception e) { Plugin.L.LogWarning($"Menu mode: {e.Message}"); }
        try { CursorManager.SetLocked(); }
        catch { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }

    // Every frame while open: the game likes to grab the cursor back.
    public void Tick()
    {
        if (!IsOpen) return;
        FreeCursor();
        if (ChoppaInput.Pressed(KeyCode.Escape)) Close();
    }

    static void FreeCursor()
    {
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
    }

    public void Draw()
    {
        if (!IsOpen) return;
        try { DrawPanel(); }
        catch (Exception e)
        {
            Plugin.L.LogError($"Choppa chooser: {e}");
            Close();
        }
    }

    void DrawPanel()
    {
        float k = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
        MakeStyles(k);

        var list = Vehicles.Catalog;
        float pad = 24f * k, cardH = 150f * k, gap = 12f * k, headH = 92f * k, footH = 36f * k;
        float w = Mathf.Min(760f * k, Screen.width - 32f);
        float listH = list.Length * (cardH + gap) - gap;
        float viewH = Mathf.Min(listH, Screen.height * 0.85f - headH - footH - pad * 2f);
        float h = pad * 2f + headH + viewH + footH;
        var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

        GUI.DrawTexture(panel, panelTex);
        GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 6f * k), accentTex);

        float x = panel.x + pad, y = panel.y + pad, inner = w - pad * 2f;
        GUI.Label(new Rect(x, y, inner, 40f * k), "Choose your choppa", title);
        GUI.Label(new Rect(x, y + 42f * k, inner, 40f * k),
            "Call In: it flies over and lands near you.   Spawn: it appears right in front of you.\nYour old choppa goes away if nobody's in it.", sub);
        y += headH;

        bool scrolls = listH > viewH + 0.5f;
        var view = new Rect(x, y, inner, viewH);
        var content = new Rect(0f, 0f, inner - (scrolls ? 18f * k : 0f), listH);
        scroll = GUI.BeginScrollView(view, scroll, content);
        for (int i = 0; i < list.Length; i++)
            DrawCard(list[i], new Rect(0f, i * (cardH + gap), content.width, cardH), k);
        GUI.EndScrollView();

        y += viewH;
        if (GUI.Button(new Rect(panel.xMax - pad - 140f * k, y + 6f * k, 140f * k, footH - 6f * k),
                $"Close ({ChoppaConfig.SpawnKey.Value} / Esc)", close))
            Close();
    }

    void DrawCard(VehicleInfo v, Rect r, float k)
    {
        GUI.DrawTexture(r, cardTex);
        float p = 14f * k, bw = 150f * k, bh = 50f * k;
        float textW = r.width - bw - p * 3f;
        GUI.Label(new Rect(r.x + p, r.y + p, textW, 32f * k), v.Name, name);
        GUI.Label(new Rect(r.x + p, r.y + p + 32f * k, textW, 24f * k), v.Seats, seats);
        GUI.Label(new Rect(r.x + p, r.y + p + 58f * k, textW, r.height - p * 2f - 58f * k), v.Description, body);

        float bx = r.xMax - p - bw, by = r.y + (r.height - bh * 2f - 10f * k) / 2f;
        if (GUI.Button(new Rect(bx, by, bw, bh), "Call In", button)) Pick(v.Vehicle, true);
        if (GUI.Button(new Rect(bx, by + bh + 10f * k, bw, bh), "Spawn", button)) Pick(v.Vehicle, false);
    }

    void Pick(Vehicle v, bool flyIn)
    {
        Close();
        choose(v, flyIn);
    }

    float stylesFor = -1f;

    void MakeStyles(float k)
    {
        if (Mathf.Approximately(stylesFor, k) && title != null) return;
        stylesFor = k;
        panelTex ??= Solid(new Color(0.10f, 0.11f, 0.16f, 0.94f));
        cardTex ??= Solid(new Color(0.18f, 0.20f, 0.28f, 1f));
        accentTex ??= Solid(new Color(1.00f, 0.78f, 0.15f, 1f));
        var btn = Solid(new Color(0.95f, 0.30f, 0.22f, 1f));
        var btnHover = Solid(new Color(1.00f, 0.45f, 0.30f, 1f));
        var btnDown = Solid(new Color(0.75f, 0.20f, 0.15f, 1f));
        var grey = Solid(new Color(0.30f, 0.32f, 0.40f, 1f));
        var greyHover = Solid(new Color(0.40f, 0.42f, 0.52f, 1f));

        int F(float px) => Mathf.RoundToInt(px * k);
        title = Label(F(30), FontStyle.Bold, Color.white);
        sub = Label(F(15), FontStyle.Normal, new Color(0.78f, 0.80f, 0.88f));
        name = Label(F(24), FontStyle.Bold, new Color(1.00f, 0.82f, 0.25f));
        seats = Label(F(16), FontStyle.Bold, new Color(0.55f, 0.85f, 1.00f));
        body = Label(F(15), FontStyle.Normal, new Color(0.88f, 0.89f, 0.94f));
        button = Button(F(20), btn, btnHover, btnDown);
        close = Button(F(14), grey, greyHover, grey);
    }

    static GUIStyle Label(int size, FontStyle style, Color color)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, wordWrap = true, richText = false };
        s.normal.textColor = color;
        return s;
    }

    static GUIStyle Button(int size, Texture2D normal, Texture2D hover, Texture2D down)
    {
        var s = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        s.normal.background = normal; s.normal.textColor = Color.white;
        s.hover.background = hover; s.hover.textColor = Color.white;
        s.active.background = down; s.active.textColor = Color.white;
        s.focused.background = normal; s.focused.textColor = Color.white;
        return s;
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
