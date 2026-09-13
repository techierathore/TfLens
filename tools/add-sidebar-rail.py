#!/usr/bin/env python3
"""Draw the sidebar rail in the shell mockups (amend-docs, 2026-09-13).

WHY. `docs/TfLens-UIDesign.md` §Layout shell names `SidebarRail` as part of the shell, and
REQ-UI-006 names it too, so the app renders it. The mockups never drew it. The parity gate
then measured the app's sidebar as 263px of content in a 255px box against a mockup at
255/255 and reported "content is cut off horizontally" against the APP — the app was right
and the drawing was wrong. Logged as MISS-TfLens-20260913-01.

WHAT. Per mockup carrying the shell:
  1. `.sidebar` gains `position:relative` and its `overflow:hidden` becomes `overflow:visible`,
     matching the app, whose sidebar computes `overflow-x: visible`. Nothing depended on the
     hidden overflow: the collapsed state hides its labels with `display:none`, not by clipping.
  2. A `.sb-rail` rule is added, mirroring the library's geometry — the rail is `w-4` (16px)
     pinned at `-right-4` and pulled back by `-translate-x-1/2`, so exactly half of it (8px)
     sits outside the sidebar's right edge.
  3. A `<button class="sb-rail">` is inserted as the last child of the sidebar, as the library
     renders it last inside `<Sidebar>`.

Idempotent: a file already carrying `sb-rail` is skipped.
"""
import re
import sys
from pathlib import Path

RAIL_CSS = (
    ".sb-rail{position:absolute;top:0;bottom:0;right:-16px;width:16px;transform:translateX(-50%);"
    "border:0;background:transparent;padding:0;cursor:w-resize;z-index:20}"
    ".sb-rail::after{content:'';position:absolute;top:0;bottom:0;left:50%;width:2px}"
    ".sb-rail:hover::after{background:var(--sidebar-border)}"
    "@media (max-width:767px){.sb-rail{display:none}}"
)
RAIL_HTML = '<button class="sb-rail" title="Toggle Sidebar" tabindex="-1" aria-label="Toggle Sidebar" onclick="toggleSidebar()"></button>'

SIDEBAR_RULE = re.compile(
    r"(\.sidebar\{width:256px;flex:none;background:var\(--sidebar\);"
    r"border-right:1px solid var\(--sidebar-border\);display:flex;flex-direction:column;"
    r"transition:width \.2s;)overflow:hidden(\})"
)
ASIDE_OPEN = '<aside class="sidebar" data-testid="app-sidebar">'


def amend(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    if "sb-rail" in text:
        return "skipped (already has the rail)"
    if ASIDE_OPEN not in text:
        return "skipped (no shell sidebar)"

    new, n = SIDEBAR_RULE.subn(r"\1position:relative;overflow:visible\2" + RAIL_CSS, text)
    if n != 1:
        return f"UNCHANGED — the .sidebar rule did not match exactly once (matched {n})"

    start = new.find(ASIDE_OPEN)
    close = new.find("</aside>", start)
    if close < 0:
        return "UNCHANGED — no closing </aside>"
    new = new[:close] + RAIL_HTML + new[close:]

    path.write_text(new, encoding="utf-8")
    return "amended"


def main(argv):
    targets = [Path(a) for a in argv[1:]]
    if not targets:
        targets = sorted(Path("docs/mockups").glob("*.html"))
    for p in targets:
        print(f"  {p.name:<34} {amend(p)}")


if __name__ == "__main__":
    main(sys.argv)
