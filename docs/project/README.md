# Project: mod-template

Project layer for **this** repo. In a mod repo, replace this file with the mod's own
description (what it does in game, design, settings/keys, network messages, changelog,
mod-specific lessons). It is project-owned and never synced.

- **Repo:** `WWTHP/mod-template`, a GitHub template repository.
- **Game(s):** generic, plus game layers under `docs/games/` (currently: Over the Hill).
- **Contents:** knowledge layers (`docs/generic/`, `docs/games/`), rules (`AGENTS.md`), tooling
  (`tools/`), the new-mod skeleton (`skeleton/`), reference snippets (`snippets/`), CI and
  GitHub templates (`.github/`).
- **Checks:** `tools/check.sh` here generates a throwaway mod from `skeleton/` and tests it,
  so a broken skeleton fails CI.

## Working on the template

- Changes are PRs. After merging, sync every mod repo (`tools/template-sync.sh` in each, see
  `docs/generic/repo-management.md`) so they get the change.
- Keep layers clean: only facts true for any IL2CPP/MelonLoader mod go in `docs/generic/`.
  Game facts go in `docs/games/<game>/`. Mod facts stay in the mod repo.
- When adding a path that mod repos should receive, add it to `.template-manifest`.

## Changelog

- 2026-11-15: created from the `overthehill` workspace (WombatGD + AbominableSnowman) knowledge
  base, coding standards, testing strategy and tooling.
