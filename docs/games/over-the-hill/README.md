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

Game-code reference (interop stubs: signatures plus calls into native code, no game logic):
`tools/games/over-the-hill/regen-decomp.sh` writes `~/projects/mods/wwthp/decomp/over-the-hill/`
(`assembly-csharp-dump.txt`, `types/*.cs`). It's outside every repo; the script refuses to write inside one.

Legal position (not legal advice): reading interop stubs to make mods interoperate is the
interoperability case that US fair use (*Sega v. Accolade*, *Sony v. Connectix*, DMCA §1201(f))
and the EU Software Directive Art. 6 protect. The open risk is contractual. **Before publishing
any mod, check the game's EULA and Funselektor's stated stance on mods** and record it here.
Not yet checked (2026-11-15).

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
| `VehicleBase` (VPP, `Il2CppVehiclePhysics`) | `wheelState[i]` (`suspensionState.contactSpeed`/`compressionRatio`, `grounded`, `wheelCol`), `isHardImpact`/`localImpactPosition`/`localImpactVelocity`, `SetWheelRadius(int, float)`/`GetWheelRadius`, `SetWheelTireFrictionMultiplier(int, float)` (oth-tire-damage) |
| `CampManager` | `RepairPartyCars()`, `RPC_EndCamping()` (camp heals cars) |
| `CarKicker` | seen in the decompiled sources (impulses to cars); not used by a mod yet |
| `CarDamageSystem` | per car: `DamageAmount`, `MaxDamageAmount`, `vehicle` (VPP `VPVehicleController`), `damageEffects`, `originalSpeedLimit`, `originalMaxTorque`, `ApplyDriveabilityDebuffs()`/`ResetDriveabilityDebuffs()`, `HealOnCabinArrival`, `HealOnPointOfInterestArrival`, `OnPOIReached(PointOfInterest)`, private `PerformLocalPlayerHealingOperations(HealthChangingReason)` (HealedByPOI/Teleporting/Self/Other/Camping) |
| `DamageEffects` | ScriptableObject: `AnimationCurve` `maxSpeedMultiplier`, `accelerationMultiplier`, `torqueMultiplier` (damage → driveability). Worst case seen on the Wombat: speed x0.50, torque x0.80 |
| `Il2CppVehiclePhysics.VPVehicleController` (in Assembly-CSharp) | `speedControl` (`speedLimiter`, `speedLimit`), `engine` (`peakRpmTorque`, `torqueCap`, `torqueCapLimit`, …), runtime `m_engine.throttleInput` |
| `PointOfInterest` | `Info.Type` (`PointOfInterestType`: Generic, Cabin, Merchant, Scenic, Trail, Portal, ItemRecovery, Puzzle). Houses are `Cabin : SpawnablePointOfInterest` |
| `VehicleUpgradeObject` | mounted part on a car: `VehiclePartReference` (`VehiclePartSO`; `VehicleCosmetic.CosmeticType`). Side jerry cans exist as `Side_Jerry Cans` / `Side_Jerrycan` in the assets. Spare wheels: `Sparewheel_Rear`, `_Trasheroo`, `_Machette`, `_Axe_Shovel`, `_Wreath`, `_cover_00/01`. Side tools: `Side_Tool_{Sledgehammer,PipeWrench,HiLiftJack,Axe,Shovel,PickAxe,IcePicks,BoltCutters,Machette}` |
| `Odometer` | per car: `mTravelled` (metres) |
| `ContextUIInclineIndicator` | the two bottom-left tilt dials (HUD). Only `SetIncline`/`SetEnabled`: driven from outside, so a clone with the component removed stays still. See §HUD |
| `CarHealthUI`, `ContextUIEngineRPMDisplay`, `ContextUIOdometerPanel` | HUD panels. `CarHealthUI`'s rect is a full-screen container; the RPM display sits bottom right. Neither is a good placement anchor. `ContextUIEngineRPMDisplay : UIContainer` needs a reference to `Il2CppFunselektor.FunCore.Runtime` |
| Terrain layers | snow detection by layer name keywords (`snow`, `ice`, `glacier`, `frost`) |

## HUD (uGUI)

- Screen-space HUD. On 3440x1440 the tilt dials are at (144,1176) and (347,1176), 187x160 px each.
  Canvas units ≈ 1080p pixels (a 140x120 dial renders at 187x160 on 1440p).
- Tilt dial parts (`RollIndicator`, and the pitch twin): `Mask/{BlurredBackground (sprite
  InclineIndicator-Mask, TranslucentImage), RotatingPart/{Terrain, Sky (InclineIndicator-Bottom,
  white/black α0.4), GroundLine (RawImage 140x2), Car (InclineIndicator-CarBack)}, Border, Lines
  (side ticks only)}`. The parent has no LayoutGroup.
- HUD font: TMP `NunitoSans-Bold SDF` (e.g. from `ContextUIEngineRPMDisplay`'s TMP text).
- **To add a HUD element in the game's style, clone a stock one** (oth-fuel-management
  `StockDial.cs`). Instantiate it under an inactive holder GameObject, `DestroyImmediate` the game's
  logic component, reparent next to the original, and mirror its `activeInHierarchy`. Add
  `Image`/`TextMeshProUGUI` children for your own content. A look-alike drawn with IMGUI was
  rejected in testing: hard to read, off-style. Needs refs `UnityEngine.UI`, `Unity.TextMeshPro`
  (`Il2CppTMPro`), and `UnityEngine.TextRenderingModule` for `TextAnchor`.
- To find a HUD element's on-screen rect: `RectTransform.GetWorldCorners` →
  `RectTransformUtility.WorldToScreenPoint` (null camera for overlay canvases).

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
| `oth-settings-hub` | SettingsHub | F5 menu for every mod (tabs) | none (AppDomain slots `WWTHP.SettingsHub.*.v1`, see generic §Menus) | 1 | `SettingsHub/` (CrashGuard only) |
| `oth-tire-damage` | TireDamage | F4 tire repair screen, F1 menu without the hub | none | - | `TireDamage/` (CrashGuard only) |
| `oth-fuel-management` | FuelManagement | F10 menu, F11 (hold) siphon | `"FUEL"` `0x4C455546` | 1 | `FuelManagement/` (CrashGuard only) |

Taken by third-party mods the user runs: `OTHNight`, `ImmersiveFirstPersonView`, `OTH.CosmeticStacker`
(no networking); `OTHNight` uses F2/F3. Pick unused keys; make every key configurable. A mod's own
menu key only works without the settings hub; new mods should still have one.

## Incident log (game-level lessons, newest first)

| Date | Repo/version | What happened | Lesson |
| --- | --- | --- | --- |
| 2026-11-11 | oth-wombatgd 0.5.3 | Confirmed working in MP as client after smoke rework | cloning stock exhaust + `Emit` is safe |
| 2026-11-10 | oth-wombatgd 0.5.3 | Repeated MP crashes; CrashGuard marker = `smoke` | not networking: hand-built ParticleSystem; rule 7 |
| 2026-11-10 | oth-wombatgd 0.5.2 | Detour forwarded unknown keys → silent death ~0.2 s after a second mod hooked | rule 4 + chaining convention |
| 2026-11-10 | oth-abominable-snowman 0.1.1 | NativeHook broke the join handshake under Proton | rule 2, vtable swap |
| 2026-11-10 | oth-wombatgd 0.5.0 | Harmony on Fusion struct method garbled `ReliableKey`; map load hung | rule 1 |
| 2026-11-15 | both | Coding standards, tests, CI introduced; Profile wire format moved to `Core/` | see generic testing.md |
