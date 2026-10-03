#!/bin/bash
# Run deepseek.py, append its COST (from stderr) to the shared cost log.
python3 scripts/deepseek.py "$@" 2>/tmp/ds.err
c=$(grep -oE 'COST=[0-9.]+' /tmp/ds.err | cut -d= -f2)
[ -n "$c" ] && python3 scripts/cost.py add "$c"
