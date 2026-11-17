// sources: BalanceTuning.cpp
// TownMatchmakingPolicy.h (header-only, links the real BalanceTuning.cpp for the live levers) and TownDiagnosticData.h's name
// functions, as JSON lines in one array. Each line has "k" plus its inputs and outputs.
// Doubles are printed as their IEEE bits (decimal uint64; keys ending in "b", arrays of such numbers named "...b") so NaN, -0 and
// infinities round-trip exactly. Strings are JSON-escaped ('"', '\\' and control bytes as \u00XX, so Key()'s '\0' and '\1' survive).
// A fighter is [id, role, abilityBits, mmrBits]; a history is an array of id arrays.
//
// Shared tables, printed first and referenced by index from the cases:
//   tune     id, ae ri sp cc cs rc us = abilityExponent, ratingInfluence (bits), showcasePercent, ambientCloseCap, ambientCloseSpan,
//            ambientRatioCap, ambientUnderdogSpan (bits). Each search sets BalanceTuning::Get() to its tune row first.
//   roster   id, f = fighters
//   hist     id, h = history
// Searches (each prints the Match M = [valid, a[], b[], scoreAb, scoreBb, diagnostic, diagnosticUnderdog, bestAvailable, failure,
// diagnosticModel, requestedCoverage, actualCoverage, relaxation, modelScoreAb[3], modelScoreBb[3], modelProbabilityAb[3]]
// and the ranking keys X of the search that produced it):
//   sp       SelectPlayer(players p, npcs roster r, division, seed, history h, maxEnemies, maxTotal, role, rarity, solo, t11, diff)
//   amb      SelectAmbient(npcs r, seed in -> seed out, history h, maxTotal)
//   diag     SelectAmbientDiagnostic(npcs r, seed in -> out, history h, request q, maxTotal);
//            q = [active, underdog, balanceStyleTier, enabledCoverage[4], model, coverage, desiredType, desiredTier, influenceb,
//            reducedInfluenceb, seed]
//   X = {src, has, fit, d, n, t, has2, fit2, d2, n2, t2, cp, sp, cp2, sp2}: src names where the result came from ("empty", "duel",
//       "tooweak", "named1", "best", "count0".."count3", "fallback", "cat0".."cat2", "diag", "ambient"); fit/d/n/t = the winner's
//       bucket, raw distance (bits), novelty (bits) and tie hash; ...2 = the best candidate that LOST to it (has2 = 0: there was none).
//       For "fallback" d/d2 are the ratios of the strongest and second-strongest under-band lineups (n = 0). The keys come from
//       VERBATIM copies of SelectPlayer / AmbientCandidate / SelectAmbient / DiagnosticCandidate / SelectAmbientDiagnostic below
//       whose only change is a Ranked that also remembers its runner-up and the source labels; every search runs both the real
//       header function and the copy, and the program aborts if their Match differs in any field.
// Small functions:
//   fin      Finite(vb); abil Ability(5 x bits) ; rf RatingFactor(mmrb, infb) and RatingFactor(mmrb); fp FighterPower(a, m, inf) and
//            FighterPower(a, m); ts4 TeamScore(abil[], mmr[], count, inf) and the 3-arg; ts2 TeamScore(abil[], count)
//   rec      RecordHistory applied to a history, step by step (h0 in, ids per step, h after each step)
//   nov      Novelty(ids, history, Types(roster)); key Key(ids); types Types(roster) as [id, type] pairs in map order
//   cov      CoverageFor(a, b); tidx TypeIndex(name|null), tc TypeCount(); named Named/NamedDivision(role)
//   ht       HasTarget(roster, a idx, b idx, type, tier); sid ScoreIds(roster, ids, inf); fm FillModels(match a/b ids, roster, reduced)
//   ros      Roster(input r, seed, excluded, anchor, influence) -> chosen fighter ids in order
//   dnames   StatusName/ModelName/CoverageName(v) for v -2..7
#include "TownMatchmakingPolicy.h"
#include "TownDiagnosticData.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cstdint>
#include <string>
#include <vector>

using namespace TownMatchmakingPolicy;

static unsigned s = 101010u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static unsigned R32() { unsigned hi = R() & 0xffffu; unsigned lo = R() & 0xffffu; return (hi << 16) | lo; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }

static unsigned long long Bits(double v) { unsigned long long b; std::memcpy(&b, &v, 8); return b; }
static std::string Esc(const std::string& v) {
    std::string o;
    for (size_t i = 0; i < v.size(); ++i) {
        const unsigned char c = static_cast<unsigned char>(v[i]);
        if (c == '"' || c == '\\') { o += '\\'; o += static_cast<char>(c); }
        else if (c < 0x20) { char b[8]; std::snprintf(b, sizeof b, "\\u%04x", c); o += b; }
        else o += static_cast<char>(c);
    }
    return o;
}
static void PStr(const std::string& v) { std::printf("\"%s\"", Esc(v).c_str()); }
static void PIds(const std::vector<std::string>& ids) {
    std::printf("[");
    for (size_t i = 0; i < ids.size(); ++i) { if (i) std::printf(","); PStr(ids[i]); }
    std::printf("]");
}
static void PHist(const History& h) {
    std::printf("[");
    for (size_t i = 0; i < h.size(); ++i) { if (i) std::printf(","); PIds(h[i]); }
    std::printf("]");
}
static void PFighters(const std::vector<Fighter>& f) {
    std::printf("[");
    for (size_t i = 0; i < f.size(); ++i) {
        if (i) std::printf(",");
        std::printf("["); PStr(f[i].id); std::printf(","); PStr(f[i].role);
        std::printf(",%llu,%llu]", Bits(f[i].ability), Bits(f[i].mmr));
    }
    std::printf("]");
}
static void PMatch(const Match& m) {
    std::printf("[%d,", m.valid ? 1 : 0); PIds(m.a); std::printf(","); PIds(m.b);
    std::printf(",%llu,%llu,%d,%d,%d,%d,%d,%d,%d,%d,[", Bits(m.scoreA), Bits(m.scoreB), m.diagnostic ? 1 : 0,
        m.diagnosticUnderdog ? 1 : 0, m.bestAvailable ? 1 : 0, (int)m.failure, m.diagnosticModel, m.requestedCoverage,
        m.actualCoverage, m.relaxation);
    for (int i = 0; i < 3; ++i) std::printf("%s%llu", i ? "," : "", Bits(m.modelScoreA[i]));
    std::printf("],[");
    for (int i = 0; i < 3; ++i) std::printf("%s%llu", i ? "," : "", Bits(m.modelScoreB[i]));
    std::printf("],[");
    for (int i = 0; i < 3; ++i) std::printf("%s%llu", i ? "," : "", Bits(m.modelProbabilityA[i]));
    std::printf("]]");
}
static bool SameMatch(const Match& x, const Match& y) {
    if (x.valid != y.valid || x.a != y.a || x.b != y.b || Bits(x.scoreA) != Bits(y.scoreA) || Bits(x.scoreB) != Bits(y.scoreB) ||
        x.diagnostic != y.diagnostic || x.diagnosticUnderdog != y.diagnosticUnderdog || x.bestAvailable != y.bestAvailable ||
        x.failure != y.failure || x.diagnosticModel != y.diagnosticModel || x.requestedCoverage != y.requestedCoverage ||
        x.actualCoverage != y.actualCoverage || x.relaxation != y.relaxation) return false;
    for (int i = 0; i < 3; ++i)
        if (Bits(x.modelScoreA[i]) != Bits(y.modelScoreA[i]) || Bits(x.modelScoreB[i]) != Bits(y.modelScoreB[i]) ||
            Bits(x.modelProbabilityA[i]) != Bits(y.modelProbabilityA[i])) return false;
    return true;
}

