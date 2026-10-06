# Big Choppa: notes for Claude

BepInEx 6 IL2CPP mod for **Big Walk** (Steam, Unity 6, URP, Mirror networking). Adds a goofy primitive-built toy
helicopter with "real" controls (collective, pedals, mouse cyclic), 6 seats (Little Bird) or 3 (classic), multiplayer sync, synthesised audio,
and a break-apart crash. Published on Thunderstore as `BigChoppa` (community `big-walk`).

`README.md` covers controls/config for devs; `thunderstore/README.md` is the player-facing page. Machine-specific
setup (second test PC, share credentials, undo steps) lives in `DEV-NOTES.local.md` (gitignored), so read it if present.

## Design intent (from the owner)

- Cartoon kids-playground look, but flight controls like Arma/Wardogs: no Battlefield "space = up". Not hyperreal
  either: no ground effect, engine or rotor science.
- Defaults: W/S collective (springs back to hover when released, to idle when grounded), A/D pedals, mouse
  forward/back = pitch, left/right = roll. All keys configurable.
- Engine is on while someone is in the pilot seat, off otherwise.
- **The crash is a surprise**: don't mention breaking apart, stunning or debris in `thunderstore/README.md`,
  `thunderstore/CHANGELOG.md` or the manifest description.
- Proxy (remote) choppas intentionally do NOT play hard-landing bonks; the owner was happy with that.

## Build, deploy, test

- `.\build.ps1` builds Release against the game's `BepInEx\core` + `BepInEx\interop`, installs to
  `<game>\BepInEx\plugins\BigChoppa\`, then pushes the DLL to every UNC game folder listed in `remote-targets.txt`
  (gitignored). A push fails with a warning if the game is running on that machine (DLL locked) or the PC is offline.
- `.\build.ps1 -InstallBepInEx` also copies the BepInEx loader + interop to the remote targets (first-time setup, or
  after a game update regenerates interop).
- **Second-PC pushes are on hold.** Olga's install (mod + BepInEx) was removed again on 2026-10-05 after the 2.0.0
  test. Her line in `remote-targets.txt` is commented out. To resume: uncomment it, close her game, run
  `.uild.ps1 -InstallBepInEx`; she then launches from Steam (not r2modman) to get the dev build.
- Default game path: `E:\SteamLibrary\steamapps\common\Big Walk` (`-GameDirectory` to override).
- BepInEx location: the game folder's `BepInEx` if present, else the r2modman profile named **Dev**
  (`%APPDATA%\r2modmanPlus-local\BigWalk\profiles\Dev\BepInEx`); `-BepInExDirectory` overrides. The **Default**
  profile is the owner's "player" profile with the published Thunderstore version; never install dev builds there.
  The Dev profile must NOT have the Thunderstore BigChoppa installed (same GUID, only one loads; the build warns).
  To test a dev build, launch the game from r2modman with the Dev profile selected.
- `package.ps1` zips the DLL from `bin\Release\net6.0`, not from the install folder.
- Logs: `LogOutput.log` in whichever BepInEx folder the build used (currently the r2modman Dev profile); a remote target's log is readable at `<unc>\BepInEx\LogOutput.log`.
- The interop folder only exists after launching the game once with BepInEx.
- Config file: `BepInEx\config\com.andrew1431.bigchoppa.cfg`. Nobody's .cfg ships with the mod; BepInEx writes it on
  first load and from then on the saved value wins over the code default.
- **Changing any config default: stop and ask the developer whether existing players should get the new value.** If
  yes (a sensible default, not a personal preference), add a migration in `ChoppaConfig.Migrate`: bump
  `CurrentConfigVersion` and add `if (from < N) Upgrade(Entry, oldDefault);`. It only rewrites the value if it still
  equals the old default, so players' own tweaks survive. Migrations also update the dev/test machines' configs, so no
  hand-editing needed. Never rename a key just to force a new default (it throws away people's settings).
  Every default change also gets a `thunderstore/CHANGELOG.md` line naming `[Section] Key` and the old → new value,
  so players can switch back.
- The .cfg is watched and reloaded live (`ChoppaConfig.WatchForEdits`/`Tick`), so read `.Value` at use time rather than
  caching it when you want a setting to be live-tunable. Values used only at build time apply to the next spawn.
- Dev-only code goes inside `#if DEVBUILD`. `build.ps1` defines it; `package.ps1` builds with `-Publish`, which
  doesn't, and refuses to package a DLL containing `DevAutoHost`. Dev-only config entries must be inside the `#if` too, so they never show up in players' configs.
