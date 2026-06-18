from __future__ import annotations

import io
import json
import re
import zipfile
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from .paths import (
    CDN_DATA_FILES,
    VERSION_PATH,
    cdn_data_dir,
    cdn_dir,
    translation_en_dir,
)

VERSION_URL = "http://client.projectgorgon.com/fileversion.txt"
CDN_BASE = "https://cdn.projectgorgon.com"
TRANSLATION_ZIP = "Translation.zip"


def _fetch_text(url: str, timeout: int = 60) -> str:
    req = Request(url, headers={"User-Agent": "PgTranslatePipeline/1.0"})
    with urlopen(req, timeout=timeout) as resp:
        return resp.read().decode("utf-8", errors="replace").strip()


def _fetch_bytes(url: str, timeout: int = 120) -> bytes:
    req = Request(url, headers={"User-Agent": "PgTranslatePipeline/1.0"})
    with urlopen(req, timeout=timeout) as resp:
        return resp.read()


def read_game_version() -> str:
    if VERSION_PATH.is_file():
        return VERSION_PATH.read_text(encoding="utf-8").strip()
    return fetch_game_version()


def fetch_game_version() -> str:
    raw = _fetch_text(VERSION_URL)
    match = re.search(r"\d+", raw)
    if not match:
        raise RuntimeError(f"Versão inválida em {VERSION_URL}: {raw!r}")
    version = match.group(0)
    VERSION_PATH.parent.mkdir(parents=True, exist_ok=True)
    VERSION_PATH.write_text(version, encoding="utf-8")
    return version


def _download_file(url: str, dest: Path, force: bool = False) -> bool:
    if dest.is_file() and not force:
        return False
    dest.parent.mkdir(parents=True, exist_ok=True)
    try:
        data = _fetch_bytes(url)
    except (HTTPError, URLError) as exc:
        print(f"  skip {dest.name}: {exc}")
        return False
    dest.write_bytes(data)
    return True


def fetch_cdn(version: str | None = None, force: bool = False) -> str:
    version = version or fetch_game_version()
    data_dir = cdn_data_dir(version)
    data_dir.mkdir(parents=True, exist_ok=True)

    print(f"CDN v{version} → {data_dir}")
    for name in CDN_DATA_FILES:
        url = f"{CDN_BASE}/v{version}/data/{name}"
        dest = data_dir / name
        if _download_file(url, dest, force=force):
            print(f"  ok {name}")
        elif dest.is_file():
            print(f"  cached {name}")

    trans_dir = translation_en_dir(version)
    zip_url = f"{CDN_BASE}/v{version}/data/{TRANSLATION_ZIP}"
    zip_path = cdn_dir(version) / TRANSLATION_ZIP
    if _download_file(zip_url, zip_path, force=force) or zip_path.is_file():
        _extract_translation_zip(zip_path, trans_dir, force=force)
    else:
        _fetch_translation_files(version, trans_dir, force=force)

    return version


def _extract_translation_zip(zip_path: Path, dest: Path, force: bool = False) -> None:
    if not zip_path.is_file():
        return
    dest.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zip_path) as zf:
        for info in zf.infolist():
            if info.is_dir():
                continue
            name = Path(info.filename).name
            if not name.startswith("strings_") or not name.endswith(".json"):
                continue
            out = dest / name
            if out.is_file() and not force:
                continue
            out.write_bytes(zf.read(info))
            print(f"  ok {name} (zip)")


def _fetch_translation_files(version: str, dest: Path, force: bool = False) -> None:
    manifest_url = f"{CDN_BASE}/v{version}/data/Translation/version.json"
    try:
        manifest = json.loads(_fetch_text(manifest_url))
    except (HTTPError, URLError, json.JSONDecodeError):
        print("  skip Translation: manifest indisponível")
        return

    dest.mkdir(parents=True, exist_ok=True)
    files = manifest.get("files") or manifest.get("Files") or []
    if isinstance(files, dict):
        files = list(files.keys())

    for entry in files:
        name = Path(str(entry)).name
        if not name.startswith("strings_") or not name.endswith(".json"):
            continue
        url = f"{CDN_BASE}/v{version}/data/Translation/{name}"
        dest_file = dest / name
        if _download_file(url, dest_file, force=force):
            print(f"  ok {name}")
        elif dest_file.is_file():
            print(f"  cached {name}")
