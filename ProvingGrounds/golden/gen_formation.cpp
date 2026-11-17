// sources:
// copies: 8
// ArenaIngress.cpp's formation maths (task M4.3a). ArenaIngress.cpp includes KenshiLib and cannot be linked, so the functions and
// expressions that compute a point from a site are COPIED VERBATIM below between "// BEGIN COPY ArenaIngress.cpp:<a>-<b>" and
// "// END COPY" markers, and the "// copies: 8" line above states how many; golden/check_copies.py (run by gen.sh before compiling)
// and Tests/Fidelity/GoldenCopiesTests.cs fail when a copy no longer equals upstream line for line or the count is not exact. Nothing inside a copy is edited: the engine types and calls it uses are stand-ins
// declared OUTSIDE the copies, with the original's names:
//   Ogre::Vector3 / Ogre::Quaternion / Ogre::Radian / Ogre::Math   golden/stubs/ogre_formation_stub.h (bodies from Ogre 1.9.0)
//   Building (getPosition, getOrientation, isValid, getPositionMarker), Character (isValid, isDead, getPosition, lookatPosition)
//   IssueMove (records the point), TownArena::IsBusy (a flag), ArenaIngress::GetBoundRegistry (a pointer the case sets)
//   the ArenaIngress.cpp globals the copies read (g_locationMode, g_targets, g_mode, g_fighters, g_teams, g_walkInTarget)
// The two excerpts from big functions (Begin's target assignment :1326-1335, BeginWalkIn's target :1735-1750) sit inside wrapper
// functions whose parameters have the names the excerpt uses. Copied ranges:
//   ArenaIngress.cpp:46-110    constants and marker tables
//   ArenaIngress.cpp:161-173   HorizontalDistSq, LocalToWorld
//   ArenaIngress.cpp:235-429   ScrambleOffset, TeamFormationOffset, AssignArenaTargets, AssignBannerTargets
//   ArenaIngress.cpp:481-511   RegistryApproachPoint
//   ArenaIngress.cpp:549-596   EscortGatherBase, IssueEscortMoves
//   ArenaIngress.cpp:629-658   FaceOpposingFormation
//   ArenaIngress.cpp:1326-1335 Begin: banner targets / arena targets at the location's scale
//   ArenaIngress.cpp:1735-1750 BeginWalkIn: the walk-in target
// Lines, one case each (vectors as [x,y,z], %.9g; yaw = Ogre::Quaternion(Radian(yaw), UNIT_Y), the building's orientation):
//   "quat"     yaw -> w,x,y,z                                        "rot"   arbitrary quaternion * vector
//   "hd"       HorizontalDistSq                                      "l2w"   LocalToWorld(pos, yaw, local)
//   "scramble" ScrambleOffset(slot, count, inner, outer)             "team"  TeamFormationOffset(team, slot, teamCount, side, col, row)
//   "arena"    AssignArenaTargets(mode, teams, pos, yaw, scale)      "banner" AssignBannerTargets(mode, teams | null, pos)
//   "site"     Begin's assignment (location, town busy, mode, teams, pos, yaw)
//   "walkin"   BeginWalkIn's target (location, team, pos, yaw)
//   "escort"   EscortGatherBase + IssueEscortMoves (site | null, registry | null | invalid, location, escort validity)
//   "regnode"  RegistryApproachPoint (from | null | invalid, marker)
//   "face"     FaceOpposingFormation (mode, teams, targets, index, fighter position -> the face point or none)
#include "ArenaIngress.h"
#include "ogre_formation_stub.h"
#include <cmath>
#include <cstdio>
#include <vector>

#ifndef NULL
#define NULL 0
#endif

class Building
{
public:
    Ogre::Vector3 pos;
    Ogre::Quaternion ori;
    bool valid;
    Ogre::Vector3 marker;
    Building() : pos(0.0f, 0.0f, 0.0f), ori(), valid(true), marker(0.0f, 0.0f, 0.0f) {}
    Ogre::Vector3 getPosition() const { return pos; }
    Ogre::Quaternion getOrientation() const { return ori; }
    bool isValid() const { return valid; }
    Ogre::Vector3 getPositionMarker(const Ogre::Vector3&) const { return marker; }
};

class Character
{
public:
    Ogre::Vector3 pos;
    bool valid;
    bool dead;
    bool looked;
    Ogre::Vector3 lookedAt;
    Character() : pos(0.0f, 0.0f, 0.0f), valid(true), dead(false), looked(false), lookedAt(0.0f, 0.0f, 0.0f) {}
    bool isValid() const { return valid; }
    bool isDead() const { return dead; }
    Ogre::Vector3 getPosition() const { return pos; }
    void lookatPosition(const Ogre::Vector3& p, bool) { looked = true; lookedAt = p; }
};

namespace TownArena
{
    bool g_busy = false;
    bool IsBusy() { return g_busy; }
}

namespace
{
    Building* g_stubRegistry = NULL;
    std::vector<int> g_moveWho;
    std::vector<Ogre::Vector3> g_moveTo;
    Character* g_moveBase = NULL;
}

Building* ArenaIngress::GetBoundRegistry() { return g_stubRegistry; }

namespace
{
    void IssueMove(Character* c, const Ogre::Vector3& loc)
    {
        g_moveWho.push_back(static_cast<int>(c - g_moveBase));
        g_moveTo.push_back(loc);
    }

