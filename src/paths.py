from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CACHE_DIR = ROOT / "cache"
OUTPUT_DIR = ROOT / "output"
GLOSSARY_PATH = ROOT / "glossary.json"
STRINGS_PATH = CACHE_DIR / "strings.json"
TRANSLATIONS_PATH = CACHE_DIR / "translations.json"
VERSION_PATH = CACHE_DIR / "game_version.txt"

CDN_DATA_FILES = (
    "items.json",
    "abilities.json",
    "skills.json",
    "effects.json",
    "npcs.json",
    "quests.json",
    "recipes.json",
)

CDN_FILE_TO_CATEGORY = {
    "items.json": "items",
    "abilities.json": "abilities",
    "skills.json": "skills",
    "effects.json": "effects",
    "npcs.json": "npcs",
    "quests.json": "quests",
    "recipes.json": "recipes",
}

TEXT_FIELDS = (
    "Name",
    "Description",
    "Desc",
    "FriendlyName",
    "DisplayName",
    "Tooltip",
    "EffectDescs",
    "ShortDesc",
    "LongDesc",
)


def cdn_dir(version: str) -> Path:
    return CACHE_DIR / f"v{version}"


def cdn_data_dir(version: str) -> Path:
    return cdn_dir(version) / "data"


def translation_en_dir(version: str) -> Path:
    return cdn_data_dir(version) / "Translation"


def official_out_dir() -> Path:
    return OUTPUT_DIR / "Translation"
