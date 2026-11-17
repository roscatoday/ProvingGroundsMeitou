// sources: BalanceTuning.cpp ArenaPerformance.cpp pgconfig_stub.cpp
// Task 1.13: the PURE parts of five glue files. The glue files themselves include kenshi/*.h, Windows.h and PGLog.h, so they cannot be
// linked here: the functions below are COPIED VERBATIM from them (line ranges on each), with only the engine reads replaced by plain
// values, and each replacement is named in a "// was:" comment. They are linked with the real TownBettingPolicy.h / BalanceTuning.cpp
// (ReturnCats, CanAccept) and the PGConfig stub (bookie range; ClampStake is copied from PGConfig.cpp:1093-1100), so g++'s printf and
// iostream make the bytes the original writes for %d, %u and %02d. %.Nf is glibc's (exact value, ties to even), NOT the shipped
// original's msvcr100 (ties half away from zero, 17 digits then zeros), so the golden tests run Core under CMath.UseGlibcForTest
// (see Core/Msvcr100Format.cs). Lines, each {"k": kind, inputs..., outputs...}:
//   "eval"     ArenaCombatProfile::Evaluate + StatText over a Profile        ArenaCombatProfile.cpp:12-21, 53-56, 59-60, 89-116
//   "read"     Read's decision part, getters as plain values               ArenaCombatProfile.cpp:23-36, 74-86
//   "style"    ArenaCombatProfile StyleFor(category index 0..9)              ArenaCombatProfile.cpp:38-51
//   "roster"   RosterStatus::Read over plain medical values                  RosterStatus.cpp:9-48
//   "rand"     TownSpectators ChatterRandom (seed, tick)                      TownSpectators.cpp:140-146
//   "chatter"  TownSpectators ChatterLine (fighter names)                    TownSpectators.cpp:178-207
//   "winners"  TownSpectators ResultWinners + ResultChatterLine              TownSpectators.cpp:208-253
//   "cycle"    TownSpectators ScheduleChatterCycle                           TownSpectators.cpp:254-284
//   "react"    TownSpectators::ReactToResult                                 TownSpectators.cpp:311-335
//   "seed"     TickChatter's restart seed                                    TownSpectators.cpp:293-294
//   "capture"  TownBookie Capture's text                                     TownBookie.cpp:113-130
//   "view"     TownBookie MakeView                                           TownBookie.cpp:131-198
//   "result"   TownBookie MakeResultView                                     TownBookie.cpp:199-230
//   "wager"    TownBookie Wager's accepted texts                             TownBookie.cpp:107-109
//   "settle"   TownBookie::Settle's messages                                 TownBookie.cpp:383-401
#include "PGConfig.h"
#include "TownBettingPolicy.h"
#include "SparPodium.h"
#include "ArenaPerformance.h"
#include "RosterStatusPolicy.h"
#include "MatchRules.h"
#include "pgconfig_stub.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <iomanip>
#include <limits>
#include <sstream>
#include <string>
#include <vector>

namespace
{
    unsigned s_seed = 20261113u;
    unsigned R() { s_seed = s_seed * 1664525u + 1013904223u; return s_seed >> 8; }

    std::string Json(const std::string& value)
    {
        std::string out = "\"";
        for (size_t i = 0; i < value.size(); ++i)
        {
            const unsigned char c = static_cast<unsigned char>(value[i]);
            if (c == '"' || c == '\\') { out += '\\'; out += static_cast<char>(c); }
            else if (c == '\n') out += "\\n";
            else if (c < 32) { char e[8]; snprintf(e, sizeof e, "\\u%04x", c); out += e; }
            else out += static_cast<char>(c);
        }
        return out + "\"";
    }
    unsigned long long Bits(double d) { unsigned long long b; std::memcpy(&b, &d, 8); return b; }
    unsigned FBits(float f) { unsigned b; std::memcpy(&b, &f, 4); return b; }
    std::string List(const std::vector<std::string>& v)
    {
        std::string out = "[";
        for (size_t i = 0; i < v.size(); ++i) { if (i) out += ","; out += Json(v[i]); }
        return out + "]";
    }
    bool g_first = true;
    void Line(const std::string& body) { std::printf("%s%s\n", g_first ? "[" : ",", body.c_str()); g_first = false; }
}

// PGConfig.cpp:1093-1100, verbatim (g_bookie = the stub's range)
namespace PGConfig
{
    int ClampStake(int stake)
    {
        const Bookie& g_bookie = BookieValues();
        if (stake < g_bookie.minimum)
            return g_bookie.minimum;
        if (stake > g_bookie.maximum)
            return g_bookie.maximum;
        return stake;
    }
}

