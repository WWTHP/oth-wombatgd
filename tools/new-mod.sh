#!/usr/bin/env bash
# Creates a new mod from skeleton/: <Name>/ (MelonLoader project) and tests/<Name>.Tests/.
#
#   tools/new-mod.sh <Name> [--root DIR]
#
# <Name> must be a C# identifier in PascalCase (e.g. WombatGD, AbominableSnowman).
# --root writes into another directory (used by check.sh to self-test the skeleton).
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"

name="${1:-}"
root="$here"
if [[ "${2:-}" == "--root" ]]; then root="${3:?--root needs a directory}"; fi

if [[ ! "$name" =~ ^[A-Z][A-Za-z0-9]{1,40}$ ]]; then
    echo "usage: tools/new-mod.sh <PascalCaseName> [--root DIR]" >&2
    exit 2
fi
if [[ "$name" == "ModName" ]]; then echo "pick a real name" >&2; exit 2; fi
if [[ -e "$root/$name" || -e "$root/tests/$name.Tests" ]]; then
    echo "refusing to overwrite: $root/$name or $root/tests/$name.Tests exists" >&2
    exit 1
fi

mkdir -p "$root/tests"
cp -R "$here/skeleton/ModName" "$root/$name"
cp -R "$here/skeleton/ModName.Tests" "$root/tests/$name.Tests"
mv "$root/$name/ModName.csproj" "$root/$name/$name.csproj"
mv "$root/tests/$name.Tests/ModName.Tests.csproj" "$root/tests/$name.Tests/$name.Tests.csproj"
grep -rl 'ModName' "$root/$name" "$root/tests/$name.Tests" | xargs sed -i "s/ModName/$name/g"

echo "Created $root/$name and $root/tests/$name.Tests"
if [[ "$root" == "$here" ]]; then
    cat <<EOF
Next:
  1. Edit MelonGame/MelonInfo in $name/Mod.cs and pick a free menu key (docs/games/<game>/README.md registry).
  2. Describe the mod in docs/project/README.md and README.md.
  3. tools/check.sh
EOF
fi
