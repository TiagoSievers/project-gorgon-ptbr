from __future__ import annotations

import json
import re
from collections import defaultdict
from pathlib import Path

from .fetch_cdn import read_game_version
from .paths import (
    CDN_FILE_TO_CATEGORY,
    STRINGS_PATH,
    TEXT_FIELDS,
    cdn_data_dir,
    translation_en_dir,
)

SKIP_PATTERN = re.compile(
    r"^[\d\s\W]+$|^[A-Z0-9_\-]+$|^\{.*\}$|^%.*%$",
    re.DOTALL,
)
MIN_LEN = 2
MAX_LEN = 8000


def is_translatable(text: str) -> bool:
    s = text.strip()
    if len(s) < MIN_LEN or len(s) > MAX_LEN:
        return False
    if SKIP_PATTERN.match(s):
        return False
    if s.startswith("http://") or s.startswith("https://"):
        return False
    return True


def _walk_value(value, out: set[str]) -> None:
    if isinstance(value, str):
        if is_translatable(value):
            out.add(value.strip())
    elif isinstance(value, list):
        for item in value:
            _walk_value(item, out)
    elif isinstance(value, dict):
        for v in value.values():
            _walk_value(v, out)


def _walk_object_fields(obj: dict, out: set[str]) -> None:
    for key in TEXT_FIELDS:
        if key not in obj:
            continue
        val = obj[key]
        if isinstance(val, str) and is_translatable(val):
            out.add(val.strip())
        elif isinstance(val, list):
            for item in val:
                if isinstance(item, str) and is_translatable(item):
                    out.add(item.strip())


def extract_from_cdn_json(path: Path, category: str, by_category: dict[str, set[str]]) -> int:
    if not path.is_file():
        return 0
    raw = path.read_text(encoding="utf-8").strip()
    if not raw or raw.startswith("<"):
        print(f"  skip {path.name}: não é JSON")
        return 0
    try:
        data = json.loads(raw)
    except json.JSONDecodeError:
        print(f"  skip {path.name}: JSON inválido")
        return 0
    bucket = by_category.setdefault(category, set())
    before = len(bucket)

    if isinstance(data, list):
        for item in data:
            if isinstance(item, dict):
                _walk_object_fields(item, bucket)
                _walk_value(item, bucket)
    elif isinstance(data, dict):
        for item in data.values():
            if isinstance(item, dict):
                _walk_object_fields(item, bucket)
                _walk_value(item, bucket)
            elif isinstance(item, str) and is_translatable(item):
                bucket.add(item.strip())

    return len(bucket) - before


def translation_file_category(filename: str) -> str:
    # strings_skills.json -> skills, strings_ui.json -> ui
    stem = Path(filename).stem
    if stem.startswith("strings_"):
        return stem[len("strings_") :]
    return stem


def extract_from_translation_file(path: Path, by_category: dict[str, set[str]]) -> int:
    if not path.is_file():
        return 0
    category = translation_file_category(path.name)
    bucket = by_category.setdefault(category, set())
    before = len(bucket)
    data = json.loads(path.read_text(encoding="utf-8"))

    if isinstance(data, dict):
        for val in data.values():
            if isinstance(val, str) and is_translatable(val):
                bucket.add(val.strip())
    elif isinstance(data, list):
        for val in data:
            if isinstance(val, str) and is_translatable(val):
                bucket.add(val.strip())

    return len(bucket) - before


def extract(version: str | None = None) -> dict[str, list[str]]:
    version = version or read_game_version()
    data_dir = cdn_data_dir(version)
    trans_dir = translation_en_dir(version)

    by_category: dict[str, set[str]] = defaultdict(set)

    for filename, category in CDN_FILE_TO_CATEGORY.items():
        added = extract_from_cdn_json(data_dir / filename, category, by_category)
        print(f"  {category:12} +{added:6}  ({filename})")

    if trans_dir.is_dir():
        for path in sorted(trans_dir.glob("strings_*.json")):
            cat = translation_file_category(path.name)
            added = extract_from_translation_file(path, by_category)
            print(f"  {cat:12} +{added:6}  ({path.name})")

    result = {cat: sorted(values) for cat, values in sorted(by_category.items())}
    total_entries = sum(len(v) for v in result.values())
    unique = len({s for vals in result.values() for s in vals})

    STRINGS_PATH.parent.mkdir(parents=True, exist_ok=True)
    STRINGS_PATH.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"Extraído: {total_entries} entradas, {unique} strings únicas → {STRINGS_PATH}")
    return result