- `[Dev] AutoHost` (dev builds, default false, set true on this PC only): `DevAutoHost.cs` clicks through the
  main menu and hosts the most recent save. Hold Shift at startup to skip. A second test PC that joins as a client leaves it off.
- `Debug.VerboseLogging` (default false) adds detail to the log and an on-screen network status line.

## Versioning and Thunderstore release

- Version lives in two places that must match: `Plugin.cs` `PluginVersion` and `thunderstore/manifest.json`
  `version_number` (semver). `package.ps1` refuses to package if they differ.
- Plugin GUID is `com.andrew1431.bigchoppa` and must never change: it names every player's config file.
- Release steps: bump both versions, add a `thunderstore/CHANGELOG.md` entry, run `.\package.ps1` → produces
  `dist\Andrew1431-BigChoppa-<ver>.zip` (manifest, icon, README, CHANGELOG, DLL at zip root). Upload at
  https://thunderstore.io/c/big-walk/create/ (the owner does this; it needs their login). Versions can't be
  re-uploaded or deleted, only deprecated.
- `.\package.ps1 -Upload` publishes the zip with tcli (`dotnet tool install -g tcli`; config in
  `thunderstore/thunderstore.toml`) using a team service-account token in `$env:TCLI_AUTH_TOKEN`. The token never
  goes in the repo or chat. CI can't build the DLL (it needs the game's interop assemblies), so publishing stays local.
- Manifest rules: `name` is [A-Za-z0-9_] only, description ≤ 250 chars, no BOM in manifest.json, icon exactly
  256×256 PNG. `thunderstore/make-icon.ps1` regenerates `icon.png` with System.Drawing.
- Dependency: `BepInEx-BepInExPack_IL2CPP-6.0.755` (matches the BepInEx 6.0.0-be.755 we build against). Check the
  latest with `https://thunderstore.io/api/experimental/package/BepInEx/BepInExPack_IL2CPP/`.
