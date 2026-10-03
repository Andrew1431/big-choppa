using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace BigChoppa;

internal enum InputBackend { Auto, Rewired, Unity }

internal static class ChoppaConfig
{
    // Controls
    public static ConfigEntry<KeyCode> CollectiveUpKey, CollectiveDownKey, PedalLeftKey, PedalRightKey;
    public static ConfigEntry<KeyCode> SpawnKey, EnterExitKey, ResetKey, CameraKey, FreeLookKey, HudKey;
    public static ConfigEntry<float> MouseSensitivity;
    public static ConfigEntry<bool> InvertPitch, InvertRoll;
    public static ConfigEntry<InputBackend> Backend;
    public static ConfigEntry<bool> RequireCursorLock;

    // Flight
    public static ConfigEntry<float> MaxLiftG, CollectiveRate, MaxPitchRollRate, YawRate, ControlResponse;
    public static ConfigEntry<float> MouseSmoothing, AutoLevel, Weathervane, SpinUpSeconds;
    public static ConfigEntry<float> ForwardDrag, SideDrag, VerticalDrag, LinearDrag, Mass, CenterOfMassHeight;
    public static ConfigEntry<bool> CollectiveSpringBack, HoverTiltCompensation;
    public static ConfigEntry<float> CollectiveReturnRate;

    // Crashing
    public static ConfigEntry<bool> BreakApart;
    public static ConfigEntry<float> CrashDeltaV, CrashBlast, DebrisSeconds, CrashStunRadius;

    // Model / seat / camera
    public static ConfigEntry<float> HeliScale, SeatHeightOffset, SeatForwardOffset, EnterDistance;
    public static ConfigEntry<float> ChaseDistance, ChaseHeight, CockpitEyeHeight;
    public static ConfigEntry<bool> SitWhileFlying;
    public static ConfigEntry<int> ChoppaLayer;

    // Lights
    public static ConfigEntry<float> HeadlightIntensity, HeadlightRange, HeadlightAngle, HeadlightTilt, GlowIntensity;

    // Audio
    public static ConfigEntry<float> AudioVolume, AudioMaxDistance;

    // Pilot's Logbook
    public static ConfigEntry<bool> LogbookEnabled;
    public static ConfigEntry<string> LogbookInstallId;

    // Networking
    public static ConfigEntry<bool> NetEnabled;
    public static ConfigEntry<float> NetSendRate, NetInterpDelay;

    // Debug
    public static ConfigEntry<bool> ShowHud, VerboseLogging;
#if DEVBUILD
    public static ConfigEntry<bool> DevAutoHost;
#endif

    // Bump when adding a migration below.
    const int CurrentConfigVersion = 1;

