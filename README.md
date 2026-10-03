# Big Choppa

A goofy playground-style helicopter for Big Walk with Arma-style flight controls (BepInEx 6 IL2CPP mod).

## Build and install

Close Big Walk, then in PowerShell in this folder:

```powershell
.\build.ps1
```

(If scripts are blocked: `Set-ExecutionPolicy -Scope Process Bypass` first.)

This installs `BigChoppa.dll` to `BepInEx\plugins\BigChoppa`. The old `BepInEx\plugins\BigWalkHello` folder can be deleted.

## Flying

| Key | Action |
| --- | --- |
| F8 | Spawn your choppa in front of you (moves it if it exists) |
| G | Board / leave (within ~5 m) |
| W / S | Collective up / down (springs back to hover when released, or to idle on the ground) |
| A / D | Pedals: yaw left / right |
| Mouse | Cyclic: forward = nose down, back = nose up, left/right = roll |
| Left Alt (hold) | Free look instead of cyclic |
| V | Chase / cockpit camera |
| F9 | Flip upright |
| F10 | Toggle HUD |

There's no self-levelling by default. The choppa holds whatever attitude you leave it in, and lift always points out of the rotor, so you move by tilting. Rotors spin up when you board and down when you leave. Leave mid-air at your own risk.

Hit something hard enough and it breaks into bouncing toy pieces. The pilot gets thrown out and knocked down, and so does anyone standing too close. Tune it under `[Crashing]`.

## Config

All keys and tuning values are in `BepInEx\config\com.ryan1.bigwalk.bigchoppa.cfg`. The file appears after the first launch with the mod and is re-read at startup. Useful knobs:

- `Controls.MouseSensitivity`, `InvertPitch`, `InvertRoll`
- `Flight.AutoLevel` (0 = Arma-like; 0.3 = friendlier), `ControlResponse`, `YawRate`, `MaxLiftG`, drags
- `Model.Scale`, `SeatHeightOffset`, `SeatForwardOffset`, `SitWhileFlying`
- `Debug.VerboseLogging` shows raw mouse values on the HUD and logs seating/camera details to `BepInEx\LogOutput.log`

## Multiplayer status

Local only. Each player with the mod spawns their own choppa. Other players see your character flying around in a sitting pose, but not the choppa.

## Files

- `Plugin.cs`: entry point, registers injected types
- `ChoppaConfig.cs`: all config entries
- `ChoppaInput.cs`: key/mouse reading (Rewired first, Unity Input fallback)
- `Helicopter.cs`: flight model and rotor/eye animation
- `HeliModel.cs`: the primitive-built model
- `ChoppaMaterials.cs`: URP-compatible materials
- `ChoppaManager.cs`: local player lookup, spawn, boarding, camera, HUD, crash handling
- `ChoppaCrash.cs`: break-apart debris
