// sources: ArenaRewards.cpp ArenaPerformance.cpp pgconfig_stub.cpp
// PGConfig's TEXT output. PGConfig.cpp itself cannot be linked here (rapidjson, Windows.h, PGLog -> KenshiLib), so the functions
// that build text are COPIED VERBATIM below from upstream PGConfig.cpp (line ranges on each; only the file write at the end of
// WriteDefaults, :614-619, is replaced by returning the string) and PGLog::Quote from PGLog.cpp:55-64. They are linked with the REAL
// ArenaRewards.cpp (the shipped catalogue the default file lists) and ArenaPerformance.cpp, so g++'s printf produces the bytes the
// original writes for %d and %u. %g is glibc's (two-digit exponent, exact digits), NOT the shipped original's msvcr100 (three-digit
// exponent `1e+010`, 17 digits then zeros, ties half away from zero), so the golden tests run Core under CMath.UseGlibcForTest.
// The PGConfig.cpp globals the copies read (g_multiplier, g_recruitment, g_bookie, g_challengeBase, g_challenges, ...) are local
// variables of the same names here. Lines, each {"k": kind, inputs..., "text": output}:
//   "g"         FormatNumber(x)                                   PGConfig.cpp:466-473
//   "defaults"  WriteDefaults' file text, at the shipped globals and at varied ones (the catalogue is always the shipped one:
//               WriteDefaults runs before ApplyPriceOverrides)    PGConfig.cpp:475-612
//   "overlay"   FormatEditableOverlay(settings)                    PGConfig.cpp:791-821
//   "summary"   Load's summary line                                PGConfig.cpp:864-885
//   "range"     ReadWhole / OptionalNumber range texts             PGConfig.cpp:109-110, 143-144
//   "quote"     PGLog::Quote(s)                                    PGLog.cpp:55-64
#include "PGConfig.h"
#include "ArenaRewards.h"
#include "ArenaPerformance.h"
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>

namespace
{
    unsigned s_seed = 20261111u;
    unsigned R() { s_seed = s_seed * 1664525u + 1013904223u; return s_seed >> 8; }

    std::string Json(const std::string& value)
    {
        std::string out = "\"";
        for (size_t i = 0; i < value.size(); ++i)
        {
            const unsigned char c = static_cast<unsigned char>(value[i]);
            if (c == '"' || c == '\\') { out += '\\'; out += static_cast<char>(c); }
            else if (c == '\n') out += "\\n";
            else if (c == '\r') out += "\\r";
            else if (c < 32) { char e[8]; snprintf(e, sizeof e, "\\u%04x", c); out += e; }
            else out += static_cast<char>(c);
        }
        return out + "\"";
    }
}

// ---- verbatim copies -------------------------------------------------------------------------------------------------------------
namespace PGLog
{
    // PGLog.cpp:55-64
    std::string Quote(const std::string& value) {
        std::string out = "\"";
        for (size_t i = 0; i < value.size(); ++i) {
            const unsigned char c = static_cast<unsigned char>(value[i]);
            if (c == '"' || c == '\\') { out += '\\'; out += c; }
            else if (c < 32) { char escaped[7]; sprintf_s(escaped, "\\u%04x", static_cast<unsigned>(c)); out += escaped; }
            else out += c;
        }
        return out + "\"";
    }
}

namespace Copy
{
    using namespace PGConfig;
    const char* const kFileName = "pg_config.json";
    const char* const kTierNames[4] = { "standard", "high", "specialist", "masterwork" };

    double g_multiplier = 1.0;
    Recruitment g_recruitment = { 25, 250, 5, 2.5 };
    Bookie g_bookie = { 10, 10000 };
    Challenges g_challenges = { ChallengeNormal, 12, 500, 2.0, 24, 1.0, 1.0, 1.0 };
    ArenaPerformance::Profile g_performanceProfile = ArenaPerformance::Normal;
    bool g_f8OpensDebugMenu = false;
    int g_challengeBase[3] = { 500, 1000, 2500 };
    int g_rejected = 0;
    int g_appliedGear = 0;
    int g_appliedSupplies = 0;
    int g_appliedLicences = 0;

