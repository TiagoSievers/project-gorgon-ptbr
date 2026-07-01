#!/usr/bin/env python3
"""Remove subtexto/vendor/lixo legado de strings_npctalk.json — só falas Falar (ShowTalk).

Espelha NpcTalkStore.IsEligibleTalkEntry em main.cs.
Uso: python3 scripts/clean-strings-npctalk.py [--dry-run] [caminho.json]
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT = ROOT / "output/Translation/strings_npctalk.json"

COMMUNITY_SCORE = re.compile(r"Community Score:\s*(\d+)", re.I)
FRIENDSHIP_PREFIXES = ("BestFriends", "CloseFriends", "Despised:", "LikeFamily:")


def contains_you_address(source: str) -> bool:
    s = source.lower()
    return (
        " you " in s
        or s.startswith("you ")
        or " you." in s
        or " you," in s
        or " you?" in s
        or " you!" in s
    )


def is_npc_subtext_or_vendor_blurb(source: str) -> bool:
    if contains_you_address(source):
        return False
    lower = source.lower()
    if source.startswith(("A ", "An ")):
        if any(
            x in lower
            for x in (
                " selling ",
                " vendor",
                " demeanor",
                " looks ",
                " with a ",
                " covered in ",
                " attempting to ",
                " constantly ",
            )
        ):
            return True
        if source.endswith(".") and len(source) < 120:
            return True
    if source.startswith(
        ("He ", "She ", "It ", "He's ", "She's ", "His ", "Her ")
    ) and not contains_you_address(source):
        return True
    if source.startswith(
        (
            "Looking ",
            "Busy ",
            "Laughing ",
            "Checking ",
            "Fiddling ",
            "Distractedly ",
        )
    ):
        return True
    return False


def is_likely_talk_line(source: str) -> bool:
    if contains_you_address(source):
        return True
    if source.startswith(('"', "'")):
        return True
    if "?" in source and len(source) >= 12 and not source.startswith(
        ("Is he ", "Is she ", "Is it ", "Are they ")
    ):
        return True
    if source.startswith(
        (
            "I ",
            "I'm ",
            "I'll ",
            "I've ",
            "We ",
            "Welcome ",
            "Hello",
            "Good morning",
            "Good afternoon",
            "Good evening",
            "Goodbye",
        )
    ):
        return True
    return False


def is_eligible_talk_entry(source: str) -> bool:
    if not source or not source.strip():
        return False
    source = source.strip()
    if " " not in source:
        return False
    if source.lower().startswith("x:") and " y:" in source.lower():
        return False
    if COMMUNITY_SCORE.search(source):
        return False
    if "Friends:" in source or source.startswith(FRIENDSHIP_PREFIXES):
        return False
    if is_npc_subtext_or_vendor_blurb(source):
        return False
    return is_likely_talk_line(source)


def main() -> int:
    dry_run = "--dry-run" in sys.argv
    paths = [Path(a) for a in sys.argv[1:] if not a.startswith("-")]
    path = paths[0] if paths else DEFAULT

    data: dict[str, str] = json.loads(path.read_text(encoding="utf-8"))
    kept = {k: v for k, v in data.items() if is_eligible_talk_entry(k)}
    removed = len(data) - len(kept)

    print(f"{path}: {len(data)} → {len(kept)} entradas ({removed} removidas)")
    if dry_run:
        print("(dry-run — arquivo não alterado)")
        return 0

    ordered = dict(sorted(kept.items(), key=lambda kv: kv[0].lower()))
    path.write_text(
        json.dumps(ordered, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