// ---- ArenaCombatProfile.cpp ------------------------------------------------------------------------------------------------------
namespace ArenaCombatProfile
{
    // ArenaCombatProfile.h:6-12
    struct Profile {
        bool valid, martial;
        std::string style;
        double attack, defence, strength, toughness, dexterity, weaponSkill;
        Profile();
    };
    // the ten categories StyleFor tells apart, in its switch order; 9 = any other
    enum WeaponCategory { SKILL_KATANAS, SKILL_SABRES, SKILL_BLUNT, SKILL_HEAVY, SKILL_HACKERS, SKILL_UNARMED, SKILL_BOW, SKILL_TURRET,
        ATTACK_POLEARMS, OTHER };

namespace {
bool Finite(double value) {
    return value == value && value <= (std::numeric_limits<double>::max)() &&
        value >= -(std::numeric_limits<double>::max)();
}

bool CoreStatsFinite(const Profile& profile) {
    return Finite(profile.attack) && Finite(profile.defence) &&
        Finite(profile.strength) && Finite(profile.toughness) &&
        Finite(profile.dexterity);
}

const char* StyleFor(WeaponCategory category) {
    switch (category) {
    case SKILL_KATANAS: return "Katana";
    case SKILL_SABRES: return "Sabre";
    case SKILL_BLUNT: return "Blunt";
    case SKILL_HEAVY: return "Heavy weapon";
    case SKILL_HACKERS: return "Hacker";
    case SKILL_UNARMED: return "Martial arts";
    case SKILL_BOW: return "Crossbow";
    case SKILL_TURRET: return "Turret";
    case ATTACK_POLEARMS: return "Polearm";
    default: return "Weapon";
    }
}

void Append(std::ostringstream& out, const char* label, double value) {
    if (out.tellp() > 0) out << '\n';
    out << label << ' ' << value;
}
}

Profile::Profile() : valid(false), martial(false), attack(0), defence(0),
    strength(0), toughness(0), dexterity(0), weaponSkill(0) {}

double Evaluate(const Profile& profile) {
    if (!profile.valid || !CoreStatsFinite(profile) ||
        (!profile.martial && !Finite(profile.weaponSkill))) return -1;
    const double score = profile.martial ?
        10 + .30 * profile.attack + .30 * profile.defence + .15 * profile.strength +
            .15 * profile.toughness + .10 * profile.dexterity :
        10 + .25 * profile.attack + .25 * profile.defence + .15 * profile.strength +
            .15 * profile.toughness + .10 * profile.dexterity + .10 * profile.weaponSkill;
    return Finite(score) && score > 0 ? score : -1;
}

std::string StatText(const Profile& profile) {
    if (!profile.valid || !CoreStatsFinite(profile) ||
        (!profile.martial && !Finite(profile.weaponSkill))) return "Combat stats unavailable";
    std::ostringstream out;
    out << std::fixed << std::setprecision(0);
    Append(out, profile.martial ? "Martial arts" : "Attack", profile.attack);
    Append(out, profile.martial ? "Dodge" : "Defence", profile.defence);
    Append(out, "Strength", profile.strength);
    Append(out, "Toughness", profile.toughness);
    Append(out, "Dexterity", profile.dexterity);
    if (!profile.martial) {
        const std::string skill = profile.style.empty() || profile.style == "Weapon" ?
            "Weapon skill" : profile.style + " skill";
        Append(out, skill.c_str(), profile.weaponSkill);
    }
    return out.str();
}
const char* Style(int category) { return StyleFor(static_cast<WeaponCategory>(category)); }

// ArenaCombatProfile.cpp:23-36 and :74-86, verbatim (Read from `const bool usableWeapon` on). was: CharStats* (a struct of the getter
// values below; its getEquippedWeaponSkill() returns the field `equipped`), weapon && weapon->isValid() (usable), weapon->getCategory()
// (weaponCategory).
struct CharStats {
    double katanas, sabres, blunt, heavyWeapons, hackers, unarmed, bows, turrets, polearms, equipped;
    double unarmedAttack, meleeAttack, dodge, meleeDefence, strength, toughnessValue, dexterity;
    double getEquippedWeaponSkill() const { return equipped; }
    double getMeleeAttack_unarmed(bool) const { return unarmedAttack; }
    double getMeleeAttack_melee() const { return meleeAttack; }
    double getDodge(bool) const { return dodge; }
    double getMeleeDefence_melee(bool) const { return meleeDefence; }
    double strengthActual() const { return strength; }
    double toughness() const { return toughnessValue; }
    double dexterityActual() const { return dexterity; }
};
double SkillFor(const CharStats* stats, WeaponCategory category) {
    switch (category) {
    case SKILL_KATANAS: return stats->katanas;
    case SKILL_SABRES: return stats->sabres;
    case SKILL_BLUNT: return stats->blunt;
    case SKILL_HEAVY: return stats->heavyWeapons;
    case SKILL_HACKERS: return stats->hackers;
    case SKILL_UNARMED: return stats->unarmed;
    case SKILL_BOW: return stats->bows;
    case SKILL_TURRET: return stats->turrets;
    case ATTACK_POLEARMS: return stats->polearms;
    default: return stats->getEquippedWeaponSkill();
    }
}
Profile ReadFrom(bool usable, WeaponCategory weaponCategory, const CharStats* stats) {
    Profile profile;
    const bool usableWeapon = usable;
    const WeaponCategory category = usableWeapon ? weaponCategory : SKILL_UNARMED;
    profile.martial = !usableWeapon || category == SKILL_UNARMED;
    profile.style = profile.martial ? "Martial arts" : usableWeapon ? StyleFor(category) : "Weapon";
    profile.attack = profile.martial ? stats->getMeleeAttack_unarmed(true) : stats->getMeleeAttack_melee();
    profile.defence = profile.martial ? stats->getDodge(true) : stats->getMeleeDefence_melee(false);
    profile.strength = stats->strengthActual();
    profile.toughness = stats->toughness();
    profile.dexterity = stats->dexterityActual();
    profile.weaponSkill = profile.martial ? 0 : usableWeapon ?
        SkillFor(stats, category) : stats->getEquippedWeaponSkill();
    profile.valid = CoreStatsFinite(profile) && (profile.martial || Finite(profile.weaponSkill));
    return profile;
}
}

// ---- RosterStatus.cpp ------------------------------------------------------------------------------------------------------------
namespace RosterStatus
{
    struct Snapshot { float lowestLimb; float blood; float recovery; RosterStatusPolicy::Band band; };
    // was: MedicalSystem::HealthPartStatus (flesh, fleshStun, maxHealth()); present = the anatomy pointer is not NULL
    struct Part { bool present; float flesh, fleshStun, maxHealth; };

    // RosterStatus.cpp:9-48. was: Character* (valid = character && isValid()), MedicalSystem* (hasMedical), getMaxBlood(), blood, anatomy
    bool Read(bool valid, bool hasMedical, float maxBloodIn, float bloodIn, const std::vector<Part>& anatomy, Snapshot& snapshot,
        std::string* error)
    {
        snapshot.lowestLimb = 0.0f;
        snapshot.blood = 0.0f;
        snapshot.recovery = 0.0f;
        snapshot.band = RosterStatusPolicy::Red;
        if (error) *error = "health data unavailable";
        if (!valid) return false;

        if (!hasMedical || anatomy.size() == 0)
        {
            if (error) *error = !hasMedical ? "medical system unavailable" : "anatomy is empty";
            return false;
        }

        const float maxBlood = maxBloodIn;
        snapshot.blood = maxBlood > 0.0f ? bloodIn / maxBlood : 0.0f;
        snapshot.lowestLimb = 1.0f;
        for (uint32_t i = 0; i < anatomy.size(); ++i)
        {
            const Part* part = anatomy[i].present ? &anatomy[i] : NULL;
            if (!part || !(part->maxHealth > 0.0f))
            {
                if (error)
                {
                    char detail[128];
                    // was: medical->anatomy.size() (lektor, uint32_t)
                    sprintf_s(detail, "anatomy part %u/%u has no valid maximum health", i, static_cast<uint32_t>(anatomy.size()));
                    *error = detail;
                }
                return false;
            }
            const float ratio = (part->flesh - part->fleshStun) / part->maxHealth;
            snapshot.lowestLimb = RosterStatusPolicy::LowestLimb(snapshot.lowestLimb, ratio);
        }
        snapshot.recovery = RosterStatusPolicy::Recovery(snapshot.lowestLimb, snapshot.blood);
        snapshot.band = RosterStatusPolicy::BandFor(snapshot.recovery);
        if (error) error->clear();
        return true;
    }
}

// ---- TownSpectators.cpp ----------------------------------------------------------------------------------------------------------
namespace Spectators
{
    unsigned chatterSeed = 0;
    unsigned g_tick = 0;             // was: GetTickCount()
    int lastChatterSpeaker = -1;     // was: hand; a speaker is its index in `seated`
    float elapsed = 0;
    float nextChatterCycle = 0;
    struct ChatterCue { int speaker; float due; ChatterCue() : speaker(-1), due(0.0f) {} };
    std::vector<ChatterCue> chatterCues;
    ArenaPerformance::Policy activePolicy;
    std::vector<std::string> g_fighters;   // was: ChatterFighters()'s names

