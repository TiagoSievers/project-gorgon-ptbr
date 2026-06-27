#!/usr/bin/env bash
# Mescla falas capturadas no jogo → output/pt-BR/npcs.yaml do repo
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 "$ROOT/scripts/merge-npcs-from-game.py" "$@"
