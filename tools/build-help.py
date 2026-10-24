#!/usr/bin/env python3
"""Build the in-app guide (F1) from the website guide.

Reads docs/guide.html (Korean) and docs/en/guide.html (English) and writes
src/Gpixel.App/Assets/Help/help.<lang>.json. Re-run after editing either guide:

    python tools/build-help.py

Python 3 standard library only. Pages are split like docs/guide.js does: each
chapter (section.doc, h2) has an intro page (h2 + content before the first h3),
and every h3 starts its own page.
"""

from __future__ import annotations

import json
import re
import sys
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "src" / "Gpixel.App" / "Assets" / "Help"
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
        page = None
        for c in section.children:
            if isinstance(c, str):
                if c.strip():
                    raise SystemExit(f"{path}: stray text in section {cid}")
                continue
            if c.tag == "h2":
                chapter["title"] = runs_text(runs_of(c, base))
                page = {"id": cid, "title": chapter["title"], "blocks": []}
                chapter["pages"].append(page)
                continue
            if c.tag == "h3":
                page = {"id": c.attrs["id"], "title": runs_text(runs_of(c, base)), "blocks": []}
                chapter["pages"].append(page)
                continue
            if page is None:
                raise SystemExit(f"{path}: content before h2 in section {cid}")
            page["blocks"].extend(block(c, base, page["id"]))
        for p in chapter["pages"]:
            p["text"] = "\n".join(t for b in p["blocks"] for t in block_texts(b) if t)
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

    ids = {p["id"] for g in groups for c in g["chapters"] for p in c["pages"]}
    for g in groups:
        for c in g["chapters"]:
            for p in c["pages"]:
                for href in re.findall(r'"href": "#([^"]+)"', json.dumps(p["blocks"])):
                    if href not in ids:
                        print(f"warning: {path.name} page {p['id']} links to unknown #{href}", file=sys.stderr)
    return {"groups": groups}


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for lang, (path, base) in SOURCES.items():
        doc = build(path, base)
        out = OUT_DIR / f"help.{lang}.json"
        text = json.dumps(doc, ensure_ascii=False, separators=(",", ":"))
        out.write_text(text + "\n", encoding="utf-8", newline="\n")
        pages = sum(len(c["pages"]) for g in doc["groups"] for c in g["chapters"])
        print(f"{out.relative_to(ROOT)}: {pages} pages, {out.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
