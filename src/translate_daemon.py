from __future__ import annotations

import json
import os
import signal
import sys
import time
from datetime import datetime
from pathlib import Path

from . import official_writer, translate, yaml_writer
from .install import install_official
from .paths import CACHE_DIR, STRINGS_PATH, TRANSLATIONS_PATH

PIDFILE = CACHE_DIR / "daemon.pid"
LOG_PATH = CACHE_DIR / "daemon.log"
DEFAULT_CATEGORIES = "skills,abilities,ui,items,quests,npcs,effects"

_shutdown = False


def _log(msg: str) -> None:
    line = f"{datetime.now():%Y-%m-%d %H:%M:%S} {msg}"
    print(line, flush=True)
    LOG_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(LOG_PATH, "a", encoding="utf-8") as fh:
        fh.write(line + "\n")


def _parse_categories(raw: str | None) -> list[str] | None:
    if not raw:
        return None
    return [c.strip() for c in raw.split(",") if c.strip()]


def _pending_count(categories: list[str] | None) -> int:
    if not STRINGS_PATH.is_file():
        return 0
    by_category: dict[str, list[str]] = json.loads(STRINGS_PATH.read_text(encoding="utf-8"))
    cache = translate.load_cache()
    pending = 0
    for cat, strings in by_category.items():
        if categories and cat not in categories:
            continue
        for s in strings:
            if s not in cache:
                pending += 1
    return pending


def acquire_pidfile() -> bool:
    PIDFILE.parent.mkdir(parents=True, exist_ok=True)
    if PIDFILE.is_file():
        try:
            old_pid = int(PIDFILE.read_text(encoding="utf-8").strip())
            os.kill(old_pid, 0)
            return False
        except (OSError, ValueError):
            PIDFILE.unlink(missing_ok=True)
    PIDFILE.write_text(str(os.getpid()), encoding="utf-8")
    return True


def release_pidfile() -> None:
    PIDFILE.unlink(missing_ok=True)


def _handle_signal(signum: int, _frame) -> None:
    global _shutdown
    _log(f"sinal {signum} — encerrando daemon")
    _shutdown = True


def run_daemon(
    *,
    categories: list[str] | None = None,
    batch_size: int = 150,
    workers: int = 4,
    pause: float = 120.0,
    install_interval: float = 600.0,
    install_on_sync: bool = True,
    install_min_new: int = 50,
) -> int:
    if not acquire_pidfile():
        _log("daemon já em execução — saindo")
        return 0

    signal.signal(signal.SIGTERM, _handle_signal)
    signal.signal(signal.SIGINT, _handle_signal)

    cat_label = ",".join(categories) if categories else "todas"
    _log(
        f"daemon iniciado pid={os.getpid()} "
        f"batch={batch_size} workers={workers} pause={pause}s "
        f"install_interval={install_interval}s categories={cat_label}"
    )

    last_install_count = len(translate.load_cache())
    last_install_time = 0.0

    try:
        while not _shutdown:
            pending = _pending_count(categories)
            if pending == 0:
                _log("nada pendente — aguardando")
                time.sleep(pause)
                continue

            cache_before = len(translate.load_cache())
            _log(f"traduzindo lote (pendentes={pending}, cache={cache_before})")
            translate.translate_strings(
                categories=categories,
                limit=batch_size,
                workers=workers,
                delay=0,
            )
            cache_after = len(translate.load_cache())
            new_entries = cache_after - cache_before
            _log(f"lote ok +{new_entries} (cache={cache_after}, pendentes={_pending_count(categories)})")

            now = time.time()
            should_install = install_on_sync and (
                new_entries >= install_min_new
                or (now - last_install_time >= install_interval and cache_after > last_install_count)
            )
            if should_install:
                _log("write + install language pack")
                yaml_writer.write_yaml()
                official_writer.write_official()
                targets = install_official()
                for path in targets:
                    _log(f"instalado → {path}")
                _log("no jogo: /reloadstrings para aplicar")
                last_install_count = cache_after
                last_install_time = now

            for _ in range(int(pause)):
                if _shutdown:
                    break
                time.sleep(1)
    finally:
        release_pidfile()
        _log("daemon encerrado")

    return 0