// ---- the instrumented copies -------------------------------------------------------------------------------------------------
struct Info {
    const char* src; bool has; int fit; double d, n; unsigned t; bool has2; int fit2; double d2, n2; unsigned t2; int cp, sp, cp2, sp2;
    Info() : src("empty"), has(false), fit(0), d(0), n(0), t(0), has2(false), fit2(0), d2(0), n2(0), t2(0), cp(0), sp(0), cp2(0), sp2(0) {}
};
static void PInfo(const Info& x) {
    std::printf("{\"src\":\"%s\",\"has\":%d,\"fit\":%d,\"d\":%llu,\"n\":%llu,\"t\":%u,\"has2\":%d,\"fit2\":%d,\"d2\":%llu,\"n2\":%llu,"
        "\"t2\":%u,\"cp\":%d,\"sp\":%d,\"cp2\":%d,\"sp2\":%d}", x.src, x.has ? 1 : 0, x.fit, Bits(x.d), Bits(x.n), x.t, x.has2 ? 1 : 0,
        x.fit2, Bits(x.d2), Bits(x.n2), x.t2, x.cp, x.sp, x.cp2, x.sp2);
}
namespace I {
using namespace TownMatchmakingPolicy::Detail;
// Detail::Ranked (TownMatchmakingPolicy.h:215-229) with the same rule, plus the runner-up.
struct RankedI {
    Match match; int fit; double novelty; unsigned tie; double dist;
    bool has2; int fit2; double novelty2; unsigned tie2; double dist2;
    RankedI() : fit(0), novelty(0), tie(0), dist(0), has2(false), fit2(0), novelty2(0), tie2(0), dist2(0) {}
    void Consider(const Match& candidate, double distance, double variety, unsigned tieBreak) {
        const int bucket = static_cast<int>(distance / .025);
        if (!match.valid || bucket < fit || (bucket == fit &&
            (variety < novelty || (variety == novelty && tieBreak < tie)))) {
            if (match.valid) { has2 = true; fit2 = fit; novelty2 = novelty; tie2 = tie; dist2 = dist; }
            match = candidate; fit = bucket; novelty = variety; tie = tieBreak; dist = distance;
        } else if (!has2 || bucket < fit2 || (bucket == fit2 && (variety < novelty2 || (variety == novelty2 && tieBreak < tie2)))) {
            has2 = true; fit2 = bucket; novelty2 = variety; tie2 = tieBreak; dist2 = distance;
        }
    }
    void Fill(Info& x, const char* src) const {
        x.src = src; x.has = match.valid; x.fit = fit; x.d = dist; x.n = novelty; x.t = tie;
        x.has2 = has2; x.fit2 = fit2; x.d2 = dist2; x.n2 = novelty2; x.t2 = tie2;
    }
};
static const char* kCount[] = {"count0", "count1", "count2", "count3"};
static const char* kCat[] = {"cat0", "cat1", "cat2"};
// TownMatchmakingPolicy.h:243-422, verbatim except RankedI, the fallback runner-up and the Info labels at each return.
inline Match SelectPlayer(const std::vector<Fighter>& players, const std::vector<Fighter>& npcs,
    Division division, unsigned seed, const History& history, int maxEnemies,
    int maxTotal, const std::string& requiredRole, int rarity,
    bool forceSoloNamed, bool teams1v1,
    PGConfig::ChallengeDifficulty challengeDifficulty, Info& info) {
    Match empty;
    info = Info();
    if (players.empty() || players.size() > 3 || division < Easy || division > Hard ||
        rarity < -1 || rarity > 3 || maxEnemies < 1 || maxTotal < 2) return empty;
    if (!requiredRole.empty() && (!Detail::Named(requiredRole) || Detail::NamedDivision(requiredRole) != division)) return empty;
    const double influence = 0.0;
    std::set<std::string> playerIds;
    double values[3], ratings[3], strongest = 0;
    for (size_t i = 0; i < players.size(); ++i) {
        if (players[i].id.empty() || !playerIds.insert(players[i].id).second) return empty;
        values[i] = players[i].ability;
        ratings[i] = players[i].mmr;
        strongest = (std::max)(strongest, FighterPower(values[i], ratings[i], influence));
    }
    const double playerScore = TeamScore(values, ratings, static_cast<int>(players.size()), influence);
    if (playerScore <= 0) return empty;
    const int limit = (std::min)(4, (std::min)(maxEnemies, maxTotal - static_cast<int>(players.size())));
    if (limit < 1) return empty;
    const std::vector<Fighter> roster = Detail::Roster(npcs, seed, playerIds, requiredRole, influence);
    const std::map<std::string, std::string> types = Detail::Types(roster);
    static const double low[] = { .55, .85, 1.15 }, high[] = { .80, 1.10, 1.45 }, target[] = { .70, 1.00, 1.30 };
    static const double rarityLow[3][4] = {
        {.25, .45, .70, 1.05}, {.50, .75, 1.00, 1.35}, {.85, 1.10, 1.40, 1.80}
    };
    static const double rarityHigh[3][4] = {
        {.45, .70, 1.05, 1.45}, {.75, 1.00, 1.35, 1.80}, {1.10, 1.40, 1.80, 2.30}
    };
    const double playerDifficulty = .80;
    const double selectionScale = challengeDifficulty == PGConfig::ChallengeEasy ? .85 :
        challengeDifficulty == PGConfig::ChallengeHard ? 1.15 : 1.0;
    const double minimum = (rarity < 0 ? low[division] : rarityLow[division][rarity]) * playerDifficulty * selectionScale;
    const double maximum = (rarity < 0 ? high[division] : rarityHigh[division][rarity]) * playerDifficulty * selectionScale;
    if (forceSoloNamed && !requiredRole.empty() && players.size() == 1)
    {
        for (size_t i = 0; i < roster.size(); ++i)
        {
            if (roster[i].role != requiredRole) continue;
            std::vector<int> anchor(1, static_cast<int>(i));
            Match duel;
            duel.scoreA = playerScore;
            duel.scoreB = Detail::Score(roster, anchor, duel.b, influence);
            if (duel.scoreB <= 0) return empty;
            duel.valid = true;
            duel.a.assign(playerIds.begin(), playerIds.end());
            info.src = "duel";
            return duel;
        }
        return empty;
    }
    double weakestSingle = (std::numeric_limits<double>::max)();
    bool sawSingleton = false;
    for (size_t i = 0; i < roster.size(); ++i) {
        if (!requiredRole.empty() && roster[i].role != requiredRole) continue;
        const double score = FighterPower(roster[i].ability, roster[i].mmr, influence);
        if (score <= 0) continue;
        sawSingleton = true;
        weakestSingle = (std::min)(weakestSingle, score / playerScore);
    }
    if (sawSingleton && weakestSingle > maximum + 1e-12) {
        empty.failure = Match::PlayersTooWeak;
        info.src = "tooweak";
        return empty;
    }
    unsigned varietySeed = seed ^ (0x9e3779b9u * static_cast<unsigned>(1 + division * 4 + rarity));
    const double desired = rarity < 0 ? target[division] * playerDifficulty * selectionScale :
        minimum + (maximum - minimum) * (Detail::Next(varietySeed) % 10001) / 10000.0;
    RankedI best;
    RankedI byCount[4];
    RankedI favorableByCount[2];
    Match fallback;
    double fallbackRatio = 0.0;
    double fallbackNovelty = 0.0;
    unsigned fallbackTie = 0;
    bool fb1 = false, fb2 = false; double fbTop = 0, fbSecond = 0;   // instrumentation only
    const int size = static_cast<int>(roster.size());
    for (int i = 0; i < size; ++i) for (int j = -1; j < (limit >= 2 ? size : 0); ++j) {
        if (j >= 0 && j <= i) continue;
        for (int k = -1; k < (limit >= 3 && j >= 0 ? size : 0); ++k) {
            if (k >= 0 && k <= j) continue;
            for (int l = -1; l < (limit >= 4 && k >= 0 ? size : 0); ++l) {
                if (l >= 0 && l <= k) continue;
                std::vector<int> indices; indices.push_back(i);
                if (j >= 0) indices.push_back(j); if (k >= 0) indices.push_back(k); if (l >= 0) indices.push_back(l);
                if (requiredRole.empty() && indices.size() == 1 && rarity >= 0) {
                    const TownFighterCatalog::Entry* entry = TownFighterCatalog::Find(roster[i].role.c_str());
                    const int tierCeiling = division == Easy ? 0 :
                        division == Medium ? (rarity >= 2 ? 1 : 0) :
                        rarity == 3 ? 2 : rarity >= 1 ? 1 : 0;
                    if (!entry || entry->tier > tierCeiling) continue;
                }
                bool anchor = requiredRole.empty(), overwhelming = false;
                for (size_t n = 0; n < indices.size(); ++n) {
                    const Fighter& f = roster[indices[n]];
                    anchor = anchor || f.role == requiredRole;
                    overwhelming = overwhelming || FighterPower(f.ability, f.mmr, influence) > strongest;
                }
                if (!anchor || (division == Easy && rarity < 0 && overwhelming)) continue;
                Match candidate;
                candidate.scoreA = playerScore;
                candidate.scoreB = Detail::Score(roster, indices, candidate.b, influence);
                if (candidate.scoreB <= 0) continue;
                const double rawRatio = candidate.scoreB / playerScore;
                const double ratio = rawRatio /
                    (teams1v1 && indices.size() > 1 ? 1.15 : 1.0);
                if (players.size() == 1 && indices.size() <= 2 &&
                    ratio < minimum - 1e-12 && ratio >= minimum * .80 - 1e-12)
                {
                    candidate.valid = true;
                    candidate.bestAvailable = true;
                    candidate.a.assign(playerIds.begin(), playerIds.end());
                    favorableByCount[indices.size() - 1].Consider(candidate,
                        minimum - ratio, Detail::Novelty(candidate.b, history, types),
                        Detail::Hash(seed, Detail::Key(candidate.b)));
                }
                if (rarity >= 2 && ratio < minimum - 1e-12)
                {
                    if (!fb1 || ratio > fbTop) { if (fb1) { fb2 = true; fbSecond = fbTop; } fb1 = true; fbTop = ratio; }
                    else if (!fb2 || ratio > fbSecond) { fb2 = true; fbSecond = ratio; }
                    const bool stronger = !fallback.valid || ratio > fallbackRatio + 1e-12;
                    const bool tied = fallback.valid && std::fabs(ratio - fallbackRatio) <= 1e-12;
                    if (stronger || tied)
                    {
                        const double novelty = Detail::Novelty(candidate.b, history, types);
                        const unsigned tie = Detail::Hash(seed, Detail::Key(candidate.b));
                        if (stronger || novelty < fallbackNovelty ||
                            (novelty == fallbackNovelty && tie < fallbackTie))
                        {
                            candidate.valid = true;
                            candidate.bestAvailable = true;
                            candidate.a.assign(playerIds.begin(), playerIds.end());
                            fallback = candidate;
                            fallbackRatio = ratio;
                            fallbackNovelty = novelty;
                            fallbackTie = tie;
                        }
                    }
                }
                if (ratio < minimum - 1e-12 || ratio > maximum + 1e-12) continue;
                candidate.valid = true; candidate.a.assign(playerIds.begin(), playerIds.end());
                if (!requiredRole.empty() && players.size() == 1 && indices.size() == 1) { info.src = "named1"; return candidate; }
                RankedI& ranked = rarity < 0 ? best : byCount[indices.size() - 1];
                ranked.Consider(candidate, std::fabs(ratio - desired), Detail::Novelty(candidate.b, history, types), Detail::Hash(seed, Detail::Key(candidate.b)));
            }
        }
    }
    if (rarity < 0) { best.Fill(info, "best"); return best.match; }
    for (int i = 0; i < 2; ++i)
        if (!byCount[i].match.valid && favorableByCount[i].match.valid)
            byCount[i] = favorableByCount[i];
    int available[4], count = 0;
    if (players.size() == 1) {
        static const int weights[] = {50, 30, 15, 5};
        int total = 0;
        for (int i = 0; i < 4; ++i) if (byCount[i].match.valid) total += weights[i];
        if (total) {
            int roll = static_cast<int>(Detail::Next(varietySeed) % total);
            for (int i = 0; i < 4; ++i) if (byCount[i].match.valid) {
                if (roll < weights[i]) { byCount[i].Fill(info, kCount[i]); return byCount[i].match; }
                roll -= weights[i];
            }
        }
    }
    for (int i = 0; i < 4; ++i) if (byCount[i].match.valid) available[count++] = i;
    if (count) { const int c = available[Detail::Next(varietySeed) % count]; byCount[c].Fill(info, kCount[c]); return byCount[c].match; }
    if (fallback.valid) { info.src = "fallback"; info.has = true; info.d = fbTop; info.has2 = fb2; info.d2 = fbSecond; return fallback; }
    return empty;
}
// TownMatchmakingPolicy.h:444-469, verbatim except RankedI.
inline void AmbientCandidate(const std::vector<Fighter>& roster, const std::vector<int>& a,
    const std::vector<int>& b, const History& history, const std::map<std::string, std::string>& types,
    double closeTarget, double underdogTarget, double showcaseTarget,
    double closeCap, double ratioCap,
    int attendance, unsigned seed, std::set<std::string>& seen, RankedI* best) {
    Match candidate;
    candidate.scoreA = Score(roster, a, candidate.a); candidate.scoreB = Score(roster, b, candidate.b);
    if (candidate.scoreA <= 0 || candidate.scoreB <= 0) return;
    const double ratio = (std::max)(candidate.scoreA, candidate.scoreB) / (std::min)(candidate.scoreA, candidate.scoreB);
    if (ratio > ratioCap + 1e-12) return;
    if (candidate.b < candidate.a) { candidate.a.swap(candidate.b); std::swap(candidate.scoreA, candidate.scoreB); }
    std::string key = Key(candidate.a); key += '\1'; key += Key(candidate.b);
    if (!seen.insert(key).second) return;
    std::vector<std::string> members(candidate.a); members.insert(members.end(), candidate.b.begin(), candidate.b.end());
    std::sort(members.begin(), members.end());
    candidate.valid = true;
    const int category = ratio <= closeCap + 1e-12 ? 0 :
        ratio <= ratioCap + 1e-12 ? 1 : 2;
    const double variety = Novelty(members, history, types) + .01 * std::abs(static_cast<int>(members.size()) - attendance);
    const double target = category == 2 ? showcaseTarget : category == 1 ? underdogTarget : closeTarget;
    best[category].Consider(candidate, std::fabs(ratio - target), variety, Hash(seed, key));
}
// TownMatchmakingPolicy.h:471-540, verbatim except RankedI and the Info label.
inline Match SelectAmbient(const std::vector<Fighter>& npcs, unsigned& seed,
    const History& history, int maxTotal, Info& info) {
    info = Info();
    const unsigned attemptSeed = Detail::Next(seed);
    unsigned random = attemptSeed;
    const int capacity = (std::min)(8, maxTotal);
    Match empty;
    if (capacity < 2) return empty;
    const std::set<std::string> excluded;
    const std::vector<Fighter> roster = Detail::Roster(npcs, random, excluded, std::string());
    const int size = static_cast<int>(roster.size());
    if (size < 2) return empty;
    const unsigned categoryRoll = Detail::Next(random);
    const BalanceTuning::Values& tuning = BalanceTuning::Get();
    const int showcasePercent = tuning.showcasePercent;
    const double closeCap = tuning.ambientCloseCap;
    int category;
    if (showcasePercent > 0) {
        const unsigned bucket = categoryRoll % 100u;
        category = bucket < static_cast<unsigned>(showcasePercent) ? 2 :
            bucket < static_cast<unsigned>(showcasePercent + 20) ? 1 : 0;
    } else {
        category = (categoryRoll % 5u) == 0 ? 1 : 0;
    }
    const double closeTarget = 1 + (Detail::Next(random) % 10000) * tuning.ambientCloseSpan / 10000;
    const double underdogTarget = closeCap + (Detail::Next(random) % 10000) * tuning.ambientUnderdogSpan / 10000;
    double showcaseTarget = 0.0;
    if (showcasePercent > 0)
        showcaseTarget = tuning.ambientRatioCap + (Detail::Next(random) % 10000) *
            (Detail::kShowcaseRatioHigh - tuning.ambientRatioCap) / 10000;
    const double ratioCap = showcasePercent > 0 ? Detail::kShowcaseRatioHigh : tuning.ambientRatioCap;
    const int attendance = 2 + Detail::Next(random) % ((std::min)(capacity, size) - 1);
    const std::map<std::string, std::string> types = Detail::Types(roster);
    RankedI best[3]; std::set<std::string> seen;
    for (int i = 0; i < size; ++i) for (int j = i + 1; j < size; ++j) {
        const std::vector<int> a(1, i), b(1, j);
        I::AmbientCandidate(roster, a, b, history, types, closeTarget, underdogTarget, showcaseTarget, closeCap, ratioCap, attendance, attemptSeed, seen, best);
    }
    for (int attempt = 0; attempt < 4096 && capacity > 2 && size > 2; ++attempt) {
        const int countA = 1 + Detail::Next(random) % 4, countB = 1 + Detail::Next(random) % 4;
        if (countA + countB > capacity || countA + countB > size || countA + countB == 2) continue;
        std::vector<int> available;
        for (int i = 0; i < size; ++i) available.push_back(i);
        std::vector<int> a, b;
        for (int i = 0; i < countA + countB; ++i) {
            const int chosen = i + Detail::Next(random) % (size - i);
            std::swap(available[i], available[chosen]);
            (i < countA ? a : b).push_back(available[i]);
        }
        I::AmbientCandidate(roster, a, b, history, types, closeTarget, underdogTarget, showcaseTarget, closeCap, ratioCap, attendance, attemptSeed, seen, best);
    }
    const int used = best[category].match.valid ? category : (category == 0 ? 1 : 0);
    best[used].Fill(info, kCat[used]);
    Match match = best[category].match.valid ? best[category].match
        : best[category == 0 ? 1 : 0].match;
    if (match.valid && (Detail::Next(random) & 1u)) { match.a.swap(match.b); std::swap(match.scoreA, match.scoreB); }
    return match;
}
// Detail::DiagnosticRanked (TownMatchmakingPolicy.h:597-611) with the same rule, plus the runner-up.
struct DiagnosticRankedI {
    Match match;
    int coveragePenalty, stylePenalty, fit;
    double novelty, dist;
    unsigned tie;
    bool has2; int cp2, sp2, fit2; double novelty2, dist2; unsigned tie2;
    DiagnosticRankedI() : coveragePenalty(9),stylePenalty(9),fit(0),novelty(0),dist(0),tie(0),has2(false),cp2(9),sp2(9),fit2(0),novelty2(0),dist2(0),tie2(0) {}
    static bool Better(int cp,int sp,int bucket,double variety,unsigned t,int ocp,int osp,int ofit,double onov,unsigned otie) {
        return cp<ocp || (cp==ocp &&
            (sp<osp || (sp==osp && (bucket<ofit || (bucket==ofit &&
            (variety<onov || (variety==onov && t<otie)))))));
    }
    void Consider(const Match& candidate,int cp,int sp,double distance,double variety,unsigned t) {
        const int bucket=static_cast<int>(distance/.025);
        if(!match.valid || cp<coveragePenalty || (cp==coveragePenalty &&
            (sp<stylePenalty || (sp==stylePenalty && (bucket<fit || (bucket==fit &&
            (variety<novelty || (variety==novelty && t<tie)))))))) {
            if (match.valid) { has2=true; cp2=coveragePenalty; sp2=stylePenalty; fit2=fit; novelty2=novelty; dist2=dist; tie2=tie; }
            match=candidate;coveragePenalty=cp;stylePenalty=sp;fit=bucket;novelty=variety;tie=t;dist=distance;
        } else if (!has2 || Better(cp,sp,bucket,variety,t,cp2,sp2,fit2,novelty2,tie2)) {
            has2=true; cp2=cp; sp2=sp; fit2=bucket; novelty2=variety; dist2=distance; tie2=t;
        }
    }
    void Fill(Info& x) const {
        x.src = "diag"; x.has = match.valid; x.fit = fit; x.d = dist; x.n = novelty; x.t = tie; x.cp = coveragePenalty; x.sp = stylePenalty;
        x.has2 = has2; x.fit2 = fit2; x.d2 = dist2; x.n2 = novelty2; x.t2 = tie2; x.cp2 = cp2; x.sp2 = sp2;
    }
};
// TownMatchmakingPolicy.h:612-631, verbatim except DiagnosticRankedI.
inline void DiagnosticCandidate(const std::vector<Fighter>& roster,const std::vector<int>& a,const std::vector<int>& b,
    const History& history,const std::map<std::string,std::string>& types,const DiagnosticRequest& request,
    int attendance,unsigned seed,std::set<std::string>& seen,DiagnosticRankedI& best) {
    Match candidate; candidate.scoreA=Score(roster,a,candidate.a,request.influence); candidate.scoreB=Score(roster,b,candidate.b,request.influence);
    if(candidate.scoreA<=0 || candidate.scoreB<=0) return;
    const double ratio=(std::max)(candidate.scoreA,candidate.scoreB)/(std::min)(candidate.scoreA,candidate.scoreB);
    if(ratio>1.30+1e-12 || (request.underdog ? ratio<=1.15+1e-12 : ratio>1.15+1e-12)) return;
    if(candidate.b<candidate.a) {candidate.a.swap(candidate.b);std::swap(candidate.scoreA,candidate.scoreB);}
    std::string key=Key(candidate.a);key+='\1';key+=Key(candidate.b);if(!seen.insert(key).second)return;
    std::vector<std::string> members(candidate.a);members.insert(members.end(),candidate.b.begin(),candidate.b.end());std::sort(members.begin(),members.end());
    candidate.valid=true; const int coverage=CoverageFor(static_cast<int>(a.size()),static_cast<int>(b.size()));
    if(!request.enabledCoverage[coverage])return;
    const int cp=coverage==request.coverage?0:1;
    const int sp=!request.balanceStyleTier || HasTarget(roster,a,b,request.desiredType,request.desiredTier)?0:1;
    const double target=request.underdog?1.225:1.075;
    const double variety=Novelty(members,history,types)+.01*std::abs(static_cast<int>(members.size())-attendance);
    candidate.diagnostic=true;candidate.diagnosticUnderdog=request.underdog;candidate.diagnosticModel=request.model;
    candidate.requestedCoverage=request.coverage;candidate.actualCoverage=coverage;candidate.relaxation=cp?2:sp?1:0;
    best.Consider(candidate,cp,sp,std::fabs(ratio-target),variety,Hash(seed,key));
}
// TownMatchmakingPolicy.h:634-663, verbatim except DiagnosticRankedI, the instrumented SelectAmbient and the Info label.
inline Match SelectAmbientDiagnostic(const std::vector<Fighter>& npcs,unsigned& seed,const History& history,
    const DiagnosticRequest& request,int maxTotal, Info& info) {
    info = Info();
    if(!request.active) { Match m = I::SelectAmbient(npcs,seed,history,maxTotal,info); return m; }
    const unsigned attemptSeed=Detail::Next(seed);unsigned random=attemptSeed;
    const int capacity=(std::min)(8,maxTotal);Match empty;if(capacity<2)return empty;
    const std::set<std::string> excluded;const std::vector<Fighter> roster=Detail::Roster(npcs,random,excluded,std::string());
    const int size=static_cast<int>(roster.size());if(size<2)return empty;
    const int attendance=2+Detail::Next(random)%((std::min)(capacity,size)-1);
    const std::map<std::string,std::string> types=Detail::Types(roster);
    DiagnosticRankedI best;std::set<std::string> seen;
    for(int i=0;i<size;++i)for(int j=i+1;j<size;++j){const std::vector<int>a(1,i),b(1,j);I::DiagnosticCandidate(roster,a,b,history,types,request,attendance,attemptSeed,seen,best);}
    for(int attempt=0;attempt<4096 && capacity>2 && size>2;++attempt){
        const int countA=1+Detail::Next(random)%4,countB=1+Detail::Next(random)%4;
        if(countA+countB>capacity || countA+countB>size || countA+countB==2)continue;
        std::vector<int> available;for(int i=0;i<size;++i)available.push_back(i);std::vector<int>a,b;
        for(int i=0;i<countA+countB;++i){const int chosen=i+Detail::Next(random)%(size-i);std::swap(available[i],available[chosen]);(i<countA?a:b).push_back(available[i]);}
        I::DiagnosticCandidate(roster,a,b,history,types,request,attendance,attemptSeed,seen,best);
    }
    Match match=best.match;
    best.Fill(info);
    if(!match.valid){
        unsigned fallbackSeed=attemptSeed;match=I::SelectAmbient(npcs,fallbackSeed,history,maxTotal,info);
        if(info.src[0]=='c') info.src = "ambient";
        if(!match.valid)return match;match.diagnostic=true;match.diagnosticModel=request.model;match.requestedCoverage=request.coverage;
        match.actualCoverage=Detail::CoverageFor(static_cast<int>(match.a.size()),static_cast<int>(match.b.size()));match.relaxation=3;
        match.diagnosticUnderdog=(std::max)(match.scoreA,match.scoreB)/(std::min)(match.scoreA,match.scoreB)>1.15;
    }
    if(Detail::Next(random)&1u){match.a.swap(match.b);std::swap(match.scoreA,match.scoreB);}
    Detail::FillModels(match,npcs,request.reducedInfluence);
    match.scoreA=match.modelScoreA[request.model];match.scoreB=match.modelScoreB[request.model];
    return match;
}
}

