from __future__ import annotations

import json
import random
import re
import time
import urllib.parse
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from threading import Lock
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from .paths import GLOSSARY_PATH, STRINGS_PATH, TRANSLATIONS_PATH

GOOGLE_URL = "https://translate.googleapis.com/translate_a/single"
USER_AGENT = "PgTranslatePipeline/1.0"


def load_glossary() -> dict[str, str]:
    if not GLOSSARY_PATH.is_file():
        return {}
    return json.loads(GLOSSARY_PATH.read_text(encoding="utf-8"))


def load_cache() -> dict[str, str]:
    if not TRANSLATIONS_PATH.is_file():
        return {}
    return json.loads(TRANSLATIONS_PATH.read_text(encoding="utf-8"))


def save_cache(cache: dict[str, str]) -> None:
    TRANSLATIONS_PATH.parent.mkdir(parents=True, exist_ok=True)
    ordered = dict(sorted(cache.items(), key=lambda kv: kv[0].lower()))
    TRANSLATIONS_PATH.write_text(json.dumps(ordered, ensure_ascii=False, indent=2), encoding="utf-8")


def _google_translate(text: str, timeout: int = 30) -> str:
    params = urllib.parse.urlencode(
        {
            "client": "gtx",
            "sl": "en",
            "tl": "pt",
            "dt": "t",
            "q": text,
        }
    )
    url = f"{GOOGLE_URL}?{params}"
    req = Request(url, headers={"User-Agent": USER_AGENT})
    with urlopen(req, timeout=timeout) as resp:
        payload = json.loads(resp.read().decode("utf-8"))
    parts = payload[0] if payload else []
    translated = "".join(part[0] for part in parts if part and part[0])
    return translated.strip() or text


def _apply_glossary(text: str, glossary: dict[str, str]) -> str:
    out = text
    for en, pt in sorted(glossary.items(), key=lambda kv: len(kv[0]), reverse=True):
        if not en:
            continue
        pattern = re.compile(re.escape(en), re.IGNORECASE)
        out = pattern.sub(pt, out)
    return out


def translate_strings(
    *,
    categories: list[str] | None = None,
    limit: int | None = None,
    workers: int = 8,
    delay: float = 0.05,
    force: bool = False,
) -> dict[str, str]:
    if not STRINGS_PATH.is_file():
        raise FileNotFoundError(f"Rode extract antes: {STRINGS_PATH}")

    by_category: dict[str, list[str]] = json.loads(STRINGS_PATH.read_text(encoding="utf-8"))
    glossary = load_glossary()
    cache = load_cache()
    lock = Lock()
    log_path = TRANSLATIONS_PATH.parent / "translate.log"

    todo: list[str] = []
    for cat, strings in by_category.items():
        if categories and cat not in categories:
            continue
        for s in strings:
            if force or s not in cache:
                todo.append(s)

    todo = sorted(set(todo))
    if limit is not None:
        todo = todo[:limit]

    print(f"Traduzir: {len(todo)} strings (cache={len(cache)}, workers={workers})")

    def work(src: str) -> tuple[str, str | None, str | None]:
        if src in glossary:
            return src, glossary[src], None
        try:
            translated = _google_translate(src)
            translated = _apply_glossary(translated, glossary)
            if delay:
                time.sleep(delay + random.uniform(0, delay))
            return src, translated, None
        except (HTTPError, URLError, TimeoutError, json.JSONDecodeError) as exc:
            return src, None, str(exc)

    done = 0
    errors = 0
    with ThreadPoolExecutor(max_workers=max(1, workers)) as pool:
        futures = {pool.submit(work, s): s for s in todo}
        for fut in as_completed(futures):
            src, translated, err = fut.result()
            done += 1
            if err:
                errors += 1
                with open(log_path, "a", encoding="utf-8") as log:
                    log.write(f"ERR {src[:80]!r}: {err}\n")
            elif translated:
                with lock:
                    cache[src] = translated
                    if done % 50 == 0:
                        save_cache(cache)
            if done % 100 == 0 or done == len(todo):
                print(f"  {done}/{len(todo)} (cache={len(cache)}, errors={errors})")

    save_cache(cache)
    print(f"Cache salvo: {TRANSLATIONS_PATH} ({len(cache)} entradas)")
    return cache
