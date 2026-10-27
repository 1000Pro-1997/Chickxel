#!/usr/bin/env python3
"""Build the in-app guide (F1) from the website guide.

Reads docs/guide.html (Korean) and docs/en/guide.html (English) and writes
src/Gpixel.App/Assets/Help/help.<lang>.json. Re-run after editing either guide:

    python tools/build-help.py

Python 3 standard library, plus `cwebp` (libwebp) on PATH when a guide
screenshot changed. Pages are split like docs/guide.js does: every
h3 starts its own page, and the chapter's text before its first h3 becomes the
first page's "intro". A chapter (section.doc, h2) without h3 is a single page
with the chapter id.

Screenshots (<figure class="shot"> with docs/assets/guide/<name>.png) become
"shot" blocks; their lossless WebP copies are kept in
src/Gpixel.App/Assets/Help/shots/ (embedded in the app), re-encoded when the PNG
is newer, and unused ones are deleted.
"""

from __future__ import annotations

import json
import re
import shutil
import subprocess
import sys
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "src" / "Gpixel.App" / "Assets" / "Help"
SHOTS_SRC = ROOT / "docs" / "assets" / "guide"
SHOTS_OUT = OUT_DIR / "shots"
SITE = "https://1000pro-1997.github.io/GPixel/"
SOURCES = {
    "ko": (ROOT / "docs" / "guide.html", SITE),
    "en": (ROOT / "docs" / "en" / "guide.html", SITE + "en/"),
}

VOID = {"area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"}
WS = re.compile(r"[ \t\n\r\f]+")


class Node:
    __slots__ = ("tag", "attrs", "children", "parent")

    def __init__(self, tag: str, attrs: dict[str, str], parent: "Node | None"):
        self.tag = tag
        self.attrs = attrs
        self.children: list["Node | str"] = []
        self.parent = parent

    def classes(self) -> set[str]:
        return set(self.attrs.get("class", "").split())

    def elements(self, tag: str | None = None):
        for c in self.children:
            if isinstance(c, Node) and (tag is None or c.tag == tag):
                yield c

    def find(self, pred) -> "Node | None":
        for c in self.children:
            if isinstance(c, Node):
                if pred(c):
                    return c
                hit = c.find(pred)
                if hit is not None:
                    return hit
        return None

    def raw_text(self) -> str:
        return "".join(c if isinstance(c, str) else c.raw_text() for c in self.children)


class TreeBuilder(HTMLParser):
    """Small DOM: enough for the hand-written guide HTML."""

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.root = Node("#root", {}, None)
        self.cur = self.root

    def handle_starttag(self, tag, attrs):
        node = Node(tag, {k: v or "" for k, v in attrs}, self.cur)
        self.cur.children.append(node)
        if tag not in VOID:
            self.cur = node

    def handle_startendtag(self, tag, attrs):
        self.cur.children.append(Node(tag, {k: v or "" for k, v in attrs}, self.cur))

    def handle_endtag(self, tag):
        if tag in VOID:
            return
        n = self.cur
        while n is not None and n.tag != tag:
            n = n.parent
        if n is not None and n.parent is not None:
            self.cur = n.parent

    def handle_data(self, data):
        self.cur.children.append(data)


def parse(path: Path) -> Node:
    b = TreeBuilder()
    b.feed(path.read_text(encoding="utf-8"))
    b.close()
    return b.root


# ───────────── inline runs ─────────────

INLINE_TAGS = {"strong", "b", "em", "i", "code", "kbd", "a", "span", "br", "small", "sup", "sub"}


