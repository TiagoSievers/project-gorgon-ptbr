#!/usr/bin/env bash
# Valida zip do BepInEx (integridade + dotnet/coreclr para Windows nativo)
set -euo pipefail

zip="${1:?zip}"
command -v unzip >/dev/null 2>&1 || exit 1
unzip -t "$zip" >/dev/null 2>&1

# Pacote IL2CPP no Windows exige dotnet/coreclr.dll na raiz do BepInExPack.
if ! unzip -l "$zip" | grep -qiE 'BepInExPack/dotnet/coreclr\.dll|BepInExPack\\dotnet\\coreclr\.dll'; then
  echo "Erro: $zip sem BepInExPack/dotnet/coreclr.dll (zip incompleto — use fetch-bepinex-vendor)" >&2
  exit 1
fi
