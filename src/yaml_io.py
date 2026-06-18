from __future__ import annotations

from pathlib import Path


def yaml_quote(text: str) -> str:
    escaped = text.replace("\\", "\\\\").replace('"', '\\"')
    return f'"{escaped}"'


def yaml_unquote(raw: str) -> str:
    raw = raw.strip()
    if not (len(raw) >= 2 and raw[0] == '"' and raw[-1] == '"'):
        return raw

    out: list[str] = []
    i = 1
    while i < len(raw) - 1:
        ch = raw[i]
        if ch == "\\" and i + 1 < len(raw) - 1:
            n = raw[i + 1]
            if n == "n":
                out.append("\n")
                i += 2
                continue
            if n in ('"', "\\"):
                out.append(n)
                i += 2
                continue
        out.append(ch)
        i += 1
    return "".join(out)


def split_key_value(line: str) -> tuple[str, str] | None:
    in_quotes = False
    escape = False
    for i, ch in enumerate(line):
        if escape:
            escape = False
            continue
        if ch == "\\":
            escape = True
            continue
        if ch == '"':
            in_quotes = not in_quotes
            continue
        if ch == ":" and not in_quotes and i + 1 < len(line) and line[i + 1] == " ":
            key = yaml_unquote(line[:i].strip())
            value = yaml_unquote(line[i + 2 :].strip())
            return key, value
    return None


def parse_yaml_entries(path: Path) -> dict[str, str]:
    if not path.is_file():
        return {}
    entries: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        pair = split_key_value(line)
        if pair is None:
            continue
        key, value = pair
        if key:
            entries[key] = value
    return entries
