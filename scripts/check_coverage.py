#!/usr/bin/env python3
"""Check branch coverage from a cobertura report. Run from api/."""
import xml.etree.ElementTree as ET, glob, sys

files = glob.glob("TestResults/**/coverage.cobertura.xml", recursive=True)
if not files:
    print("no coverage file found", file=sys.stderr)
    sys.exit(1)

b = float(ET.parse(files[0]).getroot().get("branch-rate", 0))
print(f"branch coverage {b*100:.1f}%")
sys.exit(0 if b >= 0.10 else 1)
