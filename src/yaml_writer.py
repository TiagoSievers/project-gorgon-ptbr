from __future__ import annotations

import json
from pathlib import Path

from .paths import STRINGS_PATH, yaml_out_dir
from .translate import load_cache
from .yaml_io import yaml_quote


def write_yaml(lang: str = "pt-BR") -> dict[str, Path]:
    if not STRINGS_PATH.is_file():
        raise FileNotFoundError(f"Rode extract antes: {STRINGS_PATH}")

    cache = load_cache()
    by_category: dict[str, list[str]] = json.loads(STRINGS_PATH.read_text(encoding="utf-8"))
    out_dir = yaml_out_dir(lang)
    out_dir.mkdir(parents=True, exist_ok=True)

    written: dict[str, Path] = {}
    total_pairs = 0
    translated_pairs = 0

    for category, strings in sorted(by_category.items()):
        lines = [
            f"# Project Gorgon — {category} ({lang})",
            "# Gerado pelo pipeline CDN. Chave = EN, valor = PT.",
            "",
        ]
        pairs = 0
        for src in strings:
            dst = cache.get(src)
            if not dst or dst == src:
                continue
            lines.append(f"{yaml_quote(src)}: {yaml_quote(dst)}")
            pairs += 1

        path = out_dir / f"{category}.yaml"
        path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        written[category] = path
        total_pairs += len(strings)
        translated_pairs += pairs
        print(f"  {category:16} {pairs:5} traduções → {path.name}")

    print(f"YAML: {translated_pairs} pares traduzidos ({lang}/)")
    return written
