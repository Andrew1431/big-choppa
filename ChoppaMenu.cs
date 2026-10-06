using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigChoppa;

// The F8 chooser: a card per entry in Vehicles.Catalog (live turntable preview, seat chips, description, Call In /
// Spawn), laid out in a grid that wraps and scrolls as vehicles are added. Number keys pick too (Shift = Spawn).
// Plain IMGUI with Rects only (GUILayout is unreliable under IL2CPP); every shape comes from ChoppaUi.
internal sealed class ChoppaMenu
{
    public bool IsOpen { get; private set; }

    readonly Action<Vehicle, bool> choose; // (vehicle, flyIn)
    readonly Func<string> status;
    Vector2 scroll;
    float openedAt;
    static Vehicle? lastPick;

    readonly Dictionary<Vehicle, ChoppaPreview> previews = new();
    readonly Dictionary<Vehicle, float> yaw = new(), hover = new();
    Vehicle? hovered;

    static readonly Color Ink = new(0.93f, 0.94f, 0.97f);
    static readonly Color Muted = new(0.62f, 0.65f, 0.74f);
    static readonly Color Faint = new(0.42f, 0.45f, 0.54f);
    static readonly Color Gold = new(1.00f, 0.80f, 0.18f);
    static readonly Color Coral = new(1.00f, 0.42f, 0.36f);
    const int PreviewW = 768, PreviewH = 400;

