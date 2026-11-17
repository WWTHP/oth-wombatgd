import zipfile
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


def test_find_version_mod_cs_const_returns_version():
    src = 'public sealed class Mod : MelonMod\n{\n    public const string Version = "0.2.2";\n}\n'

    assert release.find_version([src]) == "0.2.2"


def test_find_version_meloninfo_literal_returns_version():
    src = '[assembly: MelonInfo(typeof(WombatGD.WombatGDMod), "WombatGD", "0.5.4", "jims")]'

    assert release.find_version(["// other file", src]) == "0.5.4"


def test_find_version_const_and_meloninfo_referencing_it_returns_version():
    srcs = [
        '[assembly: MelonInfo(typeof(M.Mod), "M", M.Mod.Version, "WWTHP")]\n'
        'public const string Version = "1.2.3";'
    ]

    assert release.find_version(srcs) == "1.2.3"


@pytest.mark.parametrize(
    "srcs",
    [
        [],
        ["no version here"],
        ['public const string Version = "1.2";'],  # not x.y.z
        ['public const string Version = "1.2.3-beta";'],
        ['const string Version = "1.0.0";', 'const string Version = "2.0.0";'],  # disagree
        ['const string Version = "1.0.0";', '[assembly: MelonInfo(typeof(X), "X", "1.0.1", "a")]'],
    ],
)
def test_find_version_missing_malformed_or_conflicting_raises(srcs):
    with pytest.raises(ValueError):
        release.find_version(srcs)


def test_parse_release_files_valid_lines_return_entries():
    text = """
# assets the mod loads from UserData
assets/smoke/*.rgba -> UserData/WombatGD/Smoke
assets/sounds/loops/*   ->   UserData/WombatGD/Sounds/   # trailing slash ok
"""

    assert release.parse_release_files(text) == [
        ("assets/smoke/*.rgba", "UserData/WombatGD/Smoke"),
        ("assets/sounds/loops/*", "UserData/WombatGD/Sounds"),
    ]


def test_parse_release_files_backslashes_normalised():
    assert release.parse_release_files("a/b.txt -> UserData\\X") == [("a/b.txt", "UserData/X")]


def test_parse_release_files_empty_returns_nothing():
    assert release.parse_release_files("\n# only a comment\n") == []


@pytest.mark.parametrize(
    "line",
    [
        "assets/x.wav",  # no arrow
        "assets/x.wav -> ",  # no folder
        " -> UserData/X",  # no source
        "a -> b -> c",
        "/etc/passwd -> UserData/X",  # absolute source
        "assets/x.wav -> /tmp",  # absolute folder
        "../secret -> UserData/X",  # escapes the repo
        "assets/x.wav -> UserData/../../Windows",  # escapes the game folder
        "assets/x.wav -> C:/Games",  # drive letter
    ],
)
def test_parse_release_files_malformed_or_escaping_rejected(line):
    with pytest.raises(ValueError, match="line 1"):
        release.parse_release_files(line)


def test_plan_package_places_dll_in_mods_and_files_in_folders(tmp_path):
    dll = tmp_path / "M.dll"
    a = tmp_path / "a.wav"

    plan = release.plan_package(dll, [(a, "UserData/M/Sounds")])

    assert [arc for _, arc in plan] == ["Mods/M.dll", "UserData/M/Sounds/a.wav"]


def test_plan_package_same_target_twice_raises(tmp_path):
    a1 = tmp_path / "one" / "a.wav"
    a2 = tmp_path / "two" / "a.wav"

    with pytest.raises(ValueError, match="collide"):
        release.plan_package(tmp_path / "M.dll", [(a1, "UserData/X"), (a2, "UserData/X")])


def test_write_zip_contains_planned_files_with_content(tmp_path):
    dll = tmp_path / "M.dll"
    dll.write_bytes(b"dll")
    wav = tmp_path / "a.wav"
    wav.write_bytes(b"wav")
    out = tmp_path / "M-1.0.0.zip"

    release.write_zip(out, [(dll, "Mods/M.dll"), (wav, "UserData/M/a.wav")])

    with zipfile.ZipFile(out) as z:
        assert z.namelist() == ["Mods/M.dll", "UserData/M/a.wav"]
        assert z.read("UserData/M/a.wav") == b"wav"


def test_write_zip_same_input_gives_identical_bytes(tmp_path):
    f = tmp_path / "M.dll"
    f.write_bytes(b"x")
    z1, z2 = tmp_path / "1.zip", tmp_path / "2.zip"

    release.write_zip(z1, [(f, "Mods/M.dll")])
    release.write_zip(z2, [(f, "Mods/M.dll")])

    assert z1.read_bytes() == z2.read_bytes()


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


@pytest.mark.parametrize(
    "text",
    [
        "- MelonLoader **v0.7.3**. Mods target net6.0",
        "MelonLoader 0.7.3 is required",
        "MelonLoader v0.7.3",
    ],
)
def test_melonloader_version_game_notes_returns_version(text):
    assert release.melonloader_version([text]) == "0.7.3"


def test_melonloader_version_same_version_twice_returns_it():
    assert release.melonloader_version(["MelonLoader **v0.7.3**", "uses MelonLoader v0.7.3"]) == "0.7.3"


@pytest.mark.parametrize(
    "notes",
    [
        [],
        ["no loader mentioned"],
        ["MelonLoader **v0.7**"],  # not x.y.z
        ["MelonLoader v0.7.3", "MelonLoader v0.6.1"],  # two games disagree
    ],
)
def test_melonloader_version_missing_or_ambiguous_returns_none(notes):
    assert release.melonloader_version(notes) is None


def test_build_notes_with_melonloader_names_version():
    notes = release.build_notes("M", "1.0.0", None, "00", "0.7.3")

    assert "into the game (version **0.7.3**)." in notes


def test_build_notes_without_melonloader_omits_version():
    notes = release.build_notes("M", "1.0.0", None, "00")

    assert "into the game." in notes
    assert "version" not in notes.split("\n")[1]


def test_build_notes_package_says_unzip_and_hashes_zip():
    notes = release.build_notes("W", "0.5.5", None, "ff", "0.7.3", "W-0.5.5.zip")

    assert "Unzip `W-0.5.5.zip` into the game folder" in notes
    assert "`W-0.5.5.zip` SHA-256: `ff`" in notes
    assert "Mods/` folder (replace" not in notes