    // PGConfig.cpp:466-473
        void FormatNumber(char* out, size_t size, double value)
        {
            sprintf_s(out, size, "%g", value);
            // A whole number would print as "1", which reads as an integer in a
            // file where the user is about to type fractional values.
            if (strchr(out, '.') == NULL && strchr(out, 'e') == NULL)
                strcat_s(out, size, ".0");
        }

    // PGConfig.cpp:475-512
        void AppendGearSection(std::string& out, char* line,
            const char* sectionName, bool weapons)
        {
            out += "    \"";
            out += sectionName;
            out += "\": {\n";
            int itemCount = 0;
            for (int index = 0; index < static_cast<int>(ArenaRewards::PieceCount); ++index)
            {
                const ArenaRewards::Piece piece = static_cast<ArenaRewards::Piece>(index);
                if (ArenaRewards::IsWeapon(piece) == weapons && ArenaRewards::TiersFor(piece) > 0)
                    ++itemCount;
            }
            int emitted = 0;
            for (int index = 0; index < static_cast<int>(ArenaRewards::PieceCount); ++index)
            {
                const ArenaRewards::Piece piece = static_cast<ArenaRewards::Piece>(index);
                const int tiers = ArenaRewards::TiersFor(piece);
                if (tiers <= 0 || ArenaRewards::IsWeapon(piece) != weapons)
                    continue;
                ++emitted;
                const int first = static_cast<int>(ArenaRewards::FirstTier(piece));
                out += "        ";
                out += PGLog::Quote(ArenaRewards::StableId(piece));
                out += ": {\n";
                for (int offset = 0; offset < tiers; ++offset)
                {
                    const int tier = first + offset;
                    sprintf_s(line, 2048, "            \"%s\": %d%s\n",
                        kTierNames[tier],
                        ArenaRewards::Get(piece, static_cast<ArenaRewards::Tier>(tier)).cost,
                        offset + 1 < tiers ? "," : "");
                    out += line;
                }
                out += emitted < itemCount ? "        },\n" : "        }\n";
            }
            out += "    },\n";
        }