    public static void Bind(ConfigFile cfg)
    {
        const string c = "Controls";
        CollectiveUpKey = cfg.Bind(c, "CollectiveUp", KeyCode.W, "Hold to raise collective (more lift).");
        CollectiveDownKey = cfg.Bind(c, "CollectiveDown", KeyCode.S, "Hold to lower collective (less lift).");
        PedalLeftKey = cfg.Bind(c, "PedalLeft", KeyCode.A, "Left anti-torque pedal (yaw left).");
        PedalRightKey = cfg.Bind(c, "PedalRight", KeyCode.D, "Right anti-torque pedal (yaw right).");
        SpawnKey = cfg.Bind(c, "Spawn", KeyCode.F8, "Spawn a choppa in front of you (replaces your previous one if nobody is in it).");
        EnterExitKey = cfg.Bind(c, "EnterExit", KeyCode.G, "Board or leave the choppa.");
        ResetKey = cfg.Bind(c, "ResetUpright", KeyCode.F9, "Flip the choppa back upright where it is.");
        CameraKey = cfg.Bind(c, "ToggleCamera", KeyCode.V, "Switch between chase and cockpit camera.");
        FreeLookKey = cfg.Bind(c, "FreeLook", KeyCode.LeftAlt, "Hold to look around with the mouse instead of flying with it.");
        HudKey = cfg.Bind(c, "ToggleHud", KeyCode.F10, "Show/hide the flight HUD.");
        MouseSensitivity = cfg.Bind(c, "MouseSensitivity", 0.3f, "Degrees of pitch/roll per unit of mouse movement. Mouse forward = nose down, mouse left = roll left.");
        InvertPitch = cfg.Bind(c, "InvertPitch", false, "Swap mouse forward/back for pitch.");
        InvertRoll = cfg.Bind(c, "InvertRoll", false, "Swap mouse left/right for roll.");
        Backend = cfg.Bind(c, "InputBackend", InputBackend.Auto, "Where to read keys/mouse from. Auto tries the game's Rewired input first, then Unity's legacy Input.");
        RequireCursorLock = cfg.Bind(c, "RequireCursorLock", true, "Ignore flight inputs while the cursor is unlocked (menus open).");

        const string f = "Flight";
        MaxLiftG = cfg.Bind(f, "MaxLiftG", 2.0f, "Lift at full collective as a multiple of gravity. 2.0 means hover at ~50% collective.");
        CollectiveRate = cfg.Bind(f, "CollectiveRate", 0.45f, "How fast holding W/S moves the collective (fraction of full range per second).");
        MaxPitchRollRate = cfg.Bind(f, "MaxPitchRollRate", 90f, "Cap on pitch/roll rotation speed (deg/s).");
        YawRate = cfg.Bind(f, "YawRate", 65f, "Yaw speed with a pedal fully held (deg/s).");
        ControlResponse = cfg.Bind(f, "ControlResponse", 6f, "How snappily the airframe reaches the commanded rotation rate. Higher = twitchier.");
        MouseSmoothing = cfg.Bind(f, "MouseSmoothing", 0.08f, "Seconds of smoothing on mouse cyclic input.");
        AutoLevel = cfg.Bind(f, "AutoLevel", 0.0f, "Self-levelling tendency. 0 = none (Arma-like, holds whatever attitude you set). Try 0.3 for an easier ride.");
        Weathervane = cfg.Bind(f, "Weathervane", 0.5f, "How strongly the tail swings the nose into the direction of travel at speed.");
        SpinUpSeconds = cfg.Bind(f, "SpinUpSeconds", 3f, "Rotor spin up/down time when you board/leave.");
        ForwardDrag = cfg.Bind(f, "ForwardDrag", 0.003f, "Quadratic drag along the nose. Lower = higher top speed.");
        SideDrag = cfg.Bind(f, "SideDrag", 0.02f, "Quadratic drag sideways.");
        VerticalDrag = cfg.Bind(f, "VerticalDrag", 0.04f, "Quadratic drag up/down.");
        LinearDrag = cfg.Bind(f, "LinearDrag", 0.15f, "Low-speed drag in all directions (stops endless drifting).");
        Mass = cfg.Bind(f, "Mass", 600f, "Rigidbody mass. Mostly affects how it shoves things it bumps into.");
        CenterOfMassHeight = cfg.Bind(f, "CenterOfMassHeight", 1.8f, "Height (m, before Scale) of the point the choppa pivots around. Higher = swings like it hangs from the rotor; lower = tips like a bottom-heavy toy.");
        CollectiveSpringBack = cfg.Bind(f, "CollectiveSpringBack", true, "Release W/S and the collective returns to hover (or to idle when sitting on the ground). Off = it stays where you leave it.");
        CollectiveReturnRate = cfg.Bind(f, "CollectiveReturnRate", 1.0f, "How fast the collective springs back when released (fraction of full range per second).");
        HoverTiltCompensation = cfg.Bind(f, "HoverTiltCompensation", true, "Spring-back point adds a bit of lift when tilted so forward flight roughly holds altitude.");

        const string x = "Crashing";
        BreakApart = cfg.Bind(x, "BreakApart", true, "Hard impacts smash the choppa into pieces.");
        CrashDeltaV = cfg.Bind(x, "CrashSpeed", 10f, "Sudden speed change (m/s in one physics step) that counts as a crash. Higher = sturdier.");
        CrashBlast = cfg.Bind(x, "CrashBlast", 8f, "How hard the pieces fly apart.");
        DebrisSeconds = cfg.Bind(x, "DebrisSeconds", 30f, "How long wreckage lies around. 0 = forever.");
        CrashStunRadius = cfg.Bind(x, "StunRadius", 6f, "Your character gets knocked down if within this distance of a crash (pilot always is).");

        const string m = "Model";
        HeliScale = cfg.Bind(m, "Scale", 1.0f, "Overall size of the choppa.");
        SeatHeightOffset = cfg.Bind(m, "SeatHeightOffset", 0.0f, "Raise/lower where you sit (metres, before Scale).");
        SeatForwardOffset = cfg.Bind(m, "SeatForwardOffset", 0.0f, "Move where you sit forward/back (metres, before Scale).");
        EnterDistance = cfg.Bind(m, "EnterDistance", 5f, "How close you need to be to the nearest part of the choppa to board.");
        ChaseDistance = cfg.Bind(m, "ChaseCameraDistance", 12f, "Chase camera distance behind the choppa.");
        ChaseHeight = cfg.Bind(m, "ChaseCameraHeight", 3.5f, "Chase camera height above the choppa.");
        CockpitEyeHeight = cfg.Bind(m, "CockpitEyeHeightOffset", 0.0f, "Nudge the cockpit camera up/down.");
        SitWhileFlying = cfg.Bind(m, "SitWhileFlying", true, "Ask the game to put your character in its sitting pose while flying.");
        ChoppaLayer = cfg.Bind(m, "PhysicsLayer", -1, "Unity layer for the choppa's colliders. -1 = auto-detect one that collides with the ground (see LogOutput.log).");

        const string li = "Lights";
        HeadlightIntensity = cfg.Bind(li, "HeadlightIntensity", 1f, "Headlight brightness. Small changes go a long way. 0 = off.");
        HeadlightRange = cfg.Bind(li, "HeadlightRange", 250f, "How far the headlight reaches (metres).");
        HeadlightAngle = cfg.Bind(li, "HeadlightAngle", 40f, "Width of the headlight cone (degrees).");
        HeadlightTilt = cfg.Bind(li, "HeadlightTilt", 6f, "How far the headlight points below the nose (degrees).");
        GlowIntensity = cfg.Bind(li, "CabinGlow", 2f, "Faint always-on cabin light so a choppa is visible in the dark. 0 = off.");

        const string a = "Audio";
        AudioVolume = cfg.Bind(a, "Volume", 0.8f, "Choppa sound volume (rotor, motor, bonks). 0 = silent.");
        AudioMaxDistance = cfg.Bind(a, "MaxHearingDistance", 250f, "How far away you can hear a choppa (metres).");

        const string l = "Pilot Logbook"; // BepInEx forbids ' in section names
        LogbookEnabled = cfg.Bind(l, "Enabled", true, "Send a few anonymous flight stats (spawns, boardings, flight time/height/speed) so the author can see how the choppa gets used. No names, Steam IDs or locations. Set to false to turn it off completely.");
        LogbookInstallId = cfg.Bind(l, "AnonymousId", "", "Random id generated on first use so your flights can be told apart from other people's. Not linked to you. Delete it to get a new one.");

        const string n = "Networking";
        NetEnabled = cfg.Bind(n, "Enabled", true, "Share choppas with other players. Everyone in the lobby (host included) needs the mod; a host without it may kick you when you join.");
        NetSendRate = cfg.Bind(n, "SendRate", 20f, "Position updates per second sent for a choppa you're flying.");
        NetInterpDelay = cfg.Bind(n, "InterpolationDelay", 0.15f, "Seconds other players' choppas are shown in the past so their motion stays smooth. Raise if they stutter.");

        const string d = "Debug";
        ShowHud = cfg.Bind(d, "ShowHud", true, "Show the flight HUD while flying.");
        VerboseLogging = cfg.Bind(d, "VerboseLogging", false, "Log extra detail to LogOutput.log and show a network status line on screen.");

#if DEVBUILD
        DevAutoHost = cfg.Bind("Dev", "AutoHost", false, "Dev builds only: click through the menus and host your most recent save on launch. Hold Shift during startup to skip.");
#endif

        Migrate(cfg);
    }

