"""Regenerates the "What was translated" section of NOTICE.md from docs/port-map.md (GPL attribution list).

    python3 tools/notice.py           # rewrite the section between the two marker comments
    python3 tools/notice.py --check   # exit 1 if NOTICE.md is stale (re-run after docs/port-map.md changes)

Every port-map row with status `ported` or `partial` is listed: original file(s) (linked to the source at the pinned commit)
and the C# counterpart(s); a partial row also gets a short reason (its first clause). The rest of NOTICE.md is hand-written
and kept. Plain Python 3, no packages. Deterministic: the output depends only on docs/port-map.md.
"""
import re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PORT_MAP = ROOT / "docs/port-map.md"
NOTICE = ROOT / "NOTICE.md"
COMMIT = "4a20d480bf1a7cceddfadf0918ed23d719f60bc9"
BASE = "https://github.com/shutori/Shutori_Re_Kenshi_Source/blob/" + COMMIT + "/ProvingGrounds/"
BEGIN = "<!-- notice.py: begin generated list (python3 tools/notice.py) -->"
END = "<!-- notice.py: end generated list -->"
MAX_REASON = 110


def split_cells(line):
    """Split a markdown table row on unescaped pipes."""
    cells = re.split(r"(?<!\\)\|", line.strip().strip("|"))
    return [c.strip().replace("\\|", "|") for c in cells]


def parse_rows(text):
    rows = []
    for line in text.splitlines():
        if not line.startswith("|"):
            continue
        cells = split_cells(line)
        if len(cells) != 5 or cells[0] == "C++ file" or cells[0].startswith("---"):
            continue
        files, _lines, _kind, cs, status = cells
        word = re.match(r"[a-z]+", status)
        if word and word.group(0) in ("ported", "partial"):
            rows.append((files, cs, status))
    return rows


def short_reason(status):
    """The first clause of a partial row's reason, at most MAX_REASON characters."""
    reason = status.split(":", 1)[1].strip() if ":" in status else ""
    reason = re.split(r";|\(|\. ", reason)[0].strip().rstrip(",:")
    if len(reason) > MAX_REASON:
        reason = reason[:MAX_REASON].rsplit(" ", 1)[0].rstrip(",:") + "..."
    return reason


def link(name):
    name = name.strip()
    path = name if name.startswith("tests/") else "plugin/src/" + name
    return "[%s](%s%s)" % (name, BASE, path)


def render(rows):
    n_part = sum(1 for _, _, s in rows if s.startswith("partial"))
    out = ["%d files or file groups of the original are translated (%d complete, %d partial). Links point at the original source at "
           "commit `%s`." % (len(rows), len(rows) - n_part, n_part, COMMIT[:7]), "",
           "| Original (Shutori) | This port | Status |", "|---|---|---|"]
    for files, cs, status in rows:
        orig = ", ".join(link(f) for f in files.split(","))
        st = "ported" if status.startswith("ported") else "partial: " + short_reason(status)
        out.append("| %s | %s | %s |" % (orig, cs.replace("|", "\\|"), st.replace("|", "\\|")))
    return "\n".join(out)


def generate(notice_text, port_map_text):
    block = BEGIN + "\n" + render(parse_rows(port_map_text)) + "\n" + END
    if BEGIN in notice_text and END in notice_text:
        head, rest = notice_text.split(BEGIN, 1)
        return head + block + rest.split(END, 1)[1]
    m = re.search(r"## What was translated\n\n(.*?)\n## ", notice_text, re.S)
    if not m:
        raise SystemExit("NOTICE.md has no '## What was translated' section")
    return notice_text[:m.start(1)] + block + "\n" + notice_text[m.end(1):]


def main(argv):
    old = NOTICE.read_text(encoding="utf-8")
    new = generate(old, PORT_MAP.read_text(encoding="utf-8"))
    if "--check" in argv:
        if new != old:
            print("NOTICE.md is stale: run python3 tools/notice.py")
            return 1
        print("NOTICE.md OK")
        return 0
    with open(NOTICE, "w", encoding="utf-8", newline="") as f:
        f.write(new)
    print("NOTICE.md written")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
