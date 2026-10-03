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

    // Audio
    public static ConfigEntry<float> AudioVolume, AudioMaxDistance;

    // Networking
    public static ConfigEntry<bool> NetEnabled;
    public static ConfigEntry<float> NetSendRate, NetInterpDelay;

    // Debug
    public static ConfigEntry<bool> ShowHud, VerboseLogging;

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
        CenterOfMassHeight = cfg.Bind(f, "CenterOfMassHeight", 1.4f, "Height (m, before Scale) of the point the choppa pivots around. Higher = swings like it hangs from the rotor; lower = tips like a bottom-heavy toy.");
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

        const string a = "Audio";
        AudioVolume = cfg.Bind(a, "Volume", 0.8f, "Choppa sound volume (rotor, motor, bonks). 0 = silent.");
        AudioMaxDistance = cfg.Bind(a, "MaxHearingDistance", 250f, "How far away you can hear a choppa (metres).");

        const string n = "Networking";
        NetEnabled = cfg.Bind(n, "Enabled", true, "Share choppas with other players. Everyone in the lobby (host included) needs the mod; a host without it may kick you when you join.");
        NetSendRate = cfg.Bind(n, "SendRate", 20f, "Position updates per second sent for a choppa you're flying.");
        NetInterpDelay = cfg.Bind(n, "InterpolationDelay", 0.15f, "Seconds other players' choppas are shown in the past so their motion stays smooth. Raise if they stutter.");

        const string d = "Debug";
        ShowHud = cfg.Bind(d, "ShowHud", true, "Show the flight HUD while flying.");
        VerboseLogging = cfg.Bind(d, "VerboseLogging", true, "Log extra detail to LogOutput.log (useful while we get this working).");
    }
}
