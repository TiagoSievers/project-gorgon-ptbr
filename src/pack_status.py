from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path

from .fetch_cdn import read_game_version
from .install import default_linux_translation_dir, default_proton_translation_dir
from .paths import STRINGS_PATH, TRANSLATIONS_PATH, official_out_dir
from .translate import load_cache


def _count_translated_in_file(path: Path) -> tuple[int, int]:
    if not path.is_file():
        return 0, 0
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        return 0, 0
    total = 0
    translated = 0
    for val in data.values():
        if not isinstance(val, str):
            continue
        total += 1
        if _looks_portuguese(val):
            translated += 1
    return translated, total


def _looks_portuguese(text: str) -> bool:
    for ch in text:
        if ch in "áàâãéêíóôõúçÁÀÂÃÉÊÍÓÔÕÚÇ":
            return True
    return False


def _sample_key(path: Path, key: str) -> str | None:
    if not path.is_file():
        return None
    data = json.loads(path.read_text(encoding="utf-8"))
    if isinstance(data, dict):
        val = data.get(key)
        return val if isinstance(val, str) else None
    return None


def _unique_string_count() -> int:
    if not STRINGS_PATH.is_file():
        return 0
    by_category = json.loads(STRINGS_PATH.read_text(encoding="utf-8"))
    return len({s for vals in by_category.values() for s in vals})


def build_status() -> dict:
    cache = load_cache()
    unique = _unique_string_count()
    pending = max(0, unique - len(cache))

    out_ui = official_out_dir() / "strings_ui.json"
    out_translated, out_total = _count_translated_in_file(out_ui)

    proton_ui = default_proton_translation_dir() / "strings_ui.json"
    installed_translated, installed_total = _count_translated_in_file(proton_ui)

    welcome_en = "Welcome to the Project: Gorgon Demo! Click to login with your Steam account."
    welcome_cached = cache.get(welcome_en)
    welcome_out = _sample_key(out_ui, "ui.login.welcome.demo")
    welcome_installed = _sample_key(proton_ui, "ui.login.welcome.demo")

    output_mtime = out_ui.stat().st_mtime if out_ui.is_file() else 0
    installed_mtime = proton_ui.stat().st_mtime if proton_ui.is_file() else 0

    stale = len(cache) > 0 and (
        installed_mtime < output_mtime
        or welcome_installed != welcome_out
        or installed_translated < out_translated
        or (welcome_cached and welcome_out == welcome_en)
        or (welcome_cached and welcome_installed != welcome_cached)
    )

    version = read_game_version()
    version_path = official_out_dir() / "version.json"
    pack_version = ""
    if version_path.is_file():
        pack_version = json.loads(version_path.read_text(encoding="utf-8")).get("Version", "")

    return {
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "cdnVersion": version,
        "packVersion": pack_version,
        "cacheCount": len(cache),
        "uniqueStrings": unique,
        "pendingCount": pending,
        "outputUiTranslated": out_translated,
        "outputUiTotal": out_total,
        "installedUiTranslated": installed_translated,
        "installedUiTotal": installed_total,
        "welcomeOutput": welcome_out,
        "welcomeInstalled": welcome_installed,
        "welcomeCached": welcome_cached,
        "stale": stale,
        "pipelineRoot": str(Path(__file__).resolve().parents[1]),
    }
