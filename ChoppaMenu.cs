using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigChoppa;

// The F8 chooser, dressed like a toy-box game menu (Animal Crossing / Super Battle Golf): thick ink outlines with hard
// drop shadows, slowly panning dots and stripes, sticker cards that straighten and lift under the mouse, chunky
// buttons. A card per entry in Vehicles.Catalog (live turntable preview, seat chips, description, Call In / Spawn),
// laid out in a grid that wraps and scrolls as vehicles are added. Number keys pick too (Shift = Spawn).
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
#if DEVBUILD
    float diagnoseAt;
    public int DevHover = -1; // DevMenuShot: draw this card as if the mouse were over it
#endif

    static readonly Color Ink = new(0.231f, 0.180f, 0.145f);
    static readonly Color Cream = new(1f, 0.957f, 0.863f);
    static readonly Color DotBig = new(0.965f, 0.894f, 0.737f);
    static readonly Color DotSmall = new(0.984f, 0.922f, 0.796f);
    static readonly Color Body = new(0.42f, 0.337f, 0.275f);
    static readonly Color Teal = new(0.243f, 0.549f, 0.525f);
    static readonly Color Yellow = new(1f, 0.824f, 0.247f);
    static readonly Color Green = new(0.424f, 0.796f, 0.373f);
    static readonly Color GreenDark = new(0.176f, 0.42f, 0.153f);
    static readonly Color Coral = new(1f, 0.478f, 0.42f);
    static readonly Color Blue = new(0.353f, 0.722f, 0.91f);
    static readonly Color[] TagColors = { Coral, Blue, Green, Yellow };
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
        hover.Clear();
        title = null; // re-pick fonts: the game may have loaded a nicer one since last time
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
        foreach (var p in previews.Values) p.SetLive(true);
#if DEVBUILD
        diagnoseAt = Time.unscaledTime + 1f;
#endif
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        foreach (var p in previews.Values) p.SetLive(false);
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
            hover[v.Vehicle] = Mathf.MoveTowards(hover.TryGetValue(v.Vehicle, out var h) ? h : 0f, on ? 1f : 0f, dt * 4f);
            if (!previews.TryGetValue(v.Vehicle, out var p) || !p.Alive) continue;
            yaw[v.Vehicle] = (yaw.TryGetValue(v.Vehicle, out var y) ? y : 0f) + dt * Mathf.Lerp(12f, 55f, hover[v.Vehicle]);
            p.Turn(yaw[v.Vehicle]);
        }
#if DEVBUILD
        if (diagnoseAt > 0f && Time.unscaledTime > diagnoseAt)
        {
            diagnoseAt = 0f;
            foreach (var kv in previews) kv.Value.Diagnose(kv.Key);
            LogFonts();
        }