    public ChoppaMenu(Action<Vehicle, bool> choose, Func<string> status)
    {
        this.choose = choose;
        this.status = status;
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        scroll = Vector2.zero;
        openedAt = Time.unscaledTime;
        hovered = null;
        // Menu mode stops the game's look/move input while the mouse is free.
        try { ControlsManager.SetMenuMode(true); } catch (Exception e) { Plugin.L.LogWarning($"Menu mode: {e.Message}"); }
        try { CursorManager.SetFree(); } catch { }
        FreeCursor();
        foreach (var v in Vehicles.Catalog)
            if (!previews.TryGetValue(v.Vehicle, out var p) || !p.Alive && !p.Failed)
            {
                p?.Dispose();
                previews[v.Vehicle] = ChoppaPreview.Create(v.Vehicle, PreviewW, PreviewH);
                yaw[v.Vehicle] = -25f;
            }
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        try { ControlsManager.SetMenuMode(false); } catch (Exception e) { Plugin.L.LogWarning($"Menu mode: {e.Message}"); }
        try { CursorManager.SetLocked(); }
        catch { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }

    // Every frame while open (from Update): keys, cursor, and turning the previews.
    public void Tick()
    {
        if (!IsOpen) return;
        FreeCursor();
        if (ChoppaInput.Pressed(KeyCode.Escape)) { Close(); return; }

        var list = Vehicles.Catalog;
        bool shift = ChoppaInput.Held(KeyCode.LeftShift) || ChoppaInput.Held(KeyCode.RightShift);
        for (int i = 0; i < list.Length && i < 9; i++)
            if (ChoppaInput.Pressed(KeyCode.Alpha1 + i) || ChoppaInput.Pressed(KeyCode.Keypad1 + i))
            {
                Pick(list[i].Vehicle, !shift);
                return;
            }

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        foreach (var v in list)
        {
            bool on = hovered == v.Vehicle;
            hover[v.Vehicle] = Mathf.MoveTowards(hover.TryGetValue(v.Vehicle, out var h) ? h : 0f, on ? 1f : 0f, dt * 6f);
            if (!previews.TryGetValue(v.Vehicle, out var p) || !p.Alive) continue;
            yaw[v.Vehicle] = (yaw.TryGetValue(v.Vehicle, out var y) ? y : 0f) + dt * Mathf.Lerp(9f, 55f, hover[v.Vehicle]);
            p.Render(yaw[v.Vehicle]);
        }
    }

    static void FreeCursor()
    {
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
    }

    void Pick(Vehicle v, bool flyIn)
    {
        lastPick = v;
        Close();
        choose(v, flyIn);
    }

    // ---------- drawing ----------

    float k;
    GUIStyle eyebrow, title, sub, statusText, index, tag, cardName, chip, body, keycap, keycapDark, hint, primaryText, secondaryText, fallbackMark;
    Font display, text, mono;

    public void Draw()
    {
        if (!IsOpen) return;
        var keep = GUI.color;
        try { DrawMenu(); }
        catch (Exception e)
        {
            Plugin.L.LogError($"Choppa chooser: {e}");
            Close();
        }
        finally { GUI.color = keep; }
    }

    void DrawMenu()
    {
        float nk = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
        if (eyebrow == null || !Mathf.Approximately(nk, k)) MakeStyles(nk);

        float t = Time.unscaledTime - openedAt;
        float fade = ChoppaUi.Ease(t / 0.22f);
        var list = Vehicles.Catalog;

        // Backdrop: dim plus vignette.
        var screen = new Rect(0f, 0f, Screen.width, Screen.height);
        GUI.color = new Color(1f, 1f, 1f, fade);
        GUI.DrawTexture(screen, ChoppaUi.Solid(new Color(0.02f, 0.03f, 0.06f, 0.45f)));
        GUI.DrawTexture(Grow(screen, Screen.width * 0.25f), ChoppaUi.Radial(new Color(0f, 0f, 0f, 0f), new Color(0.01f, 0.01f, 0.03f, 0.75f), 1.6f));

        // Layout.
        float pad = 32f * k, gap = 20f * k, headH = 128f * k, footH = 64f * k;
        float w = Mathf.Min(1060f * k, Screen.width - 32f);
        float inner = w - pad * 2f;
        int cols = Mathf.Clamp(Mathf.FloorToInt((inner + gap) / (400f * k + gap)), 1, Mathf.Max(1, list.Length));
        float cardW = (inner - gap * (cols - 1)) / cols;
        float stageH = (cardW - 32f * k) * PreviewH / PreviewW;
        float cardH = stageH + 276f * k;
        int rows = Mathf.CeilToInt(list.Length / (float)cols);
        float listH = rows * cardH + (rows - 1) * gap;
        float viewH = Mathf.Min(listH, Screen.height - 48f - headH - footH - pad * 2f);
        float h = pad * 2f + headH + viewH + footH;
        var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f + (1f - fade) * 28f * k, w, h);

        // Panel.
        ChoppaUi.Draw(ChoppaUi.Grow(panel, 48f * k), ChoppaUi.Shadow(22f * k, 48f * k, new Color(0f, 0f, 0f, 0.65f)));
        ChoppaUi.Draw(panel, ChoppaUi.Box(22f * k, new Color(0.105f, 0.115f, 0.16f, 0.97f), new Color(0.06f, 0.065f, 0.095f, 0.98f),
            new Color(1f, 1f, 1f, 0.09f), Mathf.Max(1f, k), 0.035f));
        // Accent glow bleeding in from the top edge.
        GUI.DrawTexture(new Rect(panel.x + panel.width * 0.15f, panel.y - 70f * k, panel.width * 0.7f, 160f * k),
            ChoppaUi.Radial(ChoppaUi.A(Gold, 0.10f), ChoppaUi.A(Gold, 0f), 1.2f));
        GUI.DrawTexture(new Rect(panel.x + 22f * k, panel.y, panel.width - 44f * k, Mathf.Max(1f, 2f * k)),
            ChoppaUi.HGradient(ChoppaUi.A(Gold, 0f), ChoppaUi.A(Gold, 0.85f)));
        GUI.DrawTexture(new Rect(panel.center.x, panel.y, panel.width / 2f - 22f * k, Mathf.Max(1f, 2f * k)),
            ChoppaUi.HGradient(ChoppaUi.A(Coral, 0.85f), ChoppaUi.A(Coral, 0f)));

        float x = panel.x + pad, y = panel.y + pad;
        DrawHeader(new Rect(x, y, inner, headH));
        y += headH;

        // Cards (scroll when they don't fit; our own thin scrollbar instead of the default skin's).
        bool scrolls = listH > viewH + 0.5f;
        float barW = scrolls ? 14f * k : 0f;
        var view = new Rect(x, y, inner, viewH);
        var content = new Rect(0f, 0f, inner - barW, listH);
        if (scrolls) { cardW = (content.width - gap * (cols - 1)) / cols; }
        scroll = GUI.BeginScrollView(view, scroll, content, false, false, GUIStyle.none, GUIStyle.none);
        Vector2 mouse = Event.current != null ? Event.current.mousePosition : new Vector2(-1f, -1f);
        Vehicle? over = null;
        for (int i = 0; i < list.Length; i++)
        {
            var r = new Rect((i % cols) * (cardW + gap), (i / cols) * (cardH + gap), cardW, cardH);
            if (r.Contains(mouse) && mouse.y >= scroll.y && mouse.y <= scroll.y + viewH) over = list[i].Vehicle;
            float appear = ChoppaUi.Ease((t - 0.05f - i * 0.06f) / 0.3f);
            DrawCard(i, list[i], new Rect(r.x, r.y + (1f - appear) * 22f * k, r.width, r.height), stageH, fade * appear);
        }
        GUI.EndScrollView();
        GUI.color = new Color(1f, 1f, 1f, fade);
        if (Event.current == null || Event.current.type == EventType.Repaint) hovered = over;
        if (scrolls)
        {
            var track = new Rect(view.xMax - 5f * k, view.y, 4f * k, viewH);
            ChoppaUi.Draw(track, ChoppaUi.Box(2f * k, new Color(1f, 1f, 1f, 0.05f), new Color(1f, 1f, 1f, 0.05f)));
            float thumbH = Mathf.Max(30f * k, viewH * viewH / listH);
            float thumbY = view.y + (viewH - thumbH) * Mathf.Clamp01(scroll.y / (listH - viewH));
            ChoppaUi.Draw(new Rect(track.x, thumbY, track.width, thumbH), ChoppaUi.Box(2f * k, ChoppaUi.A(Gold, 0.7f), ChoppaUi.A(Gold, 0.5f)));
        }
        y += viewH;

        DrawFooter(new Rect(x, y, inner, footH), list.Length);
    }

