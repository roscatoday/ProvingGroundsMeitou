// sources: MatchRules.cpp
// Task 3.1: the pure target choice of FightStarter.cpp. FightStarter.cpp includes kenshi/*.h and PGLog.h, so it cannot be linked here: the
// functions below are COPIED VERBATIM from it (IsLivingFighter :132-135, MakeParticipant .. FindMatchedLivingTeamOpponent :176-277), over a
// stand-in Character with the four reads they make (isValid, isDead, isUnconcious, getPosition) and a stand-in Ogre::Vector3. Linked with
// the real MatchRules.cpp (IsOpponent). One line per case: mode (0..3; 3 is no mode, the generic branch), count, attacker index, each
// fighter [present, valid, dead, unconscious, x, y, z] and team, then per target whether IsLivingOpponent, HorizontalDistSq to each present
// target, the nearest and the matched choice.
#include "MatchRules.h"
#include <cstdio>

namespace Ogre { struct Vector3 { float x, y, z; }; }
struct Character
{
    bool valid, dead, unc; Ogre::Vector3 pos;
    bool isValid() const { return valid; }
    bool isDead() const { return dead; }
    bool isUnconcious() const { return unc; }
    Ogre::Vector3 getPosition() const { return pos; }
};
namespace {
    bool IsLivingFighter(Character* c)
    {
        return c && c->isValid() && !c->isDead() && !c->isUnconcious();
    }
    MatchRules::MatchParticipant MakeParticipant(int index, MatchRules::MatchTeam team, Character* c)
    {
        MatchRules::MatchParticipant p = {};
        p.id = index;
        p.team = team;
        p.conscious = IsLivingFighter(c);
        p.dead = c && c->isDead();
        return p;
    }

    bool IsLivingOpponent(
        MatchRules::MatchMode mode,
        int attackerIdx,
        int targetIdx,
        Character** fighters,
        MatchRules::MatchTeam* teams)
    {
        if (attackerIdx == targetIdx)
            return false;

        Character* target = fighters[targetIdx];
        if (!IsLivingFighter(target))
            return false;

        const MatchRules::MatchParticipant attacker = MakeParticipant(attackerIdx, teams[attackerIdx], fighters[attackerIdx]);
        const MatchRules::MatchParticipant opponent = MakeParticipant(targetIdx, teams[targetIdx], target);
        return MatchRules::IsOpponent(mode, attacker, opponent);
    }

    float HorizontalDistSq(const Ogre::Vector3& a, const Ogre::Vector3& b)
    {
        const float dx = a.x - b.x;
        const float dz = a.z - b.z;
        return (dx * dx) + (dz * dz);
    }

