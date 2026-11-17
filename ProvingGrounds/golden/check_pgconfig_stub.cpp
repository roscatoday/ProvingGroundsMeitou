// Not a generator (gen.sh runs every gen_*.cpp; this one is built and run first). Proves golden/stubs/pgconfig_stub.cpp links against the
// real PGConfig.h declarations and behaves as the original does with no pg_config.json. Expected numbers are worked out by hand from
// PGConfig.cpp:931-943 (ScaleMarks) and the shipped values at PGConfig.cpp:39,44,49,54. Exit code 1 on the first mismatch.
#include "pgconfig_stub.h"
#include <climits>
#include <cstdio>
static int bad = 0;
static void Eq(long long expected, long long actual, const char* what)
{
    if (expected != actual) { std::printf("stub check FAILED: %s expected %lld got %lld\n", what, expected, actual); ++bad; }
}
int main()
{
    PGConfigStub_Reset();
    const PGConfig::Recruitment& rec = PGConfig::RecruitmentValues();
    Eq(25, rec.minimum, "recruitment.minimum"); Eq(250, rec.maximum, "recruitment.maximum"); Eq(5, rec.step, "recruitment.step");
    Eq(250, (long long)(rec.combatScale * 100.0), "recruitment.combatScale*100");
    Eq(10, PGConfig::BookieValues().minimum, "bookie.minimum"); Eq(10000, PGConfig::BookieValues().maximum, "bookie.maximum");
    const PGConfig::Challenges& ch = PGConfig::ChallengeValues();
    Eq(PGConfig::ChallengeNormal, ch.difficulty, "challenges.difficulty"); Eq(12, ch.refreshHours, "refreshHours");
    Eq(500, ch.refreshBaseCostCats, "refreshBaseCostCats"); Eq(200, (long long)(ch.refreshCostMultiplier * 100.0), "refreshCostMultiplier*100");
    Eq(24, ch.cooldownHours, "cooldownHours"); Eq(1, (long long)ch.winMarksMultiplier, "winMarksMultiplier");
    Eq(1, (long long)ch.uniqueBonusMultiplier, "uniqueBonusMultiplier"); Eq(1, (long long)ch.skarnBonusMultiplier, "skarnBonusMultiplier");
    Eq(500, PGConfig::ChallengeBase(0), "base 0"); Eq(1000, PGConfig::ChallengeBase(1), "base 1"); Eq(2500, PGConfig::ChallengeBase(2), "base 2");
    Eq(500, PGConfig::ChallengeBase(-1), "base -1 falls back to 0"); Eq(500, PGConfig::ChallengeBase(3), "base 3 falls back to 0");
    Eq(10, PGConfig::kStakeStep, "kStakeStep");
    // shipped multiplier 1.0: every price is returned untouched
    Eq(37, PGConfig::ScaleMarks(37), "ScaleMarks(37) at 1.0"); Eq(-5, PGConfig::ScaleMarks(-5), "ScaleMarks(-5) at 1.0 (no clamp on this path)");
    Eq(120, PGConfig::GearCost("any", 2, 120), "GearCost at 1.0"); Eq(7, PGConfig::SupplyCost("any", 7), "SupplyCost at 1.0");
    Eq(300, PGConfig::LicenceCost("any", 300), "LicenceCost at 1.0"); Eq(300, PGConfig::GearCost(0, 9, 300), "GearCost null id, bad tier");
    // 0.5: 25*0.5=12.5 -> 13 (half up), 1*0.5=0.5 -> 1, 0 -> 0, negative -> 0
    PGConfigStub_SetMarksMultiplier(0.5);
    Eq(13, PGConfig::ScaleMarks(25), "ScaleMarks(25) at 0.5"); Eq(1, PGConfig::ScaleMarks(1), "ScaleMarks(1) at 0.5");
    Eq(0, PGConfig::ScaleMarks(0), "ScaleMarks(0) at 0.5"); Eq(0, PGConfig::ScaleMarks(-4), "ScaleMarks(-4) at 0.5");
    Eq(60, PGConfig::GearCost("any", 0, 120), "GearCost(120) at 0.5"); Eq(4, PGConfig::SupplyCost("any", 7), "SupplyCost(7) at 0.5 = 3.5 -> 4");
    // 2.5: 25*2.5=62.5 -> 63, 1*2.5 = 2.5 -> 3, INT_MAX saturates
    PGConfigStub_SetMarksMultiplier(2.5);
    Eq(63, PGConfig::ScaleMarks(25), "ScaleMarks(25) at 2.5"); Eq(3, PGConfig::ScaleMarks(1), "ScaleMarks(1) at 2.5");
    Eq(INT_MAX, PGConfig::ScaleMarks(INT_MAX), "ScaleMarks(INT_MAX) at 2.5"); Eq(750, PGConfig::LicenceCost("any", 300), "LicenceCost(300) at 2.5");
    // the mutable views and the reset
    PGConfigStub_Challenges().skarnBonusMultiplier = 2.0; PGConfigStub_Bookie().maximum = 500; PGConfigStub_SetChallengeBase(2, 3000);
    Eq(2, (long long)PGConfig::ChallengeValues().skarnBonusMultiplier, "mutable challenges"); Eq(500, PGConfig::BookieValues().maximum, "mutable bookie");
    Eq(3000, PGConfig::ChallengeBase(2), "SetChallengeBase");
    PGConfigStub_Reset();
    Eq(1, (long long)PGConfig::ChallengeValues().skarnBonusMultiplier, "reset challenges"); Eq(10000, PGConfig::BookieValues().maximum, "reset bookie");
    Eq(2500, PGConfig::ChallengeBase(2), "reset base"); Eq(37, PGConfig::ScaleMarks(37), "reset multiplier");
    if (bad) return 1;
    std::printf("golden: pgconfig stub ok\n");
    return 0;
}