    ArenaIngress::LocationMode g_locationMode = ArenaIngress::LocationArena;
    MatchRules::MatchMode g_mode = MatchRules::ModeTeamAvB;
    Ogre::Vector3 g_walkInTarget = Ogre::Vector3(0.0f, 0.0f, 0.0f);
    std::vector<Character*> g_fighters;
    std::vector<MatchRules::MatchTeam> g_teams;
    std::vector<Ogre::Vector3> g_targets;

// BEGIN COPY ArenaIngress.cpp:46-110
    // Pit entry relative to arena building (tune in-game if needed).
    static const Ogre::Vector3 kArenaMarkers[] = {
        Ogre::Vector3(-80.0f, 0.0f,  0.0f),
        Ogre::Vector3(-60.0f, 0.0f,  30.0f),
        Ogre::Vector3(-60.0f, 0.0f, -30.0f),
        Ogre::Vector3( 80.0f, 0.0f,  0.0f),
        Ogre::Vector3( 60.0f, 0.0f,  30.0f),
        Ogre::Vector3( 60.0f, 0.0f, -30.0f),
    };
    static const int kArenaMarkerCount = 6;
    // Further back than pit markers so waiters look like spectators.
    static const Ogre::Vector3 kArenaBenchA[] = {
        Ogre::Vector3(-120.0f, 0.0f,  40.0f),
        Ogre::Vector3(-120.0f, 0.0f,   0.0f),
        Ogre::Vector3(-120.0f, 0.0f, -40.0f),
    };
    static const Ogre::Vector3 kArenaBenchB[] = {
        Ogre::Vector3( 120.0f, 0.0f,  40.0f),
        Ogre::Vector3( 120.0f, 0.0f,   0.0f),
        Ogre::Vector3( 120.0f, 0.0f, -40.0f),
    };
    static const int kArenaBenchCount = 3;
    // Compact arena uses the same layout at half scale. Keep this separate
    // from the full arena values so it can be tuned against the placed model.
    static const float kSmallArenaScale = 0.5f;
    static const float kBannerGatherOffset = 20.0f;
    static const float kBannerBenchExtra = 18.0f;
    static const float kArenaLastStandingInnerRadius = 60.0f;
    static const float kArenaLastStandingOuterRadius = 155.0f;
    static const float kBannerLastStandingInnerRadius = 45.0f;
    static const float kBannerLastStandingOuterRadius = 115.0f;
    static const float kArenaTeamSideOffset = 85.0f;
    static const float kArenaTeamColumnSpacing = 35.0f;
    static const float kArenaTeamRowSpacing = 46.0f;
    static const float kBannerTeamSideOffset = 65.0f;
    static const float kBannerTeamColumnSpacing = 35.0f;
    static const float kBannerTeamRowSpacing = 42.0f;
    static const int kTeamRowsPerColumn = 5;
    static const float kEscortGatherTowardRegistry = 120.0f;
    static const float kEscortGatherFallbackLocalX = -140.0f;
    static const float kEscortSpacing = 18.0f;

    static const float kArrivalRadius = 60.0f;
    static const float kSettleRadius = 120.0f;
    // Fight formations need a tighter gate than walking up to the Registry.
    // Otherwise everyone can still be near the middle when combat is engaged.
    static const float kFormationArrivalRadius = 30.0f;
    static const float kSettleSpeed = 0.35f;
    static const float kSettleHoldSec = 0.8f;
    static const float kIngressTimeoutSec = 20.0f;
    static const float kNpcFormationTimeoutSec = 30.0f;
    // Extend only while every missing NPC is making measurable progress.
    static const float kNpcFormationHardCapSec = 90.0f;
    static const float kNpcFormationProgressLogSec = 10.0f;
    static const float kNpcFormationStallSec = 20.0f;
    static const float kNpcFormationProgressDistance = kFormationArrivalRadius;
    static const float kFightGatherHoldSec = 4.0f;
    static const float kFightCountdownSec = 2.25f;
    // RMB mouse-up cancels orders issued on the same click — wait it out, then keep re-issuing.
    static const float kApproachFirstMoveDelaySec = 0.35f;
    static const float kApproachMoveReissueSec = 0.40f;
    // FCS Instance usenode1024 on Proving Grounds Registry (building-local).
    static const Ogre::Vector3 kRegistryUseNodeLocal(-3.0f, 10.0f, 3.0f);
    // Node sits near mesh/board; nudge outward from building center onto walkable pad.
    static const float kRegistryUseNodeOutwardNudge = 30.0f;
// END COPY

// BEGIN COPY ArenaIngress.cpp:161-173
    float HorizontalDistSq(const Ogre::Vector3& a, const Ogre::Vector3& b)
    {
        const float dx = a.x - b.x;
        const float dz = a.z - b.z;
        return (dx * dx) + (dz * dz);
    }

    Ogre::Vector3 LocalToWorld(Building* building, const Ogre::Vector3& local)
    {
        Ogre::Vector3 pos = building->getPosition();
        Ogre::Quaternion ori = building->getOrientation();
        return pos + (ori * local);
    }
// END COPY

// BEGIN COPY ArenaIngress.cpp:235-429
    Ogre::Vector3 ScrambleOffset(
        int slot,
        int count,
        float innerRadius,
        float outerRadius)
    {
        // A golden-angle sunflower pattern fills the pit without obvious rows
        // or a predictable ring. Keeping the centre empty prevents one fighter
        // becoming the natural first target of everyone around them.
        static const float kGoldenAngle = 2.39996323f;
        const int safeCount = count > 0 ? count : 1;
        const float areaFraction =
            (static_cast<float>(slot) + 0.5f) / static_cast<float>(safeCount);
        const float innerSq = innerRadius * innerRadius;
        const float outerSq = outerRadius * outerRadius;
        const float radius = sqrtf(innerSq + areaFraction * (outerSq - innerSq));
        const float angle = (static_cast<float>(slot) * kGoldenAngle) +
            (static_cast<float>(safeCount) * 0.37f);
        return Ogre::Vector3(cosf(angle) * radius, 0.0f, sinf(angle) * radius);
    }

