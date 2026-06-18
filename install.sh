#!/usr/bin/env bash
# Atalho na raiz: ./install.sh
exec "$(cd "$(dirname "$0")" && pwd)/scripts/install.sh" "$@"