    void DrawHeader(Rect r)
    {
        GUI.Label(new Rect(r.x, r.y - 2f * k, r.width, 20f * k), "B I G   C H O P P A    /    H A N G A R", eyebrow);
        GUI.Label(new Rect(r.x, r.y + 18f * k, r.width, 52f * k), "Choose your choppa", title);
        GUI.Label(new Rect(r.x, r.y + 68f * k, r.width * 0.75f, 24f * k),
            "Call In flies it over and lands it near you.  Spawn drops it right in front of you.  Your old one goes if it's empty.", sub);

        // Status pill, top right.
        string s = status?.Invoke() ?? "";
        if (s.Length > 0)
        {
            var size = statusText.CalcSize(new GUIContent(s));
            float pw = size.x + 40f * k, ph = 30f * k;
            var pill = new Rect(r.xMax - pw, r.y + 4f * k, pw, ph);
            ChoppaUi.Draw(pill, ChoppaUi.Box(ph / 2f, new Color(1f, 1f, 1f, 0.05f), new Color(1f, 1f, 1f, 0.03f), new Color(1f, 1f, 1f, 0.10f), Mathf.Max(1f, k)));
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            var dot = new Rect(pill.x + 14f * k, pill.center.y - 4f * k, 8f * k, 8f * k);
            GUI.DrawTexture(ChoppaUi.Grow(dot, 6f * k), ChoppaUi.Radial(new Color(0.3f, 1f, 0.55f, 0.5f * pulse), new Color(0.3f, 1f, 0.55f, 0f)));
            ChoppaUi.Draw(dot, ChoppaUi.Box(4f * k, new Color(0.45f, 1f, 0.6f), new Color(0.25f, 0.85f, 0.45f)));
            GUI.Label(new Rect(pill.x + 30f * k, pill.y, size.x + 4f, ph), s, statusText);
        }

        GUI.DrawTexture(new Rect(r.x, r.yMax - 14f * k, r.width, Mathf.Max(1f, k)),
            ChoppaUi.HGradient(new Color(1f, 1f, 1f, 0.14f), new Color(1f, 1f, 1f, 0f)));
    }

