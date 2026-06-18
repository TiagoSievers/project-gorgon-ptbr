from __future__ import annotations

import json
import re
import time
import urllib.parse
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from threading import Lock
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from .paths import ROOT, yaml_out_dir
from .translate import GOOGLE_URL, USER_AGENT, load_cache, load_glossary, save_cache
from .yaml_io import parse_yaml_entries, yaml_quote

TEMPLATE_DIR = ROOT / "templates" / "translator-ru"

_PLACEHOLDER_RE = re.compile(r"(\$\d+|\\n|<[^>]+>|\[[^\]]*\])")


def _apply_glossary(text: str, glossary: dict[str, str]) -> str:
    out = text
    for en, pt in sorted(glossary.items(), key=lambda kv: len(kv[0]), reverse=True):
        if not en:
            continue
        pattern = re.compile(re.escape(en), re.IGNORECASE)
        out = pattern.sub(pt, out)
    return out


def _translate_en_key(key: str, glossary: dict[str, str]) -> str | None:
    if not key.strip():
        return None
    if key in glossary:
        pt = glossary[key]
        return pt if pt != key else None
    try:
        pt = _google_translate(key, source="en")
        pt = _apply_glossary(pt, glossary)
        return pt if pt and pt != key else None
    except (HTTPError, URLError, TimeoutError, json.JSONDecodeError, OSError):
        return None


def _collect_missing_template_keys(cache: dict[str, str]) -> list[str]:
    seen: set[str] = set()
    missing: list[str] = []
    for src_path in sorted(TEMPLATE_DIR.glob("*.yaml")):
        if src_path.name.endswith(".regex.yaml"):
            continue
        for key in parse_yaml_entries(src_path):
            if not key or key in seen:
                continue
            seen.add(key)
            pt = cache.get(key)
            if pt and pt != key:
                continue
            missing.append(key)
    return missing


def fill_template_cache(
    cache: dict[str, str],
    *,
    workers: int = 8,
    delay: float = 0.05,
) -> int:
    missing = _collect_missing_template_keys(cache)
    if not missing:
        return 0

    glossary = load_glossary()
    lock = Lock()
    added = 0
    print(f"Template RU: traduzindo {len(missing)} chaves ausentes do cache...")

    def work(key: str) -> tuple[str, str | None]:
        pt = _translate_en_key(key, glossary)
        if delay:
            time.sleep(delay)
        return key, pt

    done = 0
    with ThreadPoolExecutor(max_workers=max(1, workers)) as pool:
        futures = {pool.submit(work, key): key for key in missing}
        for fut in as_completed(futures):
            key, pt = fut.result()
            done += 1
            if pt:
                with lock:
                    cache[key] = pt
                    added += 1
            if done % 100 == 0 or done == len(missing):
                print(f"  template cache {done}/{len(missing)} (+{added})")

    return added


def _match_case(template: str, translated: str) -> str:
    if not template or not translated:
        return translated
    if template[0].islower() and translated[0].isupper():
        return translated[0].lower() + translated[1:]
    if template[0].isupper() and translated[0].islower():
        return translated[0].upper() + translated[1:]
    return translated


def _google_translate(text: str, *, source: str, target: str = "pt") -> str:
    if not text.strip():
        return text
    params = urllib.parse.urlencode(
        {
            "client": "gtx",
            "sl": source,
            "tl": target,
            "dt": "t",
            "q": text,
        }
    )
    url = f"{GOOGLE_URL}?{params}"
    req = Request(url, headers={"User-Agent": USER_AGENT})
    with urlopen(req, timeout=30) as resp:
        payload = json.loads(resp.read().decode("utf-8"))
    parts = payload[0] if payload else []
    translated = "".join(part[0] for part in parts if part and part[0])
    return translated.strip() or text


def _translate_with_placeholders(text: str, *, source: str) -> str:
    tokens: list[str] = []

    def repl(match: re.Match[str]) -> str:
        tokens.append(match.group(0))
        return f"__PH{len(tokens) - 1}__"

    protected = _PLACEHOLDER_RE.sub(repl, text)
    try:
        translated = _google_translate(protected, source=source)
    except Exception:
        return text
    for idx, token in enumerate(tokens):
        translated = translated.replace(f"__PH{idx}__", token)
    return translated


def _pattern_to_english_template(pattern: str) -> str:
    group = 0

    def repl(_: re.Match[str]) -> str:
        nonlocal group
        group += 1
        return f"${group}"

    tpl = pattern.strip("^$")
    tpl = re.sub(r"\(\?:[^)]*\)", repl, tpl)
    tpl = re.sub(r"\([^)]*\)", repl, tpl)
    tpl = re.sub(r"\[[^\]]*\]", repl, tpl)
    tpl = tpl.replace("\\n", "\n")
    return tpl


def _translate_static_fragment(text: str, cache: dict[str, str]) -> str:
    if not text:
        return text
    cached = cache.get(text)
    if cached and cached != text:
        return cached
    stripped = text.strip()
    if stripped != text:
        cached = cache.get(stripped)
        if cached and cached != stripped:
            return text.replace(stripped, cached, 1)
    try:
        return _google_translate(text, source="en")
    except Exception:
        return text


