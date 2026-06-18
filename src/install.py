from __future__ import annotations

import json
import os
import shutil
from pathlib import Path

from .paths import ROOT, official_out_dir

STEAM_APP_ID_FULL = "342940"
STEAM_APP_ID_DEMO = "969170"
_PROTON_REL = Path(
    "pfx/drive_c/users/steamuser/AppData/LocalLow/Elder Game/Project Gorgon/Translation"
)


def _steam_compat_base() -> Path:
    return Path.home() / ".steam/debian-installation/steamapps/compatdata"


def default_proton_translation_dirs() -> list[Path]:
    base = _steam_compat_base()
    if app_id := os.environ.get("STEAM_APP_ID"):
        return [base / app_id / _PROTON_REL]
    return [
        base / STEAM_APP_ID_FULL / _PROTON_REL,
        base / STEAM_APP_ID_DEMO / _PROTON_REL,
    ]


def default_proton_translation_dir() -> Path:
    for dest in default_proton_translation_dirs():
        if (dest / "strings_ui.json").is_file():
            return dest
    return default_proton_translation_dirs()[0]


def default_linux_translation_dir() -> Path:
    return Path.home() / ".config/unity3d/Elder Game/Project Gorgon/Translation"


def default_plugin_dir() -> Path:
    home = Path.home()
    game = (
        home
        / ".steam/debian-installation/steamapps/common/Project Gorgon/BepInEx/plugins/PgTranslateLive"
    )
    return game


def _copy_translation_tree(src: Path, dest: Path) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    for item in src.iterdir():
        target = dest / item.name
        if item.is_dir():
            if target.exists():
                shutil.rmtree(target)
            shutil.copytree(item, target)
        else:
            shutil.copy2(item, target)


def install_official(
    *,
    proton_dir: Path | None = None,
    linux_dir: Path | None = None,
    plugin_dir: Path | None = None,
) -> dict[str, str]:
    src = official_out_dir()
    if not src.is_dir():
        raise FileNotFoundError(f"output/Translation nao encontrado. Rode: python3 -m src write")

    proton_dirs = [proton_dir] if proton_dir else default_proton_translation_dirs()
    linux = linux_dir or default_linux_translation_dir()
    installed: dict[str, str] = {}

    for dest in proton_dirs:
        _copy_translation_tree(src, dest)
        label = dest.parent.parent.parent.name
        installed[f"proton_{label}"] = str(dest)

    _copy_translation_tree(src, linux)
    installed["linux"] = str(linux)

    meta = write_pack_status(plugin_dir or default_plugin_dir())
    installed["pack_status"] = str(meta)
    return installed


def write_pack_status(plugin_dir: Path) -> Path:
    from .pack_status import build_status

    plugin_dir.mkdir(parents=True, exist_ok=True)
    status = build_status()
    path = plugin_dir / "pack-status.json"
    path.write_text(json.dumps(status, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return path
