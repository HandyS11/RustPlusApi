#!/usr/bin/env bash
# Runs the full test suite (all projects, both TFMs) with coverage, merges the
# per-project/per-TFM opencover reports into one aggregate, and prints a per-class summary.
set -euo pipefail
cd "$(dirname "$0")/../.."
rm -rf TestResults
dotnet test RustPlusApi.sln \
  --settings tests/RustPlusApi.UnitTests/coverlet.runsettings \
  --results-directory ./TestResults
dotnet tool restore

# Two reports, two gates. The library gate must stay exactly what it was before the web app
# existed: merging a net10.0-only ASP.NET app into it would lower the bar the libraries clear.
dotnet tool run reportgenerator -- \
  "-reports:TestResults/**/coverage.opencover.xml" \
  "-targetdir:TestResults/merged" \
  "-assemblyfilters:-RustPlusApi.CredentialsWeb" \
  "-reporttypes:Cobertura"

dotnet tool run reportgenerator -- \
  "-reports:TestResults/**/coverage.opencover.xml" \
  "-targetdir:TestResults/merged-web" \
  "-assemblyfilters:+RustPlusApi.CredentialsWeb" \
  "-reporttypes:Cobertura"

python3 - <<'PY'
import xml.etree.ElementTree as ET
for label, path in (('libraries', 'TestResults/merged/Cobertura.xml'),
                    ('web app  ', 'TestResults/merged-web/Cobertura.xml')):
    root = ET.parse(path).getroot()
    line = float(root.attrib['line-rate']) * 100
    branch = float(root.attrib['branch-rate']) * 100
    print(f"{label} -> line={line:.2f}% branch={branch:.2f}%")
    for cls in root.iter('class'):
        name = cls.attrib.get('name', '?')
        lr = float(cls.attrib.get('line-rate', '1')) * 100
        br = float(cls.attrib.get('branch-rate', '1')) * 100
        if lr < 100 or br < 100:
            print(f"  line={lr:6.2f}% branch={br:6.2f}%  {name}")
PY

python3 tools/coverage/check_threshold.py 95 90
python3 tools/coverage/check_threshold.py 95 90 TestResults/merged-web/Cobertura.xml
