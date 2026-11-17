"""Builds the mod in Release and copies the package into dist/, then runs RefCheck on both DLLs against 0.17.3.

    python3 tools/package.py

Prints OK only when both DLLs pass; exits 1 otherwise. layouts/*.layout are copied once there are any.
Also writes dist/source.zip, the GPLv3 corresponding source of the two DLLs: `git archive` of HEAD (no history) holding SOURCE_PATHS
under ProvingGrounds/, the two shared build files from ../_dual beside it, and BUILD.txt. It stops when any of those paths has
uncommitted changes, so the zip always matches the DLLs built from the same tree.
"""
import io, os, re, shutil, subprocess, sys, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOTNET = "/mnt/c/Program Files/dotnet/dotnet.exe"


def game_folder() -> Path:
    """Meitou 0.17.3 (the game the DLLs are checked against): PG_GAME_DIR (a path as this Python sees it), else the 0.17.3 folder of the
    first Windows user that has one (WSL), else %LOCALAPPDATA% (Windows Python)."""
    if os.environ.get("PG_GAME_DIR"):
        return Path(os.environ["PG_GAME_DIR"])
    found = sorted(Path("/mnt/c/Users").glob("*/AppData/Local/Meitou/versions/0.17.3")) if Path("/mnt/c/Users").exists() else []
    return found[0] if found else Path(os.environ.get("LOCALAPPDATA", "")) / "Meitou" / "versions" / "0.17.3"


GAME = game_folder()
REFCHECK = ROOT.parent / "_dual/RefCheck/bin/Release/net10.0/RefCheck.exe"
BUILD = ROOT / "bin/modern/Release/net10.0"
CORE = ROOT / "Core/bin/modern/Release/net10.0"
DLLS = [("ProvingGrounds.Core.dll", CORE), ("ProvingGroundsMeitou.dll", BUILD)]
REQUIRED = ["meitou.json", "ProvingGroundsMeitou.mod", "README.txt", "LICENSE", "NOTICE.md"]
# Written by the mod on its first start in the player's mod folder (task 2.6, like the original); a shipped copy would overwrite the
# player's edits on every update. recovered/ (task 2.4) is the player's too.
# The three logs (task 2.7: pg_debug.log, pg_combat_debug.log, pg_challenge_debug.log) are written there too.
NEVER_SHIP = ["pg_config.json*", "recovered", "pg_*debug.log"]   # globs: pg_config.json and its .tmp / .write.tmp / .restore companions
# What builds the two DLLs, plus the licences and the docs README.txt and NOTICE.md point at (task 9.4c). Not: Tests/ (Tests/Golden is
# 53 MB of generated data), Check/, golden/, upstream/ (Shutori's own public source, linked from NOTICE.md), the rest of docs/, tools/scenarios.
SOURCE_PATHS = ["Core", "src", "ProvingGroundsMeitou.csproj", "Directory.Build.props", "meitou.json", "ProvingGroundsMeitou.mod",
                "README.txt", "LICENSE", "NOTICE.md", "tools/package.py", "tools/notice.py", "docs/port-map.md", "docs/divergences.md",
                "docs/versions.md", "docs/api.md"]
# Directory.Build.props imports ../_dual/Meitou.Dual.props (which imports nothing); the csproj imports ../_dual/Meitou.GameRefs.targets.
SHARED_BUILD = [ROOT.parent / "_dual/Meitou.Dual.props", ROOT.parent / "_dual/Meitou.GameRefs.targets"]
BUILD_TXT = """Proving Grounds for Meitou: building from this source (GPLv3, see ProvingGrounds/LICENSE and ProvingGrounds/NOTICE.md)

Layout: ProvingGrounds/ is the mod's source. _dual/ holds the two shared MSBuild files the build imports from the folder beside it
(ProvingGrounds/Directory.Build.props imports ../_dual/Meitou.Dual.props; the csproj imports ../_dual/Meitou.GameRefs.targets).
Keep both folders side by side.

Needs: the .NET SDK 10 and Meitou 0.17.3 installed (the oldest game the mod supports; the build references that game's
MeitouClient.dll, Meitou.*.dll and 0Harmony.dll and does not copy them). Directory.Build.props looks for it in
%LOCALAPPDATA%\\Meitou\\versions\\0.17.3. Another folder: dotnet build -p:GameDir=<folder holding MeitouClient.dll>.

Build:   cd ProvingGrounds
         dotnet build -c Release ProvingGroundsMeitou.csproj
Output:  bin/modern/Release/net10.0/ProvingGroundsMeitou.dll and Core/bin/modern/Release/net10.0/ProvingGrounds.Core.dll
Install: copy those two DLLs with meitou.json, ProvingGroundsMeitou.mod, README.txt, LICENSE and NOTICE.md into one mod folder.
The mod needs the original Proving Grounds (Steam Workshop 3774745195) enabled above it.

tools/package.py is the script the release uses (it also runs a reference check tool that is not part of this source and uses
fixed paths of the author's PC). tools/notice.py regenerates NOTICE.md's list from docs/port-map.md.
"""


