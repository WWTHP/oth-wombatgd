"""Publish a GitHub release of the mod, with the built DLL attached.

    .venv/bin/python tools/release.py --dry-run   # check everything, print the notes, publish nothing
    .venv/bin/python tools/release.py             # publish v<Mod.Version>

CI can't build mods (no game DLLs), so releases are built on a machine with the game installed.
The tool refuses unless: on `main`, clean, level with `origin/main`, the tag `v<version>` doesn't
exist yet (bump the version first), and `tools/check.sh` passes. It then builds
`<Mod>/<Mod>.csproj` in Release without deploying and runs `gh release create v<version>`.

The version is the mod's `const string Version = "x.y.z"` or the literal in
`[assembly: MelonInfo(typeof(...), "Name", "x.y.z", ...)]`, in any `.cs` file of the mod.

Attached: the DLL, or, when the repo has a `release-files.txt`, a zip laid out like the game
folder (`Mods/<Mod>.dll` + the listed files), for mods that need assets in `UserData/`. Each line
of `release-files.txt` is `<glob relative to the repo> -> <folder relative to the game>`, e.g.
`assets/sounds/loops/* -> UserData/WombatGD/Sounds`; `#` starts a comment.

The notes hold install steps (with the MelonLoader version named in
`docs/games/<game>/README.md`), this version's changelog row from `docs/project/README.md`, the
attachment's SHA-256, and GitHub's generated list of merged PRs.

Publishing is outward-facing: agents run it only when the user asks for a release.
"""

from __future__ import annotations

import argparse
import hashlib
import re
import subprocess
import sys
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parent.parent
ML_RE = re.compile(r"MelonLoader\s+\**v?(\d+\.\d+\.\d+)\**")
VERSION_RE = re.compile(r'const\s+string\s+Version\s*=\s*"(\d+\.\d+\.\d+)"')
MELONINFO_RE = re.compile(r'MelonInfo\(\s*typeof\([^)]*\)\s*,\s*"[^"]*"\s*,\s*"(\d+\.\d+\.\d+)"')
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


def find_version(sources: list[str]) -> str:
    """
    The mod version from its C# sources: `const string Version = "x.y.z"`, or the literal version in
    `[assembly: MelonInfo(typeof(...), "Name", "x.y.z", ...)]`. All declarations found must agree.
    """
    found = {v for src in sources for rx in (VERSION_RE, MELONINFO_RE) for v in rx.findall(src)}
    if len(found) != 1:
        raise ValueError(
            'expected one version (`const string Version = "x.y.z"` or a MelonInfo literal), '
            f"found {sorted(found) or 'none'}"
        )
    return found.pop()


def parse_release_files(text: str) -> list[tuple[str, str]]:
    """
    `release-files.txt` lines: `<glob relative to the repo> -> <folder relative to the game>`.
    Blank lines and `#` comments are skipped. Both sides must be relative and stay inside their
    root (no absolute paths, no `..`); anything else is rejected with its line number.
    """
    entries = []
    for n, raw in enumerate(text.splitlines(), 1):
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        parts = [p.strip() for p in line.split("->")]
        if len(parts) != 2 or not parts[0] or not parts[1]:
            raise ValueError(f"release-files.txt line {n}: expected `<glob> -> <game folder>`")
        for side in parts:
            p = PurePosixPath(side.replace("\\", "/"))
            if p.is_absolute() or ".." in p.parts or re.match(r"^[A-Za-z]:", side):
                raise ValueError(f"release-files.txt line {n}: {side!r} must be relative, without `..`")
        entries.append((parts[0], parts[1].replace("\\", "/").strip("/")))
    return entries


def plan_package(dll: Path, files: list[tuple[Path, str]]) -> list[tuple[Path, str]]:
    """
    Zip layout: `Mods/<dll>` plus each (source file, game folder) as `<folder>/<file name>`.
    Two sources landing on the same path are an error.
    """
    plan = [(dll, f"Mods/{dll.name}")]
    for src, folder in files:
        plan.append((src, f"{folder}/{src.name}"))
    names = [arc for _, arc in plan]
    dupes = sorted({a for a in names if names.count(a) > 1})
    if dupes:
        raise ValueError(f"release files collide in the zip: {', '.join(dupes)}")
    return plan