    // PGConfig.cpp:517-612 (the file write at :614-619 replaced by returning the text)
        std::string WriteDefaultsText()
        {
            std::string out;
            // The challenge settings block is now the longest formatted line (over 1 KiB).
            char line[2048];
            char number[32];

            out += "{\n    \"version\": 1,\n";
            out += "    // Prices and Marks costs\n";
            FormatNumber(number, sizeof(number), g_multiplier);
            sprintf_s(line, "    \"marks_multiplier\": %s,\n", number);
            out += line;

            AppendGearSection(out, line, "armour", false);
            AppendGearSection(out, line, "weapons", true);

            out += "    \"supplies\": {\n";
            for (int item = 0; item < static_cast<int>(ArenaRewards::GeneralItemCount); ++item)
            {
                const ArenaRewards::GeneralReward& reward = ArenaRewards::GetGeneral(
                    static_cast<ArenaRewards::GeneralItem>(item));
                sprintf_s(line, "        %s: %d%s\n", PGLog::Quote(reward.stableId).c_str(),
                    reward.cost,
                    item + 1 < static_cast<int>(ArenaRewards::GeneralItemCount) ? "," : "");
                out += line;
            }
            out += "    },\n";

            const ArenaRewards::ArmourSet kSets[3] =
            {
                ArenaRewards::ArmourSetT1, ArenaRewards::ArmourSetT2, ArenaRewards::ArmourSetT3
            };
            out += "    \"licences\": {\n";
            for (int set = 0; set < 3; ++set)
            {
                const ArenaRewards::Licence& licence = ArenaRewards::GetLicence(kSets[set]);
                sprintf_s(line, "        %s: %d,\n",
                    PGLog::Quote(licence.stableId).c_str(), licence.cost);
                out += line;
            }
            const ArenaRewards::Licence& weapons = ArenaRewards::GetWeaponLicence();
            sprintf_s(line, "        %s: %d\n", PGLog::Quote(weapons.stableId).c_str(),
                weapons.cost);
            out += line;
            out += "    },\n";

            FormatNumber(number, sizeof(number), g_recruitment.combatScale);
            sprintf_s(line, "    \"recruitment\": {\n"
                "        \"minimum\": %d,\n"
                "        \"maximum\": %d,\n"
                "        \"step\": %d,\n"
                "        \"combat_scale\": %s\n"
                "    },\n",
                g_recruitment.minimum, g_recruitment.maximum, g_recruitment.step, number);
            out += line;
            sprintf_s(line, "    \"bookie\": {\n"
                "        \"minimum\": %d,\n"
                "        \"maximum\": %d\n"
                "    },\n",
                g_bookie.minimum, g_bookie.maximum);
            out += line;
            sprintf_s(line, "    // Cats paid to book a challenge\n"
                "    \"challenge_buy_in\": {\n"
                "        \"easy\": %d,\n"
                "        \"medium\": %d,\n"
                "        \"hard\": %d\n"
                "    },\n"
                "    // Matchmaking and rewards for player challenges\n"
                "    \"challenges\": {\n"
                "        // easy = weaker opponents, normal = shipped balance, hard = stronger opponents\n"
                "        \"difficulty\": \"normal\",\n"
                "        // Challenge offer refresh interval in in-game hours; 12 keeps the shipped 06:00 / 18:00 schedule\n"
                "        \"refresh_hours\": 12,\n"
                "        // First paid refresh costs this many Cats (1-1,000,000)\n"
                "        \"refresh_base_cost_cats\": 500,\n"
                "        // Price multiplier per paid refresh (1.0-10.0); whole-Cat prices round to nearest; 2.0 doubles\n"
                "        \"refresh_cost_multiplier\": 2.0,\n"
                "        // In-game hours after a Skarn challenge attempt before another Skarn challenge; 0 disables the cooldown\n"
                "        \"cooldown_hours\": 24,\n"
                "        \"marks\": {\n"
                "            // Multipliers: 1.0 keeps the shipped reward\n"
                "            \"win_multiplier\": 1.0,\n"
                "            // Unique-fighter bonus: Ressa Vane 20, Bum-Per 40, Senn / Torka / Veyr 50 Marks\n"
                "            \"unique_bonus_multiplier\": 1.0,\n"
                "            // Skarn victory bonus: 100 Marks\n"
                "            \"skarn_bonus_multiplier\": 1.0\n"
                "        }\n"
                "    },\n",
                g_challengeBase[0], g_challengeBase[1], g_challengeBase[2]);
            out += line;
            out += "    // Arena performance and plugin UI\n"
                "    \"ui_settings\": {\n"
                "        \"performance_profile\": \"normal\",\n"
                "        // true lets F8 toggle the debug menu; false disables the F8 action\n"
                "        \"f8_opens_debug_menu\": false\n"
                "    }\n}\n";
            return out;
        }

