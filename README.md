# mod-template

Template for WWTHP game mods (Unity IL2CPP + MelonLoader), with the accumulated knowledge
for modding in general and for each supported game.

```bash
gh repo create WWTHP/<game>-<slug> --private --template WWTHP/mod-template --clone
cd <game>-<slug> && python3 -m venv .venv && .venv/bin/pip install -r requirements-dev.txt
tools/new-mod.sh <PascalName> && tools/check.sh
```

Start with [AGENTS.md](AGENTS.md). Full procedure: [docs/generic/repo-management.md](docs/generic/repo-management.md).

| Layer | Path |
| --- | --- |
| generic | [docs/generic/](docs/generic/) |
| Over the Hill | [docs/games/over-the-hill/](docs/games/over-the-hill/README.md) |
| this repo | [docs/project/](docs/project/README.md) |