class Runs:
    """Collects text runs with browser-like whitespace collapsing."""

    def __init__(self, base: str):
        self.base = base
        self.items: list[dict] = []

    def add_text(self, text: str, style: dict):
        text = WS.sub(" ", text)
        if not text:
            return
        prev = self.items[-1]["t"] if self.items else ""
        if text.startswith(" ") and (not self.items or prev.endswith(" ")):
            text = text[1:]
        if not text:
            return
        run = {"t": text}
        run.update(style)
        if self.items and {k: v for k, v in self.items[-1].items() if k != "t"} == style:
            self.items[-1]["t"] += text
        else:
            self.items.append(run)

    def walk(self, node: Node, style: dict):
        for c in node.children:
            if isinstance(c, str):
                self.add_text(c, style)
                continue
            s = dict(style)
            if c.tag in ("strong", "b"):
                s["b"] = True
            elif c.tag == "code":
                s["code"] = True
            elif c.tag == "kbd":
                s["kbd"] = True
                # class="os-keep": macOS에서도 ⌘로 바꾸지 않는 키(“Ctrl을 ⌘로 읽으세요” 같은 글)
                if "os-keep" in c.attrs.get("class", "").split():
                    s["keep"] = True
            elif c.tag == "span" and (cmd := c.attrs.get("data-cmd")):
                # 앱 명령의 단축키({{key:명령}}): 앱은 사용자가 바꾼 키로 그린다.
                s["cmd"] = cmd
            elif c.tag == "a" and (href := self.href(c.attrs.get("href", ""))):
                s["href"] = href
            elif c.tag == "br":
                self.add_text(" ", style)
                continue
            self.walk(c, s)

    def href(self, href: str) -> str | None:
        if not href:
            return None
        if href.startswith("#") or re.match(r"^[a-z]+:", href):
            return href
        return self.base + href

    def done(self) -> list[dict]:
        if self.items:
            self.items[0]["t"] = self.items[0]["t"].lstrip(" ")
            self.items[-1]["t"] = self.items[-1]["t"].rstrip(" ")
        return [r for r in self.items if r["t"]]


def runs_of(node: Node, base: str, skip=lambda n: False) -> list[dict]:
    r = Runs(base)
    if skip:
        shallow = Node(node.tag, node.attrs, None)
        shallow.children = [c for c in node.children if isinstance(c, str) or not skip(c)]
        node = shallow
    r.walk(node, {})
    return r.done()


# ───────────── blocks ─────────────


def is_list(n) -> bool:
    return isinstance(n, Node) and n.tag in ("ul", "ol")


def list_items(node: Node, base: str) -> list[dict]:
    items = []
    for li in node.elements("li"):
        item = {"runs": runs_of(li, base, skip=is_list)}
        sub = [c for c in li.elements() if is_list(c)]
        if sub:
            item["sub"] = [list_block(s, base) for s in sub]
        items.append(item)
    return items


def list_block(node: Node, base: str) -> dict:
    return {"type": node.tag, "items": list_items(node, base)}


def table_block(node: Node, base: str) -> dict:
    head: list = []
    rows: list = []
    for part in node.elements():
        trs = [part] if part.tag == "tr" else list(part.elements("tr"))
        for tr in trs:
            cells = [runs_of(c, base) for c in tr.elements() if c.tag in ("td", "th")]
            if part.tag == "thead" or (not head and not rows and all(c.tag == "th" for c in tr.elements())):
                head = cells
            else:
                rows.append(cells)
    return {"type": "table", "head": head, "rows": rows}


def media_block(node: Node, page_id: str) -> dict | None:
    m = node.find(lambda n: n.tag in ("video", "img"))
    if m is None:
        return None
    alt = m.attrs.get("alt") or m.attrs.get("aria-label") or ""
    src = m.attrs.get("src", "")
    return {"type": "media", "alt": WS.sub(" ", alt).strip(), "page": page_id, "src": src.rsplit("/", 1)[-1]}


def shot_block(node: Node, base: str, page_id: str) -> dict:
    """Guide screenshot: name (docs/assets/guide/<name>.png), alt and the numbered legend (figcaption)."""
    img = node.find(lambda n: n.tag == "img")
    if img is None:
        raise SystemExit(f"figure.shot without img on page {page_id}")
    src = img.attrs.get("src", "")
    m = re.fullmatch(r"(?:\.\./)?assets/guide/([\w.-]+)\.png", src)
    if m is None:
        raise SystemExit(f"figure.shot src {src!r} is not assets/guide/<name>.png on page {page_id}")
    cap = node.find(lambda n: n.tag == "figcaption")
    out = {"type": "shot", "src": m.group(1), "alt": WS.sub(" ", img.attrs.get("alt", "")).strip()}
    if cap is not None:
        out["runs"] = runs_of(cap, base)
    return out


