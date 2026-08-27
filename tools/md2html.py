#!/usr/bin/env python3
"""Render a TfLens markdown document to a self-contained HTML sibling.

Why this exists
---------------
The human-readable docs (BRD, Architecture, UI Design, Usage Guide, PROJECT-STATUS) each ship an HTML
sibling, and the Developer Guide did not — which is the one document a developer actually reads first.
No markdown converter is installed on this machine (no pandoc, no python-markdown, no node library), so
rather than hand-writing a thousand lines of HTML once and never being able to regenerate it, this
script reproduces the same shell those files already use: the pre-paint theme resolver, the light/dark
token set, a sidebar table of contents, and the copy-button/anchor behaviour.

It deliberately supports only the markdown this project actually writes — headings, paragraphs, fenced
code, tables, nested lists, block quotes, images, links, inline code, bold/italic and horizontal rules.
It is not a general markdown implementation and does not try to be.

Usage
-----
    python3 tools/md2html.py docs/TfLens-DevGuide.md [more.md ...]

Each input produces a sibling `.html`. The `<title>`, the sidebar heading and the subtitle are taken
from the document's own first `#` heading and the `**Last updated:**` / `**Audience:**` lines when
present.
"""

from __future__ import annotations

import html
import re
import sys
from pathlib import Path

# ---------------------------------------------------------------- inline markup


def slugify(text: str) -> str:
    """Turn heading text into the anchor id the sidebar links to."""
    text = re.sub(r"`([^`]*)`", r"\1", text)
    text = re.sub(r"\*\*([^*]*)\*\*", r"\1", text)
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = text.lower()
    text = re.sub(r"[^a-z0-9\s-]", "", text)
    text = re.sub(r"\s+", "-", text.strip())
    return re.sub(r"-+", "-", text)


