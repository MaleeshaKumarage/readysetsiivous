#!/bin/bash
# Run aider, tee output, append the per-message cost to the shared cost log.
~/.local/bin/aider "$@" 2>&1 | tee /tmp/aider.out
c=$(grep -oE '\$[0-9.]+ message' /tmp/aider.out | tail -1 | grep -oE '[0-9.]+')
[ -n "$c" ] && python3 scripts/cost.py add "$c"
