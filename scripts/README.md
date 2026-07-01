# scripts/

Scripts do fluxo **pack-ptbr** v0.1.2 e dev.

## Release / pack

| Script | `make` |
|--------|--------|
| `assemble-pack-ptbr.sh` | `pack-ptbr` |
| `release-pack-ptbr.sh` | `release-pack-ptbr` |
| `verify-pack-zip.sh` | `verify-pack-ptbr` |
| `build-copiar-installer-native.sh` | (dentro de `pack-ptbr`) |
| `instalador-linux.sh` | copiado como `Instalador-Linux` no zip |
| `install-pack-files.sh` | lógica de cópia (Linux/Windows) |
| `fetch-bepinex-vendor.sh` | `fetch-bepinex-vendor` |
| `validate-bepinex-zip.sh` | validação do zip BepInEx |

## Instalação / dev

| Script | `make` |
|--------|--------|
| `install.sh` | `install` |
| `uninstall.sh` | (no pack + manual) |
| `verify-install.sh` | `verify-install` |
| `install-paths.sh` | caminhos Proton 342940 |
| `apply-bepinex-minimal-logging.sh` | `apply-bepinex-minimal-logging` |
| `apply-bepinex-verbose-logging.sh` | `apply-bepinex-verbose-logging` |

Legado: `deletar/scripts-legado/` e `deletar/scripts/` (Python).
