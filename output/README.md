# output/

Pack de tradução instalado no jogo em `Translation/` (Proton app 342940).

| Pasta | Uso |
|-------|-----|
| **`Translation/`** | `strings_*.json`, `version.json`, `checksums.json` — **versionar no git**, copiado pelo `pack-ptbr` |

YAML legado `pt-BR/` foi movido para `deletar/output-pt-BR/`.

Regenerar (mantenedor): `make write` ou `make pipeline` → atualiza `Translation/` a partir do CDN + cache.