    Ogre::Vector3 TeamFormationOffset(
        MatchRules::MatchTeam team,
        int slot,
        int teamCount,
        float sideOffset,
        float columnSpacing,
        float rowSpacing)
    {
        const int column = slot / kTeamRowsPerColumn;
        const int row = slot % kTeamRowsPerColumn;
        const int firstInColumn = column * kTeamRowsPerColumn;
        const int remaining = teamCount - firstInColumn;
        const int rowsInColumn = remaining < kTeamRowsPerColumn
            ? remaining
            : kTeamRowsPerColumn;
        const float centeredRow = static_cast<float>(row) -
            (static_cast<float>(rowsInColumn - 1) * 0.5f);
        const float side = (team == MatchRules::TeamA) ? -1.0f : 1.0f;
        // Extra columns grow away from the centre. Growing inward made large
        // teams start almost on top of each other.
        const float x = side * (sideOffset + static_cast<float>(column) * columnSpacing);
        return Ogre::Vector3(x, 0.0f, centeredRow * rowSpacing);
    }

    void AssignArenaTargets(
        MatchRules::MatchMode mode,
        MatchRules::MatchTeam* teams,
        int count,
        Building* arena,
        std::vector<Ogre::Vector3>& targets,
        float scale = 1.0f)
    {
        targets.resize(static_cast<size_t>(count));
        const int half = kArenaMarkerCount / 2;

        if (mode == MatchRules::ModeTeams1v1)
        {
            int aSlot = 0;
            int bSlot = 0;
            for (int i = 0; i < count; ++i)
            {
                Ogre::Vector3 local;
                if (teams[i] == MatchRules::TeamA)
                {
                    if (aSlot == 0)
                        local = kArenaMarkers[0] * scale;
                    else
                        local = kArenaBenchA[(aSlot - 1) % kArenaBenchCount] * scale;
                    ++aSlot;
                }
                else
                {
                    if (bSlot == 0)
                        local = kArenaMarkers[half] * scale;
                    else
                        local = kArenaBenchB[(bSlot - 1) % kArenaBenchCount] * scale;
                    ++bSlot;
                }
                targets[static_cast<size_t>(i)] = LocalToWorld(arena, local);
            }
            return;
        }

        if (mode == MatchRules::ModeLastStanding)
        {
            for (int i = 0; i < count; ++i)
            {
                targets[static_cast<size_t>(i)] =
                    LocalToWorld(
                        arena,
                        ScrambleOffset(
                            i,
                            count,
                            kArenaLastStandingInnerRadius * scale,
                            kArenaLastStandingOuterRadius * scale));
            }
            return;
        }

        int teamACount = 0;
        int teamBCount = 0;
        for (int i = 0; i < count; ++i)
        {
            if (teams[i] == MatchRules::TeamA)
                ++teamACount;
            else if (teams[i] == MatchRules::TeamB)
                ++teamBCount;
        }

        int teamASlot = 0;
        int teamBSlot = 0;
        for (int i = 0; i < count; ++i)
        {
            const bool teamA = teams[i] == MatchRules::TeamA;
            const int slot = teamA ? teamASlot++ : teamBSlot++;
            const int teamCount = teamA ? teamACount : teamBCount;
            const Ogre::Vector3 local = TeamFormationOffset(
                teams[i],
                slot,
                teamCount,
                kArenaTeamSideOffset * scale,
                kArenaTeamColumnSpacing * scale,
                kArenaTeamRowSpacing * scale);
            targets[static_cast<size_t>(i)] = LocalToWorld(arena, local);
        }
    }

