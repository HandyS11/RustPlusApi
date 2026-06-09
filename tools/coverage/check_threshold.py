#!/usr/bin/env python3
"""Fail (exit 1) if any coverage report is below the given line/branch thresholds.

Usage: check_threshold.py <line_min> <branch_min>
Scans TestResults/**/coverage.opencover.xml (one per target framework) and checks
each report's overall sequence (line) and branch coverage against the floors.
"""
import sys
import glob
import xml.etree.ElementTree as ET

if len(sys.argv) != 3:
    print("Usage: check_threshold.py <line_min> <branch_min>")
    sys.exit(2)

line_min, branch_min = float(sys.argv[1]), float(sys.argv[2])
reports = glob.glob('TestResults/**/coverage.opencover.xml', recursive=True)
if not reports:
    print("No coverage reports found under TestResults/")
    sys.exit(1)

failed = False
for f in sorted(reports):
    summary = ET.parse(f).getroot().find('.//Summary')
    seq = float(summary.attrib['sequenceCoverage'])
    branch = float(summary.attrib['branchCoverage'])
    ok = seq >= line_min and branch >= branch_min
    failed = failed or not ok
    print(f"[{'OK' if ok else 'FAIL'}] {f}: line={seq}% branch={branch}% (min {line_min}/{branch_min})")

sys.exit(1 if failed else 0)