    // PGConfig.cpp:791-821
        bool FormatEditableOverlay(const EditableSettings& settings, std::string& out)
        {
            char number[32];
            char line[2048];
            FormatNumber(number, sizeof(number), settings.recruitment.combatScale);
            const char* difficulty = settings.challenges.difficulty == ChallengeEasy ? "easy" :
                settings.challenges.difficulty == ChallengeHard ? "hard" : "normal";
            char refreshScale[32], winMarks[32], uniqueMarks[32], skarnMarks[32];
            const char* performanceProfile = ArenaPerformance::PersistedName(settings.performanceProfile);
            FormatNumber(refreshScale, sizeof(refreshScale), settings.challenges.refreshCostMultiplier);
            FormatNumber(winMarks, sizeof(winMarks), settings.challenges.winMarksMultiplier);
            FormatNumber(uniqueMarks, sizeof(uniqueMarks), settings.challenges.uniqueBonusMultiplier);
            FormatNumber(skarnMarks, sizeof(skarnMarks), settings.challenges.skarnBonusMultiplier);
            sprintf_s(line, "{\n"
                "        \"recruitment\": {\"minimum\": %d, \"maximum\": %d, \"step\": %d, \"combat_scale\": %s},\n"
                "        \"bookie\": {\"minimum\": %d, \"maximum\": %d},\n"
                "        \"challenge_buy_in\": {\"easy\": %d, \"medium\": %d, \"hard\": %d},\n"
                "        \"challenges\": {\"difficulty\": \"%s\", \"refresh_hours\": %d, \"refresh_base_cost_cats\": %d, \"refresh_cost_multiplier\": %s, \"cooldown_hours\": %d, \"marks\": {\"win_multiplier\": %s, \"unique_bonus_multiplier\": %s, \"skarn_bonus_multiplier\": %s}},\n"
                "        \"performance_profile\": \"%s\",\n"
                "        // true lets F8 toggle the debug menu; false disables the F8 action\n"
                "        \"f8_opens_debug_menu\": %s\n"
                "    }",
                settings.recruitment.minimum, settings.recruitment.maximum, settings.recruitment.step, number,
                settings.bookie.minimum, settings.bookie.maximum,
                settings.challengeBase[0], settings.challengeBase[1], settings.challengeBase[2],
                difficulty, settings.challenges.refreshHours, settings.challenges.refreshBaseCostCats,
                refreshScale, settings.challenges.cooldownHours, winMarks, uniqueMarks, skarnMarks,
                performanceProfile, settings.f8OpensDebugMenu ? "true" : "false");
            out = line;
            return true;
        }

    // PGConfig.cpp:864-885 (Narrow(path) replaced by the path itself: the C# glue passes a .NET string)
        std::string Summary(const char* origin, const std::string& path)
        {
        char summary[768];
        const char* const difficultyName = g_challenges.difficulty == ChallengeEasy ? "easy" :
            g_challenges.difficulty == ChallengeHard ? "hard" : "normal";
        sprintf_s(summary,
            "Proving Grounds: %s %s (%s); applied catalogue=%d supplies=%d licences=%d marks_multiplier=%g; "
            "recruitment=%d/%d/%d combat_scale=%g; bookie=%d/%d; challenge_base=%d/%d/%d; "
            "challenge_difficulty=%s refresh_hours=%d refresh_base_cats=%d refresh_multiplier=%g "
            "cooldown_hours=%d marks=%g/%g/%g; performance=%s f8_debug=%s; rejected=%d",
            kFileName,
            origin,
            path.c_str(),
            g_appliedGear, g_appliedSupplies, g_appliedLicences, g_multiplier,
            g_recruitment.minimum, g_recruitment.maximum, g_recruitment.step,
            g_recruitment.combatScale,
            g_bookie.minimum, g_bookie.maximum,
            g_challengeBase[0], g_challengeBase[1], g_challengeBase[2],
            difficultyName, g_challenges.refreshHours, g_challenges.refreshBaseCostCats,
            g_challenges.refreshCostMultiplier, g_challenges.cooldownHours, g_challenges.winMarksMultiplier,
            g_challenges.uniqueBonusMultiplier, g_challenges.skarnBonusMultiplier,
            ArenaPerformance::PersistedName(g_performanceProfile),
            g_f8OpensDebugMenu ? "enabled" : "disabled",
            g_rejected);
        return summary;
        }

    // PGConfig.cpp:109-110
    std::string RangeWhole(int minimum, int maximum)
    {
                char range[80];
                sprintf_s(range, "expected %d to %d", minimum, maximum);
                return range;
    }
    // PGConfig.cpp:143-144
    std::string RangeNumber(double minimum, double maximum)
    {
                char range[96];
                sprintf_s(range, "expected %g to %g", minimum, maximum);
                return range;
    }
}
// ---- end of copies -----------------------------------------------------------------------------------------------------------------

