#!/usr/bin/env bash
# Pulls the template-managed files (listed in .template-manifest) from WWTHP/mod-template
# into this repo and opens a pull request with the changes.
#
#   tools/template-sync.sh            sync from mod-template main, branch + commit + push + PR
#   tools/template-sync.sh --local    only update the working tree (review with git diff)
#   TEMPLATE_REF=<sha|branch> ...     sync from a specific template revision
#
# Managed paths are owned by the template: edit them THERE (PR to mod-template), never here,
# or the next sync overwrites the change. Everything else in this repo is project-owned.
set -euo pipefail
cd "$(dirname "$0")/.."

TEMPLATE_REPO="${TEMPLATE_REPO:-WWTHP/mod-template}"
TEMPLATE_REF="${TEMPLATE_REF:-main}"
LOCAL=0
[[ "${1:-}" == "--local" ]] && LOCAL=1

if [[ "$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null)" == "$TEMPLATE_REPO" ]]; then
    echo "This is the template itself; nothing to sync." >&2
    exit 1
fi
if [[ $LOCAL == 0 && -n "$(git status --porcelain)" ]]; then
    echo "Working tree not clean; commit or stash first (or use --local)." >&2
    exit 1
fi

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
gh repo clone "$TEMPLATE_REPO" "$tmp/t" -- -q
git -C "$tmp/t" checkout -q "$TEMPLATE_REF"
sha=$(git -C "$tmp/t" rev-parse HEAD)

# Manifest: one path per line (file or directory), '#' comments. Directories are mirrored
# exactly (files deleted in the template are deleted here too).
while IFS= read -r line; do
    path="${line%%#*}"; path="${path%"${path##*[![:space:]]}"}"
    [[ -z "$path" ]] && continue
    src="$tmp/t/$path"
    if [[ -d "$src" ]]; then
        mkdir -p "$path"
        rsync -a --delete "$src/" "$path/"
    elif [[ -f "$src" ]]; then
        mkdir -p "$(dirname "$path")"
        cp -p "$src" "$path"
    else
        echo "warning: $path is in the manifest but not in the template; removing it here" >&2
        rm -rf "$path"
    fi
done < "$tmp/t/.template-manifest"
echo "$sha" > .template-version

if [[ -z "$(git status --porcelain)" ]]; then
    echo "Already up to date with $TEMPLATE_REPO@${sha:0:7}."
    exit 0
fi
if [[ $LOCAL == 1 ]]; then
    echo "Updated working tree from $TEMPLATE_REPO@${sha:0:7}. Review with: git diff"
    exit 0
fi

branch="chore/template-sync-${sha:0:7}"
git switch -q -c "$branch"
git add -A
git commit -q -m "chore(template): sync with mod-template ${sha:0:7}" \
    -m "Automated sync of template-managed files (.template-manifest)."
git push -q -u origin "$branch"
gh pr create --title "chore(template): sync with mod-template ${sha:0:7}" \
    --body "Syncs template-managed files from $TEMPLATE_REPO@$sha.

Run \`tools/check.sh\` locally before merging; template changes can tighten checks."