// ---- inputs ------------------------------------------------------------------------------------------------------------------
static const char* kCatalogIds[] = {
    "219-Proving Grounds.mod","223-Proving Grounds.mod","225-Proving Grounds.mod","206-Proving Grounds.mod","96-Proving Grounds.mod",
    "207-Proving Grounds.mod","213-Proving Grounds.mod","97-Proving Grounds.mod","210-Proving Grounds.mod","218-Proving Grounds.mod",
    "98-Proving Grounds.mod","216-Proving Grounds.mod","232-Proving Grounds.mod","230-Proving Grounds.mod","227-Proving Grounds.mod",
    "238-Proving Grounds.mod","240-Proving Grounds.mod","236-Proving Grounds.mod","298-Proving Grounds.mod","302-Proving Grounds.mod",
    "300-Proving Grounds.mod","666-Proving Grounds.mod","665-Proving Grounds.mod","670-Proving Grounds.mod","673-Proving Grounds.mod",
    "681-Proving Grounds.mod","675-Proving Grounds.mod","677-Proving Grounds.mod","679-Proving Grounds.mod","678-Proving Grounds.mod"};
static const int kCatalogCount = 30;
static const char* kNamed[] = {"616-Proving Grounds.mod","619-Proving Grounds.mod","623-Proving Grounds.mod","169-Proving Grounds.mod",
    "168-Proving Grounds.mod","167-Proving Grounds.mod"};
