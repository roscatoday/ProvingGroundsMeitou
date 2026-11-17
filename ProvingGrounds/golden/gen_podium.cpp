// sources: SparPodium.cpp MatchRules.cpp
// Random snapshots -> SparPodium::BuildPodium (header, podium places and ids) and ComputeMvpScore per fighter, as JSON lines.
// "coarse" cases take values from tiny sets so score / dealt / defense / hits / name ties all occur. Snapshot fields not printed
// stay zero. Fighter ids are unique within a snapshot (a podium entry is identified by id); eliminationIds may repeat, name absent
// ids, or name the last-standing id. Outcome 5 is outside the named enumerators: BuildPodium leaves an empty header and no podium.
#include "SparPodium.h"
#include <cstdio>
#include <cstring>
using namespace SparPodium;
static unsigned s = 777u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static float F(float lo, float hi) { return lo + (hi - lo) * (R() % 100000) / 100000.0f; }
static const char* kNames[] = {"A", "B", "Beep", "beep", "", "Ab", "a", "Zed", "Ruka", "Ruka ",
  "LongNameLongNameLongNameLongNameLongNameLongNameLongNameLongNam"};   // the last is 63 chars
static void Json(const char* t) {
  std::putchar('"');
  for (; *t; ++t) { if (*t == '"' || *t == '\\') std::putchar('\\'); std::putchar(*t); }
  std::putchar('"');
}
int main() {
  std::printf("[\n");
  const int counts[] = {0, 1, 2, 3, 16};
  for (int c = 0; c < 1500; ++c) {
    Snapshot snap = {};
    snap.fighterCount = (R() % 3) == 0 ? counts[R() % 5] : (int)(R() % 17);
    int mode = R() % 3; snap.mode = (MatchRules::MatchMode)mode;
    bool coarse = (R() % 3) != 0;
    for (int i = 0; i < snap.fighterCount; ++i) {
      FighterRow& r = snap.fighters[i];
      r.id = 100 + i * 3 + (int)(R() % 3);
      r.team = (MatchRules::MatchTeam)(R() % 3);
      std::strncpy(r.name, kNames[R() % 11], 63); r.name[63] = '\0';
      if (coarse) {
        r.damageDealt = (float)(R() % 3) * 10.0f; r.damageTaken = (float)(R() % 3) * 10.0f;
        r.damageMitigated = (float)(R() % 3) * 4.0f; r.hitsLanded = R() % 3; r.blocks = R() % 2; r.misses = R() % 3;
        r.dodges = R() % 2;
      } else {
        r.damageDealt = F(0, 400); r.damageTaken = F(0, 400); r.damageMitigated = F(-20, 200); r.hitsLanded = R() % 30;
        r.blocks = R() % 20; r.misses = R() % 20; r.dodges = R() % 10;
      }
      int survived = R() % 2;
      int idx = (int)(R() % 8);
      int shift = (R() % 6) == 0 ? 3 : 0;
      r.eliminationIndex = survived ? -1 : idx - shift;
    }
    int oc = R() % 6; snap.outcome = (OutcomeKind)oc;
    if (snap.fighterCount > 0 && (R() % 5) != 0) snap.lastStandingId = snap.fighters[R() % snap.fighterCount].id;
    else snap.lastStandingId = 999;
    snap.eliminationCount = R() % 17;
    for (int i = 0; i < snap.eliminationCount; ++i) {
      int pick = R() % 5;
      if (pick == 0) snap.eliminationIds[i] = 999;
      else if (pick == 1) snap.eliminationIds[i] = snap.lastStandingId;
      else snap.eliminationIds[i] = snap.fighterCount > 0 ? snap.fighters[R() % snap.fighterCount].id : 998;
    }
    BuildPodium(snap);
    std::printf("%s{\"mode\":%d,\"outcome\":%d,\"last\":%d,\"elim\":[", c ? "," : "", mode, oc, snap.lastStandingId);
    for (int i = 0; i < snap.eliminationCount; ++i) std::printf("%s%d", i ? "," : "", snap.eliminationIds[i]);
    std::printf("],\"f\":[");
    for (int i = 0; i < snap.fighterCount; ++i) {
      const FighterRow& r = snap.fighters[i];
      std::printf("%s{\"id\":%d,\"team\":%d,\"name\":", i ? "," : "", r.id, (int)r.team); Json(r.name);
      std::printf(",\"dealt\":%.9g,\"taken\":%.9g,\"mit\":%.9g,\"hits\":%d,\"blocks\":%d,\"misses\":%d,\"dodges\":%d,\"elimIdx\":%d,"
                  "\"mvp\":%.9g}", r.damageDealt, r.damageTaken, r.damageMitigated, r.hitsLanded, r.blocks, r.misses, r.dodges,
                  r.eliminationIndex, ComputeMvpScore(r));
    }
    std::printf("],\"header\":"); Json(snap.header);
    std::printf(",\"podium\":[");
    for (int i = 0; i < snap.podiumCount; ++i)
      std::printf("%s[%d,%d]", i ? "," : "", snap.podium[i].place, snap.podium[i].fighter.id);
    std::printf("]}\n");
  }
  std::printf("]\n");
}