def _translate_regex_value(pattern: str, ru_value: str, cache: dict[str, str]) -> str | None:
    tpl_en = _pattern_to_english_template(pattern)
    if not tpl_en or tpl_en.startswith("$"):
        if ru_value:
            pt = _translate_with_placeholders(ru_value, source="ru")
            return pt if pt and pt != ru_value else None
        return None

    parts = re.split(r"(\$\d+)", tpl_en)
    out: list[str] = []
    for i, part in enumerate(parts):
        if re.fullmatch(r"\$\d+", part):
            out.append(part)
            continue
        translated = _translate_static_fragment(part, cache)
        if (
            i + 1 < len(parts)
            and re.fullmatch(r"\$\d+", parts[i + 1])
            and part.endswith(" ")
            and not translated.endswith(" ")
        ):
            translated += " "
        if (
            i + 1 < len(parts)
            and re.fullmatch(r"\$\d+", parts[i + 1])
            and part.endswith(": ")
            and not translated.endswith(": ")
        ):
            if translated.endswith(":"):
                translated += " "
            elif not translated.endswith(": "):
                translated = translated.rstrip() + ": "
        out.append(translated)

    pt = "".join(out)
    if not pt or pt == tpl_en:
        return None
    return pt


def _translate_prefix_key(key: str, cache: dict[str, str]) -> str | None:
    core = key.rstrip()
    suffix = key[len(core) :]
    pt = cache.get(core) or cache.get(key)
    if not pt or pt == core:
        return None
    return pt + suffix


def _translate_postfix_key(key: str, cache: dict[str, str]) -> str | None:
    pt = cache.get(key)
    if pt and pt != key:
        return pt
    core = key.strip()
    if core != key:
        pt = cache.get(core)
        if pt and pt != core:
            return key.replace(core, pt, 1)
    if key.startswith(" of "):
        pt = cache.get(key[4:])
        if pt:
            return f" {pt}" if key.startswith(" ") else pt
    return None


def translate_entry(
    key: str,
    ru_value: str,
    cache: dict[str, str],
    *,
    is_regex: bool,
    filename: str,
) -> str | None:
    if is_regex:
        return _translate_regex_value(key, ru_value, cache)

    if filename == "prefixes.yaml":
        return _translate_prefix_key(key, cache)

    if filename == "postfixes.yaml":
        return _translate_postfix_key(key, cache)

    if not ru_value and not key:
        return None

    pt = cache.get(key)
    if pt and pt != key:
        return _match_case(ru_value or key, pt)

    if ru_value and not pt:
        pt = cache.get(ru_value)
        if pt and pt != ru_value:
            return _match_case(ru_value, pt)

    pt = _translate_en_key(key, load_glossary())
    if pt:
        return _match_case(ru_value or key, pt)

    return None


def _write_yaml(path: Path, filename: str, entries: dict[str, str]) -> None:
    label = filename.replace(".regex.yaml", " (regex)").replace(".keys.yaml", " (keys)").replace(".yaml", "")
    lines = [
        f"# Project Gorgon — {label} (pt-BR)",
        "# Gerado de templates/translator-ru + cache/translations.json",
        "",
    ]
    for key, value in sorted(entries.items(), key=lambda kv: kv[0].lower()):
        lines.append(f"{yaml_quote(key)}: {yaml_quote(value)}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_templates(
    lang: str = "pt-BR",
    *,
    cdn_written: dict[str, Path] | None = None,
) -> dict[str, Path]:
    if not TEMPLATE_DIR.is_dir():
        raise FileNotFoundError(f"Templates RU não encontrados: {TEMPLATE_DIR}")

    cache = load_cache()
    added = fill_template_cache(cache)
    if added:
        save_cache(cache)
        print(f"  cache/translations.json: +{added} entradas do template RU")

    out_dir = yaml_out_dir(lang)
    out_dir.mkdir(parents=True, exist_ok=True)

    cdn_by_name = {path.name: path for path in (cdn_written or {}).values()}

    written: dict[str, Path] = {}
    total_new = 0

    for src_path in sorted(TEMPLATE_DIR.glob("*.yaml")):
        is_regex = src_path.name.endswith(".regex.yaml")
        ru_entries = parse_yaml_entries(src_path)
        dest = out_dir / src_path.name

        if src_path.name in cdn_by_name:
            merged = dict(parse_yaml_entries(cdn_by_name[src_path.name]))
        else:
            merged = {}

        added = 0
        for key, ru_val in ru_entries.items():
            pt = translate_entry(key, ru_val, cache, is_regex=is_regex, filename=src_path.name)
            if not pt or pt == key:
                continue
            if is_regex or key not in merged or merged[key] == key:
                if merged.get(key) != pt:
                    added += 1
                merged[key] = pt

        if not merged:
            continue

        _write_yaml(dest, src_path.name, merged)
        written[src_path.name] = dest
        total_new += added
        print(f"  template {src_path.name:28} +{added:5} entradas → {dest.name} ({len(merged)} total)")

    print(f"Templates RU → {lang}/: {len(written)} arquivos, {total_new} entradas novas/atualizadas")
    return written