    // TownSpectators.cpp:140-146
    unsigned ChatterRandom() {
        if (!chatterSeed) chatterSeed = static_cast<unsigned>(g_tick) ^ 0x9e3779b9u;
        chatterSeed ^= chatterSeed << 13;
        chatterSeed ^= chatterSeed >> 17;
        chatterSeed ^= chatterSeed << 5;
        return chatterSeed;
    }
    // TownSpectators.cpp:178-207. was: std::vector<Character*> fighters = ChatterFighters(); name = fighter->getName()
    std::string ChatterLine() {
        static const char* generic[] = {
            "Come on!", "Hit back!", "What a hit!", "Keep fighting!",
            "Stay on them!", "Don't let up!", "Now that's a fight!",
            "This is getting good!", "Get up!", "Finish it!"
        };
        std::vector<std::string> fighters = g_fighters;
        const unsigned type = ChatterRandom() % 10u;
        if (fighters.empty() || type < 4u)
            return generic[ChatterRandom() % (sizeof(generic) / sizeof(generic[0]))];
        const std::string& fighter = fighters[ChatterRandom() % fighters.size()];
        const std::string name = fighter;
        if (type < 7u) {
            switch (ChatterRandom() % 5u) {
                case 0: return "Come on, " + name + "!";
                case 1: return name + " has this!";
                case 2: return "Finish it, " + name + "!";
                case 3: return "Stay on them, " + name + "!";
                default: return "Get back in it, " + name + "!";
            }
        }
        static const int stakes[] = { 100, 500, 1000 };
        char amount[16]; sprintf_s(amount, "%d", stakes[ChatterRandom() % 3u]);
        switch (ChatterRandom() % 4u) {
            case 0: return std::string("I put ") + amount + " Cats on " + name + "!";
            case 1: return std::string(amount) + " Cats says " + name + " takes it!";
            case 2: return "My Cats are on " + name + "!";
            default: return "Don't fail me, " + name + " - " + amount + " Cats!";
        }
    }
    // TownSpectators.cpp:208-220
    std::vector<std::string> ResultWinners(const SparPodium::Snapshot& result) {
        std::vector<std::string> winners;
        for (int i = 0; i < result.fighterCount; ++i) {
            const SparPodium::FighterRow& fighter = result.fighters[i];
            const bool winningTeam =
                (result.outcome == SparPodium::OutcomeTeamAWins && fighter.team == MatchRules::TeamA) ||
                (result.outcome == SparPodium::OutcomeTeamBWins && fighter.team == MatchRules::TeamB);
            const bool lastStanding = result.outcome == SparPodium::OutcomeLastStanding &&
                fighter.id == result.lastStandingId;
            if ((winningTeam || lastStanding) && fighter.name[0]) winners.push_back(fighter.name);
        }
        return winners;
    }
    // TownSpectators.cpp:221-253
    std::string ResultChatterLine(const SparPodium::Snapshot& result,
        const std::vector<std::string>& winners) {
        static const char* enjoyment[] = {
            "What a fight!", "Now that was worth watching!", "Give us another one!",
            "What a finish!", "Best fight all day!", "I'd pay to see that again!"
        };
        static const char* draw[] = {
            "Run it back!", "Again! We need a winner!", "Nobody won my Cats!",
            "You can't end it there!"
        };
        if (result.outcome == SparPodium::OutcomeDraw || winners.empty())
            return draw[ChatterRandom() % (sizeof(draw) / sizeof(draw[0]))];
        const std::string& winner = winners[ChatterRandom() % winners.size()];
        const unsigned type = ChatterRandom() % 10u;
        if (type < 3u)
            return enjoyment[ChatterRandom() % (sizeof(enjoyment) / sizeof(enjoyment[0]))];
        if (type < 6u) {
            switch (ChatterRandom() % 4u) {
                case 0: return winner + " earned that one!";
                case 1: return "What a finish from " + winner + "!";
                case 2: return "That's how it's done, " + winner + "!";
                default: return "I knew " + winner + " would take it!";
            }
        }
        static const int stakes[] = { 100, 500, 1000 };
        char amount[16]; sprintf_s(amount, "%d", stakes[ChatterRandom() % 3u]);
        switch (ChatterRandom() % 4u) {
            case 0: return winner + " just won me " + amount + " Cats!";
            case 1: return std::string("There go my ") + amount + " Cats...";
            case 2: return "Should've backed " + winner + "!";
            default: return "Never betting against " + winner + " again!";
        }
    }
    // TownSpectators.cpp:254-284. was: seated = the seated guests' Character*s; here seated[i] = i
    void ScheduleChatterCycle(int seatedCount) {
        std::vector<int> seated;
        for (int i = 0; i < seatedCount; ++i) seated.push_back(i);
        const unsigned minMs = static_cast<unsigned>(activePolicy.chatterMinSeconds * 1000.0f);
        const unsigned maxMs = static_cast<unsigned>(activePolicy.chatterMaxSeconds * 1000.0f);
        const unsigned rangeMs = maxMs >= minMs ? maxMs - minMs : 0u;
        const float duration = static_cast<float>(minMs +
            (rangeMs ? ChatterRandom() % (rangeMs + 1u) : 0u)) / 1000.0f;
        nextChatterCycle = elapsed + duration;
        if (seated.empty()) return;
        size_t voices = (seated.size() + 24u) / 25u;
        const size_t voiceLimit = activePolicy.maxChatterVoices > 0 ?
            static_cast<size_t>(activePolicy.maxChatterVoices) : 0u;
        if (voices > voiceLimit) voices = voiceLimit;
        if (voices > seated.size()) voices = seated.size();
        if (voices == 0u) return;
        size_t start = ChatterRandom() % seated.size();
        if (seated.size() > 1u && lastChatterSpeaker == seated[start])
            start = (start + 1u) % seated.size();
        const size_t step = seated.size() / voices > 0u ? seated.size() / voices : 1u;
        const float spacing = duration / static_cast<float>(voices);
        for (size_t i = 0; i < voices; ++i) {
            ChatterCue cue;
            cue.speaker = seated[(start + i * step) % seated.size()];
            cue.due = elapsed + (static_cast<float>(i) + 0.5f) * spacing;
            chatterCues.push_back(cue);
        }
    }
    // TownSpectators.cpp:311-335. was: guests (guestCount), seated guests (seatedCount), speaker->say(line)
    std::vector<std::pair<int, std::string> > ReactToResult(const SparPodium::Snapshot& result, int guestCount, int seatedCount) {
        std::vector<std::pair<int, std::string> > said;
        if (!activePolicy.chatterEnabled || result.outcome == SparPodium::OutcomeStopped || guestCount == 0) return said;
        std::vector<int> seated;
        for (int i = 0; i < seatedCount; ++i) seated.push_back(i);
        if (seated.empty()) return said;
        size_t voices = (seated.size() + 24u) / 25u;
        const size_t voiceLimit = activePolicy.maxChatterVoices > 0 ?
            static_cast<size_t>(activePolicy.maxChatterVoices) : 0u;
        if (voices > voiceLimit) voices = voiceLimit;
        if (voices > seated.size()) voices = seated.size();
        if (voices == 0u) return said;
        const std::vector<std::string> winners = ResultWinners(result);
        size_t start = ChatterRandom() % seated.size();
        if (seated.size() > 1u && lastChatterSpeaker == seated[start])
            start = (start + 1u) % seated.size();
        const size_t step = seated.size() / voices > 0u ? seated.size() / voices : 1u;
        for (size_t i = 0; i < voices; ++i) {
            const int speaker = seated[(start + i * step) % seated.size()];
            said.push_back(std::make_pair(speaker, ResultChatterLine(result, winners)));
            lastChatterSpeaker = speaker;
        }
        return said;
    }
    // TownSpectators.cpp:293-294. was: GetTickCount(), guests.size() (size_t)
    unsigned RestartSeed(unsigned tick, size_t guests) {
        return static_cast<unsigned>(tick) ^ static_cast<unsigned>(guests * 2654435761u);
    }
}

