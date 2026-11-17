from pathlib import Path

import pytest

import release


def _mod(root: Path, name: str) -> None:
    (root / name).mkdir()
    (root / name / f"{name}.csproj").write_text("<Project />")


def test_find_mods_mod_folders_returns_sorted_names(tmp_path):
    _mod(tmp_path, "Zeta")
    _mod(tmp_path, "Alpha")

    assert release.find_mods(tmp_path) == ["Alpha", "Zeta"]


def test_find_mods_tests_skeleton_and_loose_folders_ignored(tmp_path):
    for skip in ("tests", "skeleton"):
        _mod(tmp_path, skip)
    (tmp_path / "docs").mkdir()
    (tmp_path / "Other").mkdir()
    (tmp_path / "Other" / "Different.csproj").write_text("<Project />")

    assert release.find_mods(tmp_path) == []


def test_pick_mod_single_mod_returns_it():
    assert release.pick_mod(["FuelManagement"], None) == "FuelManagement"


def test_pick_mod_named_mod_returns_it():
    assert release.pick_mod(["A", "B"], "B") == "B"


@pytest.mark.parametrize(("mods", "wanted"), [([], None), (["A", "B"], None), (["A"], "Missing")])
def test_pick_mod_ambiguous_or_missing_raises(mods, wanted):
    with pytest.raises(ValueError):
        release.pick_mod(mods, wanted)


def test_parse_version_mod_cs_returns_version():
    src = 'public sealed class Mod : MelonMod\n{\n    public const string Version = "0.2.2";\n}\n'

    assert release.parse_version(src) == "0.2.2"


@pytest.mark.parametrize(
    "src",
    [
        "no version here",
        'public const string Version = "1.2";',  # not x.y.z
        'public const string Version = "1.2.3-beta";',
        'const string Version = "1.0.0"; const string Version = "2.0.0";',  # ambiguous
    ],
)
def test_parse_version_missing_or_malformed_raises(src):
    with pytest.raises(ValueError):
        release.parse_version(src)


README = """# Project

## Changelog

| Version | Date | Change |
| --- | --- | --- |
| 0.2.2 | 2026-11-16 | Tick scale on the dial. |
| 0.2.1 | 2026-11-16 | Cloned dial. |
| 0.1.0 | 2026-11-15 | First version. |
"""


def test_changelog_entry_existing_version_returns_change():
    assert release.changelog_entry(README, "0.2.2") == "Tick scale on the dial."


def test_changelog_entry_does_not_match_prefix_versions():
    assert release.changelog_entry(README, "0.2") is None
    assert release.changelog_entry(README, "0.1.0") == "First version."


def test_changelog_entry_missing_version_returns_none():
    assert release.changelog_entry(README, "9.9.9") is None


def test_changelog_entry_dots_are_literal():
    assert release.changelog_entry("| 0x2y2 | d | wrong |\n", "0.2.2") is None


def test_build_notes_contains_install_change_and_hash():
    notes = release.build_notes("FuelManagement", "0.2.2", "Tick scale.", "ab" * 32)

    assert "Put `FuelManagement.dll` into the game's `Mods/` folder" in notes
    assert "Tick scale." in notes
    assert "ab" * 32 in notes


def test_build_notes_no_change_points_to_changelog():
    assert "See the changelog" in release.build_notes("M", "1.0.0", None, "00")


def test_sha256_of_file_matches_known_digest(tmp_path):
    f = tmp_path / "x.dll"
    f.write_bytes(b"abc")

    assert release.sha256_of(f) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