def inline(text: str) -> str:
    """Render inline markup. Code spans are protected first so their contents stay literal."""
    spans: list[str] = []

    def stash(match: re.Match[str]) -> str:
        spans.append(match.group(1))
        return f"\x00{len(spans) - 1}\x00"

    text = re.sub(r"`([^`]+)`", stash, text)
    text = html.escape(text, quote=False)

    text = re.sub(r"!\[([^\]]*)\]\(([^)\s]+)\)", r'<img src="\2" alt="\1" loading="lazy">', text)
    text = re.sub(r"\[([^\]]+)\]\(([^)\s]+)\)", r'<a href="\2">\1</a>', text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", text)
    text = re.sub(r"(?<![\w*])\*([^*\n]+)\*(?![\w*])", r"<em>\1</em>", text)
    text = re.sub(r"~~([^~]+)~~", r"<del>\1</del>", text)

    for index, span in enumerate(spans):
        text = text.replace(f"\x00{index}\x00", f"<code>{html.escape(span, quote=False)}</code>")

    return text


# ---------------------------------------------------------------- block parsing


def render_table(rows: list[str]) -> str:
    """Render a pipe table. The second row is the alignment rule and is dropped."""

    def cells(line: str) -> list[str]:
        line = line.strip()
        if line.startswith("|"):
            line = line[1:]
        if line.endswith("|"):
            line = line[:-1]
        return [c.strip() for c in line.split("|")]

    head = cells(rows[0])
    body = [cells(r) for r in rows[2:]]

    out = ['<div class="table-wrap"><table>', "<thead><tr>"]
    out += [f"<th>{inline(c)}</th>" for c in head]
    out.append("</tr></thead><tbody>")
    for row in body:
        out.append("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in row) + "</tr>")
    out.append("</tbody></table></div>")
    return "".join(out)


def render(md: str) -> tuple[str, list[tuple[int, str, str]]]:
    """Convert a whole document. Returns the body HTML and the heading list for the sidebar."""
    lines = md.split("\n")
    out: list[str] = []
    toc: list[tuple[int, str, str]] = []
    i = 0
    list_stack: list[str] = []

    def close_lists(to_depth: int = 0) -> None:
        while len(list_stack) > to_depth:
            out.append(f"</{list_stack.pop()}>")

    while i < len(lines):
        line = lines[i]
        stripped = line.strip()

        # HTML comments — carried through so agent notes stay invisible but present.
        if stripped.startswith("<!--"):
            while i < len(lines) and "-->" not in lines[i]:
                i += 1
            i += 1
            continue

        # Fenced code.
        if stripped.startswith("```"):
            close_lists()
            lang = stripped[3:].strip()
            i += 1
            buf: list[str] = []
            while i < len(lines) and not lines[i].strip().startswith("```"):
                buf.append(lines[i])
                i += 1
            i += 1
            code = html.escape("\n".join(buf), quote=False)
            cls = f' class="language-{html.escape(lang)}"' if lang else ""
            out.append(f'<div class="codeblock"><button class="copy">Copy</button>'
                       f"<pre><code{cls}>{code}</code></pre></div>")
            continue

        if not stripped:
            close_lists()
            i += 1
            continue

        # Headings.
        heading = re.match(r"^(#{1,6})\s+(.*)$", stripped)
        if heading:
            close_lists()
            level = len(heading.group(1))
            text = heading.group(2).strip()
            slug = slugify(text)
            if level > 1:
                toc.append((level, slug, re.sub(r"`", "", text)))
            out.append(f'<h{level} id="{slug}">{inline(text)}</h{level}>')
            i += 1
            continue

        if re.match(r"^(-{3,}|\*{3,}|_{3,})$", stripped):
            close_lists()
            out.append("<hr>")
            i += 1
            continue

        # Tables.
        if "|" in stripped and i + 1 < len(lines) and re.match(r"^[\s|:-]+$", lines[i + 1].strip()) \
                and "-" in lines[i + 1]:
            close_lists()
            block = []
            while i < len(lines) and "|" in lines[i]:
                block.append(lines[i])
                i += 1
            out.append(render_table(block))
            continue

        # Block quotes (including multi-line continuations).
        if stripped.startswith(">"):
            close_lists()
            buf = []
            while i < len(lines) and lines[i].strip().startswith(">"):
                buf.append(lines[i].strip().lstrip(">").strip())
                i += 1
            joined = " ".join(b for b in buf if b)
            out.append(f"<blockquote>{inline(joined)}</blockquote>")
            continue

        # Lists, with indentation-based nesting.
        item = re.match(r"^(\s*)([-*+]|\d+\.)\s+(.*)$", line)
        if item:
            indent = len(item.group(1)) // 2
            ordered = bool(re.match(r"\d+\.", item.group(2)))
            tag = "ol" if ordered else "ul"
            while len(list_stack) > indent + 1:
                out.append(f"</{list_stack.pop()}>")
            if len(list_stack) < indent + 1:
                out.append(f"<{tag}>")
                list_stack.append(tag)
            out.append(f"<li>{inline(item.group(3))}</li>")
            i += 1
            continue

        # Paragraph: absorb following non-blank, non-structural lines.
        close_lists()
        buf = [stripped]
        i += 1
        while i < len(lines):
            nxt = lines[i].strip()
            if not nxt or nxt.startswith(("#", "```", ">", "|", "---")) \
                    or re.match(r"^(\s*)([-*+]|\d+\.)\s+", lines[i]):
                break
            buf.append(nxt)
            i += 1
        out.append(f"<p>{inline(' '.join(buf))}</p>")

    close_lists()
    return "\n".join(out), toc


# ---------------------------------------------------------------- page shell

SHELL = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>__TITLE__</title>
<script>
/* Resolve the theme BEFORE first paint (no flash). Saved choice wins; otherwise
   pick by time of day — light 07:00-19:00 local, softened dark at night. */
(function(){
  try{
    var t = localStorage.getItem('tf-theme');
    if(t!=='light' && t!=='dark'){ var h = new Date().getHours(); t = (h>=7 && h<19) ? 'light' : 'dark'; }
    document.documentElement.setAttribute('data-theme', t);
  }catch(e){ document.documentElement.setAttribute('data-theme','light'); }
})();
</script>
<style>
:root{
  --bg:#faf9f7; --fg:#1f2328; --muted:#5b636d; --border:#e2e0dc; --card:#ffffff;
  --accent:#0b62c4; --code-bg:#f4f2ef; --quote:#f6f4f1; --shadow:0 1px 2px rgba(0,0,0,.05);
  --mono:ui-monospace,SFMono-Regular,"SF Mono",Menlo,Consolas,monospace;
}
:root[data-theme="dark"]{
  --bg:#14171c; --fg:#dde1e7; --muted:#98a1ad; --border:#2a3038; --card:#191d23;
  --accent:#7cc4ff; --code-bg:#11151a; --quote:#181d24; --shadow:none;
}
*{box-sizing:border-box}
html,body{margin:0;padding:0}
body{background:var(--bg);color:var(--fg);
  font:16px/1.65 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Helvetica,Arial,sans-serif;}
.layout{display:flex;gap:32px;max-width:1280px;margin:0 auto;padding:32px 24px 96px}
nav.side{width:270px;flex:0 0 270px;position:sticky;top:32px;align-self:flex-start;max-height:calc(100vh - 64px);overflow:auto}
nav.side h1{font-size:17px;margin:0 0 4px;line-height:1.3}
nav.side .sub{font-size:12px;color:var(--muted);margin-bottom:16px}
nav.side .group{font-size:11px;letter-spacing:.6px;text-transform:uppercase;color:var(--muted);margin:14px 0 6px}
nav.side ol{list-style:none;margin:0;padding:0;font-size:13.5px}
nav.side li{margin:2px 0}
nav.side li.h3{padding-left:14px;font-size:12.5px}
nav.side a{color:var(--muted);text-decoration:none;display:block;padding:2px 6px;border-radius:5px}
nav.side a:hover{color:var(--accent);background:var(--code-bg)}
main{flex:1;min-width:0;max-width:900px}
h1,h2,h3,h4{line-height:1.25;scroll-margin-top:24px}
h1{font-size:30px;margin:0 0 8px}
h2{font-size:22px;margin:36px 0 12px;padding-bottom:6px;border-bottom:1px solid var(--border)}
h3{font-size:17.5px;margin:26px 0 8px}
h4{font-size:15px;margin:20px 0 6px;color:var(--muted)}
p{margin:12px 0}
a{color:var(--accent)}
code{font-family:var(--mono);font-size:.88em;background:var(--code-bg);padding:.12em .38em;border-radius:4px}
.codeblock{position:relative;margin:16px 0}
.codeblock pre{background:var(--code-bg);border:1px solid var(--border);border-radius:8px;padding:14px 16px;overflow:auto;margin:0}
.codeblock pre code{background:none;padding:0;font-size:13px;line-height:1.55}
.codeblock .copy{position:absolute;top:8px;right:8px;font-size:11px;padding:3px 9px;border-radius:5px;
  border:1px solid var(--border);background:var(--card);color:var(--muted);cursor:pointer;opacity:0;transition:opacity .15s}
.codeblock:hover .copy{opacity:1}
.codeblock .copy:hover{color:var(--accent);border-color:var(--accent)}
.table-wrap{overflow-x:auto;margin:16px 0}
table{border-collapse:collapse;width:100%;font-size:14px}
th,td{border:1px solid var(--border);padding:8px 11px;text-align:left;vertical-align:top}
th{background:var(--code-bg);font-weight:600}
blockquote{margin:16px 0;padding:10px 16px;background:var(--quote);border-left:3px solid var(--accent);border-radius:0 6px 6px 0;color:var(--fg)}
ul,ol{margin:12px 0;padding-left:26px}
li{margin:4px 0}
img{max-width:100%;height:auto;border:1px solid var(--border);border-radius:8px;box-shadow:var(--shadow);margin:12px 0}
hr{border:0;border-top:1px solid var(--border);margin:28px 0}
del{color:var(--muted)}
.theme-toggle{position:fixed;top:14px;right:16px;z-index:20;font-size:12px;padding:5px 11px;border-radius:6px;
  border:1px solid var(--border);background:var(--card);color:var(--muted);cursor:pointer}
.theme-toggle:hover{color:var(--accent);border-color:var(--accent)}
@media(max-width:1000px){.layout{flex-direction:column;padding:20px 16px 64px}nav.side{position:static;width:auto;flex:auto;max-height:none}}
</style>
</head>
<body>
<button id="themeToggle" class="theme-toggle" title="Toggle light / dark">Theme</button>
<div class="layout">
  <nav class="side">
    <h1>__NAV_TITLE__</h1>
    <div class="sub">__SUBTITLE__</div>
    <div class="group">Contents</div>
    <ol>
__TOC__
    </ol>
  </nav>
  <main>
__BODY__
  </main>
</div>
<script>
var btn = document.getElementById('themeToggle');
function label(){ var d = document.documentElement.getAttribute('data-theme')==='dark';
  btn.textContent = d ? 'Light' : 'Dark'; }
label();
btn.addEventListener('click', function(){
  var d = document.documentElement.getAttribute('data-theme')==='dark';
  var next = d ? 'light' : 'dark';
  document.documentElement.setAttribute('data-theme', next);
  try{ localStorage.setItem('tf-theme', next); }catch(e){}
  label();
});
document.querySelectorAll('.codeblock .copy').forEach(function(b){
  b.addEventListener('click', function(){
    var code = b.parentElement.querySelector('code');
    navigator.clipboard.writeText(code.innerText).then(function(){
      var old = b.textContent; b.textContent = 'Copied';
      setTimeout(function(){ b.textContent = old; }, 1200);
    });
  });
});
</script>
</body>
</html>
"""


def build(path: Path) -> Path:
    """Render one markdown file to its HTML sibling."""
    md = path.read_text(encoding="utf-8")
    body, toc = render(md)

    title_match = re.search(r"^#\s+(.*)$", md, re.M)
    title = title_match.group(1).strip() if title_match else path.stem

    subtitle_bits = []
    for label in ("Last updated", "Audience", "Status"):
        found = re.search(rf"\*\*{label}:\*\*\s*(.+)", md)
        if found:
            subtitle_bits.append(re.sub(r"\*\*|`", "", found.group(1)).strip())

    toc_html = "\n".join(
        f'      <li{" class=\"h3\"" if level >= 3 else ""}>'
        f'<a href="#{slug}">{html.escape(text)}</a></li>'
        for level, slug, text in toc
        if level <= 3
    )

    page = (SHELL
            .replace("__TITLE__", html.escape(title))
            .replace("__NAV_TITLE__", html.escape(title))
            .replace("__SUBTITLE__", html.escape(" · ".join(subtitle_bits)))
            .replace("__TOC__", toc_html)
            .replace("__BODY__", body))

    out = path.with_suffix(".html")
    out.write_text(page, encoding="utf-8")
    return out


def main(argv: list[str]) -> int:
    """Render every markdown file named on the command line."""
    if len(argv) < 2:
        print(__doc__)
        return 2

    for name in argv[1:]:
        source = Path(name)
        if not source.exists():
            print(f"md2html: no such file: {source}", file=sys.stderr)
            return 1
        written = build(source)
        print(f"{source} -> {written} ({written.stat().st_size:,} bytes)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