def write_zip(path: Path, plan: list[tuple[Path, str]]) -> None:
    """Writes the planned files into a zip (deflated, fixed timestamps so rebuilds are identical)."""
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for src, arc in plan:
            info = zipfile.ZipInfo(arc, date_time=(2000, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            z.writestr(info, src.read_bytes())


def changelog_entry(readme: str, version: str) -> str | None:
    """The change text of the changelog table row `| <version> | <date> | <change> |`, if any."""
    row = re.compile(rf"^\|\s*{re.escape(version)}\s*\|[^|\n]*\|(.*)\|\s*$", re.MULTILINE)
    m = row.search(readme)
    return m.group(1).strip() if m else None


def melonloader_version(game_notes: list[str]) -> str | None:
    """
    The MelonLoader version the game layer names (`MelonLoader **v0.7.3**` in
    `docs/games/<game>/README.md`). None unless the notes agree on exactly one version.
    """
    found = {m for text in game_notes for m in ML_RE.findall(text)}
    return found.pop() if len(found) == 1 else None


def build_notes(
    mod: str,
    version: str,
    change: str | None,
    sha256: str,
    melonloader: str | None = None,
    package: str | None = None,
) -> str:
    """
    Release notes (Markdown). GitHub's generated PR list is appended by `gh`. `package` is the zip's
    file name when the release ships one (mod with assets), else the bare DLL is described.
    """
    ml = f" (version **{melonloader}**)" if melonloader else ""
    if package:
        step = (
            f"2. Unzip `{package}` into the game folder (it holds `Mods/` and `UserData/`), "
            "replacing older copies."
        )
        asset = package
    else:
        step = f"2. Put `{mod}.dll` into the game's `Mods/` folder (replace any older copy)."
        asset = f"{mod}.dll"
    lines = [
        f"## Install {mod} {version}",
        f"1. Install [MelonLoader](https://github.com/LavaGang/MelonLoader) into the game{ml}.",
        step,
        "3. Start the game. Settings are in `UserData/MelonPreferences.cfg`.",
        "",
        "## Changes",
        change or "See the changelog in `docs/project/README.md`.",
        "",
        "If something goes wrong, attach `MelonLoader/Latest.log` to an issue.",
        "",
        f"`{asset}` SHA-256: `{sha256}`",
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
        raise SystemExit(f"release: {tag} already exists; bump the version (and the changelog) first")
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
        version = find_version([p.read_text(encoding="utf-8") for p in sorted((ROOT / mod).glob("*.cs"))])
        spec = ROOT / "release-files.txt"
        extra = parse_release_files(spec.read_text(encoding="utf-8")) if spec.is_file() else []
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

    attach = dll
    if extra:
        files = []
        for pattern, folder in extra:
            matched = sorted(p for p in ROOT.glob(pattern) if p.is_file())
            if not matched:
                raise SystemExit(f"release: release-files.txt: {pattern!r} matches no files")
            files += [(p, folder) for p in matched]
        try:
            plan = plan_package(dll, files)
        except ValueError as e:
            raise SystemExit(f"release: {e}") from e
        attach = dll.parent / f"{mod}-{version}.zip"
        write_zip(attach, plan)
        print(f"release: packaged {len(plan)} files into {attach.relative_to(ROOT)}")

    game_notes = [
        p.read_text(encoding="utf-8") for p in sorted((ROOT / "docs" / "games").glob("*/README.md"))
    ]
    melonloader = melonloader_version(game_notes)
    if melonloader is None:
        print("release: warning: no single MelonLoader version in docs/games/*/README.md", file=sys.stderr)
    package = attach.name if attach != dll else None
    notes = build_notes(mod, version, change, sha256_of(attach), melonloader, package)
    cmd = [
        "gh", "release", "create", tag, str(attach),
        "--target", head, "--title", f"{mod} {version}", "--notes", notes, "--generate-notes",
    ]  # fmt: skip
    if args.dry_run:
        print(f"release: dry run, would publish {tag} with {attach.relative_to(ROOT)}:\n\n{notes}")
        return
    print(_run(*cmd))


if __name__ == "__main__":
    main()
