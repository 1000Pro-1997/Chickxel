#!/usr/bin/env python3
"""Build the website pages (and then the in-app guide) from one template per page.

Sources (docs/i18n/):
  <page>.html  structure shared by every language. Placeholders:
                 {{key}}               a row of <page>.csv in the page's language
                 {{root}}              path to the site root ("" for Korean, "../" for docs/<lang>/)
                 {{lang}}              the page's language code (ko, en, ...)
                 {{key:<command id>}}  the app command's default shortcut as <kbd>s (docs/i18n/shortcuts.json, written by
                                       `dotnet run --project tools/GuideShots -- --shortcuts docs/i18n/shortcuts.json`);
                                       the website shows ⌘⌥⇧ on macOS and the app's F1 guide shows the user's own keys
                 {{selected:<lang>}}   " selected" on the page of that language (language picker)
                 {{MainStrings.Name}}  an app string (src/Gpixel.App/Localization or
                 {{UiStrings.Name}}    src/Gpixel.Core/Localization <Table>.resx / <Table>.<lang>.resx),
                                       so tool and menu names match the app
  <page>.csv   strings in the app's translation CSV format: first row `key,ko,en,...`,
               second row `#name,한국어,English,...`. Values may use {{root}} and app strings too.
               An empty cell falls back to English, then Korean.

Output: docs/<page>.html for Korean and docs/<lang>/<page>.html for every other column,
then `tools/build-help.py` regenerates src/Gpixel.App/Assets/Help/help.<lang>.json.

    python tools/build-docs.py           # write pages + in-app guide
    python tools/build-docs.py --check   # exit 1 if a page is out of date or a key is unknown/unused

Python 3 standard library only.
"""

from __future__ import annotations

import csv
import html
import json
import re
import runpy
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "i18n"
DOCS = ROOT / "docs"
RESX_DIRS = [ROOT / "src" / "Gpixel.App" / "Localization", ROOT / "src" / "Gpixel.Core" / "Localization"]
BASE_LANG = "ko"
PLACEHOLDER = re.compile(r"\{\{(key:[^{}]+|[\w.:\-]+)\}\}")
SHORTCUTS = SRC / "shortcuts.json"
MODIFIERS = ("Ctrl", "Control", "Meta", "Alt", "Shift")

_resx_cache: dict[tuple[str, str], dict[str, str]] = {}
_shortcuts: dict[str, list[str]] | None = None


def shortcut_html(command: str, page: str) -> str:
    """명령의 기본 단축키(앱 단축키 내보내기 모양 shortcuts.json의 첫 키)를 <kbd>로. 앱 F1은 data-cmd로 사용자가 바꾼 키를 보인다."""
    global _shortcuts
    if _shortcuts is None:
        _shortcuts = json.loads(SHORTCUTS.read_text(encoding="utf-8"))["shortcuts"]
    keys = _shortcuts.get(command)
    if keys is None:
        raise SystemExit(f"{page}: unknown shortcut command {command!r} (docs/i18n/shortcuts.json)")
    if not keys:
        raise SystemExit(f"{page}: command {command!r} has no default shortcut")
    # 키 이름에 '+'가 들어갈 수 있으므로(Num+) 알려진 수정자만 앞에서 떼어 낸다(앱 Shortcuts.Parse와 같음).
    text, parts = keys[0], []
    while (plus := text.find("+")) > 0 and plus < len(text) - 1 and text[:plus] in MODIFIERS:
        parts.append(text[:plus])
        text = text[plus + 1:]
    parts.append(text)
    kbds = "+".join(f"<kbd>{html.escape(k)}</kbd>" for k in parts)
    return f'<span class="key" data-cmd="{html.escape(command)}">{kbds}</span>'