    void AssignBannerTargets(
        MatchRules::MatchMode mode,
        MatchRules::MatchTeam* teams,
        int count,
        Building* banner,
        std::vector<Ogre::Vector3>& targets)
    {
        targets.resize(static_cast<size_t>(count));
        const Ogre::Vector3 base = banner->getPosition();

        if (mode == MatchRules::ModeTeams1v1 && teams)
        {
            int aSlot = 0;
            int bSlot = 0;
            for (int i = 0; i < count; ++i)
            {
                const bool teamA = (teams[i] == MatchRules::TeamA);
                const float side = teamA ? -1.0f : 1.0f;
                const int slot = teamA ? aSlot : bSlot;
                const float radius = kBannerGatherOffset + (slot > 0 ? kBannerBenchExtra : 0.0f);
                targets[static_cast<size_t>(i)] = base + Ogre::Vector3(side * radius, 0.0f, static_cast<float>(slot) * 8.0f);
                if (teamA)
                    ++aSlot;
                else
                    ++bSlot;
            }
            return;
        }

        if (mode == MatchRules::ModeLastStanding)
        {
            for (int i = 0; i < count; ++i)
                targets[static_cast<size_t>(i)] =
                    base + ScrambleOffset(
                        i,
                        count,
                        kBannerLastStandingInnerRadius,
                        kBannerLastStandingOuterRadius);
            return;
        }

        int teamACount = 0;
        int teamBCount = 0;
        for (int i = 0; i < count; ++i)
        {
            if (teams[i] == MatchRules::TeamA)
                ++teamACount;
            else if (teams[i] == MatchRules::TeamB)
                ++teamBCount;
        }

        int teamASlot = 0;
        int teamBSlot = 0;
        for (int i = 0; i < count; ++i)
        {
            const bool teamA = teams[i] == MatchRules::TeamA;
            const int slot = teamA ? teamASlot++ : teamBSlot++;
            const int teamCount = teamA ? teamACount : teamBCount;
            targets[static_cast<size_t>(i)] = base + TeamFormationOffset(
                teams[i],
                slot,
                teamCount,
                kBannerTeamSideOffset,
                kBannerTeamColumnSpacing,
                kBannerTeamRowSpacing);
        }
    }
// END COPY

// BEGIN COPY ArenaIngress.cpp:481-511
    // Walk to interact point. Prefer Kenshi usage/position marker (exposed use node),
    // fall back to Registry-style local usenode offset + outward nudge.
    Ogre::Vector3 RegistryApproachPoint(Building* building, Character* from)
    {
        if (!building)
            return Ogre::Vector3(0.0f, 0.0f, 0.0f);

        if (from && from->isValid())
        {
            const Ogre::Vector3 marker = building->getPositionMarker(from->getPosition());
            const Ogre::Vector3 center = building->getPosition();
            const Ogre::Vector3 delta = marker - center;
            const float lenSq = (delta.x * delta.x) + (delta.y * delta.y) + (delta.z * delta.z);
            // Marker valid when it isn't stuck at the building origin.
            if (lenSq > 1.0f)
                return marker;
        }

        Ogre::Vector3 world = LocalToWorld(building, kRegistryUseNodeLocal);
        const Ogre::Vector3 center = building->getPosition();
        Ogre::Vector3 dir = world - center;
        dir.y = 0.0f;
        const float lenSq = (dir.x * dir.x) + (dir.z * dir.z);
        if (lenSq > 1.0f)
        {
            dir /= sqrtf(lenSq);
            world.x += dir.x * kRegistryUseNodeOutwardNudge;
            world.z += dir.z * kRegistryUseNodeOutwardNudge;
        }
        return world;
    }
// END COPY

// BEGIN COPY ArenaIngress.cpp:549-596
    Ogre::Vector3 EscortGatherBase(Building* site)
    {
        if (!site)
            return Ogre::Vector3(0.0f, 0.0f, 0.0f);

        const Ogre::Vector3 sitePos = site->getPosition();
        Building* registry = ArenaIngress::GetBoundRegistry();
        if (registry && registry->isValid())
        {
            Ogre::Vector3 dir = registry->getPosition() - sitePos;
            dir.y = 0.0f;
            const float lenSq = (dir.x * dir.x) + (dir.z * dir.z);
            if (lenSq > 1.0f)
            {
                const float invLen = 1.0f / sqrtf(lenSq);
                dir.x *= invLen;
                dir.z *= invLen;
                return sitePos + (dir * kEscortGatherTowardRegistry);
            }
        }

        if (g_locationMode == ArenaIngress::LocationBanner)
            return sitePos + Ogre::Vector3(0.0f, 0.0f, -kEscortGatherTowardRegistry);

        return LocalToWorld(site, Ogre::Vector3(kEscortGatherFallbackLocalX, 0.0f, 0.0f));
    }

    void IssueEscortMoves(Building* site, Character** escorts, int escortCount)
    {
        if (!escorts || escortCount <= 0)
            return;

        const Ogre::Vector3 base = EscortGatherBase(site);
        for (int i = 0; i < escortCount; ++i)
        {
            Character* escort = escorts[i];
            if (!escort || !escort->isValid())
                continue;

            const int col = i % 3;
            const int row = i / 3;
            const Ogre::Vector3 offset(
                (static_cast<float>(col) - 1.0f) * kEscortSpacing,
                0.0f,
                static_cast<float>(row) * kEscortSpacing);
            IssueMove(escort, base + offset);
        }
    }
// END COPY

// BEGIN COPY ArenaIngress.cpp:629-658
    void FaceOpposingFormation(size_t index)
    {
        if (index >= g_fighters.size() || g_teams.size() != g_targets.size())
            return;
        Character* fighter = g_fighters[index];
        if (!fighter || !fighter->isValid() || fighter->isDead())
            return;

        // Use the opponent's assigned mark even before they arrive; the marks
        // already account for the arena's orientation in world space.
        size_t closest = g_targets.size();
        float distance = 1.0e30f;
        for (size_t other = 0; other < g_targets.size(); ++other)
        {
            if (other == index ||
                (g_mode != MatchRules::ModeLastStanding && g_teams[other] == g_teams[index]))
                continue;
            const float candidate = HorizontalDistSq(fighter->getPosition(), g_targets[other]);
            if (candidate < distance)
            {
                distance = candidate;
                closest = other;
            }
        }
        if (closest == g_targets.size() || distance <= 1.0f)
            return;
        Ogre::Vector3 face = g_targets[closest];
        face.y = fighter->getPosition().y;
        fighter->lookatPosition(face, true);
    }
// END COPY
}

namespace ArenaIngress
{
    // Begin (ArenaIngress.cpp:1253-1411) from "if (g_locationMode == LocationBanner)" to the end of its else block.
    void SiteAssignment(
        MatchRules::MatchMode mode,
        MatchRules::MatchTeam* teams,
        int count,
        Building* site)
    {
// BEGIN COPY ArenaIngress.cpp:1326-1335
        if (g_locationMode == LocationBanner)
            AssignBannerTargets(mode, teams, count, site, g_targets);
        else {
            const float arenaScale = g_locationMode == LocationSmallArena
                ? kSmallArenaScale
                : (!TownArena::IsBusy() ? 0.70f : 1.0f);
            AssignArenaTargets(
                mode, teams, count, site, g_targets,
                arenaScale);
        }
// END COPY
    }

