#!/usr/bin/env python3
"""Truncate archify diagram labels so they fit node width (hard guarantee).

Usage: python3 truncate_diagram.py <candidate.json>
"""
import json, sys

p = sys.argv[1]
j = json.load(open(p))
for n in j.get('nodes', []):
    n['label'] = (n.get('label') or '')[:12]
    n['sublabel'] = (n.get('sublabel') or '')[:15]
    n.setdefault('width', 150)
for e in j.get('edges', []):
    e['label'] = (e.get('label') or '')[:15]
for l in j.get('lanes', []):
    l['label'] = (l.get('label') or '')[:12]
json.dump(j, open(p, 'w'))
