"""Installs Proving Grounds for Meitou into Kenshi\\mods\\ProvingGroundsMeitou, and optionally writes the shareable zip.

Copies the package in dist/ (both DLLs, meitou.json, the .mod, README, LICENSE, NOTICE, source.zip and layouts/). Refuses while the game runs (it holds
the DLLs open) or when it cannot tell (use --force only if you are sure the game is closed). Does not touch mods.cfg or meitou.user.json.

    python3 tools/install.py                   # into the Kenshi folder the Meitou launcher uses (launcher.json), from WSL or Windows
    python3 tools/install.py --kenshi PATH     # another Kenshi folder (must already have a mods folder)
    python3 tools/install.py --zip PATH.zip    # also write the shareable zip
Package first: python3 tools/package.py
"""
import argparse, json, os, pathlib, shutil, subprocess, sys, zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
DIST = ROOT / "dist"
PACKAGE = ["ProvingGrounds.Core.dll", "ProvingGroundsMeitou.dll", "meitou.json", "ProvingGroundsMeitou.mod", "README.txt", "LICENSE", "NOTICE.md",
           "source.zip"]   # task 9.4c: the GPLv3 source travels with the mod


def windows_path(path: str) -> pathlib.Path:
    """A Windows path as this Python sees it (E:\\x -> /mnt/e/x under WSL)."""
    if os.name == "nt" or len(path) < 2 or path[1] != ":":
        return pathlib.Path(path)
    return pathlib.Path("/mnt") / path[0].lower() / path[3:].replace("\\", "/")


def launcher_folder() -> pathlib.Path | None:
    candidates = []
    if os.environ.get("LOCALAPPDATA"):
        candidates.append(pathlib.Path(os.environ["LOCALAPPDATA"]) / "Meitou")
    candidates += sorted(pathlib.Path("/mnt/c/Users").glob("*/AppData/Local/Meitou")) if pathlib.Path("/mnt/c/Users").exists() else []
    return next((c for c in candidates if (c / "launcher.json").exists()), None)


def kenshi_folder() -> pathlib.Path:
    folder = launcher_folder()
    if folder is None:
        sys.exit("Meitou's launcher.json not found: pass --kenshi")
    path = json.loads((folder / "launcher.json").read_text(encoding="utf-8-sig")).get("KenshiPath")
    if not path:
        sys.exit("launcher.json has no KenshiPath: pass --kenshi")
    return windows_path(path)


def game_running() -> bool | None:
    """True / False, or None when tasklist could not be read."""
    tasklist = "tasklist" if os.name == "nt" else "/mnt/c/Windows/System32/tasklist.exe"
    try:
        out = subprocess.run([tasklist], capture_output=True, text=True, check=True).stdout.lower()
    except (OSError, subprocess.CalledProcessError):
        return None
    return "meitouclient.exe" in out


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--kenshi", help="the Kenshi folder (default: the launcher's)")
    parser.add_argument("--zip", help="also write the shareable zip here")
    parser.add_argument("--force", action="store_true", help="install even when it cannot be checked whether the game runs")
    args = parser.parse_args()
    missing = [f for f in PACKAGE if not (DIST / f).exists()]
    if missing:
        sys.exit(f"missing from {DIST}: {', '.join(missing)}: package first (python3 tools/package.py)")
    running = game_running()
    if running:
        sys.exit("the game is running: close it first (it holds the Proving Grounds DLLs open)")
    if running is None and not args.force:
        sys.exit("could not check whether the game runs (tasklist failed): close it and pass --force")

    kenshi = windows_path(args.kenshi) if args.kenshi else kenshi_folder()
    if not (kenshi / "mods").is_dir():
        sys.exit(f"{kenshi} has no mods folder: is it a Kenshi folder?")
    target = kenshi / "mods" / "ProvingGroundsMeitou"
    target.mkdir(exist_ok=True)
    for f in PACKAGE:
        shutil.copy2(DIST / f, target / f)
    layouts = sorted((DIST / "layouts").glob("*.layout")) if (DIST / "layouts").is_dir() else []
    stale = []
    if (target / "layouts").is_dir():
        keep = {f.name for f in layouts}
        stale = [f for f in (target / "layouts").glob("*.layout") if f.name not in keep]
        for f in stale:
            f.unlink()
    if layouts:
        (target / "layouts").mkdir(exist_ok=True)
        for f in layouts:
            shutil.copy2(f, target / "layouts" / f.name)
    print(f"installed into {target} ({len(PACKAGE)} files, {len(layouts)} layouts"
          f"{'; removed old ' + ', '.join(f.name for f in stale) if stale else ''})")

    if args.zip:
        zip_path = windows_path(args.zip)
        if zip_path.exists():
            print(f"replacing {zip_path}")
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
            for f in PACKAGE:
                z.write(DIST / f, f"ProvingGroundsMeitou/{f}")
            for f in layouts:
                z.write(f, f"ProvingGroundsMeitou/layouts/{f.name}")
        print(f"wrote {zip_path}")


if __name__ == "__main__":
    main()
