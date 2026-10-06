# Changelog

## 3.0.0

- Meet the Little Bird: a brand new toy MH-6 choppa with room for six (pilot, co-pilot and two on each outside
  bench), see-through windows and saddlebag pockets on the tail boom.
- The original choppa is still here: set `[Model] Vehicle` (new, default `LittleBird`) to `Classic` to keep
  flying it. Each choppa is built the way its owner picked, so mixed lobbies work.
- Everyone in the lobby needs 3.0.0 or newer; older versions won't see your choppas.

## 2.0.0

- Much smoother ride as a passenger (or watching someone else fly), especially at speed and in turns.
  You folks can definitely report these issues on [GitHub](https://github.com/Andrew1431/big-choppa/issues), I
  didn't know this problem was occurring!
- Everyone in the lobby needs 2.0.0 or newer; older versions won't see your choppas.

## 1.2.1

- The choppa is smaller by default, which suits the world much better. `[Model] Scale` changed from 1.0 to 0.6
  (updated automatically unless you had changed it yourself); set it back to 1.0 for the old size.
- In the chase camera you can now see your own head and body in the seat.

## 1.2.0

- Calling a choppa (F8) now has it fly in from the distance and land near you, if there's a flat, open spot
  nearby. Don't like it? Set `[Controls] FlyIn` to `false` and it appears right in front of you like before.

## 1.1.4

- Items left in choppa pockets now count as held when the game saves, so they turn up in the lost & found
  instead of disappearing.

## 1.1.3

- Free look (hold Left Alt) was far too twitchy. It now has its own setting, `[Controls] FreelookMouseSensitivity`,
  defaulting to 0.4 (it was effectively 2.0 before; set it back to 2.0 for the old feel).

## 1.1.2

- Fixed: the choppa's bump sounds never played (the game's build strips the part of Unity they relied on). Hard
  landings now go *bonk*.

## 1.1.1

- Pilot's Logbook now also notes how many items are in the choppa pockets when a flight ends, the most people
  aboard at once, and what gets put into or taken out of the pockets. The full list is in the README, and
  `[Pilot Logbook] Enabled = false` still turns it all off.

## 1.1.0

- Choppa pockets: three on each side, holding anything a backpack can.
  **Items in choppa pockets are not "held" when the game saves.** Take them out before you quit, or they won't be kept.

## 1.0.3

- Night flying: navigation lights, flashing beacons and strobes while someone's at the controls, a long-reaching
  headlight, and a faint cabin glow so you can find a parked choppa in the dark. Tune them under `[Lights]`.
- The choppa now pitches and rolls around its middle instead of near the skids.
  Config: `[Flight] CenterOfMassHeight` default changed from `1.4` to `1.8`. If you never changed it, it updates
  automatically; set it back to `1.4` for the old feel.

## 1.0.2

- Live Pilot's Logbook card at the top of the page: everyone's flight totals added together.

## 1.0.1

- Pilot's Logbook: anonymous flight stats, on by default and easy to turn off (see README).

## 1.0.0

- First release: a flyable playground helicopter with collective, pedals and mouse cyclic.
- Three seats, synced for everyone in the lobby.
- Synthesised rotor and flutter sounds.
