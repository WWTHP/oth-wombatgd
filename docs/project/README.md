# Project: WombatGD

Project layer for `WWTHP/oth-wombatgd`. Game layer: [Over the Hill](../games/over-the-hill/README.md).

Makes Over the Hill's **Wombat** behave like a **1980 Mercedes 300GD** (OM617 3.0 L naturally
aspirated inline-5 diesel):

- passenger-side **snorkel** (mirrored stock snorkel) and **antenna** moved to the windscreen pillar
- **diesel engine sound** layered over / replacing the stock engine (FMOD core API)
- **coal smoke**: black diesel smoke scaled with throttle, plus a **smoke screen** key
- **F7** settings menu, **F8** engine sound cycle, **F9** smoke screen (all configurable)
- **multiplayer sync** of each player's truck profile to other players running the mod

Current version: see `MelonInfo` in `WombatGD/WombatGDMod.cs` (0.5.4 at migration).

## Layout

| Path | What |
| --- | --- |
| `WombatGD/` | the mod (`dotnet build -c Release` deploys DLL + `assets/smoke` + `assets/sounds/loops`) |
| `WombatGD/Core/Profile.Wire.cs` | profile data + network wire format (pure, unit tested) |
| `tests/WombatGD.Tests/` | xUnit tests for `Core/` |
| `tools/*.py` (project-owned) | audio + texture tools (below); `tests/python/` tests them |
| `assets/smoke/` | puff textures (raw RGBA32) + preview, from `make_smoke_textures.py` |
| `assets/sounds/loops/` | deployed loops + `CREDITS.txt`; `raw/`, `om617_raw/` = source recordings; `SOURCES.md` = licences |

## Design notes

### Engine sound
- `DieselAudio` reads `rpmEngine`/`throttle` from the stock engine FMOD event (`EngineState`) and
  sets the stock event's `vehicleType` label via a Harmony prefix on
  `CarAudioManager.SetLabeledParameterIfPresent` (borrowing another vehicle's engine sound).
- `DieselLayer` plays a bank of seamless loops from `UserData/WombatGD/Sounds` through the FMOD
  core API, positioned at the engine, crossfading neighbouring RPMs and pitching only slightly.
- No openly licensed OM617 recording exists. The `om617` bank is **synthesized** by
  `tools/synth_om617.py` (firing order 1-2-4-5-3, 144° firing interval → rpm/24 Hz, knock,
  half-order warble) and EQ-matched to a W124 300D reference with `tools/match_spectrum.py`
  → `tools/om617_eq.npy`. `make_loop.py` cuts loops from recordings; `analyze_engine.py`
  estimates RPM from a recording.

### Coal smoke
- `CoalSmoke` clones the stock exhaust system (`ExhaustParticlesManager.exhaustParticleSystem.noDamageParticleSystem`)
  per material and uses only `Emit(EmitParams)`, with textures from `SmokeAssets` (`LoadRawTextureData`).
  CrashGuard stage `smoke`.

### Antenna and snorkel
- Antenna holder `Visual/Antenna` at car-local (0.71, 1.11, 1.57) (front passenger fender).
  `AntennaMount` raycasts the `Collider/Top` MeshCollider to find the windscreen foot and rake
  (logs `Antenna: windscreen probe…`). Fallback (0.78, 1.42, 0.78), 12°. After moving, the
  `AntennaBone` joint is re-seated by toggling the bone GameObject.

### Multiplayer
- `NetSync`: Fusion reliable data, tag `"WGGD"` `0x57474744`, protocol 1, vtable-swap receive
  hook (game layer §Fusion), chaining with other mods.
- Payload = `Profile` wire format v1:
  `[version:1][vehicleId:4][flags:1][label:str][soundSet:str][17 x float32]`. Flags: snorkel 1,
  diesel layer 2, hide stock exhaust 4, sooty 8, smoke screen 16, pillar antenna 32 (older
  versions ignore bit 32). Every float is range-checked. Strings ≤ 32 bytes of `[A-Za-z0-9 _-]`.
  Change the format → bump `WireVersion` and add tests.
- Remote trucks use their owner's profile; trucks of players without the mod use the local
  settings (`Fallback`, smoke screen off).

## Changelog

| Version | Date | Change |
| --- | --- | --- |
| (unreleased) | 2026-11-15 | Analyzer/format cleanups; `Profile` wire format moved to `Core/` + 21 tests. No behaviour change intended; **needs an in-game smoke test** (load, MP sync). |
| 0.5.4 | 2026-11-11 | Antenna moved to the windscreen pillar (collider probe). |
| 0.5.3 | 2026-11-10 | Smoke clones stock exhaust, `Emit` only; fixed MP crashes (CrashGuard stage `smoke`). Confirmed in MP as client. |
| 0.5.2 | 2026-11-10 | Detour forwards only keys 1/2 (+ chained). CrashGuard added. |
| 0.5.1 | 2026-11-10 | Network receive via vtable swap, 3 s after spawn. |
| 0.5.0 | 2026-11-10 | Profile sync (Harmony on Fusion; broke MP map load, see game layer). |
| ≤0.4 | 2026-11-09/10 | Snorkel, diesel sound, coal smoke, F7 menu. |

## Mod-specific lessons

- Snorkel side and engine label are applied once per car, so re-apply them when the menu changes
  them (`ApplyLocalChanges`).
- `analyze_engine.firing()` reports sub-harmonics on spiky envelopes; feed it smoothed envelopes
  (as `main()` does).