    int FindNearestLivingOpponent(
        MatchRules::MatchMode mode,
        int attackerIdx,
        Character** fighters,
        MatchRules::MatchTeam* teams,
        int count)
    {
        Character* attacker = fighters[attackerIdx];
        if (!attacker)
            return -1;

        const Ogre::Vector3 attackerPos = attacker->getPosition();
        int nearestIdx = -1;
        float nearestDistSq = 1.0e30f;
        for (int targetIdx = 0; targetIdx < count; ++targetIdx)
        {
            if (!IsLivingOpponent(mode, attackerIdx, targetIdx, fighters, teams))
                continue;

            const float distSq =
                HorizontalDistSq(attackerPos, fighters[targetIdx]->getPosition());
            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearestIdx = targetIdx;
            }
        }
        return nearestIdx;
    }

    int FindMatchedLivingTeamOpponent(
        MatchRules::MatchMode mode,
        int attackerIdx,
        Character** fighters,
        MatchRules::MatchTeam* teams,
        int count)
    {
        const MatchRules::MatchTeam attackerTeam = teams[attackerIdx];
        int attackerSlot = 0;
        for (int i = 0; i < attackerIdx; ++i)
        {
            if (teams[i] == attackerTeam)
                ++attackerSlot;
        }

        int livingOpponentCount = 0;
        for (int targetIdx = 0; targetIdx < count; ++targetIdx)
        {
            if (IsLivingOpponent(mode, attackerIdx, targetIdx, fighters, teams))
                ++livingOpponentCount;
        }
        if (livingOpponentCount <= 0)
            return -1;

        const int desiredSlot = attackerSlot % livingOpponentCount;
        int livingSlot = 0;
        for (int targetIdx = 0; targetIdx < count; ++targetIdx)
        {
            if (!IsLivingOpponent(mode, attackerIdx, targetIdx, fighters, teams))
                continue;
            if (livingSlot == desiredSlot)
                return targetIdx;
            ++livingSlot;
        }
        return -1;
    }
}
static unsigned s = 31031u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
// small integer grid so equal distances (ties) are common; a few fractional values for the float arithmetic
static float Coord() { unsigned k = R() % 10; if (k < 7) return (float)((int)(R() % 7) - 3); return (float)((int)(R() % 2001) - 1000) / 7.0f; }
int main() {
  std::printf("[\n");
  for (int c = 0; c < 2000; ++c) {
    int mode = (int)(R() % 16); mode = mode < 5 ? 0 : mode < 10 ? 1 : mode < 15 ? 2 : 3;
    int count = 1 + (int)(R() % 8);                 // 1..8
    int pDown = R() % 4;                            // 0 = all up ... 3 = mostly down
    Character store[8] = {};
    Character* fighters[8] = {};
    MatchRules::MatchTeam teams[8] = {};
    int present[8] = {};
    for (int i = 0; i < count; ++i) {
      present[i] = (R() % 25) != 0;
      Character& ch = store[i];
      ch.valid = (R() % 20) != 0;
      ch.dead = pDown > 0 && (int)(R() % 6) < pDown && (R() % 2);
      ch.unc = pDown > 0 && (int)(R() % 4) < pDown;
      ch.pos.x = Coord(); ch.pos.y = Coord(); ch.pos.z = Coord();
      fighters[i] = present[i] ? &ch : NULL;
      int t = R() % 9; teams[i] = t < 4 ? MatchRules::TeamA : (t < 8 ? MatchRules::TeamB : MatchRules::TeamNone);
    }
    int attacker = (int)(R() % count);
    if (!present[attacker] && (R() % 3)) { present[attacker] = 1; fighters[attacker] = &store[attacker]; }
    MatchRules::MatchMode m = (MatchRules::MatchMode)mode;
    std::printf("%s{\"mode\":%d,\"count\":%d,\"a\":%d,\"f\":[", c ? "," : "", mode, count, attacker);
    for (int i = 0; i < count; ++i)
      std::printf("%s[%d,%d,%d,%d,%.9g,%.9g,%.9g,%d]", i ? "," : "", present[i], (int)store[i].valid, (int)store[i].dead, (int)store[i].unc,
                  store[i].pos.x, store[i].pos.y, store[i].pos.z, (int)teams[i]);
    std::printf("],\"living\":[");
    for (int i = 0; i < count; ++i) std::printf("%s%d", i ? "," : "", (int)IsLivingFighter(fighters[i]));
    std::printf("],\"opp\":[");
    for (int j = 0; j < count; ++j) std::printf("%s%d", j ? "," : "", (int)IsLivingOpponent(m, attacker, j, fighters, teams));
    std::printf("],\"d2\":[");
    for (int j = 0; j < count; ++j) {
      if (present[attacker] && present[j]) std::printf("%s%.9g", j ? "," : "", HorizontalDistSq(fighters[attacker]->getPosition(), fighters[j]->getPosition()));
      else std::printf("%snull", j ? "," : "");
    }
    std::printf("],\"nearest\":%d,\"matched\":%d}\n", FindNearestLivingOpponent(m, attacker, fighters, teams, count),
                FindMatchedLivingTeamOpponent(m, attacker, fighters, teams, count));
  }
  std::printf("]\n");
  return 0;
}
