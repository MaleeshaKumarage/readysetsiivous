#!/usr/bin/env python3
"""Track pipeline LLM cost in a shared log (persists across jobs on the runner).

Usage:
  python3 cost.py reset          # clear the log at run start
  python3 cost.py add <amount>   # append a dollar cost
  python3 cost.py total          # print the sum
"""
import os, sys

LOG = os.path.expanduser("~/.cache/pipeline-cost.log")
cmd = sys.argv[1] if len(sys.argv) > 1 else "total"

if cmd == "reset":
    open(LOG, "w").close()
elif cmd == "add":
    open(LOG, "a").write(sys.argv[2] + "\n")
elif cmd == "total":
    vals = [float(x) for x in open(LOG).read().split() if x]
    print(f"{sum(vals):.4f}")