// ---- TownBookie.cpp --------------------------------------------------------------------------------------------------------------
namespace Bookie
{
    // TownBookieUI.h:8-19 / TownBookieResultUI.h:8-14 without the actor handle
    struct FighterView { std::string name, style, record, stats; };
    struct View {
        std::vector<FighterView> fighters[2];
        std::string header, timer, status, slip, odds[2];
        int selectedSide, stake;
        bool canSelect, canStake, canBet, canArrange;
        // TownBookieUI.h:17-18, verbatim
        View() : selectedSide(-1), stake(100), canSelect(false), canStake(false),
            canBet(false), canArrange(false) {}
    };
    struct ResultView {
        std::vector<FighterView> fighters[2];
        std::string title, context, payout;
        int winningSide, selectedSide, tone;
        ResultView() : winningSide(-1), selectedSide(-1), tone(0) {}   // TownBookieResultUI.h:13, verbatim
    };
    struct Record { std::string id; float mmr; int wins, losses, matches; };

    // TownBookie.cpp:113-130. was: Character* (valid, getName()), ArenaCombatProfile::Read(character),
    // TownMatchmakingRuntime::Identity(character)
    FighterView Capture(bool valid, const std::string& characterName, const ArenaCombatProfile::Profile& profile,
        const std::string& identity, const std::vector<Record>& standings) {
        FighterView result;
        result.name = valid ? characterName : "Unavailable fighter";
        result.style = profile.style;
        result.stats = ArenaCombatProfile::StatText(profile);
        result.record = "Unrated | 0 W / 0 L";
        for (size_t i = 0; !identity.empty() && i < standings.size(); ++i) {
            if (standings[i].id != identity || standings[i].matches < 1) continue;
            char text[128]; sprintf_s(text, "MMR %.0f | %d W / %d L",
                standings[i].mmr, standings[i].wins, standings[i].losses);
            result.record = text; break;
        }
        return result;
    }

    // the TownBookie.cpp globals and engine reads MakeView uses
    struct In {
        int money; bool hasWallet; int selectedSide; bool market, eligible, fighting, completed, resultScheduled, resultReady;
        bool nearby, sameTown, hasActiveSave; int bookieCredit; int stake; TownBettingPolicy::Ticket ticket; int teamCounts[2];
        double ability[2]; std::vector<FighterView> fighterViews[2]; bool planned; double start, now;
        std::string actionMessage, message;
    };

    // TownBookie.cpp:131-198. was: Wallet()/getMoney(), Eligible(), AtBookie(), the first fighter / bookie same-town test,
    // LeaderboardStore::HasActiveSave(), GetBookieCredit(), TownArena::HasPlannedNpcBout() && ou, GetPlannedNpcStartHours(),
    // the game hours. `stake` is the module global (MakeView writes it back).
    View MakeView(In& g) {
        View view;
        int& stake = g.stake;
        const int money = g.hasWallet ? g.money : 0;
        const int selectedSide = g.selectedSide;
        const bool market = g.market, fighting = g.fighting, completed = g.completed;
        const TownBettingPolicy::Ticket& ticket = g.ticket;
        const int* teamCounts = g.teamCounts;
        const double* ability = g.ability;
        const bool chosen = selectedSide == 0 || selectedSide == 1;
        const bool valid = market && g.eligible;
        const bool resultBlocking = g.resultScheduled || g.resultReady;
        const bool nearby = g.nearby;
        const bool sameTown = g.sameTown;
        const bool ready = g.hasActiveSave && nearby && sameTown;
        // A configured stake ceiling can be lower than the stake the window
        // opened with, and the bet button is gated on this exact value.
        stake = PGConfig::ClampStake(stake);
        view.selectedSide = selectedSide;
        view.stake = ticket.stake > 0 ? ticket.stake : stake;
        view.canSelect = valid && !fighting && !ticket.pending && !completed && !resultBlocking;
        view.canStake = view.canSelect;
        view.canBet = !resultBlocking && ready && g.bookieCredit == 0 &&
            TownBettingPolicy::CanAccept(ticket, valid && !completed, !fighting,
                selectedSide, stake, money);
        view.canArrange = !resultBlocking && nearby && g.hasActiveSave && !market && !fighting && !ticket.pending;
        char text[384];
        const char* state = fighting ? "COMBAT - BETS CLOSED" : market ? "BETTING OPEN" : completed ? "BOUT FINISHED" : "NO BOUT OPEN";
        sprintf_s(text, "%s | %d Cats | NPC bout %dv%d", state, money, teamCounts[0], teamCounts[1]);
        view.header = text;
        if (g.planned) {
            const double start = g.start;
            const double now = g.now;
            const int minuteOfDay = static_cast<int>(std::ceil(start * 60.0)) % (24 * 60);
            const int remaining = static_cast<int>(std::ceil((std::max)(0.0, start - now) * 60.0));
            if (remaining > 0) sprintf_s(text, "NEXT BOUT  %02d:%02d  |  STARTS IN %dh %02dm",
                minuteOfDay / 60, minuteOfDay % 60, remaining / 60, remaining % 60);
            else sprintf_s(text, "NEXT BOUT  %02d:%02d  |  START TIME REACHED - WAITING FOR ARENA / MEDICS",
                minuteOfDay / 60, minuteOfDay % 60);
            view.timer = text;
        }
        for (int side = 0; side < 2; ++side) {
            view.fighters[side] = g.fighterViews[side];
            const int payout = TownBettingPolicy::ReturnCats(1000, ability[side], ability[1-side]);
            if (teamCounts[side] > 0 && payout > 0) {
                sprintf_s(text, "%.2fx return", payout / 1000.0); view.odds[side] = text;
            } else view.odds[side] = "Awaiting fighters";
        }
        view.status = g.actionMessage.empty() ? g.message : g.actionMessage;
        if (ticket.stake > 0 && ticket.side >= 0 && ticket.side <= 1) {
            sprintf_s(text, "%s: Team %s | Stake %d Cats | Quoted return %d Cats | Potential profit +%d Cats",
                ticket.pending ? "ACCEPTED" : "SETTLED", ticket.side == 0 ? "A" : "B", ticket.stake,
                ticket.payout, ticket.payout - ticket.stake);
            view.slip = text;
        } else if (chosen && market && !fighting) {
            const int payout = TownBettingPolicy::ReturnCats(stake, ability[selectedSide], ability[1-selectedSide]);
            sprintf_s(text, "YOUR BET: Team %s | Stake %d Cats | Total return %d Cats | Profit +%d Cats",
                selectedSide == 0 ? "A" : "B", stake, payout, payout - stake);
            view.slip = text;
        } else view.slip = fighting ? "Bets closed. No ticket placed." : completed ? "No ticket placed for this bout." :
            "YOUR BET: Choose a team and stake. Place bet confirms your wager.";
        if (!ticket.pending && market && !fighting) {
            if (!ready) view.status = "Bring a squad member close to this town's bookie; an active save is required.";
            else if (!valid) view.status = "Betting unavailable: fighter data is incomplete.";
            else if (g.bookieCredit != 0) view.status = "Pending Cats return must be collected before betting.";
            else if (money < stake) view.status = "Not enough Cats for this stake.";
            else if (!chosen) view.status = "Select a team, then choose your stake. One wager per bout.";
        }
        return view;
    }