    void DrawCard(int i, VehicleInfo v, Rect r, float stageH, float alpha)
    {
        float hv = hover.TryGetValue(v.Vehicle, out var hh) ? ChoppaUi.Ease(hh) : 0f;
        var accent = v.Accent;
        r.y -= 4f * k * hv;
        GUI.color = new Color(1f, 1f, 1f, alpha);

        // Card body + hover glow.
        ChoppaUi.Draw(ChoppaUi.Grow(r, 26f * k), ChoppaUi.Shadow(16f * k, 26f * k, new Color(0f, 0f, 0f, 0.45f)));
        if (hv > 0.01f)
        {
            GUI.color = new Color(1f, 1f, 1f, alpha * hv);
            ChoppaUi.Draw(ChoppaUi.Grow(r, 22f * k), ChoppaUi.Shadow(16f * k, 22f * k, ChoppaUi.A(accent, 0.28f)));
            GUI.color = new Color(1f, 1f, 1f, alpha);
        }
        ChoppaUi.Draw(r, ChoppaUi.Box(16f * k, new Color(0.15f, 0.165f, 0.225f, 1f), new Color(0.10f, 0.11f, 0.155f, 1f),
            new Color(1f, 1f, 1f, 0.08f), Mathf.Max(1f, k), 0.04f));
        if (hv > 0.01f)
        {
            GUI.color = new Color(1f, 1f, 1f, alpha * hv);
            ChoppaUi.Draw(r, ChoppaUi.Box(16f * k, new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f), ChoppaUi.A(accent, 0.75f), Mathf.Max(1.5f, 1.5f * k)));
            GUI.color = new Color(1f, 1f, 1f, alpha);
        }

        float p = 16f * k;
        var stage = new Rect(r.x + p, r.y + p, r.width - p * 2f, stageH);
        DrawStage(i, v, stage, alpha, hv);

