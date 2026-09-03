#!/usr/bin/env python3
"""Fail (exit 1) if merged coverage is below the given line/branch thresholds.

Usage: check_threshold.py <line_min> <branch_min> [report_path]
Reads a merged Cobertura report produced by ReportGenerator (by default
TestResults/merged/Cobertura.xml, the union across every library test project
and TFM) and checks its overall line-rate and branch-rate against the floors.
"""
import sys
import os
import xml.etree.ElementTree as ET

if len(sys.argv) not in (3, 4):
    print("Usage: check_threshold.py <line_min> <branch_min> [report_path]")
    sys.exit(2)

line_min, branch_min = float(sys.argv[1]), float(sys.argv[2])
report = sys.argv[3] if len(sys.argv) == 4 else 'TestResults/merged/Cobertura.xml'
if not os.path.exists(report):
    print(f"Merged coverage report not found at {report}")
    sys.exit(1)

root = ET.parse(report).getroot()
# Cobertura stores rates as 0..1 fractions on the root <coverage> element.
line = float(root.attrib['line-rate']) * 100
branch = float(root.attrib['branch-rate']) * 100
ok = line >= line_min and branch >= branch_min
print(f"[{'OK' if ok else 'FAIL'}] {report}: line={line:.2f}% branch={branch:.2f}% (min {line_min}/{branch_min})")
sys.exit(0 if ok else 1)
