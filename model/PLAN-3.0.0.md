# 3.0.0: the Little Bird

Replace the primitive-built choppa with a low-poly, toy-coloured MH-6 modelled in Blender (`mh6.blend`).
Major version: new seat layout (6 seats) and protocol bump, so mixed lobbies with 2.x won't work.

## Status

- Done: model + empties, exporter (`model/export_mh6.py` -> `Resources/mh6.bin`, embedded), runtime loader
  (`MeshModel.cs`), `Vehicle` refactor (`Vehicles.cs`; lights/pockets take per-vehicle layouts), `[Model] Vehicle`
  config (default LittleBird), vehicle in the Spawn message (protocol 3), per-choppa seat count, logbook `vehicle`
  property, transparent glass material, convex debris colliders, CHANGELOG/README.
- Next: in-game test (looks, winding/normals, glass, seats/exits, pockets, lights, colliders/landing, crash),
  then tuning. Later: F8 hold-to-pick UI. Release: version bump to 3.0.0, new icon/screenshots.

## Model conventions (mh6.blend)

- Blender units are metres, nose points -Y, up is +Z. Everything hangs off the `MH6` empty.
- One mesh object = one crash piece (see Crash). Flat shaded, one material per object except the pod.
- Spinning groups are empties: `MH6_MainRotor` (spins about local Z), `MH6_TailRotor` (spins about local X).
- Seat anchors are empties `Seat_*` at the sitting surface; local -Y is the way the rider faces.
  Pilot = `Seat_Pilot` (right-hand seat). Bench riders face outboard.
- Pockets: six saddlebags on the tail boom, meshes `MH6_Pocket{L,R}{1,2,3}`, hang points `Pocket_{L,R}{1,2,3}`
  (empties just outboard of each pouch, same facing convention). +X is the aircraft's left.
- Mesh origins sit at each piece's centre (the crash pushes debris away from the middle by position).
- Exits: `Exit_{Pilot,Copilot,BenchL_Front,BenchL_Rear,BenchR_Front,BenchR_Rear}` on the ground (z 0), 1.9 m
  out, local -Y facing away from the heli.
- Lights: `Lens_*` empties (custom props `lens_color`, `lens_size`; runtime makes the glow sphere): HeadL/R,
  NavL/R, BeaconTop/Belly, Tail, StrobeL/R. Real lights: `Light_Head` (spot, local -Y = beam), `Light_Cabin`,
  `Light_Beacon` (points). Not aiming for legal nav-light accuracy.
- Body colliders: `Col_*` CUBE empties, scale = half extents, custom prop `collider` = `box` | `capsule`
  (capsule axis = longest extent): Cabin, Nose, Boom, Tail, SkidL/R. Not crash pieces.
- No cockpit camera anchor needed: the eye is seat + `CockpitEyeHeight`.
- Materials carry a flat colour + alpha; `MH6_Glass` is the only transparent one.

## Pipeline: Blender -> game (no Unity editor)

