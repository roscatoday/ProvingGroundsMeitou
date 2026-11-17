#!/bin/bash
# Downloads Tencent/rapidjson (MIT) into golden/third_party/rapidjson/ (gitignored) for the snapshot-codec golden (gen_snapshot.cpp).
# ArenaSnapshotJson.cpp includes "../third_party/rapidjson/include/rapidjson/..."; compiled in golden/build/, that path resolves to
# golden/third_party/rapidjson/. Headers only: nothing is built inside the download folder. The original's rapidjson version is
# unknown (the upstream source release has no third_party folder); the goldens use the v1.1.0 release, pinned by commit sha.
# Usage: bash golden/fetch_rapidjson.sh   (does nothing when the pinned commit is already there)
# The tag must still point at the pinned sha: that is deliberate. If upstream ever re-tags v1.1.0, the script refuses rather than
# build goldens from different code; update SHA (and regenerate, review the diff) only on purpose.
set -euo pipefail
R=$(cd "$(dirname "$0")/.." && pwd); D="$R/golden/third_party/rapidjson"
URL=https://github.com/Tencent/rapidjson
TAG=v1.1.0
SHA=f54b0e47a08782a6131cc3d60f94d038fa6e0a51        # git ls-remote https://github.com/Tencent/rapidjson v1.1.0 (2026-11-11)
if [ -d "$D/.git" ] && [ "$(git -C "$D" rev-parse HEAD)" = "$SHA" ]; then echo "rapidjson: $TAG $SHA already present"; exit 0; fi
remote=$(git ls-remote "$URL" "refs/tags/$TAG" | cut -f1) || remote=""
[ -n "$remote" ] || { echo "rapidjson: could not reach $URL to check tag $TAG (offline?)" >&2; exit 1; }
[ "$remote" = "$SHA" ] || { echo "rapidjson: tag $TAG now points at '$remote', expected $SHA" >&2; exit 1; }
rm -rf "$D"; mkdir -p "$D"
git -C "$D" init -q
git -C "$D" fetch -q --depth 1 "$URL" "$SHA"
git -C "$D" checkout -q FETCH_HEAD
got=$(git -C "$D" rev-parse HEAD)
[ "$got" = "$SHA" ] || { echo "rapidjson: downloaded $got, expected $SHA" >&2; rm -rf "$D"; exit 1; }
echo "rapidjson: $TAG $SHA -> $D"
