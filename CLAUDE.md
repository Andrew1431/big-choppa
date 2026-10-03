# Big Choppa: notes for Claude

BepInEx 6 IL2CPP mod for **Big Walk** (Steam, Unity 6, URP, Mirror networking). Adds a goofy primitive-built toy
helicopter with "real" controls (collective, pedals, mouse cyclic), 3 seats, multiplayer sync, synthesised audio,
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
- Default game path: `E:\SteamLibrary\steamapps\common\Big Walk` (`-GameDirectory` to override).
- Logs: `<game>\BepInEx\LogOutput.log`; a remote target's log is readable at `<unc>\BepInEx\LogOutput.log`.
- The interop folder only exists after launching the game once with BepInEx.
- Config file: `BepInEx\config\com.andrew1431.bigchoppa.cfg`. **Changing a default in code doesn't change an
  existing .cfg**; edit the file too (on every test machine) when a default change should apply.
- `Debug.VerboseLogging` (default false) adds detail to the log and an on-screen network status line.

## Versioning and Thunderstore release

- Version lives in two places that must match: `Plugin.cs` `PluginVersion` and `thunderstore/manifest.json`
  `version_number` (semver). `package.ps1` refuses to package if they differ.
- Plugin GUID is `com.andrew1431.bigchoppa` and must never change: it names every player's config file.
- Release steps: bump both versions, add a `thunderstore/CHANGELOG.md` entry, run `.\package.ps1` → produces
  `dist\Andrew1431-BigChoppa-<ver>.zip` (manifest, icon, README, CHANGELOG, DLL at zip root). Upload at
  https://thunderstore.io/c/big-walk/create/ (the owner does this; it needs their login). Versions can't be
  re-uploaded or deleted, only deprecated.
- Manifest rules: `name` is [A-Za-z0-9_] only, description ≤ 250 chars, no BOM in manifest.json, icon exactly
  256×256 PNG. `thunderstore/make-icon.ps1` regenerates `icon.png` with System.Drawing.
- Dependency: `BepInEx-BepInExPack_IL2CPP-6.0.755` (matches the BepInEx 6.0.0-be.755 we build against). Check the
  latest with `https://thunderstore.io/api/experimental/package/BepInEx/BepInExPack_IL2CPP/`.
- Bump `ChoppaNet.Protocol` whenever the wire format changes; mismatched peers are ignored by the host.

## Code map

- `Plugin.cs`: entry; registers injected types (`Helicopter`, `ChoppaBonker`, `ChoppaManager`). Any new
  MonoBehaviour must be registered with `ClassInjector.RegisterTypeInIl2Cpp<T>()` before use and needs a
  `(IntPtr ptr) : base(ptr)` ctor.
- `ChoppaManager.cs`: local player lookup, client-side choppa list, boarding/seats, camera takeover, HUD, crashes.
- `Helicopter.cs`: flight model (owner) or snapshot-interpolated kinematic proxy (everyone else).
- `ChoppaNet.cs` / `ChoppaServer.cs`: networking (see below).
- `HeliModel.cs`: primitives; seat/exit anchors. `ChoppaCrash.cs`: seeded break-apart.
- `ChoppaAudio.cs` / `ChoppaBonker.cs`: procedural audio and collision bonks.
- `ChoppaPhysics.cs`: collision-layer fix. `ChoppaInput.cs`: Rewired with Unity Input fallback.
- `tools/dumper`: Mono.Cecil type dumper for the interop DLLs (excluded from the mod build). Usage:
  `dotnet build tools/dumper/dumper.csproj -o tools/dumper/out` then
  `tools/dumper/out/dumper.exe "<game>/BepInEx/interop/Mirror.dll" '^Mirror\.NetworkServer$'`
  (append `names` to list type names only). This is how game APIs were discovered; there's no source.

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
