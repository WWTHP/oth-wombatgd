#!/usr/bin/env bash
# Regenerates the local reference copies of Over the Hill's code OUTSIDE every repo:
#   $DECOMP_DIR/assembly-csharp-dump.txt   all types/fields/methods in Assembly-CSharp (grep this first)
#   $DECOMP_DIR/types/<Type>.cs            ilspycmd output for the named types
#
#   tools/games/over-the-hill/regen-decomp.sh [Il2Cpp.CarAudioManager Il2CppFusion.Sockets.ReliableKey ...]
#
# DECOMP_DIR defaults to <repo>/../decomp/over-the-hill, i.e. ~/projects/mods/wwthp/decomp/over-the-hill
# when repos are cloned side by side in ~/projects/mods/wwthp/. That folder is not a repo.
#
# These are MelonLoader interop stubs (signatures + calls into native code), not the game's logic.
# They are still Funselektor's: use them only for interoperability, never commit or share them.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../.." && pwd)"

GAME_DIR="${GAME_DIR:-$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/over the hill}"
DECOMP_DIR="${DECOMP_DIR:-$(dirname "$repo")/decomp/over-the-hill}"
INTEROP="$GAME_DIR/MelonLoader/Il2CppAssemblies"
PY="${PYTHON:-$repo/.venv/bin/python}"
ILSPY="${ILSPY:-$HOME/.dotnet/tools/ilspycmd}"

[[ -f "$INTEROP/Assembly-CSharp.dll" ]] || { echo "No interop assemblies at $INTEROP (run the game once with MelonLoader)" >&2; exit 1; }
# Refuse to write inside any git work tree (check the nearest existing ancestor).
probe="$DECOMP_DIR"
while [[ ! -d "$probe" ]]; do probe="$(dirname "$probe")"; done
if git -C "$probe" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    echo "refusing: $DECOMP_DIR is inside a git work tree; set DECOMP_DIR outside every repo" >&2
    exit 1
fi
mkdir -p "$DECOMP_DIR/types"

"$PY" "$repo/tools/dump_types.py" "$INTEROP/Assembly-CSharp.dll" > "$DECOMP_DIR/assembly-csharp-dump.txt"
echo "wrote $DECOMP_DIR/assembly-csharp-dump.txt ($(wc -l < "$DECOMP_DIR/assembly-csharp-dump.txt") lines)"

# Default set: the types the existing mods were built around.
types=("$@")
(( ${#types[@]} )) || types=(Il2Cpp.CarAudioManager Il2Cpp.AudioEmitter Il2Cpp.ExhaustParticlesManager
                             Il2Cpp.DamageParticleSystem Il2Cpp.OverlandNetworkRunner Il2Cpp.ChallengeTrail
                             Il2Cpp.ChallengeTrailInfo Il2Cpp.CarKicker Il2Cpp.PointOfInterest
                             Il2Cpp.PointOfInterestInfo Il2Cpp.RegionInfo
                             Il2CppFusion.NetworkRunner Il2CppFusion.Sockets.ReliableKey)
for t in "${types[@]}"; do
    dlls=("$INTEROP/Assembly-CSharp.dll")
    [[ "$t" == Il2CppFusion.* ]] && dlls=("$INTEROP"/Il2CppFusion.*.dll)
    out="$DECOMP_DIR/types/$t.cs"
    ok=0
    for dll in "${dlls[@]}"; do
        if "$ILSPY" -t "$t" "$dll" > "$out" 2>/dev/null && [[ -s "$out" ]]; then ok=1; break; fi
    done
    if [[ $ok == 1 ]]; then echo "wrote $out"; else echo "warning: could not decompile $t" >&2; rm -f "$out"; fi
done
