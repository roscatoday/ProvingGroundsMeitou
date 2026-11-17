"""Writes docs/port-map.md: one row per original C++ file (the mirror rule of 00-overview §4.1) with its C# counterpart and status.

    python3 tools/port_map.py            # (re)write docs/port-map.md
    python3 tools/port_map.py --check    # exit 1 if docs/port-map.md is not what the script would write

Rows: every file under upstream/ProvingGrounds/plugin/src (paths relative to it) and upstream/ProvingGrounds/tests (written
tests/<name>). The .h and .cpp of one base name share a row. Re-running keeps each row's status (read back from the existing
docs/port-map.md); new rows start as `todo`, or as the preset `dropped: <why>` below. The output depends only on the upstream
files and the existing statuses (no dates, sorted rows), so the script is deterministic.

Kinds come from the research inventories: PA = proving-grounds-research/pg-source-player-arena.md §1 and §6, TW =
pg-source-town.md §1 and §6. A file the research does not classify must be added to DECIDED with the reason; an unknown file
stops the script so a new upstream file is never mapped silently.
"""
import re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
UPSTREAM = ROOT / "upstream/ProvingGrounds"
SRC = UPSTREAM / "plugin/src"
TESTS = UPSTREAM / "tests"
OUT = ROOT / "docs/port-map.md"

# PA §6.1 (pure) and TW §6.1 (pure).
PURE = """
MatchRules SparPodium SparHitLogic MmrRating ArenaMarks BalanceTuning ArenaRewards RewardProgression RewardTransaction
ArenaLedger LeaderboardData RewardsDialoguePolicy FighterIdentityPolicy LeaderboardSavePolicy WorldLifecyclePolicy
ArenaSaveCoordinator NativeSaveEvidence NativeSyncBoundary ArenaSnapshot ArenaSnapshotJson ArenaSnapshotRepository PGConfig
ArenaPerformance PrisonerMatchLogic PrisonerRecruitmentLogic PrisonerRecruitmentPresentation ArenaMedicalPolicy MedicSupplyPolicy
RosterStatusPolicy ArenaIngressPolicy FightDisengagePolicy FrameCadencePolicy ResultsLayout LeaderboardLayout RewardsLayout PGLog
CombatBalanceLog
TownMatchmakingPolicy TownChallengePolicy TownMedicCoordinator TownAnnouncerPolicy TownMatchmakingSearch TownDiagnosticRun
TownLimbShopPolicy TownBettingPolicy TownArenaRuntimePolicy TownArenaPolicy TownMedicAIPolicy TownFighterCatalog TownDiagnosticData
TownAftercarePolicy TownChallengeBuyInPolicy TownCorpseCleanupPolicy TownSpectatorPolicy TownRosterSelection TownCardLayout
TownFurniturePolicy
""".split()
# PA §6.2 (engine glue, non-UI) and TW §6.2.
GLUE = """
CombatHooks FightStarter SparSession KOWatcher SparStats ArenaIngress ArenaIdentity ArenaMedical RosterStatus ArenaCombatProfile
PrisonerUtil PrisonerUtilPlatoon PrisonerRecruitment RewardDelivery LeaderboardStore WorldLifecycle ArenaNativePersistence
FighterIdentity ArenaSnapshotFiles ArenaNativePath ContextMenuHooks SquadUtil
TownArena TownAftercare TownLimbShop TownAnnouncer TownSpectators TownBookie TownCorpseCleanup TownMatchmakingRuntime
TownChallengeWallet
""".split()
# PA §6.2 "UI screens (MyGUI)" and TW §6.3.
UI = """
ArenaUI RewardsUI PGSettingsUI LeaderboardUI ResultsUI DebugMenu PrisonerRecruitmentUI MarksHUD NativeUI
TownArenaUI TownBookieUI TownBookieResultUI
""".split()
# PA §6.2 "Bootstrap".
BOOTSTRAP = ["Plugin", "MsvcTr1Compat"]

