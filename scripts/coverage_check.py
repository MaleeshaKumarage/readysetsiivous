#!/usr/bin/env python3
"""Check backend branch coverage from the cobertura report. Exits 1 if below threshold."""
import xml.etree.ElementTree as ET, glob, sys

f = glob.glob('TestResults/**/coverage.cobertura.xml', recursive=True)[0]
root = ET.parse(f).getroot()
line = float(root.get('line-rate', 0))
branch = float(root.get('branch-rate', 0))
print(f'coverage: line {line:.1%}, branch {branch:.1%}')
if branch < 0.10:
    print(f'FAIL: branch coverage {branch:.1%} below 10%')
    sys.exit(1)
