# Big Choppa

![Big Choppa Pilot's Logbook: live flight stats](https://big-choppa.hartwigdev.ca/card.svg)

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
- Lights: `HeadlightIntensity`, `HeadlightRange`, `HeadlightAngle`, `CabinGlow`
- Audio: `Volume`, `MaxHearingDistance`

## Multiplayer

The pilot's game simulates the choppa and everyone else sees a smoothed copy and hears the same sounds. If the pilot leaves the lobby, the host takes the choppa over.

If the host doesn't have the mod, you may be kicked when you join. Set `Networking.Enabled = false` to keep choppas to yourself.

## Pilot's Logbook (anonymous flight stats)

The choppa keeps a tiny logbook and sends it to me, the author, purely because I'm curious how people fly it. It's sent through PostHog, an analytics service. It's anonymous and there's nothing personal in it.

**What gets logged:**
- You spawned a choppa
- You got in, and whether as the pilot or a passenger
- A flight ended (logged by the pilot), with:
  - how long it lasted
  - how far it went
  - the highest it got
  - its top speed
  - its speed at the very end
  - how many people were aboard
  - how many barrel rolls and loops you pulled off
  - how many seconds you spent upside down
  - how much of the flight was in cockpit view versus chase view
  - how it ended: *landed*, *bailed* (pilot hopped out mid-air) or *ended abruptly*
- A passenger's ride ended: how long it lasted and how it ended (*got out*, *jumped out* or *ended abruptly*)
- Someone jumped out mid-air: pilot or passenger, how high it was, and how fast it was going
- Someone used the flip-upright key, and whether they were inside the choppa or outside it

Each entry also carries the mod version and a random ID made up on your PC, so one person's flights can be told apart from another's.

**What does NOT get logged:** your name, Steam ID, other players' names, chat, your location, your PC specs, or anything outside the choppa. The service is set to discard IP addresses.

**Turning it off:** set `Enabled = false` under `[Pilot Logbook]` in the config:
- **r2modman / Gale:** Config editor → `com.andrew1431.bigchoppa` → `Pilot Logbook` → `Enabled` → `false` → Save.
- **Manual install:** open `BepInEx/config/com.andrew1431.bigchoppa.cfg` and change `Enabled = true` under `[Pilot Logbook]` to `Enabled = false`.

With it off, nothing is sent at all.

The card at the top of this page shows everyone's totals added together, refreshed about every 10 minutes.

## Source and bug reports

https://github.com/Andrew1431/big-choppa