# Files whose kind the research does not give (or gives differently for the .h and the .cpp), decided from the content.
DECIDED = {
    "PrisonerUtilInternal": ("glue", "Not in PA §1/§6; an 11-line header declaring CollectPlayerOwnedCageBuildings / "
                                     "IsBuildingOwnedByPlayer over Building*, implemented in PrisonerUtilPlatoon.cpp with kenshi/* "
                                     "includes"),
    "ArenaRingGuard": ("glue", "PA §6.1 lists ArenaRingGuard.h as pure (inline ring arithmetic ClassifyRing, RingReturnPoint, "
                               "RingCorrectionDue) and §6.2 lists ArenaRingGuard.cpp as glue; the shared row is glue "
                               "(src/Game/ArenaRingGuard.cs, M3 task 3.4) and the header's arithmetic goes to Core in M1 task "
                               "1.5, recorded as `partial`"),
}
# Kinds taken from the research that the source makes worth a remark (the kind stays the research's).
REMARKS = {
    "ArenaIngress": "glue (src/Game/ArenaIngress.cs, task 4.3b; its status is in its table row); its pure formation maths (the :46-110 constants and "
                    "marker tables, ScrambleOffset, TeamFormationOffset, AssignArenaTargets, AssignBannerTargets, Begin's staging "
                    "scale, the walk-in target, the escort gather points, the Registry use-node fallback, FaceOpposingFormation's "
                    "pick) is lifted into Core/ArenaFormation.cs (+ Core/Vec3.cs), M4 task 4.3a, golden formation.json; there is "
                    "no ArenaFormation original file",
    "CombatBalanceLog": "pure per PA §6.1, but the .cpp includes kenshi/Character.h and kenshi/GameWorld.h; M1 task 1.12 ports "
                        "it with an injected sink",
    "TownMatchmakingSearch": "pure per TW §6.1 (Win32 worker thread, no kenshi/* includes); the queue and generation rule are "
                             "Core/TownMatchmakingSearch.cs (task 1.10), the thread, its events and lock Core/Glue/TownMatchmakingWorker.cs "
                             "+ src/Town/TownMatchmakingSearch.cs (the world's worker, task 7.2)",
    "PGLog": "pure per PA §6.1; PGLog.h includes KenshiLib's <Debug.h>",
}

DROPPED = {
    "MsvcTr1Compat": "dropped: VS2010/KenshiLib build shim, nothing to port to C#",
    "Plugin": "dropped: RE_Kenshi start-up (startPlugin), replaced by src/ProvingGroundsMod.cs",
    "ArenaSnapshotFiles": "dropped: sidecar file IO (atomic replace, backups); Meitou saves mod data in the save",
    "ArenaNativePath": "dropped: sidecar path encoding; Meitou saves mod data in the save",
    "NativeSaveEvidence": "dropped: proof the native save finished before the sidecar is written; Meitou saves mod data in the save",
    "NativeSyncBoundary": "dropped: sidecar and native-save sync boundary; Meitou saves mod data in the save",
}

STATUS_RE = re.compile(r"^(todo|ported|partial: .+|dropped: .+)$")
HEADER = "| C++ file | lines | kind | C# file | status |"


def kind_of(base):
    kinds = [k for k, names in (("pure", PURE), ("glue", GLUE), ("ui", UI), ("bootstrap", BOOTSTRAP)) if base in names]
    if base in DECIDED:
        kinds.append(DECIDED[base][0])
    if len(kinds) != 1:
        sys.exit(f"port_map: {base} has {len(kinds)} kinds ({kinds}); classify it once in PURE/GLUE/UI/BOOTSTRAP or DECIDED")
    return kinds[0]


def csharp_of(base, kind):
    if base in DROPPED:
        return "-"
    if kind == "pure":
        return f"Core/{base}.cs"
    if kind == "glue":
        return f"src/Town/{base}.cs" if base.startswith("Town") else f"src/Game/{base}.cs"
    if kind == "ui":
        return f"src/Ui/{base}.cs"
    if kind == "test":
        return f"Tests/Ported/{base}.cs"
    return "-"


def line_count(path):
    return path.read_bytes().count(b"\n")


def ext_order(name):
    return (0 if name.endswith(".h") else 1, name)


def collect():
    groups = {}  # (section, base) -> [relative names]
    for p in sorted(SRC.rglob("*")):
        if p.is_file():
            rel = p.relative_to(SRC).as_posix()
            groups.setdefault((0, Path(rel).stem), []).append((rel, p))
    for p in sorted(TESTS.rglob("*")):
        if p.is_file():
            groups.setdefault((1, p.stem), []).append(("tests/" + p.relative_to(TESTS).as_posix(), p))
    rows = []
    for (section, base), files in groups.items():
        files.sort(key=lambda f: ext_order(f[0]))
        kind = "test" if section == 1 else kind_of(base)
        rows.append({"section": section, "base": base, "files": [f[0] for f in files],
                     "lines": [line_count(f[1]) for f in files], "kind": kind, "csharp": csharp_of(base, kind)})
    rows.sort(key=lambda r: (r["section"], r["base"].lower(), r["base"]))
    known = set(PURE) | set(GLUE) | set(UI) | set(BOOTSTRAP) | set(DECIDED) | set(REMARKS) | set(DROPPED)
    stale = sorted(known - {r["base"] for r in rows if r["section"] == 0})
    if stale:
        sys.exit(f"port_map: classified names with no upstream file: {', '.join(stale)}")
    return rows