    // BeginWalkIn (ArenaIngress.cpp:1722-1770) from "if (g_locationMode == LocationBanner)" to the end of its else block.
    void WalkInTarget(MatchRules::MatchTeam team, Building* site)
    {
// BEGIN COPY ArenaIngress.cpp:1735-1750
        if (g_locationMode == LocationBanner)
        {
            const Ogre::Vector3 base = site->getPosition();
            const float side = (team == MatchRules::TeamA) ? -1.0f : 1.0f;
            g_walkInTarget = base + Ogre::Vector3(side * kBannerGatherOffset, 0.0f, 0.0f);
        }
        else
        {
            const int half = kArenaMarkerCount / 2;
            Ogre::Vector3 local = (team == MatchRules::TeamA)
                ? kArenaMarkers[0]
                : kArenaMarkers[half];
            if (g_locationMode == LocationSmallArena)
                local = local * kSmallArenaScale;
            g_walkInTarget = LocalToWorld(site, local);
        }
// END COPY
    }
}

// ---------------------------------------------------------------- case generation (not copied)
static unsigned s = 20261113u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }
static float RF(int lo, int hi, int div) { const int n = lo + static_cast<int>(R() % static_cast<unsigned>(hi - lo + 1)); return static_cast<float>(n) / static_cast<float>(div); }

static void PV(const Ogre::Vector3& v) { std::printf("[%.9g,%.9g,%.9g]", v.x, v.y, v.z); }
static void PVs(const std::vector<Ogre::Vector3>& vs)
{
    std::printf("[");
    for (size_t i = 0; i < vs.size(); ++i) { if (i) std::printf(","); PV(vs[i]); }
    std::printf("]");
}
static void PTeams(const std::vector<MatchRules::MatchTeam>& t)
{
    std::printf("[");
    for (size_t i = 0; i < t.size(); ++i) std::printf("%s%d", i ? "," : "", static_cast<int>(t[i]));
    std::printf("]");
}

static const float kYaws[] = {0.0f, 1.57079637f, -1.57079637f, 3.14159274f, -3.14159274f, 0.3f, -2.7f, 5.9f, 12.0f, 0.785398185f, -0.001f, 2.0f};
static const int kYawCount = static_cast<int>(sizeof(kYaws) / sizeof(kYaws[0]));
static float Yaw() { return (R() % 3 == 0) ? RF(-31416, 31416, 5000) : kYaws[R() % kYawCount]; }
static Ogre::Vector3 Pos()
{
    const unsigned k = R() % 4;
    if (k == 0) return Ogre::Vector3(0.0f, 0.0f, 0.0f);
    const float x = RF(-9000000, 9000000, 100);
    const float y = RF(-2000, 200000, 100);
    const float z = RF(-9000000, 9000000, 100);
    return Ogre::Vector3(x, y, z);
}
static Building MakeSite(float yaw)
{
    Building b;
    b.pos = Pos();
    b.ori = Ogre::Quaternion(Ogre::Radian(yaw), Ogre::Vector3::UNIT_Y);
    return b;
}

// teams: a then b in a shuffled order, with "none" sprinkled in when allowNone
static std::vector<MatchRules::MatchTeam> Teams(int a, int b, int none)
{
    std::vector<MatchRules::MatchTeam> t;
    for (int i = 0; i < a; ++i) t.push_back(MatchRules::TeamA);
    for (int i = 0; i < b; ++i) t.push_back(MatchRules::TeamB);
    for (int i = 0; i < none; ++i) t.push_back(MatchRules::TeamNone);
    const unsigned order = R() % 3;
    if (order == 1)
    {
        for (size_t i = t.size(); i > 1; --i) { const size_t j = R() % i; MatchRules::MatchTeam x = t[i - 1]; t[i - 1] = t[j]; t[j] = x; }
    }
    else if (order == 2)
    {
        std::vector<MatchRules::MatchTeam> alt;
        int ia = 0, ib = 0, in = 0;
        while (ia < a || ib < b || in < none)
        {
            if (ia < a) { alt.push_back(MatchRules::TeamA); ++ia; }
            if (ib < b) { alt.push_back(MatchRules::TeamB); ++ib; }
            if (in < none) { alt.push_back(MatchRules::TeamNone); ++in; }
        }
        t = alt;
    }
    return t;
}

static void Line(const char* kind) { std::printf("%s{\"k\":\"%s\"", Sep(), kind); }