#endif
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

    float k, now;
    GUIStyle title, statusText, badge, tagText, cardName, chip, body, buttonText, keyText, closeText, bubble, bubbleSmall, fallbackMark;
    Font display, text;

    public void Draw()
    {
        if (!IsOpen) return;
        var keepColor = GUI.color;
        var keepMatrix = GUI.matrix;
        try { DrawMenu(); }
        catch (Exception e)
        {
            Plugin.L.LogError($"Choppa chooser: {e}");
            Close();
        }
        finally
        {
            GUI.color = keepColor;
            GUI.matrix = keepMatrix;
        }
    }

    void DrawMenu()
    {
        float nk = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
        if (title == null || !Mathf.Approximately(nk, k)) MakeStyles(nk);

        now = Time.unscaledTime;
        float t = now - openedAt;
        float fade = ChoppaUi.Ease(t / 0.18f);
        var list = Vehicles.Catalog;

        // Backdrop: the world tinted teal under slowly drifting stripes.
        var screen = new Rect(0f, 0f, Screen.width, Screen.height);
        GUI.color = new Color(1f, 1f, 1f, fade);
        GUI.DrawTexture(screen, ChoppaUi.Solid(ChoppaUi.A(Teal, 0.84f)));
        ChoppaUi.Tile(screen, ChoppaUi.Stripes(new Color(1f, 1f, 1f, 0.07f), new Color(1f, 1f, 1f, 0f)), 240f * k, new Vector2(-now * 10f * k, 0f));

        // Layout. The ribbon hangs over the panel's top edge; the speech bubble sits under it.
        float pad = 46f * k, top = 96f * k, bottom = 44f * k, gap = 44f * k, ribbonOver = 60f * k, footGap = 30f * k, footH = 80f * k;
        float w = Mathf.Min(1100f * k, Screen.width - 32f);
        float inner = w - pad * 2f;
        int cols = Mathf.Clamp(Mathf.FloorToInt((inner + gap) / (420f * k + gap)), 1, Mathf.Max(1, list.Length));
        float cardW = (inner - gap * (cols - 1)) / cols;
        float stageH = (cardW - 36f * k) * PreviewH / PreviewW;
        float cardH = stageH + 316f * k;
        int rows = Mathf.CeilToInt(list.Length / (float)cols);
        float listH = rows * cardH + (rows - 1) * gap;
        float chrome = ribbonOver + top + bottom + 12f * k + footGap + footH;
        float viewH = Mathf.Min(listH, Screen.height - 32f - chrome);
        float panelH = top + viewH + bottom;
        float total = ribbonOver + panelH + footGap + footH;
        var panel = new Rect((Screen.width - w) / 2f, (Screen.height - total) / 2f + ribbonOver, w, panelH);

        // Everything below pops in with a little overshoot.
        float pop = Mathf.LerpUnclamped(0.86f, 1f, ChoppaUi.Back(t / 0.4f));
        GUIUtility.ScaleAroundPivot(new Vector2(pop, pop), new Vector2(Screen.width / 2f, Screen.height / 2f));

        // Panel: cream, ink outline, hard shadow, panning polka dots.
        float R = 48f * k, line = 5f * k;
        ChoppaUi.Draw(Down(panel, 12f * k), InkBox(R));
        ChoppaUi.Draw(panel, ChoppaUi.Box(R, Cream, Cream, Ink, line));
        ChoppaUi.Tile(Inset(panel, line + R * 0.3f), ChoppaUi.Dots(Cream, DotBig, DotSmall, 6f, 3.2f), 136f * k, new Vector2(now * 5f * k, now * 2.5f * k));

        // Cards (scroll when they don't fit). The clip reaches past the cards so the pointer, ring and shadows show.
        float x = panel.x + pad, y = panel.y + top;
        bool scrolls = listH > viewH + 0.5f;
        float m = 34f * k, mTop = scrolls ? 10f * k : 62f * k, mBot = scrolls ? 10f * k : 26f * k;
        var view = new Rect(x, y, inner, viewH);
        var clip = new Rect(x - m, y - mTop, inner + m * 2f, viewH + mTop + mBot);
        var content = new Rect(0f, 0f, inner + m * 2f, listH + mTop + mBot);
        scroll = GUI.BeginScrollView(clip, scroll, content, false, false, GUIStyle.none, GUIStyle.none);
        Vector2 mouse = Event.current != null ? Event.current.mousePosition : new Vector2(-1f, -1f);
        Vehicle? over = null;
        for (int i = 0; i < list.Length; i++)
        {
            var r = new Rect(m + (i % cols) * (cardW + gap), mTop + (i / cols) * (cardH + gap), cardW, cardH);
            if (r.Contains(mouse) && mouse.y >= scroll.y + mTop && mouse.y <= scroll.y + mTop + viewH) over = list[i].Vehicle;
            float appear = (t - 0.1f - i * 0.08f) / 0.4f;
            DrawCard(i, list[i], new Rect(r.x, r.y + (1f - ChoppaUi.Back(appear)) * 30f * k, r.width, r.height), stageH,
                fade * ChoppaUi.Ease(appear * 1.6f));
        }
        GUI.EndScrollView();
        GUI.color = new Color(1f, 1f, 1f, fade);
#if DEVBUILD
        if (DevHover >= 0 && DevHover < list.Length) over = list[DevHover].Vehicle;
#endif
        if (Event.current == null || Event.current.type == EventType.Repaint) hovered = over;
        if (scrolls)
        {
            var track = new Rect(view.xMax + pad * 0.42f, view.y, 8f * k, viewH);
            ChoppaUi.Draw(track, ChoppaUi.Box(4f * k, ChoppaUi.A(Ink, 0.12f), ChoppaUi.A(Ink, 0.12f)));
            float thumbH = Mathf.Max(40f * k, viewH * viewH / listH);
            float thumbY = view.y + (viewH - thumbH) * Mathf.Clamp01(scroll.y / (listH - viewH));
            ChoppaUi.Draw(new Rect(track.x - 2f * k, thumbY, track.width + 4f * k, thumbH), ChoppaUi.Box(6f * k, Yellow, Yellow, Ink, 2.5f * k));
        }
        DrawRibbon(panel);
        DrawStatus(panel);
        DrawClose(new Rect(panel.xMax - 96f * k, panel.y - 34f * k, 76f * k, 76f * k));
        DrawBubble(new Rect(panel.x, panel.yMax + 12f * k + footGap, panel.width, footH), list.Length);
    }

    void DrawRibbon(Rect panel)
    {
        const string s = "Choose your choppa!";
        var size = title.CalcSize(new GUIContent(s));
        float rw = size.x + 120f * k, rh = 82f * k;
        var r = new Rect(panel.center.x - rw / 2f, panel.y - rh * 0.62f, rw, rh);
        var keep = GUI.matrix;
        GUIUtility.RotateAroundPivot(-1.5f, r.center);
        ChoppaUi.Draw(Down(r, 8f * k), InkBox(26f * k));
        ChoppaUi.Draw(r, ChoppaUi.Box(26f * k, Yellow, Yellow, Ink, 5f * k));
        ChoppaUi.OutlinedLabel(new Rect(r.x, r.y - 2f * k, r.width, r.height), s, title, Color.white, Ink, 4.5f * k);
        GUI.matrix = keep;
    }

    void DrawStatus(Rect panel)
    {
        string s = status?.Invoke() ?? "";
        if (s.Length == 0) return;
        var size = statusText.CalcSize(new GUIContent(s));
        float ph = 42f * k, pw = size.x + 56f * k;
        // Stuck on the bottom-left corner, clear of the ribbon however wide that gets.
        var r = new Rect(panel.x + 34f * k, panel.yMax - ph * 0.45f, pw, ph);
        var keep = GUI.matrix;
        GUIUtility.RotateAroundPivot(-2.5f, r.center);
        ChoppaUi.Draw(Down(r, 5f * k), InkBox(ChoppaUi.PillRadius(ph)));
        ChoppaUi.Draw(r, ChoppaUi.Box(ChoppaUi.PillRadius(ph), Color.white, Color.white, Ink, 4f * k));
        float d = 15f * k * (1f + 0.12f * Mathf.Sin(now * 4f));
        var dot = new Rect(r.x + 24f * k - d / 2f, r.center.y - d / 2f, d, d);
        ChoppaUi.Draw(dot, ChoppaUi.Box(ChoppaUi.PillRadius(d) + 1f, Green, Green, Ink, 2.5f * k));
        GUI.Label(new Rect(r.x + 38f * k, r.y, size.x + 8f, ph), s, statusText);
        GUI.matrix = keep;
    }

    void DrawClose(Rect r)
    {
        bool over = Event.current != null && r.Contains(Event.current.mousePosition);
        var face = over ? ChoppaUi.Grow(r, 3f * k) : r;
        float rad = ChoppaUi.PillRadius(face.height);
        ChoppaUi.Draw(Down(face, 6f * k), InkBox(rad));
        ChoppaUi.Draw(face, ChoppaUi.Box(rad, over ? Color.Lerp(Coral, Color.white, 0.15f) : Coral, over ? Color.Lerp(Coral, Color.white, 0.15f) : Coral, Ink, 5f * k));
        var c = new Vector2(face.center.x, face.center.y - 9f * k);
        var bar = new Rect(c.x - 14f * k, c.y - 2.75f * k, 28f * k, 5.5f * k);
        var keep = GUI.matrix;
        foreach (float a in new[] { 45f, -45f })
        {
            GUIUtility.RotateAroundPivot(a, c);
            ChoppaUi.Draw(bar, ChoppaUi.Box(2.5f * k, Color.white, Color.white));
            GUI.matrix = keep;
        }
        GUI.Label(new Rect(face.x, face.center.y + 6f * k, face.width, 20f * k), ChoppaConfig.SpawnKey.Value.ToString(), closeText);
        if (GUI.Button(face, "", GUIStyle.none)) Close();
    }

    void DrawBubble(Rect area, int count)
    {
        string line1 = "<b><color=#2F7D3A>Call in</color></b> flies it over and lands it near you.  " +
                       "<b><color=#C2410C>Spawn</color></b> drops it right in front of you!";
        string keys = count > 1 ? $"1-{Mathf.Min(count, 9)}" : "1";
        string line2 = $"Your old one goes if it's empty   ·   {keys} call in   ·   Shift + number spawns   ·   Esc closes";
        float tw = Mathf.Max(bubble.CalcSize(new GUIContent(line1)).x, bubbleSmall.CalcSize(new GUIContent(line2)).x);
        float bw = tw + 60f * k, bh = area.height - 6f * k, mc = 66f * k, gapX = 14f * k;
        float x0 = area.center.x - (mc + gapX + bw) / 2f;

        // Mascot: a tiny choppa in a yellow badge, wobbling.
        var mr = new Rect(x0, area.y + bh - mc, mc, mc);
        var keep = GUI.matrix;
        GUIUtility.RotateAroundPivot(4f * Mathf.Sin(now * 2.6f), mr.center);
        ChoppaUi.Draw(Down(mr, 5f * k), InkBox(ChoppaUi.PillRadius(mc)));
        ChoppaUi.Draw(mr, ChoppaUi.Box(ChoppaUi.PillRadius(mc), Yellow, Yellow, Ink, 5f * k));
        var c = mr.center;
        ChoppaUi.Draw(new Rect(c.x + 4f * k, c.y + 1f * k, 17f * k, 6f * k), ChoppaUi.Box(3f * k, Ink, Ink));
        ChoppaUi.Draw(new Rect(c.x - 2f * k, c.y - 13f * k, 4f * k, 8f * k), ChoppaUi.Box(1.5f * k, Ink, Ink));
        float spin = Mathf.Abs(Mathf.Cos(now * 9f));
        ChoppaUi.Draw(new Rect(c.x - 21f * k * spin, c.y - 16f * k, 42f * k * spin + 1f, 5f * k), ChoppaUi.Box(2.5f * k, Ink, Ink));
        ChoppaUi.Draw(new Rect(c.x - 16f * k, c.y - 8f * k, 26f * k, 21f * k), ChoppaUi.Box(9f * k, Color.white, Color.white, Ink, 3f * k));
        GUI.matrix = keep;

        // Speech bubble.
        var br = new Rect(mr.xMax + gapX, area.y, bw, bh);
        ChoppaUi.Draw(Down(br, 5f * k), InkBox(26f * k));
        ChoppaUi.Draw(br, ChoppaUi.Box(26f * k, Color.white, Color.white, Ink, 4f * k));
        GUI.Label(new Rect(br.x + 30f * k, br.y + 10f * k, tw + 8f, 30f * k), line1, bubble);
        GUI.Label(new Rect(br.x + 30f * k, br.y + 42f * k, tw + 8f, 22f * k), line2, bubbleSmall);
    }

    void DrawCard(int i, VehicleInfo v, Rect r, float stageH, float alpha)
    {
        float h = hover.TryGetValue(v.Vehicle, out var hh) ? hh : 0f;
        float hb = ChoppaUi.Back(h);
        GUI.color = new Color(1f, 1f, 1f, alpha);

        // Stickers sit a little crooked and straighten up, lift and grow under the mouse.
        var keep = GUI.matrix;
        r.y -= 6f * k * hb;
        GUIUtility.RotateAroundPivot((i % 2 == 0 ? -1.6f : 1.6f) * (1f - Mathf.Clamp01(hb)), r.center);
        GUIUtility.ScaleAroundPivot(Vector2.one * (1f + 0.025f * hb), r.center);

        float R = 36f * k, line = 5f * k;
        ChoppaUi.Draw(Down(r, 9f * k), InkBox(R));
        if (h > 0.01f)
        {
            GUI.color = new Color(1f, 1f, 1f, alpha * Mathf.Clamp01(h * 2f));
            var ring = ChoppaUi.Grow(r, 7f * k);
            ChoppaUi.Draw(Down(ring, 10f * k), InkBox(R + 7f * k));
            ChoppaUi.Draw(ring, ChoppaUi.Box(R + 7f * k, Yellow, Yellow));
            // Bouncing pointer.
            float bob = 8f * k * Mathf.Abs(Mathf.Sin(now * 3.5f));
            GUI.DrawTexture(new Rect(r.center.x - 24f * k, r.y - 56f * k + bob, 48f * k, 48f * k), ChoppaUi.Triangle(Yellow, Ink, 9f));
            GUI.color = new Color(1f, 1f, 1f, alpha);
        }
        ChoppaUi.Draw(r, ChoppaUi.Box(R, Color.white, Color.white, Ink, line));

        float p = 18f * k;
        var stage = new Rect(r.x + p, r.y + p, r.width - p * 2f, stageH);
        DrawStage(i, v, stage, h);

        // Name, chips, description.
        float y = stage.yMax + 12f * k;
        GUI.Label(new Rect(r.x + p + 4f * k, y, r.width - p * 2f, 46f * k), v.Name, cardName);
        y += 50f * k;
        float cx = r.x + p;
        foreach (var c in v.Chips)
        {
            var label = c.ToLowerInvariant();
            float cw = chip.CalcSize(new GUIContent(label)).x + 26f * k, ch = 30f * k;
            var pill = new Rect(cx, y, cw, ch);
            ChoppaUi.Draw(pill, ChoppaUi.Box(ChoppaUi.PillRadius(ch), Cream, Cream, Ink, 3f * k));
            GUI.Label(pill, label, chip);
            cx += cw + 8f * k;
        }
        y += 42f * k;
        GUI.Label(new Rect(r.x + p + 4f * k, y, r.width - p * 2f - 8f * k, 104f * k), v.Description, body);

        // Buttons.
        float bh = 62f * k, lip = 6f * k, bgap = 12f * k;
        float avail = r.width - p * 2f - bgap, callW = avail * 1.3f / 2.3f;
        var call = new Rect(r.x + p, r.yMax - p - lip - bh, callW, bh);
        var spawn = new Rect(call.xMax + bgap, call.y, avail - callW, bh);
        string key = i < 9 ? (i + 1).ToString() : null;
        bool callIn = ChunkyButton(call, "Call in!", key, Green, true);
        bool drop = ChunkyButton(spawn, "Spawn", key != null ? "Shift " + key : null, Color.white, false);
        GUI.matrix = keep;
        if (callIn) Pick(v.Vehicle, true);
        else if (drop) Pick(v.Vehicle, false);
    }

    void DrawStage(int i, VehicleInfo v, Rect s, float h)
    {
        float R = 28f * k, line = 4f * k;
        var tint = Color.Lerp(v.Accent, Color.white, 0.55f);
        ChoppaUi.Draw(s, ChoppaUi.Box(R, tint, tint, Ink, line));
        ChoppaUi.Tile(Inset(s, line + R * 0.3f), ChoppaUi.Stripes(new Color(1f, 1f, 1f, 0.42f), new Color(1f, 1f, 1f, 0f)), 88f * k,
            new Vector2(now * 6f * k, 0f));

        var inner = Inset(s, line);
        if (previews.TryGetValue(v.Vehicle, out var prev) && prev.Alive)
        {
            var sh = prev.ShadowAt;
            float sw = inner.width * Mathf.Clamp(prev.ShadowWidth * 0.8f, 0.2f, 0.9f);
            var shadow = new Rect(inner.x + inner.width * sh.x - sw / 2f, inner.y + inner.height * sh.y - 12f * k, sw, 24f * k);
            GUI.DrawTexture(shadow, ChoppaUi.Radial(ChoppaUi.A(Ink, 0.35f), ChoppaUi.A(Ink, 0f), 1.2f));
            GUI.DrawTexture(inner, prev.Texture, ScaleMode.ScaleToFit, true);
        }
        else GUI.Label(inner, v.Name.Substring(0, 1), fallbackMark);

        // Number badge and a wobbling tag sticker.
        var nb = new Rect(s.x + 12f * k, s.y + 12f * k, 44f * k, 44f * k);
        ChoppaUi.Draw(nb, ChoppaUi.Box(ChoppaUi.PillRadius(nb.height), Color.white, Color.white, Ink, 4f * k));
        GUI.Label(nb, (i + 1).ToString(), badge);

        float right = s.xMax - 14f * k;
        if (!string.IsNullOrEmpty(v.Tag))
            right = Sticker(right, s.y + 14f * k, v.Tag, TagColors[i % TagColors.Length], Color.white, 3f * Mathf.Sin(now * 2.6f + i));
        if (lastPick == v.Vehicle) Sticker(right - 8f * k, s.y + 14f * k, "last pick", Cream, Ink, 0f);
    }

    // Right-aligned outlined sticker ending at `right`, tilted `angle` degrees; returns its left edge.
    float Sticker(float right, float y, string label, Color fill, Color ink, float angle)
    {
        float sw = tagText.CalcSize(new GUIContent(label)).x + 28f * k, sh = 34f * k;
        var r = new Rect(right - sw, y, sw, sh);
        var keep = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, r.center);
        ChoppaUi.Draw(r, ChoppaUi.Box(14f * k, fill, fill, Ink, 4f * k));
        tagText.normal.textColor = ink;
        GUI.Label(r, label, tagText);
        GUI.matrix = keep;
        return r.x;
    }

    // Chunky outlined button sitting on a hard ink lip; lifts under the mouse. Label and keycap are centred together.
    bool ChunkyButton(Rect r, string label, string key, Color fill, bool primary)
    {
        float lip = 6f * k, R = 22f * k;
        bool over = Event.current != null && new Rect(r.x, r.y, r.width, r.height + lip).Contains(Event.current.mousePosition);
        var face = over ? Down(r, -2f * k) : r;
        var f = over ? Color.Lerp(fill, Color.white, primary ? 0.15f : 0f) : fill;
        if (over && !primary) f = new Color(1f, 0.98f, 0.92f);
        ChoppaUi.Draw(Down(r, lip), InkBox(R));
        ChoppaUi.Draw(face, ChoppaUi.Box(R, f, f, Ink, 4f * k));

        float lw = buttonText.CalcSize(new GUIContent(label)).x;
        float kw = 0f, kh = 0f;
        if (key != null)
        {
            kw = primary ? 34f * k : keyText.CalcSize(new GUIContent(key)).x + 18f * k;
            kh = primary ? 34f * k : 28f * k;
        }
        float total = lw + (key != null ? 12f * k + kw : 0f);
        float x = face.center.x - total / 2f;
        var lr = new Rect(x, face.y - 1f * k, lw + 8f, face.height);
        if (primary) ChoppaUi.OutlinedLabel(lr, label, buttonText, Color.white, GreenDark, 1.6f * k);
        else
        {
            buttonText.normal.textColor = Ink;
            GUI.Label(lr, label, buttonText);
        }
        if (key != null)
        {
            var kr = new Rect(x + lw + 12f * k, face.center.y - kh / 2f, kw, kh);
            float kr0 = primary ? ChoppaUi.PillRadius(kh) : 9f * k;
            ChoppaUi.Draw(kr, ChoppaUi.Box(kr0, primary ? Color.white : Cream, primary ? Color.white : Cream, Ink, 3f * k));
            GUI.Label(kr, key, keyText);
        }
        return GUI.Button(new Rect(r.x, r.y - 2f * k, r.width, r.height + lip + 2f * k), "", GUIStyle.none);
    }

    GUIStyle InkBox(float radius) => ChoppaUi.Box(radius, Ink, Ink);
    static Rect Down(Rect r, float by) => new(r.x, r.y + by, r.width, r.height);
    static Rect Inset(Rect r, float by) => ChoppaUi.Grow(r, -by);

    void MakeStyles(float nk)
    {
        k = nk;
        // A mod can't load a .ttf of its own (IL2CPP strips Font(path)) and Windows has no rounded face, so borrow the
        // game's own Manrope (loaded with its UI), falling back to Windows fonts.
        display = ChoppaUi.LoadedFont("Manrope-ExtraBold")
                  ?? ChoppaUi.OsFont("Arial Rounded MT Bold", "Segoe UI Black", "Segoe UI Bold", "Segoe UI Semibold", "Arial");
        text = ChoppaUi.LoadedFont("Manrope-Medium") ?? ChoppaUi.OsFont("Segoe UI Semibold", "Segoe UI", "Arial");

        int F(float px) => Mathf.Max(8, Mathf.RoundToInt(px * k));
        GUIStyle S(Font f, float size, FontStyle fs, Color c, TextAnchor a = TextAnchor.UpperLeft, bool wrap = false, bool rich = false)
        {
            var s = new GUIStyle { font = f, fontSize = F(size), fontStyle = fs, alignment = a, wordWrap = wrap, richText = rich, clipping = TextClipping.Overflow };
            s.normal.textColor = c;
            return s;
        }

        title = S(display, 46, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        statusText = S(display, 17, FontStyle.Bold, Ink, TextAnchor.MiddleLeft);
        badge = S(display, 22, FontStyle.Bold, Ink, TextAnchor.MiddleCenter);
        tagText = S(display, 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        cardName = S(display, 36, FontStyle.Bold, Ink, TextAnchor.MiddleLeft);
        chip = S(display, 14, FontStyle.Bold, Ink, TextAnchor.MiddleCenter);
        body = S(text, 16, FontStyle.Bold, Body, TextAnchor.UpperLeft, true);
        buttonText = S(display, 24, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
        keyText = S(display, 15, FontStyle.Bold, Ink, TextAnchor.MiddleCenter);
        closeText = S(display, 14, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        bubble = S(display, 19, FontStyle.Normal, Ink, TextAnchor.MiddleLeft, false, true);
        bubbleSmall = S(text, 14, FontStyle.Bold, Body, TextAnchor.MiddleLeft);
        fallbackMark = S(display, 90, FontStyle.Bold, ChoppaUi.A(Ink, 0.15f), TextAnchor.MiddleCenter);
    }

#if DEVBUILD
    static bool fontsLogged;

    // Which fonts the game has loaded, to pick a rounded one for the menu (IL2CPP strips Font(path), so a .ttf of our
    // own can't be loaded).
    void LogFonts()
    {
        if (fontsLogged) return;
        fontsLogged = true;
        try
        {
            var names = new List<string>();
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null) names.Add(f.dynamic ? f.name + " (dynamic)" : f.name);
            var tmp = new List<string>();
            foreach (var a in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>())
                if (a != null) tmp.Add($"{a.name} <- {(a.sourceFontFile != null ? a.sourceFontFile.name : "no source")}");
            Plugin.L.LogInfo($"Menu fonts: display = {(display != null ? display.name : "Unity default")}; loaded Fonts = [{string.Join(", ", names)}]; " +
                             $"TMP assets = [{string.Join(", ", tmp)}]");
        }
        catch (Exception e) { Plugin.L.LogInfo($"Menu fonts: listing failed: {e.Message}"); }
    }
#endif
}
