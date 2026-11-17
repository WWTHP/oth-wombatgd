"""Publish a GitHub release of the mod, with the built DLL attached.

    .venv/bin/python tools/release.py --dry-run   # check everything, print the notes, publish nothing
    .venv/bin/python tools/release.py             # publish v<Mod.Version>

CI can't build mods (no game DLLs), so releases are built on a machine with the game installed.
The tool refuses unless: on `main`, clean, level with `origin/main`, the tag `v<version>` doesn't
exist yet (bump `Mod.Version` first), and `tools/check.sh` passes. It then builds
`<Mod>/<Mod>.csproj` in Release without deploying and runs `gh release create v<version>`. The
DLL is attached. The notes hold install steps, this version's changelog row from
`docs/project/README.md`, the DLL's SHA-256, and GitHub's generated list of merged PRs.

Publishing is outward-facing: agents run it only when the user asks for a release.
"""

from __future__ import annotations

import argparse
import hashlib
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VERSION_RE = re.compile(r'const\s+string\s+Version\s*=\s*"(\d+\.\d+\.\d+)"')
SKIP_DIRS = {"tests", "skeleton"}


def find_mods(root: Path) -> list[str]:
    """Mod projects by convention: `<Name>/<Name>.csproj`, excluding tests/ and skeleton/."""
    return sorted(
        d.name
        for d in root.iterdir()
        if d.is_dir() and d.name not in SKIP_DIRS and (d / f"{d.name}.csproj").is_file()
    )


def pick_mod(mods: list[str], wanted: str | None) -> str:
    """The mod to release: `wanted` if given (must exist), else the only one."""
    if wanted:
        if wanted not in mods:
            raise ValueError(f"no mod project {wanted!r} (found: {', '.join(mods) or 'none'})")
        return wanted
    if len(mods) != 1:
        raise ValueError(f"found {len(mods)} mod projects ({', '.join(mods) or 'none'}); pass --mod")
    return mods[0]


def parse_version(mod_cs: str) -> str:
    """`x.y.z` from `public const string Version = "x.y.z";` in Mod.cs. Exactly one is required."""
    found = VERSION_RE.findall(mod_cs)
    if len(found) != 1:
        raise ValueError(f'expected one `const string Version = "x.y.z"` in Mod.cs, found {len(found)}')
    return found[0]


def changelog_entry(readme: str, version: str) -> str | None:
    """The change text of the changelog table row `| <version> | <date> | <change> |`, if any."""
    row = re.compile(rf"^\|\s*{re.escape(version)}\s*\|[^|\n]*\|(.*)\|\s*$", re.MULTILINE)
    m = row.search(readme)
    return m.group(1).strip() if m else None


def build_notes(mod: str, version: str, change: str | None, sha256: str) -> str:
    """Release notes (Markdown). GitHub's generated PR list is appended by `gh`."""
    lines = [
        f"## Install {mod} {version}",
        "1. Install [MelonLoader](https://github.com/LavaGang/MelonLoader) into the game "
        "(version: see the game notes in `docs/games/`).",
        f"2. Put `{mod}.dll` into the game's `Mods/` folder (replace any older copy).",
        "3. Start the game. Settings are in `UserData/MelonPreferences.cfg`.",
        "",
        "## Changes",
        change or "See the changelog in `docs/project/README.md`.",
        "",
        "If something goes wrong, attach `MelonLoader/Latest.log` to an issue.",
        "",
        f"`{mod}.dll` SHA-256: `{sha256}`",
    ]
    return "\n".join(lines) + "\n"


def sha256_of(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


# ------------------------------------------------------------------ CLI glue


def _run(*cmd: str, cwd: Path = ROOT, capture: bool = True) -> str:  # pragma: no cover - subprocess glue
    result = subprocess.run(cmd, cwd=cwd, check=False, text=True, capture_output=capture)
    if result.returncode != 0:
        detail = (result.stderr or result.stdout or "").strip() if capture else ""
        raise SystemExit(f"release: `{' '.join(cmd)}` failed{': ' + detail if detail else ''}")
    return (result.stdout or "").strip() if capture else ""


def _preflight(tag: str) -> str:  # pragma: no cover - git glue
    """Refuses unless the checkout is exactly origin/main and the tag is unused. Returns the commit."""
    if _run("git", "rev-parse", "--abbrev-ref", "HEAD") != "main":
        raise SystemExit("release: switch to main first")
    if _run("git", "status", "--porcelain"):
        raise SystemExit("release: working tree not clean")
    _run("git", "fetch", "-q", "origin", "main", "--tags")
    head = _run("git", "rev-parse", "HEAD")
    if head != _run("git", "rev-parse", "origin/main"):
        raise SystemExit("release: main isn't level with origin/main (pull or push first)")
    if _run("git", "ls-remote", "--tags", "origin", f"refs/tags/{tag}"):
        raise SystemExit(f"release: {tag} already exists; bump Mod.Version (and the changelog) first")
    return head


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mod", help="mod project to release (default: the only one)")
    ap.add_argument(
        "--dry-run", action="store_true", help="check, build and print the notes; publish nothing"
    )
    args = ap.parse_args()

    try:
        mod = pick_mod(find_mods(ROOT), args.mod)
        version = parse_version((ROOT / mod / "Mod.cs").read_text(encoding="utf-8"))
    except (ValueError, OSError) as e:
        raise SystemExit(f"release: {e}") from e
    tag = f"v{version}"
    readme = ROOT / "docs" / "project" / "README.md"
    change = changelog_entry(readme.read_text(encoding="utf-8"), version) if readme.is_file() else None
    if change is None:
        print(f"release: warning: no changelog row for {version} in docs/project/README.md", file=sys.stderr)

    head = _preflight(tag)
    print(f"release: {mod} {version} at {head[:9]}: running tools/check.sh")
    _run("tools/check.sh", capture=False)
    _run("dotnet", "build", "-c", "Release", "-p:Deploy=false", "-v", "q", "-nologo", cwd=ROOT / mod)
    dll = ROOT / mod / "bin" / "Release" / "net6.0" / f"{mod}.dll"
    if not dll.is_file():
        raise SystemExit(f"release: build produced no {dll.relative_to(ROOT)} (is the game installed?)")

    notes = build_notes(mod, version, change, sha256_of(dll))
    cmd = [
        "gh", "release", "create", tag, str(dll),
        "--target", head, "--title", f"{mod} {version}", "--notes", notes, "--generate-notes",
    ]  # fmt: skip
    if args.dry_run:
        print(f"release: dry run, would publish {tag} with {dll.relative_to(ROOT)}:\n\n{notes}")
        return
    print(_run(*cmd))


if __name__ == "__main__":
    main()