def blocks_of(container: Node, base: str, page_id: str) -> list[dict]:
    """Block children of a container; stray inline content becomes a paragraph."""
    out: list[dict] = []
    pending = Node("p", {}, None)

    def flush():
        if pending.children:
            runs = runs_of(pending, base)
            if runs:
                out.append({"type": "p", "runs": runs})
            pending.children = []

    for c in container.children:
        if isinstance(c, str) or c.tag in INLINE_TAGS:
            pending.children.append(c)
            continue
        flush()
        out.extend(block(c, base, page_id))
    flush()
    return out


def block(n: Node, base: str, page_id: str) -> list[dict]:
    cls = n.classes()
    if n.tag == "p":
        runs = runs_of(n, base)
        return [{"type": "p", "runs": runs}] if runs else []
    if n.tag == "h4":
        return [{"type": "h4", "runs": runs_of(n, base)}]
    if n.tag == "ol" and "core-steps" in cls:
        items = []
        for li in n.elements("li"):
            title = li.find(lambda x: x.tag in ("strong", "b"))
            text = li.find(lambda x: x.tag == "span")
            items.append({
                "title": runs_of(title, base) if title else [],
                "runs": runs_of(text, base) if text else runs_of(li, base, skip=lambda x: x is title),
            })
        return [{"type": "steps", "items": items}]
    if n.tag in ("ul", "ol"):
        return [list_block(n, base)]
    if n.tag == "table":
        return [table_block(n, base)]
    if n.tag == "div" and "note" in cls:
        return [{"type": "note", "blocks": blocks_of(n, base, page_id)}]
    if n.tag == "pre":
        return [{"type": "pre", "text": n.raw_text().strip("\n")}]
    if n.tag == "figure" and "shot" in cls:
        return [shot_block(n, base, page_id)]
    if n.tag == "figure":
        m = media_block(n, page_id)
        return [m] if m else []
    if n.tag in ("div", "section"):
        return blocks_of(n, base, page_id)
    raise SystemExit(f"unsupported block <{n.tag}> on page {page_id}")


# ───────────── page text (search) ─────────────


def runs_text(runs: list[dict]) -> str:
    return "".join(r["t"] for r in runs)


def block_texts(b: dict):
    t = b["type"]
    if t in ("p", "h4"):
        yield runs_text(b["runs"])
    elif t in ("ul", "ol"):
        for it in b["items"]:
            yield runs_text(it["runs"])
            for s in it.get("sub", []):
                yield from block_texts(s)
    elif t == "steps":
        for it in b["items"]:
            yield runs_text(it["title"])
            yield runs_text(it["runs"])
    elif t == "table":
        for row in [b["head"], *b["rows"]]:
            yield " ".join(runs_text(c) for c in row)
    elif t == "note":
        for s in b["blocks"]:
            yield from block_texts(s)
    elif t == "pre":
        yield b["text"]
    elif t == "media":
        yield b["alt"]
    elif t == "shot":
        yield b["alt"]
        yield runs_text(b.get("runs", []))


# ───────────── document ─────────────


