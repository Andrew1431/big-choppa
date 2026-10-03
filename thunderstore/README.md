# Big Choppa

A goofy, playground-style toy helicopter for Big Walk, flown with collective, pedals and cyclic.

It has three seats, the pilot plus a two-person bench, and everyone in the lobby sees the same choppas.

**Everyone in the lobby needs the mod, including the host.**

## Controls

| Key | Action |
| --- | --- |
| F8 | Spawn a choppa in front of you (replaces your old one if it's empty) |
| G | Get in / get out. Takes the pilot seat if it's free, otherwise a passenger seat |
| W / S | Collective up / down. Springs back to hover when released, or to idle on the ground |
| A / D | Pedals: turn left / right |
| Mouse | Cyclic: forward = nose down, back = nose up, left / right = roll |
| Left Alt (hold) | Look around instead of flying with the mouse |
| V | Chase / cockpit camera |
| F9 | Flip the choppa back upright |
| F10 | Toggle the HUD |

## Flying tips

- Hold **W** until the rotor lifts you off, then let go. The collective settles at hover.
- **Push the mouse forward** to tip the nose down and fly forward. Pull back to slow down.
- The choppa doesn't level itself. Whatever angle you leave it at, it keeps. Roll and pitch back yourself.
- Want it easier? Set `AutoLevel` to `0.3` in the config.

## Config

Every key and tuning value is in `BepInEx/config/com.andrew1431.bigchoppa.cfg`. With r2modman or Gale you can edit it from the mod manager's config editor. The file appears after you've launched the game once with the mod.

Highlights:
- Controls: `MouseSensitivity`, `InvertPitch`, `InvertRoll`, every key
- Flight: `AutoLevel`, `ControlResponse`, `YawRate`, `MaxLiftG`, drag values
- Audio: `Volume`, `MaxHearingDistance`

## Multiplayer

The pilot's game simulates the choppa and everyone else sees a smoothed copy and hears the same sounds. If the pilot leaves the lobby, the host takes the choppa over.

If the host doesn't have the mod, you may be kicked when you join. Set `Networking.Enabled = false` to keep choppas to yourself.

## Source and bug reports

https://github.com/Andrew1431/big-choppa
