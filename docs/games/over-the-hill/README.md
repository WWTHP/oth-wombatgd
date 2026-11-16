# Over the Hill: game layer

Layer: **game**. Everything specific to *Over the Hill* (Funselektor). Read it together with the
generic layer ([il2cpp-melonloader.md](../../generic/il2cpp-melonloader.md)). Repos for this
game are named `oth-*`, carry the topic `over-the-hill`, and are tracked on the
**Over the Hill mods** project board.

## Facts

- Unity **IL2CPP**, multiplayer via Photon **Fusion** (host mode; shared mode handled defensively).
- Steam app id **2929250**. Developed on Linux under **Proton**, Flatpak Steam.
- MelonLoader **v0.7.3**. Mods target `net6.0` + Harmony + Il2CppInterop.
- `[assembly: MelonGame(null, "over the hill")]`.

## Environment

| What | Where |
| --- | --- |
| Game dir | `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/over the hill` |
| MelonLoader | `<game>/MelonLoader/` (`net6/`, `Il2CppAssemblies/`) |
| Mods | `<game>/Mods/` |
| Settings | `<game>/UserData/MelonPreferences.cfg`; per-mod data `<game>/UserData/<Mod>/` |
| MelonLoader log | `<game>/MelonLoader/Latest.log` (older: `Logs/`) |
| Unity log | `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/compatdata/2929250/pfx/drive_c/users/steamuser/AppData/LocalLow/Funselektor/over the hill/Player.log` (+ `Player-prev.log`). **The real error is often only here.** |
| ilspycmd | `~/.dotnet/tools/ilspycmd` (not on PATH) |
| SDK | `dotnet-sdk-10.0` from dnf (builds `net6.0`). Agents can't run `dotnet-install.sh`; installs go through the user. |
| Game process | `pgrep -fa 'over the hill'` (don't deploy while it runs) |

Regenerate the local game-code reference (git-ignored): `tools/games/over-the-hill/regen-decomp.sh`.

Workstation notes: interactive `cp` is aliased to `cp -i` and `rm` moves to the trash. Use
`\cp -f` / `\rm -rf` in agent one-liners.

## Rules specific to this game

1. **Never Harmony-patch `NetworkRunner.Fusion_Simulation_ICallbacks_OnReliableData`** or any
   method taking `ReliableKey` / `ReadOnlySpan<byte>` by value. (oth-wombatgd 0.5.0: map load
   hung in MP.)
2. **Never use `NativeHook` on `OverlandNetworkRunner.OnReliableDataReceived`.** (oth-abominable-snowman
   0.1.0: the RuntimePlayer join handshake broke under Proton and the map failed to load.)
3. Intercept reliable data **only** via the vtable swap on `OverlandNetworkRunner`'s
   `OnReliableDataReceived` slot (§Fusion), installed **3 s after
   `OverlandNetworkRunner.IsLocalPlayerSpawned`** becomes true, once.
4. **Forward only keys with `int0` 1 or 2 to the game's handler.** It throws
   `ArgumentOutOfRangeException` on any other key, which kills the process inside a native detour.
   Forward everything only when chained onto another mod's detour.
5. **Send custom packets only to peers known to run the mod** (apart from one Hello). Vanilla
   peers throw and log on unknown keys.
6. `runner.SimulationTime`, `IsLocalPlayerSpawned` and similar throw while loading. Wrap them in try/catch.
7. Particles: clone the stock exhaust system (`ExhaustParticlesManager.exhaustParticleSystem.noDamageParticleSystem`)
   with `Object.Instantiate` and only `Emit`.
   (oth-wombatgd 0.5.3: hand-built ParticleSystem + Get/SetParticles crashed in MP.)

## Useful game classes

| Class | Use |
| --- | --- |
| `CarAudioManager` | per car audio: `vehicleType` (e.g. `"Wombat"`), `engineAudioEmitter`, `Start`, `MountedUpgradesChanged`, `OnDestroy`, `SetLabeledParameterIfPresent(AudioEmitter, string parameterName, ref string label)`. Harmony-safe |
| `AudioEmitter` | wraps an FMOD `EventInstance`; parameters `rpmEngine`, `throttle`, label `vehicleType` |
| `ExhaustParticlesManager` | stock exhaust; `Update`, `OnDestroy` (Harmony-safe); `exhaustParticleSystem.noDamageParticleSystem` / `.ActiveParticleSystem` |
| `OverlandNetworkRunner` | `Instance`, `NetworkRunner`, `IsLocalPlayerSpawned`, `OnReliableDataReceived` (vtable hook) |
| `NetworkRunner` (Fusion) | `SendReliableDataToServer(ReliableKey, ReadOnlySpan<byte>)`, `SendReliableDataToPlayer(PlayerRef, …)`, `IsServer`, `IsSinglePlayer`, `Topology`, `LocalPlayer`, `ActivePlayers` |
| `ChallengeTrail`, `ChallengeTrailInfo` | challenge trail waypoints (used for yeti placement) |
| `CarKicker` | seen in the decompiled sources (impulses to cars); not used by a mod yet |
| Terrain layers | snow detection by layer name keywords (`snow`, `ice`, `glacier`, `frost`) |

Vehicle hierarchy (seen on the Wombat): cosmetic parts under `Visual/` (e.g. `Visual/Antenna`), colliders
under `Collider/` (cab: `Collider/Top`, a MeshCollider). Wobbly parts use a Rigidbody + ConfigurableJoint.

## Fusion mod-to-mod messaging

Reference implementation: [`snippets/over-the-hill/FusionNet.cs.txt`](../../../snippets/over-the-hill/FusionNet.cs.txt)
(from oth-abominable-snowman `Net.cs`). Copy it into a mod as `Net.cs`, change `KeyMagic`, and
adapt `Settings`/`Mod` references and `Describe()`.

- Topology: host mode. Clients can only send to the host; the host relays to other modded peers.
  Sync therefore needs the host to have the mod. Without it, everything works locally.
- **Send**: `il2cpp_runtime_invoke` on `SendReliableDataToServer` / `SendReliableDataToPlayer` with
  raw structs: `RawKey { int A, B, C, D }` (A = 4-char mod tag, B = protocol version) and
  `RawSpan { IntPtr Ptr; int Length }`. Max packet 8 KB.
- **Receive**: vtable swap on `OverlandNetworkRunner.OnReliableDataReceived`. Native signature
  `(this, runner, PlayerRef player, ReliableKey* key, ReadOnlySpan<byte>* data, MethodInfo*)`.
  Own key → copy bytes, enqueue (max 256), process on the main thread.
- Hello once to everyone; afterwards only to peers we've heard from.
- CrashGuard stage `net` covers the first 90 s after hooking.
- Chaining (load order independent): own key → handle. Else if `_chained` → forward all.
  Else forward only `int0` 1/2.

## Registry (keep up to date: one row per mod)

| Repo | Mod | Menu / keys | Net tag (`int0`) | Protocol | UserData folder |
| --- | --- | --- | --- | --- | --- |
| `oth-wombatgd` | WombatGD | F7 menu, F8 engine sound cycle, F9 smoke screen | `"WGGD"` `0x57474744` | 1 | `WombatGD/` |
| `oth-abominable-snowman` | AbominableSnowman | F6 menu | `"YETI"` `0x49544559` | 1 | `AbominableSnowman/` |

Taken by third-party mods the user runs: `OTHNight`, `ImmersiveFirstPersonView`, `OTH.CosmeticStacker`
(no networking). Pick unused keys; make every key configurable.

## Incident log (game-level lessons, newest first)

| Date | Repo/version | What happened | Lesson |
| --- | --- | --- | --- |
| 2026-11-11 | oth-wombatgd 0.5.3 | Confirmed working in MP as client after smoke rework | cloning stock exhaust + `Emit` is safe |
| 2026-11-10 | oth-wombatgd 0.5.3 | Repeated MP crashes; CrashGuard marker = `smoke` | not networking: hand-built ParticleSystem; rule 7 |
| 2026-11-10 | oth-wombatgd 0.5.2 | Detour forwarded unknown keys → silent death ~0.2 s after a second mod hooked | rule 4 + chaining convention |
| 2026-11-10 | oth-abominable-snowman 0.1.1 | NativeHook broke the join handshake under Proton | rule 2, vtable swap |
| 2026-11-10 | oth-wombatgd 0.5.0 | Harmony on Fusion struct method garbled `ReliableKey`; map load hung | rule 1 |
| 2026-11-15 | both | Coding standards, tests, CI introduced; Profile wire format moved to `Core/` | see generic testing.md |
