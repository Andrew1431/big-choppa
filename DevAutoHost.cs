#if DEVBUILD
using UnityEngine;

namespace BigChoppa;

// Dev builds only (build.ps1 defines DEVBUILD, package.ps1 doesn't): walks the main menu to host the most recently
// played save, once per launch. Each step calls the same method the menu's button would.
internal static class DevAutoHost
{
    static bool done;
    static float nextStepAt;
    static string lastStep;
    static int repeats;
    static bool countPicked;

    public static void Tick()
    {
        if (done || !ChoppaConfig.DevAutoHost.Value) return;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) { Finish("skipped (Shift held)"); return; }
        if (Time.unscaledTime < nextStepAt) return;

        // MainMenuManager.instance throws (rather than returning null) outside the main menu.
        var mm = Object.FindFirstObjectByType<MainMenuManager>();
        if (mm == null) { nextStepAt = Time.unscaledTime + 0.5f; return; }

        if (Active(mm.splashMenu)) Step("splash", () => mm.splashMenu.ActionContinue());
        else if (Active(mm.micCheckMenu)) Step("mic check", () => mm.micCheckMenu.ActionContinue());
        else if (Active(mm.titleMenu)) Step("title -> host", () => mm.titleMenu.GoToHostMenu());
        else if (Active(mm.hostMenuSelect)) Step("pick save", () => PickSave(mm.hostMenuSelect));
        else if (Active(mm.hostMenuConfirm)) Step("confirm", () => mm.hostMenuConfirm.ActionStart());
        else if (Active(mm.playerCountMenu) && !countPicked) Step("2 players", () => { mm.playerCountMenu.settingsRow.ActionSelect0(); countPicked = true; });
        else if (Active(mm.playerCountMenu)) Step("play", () => mm.playerCountMenu.ActionProceed());
        else if (Active(mm.loadingMenu)) Finish("loading the world");
        else if (Active(mm.errorMenu)) Finish("stopped: the game showed an error menu");
    }

    static void PickSave(HostMenuSelect select)
    {
        var saves = SaveManager.GetAllSaveDatasInFolder();
        SaveData newest = null;
        if (saves != null)
            foreach (var s in saves)
                if (newest == null || s.lastPlayedTimeAsLong > newest.lastPlayedTimeAsLong) newest = s;

        if (newest == null) { Plugin.L.LogInfo("Dev auto-host: no saves, starting a new game."); select.StartNewGame(); }
        else { Plugin.L.LogInfo($"Dev auto-host: hosting save '{newest.slotName}'."); select.ActionSelectSaveData(newest); }
    }

    static bool Active(Component c) => c != null && c.gameObject.activeInHierarchy;

    static void Step(string name, System.Action action)
    {
        // Menus animate in; a short pause stops us clicking before they're ready. Repeating the same step means it
        // didn't take, so give up rather than loop forever.
        repeats = name == lastStep ? repeats + 1 : 0;
        if (repeats > 10) { Finish($"stuck at '{name}'"); return; }
        if (repeats == 0) Plugin.L.LogInfo($"Dev auto-host: {name}");
        lastStep = name;
        nextStepAt = Time.unscaledTime + 0.5f;
        action();
    }

    static void Finish(string why)
    {
        done = true;
        Plugin.L.LogInfo($"Dev auto-host: {why}.");
    }
}
#endif
