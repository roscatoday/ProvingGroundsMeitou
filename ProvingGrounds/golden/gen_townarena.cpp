// sources: BalanceTuning.cpp ArenaMarks.cpp SparPodium.cpp MatchRules.cpp pgconfig_stub.cpp
// Task 1.13b: the PURE parts of TownArena.cpp. TownArena.cpp includes kenshi/*.h, Windows.h and PGLog.h, so it cannot be linked here:
// the functions below are COPIED VERBATIM from it (line ranges on each), with only the engine reads replaced by plain values, each
// replacement named in a "// was:" comment. A Character* becomes an Actor* (the plain fields the C++ reads off it); the module globals
// keep their names (namespace TA). They are linked with the real policy headers (TownChallengePolicy.h, TownArenaPolicy.h,
// TownArenaRuntimePolicy.h, TownFighterCatalog.h, TownMatchmakingPolicy.h, TownChallengeBuyInPolicy.h, TownAftercarePolicy.h), the real
// ArenaMarks.cpp / BalanceTuning.cpp and the PGConfig stub. %d, %u and %02d print as the original; %.Nf is glibc's, which is NOT the
// shipped original's msvcr100 on ties (11115.125 at %.2f: glibc "11115.12", msvcr100 "11115.13"), so the golden tests run Core under
// CMath.UseGlibcForTest (see Core/Msvcr100Format.cs). Lines, each
// {"k": kind, inputs..., outputs...}:
//   "ready"     Ready (+ its log line)                                   TownArena.cpp:297-346
//   "named"     NamedOpponentUnavailableReason                            TownArena.cpp:177-208
//   "preview"   PreviewUnavailableReason (+ EligibleOpponent)             TownArena.cpp:168-173, 210-232
//   "quote"     QuoteFor (+ MatchesOpponent, LegacyOpponentId, PersistentId) TownArena.cpp:141-155, 715-745
//   "readiness" GetPlayerReadiness                                        TownArena.cpp:931-950
//   "mode"      MatchModeFor                                              TownArena.cpp:234-238
//   "refresh"   GetNextChallengeRefreshCaption                            TownArena.cpp:467-476
//   "card"      DisplayedOffer, GetChallengeRarity, IsUniqueLegendaryChallenge, GetChallengeUnavailableReason, GetChallengeCaption,
//               GetChallengeBuyIn, StoredPortraitName, the portrait plan and match   TownArena.cpp:924-1055, 1150-1182
//   "direct"    GetDirectCaption                                          TownArena.cpp:1056-1061
//   "progress"  GetProgressCaption                                        TownArena.cpp:1062-1072
//   "lineup"    PlayerLineupKey                                           TownArena.cpp:157-166
//   "pkey"      UpdateMatchmakingPreview's card key                       TownArena.cpp:755-759
//   "savekey"   Challenges()'s save-key fold                              TownArena.cpp:244-248
//   "paid"      BuyChallengeRefresh's card rewrite                        TownArena.cpp:527-536
//   "search"    the search request's seed / named anchor, the searching text, the failed-search reason   TownArena.cpp:815-829, 870-895
//   "plan"      PlanNpcBout's capacity and start hour                     TownArena.cpp:657-658, 683-685
//   "result"    OnMatchResult's winner, bookie side and defeated bits     TownArena.cpp:1082-1083, 1094-1095, 1099-1100, 1114-1133
//   "marks"     BeginEvent's marks multiplier and bonus                   TownArena.cpp:1725-1733
//   "canbuy"    CanBuyChallengeRefresh                                    TownArena.cpp:483-490
//   "msg"       every formatted Status / Finish / log text and the choice between texts (case "m" names it)
//   "const"     every fixed Status / Finish / CancelPlannedNpcBout text, in source order
#include "PGConfig.h"
#include "TownArenaPolicy.h"
#include "TownArenaRuntimePolicy.h"
#include "TownChallengePolicy.h"
#include "TownChallengeBuyInPolicy.h"
#include "TownMatchmakingPolicy.h"
#include "TownAftercarePolicy.h"
#include "TownFighterCatalog.h"
#include "RosterStatus.h"
#include "ArenaMarks.h"
#include "BalanceTuning.h"
#include "MatchRules.h"
#include "SparPodium.h"
#include "pgconfig_stub.h"
#include <algorithm>
#include <climits>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <sstream>
#include <string>
#include <vector>

namespace
{
    unsigned s_seed = 20261114u;
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
    std::string JsonOrNull(const char* value) { return value ? Json(value) : std::string("null"); }
    unsigned long long Bits(double d) { unsigned long long b; std::memcpy(&b, &d, 8); return b; }
    unsigned FBits(float f) { unsigned b; std::memcpy(&b, &f, 4); return b; }
    std::string I(long long v) { return std::to_string(v); }
    std::string List(const std::vector<std::string>& v)
    {
        std::string out = "[";
        for (size_t i = 0; i < v.size(); ++i) { if (i) out += ","; out += Json(v[i]); }
        return out + "]";
    }
    bool g_first = true;
    void Line(const std::string& body) { std::printf("%s%s\n", g_first ? "[" : ",", body.c_str()); g_first = false; }
}

// The plain fields the copies read off a Character*. was: the engine calls named on each field.
struct Actor
{
    bool valid;                    // was: c->isValid()
    bool hasInstance;              // was: c->getInstanceID() != NULL
    std::string uid;               // was: c->getInstanceID()->uid
    bool hasData;                  // was: c->getGameData() != NULL
    std::string stringId;          // was: c->getGameData()->stringID
    std::string name;              // was: c->getName()
    std::string identity;          // was: TownMatchmakingRuntime::Identity(c)
    bool quarantined;              // was: IngressQuarantined(c)
    bool playerCharacter;          // was: c is in ou->player->getAllPlayerCharacters()
    bool dead, unconscious, inCombat, needsAid;   // was: isDead(), isUnconcious(), isInCombatMode(true, true), ArenaMedical::NeedsStabilization(c)
    bool rosterOk;                 // was: RosterStatus::Read(c, health, reason)'s result
    RosterStatus::Snapshot health; // was: the snapshot it filled
    std::string rosterError;       // was: what it wrote through reason
    bool medicallyAble;            // was: the four MedicalSystem reads at :325-326
    bool inAftercare, inLimbShop, fighter;   // was: TownAftercare::IsManaged(c), TownLimbShop::IsManaged(c), TownArena::IsFighter(c)
    double ability;                // was: TownMatchmakingRuntime::Ability(c)
    float townRating;              // was: LeaderboardStore::GetRating(c, LeaderboardData::Town)
    std::string selectionIdentity; // was: TownMatchmakingRuntime::SelectionIdentity(c)
    bool inRegistryTown;           // was: registry && registry->isValid() && c->getCurrentTownLocation() == registry->getTown()
};


// was: TownMatchmakingRuntime::Snapshot (characters: the std::map's values, in key order; medics: only their count is read)
struct Snapshot
{
    std::vector<TownMatchmakingPolicy::Fighter> players, npcs;
    std::vector<Actor*> characters;
    std::vector<int> medics;
    std::string context, availability, playerBlocker;
    bool playersReady;
};

namespace TA
{
    bool g_hasPlayerInterface = true;   // was: ou && ou->player
    bool g_quietDiscovery = false;
    std::string g_detail;               // was: PGLog::Debug(detail) when !g_quietDiscovery
    bool g_detailWritten = false;
    TownChallengePolicy::Card g_card;   // was: Challenges() / LeaderboardStore::GetTownChallenges()
    int g_queuedChallenge = -1, g_activeChallenge = -1;
    bool g_bookingPending = false;
    TownChallengePolicy::Offer g_previews[5], g_directPreview, g_challengeReservation;
    TownChallengeBuyInPolicy::Quote g_buyInQuotes[6];
    TownChallengeBuyInPolicy::Ticket g_buyInTicket;
    std::string g_unavailableReasons[6];

    // TownArena.cpp:136-140 (was: c->getGameData())
    const char* Id(Actor* c)
    {
        return c && c->hasData ? c->stringId.c_str() : NULL;   // was: c && c->getGameData() ? c->getGameData()->stringID.c_str() : NULL
    }
    // TownArena.cpp:141-145 (was: c->getInstanceID())
    std::string LegacyOpponentId(Actor* c)
    {
        return c ? TownChallengePolicy::OpponentKey(c->hasInstance ? c->uid : "", Id(c) ? Id(c) : "", c->name) : std::string();   // was: c->getInstanceID(), its uid, c->getName()
    }
    // TownArena.cpp:146-150
    std::string PersistentId(Actor* c)
    {
        const std::string id = c->identity;   // was: TownMatchmakingRuntime::Identity(c)
        return id.empty() ? LegacyOpponentId(c) : id;
    }
    // TownArena.cpp:151-155
    bool MatchesOpponent(Actor* c, const std::string& key)
    {
        return c && !key.empty() && (PersistentId(c) == key || LegacyOpponentId(c) == key ||
            TownChallengePolicy::OpponentKey("", Id(c) ? Id(c) : "", c->name) == key);   // was: c->getName()
    }
    // TownArena.cpp:157-166 (was: SelectionIdentity(players[i]))
    std::string PlayerLineupKey(const std::vector<Actor*>& players)
    {
        std::vector<std::string> ids;
        for (size_t i = 0; i < players.size(); ++i)
            ids.push_back(players[i]->selectionIdentity);   // was: TownMatchmakingRuntime::SelectionIdentity(players[i])
        std::sort(ids.begin(), ids.end());
        std::ostringstream key;
        for (size_t i = 0; i < ids.size(); ++i) key << '|' << ids[i].size() << ':' << ids[i];
        return key.str();
    }
    // TownArena.cpp:168-173
    bool EligibleOpponent(const Snapshot& snapshot, const std::string& id)
    {
        for (size_t i = 0; i < snapshot.npcs.size(); ++i)
            if (snapshot.npcs[i].id == id) return true;
        return false;
    }

    // TownArena.cpp:297-346
    bool Ready(Actor* c, bool doctor, bool bookedPlayer = false, std::string* reason = NULL)
    {
        if (reason) *reason = "health data unavailable";
        if (!c || !c->valid) return false;   // was: c->isValid()
        if (!doctor && c->quarantined) {   // was: IngressQuarantined(c)
            if (reason) *reason = "could not reach arena formation";
            return false;
        }
        TownArenaRuntimePolicy::Readiness r = {};
        r.valid = true;
        if (!g_hasPlayerInterface) return false;   // was: if (!ou || !ou->player) return false;
        if (c->playerCharacter) r.player = true;   // was: the getAllPlayerCharacters() loop
        if (bookedPlayer)
        {
            if (!r.player) return false;
            r.player = false; // Same recovery thresholds, explicitly player-owned.
        }
        r.dead = c->dead;   // was: c->isDead()
        r.unconscious = c->unconscious;   // was: c->isUnconcious()
        r.inCombat = c->inCombat;   // was: c->isInCombatMode(true, true)
        r.needsAid = c->needsAid;   // was: ArenaMedical::NeedsStabilization(c)
        RosterStatus::Snapshot health = {};
        // was: if (!RosterStatus::Read(c, health, reason)) return false;  (Read writes its error, or clears reason on success)
        if (reason) *reason = c->rosterError;
        if (!c->rosterOk) return false;
        health = c->health;
        if (reason) reason->clear();
        r.blood = health.blood;
        r.minimumHealth = health.lowestLimb;
        const bool medicallyAble = c->medicallyAble;   // was: MedicalSystem* med = c->getMedical(); !med->isInBloodlossTrauma() &&
        //   !med->isProbablyDying() && !med->isCrippled() && med->hasAnArmToFightWith()
        const bool ready = doctor ? TownArenaRuntimePolicy::CanProvideAid(r, medicallyAble) :
            TownArenaRuntimePolicy::CanParticipate(r);
        if (reason)
        {
            if (ready) reason->clear();
            else if (r.player) *reason = "belongs to the player squad";
            else if (r.dead) *reason = "dead";
            else if (r.unconscious) *reason = "unconscious";
            else if (r.inCombat) *reason = "already in combat";
            else if (r.needsAid) *reason = "needs first aid";
            else if (doctor && !medicallyAble) *reason = "native medical capacity check failed";
            else *reason = "recovering";
        }
        char detail[256];
        sprintf_s(detail, "Town candidate %s: ready=%d health=%.3f blood=%.3f player=%d dead=%d ko=%d combat=%d aid=%d",
            Id(c) ? Id(c) : "unknown", ready ? 1 : 0, r.minimumHealth, r.blood,
            r.player, r.dead, r.unconscious, r.inCombat, r.needsAid);
        if (!g_quietDiscovery) { g_detail = detail; g_detailWritten = true; }   // was: if (!g_quietDiscovery) PGLog::Debug(detail);
        return ready;
    }

