#!/bin/bash
# Builds each golden generator from a COPY of the upstream pure files plus golden/stubs, runs it, writes Tests/Golden/<name>.json.
# Usage: bash golden/gen.sh [name ...]     (no argument: every golden/gen_*.cpp). See golden/README.md.
# Nothing is compiled or run inside upstream/; it is only read. golden/build/ is scratch (gitignored, wiped on every run).
set -euo pipefail
R=$(cd "$(dirname "$0")/.." && pwd); B="$R/golden/build"; O="$R/Tests/Golden"
CXX=${CXX:-g++}
FLAGS="-std=c++17 -O0 -ffp-contract=off -Wno-deprecated-declarations -include msvc_compat.h"   # no fused multiply-add: MSVC x64 does not fuse either
chmod -R u+w "$B" 2>/dev/null || true; rm -rf "$B"; mkdir -p "$B" "$O"
# upstream/ is read-only (task 0.3): copy without its mode so the stubs below may overwrite same-named headers
cp --no-preserve=mode "$R"/upstream/ProvingGrounds/plugin/src/*.h "$R"/upstream/ProvingGrounds/plugin/src/*.cpp "$B"/
cp --no-preserve=mode "$R"/golden/stubs/* "$B"/            # msvc_compat.h (required), pgconfig_stub.{h,cpp} and any other stub

# the hand-written stub must link against the real PGConfig.h and behave like the original before any generator relies on it
cp "$R/golden/check_pgconfig_stub.cpp" "$B/"
# two statements: under set -e a failing command before && does not stop the script
( cd "$B" && $CXX $FLAGS -o check_pgconfig_stub check_pgconfig_stub.cpp pgconfig_stub.cpp )
"$B/check_pgconfig_stub"

if [ $# -gt 0 ]; then names=("$@"); else names=(); for g in "$R"/golden/gen_*.cpp; do n=$(basename "$g" .cpp); names+=("${n#gen_}"); done; fi
for name in "${names[@]}"; do
  gen="$R/golden/gen_$name.cpp"; [ -f "$gen" ] || { echo "golden: no generator golden/gen_$name.cpp" >&2; exit 1; }
  cp "$gen" "$B/"
  srcs=$(sed -n 's#^// sources: ##p' "$gen")              # each generator lists the upstream/stub .cpp files it needs
  # "// needs: rapidjson": the codec includes ../third_party/rapidjson/..., i.e. golden/third_party/ (gitignored, pinned download)
  if grep -q '^// needs: rapidjson' "$gen"; then bash "$R/golden/fetch_rapidjson.sh"; fi
  # "// BEGIN COPY File:a-b" blocks are verbatim copies of upstream lines: stop when one has drifted or a marker is malformed
  if grep -qE '^// (BEGIN COPY|END COPY|copies:)' "$gen"; then python3 "$R/golden/check_copies.py" "$gen"; fi
  ( cd "$B" && $CXX $FLAGS -o "gen_$name" "gen_$name.cpp" $srcs )
  "$B/gen_$name" > "$B/$name.json"                          # written aside, so a failing generator never truncates the committed file
  mv "$B/$name.json" "$O/$name.json"
  echo "golden: $name -> $(wc -c < "$O/$name.json") bytes"
done