def resx(table: str, lang: str) -> dict[str, str]:
    """앱 문구 표(<table>.resx = 한국어, <table>.<lang>.resx). 그 언어 파일에 없는 값은 기본(한국어) 값."""
    cached = _resx_cache.get((table, lang))
    if cached is not None:
        return cached
    base = next((d / f"{table}.resx" for d in RESX_DIRS if (d / f"{table}.resx").exists()), None)
    if base is None:
        raise SystemExit(f"unknown app string table: {table}")

    def load(path: Path) -> dict[str, str]:
        if not path.exists():
            return {}
        return {d.get("name"): d.findtext("value") or "" for d in ET.parse(path).getroot().iter("data")}

    values = load(base)
    if lang != BASE_LANG:
        values = {**values, **load(base.with_name(f"{table}.{lang}.resx"))}
    _resx_cache[(table, lang)] = values
    return values


def read_strings(path: Path) -> tuple[list[str], dict[str, dict[str, str]]]:
    """<page>.csv → (언어 목록, key → {언어: 값})."""
    with open(path, encoding="utf-8", newline="") as f:
        rows = list(csv.reader(f))
    header = rows[0]
    if header[0] != "key" or BASE_LANG not in header[1:]:
        raise SystemExit(f"{path}: first row must be key,{BASE_LANG},...")
    langs = header[1:]
    strings: dict[str, dict[str, str]] = {}
    for row in rows[1:]:
        if not row or row[0].startswith("#"):
            continue
        if row[0] in strings:
            raise SystemExit(f"{path}: duplicate key {row[0]}")
        strings[row[0]] = {lang: row[i + 1] if i + 1 < len(row) else "" for i, lang in enumerate(langs)}
    return langs, strings


def render(template: str, strings: dict[str, dict[str, str]], lang: str, page: str, used: set[str]) -> str:
    root = "" if lang == BASE_LANG else "../"

    def value(key: str, depth: int) -> str:
        if key == "root":
            return root
        if key == "lang":
            return lang
        if key.startswith("key:"):
            return shortcut_html(key[len("key:"):], page)
        if key.startswith("selected:"):
            return " selected" if key[len("selected:"):] == lang else ""
        table, _, name = key.partition(".")
        if table.endswith("Strings") and name:
            values = resx(table, lang)
            if name not in values:
                raise SystemExit(f"{page}: unknown app string {key}")
            return html.escape(values[name])
        if key not in strings:
            raise SystemExit(f"{page}: unknown key {{{{{key}}}}}")
        used.add(key)
        cells = strings[key]
        text = cells.get(lang) or cells.get("en") or cells.get(BASE_LANG, "")
        return expand(text, depth + 1) if depth < 2 else text

    def expand(text: str, depth: int) -> str:
        return PLACEHOLDER.sub(lambda m: value(m.group(1), depth), text)

    return expand(template, 0)


def output_path(page: str, lang: str) -> Path:
    return DOCS / f"{page}.html" if lang == BASE_LANG else DOCS / lang / f"{page}.html"


def main() -> int:
    check = "--check" in sys.argv[1:]
    stale: list[str] = []
    for template_path in sorted(SRC.glob("*.html")):
        page = template_path.stem
        langs, strings = read_strings(SRC / f"{page}.csv")
        template = template_path.read_text(encoding="utf-8")
        used: set[str] = set()
        for lang in langs:
            text = render(template, strings, lang, page, used)
            out = output_path(page, lang)
            if out.exists() and out.read_text(encoding="utf-8") == text:
                continue
            if check:
                stale.append(str(out.relative_to(ROOT)))
            else:
                out.parent.mkdir(parents=True, exist_ok=True)
                out.write_text(text, encoding="utf-8")
                print(f"wrote {out.relative_to(ROOT)}")
        unused = sorted(set(strings) - used)
        if unused:
            print(f"{page}.csv: unused keys: {', '.join(unused)}", file=sys.stderr)
            return 1
    if check:
        if stale:
            print("out of date (run python tools/build-docs.py): " + ", ".join(stale), file=sys.stderr)
            return 1
        return 0
    runpy.run_path(str(ROOT / "tools" / "build-help.py"), run_name="__main__")
    return 0


if __name__ == "__main__":
    sys.exit(main())