def win(p):
    m = re.match(r"^/mnt/([a-z])/(.*)$", str(Path(p).resolve()))
    if not m:
        sys.exit(f"not a Windows drive path: {p}")
    return f"{m.group(1).upper()}:\\" + m.group(2).replace("/", "\\")


def write_source_zip(path):
    """dist/source.zip: HEAD's SOURCE_PATHS under ProvingGrounds/, _dual's two build files, BUILD.txt (no git history)."""
    r = subprocess.run(["git", "archive", "--format=zip", "--prefix=ProvingGrounds/", "HEAD", "--"] + SOURCE_PATHS, cwd=ROOT,
                       capture_output=True)
    if r.returncode != 0:
        sys.exit("git archive failed: " + r.stderr.decode(errors="replace"))
    with zipfile.ZipFile(io.BytesIO(r.stdout)) as src, zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as out:
        for info in src.infolist():
            out.writestr(info, src.read(info), compress_type=zipfile.ZIP_DEFLATED)
        for f in SHARED_BUILD:
            if not f.is_file():
                sys.exit(f"missing shared build file {f}")
            out.write(f, "_dual/" + f.name)
        out.writestr("BUILD.txt", BUILD_TXT.replace("\n", "\r\n"))
    print(f"  source.zip: {path.stat().st_size // 1024} KB")


def main():
    if not REFCHECK.is_file():
        sys.exit(f"RefCheck not found at {REFCHECK}: build it first (see ../_dual/README.md)")
    missing = [n for n in REQUIRED if not (ROOT / n).is_file()]
    if missing:
        sys.exit(f"missing from the source folder: {', '.join(missing)}")
    dirty = subprocess.run(["git", "status", "--porcelain", "--"] + SOURCE_PATHS, cwd=ROOT, capture_output=True, text=True).stdout
    if dirty.strip():
        sys.exit("uncommitted changes in the shipped source (source.zip is made from HEAD): commit them first\n" + dirty)
    r = subprocess.run([DOTNET, "build", "-c", "Release", "-nologo", "-v:q", "ProvingGroundsMeitou.csproj"], cwd=ROOT,
                       capture_output=True, text=True, errors="replace")
    if r.returncode != 0:
        print((r.stdout + r.stderr)[-3000:])
        sys.exit("build failed")
    missing = [str(folder / name) for name, folder in DLLS if not (folder / name).is_file()]
    if missing:
        sys.exit(f"the build did not produce: {', '.join(missing)}")
    dist = ROOT / "dist"
    if dist.exists():
        shutil.rmtree(dist)
    dist.mkdir()
    for name, folder in DLLS:
        shutil.copy2(folder / name, dist / name)
    for name in REQUIRED:
        shutil.copy2(ROOT / name, dist / name)
    layouts = sorted((ROOT / "layouts").glob("*.layout"))
    if layouts:
        (dist / "layouts").mkdir()
        for f in layouts:
            shutil.copy2(f, dist / "layouts" / f.name)
    write_source_zip(dist / "source.zip")
    failed = 0
    for name, _ in DLLS:
        r = subprocess.run([str(REFCHECK), win(dist / name), win(GAME), win(dist)], capture_output=True, text=True, errors="replace")
        out = (r.stdout + r.stderr).strip()
        print(f"  {name}: {out.splitlines()[-1] if out else '(no output from RefCheck)'}")
        if r.returncode != 0 or not out:
            failed += 1
            print("\n".join("      " + l for l in out.splitlines()[:-1][:25]))
    print(f"  dist: {', '.join(sorted(p.name for p in dist.iterdir()))}")
    shipped = sorted({p.name for n in NEVER_SHIP for p in dist.glob(n)})
    if shipped:
        sys.exit(f"dist holds {', '.join(shipped)}: the player's files must never ship")
    if failed:
        sys.exit(f"{failed} reference check(s) failed")
    print("OK")


if __name__ == "__main__":
    main()