def existing_statuses():
    """C++ file name -> status, from the current docs/port-map.md (empty when there is none)."""
    by_file = {}
    if not OUT.is_file():
        return by_file
    in_table = False
    for line in OUT.read_text(encoding="utf-8").splitlines():
        if line.strip() == HEADER:
            in_table = True
            continue
        if not in_table:
            continue
        if not line.startswith("|"):
            in_table = False
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) != 5 or set(cells[0]) <= set("-: "):
            continue
        status = cells[4]
        if not STATUS_RE.match(status):
            sys.exit(f"port_map: bad status {status!r} in docs/port-map.md for {cells[0]}")
        for name in cells[0].split(","):
            by_file[name.strip()] = status
    return by_file


def render(rows, old):
    for r in rows:
        kept = [old[f] for f in r["files"] if f in old]
        r["status"] = kept[0] if kept else DROPPED.get(r["base"], "todo")
    src_files = sum(len(r["files"]) for r in rows if r["section"] == 0)
    test_files = sum(len(r["files"]) for r in rows if r["section"] == 1)
    kinds = {}
    for r in rows:
        kinds[r["kind"]] = kinds.get(r["kind"], 0) + 1
    statuses = {}
    for r in rows:
        key = r["status"].split(":")[0]
        statuses[key] = statuses.get(key, 0) + 1
    order = ["pure", "glue", "ui", "bootstrap", "test"]
    out = [
        "# Port map",
        "",
        "Generated by `python3 tools/port_map.py` from `upstream/ProvingGrounds/` (commit 4a20d48). Re-running keeps each row's "
        "status; edit the status cell by hand (or from a task) and re-run.",
        "",
        "- Paths: plugin files relative to `upstream/ProvingGrounds/plugin/src/`; test files as `tests/<name>` "
        "(`upstream/ProvingGrounds/tests/`). Lines are `wc -l` counts, in the same order as the files.",
        "- Kinds (PA = pg-source-player-arena.md §1/§6, TW = pg-source-town.md §1/§6): `pure` -> `Core/<Base>.cs`, "
        "`glue` -> `src/Game/<Base>.cs` (`src/Town/<Base>.cs` for Town files), `ui` -> `src/Ui/<Base>.cs`, "
        "`test` -> `Tests/Ported/<Base>.cs`, `bootstrap` -> none.",
        "- Status: `todo`, `ported`, `partial: <what>`, `dropped: <why>`. `ported` rows must name an existing C# file "
        "(test `PortMapTests`).",
        "",
        f"Rows: {len(rows)} ({', '.join(f'{k} {kinds[k]}' for k in order if k in kinds)}). "
        f"Files: {src_files} in plugin/src, {test_files} in tests. "
        f"Status: {', '.join(f'{k} {statuses[k]}' for k in sorted(statuses))}.",
        "",
        HEADER,
        "|---|---|---|---|---|",
    ]
    for r in rows:
        out.append(f"| {', '.join(r['files'])} | {', '.join(str(n) for n in r['lines'])} | {r['kind']} | {r['csharp']} | "
                   f"{r['status']} |")
    out += ["", "## Kinds decided from the source", ""]
    for base in sorted(DECIDED):
        out.append(f"- {base}: {DECIDED[base][0]}. {DECIDED[base][1]}.")
    out += ["", "## Remarks on research kinds", ""]
    for base in sorted(REMARKS):
        out.append(f"- {base}: {REMARKS[base]}.")
    return "\n".join(out) + "\n"


def main():
    check = "--check" in sys.argv[1:]
    text = render(collect(), existing_statuses())
    if check:
        current = OUT.read_text(encoding="utf-8") if OUT.is_file() else ""
        if current != text:
            sys.exit("docs/port-map.md is out of date: run python3 tools/port_map.py")
        print("docs/port-map.md is up to date")
        return
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding="utf-8", newline="\n")
    print(next(line for line in text.splitlines() if line.startswith("Rows: ")))
    print(f"wrote {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
