#!/usr/bin/env bash
# Valida zip do BepInEx (unzip -t)
set -euo pipefail

zip="${1:?zip}"
command -v unzip >/dev/null 2>&1 || exit 1
unzip -t "$zip" >/dev/null 2>&1