- **Always load-test in the Dev profile before packaging.** A config section named "Pilot's Logbook" once broke
  loading entirely: BepInEx section/key names can't contain `= \n \t \ " ' [ ]`.
- Published so far: 1.0.0 (no logbook). 1.0.1 adds the Pilot Logbook. 1.0.2 (README stats card only, no code change) is packaged once the card is live. 1.0.3 adds night lights, live config reload and the CenterOfMassHeight migration. 1.1.0 adds choppa pockets. 1.1.1 adds pocket/crew logbook analytics (no gameplay change). 1.1.2 fixes bonk sounds (Collision getters are stripped) and makes dev builds log instead of send logbook events. 1.1.3 adds FreelookMouseSensitivity. 1.2.0 adds the fly-in (F8 calls the choppa in on autopilot; `[Controls] FlyIn`). 1.2.1 shrinks the default Scale to 0.6 (migrated) and shows your head/body in the chase camera. 2.0.0 (major because protocol 2 breaks mixed lobbies) smooths proxy playback: owner stamps snapshots with its physics time, receivers replay on a
  local clock with a min-tracked offset, Hermite interpolation, posed per frame in `Update` (protocol 2). 1.1.4 makes pockets inventory homes (`isInventory = true`) so pocketed items save to the lost & found (an earlier assumption that they couldn't was never tested).
- Bump `ChoppaNet.Protocol` whenever the wire format changes; mismatched peers are ignored by the host.

## Code map

- `Plugin.cs`: entry; registers injected types (`Helicopter`, `ChoppaBonker`, `ChoppaManager`). Any new
  MonoBehaviour must be registered with `ClassInjector.RegisterTypeInIl2Cpp<T>()` before use and needs a
  `(IntPtr ptr) : base(ptr)` ctor.
- `ChoppaManager.cs`: local player lookup, client-side choppa list, boarding/seats, camera takeover, HUD, crashes.
- `Helicopter.cs`: flight model (owner) or snapshot-interpolated kinematic proxy (everyone else).
- `ChoppaNet.cs` / `ChoppaServer.cs`: networking (see below).
- `ChoppaAutopilot.cs`: flies a newly called choppa in using only pilot inputs (collective, rates); `ChoppaManager.FindLandingSpot` picks a flat, clear spot with open sky or refuses the spawn.
- `Vehicles.cs`: `Vehicle` enum (Classic / LittleBird; wire value in the Spawn message, never reorder), per-vehicle
  seat count and scale (`[Model] Scale` × 1/0.6 for the Little Bird, which is modelled at real size), and
  `LittleBirdModel`, which builds the MH-6 from the embedded `Resources/mh6.bin` using its named empties.
  `[Model] Vehicle` picks what F8 calls in; each choppa is built as its spawner chose, so mixed lobbies work.
- `MeshModel.cs`: reads `mh6.bin` and instantiates nodes/meshes. Source is `model/mh6.blend`; re-export after any
  model change with `model/export_mh6.py` (Blender: `blender --background model/mh6.blend --python
  model/export_mh6.py`, or run it in an open Blender). Conventions and empties: `model/PLAN-3.0.0.md`.
- `HeliModel.cs`: the classic choppa from primitives; seat/exit anchors. `ChoppaCrash.cs`: seeded break-apart
  (Little Bird debris uses convex MeshColliders).
- `ChoppaLights.cs`: lamp lenses (unlit, swapped on/off) plus three real lights per choppa: headlight spot, cabin
  glow (always on), flashing roof beacon. Powered = `EngineOn || RotorSpin > 0.1` so proxies light up too.
- `ChoppaPockets.cs`: 6 side pockets. Each is a game `PropHome` (`pinGroup` copied from a backpack pocket, normally
  `GoesInBackpack`) plus a `CastableTarget` so the game's own crosshair place/pick-up and Mirror sync do the work.
  Homes are addressed over the network by ticket (`SeaShell.ShellReference(ticket)` → `TicketOffice`); ours are
  `60000 + (id % 900) * 6 + slot`, so spawns pick an id with a free slot. Items must be released
  (`ReleaseAll`: host `ServerSetUnpinned`, clients `LocalUnpin`) before a choppa is destroyed or they'd be destroyed
  with it. Homes have `isInventory = true`, so pocketed items count as held and the game saves them to the lost & found.
- `ChoppaAudio.cs` / `ChoppaBonker.cs`: procedural audio and collision bonks.
- `ChoppaLogbook.cs`: "Pilot's Logbook" anonymous usage events sent to PostHog (US region, `/batch/`; the project key is
  write-only and public by design). Dev builds (`DEVBUILD`) never send: they log each event to `LogOutput.log` as `Logbook (dev, not sent): …` instead. On by default, opt-out via `[Pilot Logbook] Enabled`. Events: `choppa_spawned {vehicle}`,
  `choppa_boarded {seat}`, `flight_ended {duration_s, distance_m, max_altitude_m, top_speed_kmh, end_speed_kmh, how: landed|bailed|ended
  abruptly, riders, max_riders, pocket_items, rolls, loops, vehicle, upside_down_s, cockpit_view_pct}` (pilot only), `ride_ended {duration_s, how: got
  out|jumped out|ended abruptly}` (passengers), `jumped_out {seat, height_m, speed_kmh}` (anyone leaving > 1.5 m up),
  `flipped_upright {from: inside|outside}` (F9), `choppa_ended_abruptly {impact_kmh, riders, piloted, pocket_items}` (the crash, sent by the simulating owner, so it covers unpiloted crashes too), `pocket_used {action: stowed|taken, item}` (host only, so each is counted once per lobby). The owner deliberately does NOT want game-launch or session-size events. **Any new event or property must
  be added to the list in `thunderstore/README.md`**; full transparency was a condition. Never send names or Steam IDs.
  "Ended abruptly" is the crash, worded so the README stays honest without spoiling it.
- `stats-worker/`: Cloudflare Worker `big-choppa-stats` at https://big-choppa.hartwigdev.ca serving `/card.svg` (the
  live stats card at the top of both READMEs) and `/stats.json`, from a HogQL query against PostHog project 643292,
  cached 10 min. The PostHog personal API key (read-only query scope) is the Worker secret `POSTHOG_PERSONAL_KEY`;
  it must never go in the repo or chat. `node preview.js` renders the card with fake numbers; `npx wrangler deploy`
  ships it (needs `npx wrangler login` first). New logbook stats on the card need both the query and `card.js` updated. `npx wrangler versions upload` gives a preview URL to check a query change against real data before `deploy`.
- `DevAutoHost.cs`: dev-build-only menu skipper (see Build, deploy, test).
- `DevTime.cs`: dev-build-only `,` / `.` = time of day -/+ 1 h via Enviro (`EnviroManager.Time.SetTimeOfDay`); falls
  back to `SkyManager.SetFixedTime` if the game snaps it back.
- `ChoppaPhysics.cs`: collision-layer fix. `ChoppaInput.cs`: Rewired with Unity Input fallback.
- Game APIs were discovered with a throwaway Mono.Cecil console app (Mono.Cecil.dll is in `BepInEx\core`): load an
  interop DLL (`BepInEx\interop\Assembly-CSharp.dll`, `Mirror.dll`, …), regex-match type names, print fields, enum
  values, properties and methods with static-ness. There's no game source; the interop stubs are the API surface.

## Hard-won game / IL2CPP facts

- Game types have no namespace (`PlayerCharacter`, `PlayerFaller`, …). Unity 6 API names: `linearVelocity`,
  `linearDamping`, `FindObjectsByType`.
- **Collision**: ground is layer 10 "World", and the layer collision matrix has *nothing* colliding with it; the game
  opts in per collider. Our colliders/rigidbodies set `includeLayers = ~0`, `excludeLayers = 0`,
  `layerOverridePriority = 1000` (`ChoppaPhysics.CollideWithEverything`). Without it the choppa falls through the map.
- `PlayerCharacter`: root holds the Rigidbody `rb`; `bypassUpdate/FixedUpdate/LateUpdate` flags stop the game
  driving the player (we set them while seated). Camera is at
  `kernal/crouchTranslator/CameraHeightOffset/CameraUprighter/CameraPivot/PlayerCamera`
  (`cameraMinder.playerCameraReferences.playerCamera`); we unparent it while seated and restore it on exit.
- `faller.TriggerFall()` stuns the local player (confirmed `isDazed=True`). `sitter.SetSittingLocal` +
  `playerNetworking.CmdSetSitting` give the sitting pose. `playerNetworking.username` is the display name.
- Known issue: the seated character looks like it's lying on its back. The `kernal` tilt theory was disproven (its
  localRotation is identity); next suspect is the sit pose itself (try `Model.SitWhileFlying = false`).
- URP shader "Universal Render Pipeline/Lit" works for runtime materials (`_BaseColor`).
- Il2CppInterop: `byte[]` ↔ `Il2CppStructArray<byte>` convert implicitly; `AudioClip.SetData` needs an explicit
  `(Il2CppStructArray<float>)` cast (ambiguous with the Span overload). Managed → IL2CPP delegates via
  `DelegateSupport.ConvertDelegate<T>`; keep a reference so it isn't collected. Runtime AudioClips need
  `HideFlags.DontUnloadUnusedAsset`.
- Game audio uses its own AudioSystem with mixer groups; `ChoppaAudio` guesses the SFX group by name and logs
  "Choppa audio routed to mixer group '…'". Unverified whether the game's volume slider then applies.

## Networking architecture

- Raw Mirror handler id `0xC40F` registered directly into `NetworkServer.handlers` / `NetworkClient.handlers`
  (re-checked every frame because Mirror clears them on init/shutdown). IL2CPP can't host new generic Mirror message
  structs, so we write `[ushort len][Msg type][payload]` bytes ourselves (BinaryWriter). Channel 0 reliable,
  1 unreliable.
- Handshake: the client sends `Hello(protocol)` once its player exists; the host replies `HelloAck` and only then relays
  to that connection. Mirror may disconnect peers on unknown message ids, so a vanilla host could kick a modded
  client; this is documented for players.
- Host (`ChoppaServer`) is authoritative for the choppa list, seats and ownership. The owner (whoever last took the
  pilot seat) simulates and streams `State` (pos/rot/vel/angVel/rotor/collective) at `SendRate` (2 Hz when idle);
  the host relays it unreliably and others interpolate at `NetworkTime.time - InterpolationDelay`.
- Crash: the owner detects it, breaks locally and sends `Crash(id, impact, seed)`; everyone else breaks their copy with
  the same seed and stuns their own player if inside or within radius.
- If an owner leaves, the host takes ownership (`Prune`, using `NetworkServer.connections` identities so the game's
  area-of-interest culling can't hide players). Remote passengers are pinned to our copy's seat each frame.
- Offline (no Mirror session, or `Networking.Enabled = false`): messages loop back in-process, same code path.
- v1 worked first try between two PCs (host + client), including passengers and crashes.

## TODO / known bugs

- **Late joiners don't see existing choppas.** A player who joins a lobby after choppas were spawned sees none of them
  until someone presses F8 (the new Spawn message makes them show up; unclear whether the others appear too). The
  host probably needs to replay every existing choppa (Spawn + Seats + Owner) to a connection right after its
  `HelloAck`. Owner is fine leaving it for now (reported after 1.1.0).