    // TownArena.cpp:177-208
    std::string NamedOpponentUnavailableReason(const TownChallengePolicy::Offer& offer,
        const Snapshot& snapshot)
    {
        const std::string label = TownChallengePolicy::Title(offer.encounter);
        const std::string role = TownChallengePolicy::Role(offer.encounter);
        const std::string opponentId = TownChallengePolicy::OpponentId(offer, 0);
        Actor* match = NULL;
        bool ambiguous = false;
        for (std::vector<Actor*>::const_iterator it = snapshot.characters.begin();
            it != snapshot.characters.end(); ++it) {
            Actor* candidate = *it;   // was: std::map<std::string, Character*> iterator, it->second
            if (!candidate || !candidate->valid) continue;   // was: candidate->isValid()
            if ((!opponentId.empty() && MatchesOpponent(candidate, opponentId)) ||
                (opponentId.empty() && TownArenaRuntimePolicy::Equals(Id(candidate), role.c_str()))) {
                if (match && match != candidate) { ambiguous = true; break; }
                match = candidate;
            }
        }
        if (ambiguous) return label + ": identity is ambiguous";
        if (!match) return label + ": not present in the registry town";
        if (match->inAftercare) return label + ": still in aftercare";   // was: TownAftercare::IsManaged(match)
        if (match->inLimbShop) return label + ": waiting for replacement limbs";   // was: TownLimbShop::IsManaged(match)
        if (match->fighter) return label + ": already assigned to an active bout";   // was: TownArena::IsFighter(match)
        std::string reason;
        const bool quiet = g_quietDiscovery;
        g_quietDiscovery = true;
        const bool ready = Ready(match, false, false, &reason);
        g_quietDiscovery = quiet;
        if (!ready) return label + ": " + (reason.empty() ? "not ready to fight" : reason);
        if (!(match->ability > 0)) return label + ": combat stats unavailable";   // was: TownMatchmakingRuntime::Ability(match)
        return label + ": unavailable for this matchup";
    }

    // TownArena.cpp:210-232
    std::string PreviewUnavailableReason(const TownChallengePolicy::Offer& offer,
        const Snapshot& snapshot)
    {
        if (!snapshot.playersReady) return snapshot.playerBlocker.empty() ?
            "Selected fighters are unavailable or recovering" : snapshot.playerBlocker;
        if (!TownChallengePolicy::Resolved(offer)) {
            if (TownChallengePolicy::NamedForDivision(offer.encounter, offer.division))
                return NamedOpponentUnavailableReason(offer, snapshot);
            return "No suitable recovered opponents for this team";
        }
        if (static_cast<int>(snapshot.players.size()) != TownChallengePolicy::Players(offer))
            return "Selected lineup no longer matches this challenge";
        for (int i = 0; i < TownChallengePolicy::Opponents(offer); ++i)
            if (!EligibleOpponent(snapshot, TownChallengePolicy::OpponentId(offer, i))) {
                if (i == 0 && TownChallengePolicy::NamedForDivision(offer.encounter, offer.division))
                    return NamedOpponentUnavailableReason(offer, snapshot);
                return TownChallengePolicy::OpponentName(offer, i) + " is unavailable or recovering";
            }
        const int participants = TownChallengePolicy::Players(offer) + TownChallengePolicy::Opponents(offer);
        if (static_cast<int>(snapshot.medics.size()) < TownAftercarePolicy::RequiredMedics(participants))
            return "Waiting for medical capacity";
        return "";
    }

    // TownArena.cpp:234-238
    MatchRules::MatchMode MatchModeFor(const TownChallengePolicy::Offer& offer)
    {
        return offer.mode == TownChallengePolicy::ModeTeams1v1 ?
            MatchRules::ModeTeams1v1 : MatchRules::ModeTeamAvB;
    }

    // TownArena.cpp:244-248 (was: LeaderboardStore::GetActiveSaveKey())
    void FoldSaveKey(TownChallengePolicy::Card& card, const std::string& key)
    {
        if (card.offers[0].epoch == -2)
        {
            for (size_t i = 0; i < key.size(); ++i) card.seed = card.seed * 33u + static_cast<unsigned char>(key[i]);
        }
    }

    // TownArena.cpp:467-476 (was: ou, its game hours, PGConfig::ChallengeValues().refreshHours)
    std::string GetNextChallengeRefreshCaption(bool hasOu, double totalHours, int refreshHours) {
        if (!hasOu) return "Next Refresh: unavailable";
        const double hours = TownArenaPolicy::HoursUntilChallengeRefresh(
            totalHours, refreshHours);
        if (!(hours >= 0.0)) return "Next Refresh: unavailable";
        const int totalMinutes = static_cast<int>(std::ceil(hours * 60.0));
        char caption[64];
        sprintf_s(caption, "Next Refresh: %dh %02dm", totalMinutes / 60, totalMinutes % 60);
        return caption;
    }

    // TownArena.cpp:483-490 (was: the save, ou, the registry, GetChallengeRefreshCost(), TownChallengeWallet::Balance(); phase 1 = Preparing,
    // 2 = Fighting)
    bool CanBuyChallengeRefresh(bool hasSave, bool hasOu, bool registryValid, int g_phase, int cost, int balance) {
        const int Preparing = 1, Fighting = 2;
        if (!hasSave || !hasOu || !registryValid ||
            g_bookingPending || g_queuedChallenge >= 0 || g_activeChallenge >= 0 ||
            g_phase == Preparing || g_phase == Fighting) return false;
        return cost > 0 && balance >= cost;
    }

    // TownArena.cpp:527-536 (card = the module's card, epoch = ChallengeCardEpoch above it)
    void PaidRefresh(TownChallengePolicy::Card& card, int epoch) {
        TownChallengePolicy::Card refreshed = card;
        refreshed.seed = card.seed ^ (0x9e3779b9u * card.paidRefreshNonce);
        if (!refreshed.seed) refreshed.seed = 0x6d2b79f5u;
        for (int slot = 0; slot < 5; ++slot)
            refreshed.offers[slot] = TownChallengePolicy::Offer();
        TownChallengePolicy::RefreshGenerated(refreshed, epoch, -1);
        for (int slot = 0; slot < 5; ++slot)
            card.offers[slot] = refreshed.offers[slot];
        if (card.paidRefreshCount < INT_MAX) ++card.paidRefreshCount;
        if (card.paidRefreshNonce < UINT_MAX) ++card.paidRefreshNonce;
    }

    // TownArena.cpp:657-658 (was: snapshot.medics.size(), g_phase == Aftercare, g_fighterCount)
    int AmbientCapacity(int medics, bool aftercare, int g_fighterCount) {
        const int capacity = (std::min)(8, medics +
            (aftercare ? g_fighterCount : 0));
        return capacity;
    }
    // TownArena.cpp:682-685 (was: ou->getTimeStamp_inGameHours().getTotalHours())
    double NextNpcHours(const TownChallengePolicy::Card& card, double totalHours) {
        const BalanceTuning::Values& tuning = BalanceTuning::Get();
        const unsigned delay = TownMatchmakingPolicy::Detail::Hash(card.ambientSeed, "next NPC start") % 61;
        return std::ceil((totalHours +
            tuning.npcLeadHoursMin + (delay / 60.0) * tuning.npcLeadHoursSpread) * 60.0) / 60.0;
    }

    // TownArena.cpp:715-745
    TownChallengeBuyInPolicy::Quote QuoteFor(const TownChallengePolicy::Offer& offer,
        const Snapshot& snapshot, int division,
        double* ownScore = NULL, double* enemyScore = NULL) {
        TownChallengeBuyInPolicy::Quote unavailable;
        if (!snapshot.playersReady || !TownChallengePolicy::Resolved(offer) ||
            static_cast<int>(snapshot.players.size()) != TownChallengePolicy::Players(offer) ||
            (offer.generated && !PreviewUnavailableReason(offer, snapshot).empty())) return unavailable;
        double own[3], ownRatings[3], enemy[4], enemyRatings[4];
        const int count = TownChallengePolicy::Opponents(offer);
        if (count < 1 || count > 4 || snapshot.players.size() > 3) return unavailable;
        for (size_t i = 0; i < snapshot.players.size(); ++i) {
            own[i] = snapshot.players[i].ability;
            ownRatings[i] = snapshot.players[i].mmr;
        }
        for (int member = 0; member < count; ++member) {
            Actor* match = NULL;
            // was: std::map<std::string, Character*> iterator; *it below was it->second
            for (std::vector<Actor*>::const_iterator it = snapshot.characters.begin(); it != snapshot.characters.end(); ++it)
                if (*it && MatchesOpponent(*it, TownChallengePolicy::OpponentId(offer, member))) {
                    if (match && match != *it) return unavailable;
                    match = *it;
                }
            if (!match) return unavailable;
            enemy[member] = match->ability;   // was: TownMatchmakingRuntime::Ability(match)
            enemyRatings[member] = match->townRating;   // was: LeaderboardStore::GetRating(match, LeaderboardData::Town)
        }
        const double own_ = TownMatchmakingPolicy::TeamScore(own, ownRatings, static_cast<int>(snapshot.players.size()));
        const double enemy_ = TownMatchmakingPolicy::TeamScore(enemy, enemyRatings, count);
        if (ownScore) *ownScore = own_;
        if (enemyScore) *enemyScore = enemy_;
        return TownChallengeBuyInPolicy::Price(division, own_, enemy_);
    }

    // TownArena.cpp:815-829 (was: result.match; snapshot = the frozen preview snapshot)
    std::string SearchFailure(const TownMatchmakingPolicy::Match& match, const TownChallengePolicy::Offer& offer,
        const Snapshot& snapshot, const TownChallengePolicy::Card& card) {
        std::string reason;
        if (match.failure == TownMatchmakingPolicy::Match::PlayersTooWeak)
            reason = "Selected fighter is too weak for this challenge.";
        else
        {
            const int rarity = TownChallengePolicy::OfferRarity(offer, card.division);
            const std::string anchor = rarity == 3 &&
                TownChallengePolicy::NamedForDivision(offer.encounter, card.division) ?
                TownChallengePolicy::Role(offer.encounter) : "";
            bool anchorReady = anchor.empty();
            for (size_t i = 0; i < snapshot.npcs.size(); ++i)
                if (snapshot.npcs[i].role == anchor) anchorReady = true;
            reason = anchorReady ?
                "No suitable recovered opponents for this challenge." :
                NamedOpponentUnavailableReason(offer, snapshot);
        }
        return reason;
    }
    // TownArena.cpp:870-871
    std::string SearchingReason(const Snapshot& snapshot) {
        return snapshot.playersReady ?
            "Searching eligible opponents..." : snapshot.playerBlocker;
    }
    // TownArena.cpp:881-895 (request.seed, requiredRole, forceSoloNamed; rarity from :874)
    void SearchRequest(const TownChallengePolicy::Offer& offer, int slot, const TownChallengePolicy::Card& card,
        unsigned& seed, std::string& requiredRole, bool& forceSoloNamed) {
        const int rarity = TownChallengePolicy::OfferRarity(offer, card.division);
        requiredRole.clear(); forceSoloNamed = false;
        seed = offer.draw ^ (static_cast<unsigned>(offer.epoch) * 1664525u) ^
            (slot * 1013904223u);
        if (rarity == 3 && TownChallengePolicy::NamedForDivision(offer.encounter, card.division)) {
            requiredRole = TownChallengePolicy::Role(offer.encounter);
            const unsigned victory = TownChallengePolicy::UniqueVictoryForEncounter(offer.encounter);
            const bool defeated = offer.encounter == TownChallengePolicy::EncounterSkarn ?
                card.skarnWon : victory && (card.uniqueWins & victory) != 0;
            forceSoloNamed = !defeated;
        }
    }