    // TownBookie.cpp:199-230
    ResultView MakeResultView(const TownBettingPolicy::Result& result, const std::vector<FighterView>* resultFighterViews) {
        ResultView view;
        view.fighters[0] = resultFighterViews[0];
        view.fighters[1] = resultFighterViews[1];
        view.winningSide = result.winningSide;
        view.selectedSide = result.selectedSide;
        view.tone = result.outcome == TownBettingPolicy::ResultWon ? 1 :
            result.outcome == TownBettingPolicy::ResultLost ? 2 : 3;
        char text[256];
        if (result.outcome == TownBettingPolicy::ResultWon) {
            view.title = "WAGER WON";
            sprintf_s(text, "Team %s claimed the arena. Your pick delivered.",
                result.winningSide == 0 ? "A" : "B");
            view.context = text;
            sprintf_s(text, "STAKE  %d CATS     RETURNED  %d CATS\nNET WIN  +%d CATS",
                result.stake, result.returned, result.net);
        } else if (result.outcome == TownBettingPolicy::ResultLost) {
            view.title = "WAGER LOST";
            sprintf_s(text, "Team %s claimed the arena. You backed Team %s.",
                result.winningSide == 0 ? "A" : "B", result.selectedSide == 0 ? "A" : "B");
            view.context = text;
            sprintf_s(text, "STAKE  %d CATS     RETURNED  0 CATS\nNET LOSS  -%d CATS",
                result.stake, -result.net);
        } else {
            view.title = "WAGER REFUNDED";
            view.context = "No victor was declared. The bookie returns your stake.";
            sprintf_s(text, "STAKE  %d CATS     RETURNED  %d CATS\nNET  0 CATS",
                result.stake, result.returned);
        }
        view.payout = text;
        return view;
    }

    // TownBookie.cpp:107-109 (message and the MarketEvent fields)
    void Wager(int side, int stake, int payout, std::string& message, std::string& fieldsOut) {
        char text[128]; sprintf_s(text, "Accepted: %d Cats. Winning total return: %d Cats.", stake, payout);
        message = text;
        char fields[160]; sprintf_s(fields, ",\"bet_side\":%d,\"stake\":%d,\"quoted_return\":%d", side, stake, payout);
        fieldsOut = fields;
    }
    // TownBookie.cpp:383-401 (the message only). was: ticket.pending, the ticket side, the credit Settle returned
    std::string SettleMessage(bool pending, int winner, int side, int credit) {
        std::string message;
        char text[192];
        if (pending) {
            if (winner < 0) sprintf_s(text, "Bout cancelled/drawn: %d Cats refunded.", credit);
            else sprintf_s(text, "Team %s wins. Bet %s: %d Cats returned.",
                winner == 0 ? "A" : "B", winner == side ? "won" : "lost", credit);
            message = text;
        } else {
            message = winner < 0 ? "Bout cancelled or drawn. No wager placed." :
                winner == 0 ? "Team A wins. No wager placed." : "Team B wins. No wager placed.";
        }
        return message;
    }
}

// ---- case makers -----------------------------------------------------------------------------------------------------------------
namespace
{
    double StatValue()
    {
        switch (R() % 12)
        {
        case 0: return 0.0;
        case 1: return 0.5;
        case 2: return 2.5;
        case 3: return -0.4;
        case 4: return -3.5;
        case 5: return std::numeric_limits<double>::quiet_NaN();
        case 6: return R() % 2 ? std::numeric_limits<double>::infinity() : -std::numeric_limits<double>::infinity();
        case 7: return (R() % 2001) / 4.0;            // quarter steps: x.5 ties
        case 8: return (R() % 100000) / 1000.0 - 20.0;
        case 9: return 1e300 * (R() % 3 + 1);          // a finite score may overflow
        default: return (R() % 10000) / 97.0;
        }
    }
    const char* kStyles[] = { "", "Weapon", "Katana", "Martial arts", "Heavy weapon", "Polearm", "Crossbow" };
    ArenaCombatProfile::Profile MakeProfile()
    {
        ArenaCombatProfile::Profile p;
        p.valid = R() % 8 != 0;
        p.martial = R() % 2 == 0;
        p.style = kStyles[R() % 7];
        p.attack = StatValue(); p.defence = StatValue(); p.strength = StatValue();
        p.toughness = StatValue(); p.dexterity = StatValue(); p.weaponSkill = StatValue();
        if (R() % 3 == 0) { p.attack = R() % 101; p.defence = R() % 101; p.strength = R() % 101; p.toughness = R() % 101;
            p.dexterity = R() % 101; p.weaponSkill = R() % 101; }   // plain stats: most cases reach the formula
        return p;
    }
    std::string ProfileJson(const ArenaCombatProfile::Profile& p)
    {
        char b[512];
        snprintf(b, sizeof b, "\"valid\":%d,\"martial\":%d,\"style\":%s,\"stats\":[%llu,%llu,%llu,%llu,%llu,%llu]",
            p.valid ? 1 : 0, p.martial ? 1 : 0, Json(p.style).c_str(), Bits(p.attack), Bits(p.defence), Bits(p.strength),
            Bits(p.toughness), Bits(p.dexterity), Bits(p.weaponSkill));
        return b;
    }
    float PartValue()
    {
        switch (R() % 9)
        {
        case 0: return 0.0f;
        case 1: return -5.0f;
        case 2: return std::numeric_limits<float>::quiet_NaN();
        case 3: return 100.0f;
        default: return static_cast<float>(R() % 20001) / 100.0f - 20.0f;
        }
    }
    const char* kNames[] = { "Beep", "Ruka", "Burn", "Hamut", "Agnu", "Kang", "Cat-Lon", "" };
    std::string ViewJson(const Bookie::FighterView& v)
    {
        return "[" + Json(v.name) + "," + Json(v.style) + "," + Json(v.record) + "," + Json(v.stats) + "]";
    }
    std::string ViewsJson(const std::vector<Bookie::FighterView>& v)
    {
        std::string out = "[";
        for (size_t i = 0; i < v.size(); ++i) { if (i) out += ","; out += ViewJson(v[i]); }
        return out + "]";
    }
    void SetPolicy(ArenaPerformance::Policy& p)
    {
        static const float mins[] = { 4.0f, 0.0f, 2.5f, 6.0f, 0.001f, 8.0f };
        static const float maxs[] = { 8.0f, 0.0f, 2.5f, 3.0f, 0.0015f, 12.345f };
        p.chatterMinSeconds = mins[R() % 6];
        p.chatterMaxSeconds = maxs[R() % 6];
        static const int voices[] = { 5, 0, -1, 1, 2, 64 };
        p.maxChatterVoices = voices[R() % 6];
        p.chatterEnabled = R() % 5 != 0;
    }
    unsigned SeedValue()
    {
        switch (R() % 4) { case 0: return 0u; case 1: return 0x9e3779b9u; default: return R() * 2654435761u + R(); }
    }
    void MakeResult(SparPodium::Snapshot& s)
    {
        std::memset(&s, 0, sizeof s);
        s.outcome = static_cast<SparPodium::OutcomeKind>(R() % 5);
        s.fighterCount = static_cast<int>(R() % 7);
        for (int i = 0; i < s.fighterCount; ++i)
        {
            s.fighters[i].id = static_cast<int>(R() % 6);
            s.fighters[i].team = static_cast<MatchRules::MatchTeam>(R() % 3);
            const char* n = kNames[R() % 8];
            std::strncpy(s.fighters[i].name, n, 63);
        }
        s.lastStandingId = static_cast<int>(R() % 6);
    }
    std::string ResultJson(const SparPodium::Snapshot& s)
    {
        char b[64];
        std::string out;
        snprintf(b, sizeof b, "\"outcome\":%d,\"last\":%d,\"fighters\":[", static_cast<int>(s.outcome), s.lastStandingId);
        out = b;
        for (int i = 0; i < s.fighterCount; ++i)
        {
            snprintf(b, sizeof b, "%s[%d,%d,", i ? "," : "", s.fighters[i].id, static_cast<int>(s.fighters[i].team));
            out += b; out += Json(s.fighters[i].name); out += "]";
        }
        return out + "]";
    }
}

