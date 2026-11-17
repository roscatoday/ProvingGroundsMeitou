// sources: ArenaMarks.cpp SparPodium.cpp MatchRules.cpp pgconfig_stub.cpp
// ArenaMarks: every public function, as JSON lines in one array; each line has "k" (the function) plus its inputs and outputs.
//   "aw"  ComputeAwards then ApplyWinningTeamBonus on the same snapshot (cs = combat skill, null when absent; mm = matchMultiplier;
//         eligible + bonus = ApplyWinningTeamBonus inputs; m0 = marks after ComputeAwards, m1 = after the bonus)
//   "ad"  DifficultyMultiplier + ApplyDifficulty (halves: base * 0.5/1.5 clamp ends, and exact .5 points)
//   "cm"  Division/Rarity/ChallengeMultiplier over division -1..3, rarity -1..4
//   "ub"  UniqueChallengeBonus with the stub's unique/skarn bonus multipliers set (PGConfig.cpp:49 ships 1.0 / 1.0)
// ArenaMarks does not read the marks (price) multiplier (no ScaleMarks call), so it is not varied here.
// Snapshot fields not printed (names, stats, eliminations, header, podium entries past [0]) stay zero; podium[0].fighter.id is "mvp".
#include "ArenaMarks.h"
#include "pgconfig_stub.h"
#include <cstdio>
#include <cstring>
static unsigned s = 424242u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static float F(float lo, float hi) { return lo + (hi - lo) * (R() % 100000) / 100000.0f; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }
int main() {
  std::printf("[\n");
  // --- ComputeAwards + ApplyWinningTeamBonus
  const float mms[] = { 1.0f, 0.75f, 1.25f, 1.5f, 2.0f, 2.5f, 0.9375f, 0.0f, -0.5f, 0.1f };
  for (int c = 0; c < 3000; ++c) {
    SparPodium::Snapshot snap = {};
    snap.fighterCount = (c % 50 == 0) ? 0 : 1 + R() % 16;
    int mode = R() % 3; snap.mode = (MatchRules::MatchMode)mode;
    for (int i = 0; i < snap.fighterCount; ++i) {
      SparPodium::FighterRow& r = snap.fighters[i];
      r.id = 1 + (int)(R() % 20);   // ids may repeat: the MVP and last-standing tests compare ids
      int t = R() % 4;              // mostly A/B, sometimes TeamNone even in team modes
      r.team = mode == MatchRules::ModeLastStanding ? MatchRules::TeamNone
             : (t == 0 ? MatchRules::TeamNone : (t % 2 ? MatchRules::TeamA : MatchRules::TeamB));
    }
    int oc = R() % 5; snap.outcome = (SparPodium::OutcomeKind)oc;
    snap.lastStandingId = 1 + (int)(R() % 20);
    snap.podiumCount = R() % 4;
    int mvp = snap.podiumCount > 0 ? 1 + (int)(R() % 22) : 0;
    snap.podium[0].fighter.id = mvp;
    bool hasCs = (R() % 4) != 0;
    float cs[16];
    int equalCs = R() % 6 == 0;
    for (int i = 0; i < 16; ++i) cs[i] = equalCs ? 40.0f : F(0, 100);
    float mm = (R() % 3) ? mms[R() % 10] : F(0, 3);
    int m[16];
    for (int i = 0; i < 16; ++i) m[i] = -7;   // ComputeAwards must overwrite every [0, fighterCount)
    ArenaMarks::ComputeAwards(snap, hasCs ? cs : 0, m, mm);
    int m0[16]; std::memcpy(m0, m, sizeof m);
    bool el[16]; for (int i = 0; i < 16; ++i) el[i] = (R() % 4) != 0;
    int bonusPick = R() % 5;
    int bonus = bonusPick == 0 ? 0 : bonusPick == 1 ? -5 : (int)(R() % 200) + 1;
    ArenaMarks::ApplyWinningTeamBonus(snap, el, m, bonus);
    std::printf("%s{\"k\":\"aw\",\"mode\":%d,\"outcome\":%d,\"last\":%d,\"pc\":%d,\"mvp\":%d,\"mm\":%.9g,\"bonus\":%d,\"f\":[",
                Sep(), mode, oc, snap.lastStandingId, snap.podiumCount, mvp, mm, bonus);
    for (int i = 0; i < snap.fighterCount; ++i)
      std::printf("%s{\"id\":%d,\"team\":%d,\"cs\":%.9g,\"el\":%d,\"m0\":%d,\"m1\":%d}", i ? "," : "", snap.fighters[i].id,
                  (int)snap.fighters[i].team, cs[i], (int)el[i], m0[i], m[i]);
    std::printf("],\"hascs\":%d}\n", (int)hasCs);
  }
  // --- DifficultyMultiplier + ApplyDifficulty
  for (int c = 0; c < 1500; ++c) {
    int base = (int)(R() % 60) - 5;
    float self, opp;
    int pick = R() % 5;
    if (pick == 0) { self = 50.0f; opp = 50.0f - 30.0f; }        // mult exactly 0.5
    else if (pick == 1) { self = 20.0f; opp = 80.0f; }           // 1.5 at the clamp
    else if (pick == 2) { self = 0.0f; opp = 100.0f; }           // above the clamp
    else if (pick == 3) { self = F(0, 100); opp = self + 30.0f; } // 1.5 (+rounding)
    else { self = F(0, 100); opp = F(0, 100); }
    float dm = ArenaMarks::DifficultyMultiplier(self, opp);
    int ad = ArenaMarks::ApplyDifficulty(base, self, opp);
    std::printf("%s{\"k\":\"ad\",\"base\":%d,\"self\":%.9g,\"opp\":%.9g,\"dm\":%.9g,\"ad\":%d}\n", Sep(), base, self, opp, dm, ad);
  }
  // --- Division / Rarity / Challenge multipliers
  for (int d = -1; d <= 3; ++d)
    for (int r = -1; r <= 4; ++r)
      std::printf("%s{\"k\":\"cm\",\"div\":%d,\"rar\":%d,\"dv\":%.9g,\"rv\":%.9g,\"cv\":%.9g}\n", Sep(), d, r,
                  ArenaMarks::DivisionMultiplier(d), ArenaMarks::RarityMultiplier(r), ArenaMarks::ChallengeMultiplier(d, r));
  // --- UniqueChallengeBonus (halves: 20*0.375 = 7.5, 40*0.0125 = 0.5, 100*0.005 ~ 0.5, 50*0.25 = 12.5, 100*0.125 = 12.5)
  const double ubm[] = { 1.0, 0.5, 2.5, 0.0, 0.375, 0.0125, 0.005, 0.25, 0.125, 1.005, 3.3, 100.0, 0.999999 };
  for (int a = 0; a < 13; ++a)
    for (int b = 0; b < 13; b += 4) {
      PGConfigStub_Reset();
      PGConfigStub_Challenges().uniqueBonusMultiplier = ubm[a];
      PGConfigStub_Challenges().skarnBonusMultiplier = ubm[(a + b) % 13];
      for (int d = -1; d <= 3; ++d)
        for (int sk = 0; sk <= 1; ++sk)
          std::printf("%s{\"k\":\"ub\",\"um\":%.17g,\"sm\":%.17g,\"div\":%d,\"skarn\":%d,\"ub\":%d}\n", Sep(), ubm[a],
                      ubm[(a + b) % 13], d, sk, ArenaMarks::UniqueChallengeBonus(d, sk != 0));
    }
  std::printf("]\n");
}