    // Edits to the .cfg (by hand or a mod manager's config editor) apply without restarting. Most settings are read
    // every frame; ones used when a choppa is built (model, seats) apply to the next spawn.
    static ConfigFile file;
    static FileSystemWatcher watcher;
    static long lastChangeMs = -1;

    public static void WatchForEdits(ConfigFile cfg)
    {
        file = cfg;
        try
        {
            watcher = new FileSystemWatcher(Path.GetDirectoryName(cfg.ConfigFilePath), Path.GetFileName(cfg.ConfigFilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            // Fires on a worker thread; the reload itself happens in Tick on the main thread.
            watcher.Changed += (_, _) => lastChangeMs = Environment.TickCount64;
            watcher.Created += (_, _) => lastChangeMs = Environment.TickCount64;
            watcher.Renamed += (_, _) => lastChangeMs = Environment.TickCount64;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) { Plugin.L.LogWarning($"Config live reload unavailable: {e.Message}"); }
    }

    public static void Tick()
    {
        long changed = lastChangeMs;
        if (changed < 0 || Environment.TickCount64 - changed < 300) return; // editors often write in several steps
        lastChangeMs = -1;
        bool save = file.SaveOnConfigSet;
        file.SaveOnConfigSet = false; // don't rewrite the file we're reading
        try { file.Reload(); Plugin.L.LogInfo("Config reloaded from disk."); }
        catch (Exception e) { Plugin.L.LogWarning($"Config reload failed: {e.Message}"); }
        finally { file.SaveOnConfigSet = save; }
    }

    // Existing .cfg files keep their saved values, so a changed default only reaches fresh installs. Each migration moves
    // a setting to its new default if it still holds the old one (i.e. the player never touched it).
    static void Migrate(ConfigFile cfg)
    {
        var version = cfg.Bind("Internal", "ConfigVersion", 0, "Used to apply updated defaults when the mod updates. Don't edit.");
        int from = version.Value;
        if (from >= CurrentConfigVersion) return;

        if (from < 1) Upgrade(CenterOfMassHeight, 1.4f);

        version.Value = CurrentConfigVersion;
    }

    static void Upgrade<T>(ConfigEntry<T> entry, T oldDefault)
    {
        if (!EqualityComparer<T>.Default.Equals(entry.Value, oldDefault)) return;
        entry.Value = (T)entry.DefaultValue;
        Plugin.L.LogInfo($"Config: {entry.Definition} updated to new default {entry.Value} (was the old default {oldDefault}).");
    }
}
