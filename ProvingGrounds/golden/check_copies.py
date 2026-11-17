#!/usr/bin/env python3
"""Fails when a VERBATIM copy in a golden generator no longer equals upstream line for line.

Grammar (each marker a whole line at column 0; lines are compared with any trailing \\r removed, so CRLF checkouts pass):
  // copies: <n>                        once, near the top: the exact number of copies in the file
  // BEGIN COPY <File>:<first>-<last>   opens a copy of upstream/ProvingGrounds/plugin/src/<File> lines first..last
  // END COPY                           closes it
A copy's lines must equal those upstream lines exactly (indentation and spacing included), with 1 <= first <= last <= the upstream
file's line count. Any other line starting with "// BEGIN COPY", "// END COPY" or "// copies:" is an error, as are a nested BEGIN,
an END with no BEGIN, a BEGIN with no END, markers without a "// copies:" line, and a count that is not exact.
Usage: python3 golden/check_copies.py golden/gen_x.cpp [...]   (gen.sh runs it for every generator with a copy marker;
Tests/Fidelity/GoldenCopiesTests.cs runs the same check in the test suite)."""
import os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "upstream", "ProvingGrounds", "plugin", "src")
BEGIN = re.compile(r"^// BEGIN COPY ([A-Za-z0-9_]+\.(?:cpp|h)):([0-9]+)-([0-9]+)$")
END = "// END COPY"
COUNT = re.compile(r"^// copies: ([0-9]+)$")


def read_lines(path):
    with open(path, encoding="utf-8", newline="") as f:
        text = f.read()
    lines = text.split("\n")
    if lines and lines[-1] == "":
        lines.pop()                     # a final newline ends the last line; it is not one more line
    return [line[:-1] if line.endswith("\r") else line for line in lines]


def check(path):
    name = os.path.basename(path)
    lines = read_lines(path)
    errors, copies, declared, open_at, upstream_cache = [], 0, None, None, {}
    for i, line in enumerate(lines):
        where = f"{name}:{i + 1}"
        if line.startswith("// copies:"):
            m = COUNT.match(line)
            if not m:
                errors.append(f"{where}: malformed count line {line!r} (expected '// copies: <n>')")
            elif declared is not None:
                errors.append(f"{where}: a second '// copies:' line")
            else:
                declared = int(m.group(1))
        elif line.startswith("// BEGIN COPY"):
            m = BEGIN.match(line)
            if not m:
                errors.append(f"{where}: malformed marker {line!r} (expected '// BEGIN COPY <File>:<first>-<last>')")
                continue
            if open_at is not None:
                errors.append(f"{where}: BEGIN COPY inside the copy opened at line {open_at[0] + 1}")
            open_at = (i, m.group(1), int(m.group(2)), int(m.group(3)))
        elif line.startswith("// END COPY"):
            if line != END:
                errors.append(f"{where}: malformed marker {line!r} (expected '// END COPY')")
                continue
            if open_at is None:
                errors.append(f"{where}: END COPY without BEGIN COPY")
                continue
            start, file, first, last = open_at
            open_at = None
            copies += 1
            src = os.path.join(SRC, file)
            if not os.path.isfile(src):
                errors.append(f"{name}:{start + 1}: no upstream file {file}")
                continue
            if file not in upstream_cache:
                upstream_cache[file] = read_lines(src)
            upstream = upstream_cache[file]
            if not (1 <= first <= last <= len(upstream)):
                errors.append(f"{name}:{start + 1}: range {file}:{first}-{last} is outside 1..{len(upstream)} "
                              f"(or first > last)")
                continue
            copy = lines[start + 1:i]
            want = upstream[first - 1:last]
            if len(copy) != len(want):
                errors.append(f"{name}:{start + 1}: {file}:{first}-{last} is {len(want)} lines, the copy has {len(copy)}")
            for k, (a, b) in enumerate(zip(copy, want)):
                if a != b:
                    errors.append(f"{name}:{start + 2 + k}: differs from {file}:{first + k}\n  copy:     {a!r}\n  upstream: {b!r}")
                    break
    if open_at is not None:
        errors.append(f"{name}:{open_at[0] + 1}: BEGIN COPY {open_at[1]}:{open_at[2]}-{open_at[3]} has no END COPY")
    if declared is None and copies > 0:
        errors.append(f"{name}: {copies} copies but no '// copies: <n>' line")
    elif declared is not None and declared != copies:
        errors.append(f"{name}: '// copies: {declared}' but the file has {copies}")
    return copies, errors


def main(argv):
    bad = False
    for path in argv[1:]:
        copies, errors = check(path)
        for e in errors:
            print("check_copies: " + e, file=sys.stderr)
        if copies == 0 and not errors:
            print(f"check_copies: {path}: no copies", file=sys.stderr)
            bad = True
        bad = bad or bool(errors)
        if not errors and copies:
            print(f"check_copies: {os.path.basename(path)}: {copies} copies equal upstream")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