namespace
{
    bool s_first = true;
    void Line(const std::string& body)
    {
        printf("%s%s\n", s_first ? "" : ",", body.c_str());
        s_first = false;
    }
    std::string D(double x) { char b[40]; snprintf(b, sizeof b, "%.17g", x); return b; }
    std::string I(long long x) { return std::to_string(x); }

    // a double picked from a spread of magnitudes and fractions
    double PickDouble()
    {
        const unsigned kind = R() % 8;
        const double u = static_cast<double>(R() % 1000000) / 1000000.0;
        switch (kind)
        {
        case 0: return static_cast<double>(R() % 101);                   // whole
        case 1: return std::floor(u * 10000.0) / 100.0;                  // two decimals
        case 2: return u * 100.0;                                        // long fraction
        case 3: return u * 1e-4;                                         // tiny (exponent form)
        case 4: return 1.0 + u * 9.0;                                    // 1..10
        case 5: return static_cast<double>(R() % 100000000) / 7.0;       // big
        case 6: return std::floor(u * 1000.0) / 8.0;                     // binary fractions (ties in %g rounding)
        default: return -u * 50.0;                                       // negative
        }
    }

    PGConfig::EditableSettings RandomSettings()
    {
        PGConfig::EditableSettings s;
        s.recruitment.minimum = static_cast<int>(R() % 2000);
        s.recruitment.maximum = static_cast<int>(R() % 1000001);
        s.recruitment.step = 1 + static_cast<int>(R() % 100);
        s.recruitment.combatScale = PickDouble();
        s.bookie.minimum = static_cast<int>(R() % 100) * 10;
        s.bookie.maximum = static_cast<int>(R() % 100000) * 10;
        s.challenges.difficulty = static_cast<PGConfig::ChallengeDifficulty>(R() % 3);
        s.challenges.refreshHours = 1 + static_cast<int>(R() % 168);
        s.challenges.refreshBaseCostCats = 1 + static_cast<int>(R() % 1000000);
        s.challenges.refreshCostMultiplier = PickDouble();
        s.challenges.cooldownHours = static_cast<int>(R() % 721);
        s.challenges.winMarksMultiplier = PickDouble();
        s.challenges.uniqueBonusMultiplier = PickDouble();
        s.challenges.skarnBonusMultiplier = PickDouble();
        for (int i = 0; i < 3; ++i) s.challengeBase[i] = static_cast<int>(R() % 1000001);
        s.performanceProfile = static_cast<ArenaPerformance::Profile>(R() % 3);
        s.f8OpensDebugMenu = (R() & 1) != 0;
        return s;
    }

    std::string SettingsJson(const PGConfig::EditableSettings& s)
    {
        return "\"rmin\":" + I(s.recruitment.minimum) + ",\"rmax\":" + I(s.recruitment.maximum) + ",\"rstep\":" + I(s.recruitment.step) +
            ",\"rscale\":" + D(s.recruitment.combatScale) + ",\"bmin\":" + I(s.bookie.minimum) + ",\"bmax\":" + I(s.bookie.maximum) +
            ",\"diff\":" + I(s.challenges.difficulty) + ",\"rh\":" + I(s.challenges.refreshHours) + ",\"rbc\":" + I(s.challenges.refreshBaseCostCats) +
            ",\"rcm\":" + D(s.challenges.refreshCostMultiplier) + ",\"cd\":" + I(s.challenges.cooldownHours) +
            ",\"win\":" + D(s.challenges.winMarksMultiplier) + ",\"uniq\":" + D(s.challenges.uniqueBonusMultiplier) +
            ",\"skarn\":" + D(s.challenges.skarnBonusMultiplier) + ",\"base\":[" + I(s.challengeBase[0]) + "," + I(s.challengeBase[1]) + "," +
            I(s.challengeBase[2]) + "],\"perf\":" + I(s.performanceProfile) + ",\"f8\":" + (s.f8OpensDebugMenu ? "true" : "false");
    }