int main()
{
    std::printf("[\n");

    // quaternion from yaw
    for (int i = 0; i < kYawCount; ++i)
    {
        const Ogre::Quaternion q(Ogre::Radian(kYaws[i]), Ogre::Vector3::UNIT_Y);
        Line("quat"); std::printf(",\"yaw\":%.9g,\"q\":[%.9g,%.9g,%.9g,%.9g]}\n", kYaws[i], q.w, q.x, q.y, q.z);
    }
    for (int c = 0; c < 60; ++c)
    {
        const float yaw = RF(-200000, 200000, 10000);
        const Ogre::Quaternion q(Ogre::Radian(yaw), Ogre::Vector3::UNIT_Y);
        Line("quat"); std::printf(",\"yaw\":%.9g,\"q\":[%.9g,%.9g,%.9g,%.9g]}\n", yaw, q.w, q.x, q.y, q.z);
    }
    // arbitrary quaternion * vector (a building whose orientation is not a pure yaw, or not unit length)
    for (int c = 0; c < 120; ++c)
    {
        const float w = RF(-1000, 1000, 1000);
        const float x = RF(-1000, 1000, 1000);
        const float y = RF(-1000, 1000, 1000);
        const float z = RF(-1000, 1000, 1000);
        const Ogre::Quaternion q(w, x, y, z);
        const float vx = RF(-20000, 20000, 100);
        const float vy = RF(-2000, 2000, 100);
        const float vz = RF(-20000, 20000, 100);
        const Ogre::Vector3 v(vx, vy, vz);
        Line("rot"); std::printf(",\"q\":[%.9g,%.9g,%.9g,%.9g],\"v\":", q.w, q.x, q.y, q.z); PV(v); std::printf(",\"out\":"); PV(q * v); std::printf("}\n");
    }
    // HorizontalDistSq
    for (int c = 0; c < 80; ++c)
    {
        const Ogre::Vector3 a = Pos();
        Ogre::Vector3 b = Pos();
        if (R() % 3 == 0) { const float dx = RF(-300, 300, 10); const float dz = RF(-300, 300, 10); b = Ogre::Vector3(a.x + dx, b.y, a.z + dz); }
        Line("hd"); std::printf(",\"a\":"); PV(a); std::printf(",\"b\":"); PV(b); std::printf(",\"d\":%.9g}\n", HorizontalDistSq(a, b));
    }
    // LocalToWorld
    for (int c = 0; c < 150; ++c)
    {
        const float yaw = Yaw();
        Building site = MakeSite(yaw);
        const float lx = RF(-20000, 20000, 100);
        const float ly = RF(-1000, 1000, 100);
        const float lz = RF(-20000, 20000, 100);
        const Ogre::Vector3 local(lx, ly, lz);
        Line("l2w"); std::printf(",\"pos\":"); PV(site.pos); std::printf(",\"yaw\":%.9g,\"local\":", yaw); PV(local);
        std::printf(",\"out\":"); PV(LocalToWorld(&site, local)); std::printf("}\n");
    }
    // ScrambleOffset: every slot of counts 0..24, plus odd inputs (negative count, slot past count, swapped radii; never a negative sqrt: no NaN in JSON)
    static const float kRadii[][2] = {
        {kArenaLastStandingInnerRadius, kArenaLastStandingOuterRadius},
        {kBannerLastStandingInnerRadius, kBannerLastStandingOuterRadius},
        {kArenaLastStandingInnerRadius * kSmallArenaScale, kArenaLastStandingOuterRadius * kSmallArenaScale},
        {kArenaLastStandingInnerRadius * 0.70f, kArenaLastStandingOuterRadius * 0.70f},
        {0.0f, 0.0f}, {100.0f, 10.0f}};
    for (int r = 0; r < 6; ++r)
        for (int count = -1; count <= 24; ++count)
            for (int slot = 0; slot < (count > 0 ? count : 1) + (r == 0 ? 2 : 0); ++slot)
            {
                Line("scramble"); std::printf(",\"slot\":%d,\"n\":%d,\"in\":%.9g,\"out\":%.9g,\"v\":", slot, count, kRadii[r][0], kRadii[r][1]);
                PV(ScrambleOffset(slot, count, kRadii[r][0], kRadii[r][1])); std::printf("}\n");
            }
    // TeamFormationOffset: each team, every slot of team counts 1..23 (columns roll over every 5), arena / banner / scaled spacings
    static const float kSpacing[][3] = {
        {kArenaTeamSideOffset, kArenaTeamColumnSpacing, kArenaTeamRowSpacing},
        {kBannerTeamSideOffset, kBannerTeamColumnSpacing, kBannerTeamRowSpacing},
        {kArenaTeamSideOffset * kSmallArenaScale, kArenaTeamColumnSpacing * kSmallArenaScale, kArenaTeamRowSpacing * kSmallArenaScale},
        {kArenaTeamSideOffset * 0.70f, kArenaTeamColumnSpacing * 0.70f, kArenaTeamRowSpacing * 0.70f}};
    for (int sp = 0; sp < 4; ++sp)
        for (int team = 0; team <= 2; ++team)
            for (int n = 1; n <= 23; ++n)
                for (int slot = 0; slot < n + (n == 23 ? 3 : 0); ++slot)
                {
                    if (sp > 1 && (n % 3) != 0) continue;   // the scaled spacings only for every third count
                    Line("team"); std::printf(",\"team\":%d,\"slot\":%d,\"n\":%d,\"side\":%.9g,\"col\":%.9g,\"row\":%.9g,\"v\":",
                        team, slot, n, kSpacing[sp][0], kSpacing[sp][1], kSpacing[sp][2]);
                    PV(TeamFormationOffset(static_cast<MatchRules::MatchTeam>(team), slot, n, kSpacing[sp][0], kSpacing[sp][1], kSpacing[sp][2]));
                    std::printf("}\n");
                }

    // AssignArenaTargets at a given scale (the default 1.0, the three staging scales and an odd one)
    static const float kScales[] = {1.0f, kSmallArenaScale, 0.70f, 1.37f};
    for (int c = 0; c < 260; ++c)
    {
        const int mode = static_cast<int>(R() % 3);
        int a, b, none;
        if (c < 40) { a = c % 8; b = (c / 8) % 8; none = 0; }
        else if (c < 60) { a = 18 + static_cast<int>(R() % 23); b = static_cast<int>(R() % 41); none = 0; }
        else { a = static_cast<int>(R() % 13); b = static_cast<int>(R() % 13); none = (R() % 5 == 0) ? static_cast<int>(R() % 3) : 0; }
        std::vector<MatchRules::MatchTeam> t = Teams(a, b, none);
        const float yaw = Yaw();
        Building site = MakeSite(yaw);
        const float scale = kScales[R() % 4];
        std::vector<Ogre::Vector3> targets;
        if (R() % 4 == 0) targets.assign(3 + R() % 5, Ogre::Vector3(7.0f, 7.0f, 7.0f));   // an old list is resized and overwritten
        AssignArenaTargets(static_cast<MatchRules::MatchMode>(mode), t.empty() ? NULL : &t[0], static_cast<int>(t.size()), &site, targets, scale);
        Line("arena"); std::printf(",\"mode\":%d,\"teams\":", mode); PTeams(t);
        std::printf(",\"pos\":"); PV(site.pos); std::printf(",\"yaw\":%.9g,\"scale\":%.9g,\"t\":", yaw, scale); PVs(targets); std::printf("}\n");
    }
    // AssignBannerTargets (world offsets: the banner's orientation is not used); teams null in Last Standing
    for (int c = 0; c < 160; ++c)
    {
        const int mode = static_cast<int>(R() % 3);
        int a, b, none;
        if (c < 40) { a = c % 8; b = (c / 8) % 8; none = 0; }
        else if (c < 55) { a = 18 + static_cast<int>(R() % 23); b = static_cast<int>(R() % 41); none = 0; }
        else { a = static_cast<int>(R() % 13); b = static_cast<int>(R() % 13); none = (R() % 5 == 0) ? static_cast<int>(R() % 3) : 0; }
        std::vector<MatchRules::MatchTeam> t = Teams(a, b, none);
        const float yaw = Yaw();
        Building site = MakeSite(yaw);
        std::vector<Ogre::Vector3> targets;
        const bool nullTeams = (mode == MatchRules::ModeLastStanding) && (R() % 2 == 0);
        AssignBannerTargets(static_cast<MatchRules::MatchMode>(mode), (nullTeams || t.empty()) ? NULL : &t[0], static_cast<int>(t.size()), &site, targets);
        Line("banner"); std::printf(",\"mode\":%d,\"null\":%d,\"teams\":", mode, nullTeams ? 1 : 0); PTeams(t);
        std::printf(",\"pos\":"); PV(site.pos); std::printf(",\"yaw\":%.9g,\"t\":", yaw); PVs(targets); std::printf("}\n");
    }
    // Begin's assignment: location x town busy x mode
    for (int c = 0; c < 240; ++c)
    {
        const int loc = c % 3;
        const bool busy = ((c / 3) % 2) == 1;
        const int mode = static_cast<int>((c / 6) % 3);
        int a = 1 + static_cast<int>(R() % 9);
        int b = 1 + static_cast<int>(R() % 9);
        if (c % 17 == 0) { a = 12 + static_cast<int>(R() % 10); b = 12 + static_cast<int>(R() % 10); }
        std::vector<MatchRules::MatchTeam> t = Teams(a, b, 0);
        const float yaw = Yaw();
        Building site = MakeSite(yaw);
        g_locationMode = static_cast<ArenaIngress::LocationMode>(loc);
        TownArena::g_busy = busy;
        g_targets.clear();
        ArenaIngress::SiteAssignment(static_cast<MatchRules::MatchMode>(mode), &t[0], static_cast<int>(t.size()), &site);
        Line("site"); std::printf(",\"loc\":%d,\"busy\":%d,\"mode\":%d,\"teams\":", loc, busy ? 1 : 0, mode); PTeams(t);
        std::printf(",\"pos\":"); PV(site.pos); std::printf(",\"yaw\":%.9g,\"t\":", yaw); PVs(g_targets); std::printf("}\n");
    }
    // BeginWalkIn's target: location x team x yaw (BeginWalkIn refuses teams other than A and B before this point)
    for (int c = 0; c < 72; ++c)
    {
        const int loc = c % 3;
        const int team = 1 + (c / 3) % 2;
        const float yaw = (c < 24) ? kYaws[(c / 6) % kYawCount] : Yaw();
        Building site = MakeSite(yaw);
        g_locationMode = static_cast<ArenaIngress::LocationMode>(loc);
        g_walkInTarget = Ogre::Vector3(-1.0f, -1.0f, -1.0f);
        ArenaIngress::WalkInTarget(static_cast<MatchRules::MatchTeam>(team), &site);
        Line("walkin"); std::printf(",\"loc\":%d,\"team\":%d,\"pos\":", loc, team); PV(site.pos);
        std::printf(",\"yaw\":%.9g,\"out\":", yaw); PV(g_walkInTarget); std::printf("}\n");
    }
    // Escorts: gather base (toward the Registry, banner fallback, local fallback) and one move per valid escort
    for (int c = 0; c < 140; ++c)
    {
        const int loc = c % 3;
        const unsigned regKind = (c / 3) % 5;   // 0 none, 1 valid far, 2 invalid, 3 valid on the site (lenSq <= 1), 4 valid ~1 unit away
        const bool siteNull = (c % 23) == 22;
        const float yaw = Yaw();
        Building site = MakeSite(yaw);
        Building reg;
        reg.pos = Pos();
        if (regKind == 3) reg.pos = Ogre::Vector3(site.pos.x + 0.5f, site.pos.y + 40.0f, site.pos.z - 0.5f);
        if (regKind == 4) reg.pos = Ogre::Vector3(site.pos.x + 0.8f, site.pos.y, site.pos.z + 0.7f);
        if (regKind == 1 && R() % 2 == 0)
        {
            const float dx = RF(-3000, 3000, 1);
            const float dy = RF(-50, 50, 1);
            const float dz = RF(-3000, 3000, 1);
            reg.pos = Ogre::Vector3(site.pos.x + dx, site.pos.y + dy, site.pos.z + dz);
        }
        reg.valid = regKind != 2;
        g_stubRegistry = (regKind == 0) ? NULL : &reg;
        g_locationMode = static_cast<ArenaIngress::LocationMode>(loc);
        const int n = static_cast<int>(R() % 11);
        std::vector<Character> escorts(static_cast<size_t>(n > 0 ? n : 1));
        std::vector<Character*> ptrs;
        for (int i = 0; i < n; ++i)
        {
            escorts[static_cast<size_t>(i)].valid = (R() % 6) != 0;
            ptrs.push_back(&escorts[static_cast<size_t>(i)]);
        }
        if (n > 0 && R() % 7 == 0) ptrs[R() % static_cast<unsigned>(n)] = NULL;
        g_moveWho.clear(); g_moveTo.clear(); g_moveBase = &escorts[0];
        Building* sp = siteNull ? NULL : &site;
        const Ogre::Vector3 base = EscortGatherBase(sp);
        IssueEscortMoves(sp, ptrs.empty() ? NULL : &ptrs[0], n);
        Line("escort"); std::printf(",\"loc\":%d,\"site\":%d,\"pos\":", loc, siteNull ? 0 : 1); PV(site.pos);
        std::printf(",\"yaw\":%.9g,\"reg\":%d,\"rpos\":", yaw, (regKind == 0) ? 0 : (reg.valid ? 1 : 2)); PV(reg.pos);
        std::printf(",\"esc\":[");
        for (int i = 0; i < n; ++i)
        {
            const int state = ptrs[static_cast<size_t>(i)] == NULL ? 0 : (ptrs[static_cast<size_t>(i)]->valid ? 1 : 2);
            std::printf("%s%d", i ? "," : "", state);
        }
        std::printf("],\"base\":"); PV(base); std::printf(",\"who\":[");
        for (size_t i = 0; i < g_moveWho.size(); ++i) std::printf("%s%d", i ? "," : "", g_moveWho[i]);
        std::printf("],\"to\":"); PVs(g_moveTo); std::printf("}\n");
    }
    g_stubRegistry = NULL;
    // RegistryApproachPoint: from null / invalid / valid; the marker at the centre, within 1 unit, far
    for (int c = 0; c < 90; ++c)
    {
        const unsigned fromKind = c % 3;   // 0 null, 1 invalid, 2 valid
        const unsigned markerKind = (c / 3) % 3;
        const float yaw = Yaw();
        Building b = MakeSite(yaw);
        if (markerKind == 0) b.marker = b.pos;
        else if (markerKind == 1) b.marker = Ogre::Vector3(b.pos.x + 0.5f, b.pos.y + 0.5f, b.pos.z - 0.5f);
        else
        {
            const float dx = RF(-400, 400, 1);
            const float dy = RF(-20, 20, 1);
            const float dz = RF(-400, 400, 1);
            b.marker = Ogre::Vector3(b.pos.x + dx, b.pos.y + dy, b.pos.z + dz);
        }
        Character from;
        from.valid = fromKind == 2;
        const Ogre::Vector3 out = RegistryApproachPoint(&b, fromKind == 0 ? NULL : &from);
        Line("regnode"); std::printf(",\"from\":%u,\"pos\":", fromKind); PV(b.pos); std::printf(",\"yaw\":%.9g,\"marker\":", yaw); PV(b.marker);
        std::printf(",\"out\":"); PV(out); std::printf("}\n");
    }
    // FaceOpposingFormation over real formations (and a mismatched teams list), each fighter at, near or far from its mark
    for (int c = 0; c < 160; ++c)
    {
        const int mode = static_cast<int>(R() % 3);
        int a = static_cast<int>(R() % 6);
        int b = static_cast<int>(R() % 6);
        if (c % 9 == 0) b = 0;
        if (a + b == 0) a = 1;
        std::vector<MatchRules::MatchTeam> t = Teams(a, b, (R() % 6 == 0) ? 1 : 0);
        const float yaw = Yaw();
        Building site = MakeSite(yaw);
        std::vector<Ogre::Vector3> targets;
        AssignArenaTargets(static_cast<MatchRules::MatchMode>(mode), &t[0], static_cast<int>(t.size()), &site, targets, 0.70f);
        if (c % 13 == 5) { for (size_t i = 0; i < targets.size(); ++i) targets[i] = site.pos; }   // every mark on one point
        std::vector<Character> fighters(t.size());
        g_fighters.clear();
        for (size_t i = 0; i < t.size(); ++i) g_fighters.push_back(&fighters[i]);
        g_teams = t;
        if (c % 31 == 30) g_teams.pop_back();
        g_targets = targets;
        g_mode = static_cast<MatchRules::MatchMode>(mode);
        const size_t index = R() % t.size();
        const unsigned where = R() % 4;
        Ogre::Vector3 p = targets[index];
        if (where >= 1)
        {
            const int span = (where == 1) ? 40 : 500;
            const int div = (where == 1) ? 100 : 1;
            const float dx = RF(-span, span, div);
            const float dy = RF(-300, 300, 1);
            const float dz = RF(-span, span, div);
            p = Ogre::Vector3(p.x + dx, p.y + dy, p.z + dz);
        }
        fighters[index].pos = p;
        FaceOpposingFormation(index);
        Line("face"); std::printf(",\"mode\":%d,\"teams\":", mode); PTeams(g_teams); std::printf(",\"t\":"); PVs(targets);
        std::printf(",\"i\":%d,\"p\":", static_cast<int>(index)); PV(p);
        std::printf(",\"looked\":%d,\"face\":", fighters[index].looked ? 1 : 0); PV(fighters[index].lookedAt); std::printf("}\n");
    }

    std::printf("]\n");
    return 0;
}