def build(path: Path, base: str) -> dict:
    root = parse(path)
    toc = root.find(lambda n: n.tag == "aside" and "toc" in n.classes())
    content = root.find(lambda n: n.tag == "div" and "content" in n.classes())
    if toc is None or content is None:
        raise SystemExit(f"{path}: aside.toc or div.content not found")

    chapters: dict[str, dict] = {}
    for section in content.elements("section"):
        if "doc" not in section.classes():
            continue
        cid = section.attrs["id"]
        chapter = {"id": cid, "title": "", "pages": []}
        # 세부 항목(h3)이 있는 장은 머리말 쪽을 따로 두지 않고, h2 다음 글을 첫 세부 쪽의 "intro"로 붙인다.
        intro: list[dict] | None = None
        page = None
        for c in section.children:
            if isinstance(c, str):
                if c.strip():
                    raise SystemExit(f"{path}: stray text in section {cid}")
                continue
            if c.tag == "h2":
                chapter["title"] = runs_text(runs_of(c, base))
                intro = []
                continue
            if c.tag == "h3":
                page = {"id": c.attrs["id"], "title": runs_text(runs_of(c, base)), "blocks": []}
                if not chapter["pages"] and intro:
                    page["intro"] = intro
                chapter["pages"].append(page)
                continue
            if intro is None:
                raise SystemExit(f"{path}: content before h2 in section {cid}")
            if page is None:
                intro.extend(block(c, base, cid))
            else:
                page["blocks"].extend(block(c, base, page["id"]))
        if not chapter["pages"]:
            chapter["pages"].append({"id": cid, "title": chapter["title"], "blocks": intro or []})
        for p in chapter["pages"]:
            p["text"] = "\n".join(t for b in p.get("intro", []) + p["blocks"] for t in block_texts(b) if t)
        chapters[cid] = chapter

    groups: list[dict] = []
    top = next(toc.elements("ol"))
    for li in top.elements("li"):
        if "toc-label" in li.classes():
            groups.append({"label": WS.sub(" ", li.raw_text()).strip(), "chapters": []})
            continue
        a = next(li.elements("a"))
        cid = a.attrs["href"].lstrip("#")
        if cid not in chapters:
            raise SystemExit(f"{path}: TOC chapter #{cid} has no section")
        if not groups:
            groups.append({"label": "", "chapters": []})
        groups[-1]["chapters"].append(chapters.pop(cid))
    if chapters:
        raise SystemExit(f"{path}: sections missing from TOC: {', '.join(chapters)}")

    # 장 id(#장)는 그 장의 첫 쪽으로 열린다.
    ids = {p["id"] for g in groups for c in g["chapters"] for p in c["pages"]}
    ids |= {c["id"] for g in groups for c in g["chapters"]}
    for g in groups:
        for c in g["chapters"]:
            for p in c["pages"]:
                for href in re.findall(r'"href": "#([^"]+)"', json.dumps(p.get("intro", []) + p["blocks"])):
                    if href not in ids:
                        print(f"warning: {path.name} page {p['id']} links to unknown #{href}", file=sys.stderr)
    return {"groups": groups}


def shot_names(blocks: list[dict]):
    for b in blocks:
        if b["type"] == "shot":
            yield b["src"]
        elif b["type"] == "note":
            yield from shot_names(b["blocks"])


def sync_shots(names: set[str]) -> None:
    """Lossless WebP copies of the screenshots the guides use (re-encoded when the PNG is newer); unused copies are deleted."""
    SHOTS_OUT.mkdir(parents=True, exist_ok=True)
    cwebp = shutil.which("cwebp")
    made = 0
    for name in sorted(names):
        png = SHOTS_SRC / f"{name}.png"
        if not png.exists():
            raise SystemExit(f"screenshot {png.relative_to(ROOT)} not found (run tools/GuideShots)")
        webp = SHOTS_OUT / f"{name}.webp"
        if webp.exists() and webp.stat().st_mtime >= png.stat().st_mtime:
            continue
        if cwebp is None:
            raise SystemExit("cwebp not found: install libwebp (brew install webp / winget install Google.Libwebp) to encode guide screenshots")
        subprocess.run([cwebp, "-quiet", "-lossless", "-z", "6", str(png), "-o", str(webp)], check=True)
        made += 1
    removed = 0
    for old in SHOTS_OUT.glob("*.webp"):
        if old.stem not in names:
            old.unlink()
            removed += 1
    size = sum(f.stat().st_size for f in SHOTS_OUT.glob("*.webp"))
    print(f"{SHOTS_OUT.relative_to(ROOT)}: {len(names)} screenshots ({made} encoded, {removed} removed), {size:,} bytes")


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    shots: set[str] = set()
    for lang, (path, base) in SOURCES.items():
        doc = build(path, base)
        for g in doc["groups"]:
            for c in g["chapters"]:
                for p in c["pages"]:
                    shots.update(shot_names(p.get("intro", []) + p["blocks"]))
        out = OUT_DIR / f"help.{lang}.json"
        text = json.dumps(doc, ensure_ascii=False, separators=(",", ":"))
        out.write_text(text + "\n", encoding="utf-8", newline="\n")
        pages = sum(len(c["pages"]) for g in doc["groups"] for c in g["chapters"])
        print(f"{out.relative_to(ROOT)}: {pages} pages, {out.stat().st_size:,} bytes")
    sync_shots(shots)


if __name__ == "__main__":
    main()
