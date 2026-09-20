#!/usr/bin/env bash
set -euo pipefail

PROJECT="Jellyfin.Plugin.BackgroundUpscaler.csproj"
VERSION="$(python3 - <<'PY'
import xml.etree.ElementTree as ET
root = ET.parse('Jellyfin.Plugin.BackgroundUpscaler.csproj').getroot()
version = root.findtext('.//Version')
if not version:
    raise SystemExit('Version not found in csproj')
print(version)
PY
)"

rm -rf dist
dotnet restore "$PROJECT"
dotnet build "$PROJECT" -c Release --no-restore

mkdir -p dist/plugin
cp "bin/Release/net10.0/Jellyfin.Plugin.BackgroundUpscaler.dll" dist/plugin/

(
  cd dist/plugin
  zip -9 "../BackgroundUpscaler_${VERSION}.zip" Jellyfin.Plugin.BackgroundUpscaler.dll
)

echo "Created dist/BackgroundUpscaler_${VERSION}.zip"
