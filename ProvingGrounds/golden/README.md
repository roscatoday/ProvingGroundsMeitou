# golden/: vectors made by the original C++ (gate G3)

`bash golden/gen.sh` compiles the original's pure files with g++ and writes `Tests/Golden/<name>.json`. The C# port must reproduce them
(ints, enums, strings exactly; floats within 1e-4, `GoldenFile.Near`). The JSON is committed: it is the reference.

    bash golden/gen.sh          # every golden/gen_*.cpp
    bash golden/gen.sh mmr      # just gen_mmr.cpp

Output is deterministic (fixed-seed LCG, `-ffp-contract=off`): running it twice gives byte-identical files.

## How it works

1. `golden/build/` is **scratch** (gitignored, wiped on every run). `gen.sh` copies `upstream/ProvingGrounds/plugin/src/*.h|*.cpp` into
   it, then `golden/stubs/*` over them. Nothing is compiled or run inside `upstream/`, and no upstream file is ever edited.
2. `stubs/msvc_compat.h` is force-included (`-include`): it maps the MSVC-only CRT calls (`sprintf_s`, `strncpy_s`, `_stricmp`, ...)
   onto standard C. `stubs/pgconfig_stub.{h,cpp}` replaces `PGConfig.cpp` (needs Windows.h, rapidjson, PGLog -> KenshiLib); the REAL
   `PGConfig.h` is used. The stub file's header comment lists which `PGConfig::` functions it implements, with the `PGConfig.cpp`
   line each shipped value comes from, and which are not needed yet.
3. `check_pgconfig_stub.cpp` runs first: it proves the stub links against the real header and gives the shipped values and the
   marks-multiplier rounding at 0.5 / 1 / 2.5. A stub change that breaks it stops the run.
4. For each `golden/gen_<name>.cpp` the script reads its `// sources:` line (the upstream or stub `.cpp` files to link), compiles
   `gen_<name>` in `golden/build/` and writes its stdout to `Tests/Golden/<name>.json`.

## Adding a generator (the pattern M1 follows; `gen_mmr.cpp` is the worked example)

1. Create `golden/gen_<name>.cpp`. First line: `// sources: A.cpp B.cpp [pgconfig_stub.cpp]`, the `.cpp` files it links (headers come
   along by `#include`). Add `pgconfig_stub.cpp` when any linked file calls `PGConfig::`; grep `PGConfig::` in the file to know.
2. Fixed seed, own LCG (`s = s * 1664525u + 1013904223u`), never `rand()` or `<random>` (not stable across standard libraries). Do not
   call `R()` twice in one expression (evaluation order is unspecified).
3. Print one JSON array, one case per line, **inputs and outputs** of every case, floats with `%.9g`, ints with `%d`, enums as ints.
   Cover every branch the inventory lists (edge values: zero, negative, ties, empty lists, max counts).
4. `bash golden/gen.sh <name>`, validate the JSON parses, commit `Tests/Golden/<name>.json`.
5. Write `Tests/Golden/<Name>GoldenTests.cs`: `GoldenFile.Load("<name>")`, rebuild the inputs, call the C#, compare.

## Knobs and traps

- `BalanceTuning::Get()` (the real `BalanceTuning.cpp` is linked) returns a mutable struct: a generator may set a lever to cover its other
  branch. `mmr.json` was made with the shipped values only (`zeroSumRating` true); the `zeroSumRating == false` path is not in it.
- The PGConfig stub values are mutable: `PGConfigStub_SetMarksMultiplier(0.5 | 1 | 2.5)`, `PGConfigStub_Challenges()`,
  `PGConfigStub_Bookie()`, `PGConfigStub_Recruitment()`, `PGConfigStub_SetChallengeBase(div, cats)`, `PGConfigStub_Reset()`
  (`#include "pgconfig_stub.h"`).
- `ArenaRewards::ApplyPriceOverrides()` stamps prices **in place** (it passes the current cost in as the default), so call it once per
  process after setting the multiplier; run one process per multiplier (or print the cases for one multiplier per file).
  `gen_rewards.cpp` does this inside one binary: `main()` forks one child per multiplier (0.5, 1, 2.5), each stamps once and
  prints its lines, the parent waits for each in turn, so the output is one ordered array and `gen.sh` needs no arguments.
- A header-only file (no `.cpp`) needs nothing linked: write the line as `// sources: ` with nothing after it (`gen_rewardprog.cpp`).
- A file that includes `kenshi/*.h`, `PGLog.h` (KenshiLib's `Debug.h`) or `<Windows.h>` cannot be linked here. rapidjson can:
  a generator with the line `// needs: rapidjson` makes `gen.sh` run `golden/fetch_rapidjson.sh` first, which downloads Tencent/rapidjson
  v1.1.0 (pinned by commit sha, checked after download) into `golden/third_party/rapidjson/` (gitignored). That is where the upstream
  `#include "../third_party/rapidjson/..."` lands from `golden/build/`; it is headers only, nothing is compiled inside it.
  `gen_snapshot.cpp` compiles the REAL ArenaSnapshotJson.cpp and ArenaSnapshotRepository.cpp this way (the original's rapidjson
  version is unknown, see docs/divergences.md). Inputs that are undefined behaviour in rapidjson 1.1.0 must stay out of a golden:
  check a new generator with `-fsanitize=address,undefined` and with `-ftrivial-auto-var-init=zero` / `=pattern` (identical output).
- The files that cannot be linked are glue or the config reader (PGConfig.cpp still needs Windows.h and PGLog, so it stays a stub);
  they are tested by hand-written cases, not golden vectors.
  Exception: `gen_pgconfig_text.cpp` pins PGConfig's TEXT output (default file, settings overlay, `%g` numbers, load summary) by
  compiling VERBATIM copies of PGConfig.cpp's text builders, linked with the real ArenaRewards.cpp; the reader itself stays hand-tested.
- Verbatim copies of glue code (`gen_formation.cpp`, task M4.3a: ArenaIngress.cpp's formation maths) sit between
  `// BEGIN COPY <File>:<first>-<last>` and `// END COPY` lines at column 0, unedited, and a `// copies: <n>` line states their exact
  number (malformed markers, stray ENDs, bad ranges and a wrong count are errors); the engine types and calls they use are
  stand-ins declared outside the copies under the original's names (`stubs/ogre_formation_stub.h` = the Ogre 1.9 Vector3 /
  Quaternion operations, bodies from OGRE v1-9-0). `check_copies.py` runs in `gen.sh` for every generator with such a marker and
  fails when a copy no longer equals upstream line for line; `Tests/Fidelity/GoldenCopiesTests.cs` runs the same check in the suite.
