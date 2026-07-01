# src/

Pipeline Python: CDN → tradução → `output/Translation/`.

```bash
python3 -m src fetch | extract | translate | write | pipeline
make serve   # HTTP local (opcional)
```

| Módulo | Função |
|--------|--------|
| `fetch_cdn.py` | Baixa CDN + Translation.zip |
| `extract.py` | Strings EN → `cache/strings.json` |
| `translate.py` | Google → `cache/translations.json` |
| `official_writer.py` | Gera `output/Translation/*.json` |
| `paths.py` | Caminhos do repo |
| `serve.py` | Servidor dev `/sync` |
| `install.py` | Instala pack no Proton/Linux |
| `pack_status.py` | Status para `serve` |

Legado: `deletar/src/` (`yaml_io`, `translate_daemon`, writers YAML).
