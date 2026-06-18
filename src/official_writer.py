from __future__ import annotations

import hashlib
import json
from pathlib import Path

from .fetch_cdn import read_game_version
from .paths import official_out_dir, translation_en_dir
from .translate import load_cache


def _checksum(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write_official(version: str | None = None) -> Path:
    version = version or read_game_version()
    en_dir = translation_en_dir(version)
    if not en_dir.is_dir():
        raise FileNotFoundError(f"Translation EN não encontrada: {en_dir}. Rode fetch.")

    cache = load_cache()
    out_dir = official_out_dir()
    out_dir.mkdir(parents=True, exist_ok=True)

    files_meta: dict[str, str] = {}
    total = 0
    translated = 0

    for en_path in sorted(en_dir.glob("strings_*.json")):
        en_data = json.loads(en_path.read_text(encoding="utf-8"))
        out_data: dict[str, str] = {}

        if isinstance(en_data, dict):
            for key, val in en_data.items():
                if not isinstance(val, str):
                    continue
                total += 1
                pt = cache.get(val, val)
                if pt != val:
                    translated += 1
                out_data[key] = pt
        else:
            out_data = en_data

        out_path = out_dir / en_path.name
        payload = json.dumps(out_data, ensure_ascii=False, indent=2, sort_keys=True)
        out_path.write_text(payload + "\n", encoding="utf-8")
        files_meta[en_path.name] = _checksum(payload.encode("utf-8"))
        print(f"  {en_path.name}: {len(out_data)} chaves")

    version_payload = {
        "CdnFileVersion": str(version),
        "Credits": "Pipeline automático PG PT-BR (Google Translate) — revisão humana recomendada",
        "Description": "Tradução parcial para português brasileiro gerada a partir do CDN oficial.",
        "Format": 1,
        "LanguageCode": "pt-BR",
        "Name": "Project Gorgon — Português (BR)",
        "Notes": "Atualize com: make write && make install-official",
        "Version": "0.2.0",
    }
    version_path = out_dir / "version.json"
    version_path.write_text(json.dumps(version_payload, indent=2) + "\n", encoding="utf-8")

    checksums = {"files": files_meta, "version": version_payload}
    checksums_path = out_dir / "checksums.json"
    checksums_path.write_text(json.dumps(checksums, indent=2) + "\n", encoding="utf-8")

    print(f"Translation/: {translated}/{total} strings traduzidas → {out_dir}")
    return out_dir