1. `model/export_mh6.py` (run with `blender --background mh6.blend --python export_mh6.py`) walks the hierarchy
   and writes `Resources/mh6.bin`: per object name, parent, local TRS, vertices/normals/triangles per material,
   custom props; per empty name + TRS; per material colour/alpha. Converts Z-up right-handed to Unity Y-up
   left-handed (swap Y/Z, flip winding) and applies modifiers (the pod's solidify).
2. The DLL embeds `mh6.bin`; a new `MeshModel` loader rebuilds GameObjects/Meshes at spawn, replacing
   `HeliModel.Build`. Materials stay runtime URP/Lit; glass switches to the transparent surface mode.
3. Colliders: keep a few hand-placed primitive colliders for the flying body (stable physics, cheap),
   use convex MeshColliders on debris only.

## Crash break-apart

`ChoppaCrash` already turns every MeshRenderer under `Model` into a rigidbody piece and throws the rotors off
whole, so separate Blender objects break apart for free. The work is making the pieces good:

- Done: the pod is split into 7 shell panels (`MH6_Pod{Windshield,Nose,Rear,Roof,Belly,SideL,SideR}`, each with
  its own solidify), the boom into `MH6_BoomFront`/`MH6_BoomAft`, skids and benches into L/R.
  Flat-shaded and same-coloured, so the seams are invisible while intact.
- Optional per-object custom props read by the exporter: `crash_group` (weld parts into one piece, e.g.
  seats+floor), `mass`, `no_debris` (vanish instead of flying, for tiny bits).
- Debris colliders: convex MeshCollider instead of BoxCollider for curved shell panels.
- Same seed on every PC already keeps pieces consistent across the lobby; nothing changes on the wire.

## Keep the classic choppa: pick your vehicle

The primitive choppa stays as an option.

- Refactor: a `ChoppaDesign` per vehicle that owns everything model-specific: build visuals, rotor transforms,
  seats/exits (count included), pocket hang points, light anchors, body colliders, centre of mass, cockpit and
  chase camera offsets, scale, maybe flight tuning (mass). `Classic` wraps today's `HeliModel`; `LittleBird`
  wraps the mesh loader. `Helicopter`, `ChoppaPockets`, `ChoppaLights`, `ChoppaCrash` read from the design
  instead of hard-coded positions.
- The design is per choppa, not per PC: the Spawn message carries a design id so every peer builds the same
  vehicle, and the host sizes seat arrays from it (Classic 3, Little Bird 6). Protocol bump.
- Step 1: config `[Choppa] Vehicle = LittleBird | Classic`, used by F8. Default `LittleBird` for everyone,
  existing players included (owner's call, 2026-10-05). New key, so no migration needed. CHANGELOG line:
  "`[Choppa] Vehicle` (new, default `LittleBird`): set `Classic` to keep flying the original choppa."
- Step 2 (later): a small picker when you press F8 (e.g. tap F8 = last used, hold F8 = choose), remembering
  the choice in the config.
- Logbook: add a `vehicle` property to `choppa_spawned` / `flight_ended` and list it in `thunderstore/README.md`.

## Parity audit vs the classic choppa

Everything `HeliModel` / `ChoppaLights` / `Helicopter` gives the classic that the Little Bird still needs:

- Done in the .blend (see conventions). Lights (`ChoppaLights.cs`) reference:
  - Steady lenses: `NavLeft` red / `NavRight` green (the classic has red at -X = its left; on the MH-6 left is
    +X), `TailLight` white, `HeadlightLeft`/`HeadlightRight` lenses on the nose.
  - Beacons (flashing red): `BeaconTop` (roof behind the mast) and `BeaconBelly`.
  - Strobes (white double flash): `StrobeLeft`/`StrobeRight` on the stabiliser tips (the MH-6 endplates).
  - Real lights: `Headlight` spot just ahead of the nose (tilt/range/angle from config), `CabinGlow` point in the
    cabin (always on), `BeaconGlow` point above the roof beacon.
  - Lens meshes can stay runtime spheres placed on the empties (they swap glow/off materials).
- Googly eyes: dropped for the Little Bird (owner); `Pupils` stays null.
- Done: `Exit_*` per seat (6).
- Cockpit camera: eye position is seat + `CockpitEyeHeight`; fine if the seat empties are right, but check the
  roof/door frame doesn't block the view.
- Config tuned for the classic geometry (seat offsets, centre of mass, chase camera, `StunRadius`) and the
  2.4 m spawn-spot check: keep as is for both (owner: "if it crashes, it crashes").
- Done (`Col_*`). Solid colliders: the classic marks tub/nose/skids/boom as solid. Little Bird needs hand-placed primitives for
  pod, boom, skids (landing), and the bonker relies on them.
- Rotor spin axes: `Helicopter` spins MainRotor about local Y and TailRotor about local X (Unity); the exporter
  must keep those axes after the Y/Z swap.
- Pockets: `ChoppaPockets` builds its own pouch meshes; for the Little Bird it should skip those (they're in the
  model) and only place the `PocketHome` colliders at the `Pocket_*` empties.
- Proxy/network, audio, bonks, crash seed: model-independent, nothing to do.

## Code changes

- `Helicopter.SeatCount` const -> per-design seat count (seat cycling/HUD, `ChoppaServer` seat arrays);
  bump `ChoppaNet.Protocol`.
- Pockets, lights, centre of mass, exits, cockpit camera, chase-camera distance (the MH-6 is ~9.3 m long with
  rotors) all come from the design.
- Logbook: `seat` values change (6 seats); update the event list in `thunderstore/README.md`.
- Config: `Scale` default likely needs revisiting for the new model (ask about migration before changing).
- Release: `PluginVersion` + manifest 3.0.0, CHANGELOG (don't mention breaking apart), new icon/screenshots.
