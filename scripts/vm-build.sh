#!/bin/zsh
# From the Mac: build (and optionally install) inside the Parallels "Windows 11" VM.
# The project lives in iCloud Drive, which the VM sees as Y:\.
#   ./scripts/vm-build.sh            # build
#   ./scripts/vm-build.sh -Install   # build + install to %LOCALAPPDATA%\Programs\LightsaberCursor
set -euo pipefail
VM="${LIGHTSABER_VM:-Windows 11}"
prlctl exec "$VM" --current-user powershell -NoProfile -ExecutionPolicy Bypass \
    -File 'Y:\Projects\lightsaber-cursor-windows\scripts\build.ps1' "$@"