int main()
{
    // eval
    for (int n = 0; n < 400; ++n)
    {
        ArenaCombatProfile::Profile p = MakeProfile();
        char b[64]; snprintf(b, sizeof b, ",\"score\":%llu,\"text\":", Bits(ArenaCombatProfile::Evaluate(p)));
        Line("{\"k\":\"eval\"," + ProfileJson(p) + b + Json(ArenaCombatProfile::StatText(p)) + "}");
    }
    for (int c = 0; c < 10; ++c)
    {
        char b[96]; snprintf(b, sizeof b, "{\"k\":\"style\",\"category\":%d,\"text\":", c);
        Line(b + Json(ArenaCombatProfile::Style(c)) + "}");
    }
    // read
    for (int n = 0; n < 200; ++n)
    {
        const bool usable = R() % 4 != 0;
        const int category = static_cast<int>(R() % 10);
        ArenaCombatProfile::CharStats g;
        double* f[] = { &g.katanas, &g.sabres, &g.blunt, &g.heavyWeapons, &g.hackers, &g.unarmed, &g.bows, &g.turrets, &g.polearms,
            &g.equipped, &g.unarmedAttack, &g.meleeAttack, &g.dodge, &g.meleeDefence, &g.strength, &g.toughnessValue, &g.dexterity };
        const bool plain = R() % 2 == 0;   // plain stats: most cases are valid
        for (int i = 0; i < 17; ++i) *f[i] = plain ? static_cast<double>(R() % 101) : StatValue();
        const ArenaCombatProfile::Profile p = ArenaCombatProfile::ReadFrom(usable, static_cast<ArenaCombatProfile::WeaponCategory>(category), &g);
        std::string gj = "[";
        for (int i = 0; i < 17; ++i) { char c[32]; snprintf(c, sizeof c, "%s%llu", i ? "," : "", Bits(*f[i])); gj += c; }
        gj += "]";
        char b[96]; snprintf(b, sizeof b, "{\"k\":\"read\",\"usable\":%d,\"category\":%d,\"getters\":", usable ? 1 : 0, category);
        Line(b + gj + "," + ProfileJson(p) + "}");
    }
    // roster
    for (int n = 0; n < 300; ++n)
    {
        const bool valid = R() % 10 != 0, hasMedical = R() % 10 != 0;
        float maxBlood = R() % 6 == 0 ? (R() % 2 ? 0.0f : -10.0f) : static_cast<float>(R() % 30001) / 100.0f;
        float blood = static_cast<float>(R() % 30001) / 100.0f - 10.0f;
        std::vector<RosterStatus::Part> parts(R() % 9);
        std::string partsJson = "[";
        for (size_t i = 0; i < parts.size(); ++i)
        {
            parts[i].present = R() % 25 != 0;
            parts[i].flesh = PartValue(); parts[i].fleshStun = R() % 3 ? 0.0f : PartValue(); parts[i].maxHealth = PartValue();
            if (R() % 3 != 0) parts[i].maxHealth = 100.0f;   // most anatomies are valid
            char b[80]; snprintf(b, sizeof b, "%s[%d,%u,%u,%u]", i ? "," : "", parts[i].present ? 1 : 0, FBits(parts[i].flesh),
                FBits(parts[i].fleshStun), FBits(parts[i].maxHealth));
            partsJson += b;
        }
        partsJson += "]";
        RosterStatus::Snapshot s; std::string error;
        const bool ok = RosterStatus::Read(valid, hasMedical, maxBlood, blood, parts, s, &error);
        char b[256]; snprintf(b, sizeof b, "{\"k\":\"roster\",\"valid\":%d,\"medical\":%d,\"maxBlood\":%u,\"blood\":%u,\"parts\":",
            valid ? 1 : 0, hasMedical ? 1 : 0, FBits(maxBlood), FBits(blood));
        char o[192]; snprintf(o, sizeof o, ",\"ok\":%d,\"lowest\":%u,\"outBlood\":%u,\"recovery\":%u,\"band\":%d,\"error\":", ok ? 1 : 0,
            FBits(s.lowestLimb), FBits(s.blood), FBits(s.recovery), static_cast<int>(s.band));
        Line(b + partsJson + o + Json(error) + "}");
    }
    // rand
    for (int n = 0; n < 40; ++n)
    {
        const unsigned seed = SeedValue(), tick = n % 5 == 0 ? 0x9e3779b9u : R() * 7919u;
        Spectators::chatterSeed = seed; Spectators::g_tick = tick;
        std::string outs;
        for (int i = 0; i < 4; ++i) { char b[16]; snprintf(b, sizeof b, "%s%u", i ? "," : "", Spectators::ChatterRandom()); outs += b; }
        char b[96]; snprintf(b, sizeof b, "{\"k\":\"rand\",\"seed\":%u,\"tick\":%u,\"out\":[", seed, tick);
        Line(b + outs + "]}");
    }
    // chatter
    for (int n = 0; n < 300; ++n)
    {
        const unsigned seed = SeedValue(), tick = R();
        Spectators::chatterSeed = seed; Spectators::g_tick = tick;
        Spectators::g_fighters.clear();
        const int count = static_cast<int>(R() % 4);
        for (int i = 0; i < count; ++i) Spectators::g_fighters.push_back(kNames[R() % 8]);
        const std::string line = Spectators::ChatterLine();
        char b[96]; snprintf(b, sizeof b, "{\"k\":\"chatter\",\"seed\":%u,\"tick\":%u,\"fighters\":", seed, tick);
        char o[48]; snprintf(o, sizeof o, ",\"seedOut\":%u,\"line\":", Spectators::chatterSeed);
        Line(b + List(Spectators::g_fighters) + o + Json(line) + "}");
    }
    // winners
    for (int n = 0; n < 300; ++n)
    {
        SparPodium::Snapshot s; MakeResult(s);
        const unsigned seed = SeedValue(), tick = R();
        Spectators::chatterSeed = seed; Spectators::g_tick = tick;
        const std::vector<std::string> winners = Spectators::ResultWinners(s);
        const std::string line = Spectators::ResultChatterLine(s, winners);
        char b[96]; snprintf(b, sizeof b, "{\"k\":\"winners\",\"seed\":%u,\"tick\":%u,", seed, tick);
        char o[48]; snprintf(o, sizeof o, ",\"seedOut\":%u,\"line\":", Spectators::chatterSeed);
        Line(b + ResultJson(s) + ",\"winners\":" + List(winners) + o + Json(line) + "}");
    }
    // cycle
    for (int n = 0; n < 200; ++n)
    {
        ArenaPerformance::Policy p; SetPolicy(p); Spectators::activePolicy = p;
        const unsigned seed = SeedValue(), tick = R();
        Spectators::chatterSeed = seed; Spectators::g_tick = tick;
        static const int counts[] = { 0, 1, 2, 3, 24, 25, 26, 49, 51, 120, 130, 1000 };
        const int seatedCount = counts[R() % 12];
        Spectators::lastChatterSpeaker = seatedCount && R() % 2 ? static_cast<int>(R() % seatedCount) : -1;
        Spectators::elapsed = R() % 4 ? static_cast<float>(R() % 100000) / 7.0f : 0.0f;
        Spectators::nextChatterCycle = -1.0f;
        Spectators::chatterCues.clear();
        Spectators::ScheduleChatterCycle(seatedCount);
        std::string cues = "[";
        for (size_t i = 0; i < Spectators::chatterCues.size(); ++i)
        {
            char c[48]; snprintf(c, sizeof c, "%s[%d,%u]", i ? "," : "", Spectators::chatterCues[i].speaker,
                FBits(Spectators::chatterCues[i].due));
            cues += c;
        }
        cues += "]";
        char b[320]; snprintf(b, sizeof b,
            "{\"k\":\"cycle\",\"min\":%u,\"max\":%u,\"voices\":%d,\"seed\":%u,\"tick\":%u,\"seated\":%d,\"last\":%d,\"elapsed\":%u",
            FBits(p.chatterMinSeconds), FBits(p.chatterMaxSeconds), p.maxChatterVoices, seed, tick, seatedCount,
            Spectators::lastChatterSpeaker, FBits(Spectators::elapsed));
        char o[96]; snprintf(o, sizeof o, ",\"next\":%u,\"seedOut\":%u,\"cues\":", FBits(Spectators::nextChatterCycle), Spectators::chatterSeed);
        Line(b + std::string(o) + cues + "}");
    }
    // react
    for (int n = 0; n < 200; ++n)
    {
        ArenaPerformance::Policy p; SetPolicy(p); Spectators::activePolicy = p;
        SparPodium::Snapshot s; MakeResult(s);
        const unsigned seed = SeedValue(), tick = R();
        Spectators::chatterSeed = seed; Spectators::g_tick = tick;
        static const int counts[] = { 0, 1, 2, 3, 25, 26, 51, 130 };
        const int seatedCount = counts[R() % 8];
        const int guestCount = R() % 6 == 0 ? 0 : seatedCount + static_cast<int>(R() % 3);
        const int last = seatedCount && R() % 2 ? static_cast<int>(R() % seatedCount) : -1;
        Spectators::lastChatterSpeaker = last;
        const std::vector<std::pair<int, std::string> > said = Spectators::ReactToResult(s, guestCount, seatedCount);
        std::string out = "[";
        for (size_t i = 0; i < said.size(); ++i)
        {
            char c[24]; snprintf(c, sizeof c, "%s[%d,", i ? "," : "", said[i].first);
            out += c; out += Json(said[i].second); out += "]";
        }
        out += "]";
        char b[200]; snprintf(b, sizeof b, "{\"k\":\"react\",\"enabled\":%d,\"voices\":%d,\"seed\":%u,\"tick\":%u,\"guests\":%d,\"seated\":%d,\"speaker\":%d,",
            p.chatterEnabled ? 1 : 0, p.maxChatterVoices, seed, tick, guestCount, seatedCount, last);
        char o[64]; snprintf(o, sizeof o, ",\"seedOut\":%u,\"lastOut\":%d,\"said\":", Spectators::chatterSeed, Spectators::lastChatterSpeaker);
        Line(b + ResultJson(s) + o + out + "}");
    }
    // seed
    for (int n = 0; n < 30; ++n)
    {
        const unsigned tick = R() * 3u + R();
        const size_t guests = n < 3 ? static_cast<size_t>(n) : static_cast<size_t>(R() % 200);
        char b[96]; snprintf(b, sizeof b, "{\"k\":\"seed\",\"tick\":%u,\"guests\":%u,\"out\":%u}", tick, static_cast<unsigned>(guests),
            Spectators::RestartSeed(tick, guests));
        Line(b);
    }
    // capture
    for (int n = 0; n < 200; ++n)
    {
        const bool valid = R() % 6 != 0;
        const std::string name = kNames[R() % 8];
        ArenaCombatProfile::Profile p = MakeProfile();
        const std::string identity = R() % 5 == 0 ? std::string() : std::string("id") + static_cast<char>('0' + R() % 4);
        std::vector<Bookie::Record> standings(R() % 5);
        std::string sj = "[";
        for (size_t i = 0; i < standings.size(); ++i)
        {
            standings[i].id = std::string("id") + static_cast<char>('0' + R() % 4);
            standings[i].matches = static_cast<int>(R() % 3) - 0;
            standings[i].wins = static_cast<int>(R() % 40); standings[i].losses = static_cast<int>(R() % 40);
            switch (R() % 4) { case 0: standings[i].mmr = 100.5f; break; case 1: standings[i].mmr = 101.5f; break;
                case 2: standings[i].mmr = -0.25f; break; default: standings[i].mmr = static_cast<float>(R() % 400000) / 100.0f; }
            char c[96]; snprintf(c, sizeof c, "%s[%s,%u,%d,%d,%d]", i ? "," : "", Json(standings[i].id).c_str(), FBits(standings[i].mmr),
                standings[i].wins, standings[i].losses, standings[i].matches);
            sj += c;
        }
        sj += "]";
        const Bookie::FighterView v = Bookie::Capture(valid, name, p, identity, standings);
        char b[48]; snprintf(b, sizeof b, "{\"k\":\"capture\",\"cvalid\":%d,\"name\":", valid ? 1 : 0);
        Line(b + Json(name) + "," + ProfileJson(p) + ",\"identity\":" + Json(identity) + ",\"standings\":" + sj + ",\"view\":" +
            ViewJson(v) + "}");
    }
    // view
    static const int bookies[][2] = { { 10, 10000 }, { 100, 500 }, { 50, 50 } };
    for (int n = 0; n < 500; ++n)
    {
        const int bi = static_cast<int>(R() % 3);
        PGConfigStub_Bookie().minimum = bookies[bi][0]; PGConfigStub_Bookie().maximum = bookies[bi][1];
        Bookie::In g;
        g.hasWallet = R() % 8 != 0; g.money = R() % 5 == 0 ? -static_cast<int>(R() % 100) : static_cast<int>(R() % 20000);
        g.selectedSide = static_cast<int>(R() % 4) - 1;
        g.market = R() % 3 != 0; g.eligible = R() % 5 != 0; g.fighting = R() % 4 == 0; g.completed = R() % 5 == 0;
        g.resultScheduled = R() % 8 == 0; g.resultReady = R() % 8 == 0;
        g.nearby = R() % 5 != 0; g.sameTown = R() % 6 != 0; g.hasActiveSave = R() % 7 != 0;
        g.bookieCredit = R() % 6 == 0 ? static_cast<int>(R() % 500) : 0;
        static const int stakes[] = { 100, 5, 20000, 777, 50, 10, 1000 };
        g.stake = stakes[R() % 7];
        g.ticket = TownBettingPolicy::Ticket();
        if (R() % 3 == 0) { g.ticket.side = static_cast<int>(R() % 4) - 1; g.ticket.stake = static_cast<int>(R() % 3) * 250;
            g.ticket.payout = static_cast<int>(R() % 3000); g.ticket.pending = R() % 2 == 0; }
        g.teamCounts[0] = static_cast<int>(R() % 5); g.teamCounts[1] = static_cast<int>(R() % 5);
        for (int s = 0; s < 2; ++s)
        {
            switch (R() % 5) { case 0: g.ability[s] = 0; break; case 1: g.ability[s] = -1; break;
                case 2: g.ability[s] = 1; break; default: g.ability[s] = 1.0 + (R() % 100000) / 3.0; }
            g.fighterViews[s].clear();
            for (int i = 0; i < g.teamCounts[s] && i < 2; ++i)
            {
                Bookie::FighterView v; v.name = kNames[R() % 8]; v.style = "Katana"; v.record = "Unrated | 0 W / 0 L"; v.stats = "Attack 3";
                g.fighterViews[s].push_back(v);
            }
        }
        g.planned = R() % 2 == 0;
        g.now = (R() % 100000) / 13.0;
        switch (R() % 5) { case 0: g.start = g.now; break; case 1: g.start = g.now - 1.0; break;
            case 2: g.start = g.now + 1e-9; break; case 3: g.start = 23.999; break;   // never NaN: (int)ceil(NaN) is undefined (x64: INT_MIN; .NET: 0)
            default: g.start = g.now + (R() % 5000) / 61.0; }
        static const char* acts[] = { "", "", "Finish the current bout first." };
        g.actionMessage = acts[R() % 3];
        g.message = R() % 2 ? "NPC bouts only. Arrange a bout to open betting." : "Lineup announced. Betting open until combat begins. One wager per bout.";
        Bookie::In before = g;
        const Bookie::View v = Bookie::MakeView(g);
        char b[512]; snprintf(b, sizeof b,
            "{\"k\":\"view\",\"bi\":%d,\"wallet\":%d,\"money\":%d,\"sel\":%d,\"market\":%d,\"eligible\":%d,\"fighting\":%d,\"completed\":%d,"
            "\"scheduled\":%d,\"ready\":%d,\"nearby\":%d,\"sameTown\":%d,\"save\":%d,\"credit\":%d,\"stake\":%d,\"ticket\":[%d,%d,%d,%d],"
            "\"teams\":[%d,%d],\"ability\":[%llu,%llu],\"planned\":%d,\"start\":%llu,\"now\":%llu,",
            bi, before.hasWallet ? 1 : 0, before.money, before.selectedSide, before.market ? 1 : 0, before.eligible ? 1 : 0,
            before.fighting ? 1 : 0, before.completed ? 1 : 0, before.resultScheduled ? 1 : 0, before.resultReady ? 1 : 0,
            before.nearby ? 1 : 0, before.sameTown ? 1 : 0, before.hasActiveSave ? 1 : 0, before.bookieCredit, before.stake,
            before.ticket.side, before.ticket.stake, before.ticket.payout, before.ticket.pending ? 1 : 0,
            before.teamCounts[0], before.teamCounts[1], Bits(before.ability[0]), Bits(before.ability[1]), before.planned ? 1 : 0,
            Bits(before.start), Bits(before.now));
        char o[160]; snprintf(o, sizeof o, ",\"stakeOut\":%d,\"vsel\":%d,\"vstake\":%d,\"flags\":[%d,%d,%d,%d],", g.stake, v.selectedSide,
            v.stake, v.canSelect ? 1 : 0, v.canStake ? 1 : 0, v.canBet ? 1 : 0, v.canArrange ? 1 : 0);
        Line(b + std::string("\"action\":") + Json(before.actionMessage) + ",\"message\":" + Json(before.message) +
            ",\"views\":[" + ViewsJson(before.fighterViews[0]) + "," + ViewsJson(before.fighterViews[1]) + "]" + o +
            "\"header\":" + Json(v.header) + ",\"timer\":" + Json(v.timer) + ",\"status\":" + Json(v.status) + ",\"slip\":" +
            Json(v.slip) + ",\"odds\":[" + Json(v.odds[0]) + "," + Json(v.odds[1]) + "],\"vviews\":[" + ViewsJson(v.fighters[0]) + "," +
            ViewsJson(v.fighters[1]) + "]}");
    }
    PGConfigStub_Reset();
    // result
    for (int n = 0; n < 60; ++n)
    {
        TownBettingPolicy::Result r;
        r.outcome = static_cast<TownBettingPolicy::ResultOutcome>(R() % 4);
        r.winningSide = static_cast<int>(R() % 3) - 1; r.selectedSide = static_cast<int>(R() % 3) - 1;
        r.stake = static_cast<int>(R() % 5000); r.returned = static_cast<int>(R() % 9000); r.net = static_cast<int>(R() % 9000) - 4500;
        std::vector<Bookie::FighterView> views[2];
        for (int s = 0; s < 2; ++s) for (int i = 0; i < static_cast<int>(R() % 3); ++i)
        { Bookie::FighterView v; v.name = kNames[R() % 8]; views[s].push_back(v); }
        const Bookie::ResultView v = Bookie::MakeResultView(r, views);
        char b[160]; snprintf(b, sizeof b, "{\"k\":\"result\",\"outcome\":%d,\"win\":%d,\"sel\":%d,\"stake\":%d,\"returned\":%d,\"net\":%d,",
            static_cast<int>(r.outcome), r.winningSide, r.selectedSide, r.stake, r.returned, r.net);
        char o[64]; snprintf(o, sizeof o, ",\"vwin\":%d,\"vsel\":%d,\"tone\":%d,", v.winningSide, v.selectedSide, v.tone);
        Line(b + std::string("\"views\":[") + ViewsJson(views[0]) + "," + ViewsJson(views[1]) + "]" + o + "\"title\":" + Json(v.title) +
            ",\"context\":" + Json(v.context) + ",\"payout\":" + Json(v.payout) + ",\"vviews\":[" + ViewsJson(v.fighters[0]) + "," +
            ViewsJson(v.fighters[1]) + "]}");
    }
    // wager / settle
    for (int n = 0; n < 20; ++n)
    {
        const int side = static_cast<int>(R() % 2), stake = static_cast<int>(R() % 10000) - 5, payout = static_cast<int>(R() % 50000);
        std::string message, fields; Bookie::Wager(side, stake, payout, message, fields);
        char b[96]; snprintf(b, sizeof b, "{\"k\":\"wager\",\"side\":%d,\"stake\":%d,\"payout\":%d,\"message\":", side, stake, payout);
        Line(b + Json(message) + ",\"fields\":" + Json(fields) + "}");
    }
    for (int n = 0; n < 40; ++n)
    {
        const bool pending = R() % 2 == 0; const int winner = static_cast<int>(R() % 4) - 2, side = static_cast<int>(R() % 3) - 1,
            credit = static_cast<int>(R() % 9000) - 100;
        char b[96]; snprintf(b, sizeof b, "{\"k\":\"settle\",\"pending\":%d,\"winner\":%d,\"side\":%d,\"credit\":%d,\"message\":",
            pending ? 1 : 0, winner, side, credit);
        Line(b + Json(Bookie::SettleMessage(pending, winner, side, credit)) + "}");
    }
    std::printf("]\n");
    return 0;
}