    // TownArena.cpp:924-930 (was: IsChallengeVisible = Visible over the same card)
    TownChallengeBuyInPolicy::Quote GetChallengeBuyIn(int slot) {
        if (slot < 0 || slot >= 6 || !TownChallengePolicy::Visible(g_card, slot)) return TownChallengeBuyInPolicy::Quote();   // was: IsChallengeVisible(slot)
        if ((slot == g_queuedChallenge || slot == g_activeChallenge) && g_buyInTicket.quote.Valid()) return g_buyInTicket.quote;
        const TownChallengePolicy::Card& card = g_card;   // was: LeaderboardStore::GetTownChallenges()
        if (card.offers[slot].state != TownArenaPolicy::OfferAvailable) return TownChallengeBuyInPolicy::Quote();
        return g_buyInQuotes[slot];
    }
    // TownArena.cpp:931-950 (the QuietScope keeps Ready's log line off)
    bool GetPlayerReadiness(Actor* player, std::string& reason)
    {
        const bool previous = g_quietDiscovery; g_quietDiscovery = true;
        struct Restore { bool p; ~Restore() { g_quietDiscovery = p; } } restore = { previous };   // was: struct QuietScope { ... } quiet;
        if (!Ready(player, false, true, &reason)) return false;
        if (!player->inRegistryTown)   // was: Building* registry = g_townRegistry.getBuilding();
        //   if (!registry || !registry->isValid() || player->getCurrentTownLocation() != registry->getTown())
        {
            reason = "not at this town registry";
            return false;
        }
        if (player->inAftercare) { reason = "still in aftercare"; return false; }   // was: TownAftercare::IsManaged(player)
        if (player->selectionIdentity.empty()) { reason = "live character handle unavailable"; return false; }   // was: TownMatchmakingRuntime::SelectionIdentity(player)
        if (!(player->ability > 0)) { reason = "combat stats unavailable"; return false; }   // was: TownMatchmakingRuntime::Ability(player)
        reason.clear();
        return true;
    }
    // TownArena.cpp:951-958
    TownChallengePolicy::Offer DisplayedOffer(const TownChallengePolicy::Card& card, int slot)
    {
        TownChallengePolicy::Offer offer = slot == g_queuedChallenge || slot == g_activeChallenge ?
            g_challengeReservation : card.offers[slot].state == TownArenaPolicy::OfferAvailable ? g_previews[slot] :
            TownChallengePolicy::PreviewSource(card, slot);
        offer.state = card.offers[slot].state;
        return offer;
    }
    // TownArena.cpp:959-970
    std::string StoredPortraitName(const TownChallengePolicy::Offer& offer, int member) {
        std::string name = TownChallengePolicy::OpponentName(offer, member);
        // Older generated previews stored the catalog description with the name.
        // Remove only that exact known suffix, preserving parentheses in names.
        const TownFighterCatalog::Entry* entry = TownFighterCatalog::Find(offer.roles[member].c_str());
        if (entry) {
            const std::string suffix = std::string(" (") + TownChallengePolicy::DivisionName(entry->tier) + " " + entry->type + ")";
            if (name.size() >= suffix.size() && name.compare(name.size() - suffix.size(), suffix.size(), suffix) == 0)
                name.resize(name.size() - suffix.size());
        }
        return name.empty() ? "Unavailable" : name;
    }
    // TownArena.cpp:971-1031 (was: Actor* opponents -> the matched actor's index in `actors`, -1 = NULL; `actors` = the deduplicated
    // nearby characters in the registry town; the early return at :1005 is `actorsAvailable`)
    void GetChallengePortraits(std::vector<int> (&opponents)[6], std::vector<std::string> (&names)[6],
        bool actorsAvailable, const std::vector<Actor*>& actors)
    {
        const TownChallengePolicy::Card& card = g_card;   // was: Challenges()
        TownChallengePolicy::Offer offers[6];
        bool any = false;
        for (int slot = 0; slot < 6; ++slot) {
            opponents[slot].clear();
            names[slot].clear();
            if (slot == 5) {
                offers[slot] = TownChallengePolicy::Offer();
                offers[slot].encounter = TownChallengePolicy::EncounterSkarn;
                offers[slot].division = 2;
                opponents[slot].resize(1, -1);   // was: resize(1, NULL)
                names[slot].push_back(TownChallengePolicy::Title(TownChallengePolicy::EncounterSkarn));
                any = true;
                continue;
            }
            if (!TownChallengePolicy::Visible(card, slot)) continue;
            offers[slot] = DisplayedOffer(card, slot);
            if (TownChallengePolicy::Resolved(offers[slot])) {
                opponents[slot].resize(TownChallengePolicy::Opponents(offers[slot]), -1);   // was: NULL
                for (size_t member = 0; member < opponents[slot].size(); ++member)
                    names[slot].push_back(StoredPortraitName(offers[slot], static_cast<int>(member)));
            } else if (offers[slot].generated &&
                TownChallengePolicy::OfferRarity(offers[slot], offers[slot].division) == 3 &&
                TownChallengePolicy::NamedForDivision(offers[slot].encounter, offers[slot].division)) {
                // A named Legendary anchor remains the intended opponent even
                // while recovering, so the card can show a faded live portrait.
                opponents[slot].resize(1, -1);   // was: resize(1, NULL)
                names[slot].push_back(TownChallengePolicy::Title(offers[slot].encounter));
            } else continue;
            any = true;
        }
        if (!any || !actorsAvailable) return;   // was: !ou || !registry || !registry->isValid() || !registry->getTown(), then the
        //   getObjectsWithinSphere scan that fills `actors` (valid, in the registry town, deduplicated)
        // Match the booking identity, including legacy keys. An ambiguous key
        // gets no portrait, even when only one of its namesakes is fight-ready.
        for (int slot = 0; slot < 6; ++slot)
            for (size_t member = 0; member < opponents[slot].size(); ++member) {
                int match = -1;   // was: Character* match = NULL;
                const std::string& opponentId = TownChallengePolicy::OpponentId(offers[slot], static_cast<int>(member));
                const char* intendedRole = opponentId.empty() && member == 0 ? TownChallengePolicy::Role(offers[slot].encounter) : "";
                for (size_t i = 0; i < actors.size(); ++i)
                    if ((!opponentId.empty() && MatchesOpponent(actors[i], opponentId)) ||
                        (opponentId.empty() && intendedRole[0] && TownArenaRuntimePolicy::Equals(Id(actors[i]), intendedRole))) {
                        if (match >= 0) { match = -1; break; }   // was: if (match) { match = NULL; break; }
                        match = static_cast<int>(i);   // was: match = actors[i];
                    }
                opponents[slot][member] = match;
                if (match >= 0) names[slot][member] = actors[match]->name;   // was: if (match) names[slot][member] = match->getName();
            }
    }
    // TownArena.cpp:1032-1055
    int GetChallengeRarity(int slot)
    {
        if (slot < 0 || slot >= 5) return -1;
        const TownChallengePolicy::Card& card = g_card;   // was: Challenges()
        if (!TownChallengePolicy::Visible(card, slot)) return -1;
        const TownChallengePolicy::Offer offer = DisplayedOffer(card, slot);
        return TownChallengePolicy::OfferRarity(offer, card.division);
    }
    bool IsUniqueLegendaryChallenge(int slot)
    {
        if (slot < 0 || slot >= 5) return false;
        const TownChallengePolicy::Card& card = g_card;   // was: Challenges()
        if (!TownChallengePolicy::Visible(card, slot)) return false;
        const TownChallengePolicy::Offer offer = DisplayedOffer(card, slot);
        return TownChallengePolicy::OfferRarity(offer, card.division) == 3 &&
            TownChallengePolicy::NamedForDivision(offer.encounter, card.division);
    }
    std::string GetChallengeUnavailableReason(int slot)
    {
        if (slot < 0 || slot >= 5) return "";
        const TownChallengePolicy::Card& card = g_card;   // was: Challenges()
        const TownChallengePolicy::Offer offer = DisplayedOffer(card, slot);
        return offer.state == TownArenaPolicy::OfferAvailable ? g_unavailableReasons[slot] : std::string();
    }
    // TownArena.cpp:1056-1061 (GetDivision() = the card's division, :466)
    std::string GetDirectCaption() {
        if (g_bookingPending) return "Cancel booking";
        if (!TownChallengePolicy::Resolved(g_directPreview)) return "1v1: select one fighter with suitable recovered opposition";
        char marks[40]; sprintf_s(marks, " | Marks: %.2fx", ArenaMarks::DivisionMultiplier(g_card.division));   // was: GetDivision()
        return "Book " + std::string(TownChallengePolicy::DivisionName(g_card.division)) + " 1v1 vs " + g_directPreview.name + marks;   // was: GetDivision()
    }
    // TownArena.cpp:1062-1072
    std::string GetProgressCaption()
    {
        const TownChallengePolicy::Card& card = g_card;   // was: LeaderboardStore::GetTownChallenges()
        char text[128];
        unsigned completed = 0;
        for (unsigned bit = 1; bit <= TownChallengePolicy::UniqueVeyr; bit <<= 1)
            if (card.uniqueWins & bit) ++completed;
        sprintf_s(text, "Challenge wins: %u | Skarn unlock: %u / 5 unique fighters defeated",
            card.challengeWins, completed);
        return text;
    }
    // TownArena.cpp:1150-1182
    std::string GetChallengeCaption(int slot, bool includeLineup = true)
    {
        if (slot < 0 || slot >= 5) return "Unavailable";
        const TownChallengePolicy::Card& card = g_card;   // was: Challenges()
        if (!TownChallengePolicy::Visible(card, slot)) return "";
        TownChallengePolicy::Offer offer = DisplayedOffer(card, slot);
        const int rarity = TownChallengePolicy::OfferRarity(offer, card.division);
        const char* challengeLabel = rarity == 3 &&
            TownChallengePolicy::NamedForDivision(offer.encounter, card.division) ?
            "Unique" : TownChallengePolicy::RarityName(rarity);
        if (offer.generated && !TownChallengePolicy::Resolved(offer) && offer.state == TownArenaPolicy::OfferConsumed)
            return std::string(challengeLabel) +
                " | " + TownChallengePolicy::DivisionName(card.division) + " | " +
                TownChallengePolicy::ModeName(offer.mode) + "\nAttempt used";
        char format[32];
        sprintf_s(format, "%dv%d", TownChallengePolicy::Players(offer), TownChallengePolicy::Opponents(offer));
        std::string lineup = offer.name.empty() ? TownChallengePolicy::Title(offer.encounter) : offer.name;
        for (int i = 1; i < TownChallengePolicy::Opponents(offer); ++i)
            lineup += " + " + (TownChallengePolicy::OpponentName(offer, i).empty() ?
                std::string(TownChallengePolicy::Title(TownChallengePolicy::OpponentRole(offer, i))) : TownChallengePolicy::OpponentName(offer, i));
        const bool resolved = TownChallengePolicy::Resolved(offer);
        std::string text = std::string(challengeLabel) +
            " | " + TownChallengePolicy::DivisionName(card.division) + " | " +
            TownChallengePolicy::ModeName(offer.mode) + " | " + format + "\n" +
            (includeLineup ? lineup + " | " : std::string());
        if (!offer.generated) text = "Legacy | " + text;
        if (offer.generated && !resolved && offer.state == TownArenaPolicy::OfferAvailable)
            return std::string(challengeLabel) +
                " | " + TownChallengePolicy::DivisionName(card.division) + " | " +
                TownChallengePolicy::ModeName(offer.mode) + "\n" + g_unavailableReasons[slot];
        return text + (offer.state == TownArenaPolicy::OfferConsumed ? "Attempt used" :
            offer.state == TownArenaPolicy::OfferBooked ? "Reserved" : !resolved ? "Opponent unavailable" : "Book");
    }

