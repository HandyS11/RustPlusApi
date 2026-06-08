#!/usr/bin/env bash
# Runs the test suite (both TFMs) with coverage and prints a per-class summary.
set -euo pipefail
cd "$(dirname "$0")/../.."
rm -rf TestResults
dotnet test tests/RustPlusApi.Tests/RustPlusApi.Tests.csproj \
  --settings tests/RustPlusApi.Tests/coverlet.runsettings \
  --results-directory ./TestResults
python3 - <<'PY'
import xml.etree.ElementTree as ET, glob
for f in sorted(glob.glob('TestResults/**/coverage.opencover.xml', recursive=True)):
    t = ET.parse(f); s = t.getroot().find('.//Summary')
    print(f, "->", f"seq={s.attrib['sequenceCoverage']}% branch={s.attrib['branchCoverage']}%")
    for cls in t.getroot().iter('Class'):
        name = cls.findtext('FullName') or '?'
        cs = cls.find('Summary')
        if cs is None: continue
        n = int(cs.attrib.get('numSequencePoints', '0'))
        if n == 0: continue
        sq, br = cs.attrib.get('sequenceCoverage','0'), cs.attrib.get('branchCoverage','0')
        if float(sq) < 100 or float(br) < 100:
            print(f"  {sq:>6}/{br:>6}  {name}")
PY