        // Name, chips, description.
        float y = stage.yMax + 16f * k;
        GUI.Label(new Rect(r.x + p + 2f * k, y, r.width - p * 2f, 36f * k), v.Name, cardName);
        y += 42f * k;
        float cx = r.x + p;
        for (int c = 0; c < v.Chips.Length; c++)
        {
            var label = v.Chips[c];
            float cw = chip.CalcSize(new GUIContent(label)).x + 22f * k, ch = 26f * k;
            var pill = new Rect(cx, y, cw, ch);
            if (c == 0) ChoppaUi.Draw(pill, ChoppaUi.Box(ch / 2f, ChoppaUi.A(accent, 0.24f), ChoppaUi.A(accent, 0.16f), ChoppaUi.A(accent, 0.55f), Mathf.Max(1f, k)));
            else ChoppaUi.Draw(pill, ChoppaUi.Box(ch / 2f, new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0.04f), new Color(1f, 1f, 1f, 0.10f), Mathf.Max(1f, k)));
            chip.normal.textColor = c == 0 ? Color.Lerp(accent, Color.white, 0.35f) : Muted;
            GUI.Label(pill, label, chip);
            cx += cw + 8f * k;
        }
        y += 40f * k;
        GUI.Label(new Rect(r.x + p + 2f * k, y, r.width - p * 2f - 4f * k, 76f * k), v.Description, body);

        // Buttons.
        float bh = 52f * k, bgap = 12f * k;
        float bw = (r.width - p * 2f - bgap) / 2f;
        var call = new Rect(r.x + p, r.yMax - p - bh, bw, bh);
        var spawn = new Rect(call.xMax + bgap, call.y, bw, bh);
        string key = i < 9 ? (i + 1).ToString() : null;
        if (PrimaryButton(call, "CALL IN", key, accent)) Pick(v.Vehicle, true);
        if (SecondaryButton(spawn, "SPAWN", key != null ? "SHIFT " + key : null)) Pick(v.Vehicle, false);
    }

    void DrawStage(int i, VehicleInfo v, Rect s, float alpha, float hv)
    {
        var accent = v.Accent;
        ChoppaUi.Draw(s, ChoppaUi.Box(12f * k, new Color(0.075f, 0.085f, 0.12f, 1f), new Color(0.05f, 0.055f, 0.08f, 1f),
            new Color(1f, 1f, 1f, 0.06f), Mathf.Max(1f, k)));
        // Spotlight and floor grid.
        GUI.DrawTexture(new Rect(s.x + s.width * 0.08f, s.y - s.height * 0.1f, s.width * 0.84f, s.height * 1.1f),
            ChoppaUi.Radial(ChoppaUi.A(accent, 0.20f + 0.10f * hv), ChoppaUi.A(accent, 0f), 1.3f));
        var line = ChoppaUi.HGradient(ChoppaUi.A(Color.white, 0f), ChoppaUi.A(Color.white, 0.06f));
        var lineR = ChoppaUi.HGradient(ChoppaUi.A(Color.white, 0.06f), ChoppaUi.A(Color.white, 0f));
        for (int g = 0; g < 6; g++)
        {
            float f = g / 5f;
            float gy = s.y + s.height * (0.62f + 0.36f * f * f);
            float half = s.width * (0.5f - 0.04f);
            GUI.DrawTexture(new Rect(s.center.x - half, gy, half, 1f), line);
            GUI.DrawTexture(new Rect(s.center.x, gy, half, 1f), lineR);
        }

        if (previews.TryGetValue(v.Vehicle, out var prev) && prev.Alive)
        {
            var sh = prev.ShadowAt;
            float sw = s.width * Mathf.Clamp(prev.ShadowWidth * 0.8f, 0.2f, 0.9f);
            var shadow = new Rect(s.x + s.width * sh.x - sw / 2f, s.y + s.height * sh.y - 14f * k, sw, 28f * k);
            GUI.DrawTexture(shadow, ChoppaUi.Radial(new Color(0f, 0f, 0f, 0.55f), new Color(0f, 0f, 0f, 0f), 1.2f));
            GUI.DrawTexture(s, prev.Texture, ScaleMode.ScaleToFit, true);
        }
        else GUI.Label(s, v.Name.Substring(0, 1), fallbackMark);

        // Index and tag.
        GUI.Label(new Rect(s.x + 12f * k, s.y + 8f * k, 80f * k, 20f * k), (i + 1).ToString("00"), index);
        float tx = s.xMax - 12f * k;
        if (!string.IsNullOrEmpty(v.Tag)) tx = Pill(tx, s.y + 10f * k, v.Tag, accent, true);
        if (lastPick == v.Vehicle) Pill(tx - 6f * k, s.y + 10f * k, "LAST PICK", Color.white, false);
    }

    // Right-aligned pill ending at `right`; returns its left edge.
    float Pill(float right, float y, string label, Color c, bool filled)
    {
        float pw = tag.CalcSize(new GUIContent(label)).x + 18f * k, ph = 22f * k;
        var r = new Rect(right - pw, y, pw, ph);
        if (filled) ChoppaUi.Draw(r, ChoppaUi.Box(ph / 2f, Color.Lerp(c, Color.white, 0.2f), c, default, 0f, 0.25f));
        else ChoppaUi.Draw(r, ChoppaUi.Box(ph / 2f, new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0.03f), new Color(1f, 1f, 1f, 0.25f), Mathf.Max(1f, k)));
        tag.normal.textColor = filled ? ChoppaUi.TextOn(c) : Ink;
        GUI.Label(r, label, tag);
        return r.x;
    }

    bool PrimaryButton(Rect r, string label, string key, Color accent)
    {
        bool over = Event.current != null && r.Contains(Event.current.mousePosition);
        var top = Color.Lerp(accent, Color.white, over ? 0.32f : 0.18f);
        var bottom = Color.Lerp(accent, Color.black, over ? 0.05f : 0.18f);
        if (over) ChoppaUi.Draw(ChoppaUi.Grow(r, 14f * k), ChoppaUi.Shadow(10f * k, 14f * k, ChoppaUi.A(accent, 0.45f)));
        ChoppaUi.Draw(r, ChoppaUi.Box(10f * k, top, bottom, ChoppaUi.A(Color.white, 0.35f), Mathf.Max(1f, k), 0.22f));
        primaryText.normal.textColor = ChoppaUi.TextOn(accent);
        return Button(r, label, key, primaryText, keycapDark);
    }

    bool SecondaryButton(Rect r, string label, string key)
    {
        bool over = Event.current != null && r.Contains(Event.current.mousePosition);
        ChoppaUi.Draw(r, ChoppaUi.Box(10f * k, new Color(1f, 1f, 1f, over ? 0.12f : 0.05f), new Color(1f, 1f, 1f, over ? 0.07f : 0.02f),
            new Color(1f, 1f, 1f, over ? 0.45f : 0.22f), Mathf.Max(1f, k)));
        return Button(r, label, key, secondaryText, keycap);
    }

    // Label on the left, keycap on the right; the whole rect is the click target.
    bool Button(Rect r, string label, string key, GUIStyle text, GUIStyle cap)
    {
        float inset = 16f * k;
        GUI.Label(new Rect(r.x + inset, r.y, r.width - inset * 2f, r.height), label, text);
        if (key != null)
        {
            float kw = cap.CalcSize(new GUIContent(key)).x + 12f * k, kh = 22f * k;
            var kr = new Rect(r.xMax - inset - kw, r.center.y - kh / 2f, kw, kh);
            bool dark = cap == keycapDark;
            ChoppaUi.Draw(kr, ChoppaUi.Box(5f * k, dark ? new Color(0f, 0f, 0f, 0.16f) : new Color(1f, 1f, 1f, 0.08f),
                dark ? new Color(0f, 0f, 0f, 0.22f) : new Color(1f, 1f, 1f, 0.04f),
                dark ? new Color(0f, 0f, 0f, 0.25f) : new Color(1f, 1f, 1f, 0.18f), Mathf.Max(1f, k)));
            GUI.Label(kr, key, cap);
        }
        return GUI.Button(r, "", GUIStyle.none);
    }

    void DrawFooter(Rect r, int count)
    {
        float y = r.y + 22f * k, x = r.x;
        x = Hint(x, y, count > 1 ? $"1-{Mathf.Min(count, 9)}" : "1", "Call in");
        x = Hint(x + 18f * k, y, "SHIFT + #", "Spawn");
        Hint(x + 18f * k, y, "ESC", "Close");

        var close = new Rect(r.xMax - 130f * k, r.y + 14f * k, 130f * k, 38f * k);
        if (SecondaryButton(close, "CLOSE", ChoppaConfig.SpawnKey.Value.ToString())) Close();
    }

    float Hint(float x, float y, string key, string what)
    {
        float kw = keycap.CalcSize(new GUIContent(key)).x + 12f * k, kh = 22f * k;
        var kr = new Rect(x, y, kw, kh);
        ChoppaUi.Draw(kr, ChoppaUi.Box(5f * k, new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.04f), new Color(1f, 1f, 1f, 0.18f), Mathf.Max(1f, k)));
        GUI.Label(kr, key, keycap);
        float tw = hint.CalcSize(new GUIContent(what)).x;
        GUI.Label(new Rect(kr.xMax + 8f * k, y, tw + 4f, kh), what, hint);
        return kr.xMax + 8f * k + tw;
    }

    static Rect Grow(Rect r, float by) => ChoppaUi.Grow(r, by);

    void MakeStyles(float nk)
    {
        k = nk;
        display ??= ChoppaUi.OsFont("Bahnschrift", "Segoe UI Semibold", "Segoe UI", "Arial");
        text ??= ChoppaUi.OsFont("Segoe UI", "Arial");
        mono ??= ChoppaUi.OsFont("Cascadia Mono", "Consolas", "Lucida Console", "Courier New");

        int F(float px) => Mathf.Max(8, Mathf.RoundToInt(px * k));
        GUIStyle S(Font f, float size, FontStyle fs, Color c, TextAnchor a = TextAnchor.UpperLeft, bool wrap = false)
        {
            var s = new GUIStyle { font = f, fontSize = F(size), fontStyle = fs, alignment = a, wordWrap = wrap, richText = false, clipping = TextClipping.Clip };
            s.normal.textColor = c;
            return s;
        }

        eyebrow = S(mono, 12, FontStyle.Bold, Gold);
        title = S(display, 38, FontStyle.Bold, Ink);
        sub = S(text, 15, FontStyle.Normal, Muted);
        statusText = S(mono, 12, FontStyle.Bold, Ink, TextAnchor.MiddleLeft);
        index = S(mono, 13, FontStyle.Bold, ChoppaUi.A(Color.white, 0.35f));
        tag = S(mono, 11, FontStyle.Bold, Ink, TextAnchor.MiddleCenter);
        cardName = S(display, 30, FontStyle.Bold, Ink);
        chip = S(mono, 12, FontStyle.Bold, Muted, TextAnchor.MiddleCenter);
        body = S(text, 15, FontStyle.Normal, new Color(0.78f, 0.80f, 0.86f), TextAnchor.UpperLeft, true);
        keycap = S(mono, 11, FontStyle.Bold, Muted, TextAnchor.MiddleCenter);
        keycapDark = S(mono, 11, FontStyle.Bold, new Color(0f, 0f, 0f, 0.6f), TextAnchor.MiddleCenter);
        hint = S(text, 14, FontStyle.Normal, Faint, TextAnchor.MiddleLeft);
        primaryText = S(display, 19, FontStyle.Bold, Color.black, TextAnchor.MiddleLeft);
        secondaryText = S(display, 19, FontStyle.Bold, Ink, TextAnchor.MiddleLeft);
        fallbackMark = S(display, 90, FontStyle.Bold, ChoppaUi.A(Color.white, 0.12f), TextAnchor.MiddleCenter);
    }
}
