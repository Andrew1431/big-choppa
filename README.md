# Big Choppa

[![Big Choppa Pilot's Logbook: live flight stats](https://big-choppa.hartwigdev.ca/card.svg)](https://thunderstore.io/c/big-walk/p/Andrew1431/BigChoppa/)

A goofy playground-style helicopter for Big Walk with Arma-style flight controls (BepInEx 6 IL2CPP mod).

> **Choppa pockets don't count as "held".** The six side pockets take anything a backpack takes, but the game only
> saves items players are actively holding. Anything left in a pocket when the game ends isn't kept.

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

All keys and tuning values are in `BepInEx\config\com.andrew1431.bigchoppa.cfg`. The file appears after the first launch with the mod and is re-read at startup. Useful knobs:

- `Controls.MouseSensitivity`, `InvertPitch`, `InvertRoll`
- `Flight.AutoLevel` (0 = Arma-like; 0.3 = friendlier), `ControlResponse`, `YawRate`, `MaxLiftG`, drags
- `Lights.HeadlightIntensity`, `HeadlightRange`, `HeadlightAngle`, `HeadlightTilt`, `CabinGlow`
- `Model.Scale`, `SeatHeightOffset`, `SeatForwardOffset`, `SitWhileFlying`
- `Debug.VerboseLogging` shows raw mouse values on the HUD and logs seating/camera details to `BepInEx\LogOutput.log`

## Multiplayer status

Shared. Everyone in the lobby, host included, needs the mod (a host without it may kick a modded client on join).

- Choppas, their seats and crashes are shared. Each choppa has 3 seats: the pilot plus a two-person rear bench. Press G next to a choppa to take the pilot seat if it's free, otherwise a bench seat.
- The PC of whoever last took the pilot seat simulates that choppa and streams its position; everyone else sees a smoothed copy (`Networking.InterpolationDelay`).
- Crashes break every copy apart and stun anyone sitting in it or standing within `StunRadius`, on their own PC.
- If a choppa's owner leaves, the host takes it over.
- One parked choppa per player: F8 replaces yours if nobody is in it.
- Messages ride on the game's Mirror connection under a custom message id; nothing extra to open or forward.

## Files

- `Plugin.cs`: entry point, registers injected types
- `ChoppaConfig.cs`: all config entries
- `ChoppaInput.cs`: key/mouse reading (Rewired first, Unity Input fallback)
- `Helicopter.cs`: flight model and rotor/eye animation
- `HeliModel.cs`: the primitive-built model
- `ChoppaMaterials.cs`: URP-compatible materials
- `ChoppaLights.cs`: nav lights, beacons, strobes, headlight, cabin glow
- `ChoppaPockets.cs`: six side pockets built on the game's `PropHome` (backpack-style item slots)
- `ChoppaManager.cs`: local player lookup, choppa list sync, boarding/seats, camera, HUD, crash handling
- `ChoppaNet.cs`: raw Mirror transport, handshake, offline loopback
- `ChoppaServer.cs`: host-side authority (spawns, seats, ownership, relaying)
- `ChoppaCrash.cs`: break-apart debris
- `ChoppaAudio.cs`: synthesised rotor/motor loops and plastic bonks (no audio files)
- `ChoppaBonker.cs`: plays a bonk when a piece (or a hard landing) hits something
- `ChoppaLogbook.cs`: "Pilot's Logbook", anonymous PostHog events (opt-out via `[Pilot Logbook] Enabled`)
