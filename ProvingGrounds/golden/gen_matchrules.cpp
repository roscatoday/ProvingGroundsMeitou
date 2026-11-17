// sources: MatchRules.cpp
// Random participant lists -> MatchRules::EvaluateEnd, and random pairs -> MatchRules::IsOpponent, as JSON lines in one array.
// Case kinds: "end" (mode, downedPercent, null list flag, count, participants -> end kind + EndKindName), "opp" (mode, a, b -> bool),
// "name" (EndKindName of every kind 0..5, 5 being out of range).
#include "MatchRules.h"
#include <cstdio>
using namespace MatchRules;
static unsigned s = 424242u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static const int kPercents[] = {0, 25, 50, 100, -10, 33, 1, 99, 101};
int main() {
  std::printf("[\n");
  bool first = true;
  for (int c = 0; c < 3000; ++c) {
    int mode = R() % 3;
    int pct = kPercents[R() % 9];
    int count = R() % 9;                       // 0..8
    bool nullList = (R() % 40) == 0;
    int pConscious = R() % 5;                  // 0 = all down ... 4 = mostly up
    int pElim = R() % 4;                       // 0 = none eliminated ... 3 = all eliminated
    MatchParticipant list[8] = {};
    for (int i = 0; i < count; ++i) {
      MatchParticipant& p = list[i];
      p.id = 1 + (int)(R() % 10);
      int t = R() % 7; p.team = t < 3 ? TeamA : (t < 6 ? TeamB : TeamNone);
      if (mode == ModeLastStanding && (R() % 2)) p.team = TeamNone;
      p.conscious = pConscious == 0 ? false : (int)(R() % 4) < pConscious;
      p.dead = !p.conscious && (R() % 3) == 0;
      p.eliminated = pElim == 3 ? true : (pElim == 0 ? false : (int)(R() % 3) < pElim);
    }
    MatchEndKind end = EvaluateEnd((MatchMode)mode, nullList ? 0 : list, count, pct);
    std::printf("%s{\"k\":\"end\",\"mode\":%d,\"pct\":%d,\"null\":%d,\"p\":[", first ? "" : ",", mode, pct, (int)nullList);
    first = false;
    for (int i = 0; i < count; ++i)
      std::printf("%s[%d,%d,%d,%d,%d]", i ? "," : "", list[i].id, (int)list[i].team, (int)list[i].conscious,
                  (int)list[i].dead, (int)list[i].eliminated);
    std::printf("],\"end\":%d,\"name\":\"%s\"}\n", (int)end, EndKindName(end));
  }
  for (int c = 0; c < 1500; ++c) {
    int mode = R() % 3;
    MatchParticipant a = {}, b = {};
    a.id = 1 + (int)(R() % 4); b.id = 1 + (int)(R() % 4);
    a.team = (MatchTeam)(R() % 3); b.team = (MatchTeam)(R() % 3);
    a.conscious = R() % 2; b.conscious = R() % 2;
    a.eliminated = R() % 2; b.eliminated = R() % 2;
    bool o = IsOpponent((MatchMode)mode, a, b);
    std::printf(",{\"k\":\"opp\",\"mode\":%d,\"a\":[%d,%d,%d,0,%d],\"b\":[%d,%d,%d,0,%d],\"opp\":%d}\n", mode,
                a.id, (int)a.team, (int)a.conscious, (int)a.eliminated, b.id, (int)b.team, (int)b.conscious, (int)b.eliminated, (int)o);
  }
  for (int k = 0; k < 6; ++k)
    std::printf(",{\"k\":\"name\",\"kind\":%d,\"name\":\"%s\"}\n", k, EndKindName((MatchEndKind)k));
  std::printf("]\n");
}
