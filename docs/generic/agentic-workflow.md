# Agentic workflow

Layer: **generic**. How AI agents (Claude Code or others) should work in WWTHP mod repos. The key constraint:
**an agent cannot run the game.** Everything an agent says about in-game behaviour is a
hypothesis until the user tests it and the logs confirm it. Work so each in-game test
answers as much as possible.

## The loop

```
understand → locate → plan → change → verify (check.sh) → deploy? → user tests → read logs → record
```

1. **Understand.** Read `AGENTS.md`, `docs/project/README.md`, the game layer
   (`docs/games/<game>/`), and the debugging tables in the game layer and
   [il2cpp-melonloader.md](il2cpp-melonloader.md). Check the issue/board for prior work.
2. **Locate.** Find the game types and members (`.game/` from the game's `regen-decomp.sh`,
   `tools/dump_types.py`, `ilspycmd`). Confirm every Unity/IL2CPP API you plan to call exists
   in this build *before* writing code around it.
3. **Plan.** For anything touching networking, native hooks, or particles/physics creation,
   state the plan and the riskiest assumption to the user first. Pick the safest hook
   (polling → Harmony on simple methods → vtable swap).
4. **Change.** Small, single-hypothesis changes. Put pure logic in `Core/` with tests.
   Add log lines that will prove or disprove the hypothesis from the log alone.
5. **Verify.** `tools/check.sh` must pass. It builds with `-p:Deploy=false` and never touches
   the game.
6. **Deploy only when the user is ready to test.** `dotnet build -c Release` (no
   `Deploy=false`) overwrites the DLL in the user's live game. Don't deploy while the game is
   running (`pgrep -fa '<game process>'`) or when the user has removed a mod for testing.
   Bump the version first, so the log shows which build ran.
7. **Ask for evidence.** Tell the user exactly what to try (single player / client / host), and
   which log lines to look for. Then read `MelonLoader/Latest.log` **and** Proton `Player.log`
   yourself, since they are local files.
8. **Record.** Put the lesson in the right layer ([repo-management.md](repo-management.md)):
   project → `docs/project/`. Game or generic → PR to `mod-template`, then sync. Update the
   mod's changelog. Close or update the issue on the board.

## Reporting rules

- Separate **verified** (tests passed, built, log showed X) from **untested** (needs an in-game
  run). Never say "fixed" for in-game behaviour the user hasn't confirmed.
- Say when the deployed DLL differs from the source (built but not deployed, or the reverse).
- If a check fails, report the failure output. Don't weaken a test, threshold or analyzer
  rule to make it pass without saying so and why.

## Debugging crashes and hangs

- **One hypothesis per build.** (oth-wombatgd 0.5.x: crashes first blamed on networking; the
  CrashGuard marker showed they were in the smoke stage.) Instrument before you guess.
- Silent process death → suspect an exception crossing a native callback, or native code in
  a guarded stage. Look for `in_progress_<stage>.txt`.
- Map load hangs in MP → suspect anything running during the Fusion join handshake.
- When the MelonLoader log is clean, the answer is usually in `Player.log`.

## Safety boundaries for agents

- **Never** run anything that writes into the game folder except the build's deploy step (and
  the game layer's documented scripts), and only when deployment is intended. Never edit or delete `UserData/` files without asking. They
  hold the user's settings and CrashGuard state.
- Never delete or overwrite assets, recordings or decompiled sources without checking what
  they are first. `rm` is aliased to trash here; still treat deletes as irreversible.
- SDK or system package installs go through the user (`sudo dnf …`). Agents can't run
  installer scripts in auto mode. `pip install` into `.venv` is fine; then update
  `requirements-dev.txt` with the pinned version.
- Downloaded files (recordings, references) go in their own new folder. Record their licence
  before use and never execute them.
- Git and GitHub: follow [repo-management.md](repo-management.md). Work on a branch, open a PR,
  merge after green checks. Ask before destructive operations (deleting repos/branches with
  unmerged work, force-push, making anything public).

## Working with multiple agents

- **One agent per repo.** Mods are independent repos, so parallel agents only meet in
  `mod-template`. Keep template PRs small and sync them out promptly.
- Hooks that chain (the network vtable slot) must keep working in **any** load order. When
  changing one mod's `Detour`, re-read the other mod's and test both installed together.
- Hand-offs: leave the state in the repo (issue comments, `docs/project/`, PR descriptions):
  what was tried, what the log showed, what's next. Never only in chat or private agent memory.
- Use subagents for broad searches (decompiled dumps, logs that are megabytes long) and keep
  only the conclusion. Don't delegate the decision on a native hook or a crash cause.

## Reading big files efficiently

- `Player.log` is often over 1 MB: `grep -n -E 'Exception|Error|<ModName>|Fusion' Player.log | tail -50`.
- `.game/assembly-csharp-dump.txt` is hundreds of KB: grep for the type, then read only those lines.
- Interop signatures: `~/.dotnet/tools/ilspycmd -t Il2Cpp.<Type> <Il2CppAssemblies>/Assembly-CSharp.dll`.

## Definition of done

- [ ] `tools/check.sh` passes (format, lint, strict build, tests, coverage).
- [ ] New pure logic is in `Core/` and tested. New untrusted-input parsing has the full
      malformed-input test set.
- [ ] Version bumped if behaviour changed. Protocol version bumped if the wire format changed.
- [ ] In-game test steps and expected log lines given to the user, or results recorded.
- [ ] Lessons recorded in the right layer. PR description says what's verified and what needs in-game testing.
- [ ] CI green on the PR.