    // TownArena.cpp:1082-1083 (one fighter's test; was: g_fighterTeams[i])
    bool MarksWinner(SparPodium::OutcomeKind outcome, MatchRules::MatchTeam team) {
        return (outcome == SparPodium::OutcomeTeamAWins && team == MatchRules::TeamA) ||
            (outcome == SparPodium::OutcomeTeamBWins && team == MatchRules::TeamB);
    }
    // TownArena.cpp:1094-1095
    int BookieSide(SparPodium::OutcomeKind outcome) {
        return outcome == SparPodium::OutcomeTeamAWins ? 0 :
            outcome == SparPodium::OutcomeTeamBWins ? 1 : -1;
    }
    // TownArena.cpp:1099-1100
    int ChallengeOutcome(SparPodium::OutcomeKind outcome) {
        const bool playerWon = outcome == SparPodium::OutcomeTeamAWins;
        return playerWon ? 1 : outcome == SparPodium::OutcomeTeamBWins ? 0 : -1;
    }
    // TownArena.cpp:1114-1133 (was: g_challengeReservation; the live opponents' Id(opponent), NULL for an invalid one, which `continue`s)
    unsigned Defeated(const TownChallengePolicy::Offer& g_challengeReservation, const std::vector<const char*>& liveIds) {
        unsigned defeated = 0;
        // Use the reserved lineup first: defeated/removed actors can already
        // have an invalid live handle by the time the podium result arrives.
        for (int member = 0; member < TownChallengePolicy::Opponents(g_challengeReservation); ++member)
            for (int encounter = TownChallengePolicy::EncounterSenn;
                encounter <= TownChallengePolicy::EncounterVeyr; ++encounter)
                if (encounter != TownChallengePolicy::EncounterSkarn &&
                    TownArenaRuntimePolicy::Equals(g_challengeReservation.roles[member].c_str(),
                        TownChallengePolicy::Role(encounter)))
                    defeated |= TownChallengePolicy::UniqueVictoryForEncounter(encounter);
        for (size_t i = 0; i < liveIds.size(); ++i)
        {
            if (!liveIds[i]) continue;
            for (int encounter = TownChallengePolicy::EncounterSenn;
                encounter <= TownChallengePolicy::EncounterVeyr; ++encounter)
                if (encounter != TownChallengePolicy::EncounterSkarn &&
                    TownArenaRuntimePolicy::Equals(liveIds[i], TownChallengePolicy::Role(encounter)))
                    defeated |= TownChallengePolicy::UniqueVictoryForEncounter(encounter);
        }
        return defeated;
    }
    // TownArena.cpp:1725-1733 (was: player != NULL, GetDivision(), GetChallengeRarity(g_activeChallenge), g_challengeReservation.encounter)
    void ActiveMarks(bool player, int division, int g_activeChallenge, int activeRarity, int reservationEncounter,
        float& g_activeMarksMultiplier, int& g_activeMarksBonus) {
        g_activeMarksMultiplier = player ? ArenaMarks::ChallengeMultiplier(division,
            g_activeChallenge >= 0 ? activeRarity : 0) *
            static_cast<float>(PGConfig::ChallengeValues().winMarksMultiplier) : 1.0f;
        const int activeEncounter = g_activeChallenge >= 0 ? reservationEncounter : -1;
        const bool skarnChallenge = activeEncounter == TownChallengePolicy::EncounterSkarn;
        const bool uniqueChallenge = skarnChallenge ||
            TownChallengePolicy::UniqueVictoryForEncounter(activeEncounter) != 0;
        g_activeMarksBonus = player && uniqueChallenge ?
            ArenaMarks::UniqueChallengeBonus(division, skarnChallenge) : 0;
    }
}

// ---- the texts ------------------------------------------------------------------------------------------------------------------
namespace Msg
{
    // TownArena.cpp:131 (Status's log line)
    std::string StatusLog(const std::string& message) { return "Proving Grounds: town trial - " + message; }
    // TownArena.cpp:289
    std::string ResolvedLog(const std::string& boundName, const std::string& uid) {
        return "Proving Grounds: challenge opponent resolved: " + boundName + " key=" + uid;
    }
    // TownArena.cpp:362-364
    std::string Settle(int outcome, int credit) {
        char message[128];
        sprintf_s(message, outcome < 0 ? "Challenge cancelled/drawn: %d Cats refunded." :
            outcome == 1 ? "Challenge won: %d Cats total return." : "Challenge lost: buy-in consumed.", credit);
        return message;
    }
    // TownArena.cpp:445-446 (was: character->getName(), TownMatchmakingRuntime::Identity(character))
    std::string QuarantineLog(const std::string& name, const std::string& identity) {
        return "Proving Grounds: ingress fighter quarantined for this world session name=" +
            name + " identity=" + identity;
    }
    // TownArena.cpp:451-459 (was: TownBookie::HasPendingWager(), ArenaIngress::GetStatus(), ArenaIngress::IsPending())
    std::string PreparationCancel(bool wagerPending, const std::string& ingressStatus, bool ingressPending) {
        const bool formationFailure = !ingressPending &&
            ingressStatus.find("NPC formation ") == 0;
        return (formationFailure ? ingressStatus :
            "NPC bout cancelled: locked fighter unavailable or recovering") +
            std::string(wagerPending && !formationFailure ? "; wager refunded" : "");
    }
    // TownArena.cpp:518-519
    std::string RefreshCost(int cost, int balance) {
        char message[128];
        sprintf_s(message, "Refresh costs %d Cats; faction balance: %d Cats", cost, balance);
        return message;
    }
    // TownArena.cpp:557-561
    std::string Refreshed(int cost, int nextCost) {
        char message[128];
        sprintf_s(message, "Challenge offers refreshed for %d Cats; next refresh costs %d Cats",
            cost, nextCost);
        return message;
    }
    // TownArena.cpp:586 (was: result.diagnostics; PGLog::Debug(... .c_str()) returns the text)
    std::string MatchmakingDiscoveryLog(const std::string& diagnostics) {
        return "Proving Grounds: matchmaking discovery - " + diagnostics;
    }
    // TownArena.cpp:812-813
    std::string SearchResolvedLog(int slot, bool bestAvailable) {
        return std::string("Proving Grounds: challenge search resolved slot=") +
            static_cast<char>('0' + slot) + (bestAvailable ? " best-available" : " in-band");
    }
    // TownArena.cpp:1136 (caption = GetProgressCaption())
    std::string ProgressionLog(const std::string& caption) {
        return "Proving Grounds: shared town progression updated - " + caption;
    }
    // TownArena.cpp:1205-1208 (was: g_unavailableReasons[slot]; force = Status's second argument)
    std::string BookUnavailable(int slot, TownArenaPolicy::OfferState state, const TownChallengePolicy::Offer& offer,
        const std::string& reason, bool& force) {
        force = false;
        if (slot < 5 && state == TownArenaPolicy::OfferAvailable && offer.generated &&
            !TownChallengePolicy::Resolved(offer) && !reason.empty())
        { force = true; return reason; }
        else return "Challenge is reserved, used, or an opponent is unavailable";
    }
    // TownArena.cpp:1216-1219 (was: PGConfig::ChallengeValues().cooldownHours, the game hours)
    std::string SkarnCooldown(int cooldownHours, double gameHours, double skarnLastEnd) {
        const double remaining = cooldownHours -
            (gameHours - skarnLastEnd);
        char message[128];
        sprintf_s(message, "Skarn cooldown: %.1f in-game hours remaining", remaining);
        return message;
    }
    // TownArena.cpp:1225-1226
    std::string SelectExactly(int required) {
        char message[96];
        sprintf_s(message, "Select exactly %d squad fighter%s for this challenge", required, required == 1 ? "" : "s");
        return message;
    }
    // TownArena.cpp:1260
    std::string Requires(int buyIn, int balance) {
        char message[128]; sprintf_s(message, "Challenge requires %d Cats; balance: %d Cats. Collect any pending return first.", buyIn, balance);
        return message;
    }
    // TownArena.cpp:1292-1293 (caption = GetChallengeCaption(slot))
    std::string Reserved(const std::string& caption, int buyIn, int payout) {
        char receipt[128]; sprintf_s(receipt, " | Buy-in: %d Cats | Winning total return: %d Cats", buyIn, payout);
        return "Challenge reserved: " + caption + receipt;
    }
    // TownArena.cpp:1357-1358
    std::string Schedule(bool enabled) {
        return enabled ? "Ambient NPC bouts resumed" :
            "Ambient NPC bouts paused; current fight continues";
    }
    // TownArena.cpp:1380, :1402
    std::string SlotReserved(const std::string& name) { return "Next town 1v1 slot reserved for " + name; }
    std::string DirectReserved(const std::string& name) { return "1v1 reserved vs " + name; }
    // TownArena.cpp:1481
    std::string Underway(bool g_playerMatch) { return g_playerMatch ? "Booked town fight underway" : "Town 1v1 underway"; }
    // TownArena.cpp:1593, :1598
    std::string WaitingFor(const std::string& name, int matches) {
        return "Challenge waiting for " + name + (matches == 0 ? ": not found near town" : ": ambiguous identity");
    }
    std::string WaitingBlocker(const std::string& name, const std::string& challengeBlocker) {
        return "Challenge waiting for " + name + ": " + challengeBlocker;
    }
    // TownArena.cpp:1615-1618 (was: snapshot.availability, snapshot.npcs.size(), snapshot.medics.size())
    std::string BoutWaiting(const std::string& availability, size_t npcs, size_t medics) {
        return "Town bout waiting: " + availability +
            (npcs < 2 ? "; need at least 2 eligible opponents" :
            medics < 2 ? "; need at least 2 available medics" :
            "; no balanced lineup within medical capacity");
    }
    // TownArena.cpp:1643-1645
    std::string AmbientLog(size_t a, size_t b, double scoreA, double scoreB) {
        char balance[160];
        sprintf_s(balance, "Proving Grounds: ambient matchup %uv%u scores %.2f / %.2f", static_cast<unsigned>(a),
            static_cast<unsigned>(b), scoreA, scoreB);
        return balance;
    }
    // TownArena.cpp:1692-1694
    std::string MedicsWaiting(size_t doctors, int requiredMedics) {
        char waiting[128];
        sprintf_s(waiting, "Town booking waiting for medics: %u/%d fit and available",
            static_cast<unsigned>(doctors), requiredMedics);
        return waiting;
    }
    // TownArena.cpp:1754, :2019 (was: ArenaIngress::GetStatus())
    std::string StagingFailed(const std::string& status) { return "Town staging failed: " + status; }
    std::string StagingEnded(const std::string& status) { return "Town staging ended: " + status; }
    // TownArena.cpp:1758-1761 (was: matchMode, player != NULL)
    std::string Assembling(MatchRules::MatchMode matchMode, bool player, int playerCount, int npcTeamSize, int neededEnemies) {
        char assembling[96];
        sprintf_s(assembling, "Town %s assembling: %d vs %d",
            matchMode == MatchRules::ModeTeams1v1 ? "Teams 1v1" : "Teams",
            player ? playerCount : npcTeamSize, player ? neededEnemies : neededEnemies - npcTeamSize);
        return assembling;
    }
    // TownArena.cpp:2064-2066 (was: TownAftercare::GetStatus(), TownLimbShop::IsActive())
    std::string AftercareDone(const std::string& medicalStatus, bool limbShopActive) {
        return medicalStatus + (limbShopActive ? "; replacement-limb visits pending" : "");
    }

