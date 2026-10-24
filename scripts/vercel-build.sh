#!/usr/bin/env bash
set -euo pipefail
if ! command -v dotnet >/dev/null; then
  curl --fail --silent --show-error --location https://dot.net/v1/dotnet-install.sh -o /tmp/lumiere-dotnet-install.sh
  bash /tmp/lumiere-dotnet-install.sh --version 10.0.402 --install-dir /tmp/lumiere-dotnet
  export PATH="/tmp/lumiere-dotnet:$PATH"
fi
node scripts/build-frontend.mjs
