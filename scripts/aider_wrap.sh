#!/bin/bash
# Run aider, tee output, append the per-message cost to the shared cost log.
# Then revert any workflow-file change (GITHUB_TOKEN cannot push those).
~/.local/bin/aider "$@" 2>&1 | tee /tmp/aider.out
c=$(grep -oE '\$[0-9.]+ message' /tmp/aider.out | tail -1 | grep -oE '[0-9.]+')
[ -n "$c" ] && python3 scripts/cost.py add "$c"

git fetch origin main --quiet 2>/dev/null || true
git checkout origin/main -- .github/workflows/ ci-build-test.yml 2>/dev/null || true
git add .github/workflows ci-build-test.yml 2>/dev/null
git -c user.name=cleaning-agent -c user.email=agent@readysetsiivous.fi commit -m "chore: revert workflow changes" 2>/dev/null || true
