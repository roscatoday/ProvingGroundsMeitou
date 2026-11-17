# Meitou versions (task 9.3d, 2026-11-17)

Which Meitou games Proving Grounds for Meitou runs on, how that was checked, and what differs between them. Decision D1 says "Meitou
0.17.3 and later" (meitou.json `"gameVersion": ">=0.17.3"`). Every gate M1-M8 ran on 0.17.3; this task checked the newer games the owner
has (installed under `%LOCALAPPDATA%\Meitou\versions`, plus the official 0.19.0 / 0.20.0 zips in `MeitouMods/_hosts`).

## How one build runs on every version

- One package for every game: the two DLLs are built against 0.17.3, the oldest supported game (`Directory.Build.props`
  `MeitouModernDir`), target net10.0, with the game assemblies referenced `Private=false` (the game supplies them). .NET loads a reference
  whose assembly version is HIGHER than the one compiled in (it refuses only an older one), so the 0.17.3 build binds to 0.20's
  `Meitou.*.dll` / `MeitouClient.dll`; the 0.20 games run on .NET 11 (`MeitouClient.runtimeconfig.json` tfm net11.0) and load a net10.0
  DLL as they load every other Workshop code mod. This is the shipping model: no per-version build, no 0.20 build.
- What can break is a member: a game member renamed, removed or given another signature fails only when a method that uses it is
  JIT-compiled (MissingMethodException / MissingFieldException), and a by-name lookup (`src/Engine/Members.cs`) silently finds nothing.
  An added OPTIONAL parameter is such a break too (the compiled call names the old signature). So every version is checked three ways:
  - **RefCheck** (`../_dual/RefCheck`, as `tools/package.py` calls it): every game member both DLLs reference, read from the metadata of
    the game folder.
  - **PgCheck** `versions` (metadata of every installed game >= 0.17.3, the members the adapters call), and `hooks`, `engine`, `ui` run ON the
    newer game: `../_dual/swapcheck.py` copies PgCheck's 0.17.3 build, puts the game's DLLs over its copies and runs it on .NET 11 (a
    check cannot be compiled against the net11 games). hooks resolves every `Members.cs` field and applies every Harmony patch for real;
    engine JIT-compiles every adapter method; ui builds, arranges and rebinds every window's layouts. (Run here through a wrapper that adds
    three ProvingGrounds rows to swapcheck's CHECKS list without editing the shared tool: `("PG-<verb>", "ProvingGrounds/Check",
    "ProvingGroundsMeitou", ["ProvingGroundsMeitou", "ProvingGrounds.Core"], ["<verb>"])` with `--dist ProvingGroundsMeitou`.)
  - **Live runs** on 0.20.1: `GAME=<0.20.1 folder> bash tools/live.sh <scenario>` (live.sh needs no other step for another version: it
    launches that folder's MeitouClient.exe with the same installed package).

## Results (build 33fd64a; the review fixes re-checked hooks and engine on 0.17.3, 0.20.1 and 0.20.2)

| Game (commit) | RefCheck Core / Meitou DLL | PgCheck versions | hooks (swap) | engine (swap) | ui (swap) | Live |
|---|---|---|---|---|---|---|
| 0.17.3 (fa34a1b) | OK / OK | OK | 333 rows OK (built against it) | 31 rows OK (30 before the review row) | 2574 PASS | every gate M1-M8 |
| 0.17.4-nightly.20261111 | OK / OK | OK | - | - | - | - |
| 0.17.5-nightly.20261112.2 | OK / OK | OK | - | - | - | - |
| 0.18.0 | OK / OK | OK | - | - | - | - |
| 0.18.1-nightly.20261113 | OK / OK | OK | - | - | - | - |
| 0.19.0 (_hosts zip) | OK / OK | (not installed) | - | - | - | - |
| 0.20.0 (_hosts zip) | OK / OK | (not installed) | 333 OK | 30 OK | - | - |
| 0.20.1-nightly.20261115.3 (8d92723) | OK / OK | OK | 333 OK | 31 OK | 2574 PASS | smoke, prisoner-recruit, rewards, town-ambient PASS |
| 0.20.2-nightly.20261116 (42fc341) | OK / OK (after the fix below) | OK (after the fix) | 333 OK | 31 OK (after the fix; the 5-parameter binding row OK) | 12 of 2574 FAIL (1 px, below) | not run |
| 0.20.2-nightly.20261117.2 (0.2.0 build, 2026-11-17) | OK / OK | OK (after the two fixes below) | 333 OK (after the fix) | 31 OK | not run | interop scenario (docs/PROGRESS.md, 0.2.0) |

"-" = not run (RefCheck and PgCheck versions cover every member the DLLs name; the swap checks were run on the 0.20 line).

## What moved or vanished, and how the port handles it

| Member | 0.17.3 | Where it changed | Resolution |
|---|---|---|---|
| `FixtureRegistry.Release` | `Release(World, CharacterId, int releaserFaction = -1, CrimeSystem? crimes = null)` (src/Meitou.Simulation/Locks/FixtureRegistry.cs:1751; same at 0.20.1, :1869) | 0.20.2-nightly.20261116: `Release(..., CrimeSystem? crimes = null, bool jailer = false)` (FixtureRegistry.cs:1875). RefCheck FAILED and PgCheck engine could not JIT-bind Cages.Release / PutInCage / Imprison before the fix | Bound by reflection: `Members.FixturesRelease` (part CageRelease) takes whichever signature exists; `Cages.Release` calls it with releaser -1, no crime system and, on 0.20.2, `jailer: false` (the mod's releases are no jailer's task). PutInCage, the test verb pg imprison and the spike pg uncage go through it. PgCheck versions lists both signatures, and PgCheck engine binds the delegate for the game's own signature (row "Cages binds FixtureRegistry.Release": 4 parameters on 0.17.3 / 0.20.1, 5 on 0.20.2). Without either the part is off (one log line): the Arena window refuses any match with a prisoner ("Prisoner matches unavailable: this game version cannot release prisoners from cages", docs/divergences.md "Prisoner matches without a cage release"), a recruit is refused with the prisoner left in its cage, a cage return of a prisoner held in another cage fails |
| `Inventory.Add` | `Add(ItemInstance)` (Inventory.cs:146) | 0.20.0: `Add(ItemInstance, bool mainShut = false)` | Already bound by reflection since task 5.2a (`Members.InventoryAdd`, called with `mainShut: false`); live rewards on 0.20.1 PASS |
| `CrimeSystem.SetPending` (the town rules' crime prefix, task 7.4) | `SetPending(World, int culprit, CrimeCode, int victimFaction, CharacterId victim, float timer, bool viaWrapper = false)` | 0.20.2-nightly.20261117.2: two more optional parameters, `string? victimHolder = null, bool wasUp = false` (CrimeSystem.cs:121). PgCheck hooks (swap) found the patch target missing: without it a town bout between the squad and Scratch's fighters would be charged as assault by the game | `Members.CrimeSetPending` takes whichever signature exists (the 7-parameter one first); the prefix reads only world, culprit, code and victim by name, so it is the same prefix on both. PgCheck versions lists both |
| `DialogueSystem.PlayerHeads(World)` (the recruit's faction cap, task 6.4a) | internal static, DialogueSystem.Recruit.cs:48-55 | 0.20.2-nightly.20261117.2: removed; the game's own Recruit and GiveSlaves read public `SquadManagement.CappedHeads(World)` (src/Meitou.Simulation/Factions/SquadManagement.cs) | `Members.PlayerHeads` takes the old one, else `CappedHeads` (the count the game's own cap check now uses: members of a squad a code mod keeps uncounted are left out). Without either the recruitment part switched itself off (one log line, Grant Freedom reads Recruitment Unavailable). PgCheck versions accepts either (a row may name an alternative in another class) |

No other member moved: RefCheck reports 0 missing for both DLLs on every version above once FixtureRegistry.Release is bound, and PgCheck
hooks on 0.20.0 / 0.20.1 / 0.20.2 resolves every `Members.cs` member (by-name, internal ones included) and applies every patch.

## Known differences between the versions

- **The release line for a player prisoner (0.20.2).** At 0.17.3-0.20.1 `FixtureRegistry.Release` posts "{1} has been released from
  prison." for a player-faction prisoner; 0.20.2 posts it only for a jailer's release (`jailer` true, and not for a slave). The mod passes
  `jailer: false`, so on 0.20.2 no such line follows a Proving Grounds release. The mod releases NPC prisoners (the contract release, the
  recruit, the cage return), which the line never named, so nothing visible changes in the mod's own flows. 0.20.2 also changed the escaped-flag
  rule for a releaser of a faction (FixtureRegistry.cs:1896-1899); with releaser -1, the mod's, it is the same as at 0.17.3.
- **Window rebinding pixels (0.20.2, cosmetic).** 0.20.2 rewrote Meitou's MyGUI arrange to follow MyGUI's own history of a widget
  (`MyGuiScale`, `MyGuiPresentation` in src/Meitou.Data/Ui/UiDocument.cs: a position becomes whole pixels when the widget is made and is
  aligned again only when its parent is sized). PgCheck ui's equivalence rows ("bound over model A's layout = model B registered fresh",
  0.01 px) then differ by 1 px in 12 of the Rewards window's model pairs (e.g. `PG_Reward_0_Name` x 701 vs 702, `PG_Licence_2_Buy` width
  199 vs 200): when the Rewards window rebinds a model of the same shape over the layout it already shows (a tab or a purchase that keeps
  the layout), a few labels and buttons can sit 1 px from where a freshly opened window puts them. Every other ui row passes on 0.20.2 and
  all 2574 pass on 0.20.1. Not changed: it is 1 px, only on 0.20.2, and the arrange rules are Meitou's (they follow Kenshi's MyGUI); a
  newer game may change them again. Not seen live (0.20.2 had no live run).
- **0.20.1 live**: smoke 35/0, prisoner-recruit 36/0, rewards 58/0, town-ambient 32/2 then 34/0 (the same counts as the M8 gate on
  0.17.3), only the two mods in the list; no exception, no part-off line. The town-ambient FAILs were both step (8): 30 s after the squad's
  move order west, one of the six player characters still counted as in Scratch, so the announced bout was not cancelled yet. The rerun
  (with a new diagnostic in tools/scenarios/town-ambient.sh that probes every squad member when the walk leaves someone in town) passed:
  0 in town, bout cancelled. Not reproduced, so the cause (a straggler of the game's group move, or a status-town update) is unknown;
  the diagnostic will name the character if it happens again.

## Recommendation for README and the Workshop page (task 9.4c writes them)

- "Requires Meitou 0.17.3 or later. Tested on Meitou 0.17.3 and 0.20.1." The manifest's `>=0.17.3` stays: every game from 0.17.3 to
  0.20.2 passes RefCheck with this build, and 0.20.2 also passes the swapped hooks / engine checks.
- Do not claim 0.20.2 as tested until a live run there (at least smoke, prisoner-recruit and rewards: the release path and the
  Rewards window are what changed). A newer nightly needs the three checks above again (RefCheck, PgCheck versions, the swapped hooks /
  engine / ui) before the wording changes; mod-check alone does not find a changed signature (it never reaches the call).