static const char* kJunk[] = {"", "221-Proving Grounds.mod", "nobody", "617-Proving Grounds.mod", "219-Proving Grounds.mod "};
static const double kNaN = std::numeric_limits<double>::quiet_NaN(), kInf = std::numeric_limits<double>::infinity();

static double Abil() {
    const unsigned k = R() % 40;
    if (k == 0) return 0;
    if (k == 1) return -5;
    if (k == 2) return kNaN;
    if (k == 3) return kInf;
    if (k < 8) return 30 + (R() % 9) * 5;   // repeats: equal powers, the ById tie-break of ByPower
    return 20 + (R() % 18000) / 256.0;
}
static double Mmr() {
    const unsigned k = R() % 30;
    if (k < 15) return 100;
    if (k == 15) return -10;
    if (k == 16) return kNaN;
    if (k == 17) return 0;
    return 40 + (R() % 880) / 4.0;
}
static std::string Role(int namedPercent) {
    const unsigned k = R() % 100;
    if (k < static_cast<unsigned>(namedPercent)) return kNamed[R() % 6];
    if (k < static_cast<unsigned>(namedPercent) + 6) return kJunk[R() % 5];
    return kCatalogIds[R() % kCatalogCount];
}
static std::vector<Fighter> MakeRoster(int n, int namedPercent, bool clean) {
    std::vector<Fighter> v;
    for (int i = 0; i < n; ++i) {
        Fighter f;
        char id[16]; std::snprintf(id, sizeof id, "n%d", static_cast<int>(R() % 900));
        f.id = id;
        if (!clean) {
            const unsigned d = R() % 40;
            if (d == 0) f.id.clear();
            else if (d == 1 && !v.empty()) f.id = v[R() % v.size()].id;
        }
        f.role = clean ? std::string(kCatalogIds[R() % kCatalogCount]) : Role(namedPercent);
        f.ability = clean ? 20 + (R() % 18000) / 256.0 : Abil();
        f.mmr = clean ? (R() % 2 ? 100 : 40 + (R() % 880) / 4.0) : Mmr();
        v.push_back(f);
    }
    return v;
}
static History MakeHistory(const std::vector<Fighter>& roster, int entries) {
    History h;
    for (int e = 0; e < entries; ++e) {
        std::vector<std::string> ids;
        const int n = 1 + static_cast<int>(R() % 4);
        for (int i = 0; i < n; ++i) {
            if (!roster.empty() && R() % 6) ids.push_back(roster[R() % roster.size()].id);
            else { char id[16]; std::snprintf(id, sizeof id, "x%d", static_cast<int>(R() % 9)); ids.push_back(id); }
        }
        if (!h.empty() && R() % 5 == 0) ids = h[R() % h.size()];
        h.push_back(ids);
    }
    return h;
}

