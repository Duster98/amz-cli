#!/usr/bin/env bash
# Compila la SPA e la incorpora nella CLI come WebAssets/web.zip.
set -euo pipefail
cd "$(dirname "$0")"

if [[ ! -d node_modules ]]; then
  npm ci --no-audit --no-fund
fi

npm run build

DEST="$(cd .. && pwd)/src/AmazonScraper.Cli/WebAssets/web.zip"
mkdir -p "$(dirname "$DEST")"
python3 - "$PWD/dist" "$DEST" <<'PY'
import pathlib
import sys
import zipfile

source = pathlib.Path(sys.argv[1])
destination = pathlib.Path(sys.argv[2])
destination.parent.mkdir(parents=True, exist_ok=True)
if destination.exists():
    destination.unlink()

files = sorted(path for path in source.rglob("*") if path.is_file())
with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for file in files:
        archive.write(file, file.relative_to(source).as_posix())

print(f"Web UI: {len(files)} file, {destination.stat().st_size / 1024:.1f} KB -> {destination}")
PY
