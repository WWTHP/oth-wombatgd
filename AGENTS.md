# AGENTS.md

Instructions for any agent (or human) working in a WWTHP mod repo. Template-managed: edit it in
`WWTHP/mod-template`, never in a mod repo.

## Read in this order

1. **This file**: rules that apply everywhere.
2. **Project layer**: [`docs/project/README.md`](docs/project/README.md): what this repo is, which game, its design and changelog.
3. **Game layer**: `docs/games/<game>/README.md` (e.g. [Over the Hill](docs/games/over-the-hill/README.md)): paths, logs, game rules, classes, registry, incidents.
4. **Generic layer** (`docs/generic/`), as needed:

| Doc | Read it when |
| --- | --- |
| [agentic-workflow.md](docs/generic/agentic-workflow.md) | before any task: work loop, reporting, safety, definition of done |
| [il2cpp-melonloader.md](docs/generic/il2cpp-melonloader.md) | building or debugging a mod: approach, hooks, crashes, assets |
| [coding-standards.md](docs/generic/coding-standards.md) | writing C#, Python or shell |
| [testing.md](docs/generic/testing.md) | adding logic or tests; in-game checklist |
| [repo-management.md](docs/generic/repo-management.md) | git, PRs, template sync, promoting lessons, new mods/repos |

## Verify before you say "done"

```bash
python3 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt   # once
tools/check.sh          # format + lint + strict build + unit tests + coverage; never deploys
tools/check.sh --fix    # apply formatters first
```

It must end with `All checks passed.` (Mod builds show as SKIPPED where the game isn't installed,
e.g. CI.) An agent cannot test in game: say what is verified and what still needs the
user's in-game test.

## Build and deploy

```bash
cd <Mod> && dotnet build -c Release                    # builds AND copies into the game's Mods/
cd <Mod> && dotnet build -c Release -p:Deploy=false    # build only
```

Deploying changes the user's live game. Only do it when they're about to test, never while the
game runs, and bump the mod version first.

Release (GitHub release with the DLL attached, only when the user asks):
`.venv/bin/python tools/release.py --dry-run`, then without `--dry-run`. Details: repo-management.md §Releases.

## Non-negotiable rules (generic)

Each one comes from a real crash or hang. The game layer adds game-specific rules.

1. **Never Harmony-patch or code-patch an IL2CPP method taking by-value structs** (keys, spans).
   Harmony is fine on methods with reference/primitive parameters.
2. **Intercept struct-taking virtual methods only with the vtable-slot swap** (generic doc §3).
3. **No exception may escape a native (`[UnmanagedCallersOnly]`) callback.** Catch everything and
   queue work to the main thread.
4. **Never run mod code inside the game's load/join handshake.** Hook late, once.
5. **Never send custom network data to peers not known to run the mod** (except one Hello).
6. **Mods must not depend on each other.** Shared code is copied (via the template), not referenced.
7. **Check that a Unity API exists in the game's build before using it.** Builds strip APIs.
8. **Prefer cloning the game's objects** to building complex components from scratch.
9. **Wrap risky stages in `CrashGuard`.**
10. **Guard engine calls that throw while loading** with try/catch.
11. **Parsers of untrusted input live in `Core/`** with full malformed-input tests. They reject, never coerce.
12. **Never commit the game's code** (decompiled sources, dumps, DLLs). They live in `../decomp/<game>/` (next to the repo clones, not a repo), written by `tools/games/<game>/regen-decomp.sh`.
13. **Licences**: only CC0/CC BY (or own) media, recorded in `SOURCES.md`, credits shipped.
14. **Don't weaken a check** (analyzer rule, threshold, assertion) to make it pass without telling the user why.
15. **Don't edit template-managed files in a mod repo** (`.template-manifest`). Change the template.

## Conventions

- Settings via `MelonPreferences` (one category per mod). UI via IMGUI. Every key configurable,
  and registered in the game layer's registry.
- Log through the mod's `MelonLogger.Instance`. Log what was found/skipped so a single in-game
  run can be diagnosed from the log.
- Conventional commits (`fix(net): …`), branch + PR + green CI (see repo-management.md).
- Python: `.venv/bin/python tools/<script>.py`.