struct Tune { double ae, ri; int sp; double cc, cs, rc, us; };
static const Tune kTunes[] = {
    {2.5, 1.0, 0, 1.10, 0.10, 1.15, 0.05},     // shipped
    {2.5, 1.0, 25, 1.10, 0.10, 1.15, 0.05},    // showcase band on
    {2.0, 0.5, 0, 1.15, 0.15, 1.30, 0.15},     // the pre-narrowing band, softer exponent and MMR
    {3.0, 1.0, 70, 1.05, 0.05, 1.25, 0.20},    // showcase dominant
};
static void SetTune(int t) {
    BalanceTuning::Values& v = BalanceTuning::Get();
    v.abilityExponent = kTunes[t].ae; v.ratingInfluence = kTunes[t].ri; v.showcasePercent = kTunes[t].sp;
    v.ambientCloseCap = kTunes[t].cc; v.ambientCloseSpan = kTunes[t].cs; v.ambientRatioCap = kTunes[t].rc; v.ambientUnderdogSpan = kTunes[t].us;
}

static std::vector<std::vector<Fighter> > g_rosters;
static std::vector<History> g_hists;
static int AddRoster(const std::vector<Fighter>& r) {
    g_rosters.push_back(r);
    const int id = static_cast<int>(g_rosters.size()) - 1;
    std::printf("%s{\"k\":\"roster\",\"id\":%d,\"f\":", Sep(), id); PFighters(r); std::printf("}\n");
    return id;
}
static int AddHist(const History& h) {
    g_hists.push_back(h);
    const int id = static_cast<int>(g_hists.size()) - 1;
    std::printf("%s{\"k\":\"hist\",\"id\":%d,\"h\":", Sep(), id); PHist(h); std::printf("}\n");
    return id;
}

static int g_spCount = 0;
static void RunSp(int tune, const std::vector<Fighter>& players, int r, int division, unsigned seed, int h, int maxE, int maxT,
    const std::string& role, int rarity, bool solo, bool t11, int diff) {
    SetTune(tune);
    const Match real = SelectPlayer(players, g_rosters[r], static_cast<Division>(division), seed, g_hists[h], maxE, maxT, role, rarity,
        solo, t11, static_cast<PGConfig::ChallengeDifficulty>(diff));
    Info info;
    const Match copy = I::SelectPlayer(players, g_rosters[r], static_cast<Division>(division), seed, g_hists[h], maxE, maxT, role, rarity,
        solo, t11, static_cast<PGConfig::ChallengeDifficulty>(diff), info);
    if (!SameMatch(real, copy)) { std::fprintf(stderr, "instrumented SelectPlayer differs (case %d)\n", g_spCount); std::exit(2); }
    std::printf("%s{\"k\":\"sp\",\"tune\":%d,\"p\":", Sep(), tune); PFighters(players);
    std::printf(",\"r\":%d,\"div\":%d,\"seed\":%u,\"h\":%d,\"maxE\":%d,\"maxT\":%d,\"role\":", r, division, seed, h, maxE, maxT);
    PStr(role);
    std::printf(",\"rarity\":%d,\"solo\":%d,\"t11\":%d,\"diff\":%d,\"M\":", rarity, solo ? 1 : 0, t11 ? 1 : 0, diff);
    PMatch(real); std::printf(",\"X\":"); PInfo(info); std::printf("}\n");
    ++g_spCount;
}
static void RunAmb(int tune, int r, unsigned seed, int h, int maxT) {
    SetTune(tune);
    unsigned s1 = seed, s2 = seed;
    const Match real = SelectAmbient(g_rosters[r], s1, g_hists[h], maxT);
    Info info;
    const Match copy = I::SelectAmbient(g_rosters[r], s2, g_hists[h], maxT, info);
    if (!SameMatch(real, copy) || s1 != s2) { std::fprintf(stderr, "instrumented SelectAmbient differs\n"); std::exit(2); }
    std::printf("%s{\"k\":\"amb\",\"tune\":%d,\"r\":%d,\"seed\":%u,\"h\":%d,\"maxT\":%d,\"seedOut\":%u,\"M\":", Sep(), tune, r, seed, h, maxT, s1);
    PMatch(real); std::printf(",\"X\":"); PInfo(info); std::printf("}\n");
}
static void RunDiag(int tune, int r, unsigned seed, int h, const DiagnosticRequest& q, int maxT) {
    SetTune(tune);
    unsigned s1 = seed, s2 = seed;
    const Match real = SelectAmbientDiagnostic(g_rosters[r], s1, g_hists[h], q, maxT);
    Info info;
    const Match copy = I::SelectAmbientDiagnostic(g_rosters[r], s2, g_hists[h], q, maxT, info);
    if (!SameMatch(real, copy) || s1 != s2) { std::fprintf(stderr, "instrumented SelectAmbientDiagnostic differs\n"); std::exit(2); }
    std::printf("%s{\"k\":\"diag\",\"tune\":%d,\"r\":%d,\"seed\":%u,\"h\":%d,\"maxT\":%d,\"q\":[%d,%d,%d,[%d,%d,%d,%d],%d,%d,%d,%d,%llu,%llu,%u],"
        "\"seedOut\":%u,\"M\":", Sep(), tune, r, seed, h, maxT, q.active ? 1 : 0, q.underdog ? 1 : 0, q.balanceStyleTier ? 1 : 0,
        q.enabledCoverage[0] ? 1 : 0, q.enabledCoverage[1] ? 1 : 0, q.enabledCoverage[2] ? 1 : 0, q.enabledCoverage[3] ? 1 : 0,
        q.model, q.coverage, q.desiredType, q.desiredTier, Bits(q.influence), Bits(q.reducedInfluence), q.seed, s1);
    PMatch(real); std::printf(",\"X\":"); PInfo(info); std::printf("}\n");
}

