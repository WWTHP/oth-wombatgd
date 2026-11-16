# oth-wombatgd

**WombatGD**: an [Over the Hill](docs/games/over-the-hill/README.md) MelonLoader mod that turns
the Wombat into a 1980 Mercedes 300GD: passenger-side snorkel, pillar antenna, OM617 diesel
sound, coal smoke with a smoke-screen key, F7 menu, and multiplayer profile sync.

```bash
git lfs install && python3 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt
tools/check.sh                          # all checks, no deploy
cd WombatGD && dotnet build -c Release  # build + deploy to the game
```

Details: [docs/project/README.md](docs/project/README.md). Agents: start at [AGENTS.md](AGENTS.md).
Sound credits: [assets/sounds/loops/CREDITS.txt](assets/sounds/loops/CREDITS.txt).
