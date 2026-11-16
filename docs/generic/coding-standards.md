# Coding standards

Layer: **generic** (any Unity IL2CPP / MelonLoader mod repo in WWTHP).

The rules below are enforced by tooling where possible. `tools/check.sh` runs all of it.
Anything not enforced is a review rule. The IL2CPP safety rules in
[`AGENTS.md`](../../AGENTS.md) and the game layer (`docs/games/<game>/`) take priority over everything here.

## Enforcement summary

| What | Tool | Config | Gate |
| --- | --- | --- | --- |
| C# formatting | `dotnet format whitespace` | `.editorconfig` | must be clean |
| C# style / naming / imports | `dotnet format style` (IDE rules) | `.editorconfig` | no warnings |
| C# code analysis | .NET analyzers, `latest-recommended` | `Directory.Build.props` + `.editorconfig` | zero warnings with `-p:StrictBuild=true` |
| Python lint (incl. security) | `ruff check` | `pyproject.toml` | must be clean |
| Python formatting | `ruff format` | `pyproject.toml` | must be clean |
| Tests + coverage | xUnit / pytest | see [testing.md](testing.md) | all pass, thresholds met |
| CI | GitHub Actions `ci.yml` | runs `tools/check.sh` without the game | must be green to merge |

Fix formatting automatically with `tools/check.sh --fix`, then review the diff.

---

## C# (mods)

### Project setup
- Target `net6.0` (MelonLoader 0.7.x), `LangVersion latest`, `Nullable disable`, `AllowUnsafeBlocks true`.
- Every game reference is `<Private>false</Private>`. Never ship game or MelonLoader DLLs.
- Keep the deploy targets conditioned on `'$(Deploy)' != 'false'` so checks can build without
  touching the game install.
- Shared settings live in `Directory.Build.props`. Don't repeat them in each `.csproj`.

### Layout and naming (enforced)
- 4 spaces, LF, Allman braces, `using` directives outside the namespace, `System` first.
- Types, methods, properties, public/internal fields: `PascalCase`.
- Private static mutable fields: `_camelCase` (`_slot`, `_next`). Private static readonly: `PascalCase` (`Inbox`).
- Locals and parameters: `camelCase`. **Exception:** Harmony's magic parameter names (`__instance`, `__result`).
- Accessibility modifiers always written (`private`, `internal`).
- Seal classes that have no subclasses (`CA1852`). Use `Array.Empty<T>()` rather than `new T[0]`.
- To keep a hand-aligned block (matrices, tables), wrap it in
  `#pragma warning disable format` / `#pragma warning restore format` with a reason.

### File organisation
- One concern per file (`Settings.cs`, `Menu.cs`, `Net.cs`, `CrashGuard.cs`, feature files).
- `Mod.cs` (or `<Name>Mod.cs`) only wires lifecycle callbacks to subsystems. No logic.
- **`Core/` folder = pure C#.** No `UnityEngine`, `Il2Cpp*`, `MelonLoader` or `HarmonyLib`.
  Data formats, parsing, validation, maths, state machines go here, as `partial` classes
  when the game side needs the same type. The test project compiles `Core/` alone, so a stray
  game reference breaks the test build.

### Error handling
- Every entry point called by the game (`OnUpdate`, Harmony patches, native detours) must not
  let an exception escape a native frame. Catch, log once (not every frame), and degrade.
- Catching `Exception` is allowed and expected at those boundaries (`CA1031` is off), but
  **never silently**: log with context, or record why the error is ignored in a comment.
- Validate everything from the network or from files at the boundary: length, version,
  ranges, NaN/Infinity, allowed characters. Return `null` / reject; don't coerce.
  Use the helpers in `Core/Validate.cs` (from the skeleton). `oth-wombatgd`'s `Profile.Deserialize`
  is a full reference implementation.
- Risky native or engine work is wrapped in `CrashGuard.Enter/Clear` with a named stage.

### IL2CPP specifics
- Prefer reading state in `OnUpdate` over patching. Patch with Harmony only on methods whose
  parameters are reference types or primitives.
- Compare IL2CPP enums via `.ToString()` or the interop enum, never by assumed integer values.
- Check `obj == null || obj.WasCollected` before touching cached game objects.
- Cache lookups (`FindObjectsOfType`, `GetComponent`) and refresh them on spawn events, not
  every frame.
- Never allocate per-frame in hot paths when avoidable (particles, audio updates).

### Comments and docs
- Every class gets a `<summary>` explaining *what it does in the game* and *why it is built
  this way* when the reason is a past failure (e.g. "vtable swap because Harmony corrupted
  ReliableKey").
- Comments explain why, not what. Reference the knowledge base for long stories.
- Keep the wire format documented next to (de)serialisation code.

### Versioning
- `MelonInfo` version follows semver. Bump the patch for fixes and the minor for features.
  Bump the protocol constant (`Protocol` / `WireVersion`) whenever the network format changes.
- Record notable changes in `docs/project/README.md` (changelog) and lessons in the right layer
  (see [repo-management.md](repo-management.md)).

---

## Python (`tools/`)

- Python ≥ 3.12, run via `.venv/bin/python`. Dependencies pinned in `requirements-dev.txt`.
- `ruff` rule sets: pycodestyle, pyflakes, isort, bugbear, pyupgrade, naming, simplify,
  bandit (security), numpy. Line length 110.
- Each script: module docstring with purpose and `Usage:`; a `main()`; `if __name__ == "__main__":`.
- Keep signal and maths functions **pure** (arrays in, arrays out) and separate from I/O
  (`decode`, `load`, `main`) so they can be unit tested.
- Randomness takes an explicit `np.random.Generator` so output is reproducible.
- `subprocess` only with argument lists (never `shell=True`) and `check=True`.
- `zip(..., strict=True)` when lengths must match.
- When changing a generator script, prove the output is unchanged (or intended to change) by
  hashing its output before and after with a fixed seed.

---

## Shell

- `#!/usr/bin/env bash` + `set -uo pipefail` (add `-e` when no step may fail).
- Scripts must work from any working directory (`cd "$(dirname "$0")/.."`) and in CI
  (no game installed: skip game-dependent steps, don't fail them).
- Interactive shells may alias `cp`/`rm` (e.g. `cp -i`). In agent one-liners use `\cp -f` / `\rm -rf`.

## Assets

- Generated assets are reproducible from a script in `tools/` with the parameters recorded.
- Recorded assets need a licence entry (`assets/**/SOURCES.md`). CC BY credits ship with them.
  Never use NC/ND-licensed or unlicensed material.
- Media (`*.wav`, `*.ogg`, `*.png`, `*.rgba`, `*.npy`, …) is stored with Git LFS (`.gitattributes`).