    // Every fixed text, verbatim, with its line. CancelPlannedNpcBout reasons go to TownBookie::CancelNpcBout.
    struct Fixed { int line; const char* text; };
    const Fixed kFixed[] = {
        { 97, "Town trial idle" },
        { 468, "Next Refresh: unavailable" },
        { 493, "Load or start a game before refreshing challenge offers" },
        { 498, "Open the town registry before refreshing challenge offers" },
        { 503, "Finish or cancel the current booking before refreshing challenge offers" },
        { 511, "Challenge refresh is unavailable because the in-game clock is invalid" },
        { 524, "Could not collect the challenge refresh cost" },
        { 618, "Saved NPC lineup and wager waiting for the original fighters to load" },
        { 688, "Next NPC lineup announced; betting open for the scheduled bout" },
        { 692, "Player booking took the next slot" },
        { 695, "Upcoming NPC bout cancelled for player booking; wagers refunded" },
        { 787, "Proving Grounds: started lazy challenge search generation" },
        { 901, "Background matchmaking is unavailable." },
        { 1187, "Load or start a game before booking challenges" },
        { 1192, "Finish the current challenge or cancel the pending booking first" },
        { 1201, "This encounter is not bookable in the selected division" },
        { 1208, "Challenge is reserved, used, or an opponent is unavailable" },
        { 1235, "Every selected fighter must be recovered and in town" },
        { 1245, "Selected lineup changed; review the refreshed cards before booking" },
        { 1254, "Review the refreshed buy-in and odds before booking this challenge" },
        { 1271, "Could not collect the challenge buy-in" },
        { 1347, "Town booking cancelled" },
        { 1355, "NPC scheduling paused" },
        { 1364, "A town booking already holds the next slot" },
        { 1371, "Booking needs a recovered squad member at Scratch's Registry" },
        { 1384, "Arena progress is unavailable; resolve the persistence error and reload" },
        { 1385, "Finish or cancel the current player booking first" },
        { 1391, "Selected lineup changed; review the current 1v1 opponent before booking" },
        { 1396, "The displayed 1v1 lineup is currently unavailable" },
        { 1479, "Your wagered fight is starting." },
        { 1490, "Arena is busy; finish the current operation first" },
        { 1496, "Interact with Scratch's Registry before starting the town trial" },
        { 1518, "No finished Rust Crucible found in this Registry's town" },
        { 1535, "The reserved lineup or division is no longer valid; cancel the booking" },
        { 1602, "Reserved opponent is still recovering" },
        { 1622, "An announced fighter or arena is unavailable" },
        { 1632, "An announced fighter is no longer eligible" },
        { 1650, "Town booking waiting for recovered opponents" },
        { 1655, "Pit fighters are ready, but no Scratch medic is fit and available" },
        { 1667, "Town booking waiting for every selected fighter to recover and return" },
        { 1686, "Town booking unavailable: medic AI control could not be installed" },
        { 1770, "A player booking holds the next town slot" },
        { 1774, "Current bout is already underway" },
        { 1777, "Waiting for recovered NPC fighters and medical capacity to announce the next bout" },
        { 1783, "Scheduled bout cancelled" },
        { 1794, "Town trial cancelled; town AI released" },
        { 1870, "Town event stopped: no player characters remain in Scratch" },
        { 1879, "Scheduled bout cancelled: no player characters remain in Scratch" },
        { 1933, "Saved NPC lineup and wager restored" },
        { 1934, "Saved NPC lineup could not reopen its market" },
        { 1936, "Saved NPC lineup unavailable after one game hour in Scratch" },
        { 1978, "Town queue waiting: return to the booked Registry" },
        { 1986, "Town booking waiting for its fighter to recover and return; cancel to release slot" },
        { 2045, "Town fight finished; medics treating and transferring fighters independently" },
    };
}

// ---- case makers -----------------------------------------------------------------------------------------------------------------
namespace
{
    const char* kIds[] = { "", "96-Proving Grounds.mod", "206-Proving Grounds.mod", "666-Proving Grounds.mod", "675-Proving Grounds.mod",
        "169-Proving Grounds.mod", "616-Proving Grounds.mod", "999-unknown.mod", "167-Proving Grounds.mod", "168-Proving Grounds.mod" };
    const char* kNames[] = { "", "Beep", "Ruka", "Burn (Medium Sabre)", "Agnu (Hard Champion)", "Kang (Easy", "Cat-Lon (Hard Hacker)", "Ressa Vane" };
    const char* kKeys[] = { "", "k1", "k2", "k3", "96-Proving Grounds.mod|Beep", "|96-Proving Grounds.mod|Ruka" };
    const char* kErrors[] = { "health data unavailable", "medical system unavailable", "anatomy is empty", "anatomy part 2/7 has no valid maximum health" };
    const char* kBlockers[] = { "", "Select 1-3 recovered fighters", "Beep: recovering" };

    float Health()
    {
        switch (R() % 6) { case 0: return 0.9f; case 1: return 0.89999998f; case 2: return 1.0f; case 3: return 0.0f;
            case 4: return 0.5f; default: return static_cast<float>(R() % 2001) / 1000.0f - 0.5f; }
    }
    Actor MakeActor()
    {
        Actor a;
        a.valid = R() % 8 != 0;
        a.hasInstance = R() % 5 != 0; a.uid = kKeys[R() % 4];
        a.hasData = R() % 6 != 0; a.stringId = kIds[R() % 10];
        a.name = kNames[R() % 8];
        a.identity = R() % 3 ? kKeys[R() % 4] : "";
        a.quarantined = R() % 7 == 0;
        a.playerCharacter = R() % 4 == 0;
        a.dead = R() % 8 == 0; a.unconscious = R() % 8 == 0; a.inCombat = R() % 8 == 0; a.needsAid = R() % 8 == 0;
        a.rosterOk = R() % 6 != 0;
        a.health.lowestLimb = Health(); a.health.blood = Health(); a.health.recovery = 0; a.health.band = RosterStatusPolicy::Red;
        a.rosterError = a.rosterOk ? "" : kErrors[R() % 4];
        a.medicallyAble = R() % 5 != 0;
        a.inAftercare = R() % 7 == 0; a.inLimbShop = R() % 7 == 0; a.fighter = R() % 7 == 0;
        switch (R() % 5) { case 0: a.ability = 0; break; case 1: a.ability = -1; break; case 2: a.ability = std::nan(""); break;
            default: a.ability = 1.0 + (R() % 100000) / 7.0; }
        a.townRating = R() % 4 ? static_cast<float>(R() % 4000) / 10.0f : 100.0f;
        a.selectionIdentity = R() % 6 ? std::string("sel") + kKeys[R() % 6] : std::string();
        a.inRegistryTown = R() % 6 != 0;
        return a;
    }
    std::string ActorJson(const Actor& a)
    {
        char b[512];
        snprintf(b, sizeof b, "{\"valid\":%d,\"inst\":%d,\"uid\":%s,\"data\":%d,\"sid\":%s,\"name\":%s,\"identity\":%s,\"quar\":%d,\"pc\":%d,"
            "\"flags\":[%d,%d,%d,%d],\"rok\":%d,\"lowest\":%u,\"blood\":%u,\"rerr\":%s,\"able\":%d,\"state\":[%d,%d,%d],\"ability\":%llu,"
            "\"rating\":%u,\"sel\":%s,\"town\":%d}",
            a.valid, a.hasInstance, Json(a.uid).c_str(), a.hasData, Json(a.stringId).c_str(), Json(a.name).c_str(), Json(a.identity).c_str(),
            a.quarantined, a.playerCharacter, a.dead, a.unconscious, a.inCombat, a.needsAid, a.rosterOk, FBits(a.health.lowestLimb),
            FBits(a.health.blood), Json(a.rosterError).c_str(), a.medicallyAble, a.inAftercare, a.inLimbShop, a.fighter, Bits(a.ability),
            FBits(a.townRating), Json(a.selectionIdentity).c_str(), a.inRegistryTown);
        return b;
    }
    TownChallengePolicy::Offer MakeOffer()
    {
        TownChallengePolicy::Offer o;
        static const int encounters[] = { 0, 1, 2, 3, 4, 5, 6, 7, 8, -1, 9 };
        o.encounter = encounters[R() % 11];
        o.epoch = static_cast<int>(R() % 5) - 2;
        o.format = static_cast<int>(R() % 8);
        o.mode = R() % 6 == 0 ? 2 : static_cast<int>(R() % 2);
        o.state = static_cast<TownArenaPolicy::OfferState>(R() % 3);
        o.generated = R() % 3 != 0;
        static const unsigned draws[] = { 0, 3999, 4000, 6499, 6500, 8499, 8500, 9999 };
        o.draw = R() % 3 ? draws[R() % 8] : R() % 10000;
        o.division = static_cast<int>(R() % 3);
        o.playerCount = static_cast<int>(R() % 5);
        o.enemyCount = static_cast<int>(R() % 5);
        o.context = R() % 2 ? "" : "|4:selk1";
        for (int i = 0; i < 4; ++i)
        {
            TownChallengePolicy::OpponentId(o, i) = R() % 4 ? kKeys[R() % 6] : "";
            TownChallengePolicy::OpponentName(o, i) = kNames[R() % 8];
            o.roles[i] = kIds[R() % 10];
        }
        return o;
    }
    std::string OfferJson(const TownChallengePolicy::Offer& o)
    {
        std::string out = "{\"epoch\":" + I(o.epoch) + ",\"enc\":" + I(o.encounter) + ",\"format\":" + I(o.format) + ",\"mode\":" + I(o.mode) +
            ",\"state\":" + I(o.state) + ",\"gen\":" + I(o.generated) + ",\"draw\":" + I(o.draw) + ",\"div\":" + I(o.division) +
            ",\"pc\":" + I(o.playerCount) + ",\"ec\":" + I(o.enemyCount) + ",\"ctx\":" + Json(o.context) + ",\"ids\":[";
        for (int i = 0; i < 4; ++i) out += (i ? "," : "") + Json(TownChallengePolicy::OpponentId(o, i));
        out += "],\"names\":[";
        for (int i = 0; i < 4; ++i) out += (i ? "," : "") + Json(TownChallengePolicy::OpponentName(o, i));
        out += "],\"roles\":[";
        for (int i = 0; i < 4; ++i) out += (i ? "," : "") + Json(o.roles[i]);
        return out + "]}";
    }
    TownMatchmakingPolicy::Fighter MakeFighter(bool npc)
    {
        TownMatchmakingPolicy::Fighter f;
        f.id = npc ? kKeys[R() % 6] : "p";
        f.role = kIds[R() % 10];
        f.ability = R() % 6 ? 1.0 + (R() % 10000) / 9.0 : (R() % 2 ? 0.0 : -2.0);
        f.mmr = R() % 3 ? static_cast<double>(static_cast<float>(R() % 4000) / 10.0f) : 100.0;
        return f;
    }
    std::string FightersJson(const std::vector<TownMatchmakingPolicy::Fighter>& v)
    {
        std::string out = "[";
        for (size_t i = 0; i < v.size(); ++i)
            out += (i ? "," : "") + std::string("[") + Json(v[i].id) + "," + Json(v[i].role) + "," + I((long long)Bits(v[i].ability)) +
                "," + I((long long)Bits(v[i].mmr)) + "]";
        return out + "]";
    }
    // A snapshot; its characters point into `actors`, some entries NULL and some repeated (the same Actor* under two keys).
    Snapshot MakeSnapshot(std::vector<Actor>& actors, std::string& charactersJson)
    {
        Snapshot s;
        s.playersReady = R() % 5 != 0;
        s.playerBlocker = kBlockers[R() % 3];
        const int players = static_cast<int>(R() % 5);
        for (int i = 0; i < players; ++i) s.players.push_back(MakeFighter(false));
        const int npcs = static_cast<int>(R() % 6);
        for (int i = 0; i < npcs; ++i) s.npcs.push_back(MakeFighter(true));
        s.medics.resize(R() % 9);
        s.availability = R() % 2 ? "0 fighters" : "";
        s.context = "ctx";
        actors.clear();
        const int count = static_cast<int>(R() % 6);
        for (int i = 0; i < count; ++i) actors.push_back(MakeActor());
        const int entries = count ? static_cast<int>(R() % 7) : 0;
        charactersJson = "[";
        std::vector<int> picks;
        for (int i = 0; i < entries; ++i) picks.push_back(R() % 9 == 0 ? -1 : static_cast<int>(R() % count));
        for (size_t i = 0; i < picks.size(); ++i)
            charactersJson += (i ? "," : "") + I(picks[i]);
        charactersJson += "]";
        // pointers taken after `actors` is complete (no reallocation)
        for (size_t i = 0; i < picks.size(); ++i) s.characters.push_back(picks[i] < 0 ? NULL : &actors[picks[i]]);
        return s;
    }
    std::string SnapshotJson(const Snapshot& s, const std::vector<Actor>& actors, const std::string& charactersJson)
    {
        std::string a = "[";
        for (size_t i = 0; i < actors.size(); ++i) a += (i ? "," : "") + ActorJson(actors[i]);
        a += "]";
        return "{\"ready\":" + I(s.playersReady) + ",\"blocker\":" + Json(s.playerBlocker) + ",\"players\":" + FightersJson(s.players) +
            ",\"npcs\":" + FightersJson(s.npcs) + ",\"medics\":" + I(s.medics.size()) + ",\"avail\":" + Json(s.availability) +
            ",\"actors\":" + a + ",\"chars\":" + charactersJson + "}";
    }
    // An offer whose opponent ids are drawn from the snapshot's actors or npcs, so the eligible / matched paths are reached.
    void LinkOffer(TownChallengePolicy::Offer& o, const Snapshot& s, std::vector<Actor>& actors)
    {
        for (int i = 0; i < 4; ++i)
        {
            const unsigned pick = R() % 4;
            if (pick == 0 && !s.npcs.empty()) TownChallengePolicy::OpponentId(o, i) = s.npcs[R() % s.npcs.size()].id;
            else if (pick == 1 && !actors.empty()) TownChallengePolicy::OpponentId(o, i) = TA::PersistentId(&actors[R() % actors.size()]);
            else if (pick == 2 && !actors.empty())   // the third key MatchesOpponent accepts: OpponentKey("", Id(c), name)
            {
                Actor* a = &actors[R() % actors.size()];
                TownChallengePolicy::OpponentId(o, i) = TownChallengePolicy::OpponentKey("", TA::Id(a) ? TA::Id(a) : "", a->name);
            }
        }
        if (R() % 3 == 0) { o.generated = true; o.playerCount = static_cast<int>(s.players.size()); }
    }
    std::string QuoteJson(const TownChallengeBuyInPolicy::Quote& q) { return "[" + I(q.buyIn) + "," + I(q.payout) + "]"; }
    void MakeCard(TownChallengePolicy::Card& card)
    {
        card = TownChallengePolicy::Card();
        const uint32_t seedHi = R(), seedLo = R();   // one R() each, in the order g++ evaluated the old unsequenced expression
        card.seed = seedHi * 2654435761u + seedLo;
        if (R() % 5) { const uint32_t ambientHi = R(), ambientLo = R(); card.ambientSeed = ambientHi * 7u + ambientLo; }
        else card.ambientSeed = 0u;
        card.division = static_cast<int>(R() % 3);
        card.uniqueWins = R() % 3 ? R() % 64 : (R() % 2 ? 31u : 0xffffffffu);
        card.challengeWins = R() % 4 ? R() % 50 : 4294967295u;
        card.skarnWon = R() % 2 == 0;
        card.skarnLastEnd = (R() % 1000) / 4.0;
        card.paidRefreshCount = R() % 6 == 0 ? INT_MAX : static_cast<int>(R() % 5);
        card.paidRefreshNonce = R() % 6 == 0 ? UINT_MAX : R() % 5;
        for (int i = 0; i < 5; ++i) card.offers[i] = MakeOffer();
        if (R() % 8 == 0) card.offers[0].epoch = -2;
        card.skarnOffer = MakeOffer();
    }
    std::string CardJson(const TownChallengePolicy::Card& c)
    {
        std::string out = "{\"seed\":" + I(c.seed) + ",\"aseed\":" + I(c.ambientSeed) + ",\"div\":" + I(c.division) + ",\"uw\":" + I(c.uniqueWins) +
            ",\"cw\":" + I(c.challengeWins) + ",\"skarnWon\":" + I(c.skarnWon) + ",\"skarnEnd\":" + I((long long)Bits(c.skarnLastEnd)) +
            ",\"prc\":" + I(c.paidRefreshCount) + ",\"nonce\":" + I(c.paidRefreshNonce) + ",\"offers\":[";
        for (int i = 0; i < 5; ++i) out += (i ? "," : "") + OfferJson(c.offers[i]);
        return out + "],\"skarn\":" + OfferJson(c.skarnOffer) + "}";
    }
}

