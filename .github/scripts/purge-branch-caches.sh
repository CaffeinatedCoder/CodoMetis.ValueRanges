#!/usr/bin/env bash
# Deletes the Actions caches of branches that no longer exist (#476). A cache is scoped to the ref
# that wrote it, and a run can restore only its own branch's caches or the default branch's, so
# once a branch is gone its caches are dead weight until the 7-day idle expiry. Worse, they count
# towards the 10 GB limit, so eviction pushes out caches that live branches still use.
# Automatic Dependency Submission writes about 300 MB of NuGet cache for every pushed branch.
#
#   purge-branch-caches.sh [--dry-run] <branch>   deletes every cache of refs/heads/<branch>
#   purge-branch-caches.sh [--dry-run]            deletes every cache whose branch is gone
#
# Needs a token that can write Actions (GH_TOKEN, or a logged-in gh) and the repository in
# GH_REPO, or a checkout whose remote gh can read. --dry-run lists what would go and deletes nothing.
set -euo pipefail

dry_run=false
if [[ ${1:-} == --dry-run ]]; then
  dry_run=true
  shift
fi

repo=${GH_REPO:-$(gh repo view --json nameWithOwner --jq .nameWithOwner)}

# Lists the caches as "<id> <bytes> <ref>", one per line.
caches() {
  gh api --paginate "repos/$repo/actions/caches?per_page=100" \
    --jq '.actions_caches[] | "\(.id) \(.size_in_bytes) \(.ref)"'
}

purge() {
  local id size ref count=0 bytes=0
  while read -r id size ref; do
    [[ -n $id ]] || continue
    echo "$ref  $(( size / 1048576 )) MB  cache $id"
    $dry_run || gh api --method DELETE "repos/$repo/actions/caches/$id" --silent
    count=$(( count + 1 ))
    bytes=$(( bytes + size ))
  done
  echo "$($dry_run && echo Would delete || echo Deleted) $count caches, $(( bytes / 1048576 )) MB."
}

if (( $# == 1 )); then
  caches | awk -v ref="refs/heads/$1" '$3 == ref' | purge
  exit 0
fi

# The caches are listed before the branches. A cache listed first was written by a branch that
# existed then, so a branch pushed meanwhile is in the second list, and its fresh cache is kept.
listed=$(caches)
branches=$(gh api --paginate "repos/$repo/branches?per_page=100" --jq '.[].name' | sed 's|^|refs/heads/|')

# Only refs/heads/* is swept: a tag's or a pull request's cache is not tied to a branch's lifetime.
awk 'NR == FNR { live[$0] = 1; next } $3 ~ /^refs\/heads\// && !($3 in live)' \
  <(printf '%s\n' "$branches") <(printf '%s\n' "$listed") | purge
