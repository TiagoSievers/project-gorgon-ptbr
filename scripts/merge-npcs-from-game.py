#!/usr/bin/env python3
"""Mescla npcs.yaml do jogo (falas capturadas) → repo output/pt-BR/npcs.yaml + cache."""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from src.paths import ROOT as REPO_ROOT  # noqa: E402
from src.translate import load_cache, save_cache  # noqa: E402
from src.yaml_io import parse_yaml_entries, yaml_quote  # noqa: E402

DEFAULT_GAME_YAML = Path(
    os.environ.get(
        "GAME_DIR",
        Path.home()
        / ".steam/debian-installation/steamapps/common/Project Gorgon",
    )
) / "BepInEx/plugins/Translator/translations/pt-BR/npcs.yaml"

REPO_YAML = REPO_ROOT / "output/pt-BR/npcs.yaml"


def _write_yaml(path: Path, entries: dict[str, str]) -> None:
    header = [
        "# Project Gorgon — npcs (pt-BR)",
        "# Gerado de templates/translator-ru + cache/translations.json",
        "# Inclui falas de NPC capturadas ao vivo (PgTranslateLive)",
    ]
    if path.is_file():
        kept: list[str] = []
        for line in path.read_text(encoding="utf-8").splitlines():
            t = line.strip()
            if t.startswith("#"):
                kept.append(t)
            elif not t:
                continue
            else:
                break
        if kept:
            header = kept

    lines = header + [""]
    for key in sorted(entries, key=str.lower):
        lines.append(f"{yaml_quote(key)}: {yaml_quote(entries[key])}")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def merge(source: Path, dest: Path | None = None) -> int:
    dest = dest or REPO_YAML
    if not source.is_file():
        print(f"Erro: arquivo não encontrado: {source}", file=sys.stderr)
        return 1

    incoming = parse_yaml_entries(source)
    repo = parse_yaml_entries(dest) if dest.is_file() else {}
    cache = load_cache()

    added = 0
    for key, value in incoming.items():
        if not key or not value or key == value:
            continue
        if key in repo and repo[key] == value:
            continue
        if key not in repo or repo[key] != value:
            repo[key] = value
            cache[key] = value
            added += 1

    if added == 0:
        print("Nada novo para mesclar.")
        return 0

    _write_yaml(dest, repo)
    save_cache(cache)
    print(f"Mesclado: +{added} entradas → {dest}")
    print(f"Total npcs.yaml: {len(repo)} | cache atualizado")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Mescla npcs.yaml do jogo (falas capturadas) no repositório."
    )
    parser.add_argument(
        "source",
        nargs="?",
        type=Path,
        default=DEFAULT_GAME_YAML,
        help=f"Caminho do npcs.yaml no jogo (padrão: {DEFAULT_GAME_YAML})",
    )
    parser.add_argument(
        "--dest",
        type=Path,
        default=REPO_YAML,
        help="Destino no repo (padrão: output/pt-BR/npcs.yaml)",
    )
    args = parser.parse_args()
    return merge(args.source, args.dest)


if __name__ == "__main__":
    raise SystemExit(main())
