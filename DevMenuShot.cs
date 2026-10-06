#if DEVBUILD
using System.IO;
using UnityEngine;

namespace BigChoppa;

// Dev builds only: screenshots the F8 menu without anyone at the keyboard, for iterating on its look. Triggered by
// BepInEx\config\bigchoppa-menushot.txt: line 1 = output .png path, optional line 2 = card index to draw hovered,
// optional line 3 = "quit" to close the game afterwards. The file is deleted once the shot is taken.
internal static class DevMenuShot
{
    static string trigger;
    static float readyAt = -1f, shotAt = -1f, doneAt = -1f;
    static bool quit;

    public static void Tick(ChoppaMenu menu)
    {
        trigger ??= Path.Combine(BepInEx.Paths.ConfigPath, "bigchoppa-menushot.txt");
        float now = Time.unscaledTime;
        if (readyAt < 0f)
        {
            if (!ChoppaNet.Ready || !File.Exists(trigger)) return;
            readyAt = now + 6f; // let the world settle
            return;
        }
        if (shotAt < 0f && now >= readyAt)
        {
            var lines = File.ReadAllLines(trigger);
            menu.DevHover = lines.Length > 1 && int.TryParse(lines[1].Trim(), out var h) ? h : -1;
            quit = lines.Length > 2 && lines[2].Trim() == "quit";
            menu.Open();
            shotAt = now + 2.5f;
            return;
        }
        if (doneAt < 0f && shotAt > 0f && now >= shotAt)
        {
            var path = File.ReadAllLines(trigger)[0].Trim();
            ScreenCapture.CaptureScreenshot(path);
            Plugin.L.LogInfo($"Dev menu shot: {path}");
            doneAt = now + 1.5f;
            return;
        }
        if (doneAt > 0f && now >= doneAt)
        {
            doneAt = float.MaxValue;
            menu.DevHover = -1;
            menu.Close();
            File.Delete(trigger);
            if (quit) Application.Quit();
        }
    }
}
#endif
