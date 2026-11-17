// sources: MmrRating.cpp SparPodium.cpp MatchRules.cpp BalanceTuning.cpp
// Random snapshots -> MmrRating::ApplyMatch outputs, as JSON lines in one array.
// Snapshot fields not printed (eliminationCount, eliminationIds, podium, header) stay zero: the C# test must build its snapshot the same way.
#include "MmrRating.h"
#include <cstdio>
#include <cstring>
static unsigned s = 12345u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static float F(float lo, float hi) { return lo + (hi - lo) * (R() % 100000) / 100000.0f; }
int main() {
  std::printf("[\n");
  for (int c = 0; c < 2000; ++c) {
    SparPodium::Snapshot snap = {};
    snap.fighterCount = 2 + R() % 7;
    int mode = R() % 3; snap.mode = (MatchRules::MatchMode)mode;
    for (int i = 0; i < snap.fighterCount; ++i) {
      SparPodium::FighterRow& r = snap.fighters[i]; r.id = i + 1;
      r.team = mode == MatchRules::ModeLastStanding ? MatchRules::TeamNone : (i % 2 ? MatchRules::TeamB : MatchRules::TeamA);
      r.damageDealt = F(0, 400); r.damageTaken = F(0, 400); r.damageMitigated = F(0, 200); r.hitsLanded = R() % 30;
      r.blocks = R() % 20; r.misses = R() % 20; r.dodges = R() % 10; r.eliminationIndex = (R() % 2) ? -1 : (int)(R() % 8);
      std::snprintf(r.name, sizeof r.name, "F%d", i);
    }
    int oc = R() % 5; snap.outcome = (SparPodium::OutcomeKind)oc; snap.lastStandingId = 1 + R() % snap.fighterCount;
    float before[16]; int matches[16]; bool mask[16]; float after[16]; int wins[16], losses[16];
    for (int i = 0; i < snap.fighterCount; ++i) { before[i] = F(0, 400); matches[i] = R() % 40; mask[i] = (R() % 5) != 0; }
    MmrRating::ApplyMatch(snap, before, matches, mask, after, wins, losses);
    std::printf("%s{\"mode\":%d,\"outcome\":%d,\"last\":%d,\"f\":[", c ? "," : "", mode, oc, snap.lastStandingId);
    for (int i = 0; i < snap.fighterCount; ++i) {
      const SparPodium::FighterRow& r = snap.fighters[i];
      std::printf("%s{\"team\":%d,\"dealt\":%.9g,\"taken\":%.9g,\"mit\":%.9g,\"hits\":%d,\"blocks\":%d,\"misses\":%d,\"dodges\":%d,\"elim\":%d,"
                  "\"before\":%.9g,\"matches\":%d,\"mask\":%d,\"after\":%.9g,\"win\":%d,\"loss\":%d}",
                  i ? "," : "", (int)r.team, r.damageDealt, r.damageTaken, r.damageMitigated, r.hitsLanded, r.blocks, r.misses,
                  r.dodges, r.eliminationIndex, before[i], matches[i], (int)mask[i], after[i], wins[i], losses[i]);
    }
    std::printf("]}\n");
  }
  std::printf("]\n");
}
