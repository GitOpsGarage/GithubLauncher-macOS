#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
git diff --cached --check
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
"$DOTNET_BIN" build GithubLauncher.csproj -c Release -v quiet
"$DOTNET_BIN" run --project tests/MacCompatibility.Tests.csproj
python3 - <<'PY'
import json
from pathlib import Path
for name in ('catalog.json', 'quiver-catalog.json', 'mac-compatibility.json'):
    json.loads(Path(name).read_text())
print('Catalog JSON valid.')
PY
bash -n scripts/build-macos.sh scripts/check.sh