static double AnyD() {
    const unsigned k = R() % 16;
    if (k == 0) return kNaN;
    if (k == 1) return kInf;
    if (k == 2) return -kInf;
    if (k == 3) return 0;
    if (k == 4) return -1.0 * (R() % 50);
    if (k == 5) return 1e308;
    if (k == 6) return (std::numeric_limits<double>::max)();
    return (R() % 400000) / 1000.0;
}
static double Inf01() {
    const unsigned k = R() % 10;
    if (k == 0) return -0.25;
    if (k == 1) return 1.5;
    if (k == 2) return kNaN;
    if (k == 3) return 0;
    if (k == 4) return 1;
    return (R() % 1001) / 1000.0;
}

int main() {
    std::printf("[\n");
    const int tuneCount = static_cast<int>(sizeof kTunes / sizeof kTunes[0]);
    for (int t = 0; t < tuneCount; ++t)
        std::printf("%s{\"k\":\"tune\",\"id\":%d,\"ae\":%llu,\"ri\":%llu,\"sp\":%d,\"cc\":%llu,\"cs\":%llu,\"rc\":%llu,\"us\":%llu}\n", Sep(), t,
            Bits(kTunes[t].ae), Bits(kTunes[t].ri), kTunes[t].sp, Bits(kTunes[t].cc), Bits(kTunes[t].cs), Bits(kTunes[t].rc), Bits(kTunes[t].us));
    SetTune(0);

    // ---- small functions ----
    for (int c = 0; c < 60; ++c) { const double v = AnyD(); std::printf("%s{\"k\":\"fin\",\"vb\":%llu,\"o\":%d}\n", Sep(), Bits(v), Finite(v) ? 1 : 0); }
    for (int c = 0; c < 300; ++c) {
        double v[5];
        for (int i = 0; i < 5; ++i) v[i] = R() % 6 ? (R() % 200000) / 1000.0 - (R() % 9 == 0 ? 300 : 0) : AnyD();
        std::printf("%s{\"k\":\"abil\",\"vb\":[%llu,%llu,%llu,%llu,%llu],\"ob\":%llu}\n", Sep(), Bits(v[0]), Bits(v[1]), Bits(v[2]), Bits(v[3]),
            Bits(v[4]), Bits(Ability(v[0], v[1], v[2], v[3], v[4])));
    }
    for (int c = 0; c < 300; ++c) {
        const double m = R() % 5 ? (R() % 400000) / 1000.0 : AnyD(), inf = Inf01();
        std::printf("%s{\"k\":\"rf\",\"mb\":%llu,\"ib\":%llu,\"ob\":%llu,\"o1b\":%llu}\n", Sep(), Bits(m), Bits(inf), Bits(RatingFactor(m, inf)),
            Bits(RatingFactor(m)));
    }
    for (int t = 0; t < tuneCount; ++t) {
        SetTune(t);
        for (int c = 0; c < 150; ++c) {
            const double a = R() % 5 ? (R() % 100000) / 1000.0 : AnyD(), m = R() % 5 ? (R() % 400000) / 1000.0 : AnyD(), inf = Inf01();
            std::printf("%s{\"k\":\"fp\",\"tune\":%d,\"ab\":%llu,\"mb\":%llu,\"ib\":%llu,\"ob\":%llu,\"o2b\":%llu}\n", Sep(), t, Bits(a), Bits(m),
                Bits(inf), Bits(FighterPower(a, m, inf)), Bits(FighterPower(a, m)));
        }
        for (int c = 0; c < 80; ++c) {
            const int count = static_cast<int>(R() % 7) - 1;
            double ab[6], mm[6];
            for (int i = 0; i < 6; ++i) { ab[i] = R() % 9 ? 20 + (R() % 70000) / 1000.0 : AnyD(); mm[i] = R() % 9 ? (R() % 300000) / 1000.0 : AnyD(); }
            const double inf = Inf01();
            std::printf("%s{\"k\":\"ts\",\"tune\":%d,\"n\":%d,\"ab\":[", Sep(), t, count);
            for (int i = 0; i < 6; ++i) std::printf("%s%llu", i ? "," : "", Bits(ab[i]));
            std::printf("],\"mb\":[");
            for (int i = 0; i < 6; ++i) std::printf("%s%llu", i ? "," : "", Bits(mm[i]));
            std::printf("],\"ib\":%llu,\"o4b\":%llu,\"o3b\":%llu,\"o2b\":%llu,\"onullb\":%llu}\n", Bits(inf), Bits(TeamScore(ab, mm, count, inf)),
                Bits(TeamScore(ab, mm, count)), Bits(TeamScore(ab, count)), Bits(TeamScore(static_cast<const double*>(0), count)));
        }
    }
    SetTune(0);
    for (int c = 0; c < 40; ++c) {
        std::vector<Fighter> pool = MakeRoster(6, 0, true);
        History h = MakeHistory(pool, static_cast<int>(R() % 4));
        std::printf("%s{\"k\":\"rec\",\"h0\":", Sep()); PHist(h); std::printf(",\"steps\":[");
        const int steps = 1 + static_cast<int>(R() % 9);
        for (int st = 0; st < steps; ++st) {
            std::vector<std::string> ids;
            const int n = static_cast<int>(R() % 5);
            for (int i = 0; i < n; ++i) ids.push_back(pool[R() % pool.size()].id);
            RecordHistory(h, ids);
            std::printf("%s{\"ids\":", st ? "," : ""); PIds(ids); std::printf(",\"h\":"); PHist(h); std::printf("}");
        }
        std::printf("]}\n");
    }
    for (int c = 0; c < 200; ++c) {
        std::vector<Fighter> pool = MakeRoster(4 + static_cast<int>(R() % 10), 10, false);
        History h = MakeHistory(pool, static_cast<int>(R() % 9));
        std::vector<std::string> ids;
        const int n = 1 + static_cast<int>(R() % 6);
        for (int i = 0; i < n; ++i) ids.push_back(pool[R() % pool.size()].id);
        if (R() % 2) std::sort(ids.begin(), ids.end());
        if (!h.empty() && R() % 6 == 0) { ids = h[R() % h.size()]; std::sort(ids.begin(), ids.end()); }
        const std::map<std::string, std::string> types = Detail::Types(pool);
        std::printf("%s{\"k\":\"nov\",\"pool\":", Sep()); PFighters(pool); std::printf(",\"h\":"); PHist(h); std::printf(",\"ids\":"); PIds(ids);
        std::printf(",\"ob\":%llu,\"key\":", Bits(Detail::Novelty(ids, h, types))); PStr(Detail::Key(ids));
        std::printf(",\"types\":[");
        bool firstType = true;
        for (std::map<std::string, std::string>::const_iterator it = types.begin(); it != types.end(); ++it) {
            std::printf("%s[", firstType ? "" : ","); PStr(it->first); std::printf(","); PStr(it->second); std::printf("]"); firstType = false;
        }
        std::printf("]}\n");
    }
    { std::vector<std::string> none; std::printf("%s{\"k\":\"keyempty\",\"key\":", Sep()); PStr(Detail::Key(none)); std::printf("}\n"); }
    for (int a = -1; a <= 5; ++a) for (int b = -1; b <= 5; ++b)
        std::printf("%s{\"k\":\"cov\",\"a\":%d,\"b\":%d,\"o\":%d}\n", Sep(), a, b, Detail::CoverageFor(a, b));
    {
        const char* names[] = {"Polearm","Sabre","Katana","Champion","Mace","Greatsword","Martial","Hacker","polearm","", "Hackers"};
        for (int i = 0; i < 11; ++i) { std::printf("%s{\"k\":\"tidx\",\"name\":", Sep()); PStr(names[i]); std::printf(",\"o\":%d}\n", Detail::TypeIndex(names[i])); }
        std::printf("%s{\"k\":\"tidx\",\"name\":null,\"o\":%d}\n", Sep(), Detail::TypeIndex(0));
        std::printf("%s{\"k\":\"tc\",\"o\":%d}\n", Sep(), Detail::TypeCount());
        const char* roles[] = {"616-Proving Grounds.mod","619-Proving Grounds.mod","623-Proving Grounds.mod","169-Proving Grounds.mod",
            "168-Proving Grounds.mod","167-Proving Grounds.mod","617-Proving Grounds.mod","","219-Proving Grounds.mod","616-proving grounds.mod"};
        for (int i = 0; i < 10; ++i) {
            std::printf("%s{\"k\":\"named\",\"role\":", Sep()); PStr(roles[i]);
            std::printf(",\"named\":%d,\"div\":%d}\n", Detail::Named(roles[i]) ? 1 : 0, Detail::NamedDivision(roles[i]));
        }
    }
    for (int c = 0; c < 200; ++c) {
        std::vector<Fighter> pool = MakeRoster(2 + static_cast<int>(R() % 10), 8, false);
        std::vector<int> a, b;
        const int na = static_cast<int>(R() % 4), nb = static_cast<int>(R() % 4);
        for (int i = 0; i < na; ++i) a.push_back(static_cast<int>(R() % pool.size()));
        for (int i = 0; i < nb; ++i) b.push_back(static_cast<int>(R() % pool.size()));
        const int type = static_cast<int>(R() % 10) - 1, tier = static_cast<int>(R() % 5) - 1;
        std::printf("%s{\"k\":\"ht\",\"pool\":", Sep()); PFighters(pool); std::printf(",\"a\":[");
        for (size_t i = 0; i < a.size(); ++i) std::printf("%s%d", i ? "," : "", a[i]);
        std::printf("],\"b\":[");
        for (size_t i = 0; i < b.size(); ++i) std::printf("%s%d", i ? "," : "", b[i]);
        std::printf("],\"type\":%d,\"tier\":%d,\"o\":%d}\n", type, tier, Detail::HasTarget(pool, a, b, type, tier) ? 1 : 0);
    }
    for (int t = 0; t < tuneCount; ++t) {
        SetTune(t);
        for (int c = 0; c < 60; ++c) {
            std::vector<Fighter> pool = MakeRoster(2 + static_cast<int>(R() % 10), 8, false);
            std::vector<std::string> ids;
            const int n = static_cast<int>(R() % 5);
            for (int i = 0; i < n; ++i) {
                if (R() % 7) ids.push_back(pool[R() % pool.size()].id);
                else ids.push_back("ghost");
            }
            const double inf = Inf01();
            std::printf("%s{\"k\":\"sid\",\"tune\":%d,\"pool\":", Sep(), t); PFighters(pool); std::printf(",\"ids\":"); PIds(ids);
            std::printf(",\"ib\":%llu,\"ob\":%llu}\n", Bits(inf), Bits(Detail::ScoreIds(pool, ids, inf)));
            Match m;
            for (int i = 0; i < 1 + static_cast<int>(R() % 3); ++i) m.a.push_back(R() % 6 ? pool[R() % pool.size()].id : std::string("ghost"));
            for (int i = 0; i < 1 + static_cast<int>(R() % 3); ++i) m.b.push_back(pool[R() % pool.size()].id);
            const double reduced = Inf01();
            Detail::FillModels(m, pool, reduced);
            std::printf("%s{\"k\":\"fm\",\"tune\":%d,\"pool\":", Sep(), t); PFighters(pool); std::printf(",\"a\":"); PIds(m.a);
            std::printf(",\"b\":"); PIds(m.b); std::printf(",\"rb\":%llu,\"M\":", Bits(reduced)); PMatch(m); std::printf("}\n");
        }
    }
    SetTune(0);
    for (int c = 0; c < 120; ++c) {
        const int t = c % tuneCount;
        SetTune(t);
        const int n = c % 4 == 0 ? 66 + static_cast<int>(R() % 120) : 2 + static_cast<int>(R() % 40);
        std::vector<Fighter> input = MakeRoster(n, 6, c % 4 == 0 ? (R() % 2 == 0) : false);
        if (c % 4 == 0 && R() % 2) {   // some named fighters in a big clean roster too
            for (int i = 0; i < 4; ++i) { Fighter f; char id[16]; std::snprintf(id, sizeof id, "u%d", i); f.id = id; f.role = kNamed[R() % 6]; f.ability = 50 + i; input.push_back(f); }
        }
        std::set<std::string> excluded;
        for (int i = 0; i < static_cast<int>(R() % 4); ++i) excluded.insert(input[R() % input.size()].id);
        const std::string anchor = R() % 3 ? std::string() : std::string(R() % 5 ? kNamed[R() % 6] : "nobody");
        const double inf = R() % 3 ? kTunes[t].ri : Inf01();
        const unsigned seed = R() % 10 ? R32() : 0u;
        const std::vector<Fighter> out = Detail::Roster(input, seed, excluded, anchor, inf);
        const std::vector<Fighter> out2 = Detail::Roster(input, seed, excluded, anchor);
        std::printf("%s{\"k\":\"ros\",\"tune\":%d,\"in\":", Sep(), t); PFighters(input); std::printf(",\"seed\":%u,\"ex\":[", seed);
        bool firstEx = true;
        for (std::set<std::string>::const_iterator it = excluded.begin(); it != excluded.end(); ++it) { std::printf("%s", firstEx ? "" : ","); PStr(*it); firstEx = false; }
        std::printf("],\"anchor\":"); PStr(anchor); std::printf(",\"ib\":%llu,\"o\":[", Bits(inf));
        for (size_t i = 0; i < out.size(); ++i) { std::printf("%s", i ? "," : ""); PStr(out[i].id); }
        std::printf("],\"o2\":[");
        for (size_t i = 0; i < out2.size(); ++i) { std::printf("%s", i ? "," : ""); PStr(out2[i].id); }
        std::printf("]}\n");
    }
    SetTune(0);
    for (int v = -2; v <= 7; ++v)
        std::printf("%s{\"k\":\"dnames\",\"v\":%d,\"status\":\"%s\",\"model\":\"%s\",\"coverage\":\"%s\"}\n", Sep(), v,
            TownDiagnosticData::StatusName(v), TownDiagnosticData::ModelName(v), TownDiagnosticData::CoverageName(v));

    // ---- shared rosters and histories for the searches ----
    std::vector<int> small, big;
    for (int i = 0; i < 30; ++i) small.push_back(AddRoster(MakeRoster(8 + static_cast<int>(R() % 17), 6, i % 3 == 0)));
    for (int i = 0; i < 8; ++i) big.push_back(AddRoster(MakeRoster(70 + static_cast<int>(R() % 11), 4, i % 2 == 0)));
    std::vector<int> hists;
    hists.push_back(AddHist(History()));
    for (int i = 0; i < 20; ++i) {
        const std::vector<Fighter>& base = g_rosters[small[R() % small.size()]];
        hists.push_back(AddHist(MakeHistory(base, 1 + static_cast<int>(R() % 7))));
    }

    // ---- SelectPlayer: every division x rarity x player count 1-3 x mode, three difficulties ----
    for (int diff = 0; diff < 3; ++diff)
        for (int division = 0; division < 3; ++division)
            for (int rarity = -1; rarity <= 3; ++rarity)
                for (int pc = 1; pc <= 3; ++pc)
                    for (int mode = 0; mode < 2; ++mode) {
                        std::vector<Fighter> players;
                        for (int i = 0; i < pc; ++i) {
                            Fighter p; char id[8]; std::snprintf(id, sizeof id, "p%d", i + 1); p.id = id;
                            p.ability = 30 + (R() % 12000) / 256.0; p.mmr = R() % 2 ? 100 : 60 + R() % 120;
                            players.push_back(p);
                        }
                        const int r = small[R() % small.size()];
                        const int h = hists[R() % hists.size()];
                        const int maxE = 1 + static_cast<int>(R() % 4);
                        const int maxT = pc + 1 + static_cast<int>(R() % 5);
                        RunSp(R() % 4 == 0 ? 2 : 0, players, r, division, R32(), h, R() % 3 ? 4 : maxE, R() % 3 ? 6 : maxT, std::string(), rarity, false,
                            mode == 1, diff);
                    }
    // named anchors (required role for the division, forced duels, mismatched division, an unknown role)
    for (int c = 0; c < 90; ++c) {
        const int pc = 1 + static_cast<int>(R() % 3);
        std::vector<Fighter> players;
        for (int i = 0; i < pc; ++i) { Fighter p; char id[8]; std::snprintf(id, sizeof id, "p%d", i + 1); p.id = id; p.ability = 35 + (R() % 12000) / 256.0; players.push_back(p); }
        const int division = static_cast<int>(R() % 3);
        const unsigned pick = R() % 10;
        std::string role = pick < 7 ? std::string(division == 0 ? kNamed[0] : division == 1 ? kNamed[1] : kNamed[2 + R() % 4]) :
            pick < 9 ? std::string(kNamed[R() % 6]) : std::string("219-Proving Grounds.mod");
        std::vector<Fighter> npcs = g_rosters[small[R() % small.size()]];
        if (R() % 4) {   // the anchor in the pool (sometimes two candidates with that role)
            for (int i = 0; i < 1 + static_cast<int>(R() % 2); ++i) {
                Fighter f; char id[8]; std::snprintf(id, sizeof id, "nm%d", i); f.id = id; f.role = role;
                f.ability = 30 + (R() % 15000) / 256.0; npcs.push_back(f);
            }
        }
        const int r = AddRoster(npcs);
        RunSp(0, players, r, division, R32(), hists[R() % hists.size()], 1 + static_cast<int>(R() % 4), 2 + static_cast<int>(R() % 6), role,
            static_cast<int>(R() % 5) - 1, R() % 2 == 0, R() % 2 == 0, static_cast<int>(R() % 3));
    }
    // the >64-regular stratified sample (fewer: each is the full subset sweep)
    for (int c = 0; c < 10; ++c) {
        std::vector<Fighter> players(1);
        players[0].id = "p1"; players[0].ability = 40 + (R() % 8000) / 256.0;
        if (c % 3 == 2) { Fighter p; p.id = "p2"; p.ability = 45; players.push_back(p); }
        RunSp(c % 5 == 4 ? 3 : 0, players, big[c % big.size()], static_cast<int>(R() % 3), R32(), hists[R() % hists.size()], c < 4 ? 4 : 2, 6,
            std::string(), static_cast<int>(R() % 5) - 1, false, c % 2 == 0, static_cast<int>(R() % 3));
    }
    // invalid arguments and edges
    {
        std::vector<Fighter> one(1); one[0].id = "p1"; one[0].ability = 50;
        std::vector<Fighter> none, four(4), dup(2), blank(1), weak(1), nanp(1);
        for (int i = 0; i < 4; ++i) { char id[8]; std::snprintf(id, sizeof id, "p%d", i); four[i].id = id; four[i].ability = 40; }
        dup[0].id = dup[1].id = "p1"; dup[0].ability = dup[1].ability = 50;
        blank[0].ability = 50;
        weak[0].id = "p1"; weak[0].ability = 5;
        nanp[0].id = "p1"; nanp[0].ability = kNaN;
        const int r = small[0], h = hists[1];
        RunSp(0, none, r, 0, 5, h, 4, 6, std::string(), -1, false, false, 1);
        RunSp(0, four, r, 0, 5, h, 4, 6, std::string(), -1, false, false, 1);
        RunSp(0, dup, r, 0, 5, h, 4, 6, std::string(), -1, false, false, 1);
        RunSp(0, blank, r, 0, 5, h, 4, 6, std::string(), -1, false, false, 1);
        RunSp(0, nanp, r, 0, 5, h, 4, 6, std::string(), -1, false, false, 1);
        RunSp(0, weak, r, 2, 5, h, 4, 6, std::string(), 3, false, false, 2);
        RunSp(0, one, r, -1, 5, h, 4, 6, std::string(), -1, false, false, 1);
        RunSp(0, one, r, 3, 5, h, 4, 6, std::string(), -1, false, false, 1);
        RunSp(0, one, r, 1, 5, h, 4, 6, std::string(), -2, false, false, 1);
        RunSp(0, one, r, 1, 5, h, 4, 6, std::string(), 4, false, false, 1);
        RunSp(0, one, r, 1, 5, h, 0, 6, std::string(), 1, false, false, 1);
        RunSp(0, one, r, 1, 5, h, 4, 1, std::string(), 1, false, false, 1);
        RunSp(0, four, r, 1, 5, h, 4, 6, std::string(), 1, false, false, 1);
        RunSp(0, one, r, 1, 0, h, 4, 2, std::string(), 1, false, false, 1);
        RunSp(0, one, r, 1, 0, h, 4, 6, std::string(), 1, false, false, 0);
        RunSp(0, one, AddRoster(std::vector<Fighter>()), 1, 0, h, 4, 6, std::string(), 1, false, false, 1);
        // a strong-player roster that can only be filled by the under-band fallback / favorable candidates
        std::vector<Fighter> strong(1); strong[0].id = "p1"; strong[0].ability = 95;
        for (int c = 0; c < 12; ++c) RunSp(0, strong, small[R() % small.size()], static_cast<int>(R() % 3), R32(), hists[R() % hists.size()], 4, 6,
            std::string(), 2 + static_cast<int>(R() % 2), false, R() % 2 == 0, static_cast<int>(R() % 3));
        std::vector<Fighter> mid(1); mid[0].id = "p1"; mid[0].ability = 70;
        for (int c = 0; c < 12; ++c) RunSp(0, mid, small[R() % small.size()], static_cast<int>(R() % 3), R32(), hists[R() % hists.size()], 2, 6,
            std::string(), static_cast<int>(R() % 4), false, R() % 2 == 0, static_cast<int>(R() % 3));
        // TownChallengePolicyTests.cpp:69-94
        Fighter player; player.id = "player"; player.ability = 50; player.mmr = 100;
        const std::vector<Fighter> players(1, player);
        const char* nroles[] = {"616-Proving Grounds.mod","619-Proving Grounds.mod","623-Proving Grounds.mod"};
        const char* nids[] = {"ressa","bum-per","veyr"};
        const double nab[] = {55, 60, 68};
        for (int i = 0; i < 3; ++i) {
            Fighter npc; npc.id = nids[i]; npc.role = nroles[i]; npc.ability = nab[i]; npc.mmr = 100;
            const int rr = AddRoster(std::vector<Fighter>(1, npc));
            RunSp(0, players, rr, i, 1, hists[0], 3, 6, nroles[i], 3, false, false, 1);
        }
    }

    // ---- SelectAmbient ----
    for (int t = 0; t < tuneCount; ++t)
        for (int c = 0; c < 70; ++c) {
            const int r = c % 10 == 9 ? big[R() % big.size()] : small[R() % small.size()];
            const int maxT = c % 7 == 0 ? static_cast<int>(R() % 4) : 2 + static_cast<int>(R() % 8);
            RunAmb(t, r, R() % 12 ? R32() : 0u, hists[R() % hists.size()], maxT);
        }
    {   // tiny rosters
        std::vector<Fighter> one(1); one[0].id = "a"; one[0].role = kCatalogIds[0]; one[0].ability = 40;
        const int r1 = AddRoster(one);
        RunAmb(0, r1, 3, hists[0], 8);
        std::vector<Fighter> two = one; two.push_back(one[0]); two[1].id = "b"; two[1].ability = 41;
        const int r2 = AddRoster(two);
        RunAmb(0, r2, 3, hists[0], 8); RunAmb(0, r2, 4, hists[0], 2); RunAmb(1, r2, 5, hists[0], 8);
        std::vector<Fighter> far = two; far[1].ability = 80;
        const int r3 = AddRoster(far);
        RunAmb(0, r3, 3, hists[0], 8);
    }

    // ---- SelectAmbientDiagnostic ----
    for (int t = 0; t < tuneCount; ++t)
        for (int c = 0; c < 80; ++c) {
            DiagnosticRequest q;
            q.active = c % 11 != 0;
            q.underdog = R() % 4 == 0;
            q.balanceStyleTier = R() % 3 != 0;
            for (int i = 0; i < 4; ++i) q.enabledCoverage[i] = R() % 5 != 0;
            q.model = static_cast<int>(R() % 3);
            q.coverage = static_cast<int>(R() % 4);
            q.desiredType = R() % 10 ? static_cast<int>(R() % 8) : static_cast<int>(R() % 3) * 4 - 1;
            q.desiredTier = static_cast<int>(R() % 3);
            q.reducedInfluence = R() % 2 ? .25 : (R() % 4) * .25;
            q.influence = q.model == 0 ? 0 : q.model == 1 ? q.reducedInfluence : 1;
            if (R() % 10 == 0) q.influence = Inf01();
            q.seed = R32();
            const int r = c % 12 == 11 ? big[R() % big.size()] : small[R() % small.size()];
            const int maxT = c % 9 == 0 ? static_cast<int>(R() % 4) : 2 + static_cast<int>(R() % 8);
            RunDiag(t, r, R() % 12 ? R32() : 0u, hists[R() % hists.size()], q, maxT);
        }
    std::printf("]\n");
    std::fprintf(stderr, "sp cases: %d\n", g_spCount);
    return 0;
}
