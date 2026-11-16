#!/usr/bin/env bash
# Regenerates the local, NEVER-COMMITTED reference copies of Over the Hill's code:
#   .game/assembly-csharp-dump.txt   all types/fields/methods in Assembly-CSharp (grep this first)
#   .game/decomp/<Type>.cs           ilspycmd signatures for the types named on the command line
#
#   tools/games/over-the-hill/regen-decomp.sh [Il2Cpp.CarAudioManager Fusion.NetworkRunner ...]
#
# The game's code is Funselektor's copyright: .game/ is git-ignored. Don't move it elsewhere.
set -euo pipefail
cd "$(dirname "$0")/../../.."

GAME_DIR="${GAME_DIR:-$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/over the hill}"
INTEROP="$GAME_DIR/MelonLoader/Il2CppAssemblies"
PY="${PYTHON:-.venv/bin/python}"
ILSPY="${ILSPY:-$HOME/.dotnet/tools/ilspycmd}"

[[ -f "$INTEROP/Assembly-CSharp.dll" ]] || { echo "No interop assemblies at $INTEROP (run the game once with MelonLoader)" >&2; exit 1; }
mkdir -p .game/decomp

"$PY" tools/dump_types.py "$INTEROP/Assembly-CSharp.dll" > .game/assembly-csharp-dump.txt
echo "wrote .game/assembly-csharp-dump.txt ($(wc -l < .game/assembly-csharp-dump.txt) lines)"

# Default set: the types the existing mods were built around.
types=("$@")
(( ${#types[@]} )) || types=(Il2Cpp.CarAudioManager Il2Cpp.ExhaustParticlesManager Il2Cpp.OverlandNetworkRunner
                             Il2Cpp.ChallengeTrail Il2Cpp.ChallengeTrailInfo Il2Cpp.CarKicker Il2Cpp.AudioEmitter)
for t in "${types[@]}"; do
    dll="$INTEROP/Assembly-CSharp.dll"
    [[ "$t" == Fusion.* ]] && dll="$INTEROP/Il2CppFusion.Runtime.dll"
    if "$ILSPY" -t "$t" "$dll" > ".game/decomp/$t.cs" 2>/dev/null; then
        echo "wrote .game/decomp/$t.cs"
    else
        echo "warning: could not decompile $t" >&2; rm -f ".game/decomp/$t.cs"
    fi
done