int main()
{
    using namespace TA;
    // ready
    for (int n = 0; n < 500; ++n)
    {
        Actor a = MakeActor();
        const bool null = R() % 20 == 0, doctor = R() % 3 == 0, booked = R() % 3 == 0, withReason = R() % 6 != 0;
        g_hasPlayerInterface = R() % 12 != 0;
        g_detailWritten = false; g_detail.clear(); g_quietDiscovery = false;
        std::string reason = "unset";
        const bool ready = Ready(null ? NULL : &a, doctor, booked, withReason ? &reason : NULL);
        Line("{\"k\":\"ready\",\"actor\":" + (null ? std::string("null") : ActorJson(a)) + ",\"doctor\":" + I(doctor) + ",\"booked\":" + I(booked) +
            ",\"ui\":" + I(g_hasPlayerInterface) + ",\"withReason\":" + I(withReason) + ",\"out\":" + I(ready) + ",\"reason\":" + Json(reason) +
            ",\"detail\":" + (g_detailWritten ? Json(g_detail) : std::string("null")) + "}");
    }
    g_hasPlayerInterface = true;
    // named / preview / quote
    static const int bases[][3] = { { 100, 250, 500 }, { 120, 300, 800 } };
    for (int n = 0; n < 900; ++n)
    {
        std::vector<Actor> actors; std::string chars;
        Snapshot s = MakeSnapshot(actors, chars);
        TownChallengePolicy::Offer o = MakeOffer();
        LinkOffer(o, s, actors);
        const int bi = static_cast<int>(R() % 2);
        for (int d = 0; d < 3; ++d) PGConfigStub_SetChallengeBase(d, bases[bi][d]);
        const int division = static_cast<int>(R() % 4) - (R() % 5 == 0 ? 1 : 0);
        g_hasPlayerInterface = R() % 15 != 0;
        g_detailWritten = false; g_quietDiscovery = false;
        const std::string named = NamedOpponentUnavailableReason(o, s);
        const std::string preview = PreviewUnavailableReason(o, s);
        double own = -7.5, enemy = -7.5;
        const TownChallengeBuyInPolicy::Quote q = QuoteFor(o, s, division, &own, &enemy);
        const bool logged = g_detailWritten;
        Line("{\"k\":\"quote\",\"offer\":" + OfferJson(o) + ",\"snap\":" + SnapshotJson(s, actors, chars) + ",\"bi\":" + I(bi) +
            ",\"division\":" + I(division) + ",\"ui\":" + I(g_hasPlayerInterface) + ",\"named\":" + Json(named) + ",\"preview\":" + Json(preview) +
            ",\"quote\":" + QuoteJson(q) + ",\"own\":" + I((long long)Bits(own)) + ",\"enemy\":" + I((long long)Bits(enemy)) +
            ",\"logged\":" + I(logged) + "}");
    }
    // quote, built to pass the gates: players 1-3, every opponent id the persistent id of one actor and an eligible npc, medics enough
    for (int n = 0; n < 400; ++n)
    {
        std::vector<Actor> actors; std::string chars;
        Snapshot s;
        s.playersReady = R() % 10 != 0;
        const int players = 1 + static_cast<int>(R() % 3) + (R() % 15 == 0 ? 1 : 0);
        for (int i = 0; i < players; ++i) s.players.push_back(MakeFighter(false));
        const int enemies = 1 + static_cast<int>(R() % 4) + (R() % 15 == 0 ? 1 : 0);
        static const char* ids[] = { "e0", "e1", "e2", "e3", "e4", "e5" };
        for (int i = 0; i < enemies + 1; ++i)
        {
            Actor a = MakeActor(); a.identity = ids[i];
            if (R() % 5) a.ability = 1.0 + (R() % 100000) / 7.0;
            if (R() % 12 == 0) a.identity = ids[0];   // a namesake: ambiguous
            actors.push_back(a);
        }
        chars = "[";
        for (size_t i = 0; i < actors.size(); ++i) chars += (i ? "," : "") + I(i);
        const bool dup = R() % 6 == 0;   // the same Character* under a second key: not ambiguous
        if (dup) chars += ",0";
        chars += "]";
        for (size_t i = 0; i < actors.size(); ++i) s.characters.push_back(&actors[i]);
        if (dup) s.characters.push_back(&actors[0]);
        for (int i = 0; i < enemies; ++i) if (R() % 10) { TownMatchmakingPolicy::Fighter f = MakeFighter(true); f.id = ids[i]; s.npcs.push_back(f); }
        s.medics.resize(R() % 10 ? 10 : 1);
        TownChallengePolicy::Offer o = MakeOffer();
        o.generated = R() % 3 != 0;
        if (o.generated) { o.playerCount = players; o.enemyCount = enemies; }
        for (int i = 0; i < 4; ++i) TownChallengePolicy::OpponentId(o, i) = ids[i];
        if (R() % 10 == 0) TownChallengePolicy::OpponentId(o, static_cast<int>(R() % 4)) = "missing";
        const int bi = static_cast<int>(R() % 2);
        for (int d = 0; d < 3; ++d) PGConfigStub_SetChallengeBase(d, bases[bi][d]);
        const int division = static_cast<int>(R() % 3) + (R() % 12 == 0 ? 2 : 0);
        double own = -7.5, enemy = -7.5;
        g_quietDiscovery = false; g_detailWritten = false;
        const std::string named = NamedOpponentUnavailableReason(o, s);
        const std::string preview = PreviewUnavailableReason(o, s);
        const TownChallengeBuyInPolicy::Quote q = QuoteFor(o, s, division, &own, &enemy);
        Line("{\"k\":\"quote\",\"offer\":" + OfferJson(o) + ",\"snap\":" + SnapshotJson(s, actors, chars) + ",\"bi\":" + I(bi) +
            ",\"division\":" + I(division) + ",\"ui\":" + I(g_hasPlayerInterface) + ",\"named\":" + Json(named) + ",\"preview\":" + Json(preview) +
            ",\"quote\":" + QuoteJson(q) + ",\"own\":" + I((long long)Bits(own)) + ",\"enemy\":" + I((long long)Bits(enemy)) +
            ",\"logged\":" + I(g_detailWritten) + "}");
    }
    g_hasPlayerInterface = true;
    PGConfigStub_Reset();
    // readiness
    for (int n = 0; n < 300; ++n)
    {
        Actor a = MakeActor();
        if (R() % 3) { a.valid = true; a.quarantined = false; a.playerCharacter = true; a.rosterOk = true; a.rosterError = "";   // a fit squad member
            a.health.lowestLimb = 1.0f; a.health.blood = R() % 4 ? 1.0f : 0.5f; a.dead = a.unconscious = a.inCombat = a.needsAid = false; }
        g_hasPlayerInterface = R() % 12 != 0;
        g_detailWritten = false;
        std::string reason = "unset";
        const bool ok = GetPlayerReadiness(&a, reason);
        Line("{\"k\":\"readiness\",\"actor\":" + ActorJson(a) + ",\"ui\":" + I(g_hasPlayerInterface) + ",\"out\":" + I(ok) +
            ",\"reason\":" + Json(reason) + ",\"logged\":" + I(g_detailWritten) + "}");
    }
    g_hasPlayerInterface = true;
    // mode
    for (int m = -1; m <= 3; ++m)
    {
        TownChallengePolicy::Offer o; o.mode = m;
        Line("{\"k\":\"mode\",\"mode\":" + I(m) + ",\"out\":" + I(MatchModeFor(o)) + "}");
    }
    // refresh
    for (int n = 0; n < 300; ++n)
    {
        const bool hasOu = R() % 10 != 0;
        static const int refreshes[] = { 12, 1, 168, 0, 169, 7, 24 };
        const int refresh = refreshes[R() % 7];
        double hours;
        switch (R() % 8) { case 0: hours = 6.0; break; case 1: hours = -1.0; break; case 2: hours = 24000000.5; break;
            case 3: hours = 6.0 + refresh * 3; break; case 4: hours = 6.0 + refresh * 3 - 1.0 / 120.0; break;
            case 5: hours = 6.0 + refresh - 1e-9; break; default: hours = (R() % 1000000) / 37.0; }
        Line("{\"k\":\"refresh\",\"ou\":" + I(hasOu) + ",\"hours\":" + I((long long)Bits(hours)) + ",\"refresh\":" + I(refresh) +
            ",\"text\":" + Json(GetNextChallengeRefreshCaption(hasOu, hours, refresh)) + "}");
    }
    // card
    for (int n = 0; n < 400; ++n)
    {
        MakeCard(g_card);
        g_queuedChallenge = R() % 3 ? -1 : static_cast<int>(R() % 6);
        g_activeChallenge = R() % 3 ? -1 : static_cast<int>(R() % 6);
        g_bookingPending = R() % 4 == 0;
        g_challengeReservation = MakeOffer();
        for (int i = 0; i < 5; ++i) g_previews[i] = MakeOffer();
        static const char* reasons[] = { "", "Searching eligible opponents...", "Senn: still in aftercare" };
        for (int i = 0; i < 6; ++i) g_unavailableReasons[i] = reasons[R() % 3];
        g_buyInTicket = TownChallengeBuyInPolicy::Ticket();
        g_buyInTicket.quote.buyIn = R() % 3 ? 500 : static_cast<int>(R() % 200);
        g_buyInTicket.quote.payout = R() % 3 ? 1500 : 30000;
        for (int i = 0; i < 6; ++i) { g_buyInQuotes[i].buyIn = static_cast<int>(R() % 6000); g_buyInQuotes[i].payout = static_cast<int>(R() % 20000); }
        std::vector<Actor> actors(R() % 6);
        for (size_t i = 0; i < actors.size(); ++i) actors[i] = MakeActor();
        // some actors share ids with displayed opponents, so the portrait match finds them (and duplicates)
        for (size_t i = 0; i < actors.size(); ++i)
            if (R() % 2) {
                const uint32_t memberDraw = R(), previewDraw = R();   // g++ evaluated the two arguments right to left
                actors[i].identity = TownChallengePolicy::OpponentId(g_previews[previewDraw % 5], static_cast<int>(memberDraw % 4));
            }
        std::vector<Actor*> actorPtrs;
        for (size_t i = 0; i < actors.size(); ++i) actorPtrs.push_back(&actors[i]);
        const bool actorsAvailable = R() % 5 != 0;
        std::string slots = "[";
        for (int slot = -1; slot <= 6; ++slot)
        {
            const bool lineup = R() % 3 != 0;
            const TownChallengeBuyInPolicy::Quote q = GetChallengeBuyIn(slot);
            std::string displayed = "null";
            if (slot >= 0 && slot < 5) displayed = OfferJson(DisplayedOffer(g_card, slot));
            std::string portraitNames = "[";
            if (slot >= 0 && slot < 5)
            {
                const TownChallengePolicy::Offer d = DisplayedOffer(g_card, slot);
                for (int m = 0; m < 4; ++m) portraitNames += (m ? "," : "") + Json(StoredPortraitName(d, m));
            }
            portraitNames += "]";
            slots += (slot > -1 ? "," : "") + std::string("{\"slot\":") + I(slot) + ",\"lineup\":" + I(lineup) + ",\"rarity\":" +
                I(GetChallengeRarity(slot)) + ",\"unique\":" + I(IsUniqueLegendaryChallenge(slot)) + ",\"unavail\":" +
                Json(GetChallengeUnavailableReason(slot)) + ",\"caption\":" + Json(GetChallengeCaption(slot, lineup)) + ",\"buyin\":" +
                QuoteJson(q) + ",\"displayed\":" + displayed + ",\"pnames\":" + portraitNames + "}";
        }
        slots += "]";
        std::vector<int> opponents[6]; std::vector<std::string> names[6];
        GetChallengePortraits(opponents, names, actorsAvailable, actorPtrs);
        std::string portraits = "[";
        for (int slot = 0; slot < 6; ++slot)
        {
            portraits += (slot ? "," : "") + std::string("[[");
            for (size_t i = 0; i < opponents[slot].size(); ++i) portraits += (i ? "," : "") + I(opponents[slot][i]);
            portraits += "]," + List(names[slot]) + "]";
        }
        portraits += "]";
        std::string actorsJson = "[";
        for (size_t i = 0; i < actors.size(); ++i) actorsJson += (i ? "," : "") + ActorJson(actors[i]);
        actorsJson += "]";
        std::string previews = "[";
        for (int i = 0; i < 5; ++i) previews += (i ? "," : "") + OfferJson(g_previews[i]);
        previews += "]";
        std::string state = "{\"queued\":" + I(g_queuedChallenge) + ",\"active\":" + I(g_activeChallenge) + ",\"pending\":" + I(g_bookingPending) +
            ",\"reservation\":" + OfferJson(g_challengeReservation) + ",\"previews\":" + previews + ",\"reasons\":" + List(std::vector<std::string>(
            g_unavailableReasons, g_unavailableReasons + 6)) + ",\"ticket\":" + QuoteJson(g_buyInTicket.quote) + ",\"quotes\":[";
        for (int i = 0; i < 6; ++i) state += (i ? "," : "") + QuoteJson(g_buyInQuotes[i]);
        state += "]}";
        Line("{\"k\":\"card\",\"card\":" + CardJson(g_card) + ",\"state\":" + state + ",\"actors\":" + actorsJson + ",\"avail\":" +
            I(actorsAvailable) + ",\"slots\":" + slots + ",\"portraits\":" + portraits + "}");
    }
    // direct / progress
    for (int n = 0; n < 120; ++n)
    {
        MakeCard(g_card);
        g_bookingPending = R() % 5 == 0;
        g_directPreview = MakeOffer();
        Line("{\"k\":\"direct\",\"div\":" + I(g_card.division) + ",\"pending\":" + I(g_bookingPending) + ",\"offer\":" + OfferJson(g_directPreview) +
            ",\"text\":" + Json(GetDirectCaption()) + "}");
        Line("{\"k\":\"progress\",\"uw\":" + I(g_card.uniqueWins) + ",\"cw\":" + I(g_card.challengeWins) + ",\"text\":" +
            Json(GetProgressCaption()) + "}");
    }
    g_bookingPending = false;
    // lineup
    for (int n = 0; n < 80; ++n)
    {
        static const char* sels[] = { "", "b", "a", "ab", "A", "\xc3\xa9t\xc3\xa9", "\xef\xbd\x81", "\xf0\x9f\x98\x80", "10", "9" };
        std::vector<Actor> actors(R() % 5);
        std::vector<Actor*> ptrs;
        std::vector<std::string> in;
        for (size_t i = 0; i < actors.size(); ++i) { actors[i].selectionIdentity = sels[R() % 10]; in.push_back(actors[i].selectionIdentity); }
        for (size_t i = 0; i < actors.size(); ++i) ptrs.push_back(&actors[i]);
        Line("{\"k\":\"lineup\",\"ids\":" + List(in) + ",\"key\":" + Json(PlayerLineupKey(ptrs)) + "}");
    }
    // pkey / savekey / paid / search / plan
    for (int n = 0; n < 200; ++n)
    {
        MakeCard(g_card);
        const std::string lineup = n % 7 == 0 ? "" : "|2:ab|1:c";
        std::ostringstream key;   // TownArena.cpp:755-759
        key << lineup << ':' << g_card.division;
        for (int i = 0; i < 5; ++i)
            key << ':' << g_card.offers[i].epoch << ':' << g_card.offers[i].draw <<
                ':' << g_card.offers[i].encounter << ':' << g_card.offers[i].mode;
        Line("{\"k\":\"pkey\",\"lineup\":" + Json(lineup) + ",\"card\":" + CardJson(g_card) + ",\"key\":" + Json(key.str()) + "}");

        if (R() % 4 == 0) g_card.seed = 0x9e3779b9u * g_card.paidRefreshNonce;   // the refreshed seed is 0: replaced by 0x6d2b79f5
        TownChallengePolicy::Card before = g_card;
        static const char* saves[] = { "", "save_1", "Quicksave", "\xc3\xa9\xe2\x82\xac", "a very long save name with spaces 0123456789" };
        const std::string save = saves[R() % 5];
        FoldSaveKey(g_card, save);
        Line("{\"k\":\"savekey\",\"seed\":" + I(before.seed) + ",\"epoch0\":" + I(before.offers[0].epoch) + ",\"save\":" + Json(save) +
            ",\"out\":" + I(g_card.seed) + "}");

        g_card = before;
        static const int epochs[] = { -1, 0, 5, 1999999, 2000000, -2, 123 };
        const int epoch = epochs[R() % 7];
        PaidRefresh(g_card, epoch);
        Line("{\"k\":\"paid\",\"card\":" + CardJson(before) + ",\"epoch\":" + I(epoch) + ",\"out\":" + CardJson(g_card) + "}");

        g_card = before;
        std::vector<Actor> actors; std::string chars;
        Snapshot s = MakeSnapshot(actors, chars);
        TownChallengePolicy::Offer o = MakeOffer();
        LinkOffer(o, s, actors);
        if (R() % 3 == 0 && !s.npcs.empty()) s.npcs[R() % s.npcs.size()].role = TownChallengePolicy::Role(o.encounter);
        const int slot = static_cast<int>(R() % 5);
        TownMatchmakingPolicy::Match match;
        match.failure = R() % 3 == 0 ? TownMatchmakingPolicy::Match::PlayersTooWeak : TownMatchmakingPolicy::Match::NoFailure;
        unsigned seed; std::string role; bool solo;
        SearchRequest(o, slot, g_card, seed, role, solo);
        g_quietDiscovery = false;
        Line("{\"k\":\"search\",\"card\":" + CardJson(g_card) + ",\"offer\":" + OfferJson(o) + ",\"snap\":" + SnapshotJson(s, actors, chars) +
            ",\"slot\":" + I(slot) + ",\"weak\":" + I(match.failure == TownMatchmakingPolicy::Match::PlayersTooWeak) + ",\"seed\":" + I(seed) +
            ",\"role\":" + Json(role) + ",\"solo\":" + I(solo) + ",\"failure\":" + Json(SearchFailure(match, o, s, g_card)) +
            ",\"searching\":" + Json(SearchingReason(s)) + ",\"direct\":" + I(g_card.seed ^ 0x12345678u) + "}");

        const int medics = static_cast<int>(R() % 12), fighters = static_cast<int>(R() % 9);
        const bool aftercare = R() % 2 == 0;
        double now;
        switch (R() % 4) { case 0: now = 0.0; break; case 1: now = 12.0; break; case 2: now = (R() % 100000) / 60.0; break; default: now = (R() % 1000000) / 7.0; }
        Line("{\"k\":\"plan\",\"medics\":" + I(medics) + ",\"aftercare\":" + I(aftercare) + ",\"fighters\":" + I(fighters) + ",\"capacity\":" +
            I(AmbientCapacity(medics, aftercare, fighters)) + ",\"aseed\":" + I(g_card.ambientSeed) + ",\"now\":" + I((long long)Bits(now)) +
            ",\"start\":" + I((long long)Bits(NextNpcHours(g_card, now))) + "}");
    }
    // result
    for (int n = 0; n < 150; ++n)
    {
        TownChallengePolicy::Offer o = MakeOffer();
        std::vector<const char*> live;
        std::string liveJson = "[";
        const int count = static_cast<int>(R() % 5);
        for (int i = 0; i < count; ++i)
        {
            const char* id = R() % 5 == 0 ? NULL : kIds[R() % 10];
            live.push_back(id);
            liveJson += (i ? "," : "") + JsonOrNull(id);
        }
        liveJson += "]";
        const int outcome = static_cast<int>(R() % 5), team = static_cast<int>(R() % 3);
        Line("{\"k\":\"result\",\"offer\":" + OfferJson(o) + ",\"live\":" + liveJson + ",\"outcome\":" + I(outcome) + ",\"team\":" + I(team) +
            ",\"winner\":" + I(MarksWinner(static_cast<SparPodium::OutcomeKind>(outcome), static_cast<MatchRules::MatchTeam>(team))) +
            ",\"side\":" + I(BookieSide(static_cast<SparPodium::OutcomeKind>(outcome))) + ",\"challenge\":" +
            I(ChallengeOutcome(static_cast<SparPodium::OutcomeKind>(outcome))) + ",\"defeated\":" + I(Defeated(o, live)) + "}");
    }
    // marks
    static const double winMarks[] = { 1.0, 0.5, 2.5, 1.3 };
    static const double uniqueMarks[] = { 1.0, 0.0, 3.0, 1.7 };
    for (int n = 0; n < 200; ++n)
    {
        PGConfigStub_Reset();
        PGConfigStub_Challenges().winMarksMultiplier = winMarks[R() % 4];
        PGConfigStub_Challenges().uniqueBonusMultiplier = uniqueMarks[R() % 4];
        PGConfigStub_Challenges().skarnBonusMultiplier = uniqueMarks[R() % 4];
        const bool player = R() % 4 != 0;
        const int division = static_cast<int>(R() % 4) - (R() % 6 == 0 ? 1 : 0);
        const int active = R() % 3 ? static_cast<int>(R() % 5) : -1;
        const int rarity = static_cast<int>(R() % 5) - 1;
        const int encounter = static_cast<int>(R() % 11) - 1;
        float multiplier; int bonus;
        ActiveMarks(player, division, active, rarity, encounter, multiplier, bonus);
        char b[64]; snprintf(b, sizeof b, "%u", FBits(multiplier));
        Line("{\"k\":\"marks\",\"win\":" + I((long long)Bits(PGConfigStub_Challenges().winMarksMultiplier)) + ",\"unique\":" +
            I((long long)Bits(PGConfigStub_Challenges().uniqueBonusMultiplier)) + ",\"skarn\":" +
            I((long long)Bits(PGConfigStub_Challenges().skarnBonusMultiplier)) + ",\"player\":" + I(player) + ",\"div\":" + I(division) +
            ",\"active\":" + I(active) + ",\"rarity\":" + I(rarity) + ",\"enc\":" + I(encounter) + ",\"mult\":" + b + ",\"bonus\":" + I(bonus) + "}");
    }
    PGConfigStub_Reset();
    // canbuy
    for (int n = 0; n < 120; ++n)
    {
        const bool save = R() % 6 != 0, ou = R() % 8 != 0, registry = R() % 6 != 0;
        g_bookingPending = R() % 6 == 0; g_queuedChallenge = R() % 6 == 0 ? static_cast<int>(R() % 5) : -1;
        g_activeChallenge = R() % 6 == 0 ? static_cast<int>(R() % 5) : -1;
        const int phase = static_cast<int>(R() % 4), cost = static_cast<int>(R() % 4) * 150 - 150, balance = R() % 3 == 0 ? cost : static_cast<int>(R() % 5) * 100;
        Line("{\"k\":\"canbuy\",\"save\":" + I(save) + ",\"ou\":" + I(ou) + ",\"registry\":" + I(registry) + ",\"pending\":" + I(g_bookingPending) +
            ",\"queued\":" + I(g_queuedChallenge) + ",\"active\":" + I(g_activeChallenge) + ",\"phase\":" + I(phase) + ",\"cost\":" + I(cost) +
            ",\"balance\":" + I(balance) + ",\"out\":" + I(CanBuyChallengeRefresh(save, ou, registry, phase, cost, balance)) + "}");
    }
    // msg: one line per text builder call, {"m": builder, "a": its arguments, "out": the text (+ "force")}
    static const char* names[] = { "", "Beep", "Ruka (Hard Sabre)", "Cat-Lon" };
    static const char* statuses[] = { "", "NPC formation failed: Beep blocked", "NPC formation ", "Arena ingress: NPC formation lost", "Waiting" };
    for (int n = 0; n < 40; ++n)
    {
        const std::string name = names[R() % 4], other = statuses[R() % 5];
        const int a = static_cast<int>(R() % 20000) - 100, b = static_cast<int>(R() % 30000) - 50;
        Line("{\"k\":\"msg\",\"m\":\"status\",\"a\":[" + Json(name) + "],\"out\":" + Json(Msg::StatusLog(name)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"resolved\",\"a\":[" + Json(name) + "," + Json(other) + "],\"out\":" + Json(Msg::ResolvedLog(name, other)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"quarantine\",\"a\":[" + Json(name) + "," + Json(other) + "],\"out\":" + Json(Msg::QuarantineLog(name, other)) + "}");
        const bool wager = R() % 2 == 0, pending = R() % 3 == 0;
        Line("{\"k\":\"msg\",\"m\":\"prepcancel\",\"a\":[" + I(wager) + "," + Json(other) + "," + I(pending) + "],\"out\":" +
            Json(Msg::PreparationCancel(wager, other, pending)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"refreshcost\",\"a\":[" + I(a) + "," + I(b) + "],\"out\":" + Json(Msg::RefreshCost(a, b)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"refreshed\",\"a\":[" + I(a) + "," + I(b) + "],\"out\":" + Json(Msg::Refreshed(a, b)) + "}");
        const int slot = static_cast<int>(R() % 5); const bool best = R() % 2 == 0;
        Line("{\"k\":\"msg\",\"m\":\"discovery\",\"a\":[" + Json(other) + "],\"out\":" + Json(Msg::MatchmakingDiscoveryLog(other)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"searchlog\",\"a\":[" + I(slot) + "," + I(best) + "],\"out\":" + Json(Msg::SearchResolvedLog(slot, best)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"progresslog\",\"a\":[" + Json(other) + "],\"out\":" + Json(Msg::ProgressionLog(other)) + "}");
        TownChallengePolicy::Offer o = MakeOffer();
        const int bslot = static_cast<int>(R() % 7) - 1; const int state = static_cast<int>(R() % 3); const std::string reason = R() % 2 ? "" : "Senn: dead";
        bool force;
        const std::string unavailable = Msg::BookUnavailable(bslot, static_cast<TownArenaPolicy::OfferState>(state), o, reason, force);
        Line("{\"k\":\"msg\",\"m\":\"bookunavail\",\"a\":[" + I(bslot) + "," + I(state) + "," + OfferJson(o) + "," + Json(reason) + "],\"out\":" +
            Json(unavailable) + ",\"force\":" + I(force) + "}");
        static const int cooldowns[] = { 24, 0, 1, 200 };
        const int cooldown = cooldowns[R() % 4];
        const double game = (R() % 100000) / 16.0;
        const uint32_t backDraw = R(), nudgeDraw = R();
        const double last = game - (backDraw % 4000) / 16.0 + (nudgeDraw % 9 == 0 ? 0.05 : 0.0);
        Line("{\"k\":\"msg\",\"m\":\"cooldown\",\"a\":[" + I(cooldown) + "," + I((long long)Bits(game)) + "," + I((long long)Bits(last)) + "],\"out\":" +
            Json(Msg::SkarnCooldown(cooldown, game, last)) + "}");
        const int required = static_cast<int>(R() % 5) - 1;
        Line("{\"k\":\"msg\",\"m\":\"exactly\",\"a\":[" + I(required) + "],\"out\":" + Json(Msg::SelectExactly(required)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"requires\",\"a\":[" + I(a) + "," + I(b) + "],\"out\":" + Json(Msg::Requires(a, b)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"reserved\",\"a\":[" + Json(other) + "," + I(a) + "," + I(b) + "],\"out\":" + Json(Msg::Reserved(other, a, b)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"schedule\",\"a\":[" + I(wager) + "],\"out\":" + Json(Msg::Schedule(wager)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"slot\",\"a\":[" + Json(name) + "],\"out\":" + Json(Msg::SlotReserved(name)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"direct\",\"a\":[" + Json(name) + "],\"out\":" + Json(Msg::DirectReserved(name)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"underway\",\"a\":[" + I(pending) + "],\"out\":" + Json(Msg::Underway(pending)) + "}");
        const int matches = static_cast<int>(R() % 3);
        Line("{\"k\":\"msg\",\"m\":\"waitingfor\",\"a\":[" + Json(name) + "," + I(matches) + "],\"out\":" + Json(Msg::WaitingFor(name, matches)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"waitingblocker\",\"a\":[" + Json(name) + "," + Json(other) + "],\"out\":" + Json(Msg::WaitingBlocker(name, other)) + "}");
        const int npcs = static_cast<int>(R() % 4), medics = static_cast<int>(R() % 4);
        Line("{\"k\":\"msg\",\"m\":\"boutwaiting\",\"a\":[" + Json(other) + "," + I(npcs) + "," + I(medics) + "],\"out\":" +
            Json(Msg::BoutWaiting(other, npcs, medics)) + "}");
        const double sa = (R() % 100000) / 8.0, sb = R() % 4 ? (R() % 100000) / 3.0 : 0.125;
        Line("{\"k\":\"msg\",\"m\":\"ambientlog\",\"a\":[" + I(npcs) + "," + I(medics) + "," + I((long long)Bits(sa)) + "," + I((long long)Bits(sb)) +
            "],\"out\":" + Json(Msg::AmbientLog(npcs, medics, sa, sb)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"medics\",\"a\":[" + I(medics) + "," + I(required) + "],\"out\":" + Json(Msg::MedicsWaiting(medics, required)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"stagingfailed\",\"a\":[" + Json(other) + "],\"out\":" + Json(Msg::StagingFailed(other)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"stagingended\",\"a\":[" + Json(other) + "],\"out\":" + Json(Msg::StagingEnded(other)) + "}");
        const int mode = static_cast<int>(R() % 3); const int pc = static_cast<int>(R() % 4), team = static_cast<int>(R() % 5), needed = static_cast<int>(R() % 9);
        Line("{\"k\":\"msg\",\"m\":\"assembling\",\"a\":[" + I(mode) + "," + I(wager) + "," + I(pc) + "," + I(team) + "," + I(needed) + "],\"out\":" +
            Json(Msg::Assembling(static_cast<MatchRules::MatchMode>(mode), wager, pc, team, needed)) + "}");
        Line("{\"k\":\"msg\",\"m\":\"aftercare\",\"a\":[" + Json(other) + "," + I(pending) + "],\"out\":" + Json(Msg::AftercareDone(other, pending)) + "}");
    }
    for (int outcome = -2; outcome <= 2; ++outcome)
        for (int credit = -1; credit <= 1500; credit += 751)
            Line("{\"k\":\"msg\",\"m\":\"settle\",\"a\":[" + I(outcome) + "," + I(credit) + "],\"out\":" + Json(Msg::Settle(outcome, credit)) + "}");
    // const
    for (size_t i = 0; i < sizeof Msg::kFixed / sizeof Msg::kFixed[0]; ++i)
        Line("{\"k\":\"const\",\"line\":" + I(Msg::kFixed[i].line) + ",\"text\":" + Json(Msg::kFixed[i].text) + "}");
    std::printf("]\n");
    return 0;
}
