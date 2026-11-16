# Testing

Layer: **generic**.

Game mods can't be fully unit tested: IL2CPP interop types only work inside the running
game. So the strategy is **push logic into pure code that can be tested, and test the
rest in the game with a fixed checklist**.

## Layers

| Layer | What | How | Who runs it |
| --- | --- | --- | --- |
| Unit, C# | `<Mod>/Core/**` (wire formats, validation, maths) | xUnit in `tests/<Mod>.Tests/` | `tools/check.sh`, CI |
| Unit, Python | pure functions in `tools/*.py` | pytest in `tests/python/` | `tools/check.sh`, CI |
| Build | mods compile with zero analyzer warnings | `-p:StrictBuild=true` | `tools/check.sh` (needs the game; skipped in CI) |
| In-game | everything touching Unity, FMOD, Fusion | manual checklist below | the user |

## How the C# tests work

`tests/<Mod>.Tests/*.csproj` does **not** reference the mod project (loading it would pull in
IL2CPP). It compiles the mod's `Core/**/*.cs` source files directly:

```xml
<Compile Include="../../MyMod/Core/**/*.cs" Link="Core/%(RecursiveDir)%(Filename)%(Extension)" />
```

Because `internal` types are compiled into the test assembly, no `InternalsVisibleTo` is needed.
If someone adds `using UnityEngine;` to a Core file, the test project fails to compile.
That compile failure is the purity check.

New mods get this set up by `tools/new-mod.sh`. **Adding tests to an older mod that has none:**
1. Move pure logic into `<Mod>/Core/` (packet read/write, encodings, maths) as a `partial` class
   if needed. Keep behaviour identical, and prove it (hash outputs, or in-game check).
2. Copy `skeleton/ModName.Tests/` to `tests/<Mod>.Tests/`, rename, fix the `Compile Include` path.
3. `tools/check.sh` discovers `tests/*.Tests` automatically.

Tests target `net10.0` (the installed runtime); Core code must also compile for the mod's
`net6.0`. Don't use APIs newer than .NET 6 in Core.

## Conventions

These follow the global test standards, adapted for this repo.
- Names: C# `Method_Scenario_ExpectedOutcome`; Python `test_<unit>_<scenario>_<expected>`.
- Arrange / Act / Assert, one behaviour per test.
- No game, filesystem, network or ffmpeg in unit tests. Tests run in milliseconds.
- Randomness is seeded. A failing test is a bug, never a retry.
- Build test inputs with small factory helpers (e.g. `ValidProfile()`, `PacketWithFloats()`), not
  copied byte literals.
- Test inputs must be realistic. Example (oth-wombatgd): `analyze_engine.firing()` reports a
  sub-harmonic on single-sample spikes but is correct on the smoothed envelopes `main()`
  produces, so the test uses smoothed pulses.

## What must be tested

- **Every parser of untrusted input** (network packets, files a user can replace). Treat it as a
  critical path: valid round-trip, every field out of range, NaN/Infinity, truncation at every
  length, oversize, wrong version, disallowed characters. It must never throw.
- Any maths with a clear right answer (loop lengths, firing rates, ranges, geometry helpers).
- Bug fixes: add a test that fails before the fix whenever the logic is in Core.

## Coverage thresholds (enforced by `tools/check.sh`)

| Scope | Minimum | Notes |
| --- | --- | --- |
| C# `Core/` code (line) | 90% | network parsing counts as a critical path; aim for 95%+ |
| Python `tools/` (line) | 80% | `main()`/`decode()`/`load()` I/O glue and pure-CLI scripts are excluded in `pyproject.toml` |

Game-bound C# (everything outside `Core/`) has no line-coverage target. It's covered by the
in-game checklist. Don't add `[ExcludeFromCodeCoverage]` or `# pragma: no cover` to
pure code just to pass the gate.

**Check that the tests can fail.** For a new validator, temporarily break it (e.g. drop the
upper-bound check) and confirm a test fails, then restore it.

## In-game test checklist

Run by the user after `dotnet build -c Release` (game closed). Ask for both logs afterwards.

1. **Load:** MelonLoader log shows `<Mod> loaded` with the version, no errors or warnings from the mod.
2. **Single player:** feature works; menu key opens the menu; settings persist after restart.
3. **MP as client** (host has the mod): `Multiplayer sync ready`; synced state appears on both sides.
4. **MP as host**, and **MP with a vanilla peer**: the map loads, nothing is thrown at the vanilla peer.
5. **Several mods installed** (all networked mods of the same game): every hook reports ready and chains.
6. **Logs:** `Latest.log` and Proton `Player.log` have no new exceptions. No `in_progress_*.txt`
   marker is left in `UserData/<Mod>/` after quitting normally.
7. **Crash recovery** (when CrashGuard stages change): kill the game mid-stage. The next launch
   names the stage and disables it.
