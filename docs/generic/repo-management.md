# Repositories and knowledge flow (WWTHP)

Layer: **generic**. How the WWTHP GitHub organisation is organised and how knowledge moves
between repos. Agents managing the repos follow this file.

## Organisation layout

| Repo | Role |
| --- | --- |
| `WWTHP/mod-template` | **Template repository.** Shared knowledge, rules, tooling, skeleton. Every mod repo is created from it. |
| `WWTHP/<game>-<mod>` | One repo per mod, e.g. `oth-wombatgd`, `oth-abominable-snowman`. |

- All repos are **private**.
- **Grouping:** name prefix per game (`oth-` = Over the Hill), topics (`<game-slug>`,
  `melonloader-mod`, `il2cpp`, and `template` for the template), and one GitHub Project board
  per game (e.g. "Over the Hill mods") that tracks issues from all of its repos.
- Free plan: no branch protection on private repos. The rule is still **PR + green CI to
  merge to `main`**. Agents don't push directly to `main` except for initial bootstrap.

## Knowledge layers

Every repo has the same three layers. Agents read them in this order:

| Layer | Path | Owned by | Contains |
| --- | --- | --- | --- |
| generic | `docs/generic/` | template | workflow, coding standards, testing, IL2CPP/MelonLoader lessons, this file |
| game | `docs/games/<game>/` | template | paths, game classes, networking, rules and incidents specific to that game, mod registry |
| project | `docs/project/` | the mod repo | what this mod does, its design, its changelog, its own lessons |

`AGENTS.md` (template-owned) is the entry point. `CLAUDE.md` imports it plus `docs/project/README.md`.

## Template-managed files

`.template-manifest` lists the paths the template owns. In a mod repo:
- **Never edit managed files.** `tools/template-sync.sh` overwrites them. Change them in
  `mod-template` instead.
- `.template-version` records the template commit the repo was last synced to.
- Project-owned: `README.md`, `docs/project/`, the mod folder(s), `tests/<Mod>.Tests/`,
  `tests/python/` (except the template's own tool tests listed in the manifest, e.g. `test_release.py`), `assets/`, project tools (`tools/*.py` that aren't in the manifest), and
  `requirements-dev.txt` (starts as the template's copy. Keep the template's pins and add your own).

## Flows

### Template → mods (sync)
After a change merges in `mod-template`, run in each mod repo:
```bash
tools/template-sync.sh          # branch chore/template-sync-<sha>, commit, push, PR
```
Then `tools/check.sh` locally (CI can't build mods). Merge when green. A template change that
tightens a check may need fixes in the mod. Do those on the same PR.

### Mod → template (promotion)
When work on a mod teaches something:
1. Decide the layer: only this mod → `docs/project/`. This game → `docs/games/<game>/`.
   Any IL2CPP/MelonLoader mod → `docs/generic/`.
2. Project lessons: commit in the mod repo.
3. Game or generic lessons: open a PR to `mod-template` (or a **Lesson learned** issue if it isn't
   written up yet), then sync it back into every mod repo of that game.
4. Shared code that was improved in one mod (e.g. CrashGuard, the network hook) goes back to
   `skeleton/` or `snippets/` the same way.

### New mod
```bash
gh repo create WWTHP/<game>-<slug> --private --template WWTHP/mod-template --clone
cd <game>-<slug>
python3 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt
tools/new-mod.sh <PascalName>
gh api repos/WWTHP/mod-template/commits/main -q .sha > .template-version
# describe the mod in README.md + docs/project/README.md, register its keys/tags in the game layer (template PR)
gh repo edit --add-topic <game-slug> --add-topic melonloader-mod --add-topic il2cpp
gh project item-add <board number> --owner WWTHP --url <issue url>   # track work on the board
```

### New game
Add `docs/games/<game>/README.md` (environment, rules, classes, registry) and
`tools/games/<game>/regen-decomp.sh` to the template, plus a board and a prefix.

## Releases

CI can't build mods (no game DLLs in CI), so releases are built on a machine with the game
installed, from `main`:

```bash
# 1. bump Mod.Version + add the changelog row in docs/project/README.md, PR, merge
git switch main && git pull
.venv/bin/python tools/release.py --dry-run   # checks + build, prints the notes, publishes nothing
.venv/bin/python tools/release.py             # tags v<version> at HEAD, uploads <Mod>.dll
```

`tools/release.py` refuses unless the checkout is on `main`, clean and level with `origin/main`,
the tag `v<Mod.Version>` doesn't exist yet, and `tools/check.sh` passes. The notes contain install
steps, this version's changelog row, the SHA-256 and GitHub's list of merged PRs. `--mod
<Name>` picks one when a repo has several mods. The version comes from `const string Version` or
the `MelonInfo` literal.

Mods that load files from `UserData/` list them in a project-owned `release-files.txt`. The
release is then a zip laid out like the game folder, so players unzip it into the game:

```text
# <glob relative to the repo> -> <folder relative to the game>
assets/smoke/*.rgba    -> UserData/WombatGD/Smoke
assets/sounds/loops/*  -> UserData/WombatGD/Sounds
```

Ship licence/credit files with media (e.g. `CREDITS.txt` in the same folder), see AGENTS rule 13.

- Release only when the user asks: it's outward-facing.
- Repos are private, so only people with access to the repo can download releases. Making a repo public is
  the user's decision (check the game's EULA and the developer's stance on mods first, see the game layer).

## Git conventions

- Branches: `<type>/<slug>` (`feat/`, `fix/`, `chore/`, `docs/`, `refactor/`, `test/`, `ci/`), under 50 chars.
- Commits: conventional commits, scope = mod or area (`fix(net): …`, `docs(oth): …`), imperative,
  ≤72 chars, body explains why. One logical change per commit. `tools/check.sh` green at each commit.
- PRs: conventional title, fill in the template (verified / needs in-game testing / knowledge).
  Squash WIP branches; merge clean histories as they are.
- Never commit: build output, `.venv`, decompiled game code (it lives in `wwthp/decomp/`, outside every repo), the game's DLLs, secrets.
- Media goes through Git LFS (`.gitattributes`). Run `git lfs install` once per machine.

## Agent permissions in WWTHP

The owner has delegated full repo management to agents: creating repos, pushing branches,
opening/merging PRs after green checks, issues, labels, topics, project boards, syncing
templates. Agents still **ask first** before deleting repos or branches with unmerged work,
force-pushing, rewriting history on `main`, making a repo public, or changing org settings
and membership.
