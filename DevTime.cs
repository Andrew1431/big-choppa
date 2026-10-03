#if DEVBUILD
using Enviro;
using UnityEngine;

namespace BigChoppa;

// Dev builds only: , and . step the time of day back/forward an hour (for testing the lights at night).
// Host-side only in practice: the game syncs the sky from the host, so a client's change may get overwritten.
internal static class DevTime
{
    static float verifyAt = -1f, target;

    public static void Tick()
    {
        int step = (ChoppaInput.Pressed(KeyCode.Period) ? 1 : 0) - (ChoppaInput.Pressed(KeyCode.Comma) ? 1 : 0);
        if (step != 0) Shift(step);

        // If the game snapped the time back, pin it with its own fixed-time override instead.
        if (verifyAt > 0f && Time.unscaledTime > verifyAt)
        {
            verifyAt = -1f;
            var enviro = Object.FindFirstObjectByType<EnviroManager>();
            if (enviro == null || enviro.Time == null) return;
            float now = enviro.Time.GetTimeOfDay();
            if (Mathf.Abs(Mathf.DeltaAngle(now * 15f, target * 15f)) > 7.5f)
            {
                SkyManager.SetFixedTime(target);
                Plugin.L.LogInfo($"Dev time: game reset it to {now:0.00}; pinned at {target:0.00} with SkyManager.SetFixedTime (time no longer flows).");
            }
        }
    }

    static void Shift(int hours)
    {
        var enviro = Object.FindFirstObjectByType<EnviroManager>();
        if (enviro == null || enviro.Time == null) { Plugin.L.LogInfo("Dev time: no Enviro sky in this scene."); return; }
        float before = enviro.Time.GetTimeOfDay();
        target = Mathf.Repeat(before + hours, 24f);
        enviro.Time.SetTimeOfDay(target);
        verifyAt = Time.unscaledTime + 0.5f;
        Plugin.L.LogInfo($"Dev time: {before:0.00} -> {target:0.00}");
    }
}
#endif
