// Stand-in for PGConfig.cpp (which needs <Windows.h>, rapidjson and PGLog -> KenshiLib's Debug.h, none of which the source release
// ships or g++ can use). Link it, with the REAL PGConfig.h, into any generator whose sources call PGConfig::.
// Models the shipped state: no pg_config.json, so the catalogue override maps are empty and every value is the shipped default.
//
// PGConfig:: functions the pure upstream files call (grep over *.h and the pure *.cpp at upstream 4a20d48):
//   ChallengeValues     ArenaMarks.cpp:125-126 (glue callers: TownArena.cpp, TownArenaUI.cpp)             IMPLEMENTED
//   BookieValues        TownBettingPolicy.h:41,89,106                                                     IMPLEMENTED
//   ChallengeBase       TownChallengeBuyInPolicy.h:32                                                     IMPLEMENTED
//   GearCost            ArenaRewards.cpp:444                                                              IMPLEMENTED
//   SupplyCost          ArenaRewards.cpp:448                                                              IMPLEMENTED
//   LicenceCost         ArenaRewards.cpp:451,453                                                          IMPLEMENTED
//   ScaleMarks          PrisonerRecruitmentLogic.cpp:26 (and every Cost above)                            IMPLEMENTED
//   RecruitmentValues   PrisonerRecruitmentLogic.cpp:15 (not in the plan's list, but that pure file will not link without it)  IMPLEMENTED
//   BookieCreditCeiling ArenaSnapshotJson.cpp:351,394 (gen_snapshot.cpp compiles it with the fetched rapidjson)               IMPLEMENTED
//   kStakeStep, ChallengeDifficulty, ChallengeNormal/Easy/Hard, the Recruitment/Bookie/Challenges structs: header only, nothing to link.
// Not implemented, not needed yet:
//   Load, SaveEditableSettings, ShippedEditableSettings, GetEditableSettings, PerformanceProfile, F8OpensDebugMenu (config file and
//   settings UI; PGConfig.cpp needs rapidjson, tested by hand in M1 task 1.6);
//   ClampStake (TownBookie.cpp:146, glue).
//   Add one here, with the PGConfig.cpp lines quoted, when a generator first needs it.
//
// Every shipped value below is copied from upstream/ProvingGrounds/plugin/src/PGConfig.cpp; the line is quoted on each.
#include "pgconfig_stub.h"
#include <climits>

namespace
{
    // PGConfig.cpp:65 `double g_multiplier = 1.0;`
    double g_multiplier = 1.0;
    // PGConfig.cpp:39 `Recruitment shipped = { 25, 250, 5, 2.5 };`  (minimum, maximum, step, combatScale)
    PGConfig::Recruitment ShippedRecruitment() { PGConfig::Recruitment r = { 25, 250, 5, 2.5 }; return r; }
    // PGConfig.cpp:44 `Bookie shipped = { 10, 10000 };`
    PGConfig::Bookie ShippedBookie() { PGConfig::Bookie b = { 10, 10000 }; return b; }
    // PGConfig.cpp:49 `Challenges shipped = { ChallengeNormal, 12, 500, 2.0, 24, 1.0, 1.0, 1.0 };`
    // (difficulty, refreshHours, refreshBaseCostCats, refreshCostMultiplier, cooldownHours, winMarksMultiplier,
    //  uniqueBonusMultiplier, skarnBonusMultiplier -- field order of PGConfig.h:67-77)
    PGConfig::Challenges ShippedChallenges()
    {
        PGConfig::Challenges c = { PGConfig::ChallengeNormal, 12, 500, 2.0, 24, 1.0, 1.0, 1.0 };
        return c;
    }
    // PGConfig.cpp:54 `return division == 0 ? 500 : division == 1 ? 1000 : 2500;`
    int ShippedChallengeBase(int division) { return division == 0 ? 500 : division == 1 ? 1000 : 2500; }

    // PGConfig.cpp:66-68 and 71-74: the globals are built from the shipped values.
    PGConfig::Recruitment g_recruitment = ShippedRecruitment();
    PGConfig::Bookie g_bookie = ShippedBookie();
    PGConfig::Challenges g_challenges = ShippedChallenges();
    int g_challengeBase[3] = { ShippedChallengeBase(0), ShippedChallengeBase(1), ShippedChallengeBase(2) };
}

namespace PGConfig
{
    // PGConfig.cpp:891-903. With no pg_config.json g_gear is empty, so the find at 895-898 always misses and the result is
    // ScaleMarks(defaultCost); the guard at 893-894 gives the same answer.
    int GearCost(const char* pieceStableId, int tier, int defaultCost)
    {
        (void)pieceStableId; (void)tier;
        return ScaleMarks(defaultCost);
    }

    // PGConfig.cpp:905-916 (g_supplies empty)
    int SupplyCost(const char* stableId, int defaultCost)
    {
        (void)stableId;
        return ScaleMarks(defaultCost);
    }

    // PGConfig.cpp:918-929 (g_licences empty)
    int LicenceCost(const char* stableId, int defaultCost)
    {
        (void)stableId;
        return ScaleMarks(defaultCost);
    }

    // PGConfig.cpp:931-943, verbatim.
    int ScaleMarks(int marks)
    {
        // Exactly the shipped price at the shipped multiplier: the default build
        // never takes a path that could re-round a value.
        if (g_multiplier == 1.0)
            return marks;
        const double scaled = static_cast<double>(marks) * g_multiplier;
        if (scaled >= static_cast<double>(INT_MAX))
            return INT_MAX;
        if (!(scaled > 0.0))
            return 0;
        return static_cast<int>(scaled + 0.5);
    }

    // PGConfig.cpp:945-958
    const Recruitment& RecruitmentValues() { return g_recruitment; }
    const Bookie& BookieValues() { return g_bookie; }
    const Challenges& ChallengeValues() { return g_challenges; }

    // PGConfig.cpp:1086-1091
    int ChallengeBase(int division)
    {
        if (division < 0 || division > 2)
            division = 0;
        return g_challengeBase[division];
    }

    // PGConfig.cpp:1102-1109, verbatim (kShippedBookieCreditCeiling PGConfig.cpp:28, kBookOddsCeiling PGConfig.cpp:29).
    int BookieCreditCeiling()
    {
        const int kShippedBookieCreditCeiling = 50000;
        const double kBookOddsCeiling = 5.0;
        // Betting is blocked while a credit is pending, so the largest credit a
        // save can hold is one winning payout at the book's odds ceiling.
        const double ceiling = static_cast<double>(g_bookie.maximum) * kBookOddsCeiling;
        return ceiling > kShippedBookieCreditCeiling ?
            static_cast<int>(ceiling + 0.5) : kShippedBookieCreditCeiling;
    }
}

void PGConfigStub_Reset()
{
    g_multiplier = 1.0;
    g_recruitment = ShippedRecruitment();
    g_bookie = ShippedBookie();
    g_challenges = ShippedChallenges();
    for (int i = 0; i < 3; ++i) g_challengeBase[i] = ShippedChallengeBase(i);
}
void PGConfigStub_SetMarksMultiplier(double multiplier) { g_multiplier = multiplier; }
PGConfig::Recruitment& PGConfigStub_Recruitment() { return g_recruitment; }
PGConfig::Bookie& PGConfigStub_Bookie() { return g_bookie; }
PGConfig::Challenges& PGConfigStub_Challenges() { return g_challenges; }
void PGConfigStub_SetChallengeBase(int division, int cats) { if (division >= 0 && division <= 2) g_challengeBase[division] = cats; }