    PGConfig::EditableSettings Shipped()
    {
        PGConfig::EditableSettings s;
        s.recruitment = Copy::g_recruitment; s.bookie = Copy::g_bookie; s.challenges = Copy::g_challenges;
        for (int i = 0; i < 3; ++i) s.challengeBase[i] = Copy::g_challengeBase[i];
        s.performanceProfile = ArenaPerformance::Normal; s.f8OpensDebugMenu = false;
        return s;
    }
}

int main()
{
    printf("[\n");

    // "g": FormatNumber
    const double fixedG[] = { 0.0, -0.0, 1.0, 2.5, 2.0, 0.1, 0.5, 100.0, 99.99, 1e-5, 1e-4, 0.0001234, 0.00001234, 123456.0,
        999999.0, 999999.5, 1000000.0, 1234567.0, 1e15, 1.5e300, 0.125, 0.0625, 2.0000005, 2.0000015, 12.3456789, -1.0, -0.001,
        1e-300, 4.9406564584124654e-324, 1.7976931348623157e308, 3.14159265358979, 10.0, 1e100, 0.30000000000000004 };
    for (size_t i = 0; i < sizeof(fixedG) / sizeof(fixedG[0]); ++i)
    {
        char number[32];
        Copy::FormatNumber(number, sizeof(number), fixedG[i]);
        Line("{\"k\":\"g\",\"x\":" + D(fixedG[i]) + ",\"neg\":" + (std::signbit(fixedG[i]) ? "true" : "false") + ",\"text\":" + Json(number) + "}");
    }
    for (int i = 0; i < 200; ++i)
    {
        const double x = PickDouble();
        char number[32];
        Copy::FormatNumber(number, sizeof(number), x);
        Line("{\"k\":\"g\",\"x\":" + D(x) + ",\"neg\":" + (std::signbit(x) ? "true" : "false") + ",\"text\":" + Json(number) + "}");
    }

    // "defaults": the shipped globals, then two varied ones
    Line("{\"k\":\"defaults\",\"m\":" + D(Copy::g_multiplier) + ",\"rmin\":25,\"rmax\":250,\"rstep\":5,\"rscale\":2.5,\"bmin\":10,\"bmax\":10000,"
        "\"base\":[500,1000,2500],\"text\":" + Json(Copy::WriteDefaultsText()) + "}");
    for (int v = 0; v < 2; ++v)
    {
        Copy::g_multiplier = v == 0 ? 0.75 : 1e-5;
        Copy::g_recruitment.minimum = v == 0 ? 0 : 7; Copy::g_recruitment.maximum = v == 0 ? 1000000 : 9;
        Copy::g_recruitment.step = v == 0 ? 1 : 3; Copy::g_recruitment.combatScale = v == 0 ? 3.0 : 0.1;
        Copy::g_bookie.minimum = v == 0 ? 0 : 20; Copy::g_bookie.maximum = v == 0 ? 990 : 1000000;
        Copy::g_challengeBase[0] = v == 0 ? 0 : 200; Copy::g_challengeBase[1] = 1234; Copy::g_challengeBase[2] = v == 0 ? 1000000 : 2500;
        Line("{\"k\":\"defaults\",\"m\":" + D(Copy::g_multiplier) + ",\"rmin\":" + I(Copy::g_recruitment.minimum) + ",\"rmax\":" +
            I(Copy::g_recruitment.maximum) + ",\"rstep\":" + I(Copy::g_recruitment.step) + ",\"rscale\":" + D(Copy::g_recruitment.combatScale) +
            ",\"bmin\":" + I(Copy::g_bookie.minimum) + ",\"bmax\":" + I(Copy::g_bookie.maximum) + ",\"base\":[" + I(Copy::g_challengeBase[0]) +
            "," + I(Copy::g_challengeBase[1]) + "," + I(Copy::g_challengeBase[2]) + "],\"text\":" + Json(Copy::WriteDefaultsText()) + "}");
    }

    // "overlay": shipped settings, then random ones
    {
        std::string out;
        PGConfig::EditableSettings s = Shipped();
        Copy::FormatEditableOverlay(s, out);
        Line("{\"k\":\"overlay\"," + SettingsJson(s) + ",\"text\":" + Json(out) + "}");
    }
    for (int i = 0; i < 40; ++i)
    {
        std::string out;
        PGConfig::EditableSettings s = RandomSettings();
        Copy::FormatEditableOverlay(s, out);
        Line("{\"k\":\"overlay\"," + SettingsJson(s) + ",\"text\":" + Json(out) + "}");
    }

    // "summary": random globals, each origin
    const char* const origins[4] = { "could not be read, shipped prices used", "read", "written from the shipped defaults",
        "absent and not writable, shipped prices used" };
    for (int i = 0; i < 12; ++i)
    {
        PGConfig::EditableSettings s = i == 0 ? Shipped() : RandomSettings();
        Copy::g_multiplier = i == 0 ? 1.0 : PickDouble();
        Copy::g_recruitment = s.recruitment; Copy::g_bookie = s.bookie; Copy::g_challenges = s.challenges;
        for (int d = 0; d < 3; ++d) Copy::g_challengeBase[d] = s.challengeBase[d];
        Copy::g_performanceProfile = s.performanceProfile; Copy::g_f8OpensDebugMenu = s.f8OpensDebugMenu;
        Copy::g_appliedGear = static_cast<int>(R() % 100); Copy::g_appliedSupplies = static_cast<int>(R() % 10);
        Copy::g_appliedLicences = static_cast<int>(R() % 5); Copy::g_rejected = static_cast<int>(R() % 4);
        const char* origin = origins[i % 4];
        const std::string path = "C:\\Mods\\Proving Grounds\\pg_config.json";
        Line("{\"k\":\"summary\",\"m\":" + D(Copy::g_multiplier) + "," + SettingsJson(s) + ",\"ag\":" + I(Copy::g_appliedGear) +
            ",\"as\":" + I(Copy::g_appliedSupplies) + ",\"al\":" + I(Copy::g_appliedLicences) + ",\"rej\":" + I(Copy::g_rejected) +
            ",\"origin\":" + Json(origin) + ",\"path\":" + Json(path) + ",\"text\":" + Json(Copy::Summary(origin, path)) + "}");
    }

    // "range": every range PGConfig.cpp passes, plus a few more
    const int wholes[][2] = { { 0, 10000000 }, { 0, 1000000 }, { 1, 1000000 }, { 1, 168 }, { 0, 720 }, { -5, 5 } };
    for (size_t i = 0; i < sizeof(wholes) / sizeof(wholes[0]); ++i)
        Line("{\"k\":\"range\",\"whole\":true,\"min\":" + I(wholes[i][0]) + ",\"max\":" + I(wholes[i][1]) + ",\"text\":" +
            Json(Copy::RangeWhole(wholes[i][0], wholes[i][1])) + "}");
    const double numbers[][2] = { { 0.0, 100.0 }, { 1.0, 10.0 }, { 0.5, 2.25 }, { 1e-5, 1e7 } };
    for (size_t i = 0; i < sizeof(numbers) / sizeof(numbers[0]); ++i)
        Line("{\"k\":\"range\",\"whole\":false,\"min\":" + D(numbers[i][0]) + ",\"max\":" + D(numbers[i][1]) + ",\"text\":" +
            Json(Copy::RangeNumber(numbers[i][0], numbers[i][1])) + "}");

    // "quote"
    const char* const quotes[] = { "armour.pitfighter", "a\"b", "back\\slash", "tab\there", "nl\nx", "\x01\x1f", "ünï", "" };
    for (size_t i = 0; i < sizeof(quotes) / sizeof(quotes[0]); ++i)
        Line("{\"k\":\"quote\",\"s\":" + Json(quotes[i]) + ",\"text\":" + Json(PGLog::Quote(quotes[i])) + "}");

    printf("]\n");
    return 0;
}
