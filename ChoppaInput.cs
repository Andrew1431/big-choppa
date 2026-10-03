using System;
using System.Collections.Generic;
using Rewired;
using UnityEngine;

namespace BigChoppa;

// Reads raw keys/mouse. The game routes input through Rewired; Unity's legacy Input may or may not be enabled,
// so Auto tries Rewired first and permanently falls back if it throws.
internal static class ChoppaInput
{
    static bool rewiredBroken, unityBroken;
    static readonly Dictionary<KeyCode, bool> prev = new(), cur = new();

    public static string ActiveBackend { get; private set; } = "none";

    public static void Tick()
    {
        foreach (var k in new List<KeyCode>(cur.Keys))
        {
            prev[k] = cur[k];
            cur[k] = RawKey(k);
        }
    }

    public static bool Held(KeyCode k)
    {
        if (k == KeyCode.None) return false;
        if (!cur.ContainsKey(k)) { cur[k] = RawKey(k); prev[k] = cur[k]; }
        return cur[k];
    }

    public static bool Pressed(KeyCode k) => Held(k) && !prev[k];

    public static float Axis(KeyCode negative, KeyCode positive) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);

    public static Vector2 MouseDelta()
    {
        if (UseRewired())
        {
            try
            {
                if (ReInput.isReady)
                {
                    var mouse = ReInput.controllers.Mouse;
                    ActiveBackend = "Rewired";
                    return new Vector2(mouse.GetAxisRaw(0), mouse.GetAxisRaw(1));
                }
            }
            catch (Exception e) { MarkRewiredBroken(e); }
        }
        if (UseUnity())
        {
            try
            {
                ActiveBackend = "Unity";
                return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            }
            catch (Exception e) { MarkUnityBroken(e); }
        }
        return Vector2.zero;
    }

    static bool RawKey(KeyCode k)
    {
        if (UseRewired())
        {
            try
            {
                if (ReInput.isReady && ReInput.controllers.keyboardEnabled)
                    return ReInput.controllers.Keyboard.GetKey(k);
            }
            catch (Exception e) { MarkRewiredBroken(e); }
        }
        if (UseUnity())
        {
            try { return Input.GetKey(k); }
            catch (Exception e) { MarkUnityBroken(e); }
        }
        return false;
    }

    static bool UseRewired() => !rewiredBroken && ChoppaConfig.Backend.Value != InputBackend.Unity;
    static bool UseUnity() => !unityBroken && ChoppaConfig.Backend.Value != InputBackend.Rewired;

    static void MarkRewiredBroken(Exception e)
    {
        rewiredBroken = true;
        Plugin.L.LogWarning($"Rewired input failed, falling back to Unity Input: {e.Message}");
    }

    static void MarkUnityBroken(Exception e)
    {
        unityBroken = true;
        Plugin.L.LogWarning($"Unity legacy Input failed: {e.Message}");
    }
}
