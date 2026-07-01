# BepInEx — log mínimo (performance)

O instalador aplica automaticamente via `scripts/apply-bepinex-minimal-logging.sh`.

| Setting | Valor | Motivo |
|---------|-------|--------|
| `UnityLogListening` | `false` | Evita capturar milhares de `Debug.Log` do Unity na thread principal |
| `[Logging.Console] Enabled` | `false` | Sem janela preta do BepInEx |
| `[Logging.Console] LogLevels` | `Error, Warning` | Se console reativado manualmente |
| `[Logging.Disk] LogLevels` | `All` | LogOutput.log com Info do plugin (tail/grep) |
| `[Logging.Disk] InstantFlushing` | `true` | Linhas aparecem no tail em tempo real |
| `[Logging.Disk] WriteUnityLog` | `false` | Não duplicar Unity log no arquivo |

Reaplicar manualmente:

```bash
make apply-bepinex-minimal-logging
# ou
GAME_DIR="/path/to/Project Gorgon" ./scripts/apply-bepinex-minimal-logging.sh
```

Para debug temporário, reverta `UnityLogListening` e `Enabled` no console em `BepInEx/config/BepInEx.cfg`.
