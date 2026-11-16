#!/usr/bin/env bash
# Runs every automated check: format, lint, strict build, unit tests, coverage.
# Never deploys to the game (builds pass -p:Deploy=false).
#
#   tools/check.sh          verify only (run before declaring work done)
#   tools/check.sh --fix    apply formatters / auto-fixable lint first
#
# Discovers projects by convention:
#   <Mod>/<Mod>.csproj          mod projects (need the game installed to build)
#   tests/<Name>.Tests/*.csproj test projects (compile Core/ only; run anywhere, incl. CI)
#   tests/python/test_*.py      pytest suite for tools/
# Mod builds are SKIPPED (not failed) when the game's MelonLoader files are missing, e.g. in CI.
# In the template repo (no mods yet) the skeleton is generated into a temp dir and tested.
#
# Environment: PYTHON (default .venv/bin/python), GAME_DIR (default: Over the Hill, Flatpak Steam).
set -uo pipefail
cd "$(dirname "$0")/.."

FIX=0
[[ "${1:-}" == "--fix" ]] && FIX=1

PY="${PYTHON:-.venv/bin/python}"
GAME_DIR="${GAME_DIR:-$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/over the hill}"
CORE_MIN=90   # % line coverage of Core/ code (untrusted-input parsing is a critical path)
failed=()
skipped=()

step() { printf '\n\033[1m== %s\033[0m\n' "$1"; }
run() {  # run <label> <command...>
    local label=$1; shift
    if "$@"; then echo "ok: $label"; else echo "FAIL: $label"; failed+=("$label"); fi
}

mapfile -t MODS < <(for f in */*.csproj; do d=${f%%/*}; [[ -f "$f" && "$d" != tests && "$d" != skeleton ]] && echo "$d"; done | sort -u)
mapfile -t TESTS < <(ls -d tests/*.Tests 2>/dev/null)
have_game=0
[[ -f "$GAME_DIR/MelonLoader/net6/MelonLoader.dll" ]] && have_game=1

# ---------------------------------------------------------------- Python
py_paths=()
[[ -d tools ]] && py_paths+=(tools)
[[ -d tests/python ]] && py_paths+=(tests/python)
if (( ${#py_paths[@]} )); then
    step "Python: lint + format (ruff)"
    if [[ $FIX == 1 ]]; then
        "$PY" -m ruff check --fix "${py_paths[@]}"
        "$PY" -m ruff format "${py_paths[@]}"
    fi
    run "ruff check" "$PY" -m ruff check "${py_paths[@]}"
    run "ruff format" "$PY" -m ruff format --check "${py_paths[@]}"
fi
if compgen -G "tests/python/test_*.py" >/dev/null; then
    step "Python: unit tests (pytest + coverage)"
    run "pytest" "$PY" -m pytest --cov
fi

# ---------------------------------------------------------------- C#
core_coverage() {  # core_coverage <test project dir>
    "$PY" -I - "$1" "$CORE_MIN" <<'PYEOF'
import glob, sys, xml.etree.ElementTree as ET
files = glob.glob(f"{sys.argv[1]}/TestResults/*/coverage.cobertura.xml")
rate = float(ET.parse(files[0]).getroot().get("line-rate")) * 100 if files else 0.0
print(f"core line coverage: {rate:.1f}%")
sys.exit(0 if rate >= float(sys.argv[2]) else 1)
PYEOF
}
test_project() {  # test_project <dir> <runsettings>
    local p=$1 settings=$2
    run "$p tests" bash -c "cd '$p' && rm -rf TestResults && dotnet test -p:StrictBuild=true -v q -nologo \
        --collect 'XPlat Code Coverage' --settings '$settings'"
    run "$p core coverage >= $CORE_MIN%" core_coverage "$p"
}

for p in "${TESTS[@]}"; do
    step "C#: format $p"
    [[ $FIX == 1 ]] && (cd "$p" && dotnet format whitespace -v q && dotnet format style --severity warn -v q)
    run "$p format" bash -c "cd '$p' && dotnet format whitespace --verify-no-changes -v q \
        && dotnet format style --verify-no-changes --severity warn -v q"
done

for p in "${MODS[@]}"; do
    if [[ $have_game == 0 ]]; then
        skipped+=("$p format + build (game not installed at GAME_DIR)")
        continue
    fi
    step "C#: format + strict build $p (no deploy)"
    [[ $FIX == 1 ]] && (cd "$p" && dotnet format whitespace -v q && dotnet format style --severity warn -v q)
    run "$p format" bash -c "cd '$p' && dotnet format whitespace --verify-no-changes -v q \
        && dotnet format style --verify-no-changes --severity warn -v q"
    run "$p build" bash -c "cd '$p' && dotnet build -c Release -p:Deploy=false -p:StrictBuild=true \
        -p:GameDir='$GAME_DIR' -v q -nologo -clp:ErrorsOnly"
done

for p in "${TESTS[@]}"; do
    step "C#: unit tests $p"
    test_project "$p" ../coverage.runsettings
done

# ---------------------------------------------------------------- skeleton self-test (template repo)
if (( ${#MODS[@]} == 0 )) && [[ -d skeleton ]]; then
    step "Template: generate a mod from skeleton/ and test it"
    tmp=$(mktemp -d)
    trap 'rm -rf "$tmp"' EXIT
    cp .editorconfig Directory.Build.props "$tmp"/
    mkdir -p "$tmp/tests" && cp tests/coverage.runsettings "$tmp/tests/"
    run "new-mod.sh" tools/new-mod.sh SkeletonCheck --root "$tmp"
    test_project "$tmp/tests/SkeletonCheck.Tests" ../coverage.runsettings
    run "skeleton format" bash -c "cd '$tmp/tests/SkeletonCheck.Tests' && dotnet format whitespace --verify-no-changes -v q \
        && dotnet format style --verify-no-changes --severity warn -v q"
    if [[ $have_game == 1 ]]; then
        run "skeleton strict build" bash -c "cd '$tmp/SkeletonCheck' && dotnet build -c Release -p:Deploy=false \
            -p:StrictBuild=true -p:GameDir='$GAME_DIR' -v q -nologo -clp:ErrorsOnly"
    else
        skipped+=("skeleton mod build (game not installed)")
    fi
fi

echo
for s in "${skipped[@]}"; do printf '\033[33mSKIPPED:\033[0m %s\n' "$s"; done
if (( ${#failed[@]} )); then
    printf '\033[31mFAILED:\033[0m %s\n' "${failed[@]}"
    exit 1
fi
printf '\033[32mAll checks passed.\033[0m\n'
