// sources: ArenaSnapshotJson.cpp ArenaSnapshotRepository.cpp ArenaRewards.cpp ArenaPerformance.cpp pgconfig_stub.cpp
// needs: rapidjson
// The REAL save-snapshot codec (ArenaSnapshotJson.cpp) and repository (ArenaSnapshotRepository.cpp), compiled with rapidjson v1.1.0
// (golden/fetch_rapidjson.sh), plus direct probes of the rapidjson pieces the codec uses (reader, number parse, double writer).
// One JSON array, one case per line, "k" = kind:
//   base  {i, text}                           a text other cases splice (texts below are {"b":base,"at","del","ins","pad"})
//   enc   {i, bm, in:D, ok, err, text}        EncodeSnapshot of snapshot D (bm = bookie maximum in effect; 10000 = shipped)
//   dec   {i, bm, t:T, code, field, msg, snap:D}    DecodeSnapshot of text T; snap = result.snapshot (default when not Ready)
//   load  {...}                               LoadSnapshot over a scripted fake Storage (see Fake below), its calls in "log"
//   pub   {...}                               PublishSnapshot over the same fake
//   rdr   {t, ev, code, off}                  rapidjson Reader with the codec's SAX flags and a recording handler
//   num   {t, code, off, ty, d}               rapidjson Document::Parse<kParseFullPrecisionFlag> of one number literal
//   dbl   {d, w}                              rapidjson Writer::Double of the double with IEEE bits d
// Strings (Q) are byte strings: bytes 0x20..0x7e other than '"' and '\\' as themselves, every other byte as \u00XX (one code unit per
// byte; the C# test maps code units back to bytes). Doubles are IEEE bits (decimal uint64), floats IEEE bits (decimal uint32).
#include "ArenaSnapshotJson.h"
#include "ArenaSnapshotRepository.h"
#include "ArenaRewards.h"
#include "TownFighterCatalog.h"
#include "pgconfig_stub.h"
#include "../third_party/rapidjson/include/rapidjson/document.h"
#include "../third_party/rapidjson/include/rapidjson/reader.h"
#include "../third_party/rapidjson/include/rapidjson/writer.h"
#include "../third_party/rapidjson/include/rapidjson/stringbuffer.h"
#include "../third_party/rapidjson/include/rapidjson/memorystream.h"
#include <cfloat>
#include <climits>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <map>
#include <string>
#include <vector>

using namespace ArenaPersistence;

static unsigned s_seed = 20261111u;
static unsigned R() { s_seed = s_seed * 1664525u + 1013904223u; return s_seed; }
static unsigned Rn(unsigned n) { return R() % n; }
static bool Rp(unsigned pct) { return Rn(100) < pct; }
static double RU() { const uint64_t a = R(); const uint64_t b = R(); return static_cast<double>(((a << 32) | b) >> 11) / 9007199254740992.0; }

static bool g_first = true;
static void Line(const std::string& s) { std::printf("%s%s\n", g_first ? "[" : ",", s.c_str()); g_first = false; }

static std::string Q(const std::string& v) {
    std::string out = "\"";
    for (size_t i = 0; i < v.size(); ++i) {
        const unsigned char c = static_cast<unsigned char>(v[i]);
        if (c >= 0x20 && c <= 0x7e && c != '"' && c != '\\') out += static_cast<char>(c);
        else { char b[8]; std::snprintf(b, sizeof b, "\\u%04x", c); out += b; }
    }
    return out + "\"";
}
static std::string I(long long v) { char b[32]; std::snprintf(b, sizeof b, "%lld", v); return b; }
static std::string U(unsigned long long v) { char b[32]; std::snprintf(b, sizeof b, "%llu", v); return b; }
static std::string B(bool v) { return v ? "true" : "false"; }
static std::string Dbits(double d) { uint64_t u; std::memcpy(&u, &d, 8); return U(u); }
static std::string Fbits(float f) { uint32_t u; std::memcpy(&u, &f, 4); return U(u); }
static double FromBits(uint64_t u) { double d; std::memcpy(&d, &u, 8); return d; }

// ---- snapshot dump (the C# test writes the same text from its Snapshot) ----------------------------------------------------------
static std::string Strs(const std::vector<std::string>& v) {
    std::string o = "[";
    for (size_t i = 0; i < v.size(); ++i) o += (i ? "," : "") + Q(v[i]);
    return o + "]";
}
static std::string Hist(const std::vector<std::vector<std::string> >& h) {
    std::string o = "[";
    for (size_t i = 0; i < h.size(); ++i) o += (i ? "," : "") + Strs(h[i]);
    return o + "]";
}
static std::string DumpOffer(TownChallengePolicy::Offer& o) {
    std::string r = "[" + I(o.epoch) + "," + I(o.encounter) + "," + I(o.format) + "," + I(o.mode) + "," + I(o.state) + "," + B(o.generated) +
        "," + U(o.draw) + "," + I(o.division) + "," + I(o.playerCount) + "," + I(o.enemyCount) + "," + Q(o.context);
    for (int i = 0; i < 4; ++i)
        r += ",[" + Q(TownChallengePolicy::OpponentId(o, i)) + "," + Q(TownChallengePolicy::OpponentName(o, i)) + "," + Q(o.roles[i]) + "]";
    return r + "]";
}
static std::string Rows(const std::vector<LeaderboardData::Standing>& v) {
    std::string o = "[";
    for (size_t i = 0; i < v.size(); ++i)
        o += std::string(i ? "," : "") + "[" + Q(v[i].id) + "," + Q(v[i].name) + "," + Fbits(v[i].mmr) + "," + I(v[i].wins) + "," + I(v[i].losses) + "," + I(v[i].matches) + "]";
    return o + "]";
}
static std::string Dump(const Snapshot& in) {
    Snapshot s = in;
    std::string o = "{\"version\":" + I(s.version) + ",\"saveKey\":" + Q(s.saveKey) + ",\"generation\":" + Q(s.generation);
    o += ",\"ps\":" + Rows(s.fighters.playerStandings) + ",\"ts\":" + Rows(s.fighters.townStandings) + ",\"pr\":[";
    for (size_t i = 0; i < s.fighters.progression.size(); ++i) {
        const LeaderboardData::Progression& p = s.fighters.progression[i];
        o += std::string(i ? "," : "") + "[" + Q(p.id) + "," + Q(p.name) + "," + I(p.marks) + ",[";
        for (size_t j = 0; j < p.rewardTiers.size(); ++j) o += std::string(j ? "," : "") + "[" + Q(p.rewardTiers[j].id) + "," + I(p.rewardTiers[j].tier) + "]";
        o += "]]";
    }
    o += "],\"fu\":" + Strs(s.fighters.factionUnlocks);
    TownChallengePolicy::Card& c = s.challenges;
    o += ",\"ch\":[" + U(c.seed) + "," + U(c.ambientSeed) + "," + I(c.division) + "," + U(c.milestones) + "," + U(c.uniqueWins) + "," + U(c.challengeWins) +
        "," + Hist(c.playerHistory) + "," + Hist(c.ambientHistory) + ",[";
    for (int i = 0; i < 5; ++i) o += (i ? "," : "") + DumpOffer(c.offers[i]);
    o += "]," + DumpOffer(c.skarnOffer) + "," + B(c.skarnGenerated) + "," + B(c.skarnWon) + "," + B(c.skarnAttempted) + "," + B(c.skarnInProgress) + "," +
        Dbits(c.skarnLastEnd) + "," + Dbits(c.lastChallengeEndHours) + "," + B(c.hasChallengeEnd) + "," + I(c.paidRefreshCount) + "," + U(c.paidRefreshNonce) + "]";
    const TownDiagnosticData::State& d = s.diagnostic;
    o += ",\"dg\":[" + I(d.status) + "," + U(d.runId) + "," + U(d.nextSequence) + "," + I(d.config.target) + ",[";
    for (int i = 0; i < TownDiagnosticData::ModelCount; ++i) o += (i ? "," : "") + B(d.config.models[i]);
    o += "],[";
    for (int i = 0; i < TownDiagnosticData::CoverageCount; ++i) o += (i ? "," : "") + B(d.config.coverage[i]);
    o += "]," + Dbits(d.config.reducedInfluence) + "," + I(d.config.underdogPercent) + "," + B(d.config.balanceStyleTier);
    const TownDiagnosticData::Counters& k = d.counters;
    o += "," + I(k.opened) + "," + I(k.completed) + "," + I(k.cancelled) + "," + I(k.aborted) + "," + I(k.teamAWins) + "," + I(k.teamBWins) + "," +
        I(k.largerWins) + "," + I(k.smallerWins) + "," + I(k.equalWins) + "," + I(k.favoriteWins) + ",[";
    for (int i = 0; i < TownDiagnosticData::ModelCount; ++i) o += (i ? "," : "") + I(k.modelCompleted[i]);
    o += "],[";
    for (int i = 0; i < TownDiagnosticData::CoverageCount; ++i) o += (i ? "," : "") + I(k.coverageCompleted[i]);
    o += "]," + Dbits(k.schedulingHours) + "," + Dbits(k.lastCompletedHours) + "," + I(k.schedulingSamples);
    const TownDiagnosticData::Pending& q = d.pending;
    o += "," + B(q.active) + "," + B(q.underdog) + "," + U(q.sequence) + "," + I(q.model) + "," + I(q.requestedCoverage) + "," + I(q.actualCoverage) + "," +
        I(q.relaxation) + "," + I(q.sizeA) + "," + I(q.sizeB);
    const double* arrays[3] = { q.scoreA, q.scoreB, q.probabilityA };
    for (int a = 0; a < 3; ++a) {
        o += ",[";
        for (int i = 0; i < TownDiagnosticData::ModelCount; ++i) o += (i ? "," : "") + Dbits(arrays[a][i]);
        o += "]";
    }
    o += "," + Dbits(q.announcedHours) + "]";
    o += ",\"bc\":" + I(s.bookieCredit) + ",\"cc\":" + I(s.challengeCredit);
    const PlannedNpcBout& b = s.plannedNpc;
    o += ",\"np\":[" + B(b.active) + "," + Q(b.registryId) + "," + Dbits(b.startHours) + "," + Dbits(b.scoreA) + "," + Dbits(b.scoreB) + "," +
        Strs(b.teamA) + "," + Strs(b.teamB) + "," + B(b.market) + "," + B(b.wagerPending) + "," + I(b.wagerSide) + "," + I(b.stake) + "," + I(b.payout) + "]";
    o += ",\"de\":" + Q(s.diagnosticError) + "}";
    return o;
}

// ---- texts as splices of a base ---------------------------------------------------------------------------------------------------
static std::vector<std::string> g_bases;
static int AddBase(const std::string& text) { g_bases.push_back(text); Line("{\"k\":\"base\",\"i\":" + I(g_bases.size() - 1) + ",\"text\":" + Q(text) + "}"); return static_cast<int>(g_bases.size() - 1); }
// T: base index (-1 = empty text), then the bytes [at, at+del) replaced by ins, then pad spaces appended.
static std::string T(const std::string& text, int base, size_t pad = 0) {
    const std::string& b = base < 0 ? std::string() : g_bases[base];
    const std::string body = text.substr(0, text.size() - pad);
    size_t pre = 0;
    while (pre < b.size() && pre < body.size() && b[pre] == body[pre]) ++pre;
    size_t suf = 0;
    while (suf < b.size() - pre && suf < body.size() - pre && b[b.size() - 1 - suf] == body[body.size() - 1 - suf]) ++suf;
    return "{\"b\":" + I(base) + ",\"at\":" + U(pre) + ",\"del\":" + U(b.size() - pre - suf) + ",\"ins\":" + Q(body.substr(pre, body.size() - pre - suf)) +
        ",\"pad\":" + U(pad) + "}";
}
static std::string Replace(const std::string& text, const std::string& find, const std::string& repl, int nth = 0) {
    size_t at = text.find(find);
    for (int i = 0; i < nth && at != std::string::npos; ++i) at = text.find(find, at + 1);
    if (at == std::string::npos) { std::fprintf(stderr, "gen_snapshot: '%s' not found\n", find.c_str()); std::exit(1); }
    return text.substr(0, at) + repl + text.substr(at + find.size());
}

// ---- random snapshots -------------------------------------------------------------------------------------------------------------
static const char* const kPool[] = { "a", "Shutori", "Gr\xc3\xb6\xc3\x9f" "e", "\xe6\x97\xa5\xe6\x9c\xac\xe8\xaa\x9e", "\xf0\x9f\x98\x80x",
    "q\"uote", "back\\slash", "tab\tnl\n", "ctl\x01\x1f", "del\x7f", "slash/", "  spaced  ", "\xc3\xa9", "Rock & Roll <b>", "x" };
static std::string RStr() { const unsigned k = Rn(20); if (k < 15) return kPool[k]; if (k == 15) return ""; return "n" + U(R() % 100000); }
static std::string RName() { return Rp(15) ? "" : RStr(); }
static double RD(double hi) {
    switch (Rn(10)) {
    case 0: return 0.0;
    case 1: return static_cast<double>(Rn(1000)) <= hi ? static_cast<double>(Rn(1000)) : hi;
    case 2: { const double v = Rn(100000) + 0.5; return v <= hi ? v : hi * 0.5; }
    case 3: return RU() * hi;
    case 4: return RU() * RU() * hi;
    case 5: return hi;
    case 6: { const double t[] = { 5e-324, 1e-300, 2.2250738585072014e-308, 2.2250738585072009e-308, 1e-7, 0.1, 1e-6, 123456.789 };
              const double v = t[Rn(8)]; return v <= hi ? v : hi; }
    case 7: return RU() * (hi < 1000.0 ? hi : 1000.0);
    case 8: { const double v = std::ldexp(RU() + 1.0, static_cast<int>(Rn(200)) - 100); return v <= hi ? v : RU() * hi; }
    default: return std::floor(RU() * hi);
    }
}
static double RPos(double hi) { double v; do { v = RD(hi); } while (!(v > 0.0)); return v; }
static float RMmr() {
    switch (Rn(9)) {
    case 0: return 100.0f;
    case 1: return static_cast<float>(Rn(5000)) + 0.25f;
    case 2: return static_cast<float>(RU() * 3000.0);
    case 3: return 0.0f;
    case 4: return -0.0f;
    case 5: return 1.4e-45f;
    case 6: return FLT_MAX;
    case 7: return 1e-40f;
    default: return static_cast<float>(RU() * 1e30);
    }
}
static int RInt() { switch (Rn(5)) { case 0: return 0; case 1: return static_cast<int>(Rn(100)); case 2: return INT_MAX; default: return static_cast<int>(R() & 0x7fffffffu); } }
static std::vector<std::string> g_roles;
static std::vector<std::string> g_unlocks;
static std::string RRole() { return g_roles[Rn(static_cast<unsigned>(g_roles.size()))]; }
static std::string RRoleSet() { std::string r; do { r = RRole(); } while (r.empty()); return r; }
static void Standings(std::vector<LeaderboardData::Standing>& rows, const char* prefix) {
    const unsigned n = Rn(5);
    for (unsigned i = 0; i < n; ++i) {
        LeaderboardData::Standing s; s.id = std::string(prefix) + U(R() % 1000) + "-" + U(i); s.name = RName(); s.mmr = RMmr();
        s.wins = RInt(); s.losses = RInt(); s.matches = RInt(); rows.push_back(s);
    }
}
static void Offer(TownChallengePolicy::Offer& o, bool skarn, bool unlocked) {
    o = TownChallengePolicy::Offer();
    o.epoch = Rp(20) ? -2 : static_cast<int>(Rn(2000000)) - 1;
    o.state = static_cast<TownArenaPolicy::OfferState>(Rn(3));
    o.draw = Rn(10000); o.division = static_cast<int>(Rn(3)); o.mode = static_cast<int>(Rn(2));
    if (Rp(50)) {
        o.generated = true;
        o.enemyCount = static_cast<int>(Rn(5)); o.playerCount = o.enemyCount ? 1 + static_cast<int>(Rn(3)) : 0;
        if (o.state == TownArenaPolicy::OfferBooked && o.enemyCount == 0) o.state = TownArenaPolicy::OfferAvailable;
        o.encounter = static_cast<int>(Rn(skarn ? 6 : 9)); if (!skarn && o.encounter == 5 && !unlocked) o.encounter = 4;
        o.format = static_cast<int>(Rn(skarn ? 7 : 6));
        o.context = o.enemyCount ? RStr() + "ctx" : (Rp(50) ? std::string() : RStr());
        for (int i = 0; i < o.enemyCount; ++i) {
            TownChallengePolicy::OpponentId(o, i) = "op" + U(i) + "-" + U(R() % 50); TownChallengePolicy::OpponentName(o, i) = RName(); o.roles[i] = RRoleSet();
        }
    } else {
        o.encounter = static_cast<int>(Rn(skarn ? 6 : 9)); if (!skarn && o.encounter == 5 && !unlocked) o.encounter = 7;
        o.format = static_cast<int>(Rn(skarn ? 7 : 6));
        o.context = Rp(50) ? std::string() : RStr();
        o.playerCount = static_cast<int>(Rn(4)); o.enemyCount = static_cast<int>(Rn(4));
        const int used = TownChallengePolicy::Opponents(o);
        for (int i = 0; i < 4; ++i) {
            if (i < used && Rp(80)) { TownChallengePolicy::OpponentId(o, i) = "op" + U(i) + "-" + U(R() % 50); TownChallengePolicy::OpponentName(o, i) = RName(); }
            if (Rp(40)) o.roles[i] = RRole();
        }
    }
}
static void History(std::vector<std::vector<std::string> >& h) {
    const unsigned n = Rn(6);
    for (unsigned i = 0; i < n; ++i) {
        std::vector<std::string> bout; const unsigned m = 1 + Rn(8);
        for (unsigned j = 0; j < m; ++j) bout.push_back("h" + U(j) + "-" + U(R() % 100));
        h.push_back(bout);
    }
}
static Snapshot RandomSnapshot(int ceiling) {
    Snapshot s;
    s.saveKey = Rp(80) ? "save" + U(Rn(10)) : RStr() + "k"; s.generation = Rp(80) ? "gen-" + U(R()) : RStr() + "g";
    Standings(s.fighters.playerStandings, "p"); Standings(s.fighters.townStandings, "t");
    const unsigned np = Rn(4);
    for (unsigned i = 0; i < np; ++i) {
        LeaderboardData::Progression p; p.id = "f" + U(R() % 1000) + "-" + U(i); p.name = RName(); p.marks = RInt();
        for (int piece = 0; piece < ArenaRewards::PieceCount; ++piece) {
            if (!Rp(25)) continue;
            int tier; do { tier = static_cast<int>(Rn(ArenaRewards::TierMaxCount)); } while (!ArenaRewards::IsValidTier(static_cast<ArenaRewards::Piece>(piece), static_cast<ArenaRewards::Tier>(tier)));
            p.rewardTiers.push_back(RewardProgression::TierEntry(ArenaRewards::StableId(static_cast<ArenaRewards::Piece>(piece)), tier));
        }
        // the writer sorts tiers by id; keep the insertion order random so the decode order shows it
        for (size_t a = p.rewardTiers.size(); a > 1; --a) std::swap(p.rewardTiers[a - 1], p.rewardTiers[Rn(static_cast<unsigned>(a))]);
        s.fighters.progression.push_back(p);
    }
    for (size_t i = 0; i < g_unlocks.size(); ++i) if (Rp(30)) s.fighters.factionUnlocks.push_back(g_unlocks[i]);
    for (size_t a = s.fighters.factionUnlocks.size(); a > 1; --a) std::swap(s.fighters.factionUnlocks[a - 1], s.fighters.factionUnlocks[Rn(static_cast<unsigned>(a))]);
    TownChallengePolicy::Card& c = s.challenges;
    c.seed = R(); c.ambientSeed = R(); c.division = static_cast<int>(Rn(3)); c.milestones = Rn(8);
    c.uniqueWins = Rp(30) ? 31u : Rn(32); c.challengeWins = Rp(10) ? 1000000000u : Rn(1000);
    History(c.playerHistory); History(c.ambientHistory);
    const bool unlocked = TownChallengePolicy::SkarnUnlocked(c);
    for (int i = 0; i < 5; ++i) Offer(c.offers[i], false, unlocked);
    Offer(c.skarnOffer, true, unlocked);
    c.skarnGenerated = Rp(40); if (c.skarnGenerated) c.skarnOffer.encounter = 5;
    c.skarnWon = Rp(30); c.skarnAttempted = Rp(50); c.skarnInProgress = Rp(20);
    c.skarnLastEnd = RD(24000000.0); c.lastChallengeEndHours = RD(24000000.0); c.hasChallengeEnd = Rp(50);
    c.paidRefreshCount = Rp(50) ? 0 : RInt(); c.paidRefreshNonce = Rp(30) ? 1u : R();
    TownDiagnosticData::State& d = s.diagnostic;
    if (Rp(60)) {
        d.status = static_cast<int>(Rn(5)); d.runId = R(); d.nextSequence = R();
        d.config.target = 1 + static_cast<int>(Rn(10000));
        for (int i = 0; i < TownDiagnosticData::ModelCount; ++i) d.config.models[i] = Rp(60);
        for (int i = 0; i < TownDiagnosticData::CoverageCount; ++i) d.config.coverage[i] = Rp(60);
        d.config.models[Rn(TownDiagnosticData::ModelCount)] = true; d.config.coverage[Rn(TownDiagnosticData::CoverageCount)] = true;
        d.config.reducedInfluence = RD(1.0); d.config.underdogPercent = static_cast<int>(Rn(101)); d.config.balanceStyleTier = Rp(50);
        TownDiagnosticData::Counters& k = d.counters;
        k.opened = RInt(); k.completed = static_cast<int>(Rn(static_cast<unsigned>(d.config.target) + 1)); k.cancelled = RInt(); k.aborted = RInt();
        k.teamAWins = RInt(); k.teamBWins = RInt(); k.largerWins = RInt(); k.smallerWins = RInt(); k.equalWins = RInt(); k.favoriteWins = RInt();
        for (int i = 0; i < TownDiagnosticData::ModelCount; ++i) k.modelCompleted[i] = RInt();
        for (int i = 0; i < TownDiagnosticData::CoverageCount; ++i) k.coverageCompleted[i] = RInt();
        k.schedulingHours = RD(DBL_MAX); k.lastCompletedHours = RD(24000000.0); k.schedulingSamples = RInt();
        TownDiagnosticData::Pending& q = d.pending;
        q.active = d.status == TownDiagnosticData::Active && Rp(60); q.underdog = Rp(50); q.sequence = q.active ? 1 + Rn(1000) : R();
        q.model = static_cast<int>(Rn(TownDiagnosticData::ModelCount)); q.requestedCoverage = static_cast<int>(Rn(TownDiagnosticData::CoverageCount));
        q.actualCoverage = static_cast<int>(Rn(TownDiagnosticData::CoverageCount)); q.relaxation = static_cast<int>(Rn(4));
        q.sizeA = static_cast<int>(Rn(5)); q.sizeB = static_cast<int>(Rn(5));
        for (int i = 0; i < TownDiagnosticData::ModelCount; ++i) { q.scoreA[i] = RD(DBL_MAX); q.scoreB[i] = RD(1e6); q.probabilityA[i] = RD(1.0); }
        q.announcedHours = RD(24000000.0);
    }
    PlannedNpcBout& b = s.plannedNpc;
    if (Rp(50)) {
        b.active = true; b.registryId = "reg" + U(R() % 1000); b.startHours = RPos(24000000.0); b.scoreA = RPos(DBL_MAX); b.scoreB = RPos(1e9);
        const unsigned na = 1 + Rn(4), nb = 1 + Rn(4);
        for (unsigned i = 0; i < na; ++i) b.teamA.push_back("a" + U(i) + "-" + U(R() % 100));
        for (unsigned i = 0; i < nb; ++i) b.teamB.push_back("b" + U(i) + "-" + U(R() % 100));
        b.market = Rp(60);
        if (b.market && Rp(50)) {
            b.wagerPending = true; b.wagerSide = static_cast<int>(Rn(2));
            b.stake = 10 * (1 + static_cast<int>(Rn(1000)));
            b.payout = b.stake + static_cast<int>(Rn(static_cast<unsigned>(ceiling - b.stake) + 1));
        } else { b.wagerSide = Rp(50) ? -1 : static_cast<int>(Rn(2)); b.stake = static_cast<int>(Rn(1000)); b.payout = RInt(); }
    }
    s.bookieCredit = b.wagerPending ? 0 : (Rp(50) ? 0 : static_cast<int>(Rn(static_cast<unsigned>(ceiling) + 1)));
    s.challengeCredit = Rp(50) ? 0 : static_cast<int>(Rn(20001));
    return s;
}
// One rule break per fault number (every encode/decode rejection reachable from a snapshot object). Returns false past the last one.
static bool Fault(Snapshot& s, int f) {
    TownChallengePolicy::Card& c = s.challenges; TownDiagnosticData::State& d = s.diagnostic; PlannedNpcBout& b = s.plannedNpc;
    LeaderboardData::Standing st; st.id = "dup"; st.name = "n";
    LeaderboardData::Progression pr; pr.id = "pdup";
    TownChallengePolicy::Offer& o = c.offers[Rn(5)];
    switch (f) {
    case 0: s.version = 15; break;
    case 1: s.version = 17; break;
    case 2: s.saveKey = ""; break;
    case 3: s.generation = ""; break;
    case 4: s.saveKey = std::string("nul\0x", 5); break;
    case 5: s.fighters.playerStandings.push_back(st); s.fighters.playerStandings.push_back(st); break;
    case 6: st.id = ""; s.fighters.townStandings.push_back(st); break;
    case 7: st.mmr = NAN; s.fighters.playerStandings.push_back(st); break;
    case 8: st.mmr = INFINITY; s.fighters.townStandings.push_back(st); break;
    case 9: st.mmr = -1.0f; s.fighters.playerStandings.push_back(st); break;
    case 10: st.wins = -1; s.fighters.townStandings.push_back(st); break;
    case 11: s.fighters.progression.push_back(pr); s.fighters.progression.push_back(pr); break;
    case 12: pr.rewardTiers.push_back(RewardProgression::TierEntry("weapon.unknown", 1)); s.fighters.progression.push_back(pr); break;
    case 13: pr.rewardTiers.push_back(RewardProgression::TierEntry(ArenaRewards::StableId(ArenaRewards::PieceGreatsword), 0)); s.fighters.progression.push_back(pr); break;
    case 14: pr.rewardTiers.push_back(RewardProgression::TierEntry(ArenaRewards::StableId(ArenaRewards::PieceCloak), 1));
             pr.rewardTiers.push_back(RewardProgression::TierEntry(ArenaRewards::StableId(ArenaRewards::PieceCloak), 2)); s.fighters.progression.push_back(pr); break;
    case 15: pr.marks = -5; s.fighters.progression.push_back(pr); break;
    case 16: s.fighters.factionUnlocks.push_back("armour.nope"); break;
    case 17: s.fighters.factionUnlocks.push_back(g_unlocks[0]); s.fighters.factionUnlocks.push_back(g_unlocks[0]); break;
    case 18: c.playerHistory.assign(6, std::vector<std::string>(1, "x")); break;
    case 19: c.ambientHistory.push_back(std::vector<std::string>()); break;
    case 20: { std::vector<std::string> v; for (int i = 0; i < 9; ++i) v.push_back("i" + I(i)); c.playerHistory.push_back(v); break; }
    case 21: c.ambientHistory.push_back(std::vector<std::string>(2, "same")); break;
    case 22: o.epoch = -3; break;
    case 23: o.encounter = 9; break;
    case 24: c.uniqueWins = 15; o.encounter = 5; break;
    case 25: o.format = 6; break;
    case 26: o.mode = 3; break;
    case 27: o.state = static_cast<TownArenaPolicy::OfferState>(3); break;
    case 28: o.draw = 10000; break;
    case 29: o.division = 3; break;
    case 30: o.playerCount = 4; break;
    case 31: o.enemyCount = 5; break;
    case 32: o.generated = false; o.format = 4; o.opponent = "same"; o.secondOpponent = "same"; break;
    case 33: o.roles[2] = "999-Proving Grounds.mod"; break;
    case 34: o = TownChallengePolicy::Offer(); o.generated = true; o.playerCount = 1; o.enemyCount = 2; o.context = "c"; o.opponent = "x"; o.roles[0] = g_roles[0]; break;
    case 35: o = TownChallengePolicy::Offer(); o.generated = true; o.thirdName = "ghost"; break;
    case 36: o = TownChallengePolicy::Offer(); o.format = 0; o.secondOpponent = "extra"; break;
    case 37: o = TownChallengePolicy::Offer(); o.generated = true; o.playerCount = 1; break;
    case 38: o = TownChallengePolicy::Offer(); o.generated = true; o.playerCount = 1; o.enemyCount = 1; o.opponent = "x"; o.roles[0] = g_roles[1]; break;
    case 39: o = TownChallengePolicy::Offer(); o.generated = true; o.state = TownArenaPolicy::OfferBooked; break;
    case 40: c.skarnGenerated = true; c.skarnOffer.encounter = 4; break;
    case 41: c.skarnLastEnd = NAN; break;
    case 42: c.skarnLastEnd = 25000000.0; break;
    case 43: c.lastChallengeEndHours = -1.0; break;
    case 44: c.paidRefreshCount = -1; break;
    case 45: c.milestones = 8; break;
    case 46: c.uniqueWins = 32; break;
    case 47: c.challengeWins = 1000000001u; break;
    case 48: c.division = 3; break;
    case 49: d.status = 5; break;
    case 50: d.config.target = 0; break;
    case 51: for (int i = 0; i < TownDiagnosticData::ModelCount; ++i) d.config.models[i] = false; break;
    case 52: for (int i = 0; i < TownDiagnosticData::CoverageCount; ++i) d.config.coverage[i] = false; break;
    case 53: d.config.reducedInfluence = 1.5; break;
    case 54: d.config.underdogPercent = 101; break;
    case 55: d.counters.cancelled = -7; break;
    case 56: d.config.target = 3; d.counters.completed = 4; break;
    case 57: d.status = 0; d.pending.active = true; d.pending.sequence = 3; break;
    case 58: d.pending.model = 3; break;
    case 59: d.pending.probabilityA[2] = 1.5; break;
    case 60: d.pending.scoreA[1] = NAN; break;
    case 61: d.pending.announcedHours = INFINITY; break;
    case 62: s.bookieCredit = PGConfig::BookieCreditCeiling() + 1; break;
    case 63: s.bookieCredit = -1; break;
    case 64: s.challengeCredit = 20001; break;
    case 65: b = PlannedNpcBout(); b.active = true; b.startHours = 1; b.scoreA = 1; b.scoreB = 1; b.teamA.push_back("a"); b.teamB.push_back("b"); break;
    case 66: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.scoreA = 1; b.scoreB = 1; b.teamA.push_back("a"); b.teamB.push_back("b"); break;
    case 67: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.startHours = 1; b.scoreA = 1; b.scoreB = 1; b.teamA.push_back("a"); break;
    case 68: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.startHours = 1; b.scoreA = 1; b.scoreB = 1;
             for (int i = 0; i < 5; ++i) b.teamA.push_back("a" + I(i)); b.teamB.push_back("b"); break;
    case 69: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.startHours = 1; b.scoreA = 1; b.scoreB = 1; b.teamA.push_back("x"); b.teamB.push_back("x"); break;
    case 70: b = PlannedNpcBout(); b.wagerPending = true; break;
    case 71: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.startHours = 1; b.scoreA = 1; b.scoreB = 1; b.teamA.push_back("a"); b.teamB.push_back("b");
             b.market = true; b.wagerPending = true; b.wagerSide = 0; b.stake = 15; b.payout = 30; s.bookieCredit = 0; break;
    case 72: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.startHours = 1; b.scoreA = 1; b.scoreB = 1; b.teamA.push_back("a"); b.teamB.push_back("b");
             b.market = true; b.wagerPending = true; b.wagerSide = 1; b.stake = 100; b.payout = 90; s.bookieCredit = 0; break;
    case 73: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.startHours = 1; b.scoreA = 1; b.scoreB = 1; b.teamA.push_back("a"); b.teamB.push_back("b");
             b.market = true; b.wagerPending = true; b.wagerSide = 1; b.stake = 100; b.payout = PGConfig::BookieCreditCeiling() + 1; s.bookieCredit = 0; break;
    case 74: b = PlannedNpcBout(); b.registryId = "left"; break;
    case 75: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.startHours = 1; b.scoreA = 1; b.scoreB = 1; b.teamA.push_back("a"); b.teamB.push_back("b"); b.wagerSide = 2; break;
    case 76: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.startHours = 1; b.scoreA = 1; b.scoreB = 1; b.teamA.push_back("a"); b.teamB.push_back("b"); b.stake = INT_MAX / 5 + 1; break;
    case 77: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.startHours = 1; b.scoreA = 1; b.scoreB = 1; b.teamA.push_back("a"); b.teamB.push_back("b");
             b.market = true; b.wagerPending = true; b.wagerSide = 0; b.stake = 10; b.payout = 20; s.bookieCredit = 5; break;
    case 78: st.name = std::string("a\0b", 3); s.fighters.playerStandings.push_back(st); break;
    case 79: c.skarnOffer.encounter = 6; break;
    case 80: b.startHours = NAN; break;
    case 81: d.counters.schedulingHours = INFINITY; break;
    case 82: d.counters.lastCompletedHours = 24000001.0; break;
    case 83: b = PlannedNpcBout(); b.active = true; b.registryId = "r"; b.startHours = 1; b.scoreA = 1; b.scoreB = -1.0; b.teamA.push_back("a"); b.teamB.push_back("b"); break;
    case 84: st.mmr = 3.4e38f; st.id = "big"; s.fighters.playerStandings.push_back(st); break; // valid: below FLT_MAX
    case 85: o.roles[0] = TownFighterCatalog::Find("219-Proving Grounds.mod") ? "219-Proving Grounds.mod" : ""; break; // valid catalog role
    case 86: c.uniqueWins = 31; o = TownChallengePolicy::Offer(); o.encounter = 5; break; // valid: unlocked Skarn in a regular slot
    case 87: o.mode = 2; break; // written as 2, read back as Teams (0)
    default: return false;
    }
    return true;
}

// ---- fake storage -------------------------------------------------------------------------------------------------------------
struct FakeFile { ReadCode code; std::string text; bool setError; std::string error; };
struct Fake : Storage {
    std::map<std::string, FakeFile> files;
    bool listOk, listSetError, preserveOk, preserveSetError, replaceOk, replaceSetError;
    std::vector<std::string> list;
    std::string listError, preserveError, backupPath, replaceError;
    std::vector<std::string> log;
    Fake() : listOk(true), listSetError(false), preserveOk(true), preserveSetError(false), replaceOk(true), replaceSetError(false), backupPath("bak") {}
    ReadCode Read(const std::string& path, std::string& text, std::string& error) {
        log.push_back("R:" + path);
        std::map<std::string, FakeFile>::const_iterator f = files.find(path);
        if (f == files.end()) return NotFound;
        if (f->second.code == ReadOk) text = f->second.text;
        if (f->second.setError) error = f->second.error;
        return f->second.code;
    }
    bool ListBackups(const std::string& path, std::vector<std::string>& paths, std::string& error) {
        log.push_back("L:" + path);
        if (listSetError) error = listError;
        if (listOk) paths = list;
        return listOk;
    }
    bool Preserve(const std::string& path, std::string& backup, std::string& error) {
        log.push_back("P:" + path);
        if (preserveSetError) error = preserveError;
        if (preserveOk) backup = backupPath;
        return preserveOk;
    }
    bool Replace(const std::string& path, const std::string& text, std::string& error) {
        log.push_back("W:" + path + ":" + U(text.size()));
        if (replaceSetError) error = replaceError;
        if (replaceOk) { FakeFile f = { ReadOk, text, false, "" }; files[path] = f; }
        return replaceOk;
    }
};
static std::string FakeIn(const Fake& f, int base) {
    std::string o = "\"files\":[";
    bool first = true;
    for (std::map<std::string, FakeFile>::const_iterator i = f.files.begin(); i != f.files.end(); ++i) {
        o += std::string(first ? "" : ",") + "[" + Q(i->first) + "," + I(i->second.code) + "," + T(i->second.text, base) + "," + B(i->second.setError) + "," + Q(i->second.error) + "]";
        first = false;
    }
    o += "],\"list\":[" + B(f.listOk) + "," + Strs(f.list) + "," + B(f.listSetError) + "," + Q(f.listError) + "]";
    o += ",\"preserve\":[" + B(f.preserveOk) + "," + Q(f.backupPath) + "," + B(f.preserveSetError) + "," + Q(f.preserveError) + "]";
    o += ",\"replace\":[" + B(f.replaceOk) + "," + B(f.replaceSetError) + "," + Q(f.replaceError) + "]";
    return o;
}

// ---- rapidjson probes ----------------------------------------------------------------------------------------------------------
struct Recorder : rapidjson::BaseReaderHandler<rapidjson::UTF8<>, Recorder> {
    std::string ev;
    bool Add(const std::string& s) { if (!ev.empty()) ev += " "; ev += s; return true; }
    bool Null() { return Add("n"); }
    bool Bool(bool b) { return Add(b ? "t" : "f"); }
    bool RawNumber(const char* s, rapidjson::SizeType n, bool) { return Add("#" + std::string(s, n)); }
    bool String(const char* s, rapidjson::SizeType n, bool) { return Add("s" + Q(std::string(s, n))); }
    bool Key(const char* s, rapidjson::SizeType n, bool) { return Add("k" + Q(std::string(s, n))); }
    bool StartObject() { return Add("{"); }
    bool EndObject(rapidjson::SizeType c) { return Add("}" + U(c)); }
    bool StartArray() { return Add("["); }
    bool EndArray(rapidjson::SizeType c) { return Add("]" + U(c)); }
};
static void Rdr(const std::string& text) {
    rapidjson::MemoryStream input(text.data(), text.size()); rapidjson::Reader reader; Recorder rec;
    const unsigned flags = rapidjson::kParseValidateEncodingFlag | rapidjson::kParseFullPrecisionFlag | rapidjson::kParseIterativeFlag;
    reader.Parse<flags | rapidjson::kParseNumbersAsStringsFlag>(input, rec);
    Line("{\"k\":\"rdr\",\"t\":" + Q(text) + ",\"ev\":" + Q(rec.ev) + ",\"code\":" + I(reader.GetParseErrorCode()) + ",\"off\":" + U(reader.GetErrorOffset()) + "}");
}
static void Num(const std::string& text) {
    rapidjson::Document number; number.Parse<rapidjson::kParseFullPrecisionFlag>(text.c_str(), text.size());
    std::string ty, d = "0";
    if (!number.HasParseError()) {
        ty = std::string(number.IsInt() ? "i" : "") + (number.IsUint() ? "u" : "") + (number.IsInt64() ? "I" : "") + (number.IsUint64() ? "U" : "") +
            (number.IsDouble() ? "d" : "") + (number.IsNumber() ? "" : "?");
        d = Dbits(number.GetDouble());
        if (number.IsInt()) ty += ":" + I(number.GetInt());
        if (number.IsUint()) ty += ":" + U(number.GetUint());
    }
    Line("{\"k\":\"num\",\"t\":" + Q(text) + ",\"code\":" + I(number.GetParseError()) + ",\"off\":" + U(number.GetErrorOffset()) + ",\"ty\":" + Q(ty) + ",\"d\":" + d + "}");
}
static void Dbl(double v) {
    rapidjson::StringBuffer buffer; rapidjson::Writer<rapidjson::StringBuffer> w(buffer);
    const bool ok = w.Double(v);
    Line("{\"k\":\"dbl\",\"d\":" + Dbits(v) + ",\"ok\":" + B(ok) + ",\"w\":" + Q(std::string(buffer.GetString(), buffer.GetSize())) + "}");
}

// ---- cases --------------------------------------------------------------------------------------------------------------------
static int g_enc = 0, g_dec = 0;
static int CurrentBookieMax() { return PGConfigStub_Bookie().maximum; }
static std::string Enc(const Snapshot& s, std::string* textOut = 0) {
    std::string json = "untouched", error = "stale";
    const bool ok = EncodeSnapshot(s, json, error);
    Line("{\"k\":\"enc\",\"i\":" + I(g_enc++) + ",\"bm\":" + I(CurrentBookieMax()) + ",\"in\":" + Dump(s) + ",\"ok\":" + B(ok) + ",\"err\":" + Q(error) +
        ",\"text\":" + Q(json) + "}");
    if (textOut) *textOut = json;
    return ok ? json : std::string();
}
static void DecT(const std::string& text, const std::string& t) {
    const LoadResult r = DecodeSnapshot(text);
    Line("{\"k\":\"dec\",\"i\":" + I(g_dec++) + ",\"bm\":" + I(CurrentBookieMax()) + ",\"t\":" + t + ",\"code\":" + I(r.code) + ",\"field\":" + Q(r.field) +
        ",\"msg\":" + Q(r.message) + ",\"snap\":" + Dump(r.snapshot) + "}");
}
static void Dec(const std::string& text, int base, size_t pad = 0) { DecT(text, T(text, base, pad)); }
// the text an enc case printed, decoded back ({"e": enc index} instead of a splice)
static void DecEnc(const std::string& text) { DecT(text, "{\"e\":" + I(g_enc - 1) + "}"); }
static std::string RemovePlanned(const std::string& t) {
    const size_t at = t.find(",\n  \"plannedNpc\": {");
    if (at == std::string::npos) { std::fprintf(stderr, "gen_snapshot: no plannedNpc\n"); std::exit(1); }
    return t.substr(0, at) + "\n}\n";
}
static Snapshot Rich() {
    Snapshot s; s.saveKey = "Rich Save"; s.generation = "gen-rich";
    LeaderboardData::Standing a; a.id = "p-b"; a.name = "Beta"; a.mmr = 1234.5f; a.wins = 3; a.losses = 2; a.matches = 5;
    LeaderboardData::Standing b; b.id = "p-a"; b.name = "Alpha"; b.mmr = 99.75f; b.wins = 1; b.losses = 0; b.matches = 1;
    s.fighters.playerStandings.push_back(a); s.fighters.playerStandings.push_back(b);
    LeaderboardData::Standing t; t.id = "t-1"; t.name = "Town"; t.mmr = 100.0f; s.fighters.townStandings.push_back(t);
    LeaderboardData::Progression p; p.id = "p-a"; p.name = "Alpha"; p.marks = 250;
    p.rewardTiers.push_back(RewardProgression::TierEntry(ArenaRewards::StableId(ArenaRewards::PieceKatana), 1));
    p.rewardTiers.push_back(RewardProgression::TierEntry(ArenaRewards::StableId(ArenaRewards::PieceCloak), 0));
    s.fighters.progression.push_back(p);
    s.fighters.factionUnlocks.push_back(g_unlocks[1]); s.fighters.factionUnlocks.push_back(g_unlocks[0]);
    TownChallengePolicy::Card& c = s.challenges; c.seed = 77; c.ambientSeed = 88; c.division = 1; c.milestones = 3; c.uniqueWins = 5; c.challengeWins = 9;
    c.playerHistory.push_back(std::vector<std::string>(1, "op-1")); std::vector<std::string> two; two.push_back("op-2"); two.push_back("op-3"); c.ambientHistory.push_back(two);
    for (int i = 0; i < 5; ++i) { c.offers[i].epoch = 10 + i; c.offers[i].encounter = i; c.offers[i].format = i; c.offers[i].draw = 100 + i; }
    c.offers[0].opponent = "op-1"; c.offers[0].name = "One"; c.offers[0].roles[0] = TownChallengePolicy::Role(0);
    TownChallengePolicy::Offer& g = c.offers[1]; g.generated = true; g.playerCount = 2; g.enemyCount = 2; g.context = "ctx"; g.state = TownArenaPolicy::OfferBooked;
    g.opponent = "g-1"; g.name = "G1"; g.roles[0] = TownChallengePolicy::Role(1); g.secondOpponent = "g-2"; g.secondName = "G2"; g.roles[1] = "219-Proving Grounds.mod";
    c.skarnOffer.encounter = 5; c.skarnOffer.format = 6; c.skarnGenerated = true; c.skarnLastEnd = 12.5; c.lastChallengeEndHours = 30.25; c.hasChallengeEnd = true;
    c.paidRefreshCount = 2; c.paidRefreshNonce = 7;
    TownDiagnosticData::State& d = s.diagnostic; d.status = TownDiagnosticData::Active; d.runId = 4; d.nextSequence = 9; d.counters.opened = 6; d.counters.completed = 5;
    d.pending.active = true; d.pending.sequence = 8; d.pending.scoreA[0] = 0.5; d.pending.probabilityA[1] = 0.75; d.pending.announcedHours = 40.0;
    s.bookieCredit = 0; s.challengeCredit = 1500;
    PlannedNpcBout& n = s.plannedNpc; n.active = true; n.registryId = "reg-1"; n.startHours = 41.5; n.scoreA = 210.0; n.scoreB = 190.0;
    n.teamA.push_back("na-1"); n.teamB.push_back("nb-1"); n.teamB.push_back("nb-2"); n.market = true; n.wagerPending = true; n.wagerSide = 1; n.stake = 100; n.payout = 250;
    return s;
}

int main() {
    for (int i = 0; i <= TownChallengePolicy::EncounterVeyr; ++i) g_roles.push_back(TownChallengePolicy::Role(i));
    g_roles.push_back("219-Proving Grounds.mod"); g_roles.push_back("300-Proving Grounds.mod"); g_roles.push_back("");
    g_unlocks.push_back(ArenaRewards::GetWeaponLicence().stableId);
    for (int i = 0; i < ArenaRewards::ArmourSetNone; ++i) g_unlocks.push_back(ArenaRewards::GetLicence(static_cast<ArenaRewards::ArmourSet>(i)).stableId);
    for (int i = 0; i < ArenaRewards::GeneralItemCount; ++i) {
        const char* u = ArenaRewards::GetGeneral(static_cast<ArenaRewards::GeneralItem>(i)).grantsUnlockId;
        if (u) g_unlocks.push_back(u);
    }

    // 1. encode: the default snapshot, a hand-made rich one, random valid ones, one per fault; each successful text decoded back
    std::string text;
    Enc(Snapshot()); // the default snapshot: empty saveKey, rejected
    Snapshot keyedDefault; keyedDefault.saveKey = "k"; keyedDefault.generation = "g";
    const int defaultBase = AddBase(Enc(keyedDefault));
    const int richBase = AddBase(Enc(Rich()));
    Dec(g_bases[defaultBase], defaultBase); Dec(g_bases[richBase], richBase);
    for (int i = 0; i < 120; ++i) {
        const std::string t = Enc(RandomSnapshot(PGConfig::BookieCreditCeiling()));
        if (!t.empty()) DecEnc(t);
    }
    for (int f = 0; ; ++f) {
        Snapshot s = RandomSnapshot(PGConfig::BookieCreditCeiling());
        if (!Fault(s, f)) break;
        const std::string t = Enc(s);
        if (!t.empty()) DecEnc(t);
    }
    // a raised stake ceiling (bookie maximum 20000 -> credit ceiling 100000)
    PGConfigStub_Bookie().maximum = 20000;
    for (int i = 0; i < 10; ++i) { const std::string t = Enc(RandomSnapshot(PGConfig::BookieCreditCeiling())); if (!t.empty()) DecEnc(t); }
    { Snapshot s = Rich(); s.plannedNpc.payout = 100000; Enc(s); s.plannedNpc.payout = 100001; Enc(s); }
    { Snapshot s; s.saveKey = "k"; s.generation = "g"; s.bookieCredit = 100000; Enc(s); s.bookieCredit = 100001; Enc(s); }
    Dec(Replace(g_bases[defaultBase], "\"bookieCredit\": 0", "\"bookieCredit\": 99999"), defaultBase);
    PGConfigStub_Reset();
    Dec(Replace(g_bases[defaultBase], "\"bookieCredit\": 0", "\"bookieCredit\": 99999"), defaultBase);
    Dec(Replace(g_bases[defaultBase], "\"bookieCredit\": 0", "\"bookieCredit\": 50000"), defaultBase);
    { Snapshot s; s.saveKey = "k"; s.generation = "g"; s.bookieCredit = 50000; Enc(s); }

    // 2. decode: mutations of the rich text (every rejection rule on purpose)
    const std::string& rich = g_bases[richBase];
    const std::string& def = g_bases[defaultBase];
    struct M { const char* find; const char* repl; int nth; };
    const M mutations[] = {
        // unknown fields at every object level
        { "{\n  \"version\"", "{\n  \"extra\": 1,\n  \"version\"", 0 },
        { "\"id\": \"p-a\",", "\"id\": \"p-a\", \"rank\": 1,", 0 },
        { "\"marks\": 250,", "\"marks\": 250, \"bonus\": 1,", 0 },
        { "\"epoch\": 10,", "\"epoch\": 10, \"x\": 0,", 0 },
        { "\"role\": \"96-Proving Grounds.mod\"", "\"role\": \"96-Proving Grounds.mod\", \"y\": 1", 0 },
        { "\"seed\": 77,", "\"seed\": 77, \"seeds\": 1,", 0 },
        { "\"target\": 150,", "\"target\": 150, \"t\": 1,", 0 },
        { "\"opened\": 6,", "\"opened\": 6, \"z\": 1,", 0 },
        { "\"underdog\": false,", "\"underdog\": false, \"w\": 1,", 0 },
        { "\"status\": 1,", "\"status\": 1, \"s\": 1,", 0 },
        { "\"registryId\": \"reg-1\",", "\"registryId\": \"reg-1\", \"r\": 1,", 0 },
        { "\"rewardTiers\": {", "\"rewardTiers\": {\n          \"weapon.nothing\": 1,", 0 },
        { "\"saveKey\": \"Rich Save\",", "\"saveKey\": \"Rich Save\", \"a|b\": 1,", 0 },
        { "\"saveKey\": \"Rich Save\",", "\"saveKey\": \"Rich Save\", \"\": 1,", 0 },
        { "\"saveKey\": \"Rich Save\",", "\"saveKey\": \"Rich Save\", \"save\\u0000Key\": 1,", 0 },
        { "\"saveKey\": \"Rich Save\",", "\"saveKey\": \"Rich Save\", \"performanceProfile\": \"high\",", 0 },
        // duplicate keys (also through escapes) and ids
        { "\"saveKey\": \"Rich Save\",", "\"saveKey\": \"Rich Save\", \"saveKey\": \"x\",", 0 },
        { "\"saveKey\": \"Rich Save\",", "\"saveKey\": \"Rich Save\", \"save\\u004bey\": \"x\",", 0 },
        { "\"saveKey\": \"Rich Save\",", "\"saveKey\": \"Rich Save\", \"\\u0073aveKey\": \"x\",", 0 },
        { "\"wins\": 1,", "\"wins\": 1, \"wins\": 2,", 0 },
        { "\"epoch\": 10,", "\"epoch\": 10, \"epoch\": 11,", 0 },
        { "\"active\": true,", "\"active\": true, \"active\": false,", 1 },
        { "\"id\": \"p-b\"", "\"id\": \"p-a\"", 0 },
        { "\"progression\": [", "\"progression\": [{\"id\": \"p-a\", \"name\": \"\", \"marks\": 0, \"rewardTiers\": {}},", 0 },
        { "\"weapon.all\"", "\"weapon.all\", \"weapon.all\"", 0 },
        { "\"op-2\",", "\"op-3\",", 0 },
        { "\"nb-2\"", "\"na-1\"", 0 },
        { "\"id\": \"g-2\"", "\"id\": \"g-1\"", 0 },
        // wrong types
        { "\"version\": 16", "\"version\": \"16\"", 0 },
        { "\"version\": 16", "\"version\": 16.0", 0 },
        { "\"version\": 16", "\"version\": true", 0 },
        { "\"version\": 16", "\"version\": null", 0 },
        { "\"version\": 16", "\"version\": -1", 0 },
        { "\"version\": 16", "\"version\": 2147483648", 0 },
        { "\"version\": 16", "\"version\": 1e1", 0 },
        { "\"version\": 16,", "", 0 },
        { "\"saveKey\": \"Rich Save\"", "\"saveKey\": 5", 0 },
        { "\"saveKey\": \"Rich Save\"", "\"saveKey\": \"\"", 0 },
        { "\"generation\": \"gen-rich\"", "\"generation\": []", 0 },
        { "\"playerStandings\": [", "\"playerStandings\": {\"a\": [", 0 },
        { "\"wins\": 3", "\"wins\": 3.0", 0 },
        { "\"wins\": 3", "\"wins\": -3", 0 },
        { "\"wins\": 3", "\"wins\": 2147483647", 0 },
        { "\"wins\": 3", "\"wins\": 2147483648", 0 },
        { "\"wins\": 3", "\"wins\": \"3\"", 0 },
        { "\"name\": \"Beta\"", "\"name\": 1", 0 },
        { "\"name\": \"Beta\"", "\"name\": \"a\\u0000b\"", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": \"1234.5\"", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": -1", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": -0.0", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": -0", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 1e-46", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 1e-45", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 7e-46", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 3.4028235e38", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 3.4028236e38", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 3.402823466385288598e38", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 1e-400", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": -0.000e-400", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 0e400", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 1e400", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 10e308", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 1234567890123456789012345678901234567890", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 18446744073709551616", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 4294967296", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": NaN", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": Infinity", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": -Infinity", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 01", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 1.", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": .5", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 1e", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 1e+", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": +1", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": -", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 1E2", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 2.5E-3", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 0.000000000000000000000000000000000000000000001", 0 },
        { "\"mmr\": 1234.5", "\"mmr\": 123.4500000000000000000000000000000000000001", 0 },
        { "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 24000000.0", 0 },
        { "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 24000000.000000004", 0 },
        { "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 24000000.00000001", 0 },
        { "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 2.2250738585072011e-308", 0 },
        { "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 4.9406564584124654e-324", 0 },
        { "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 2.4703282292062328e-324", 0 },
        { "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 2.4703282292062327e-324", 0 },
        { "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 1e-323", 0 },
        { "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 0.1e-322", 0 },
        { "\"schedulingHours\": 0.0", "\"schedulingHours\": 1.7976931348623157e308", 0 },
        { "\"schedulingHours\": 0.0", "\"schedulingHours\": 1.7976931348623158e308", 0 },
        { "\"schedulingHours\": 0.0", "\"schedulingHours\": 1.7976931348623159e308", 0 },
        { "\"schedulingHours\": 0.0", "\"schedulingHours\": 179769313486231580793728971405303415079934132710037826936173778980444968292764750946649017977587207096330286416692887910946555547851940402630657488671505820681908902000708383676273854845817711531764475730270069855571366959622842914819860834936475292719074168444365510704342711559699508093042880177904174497791", 0 },
        { "\"schedulingHours\": 0.0", "\"schedulingHours\": 179769313486231580793728971405303415079934132710037826936173778980444968292764750946649017977587207096330286416692887910946555547851940402630657488671505820681908902000708383676273854845817711531764475730270069855571366959622842914819860834936475292719074168444365510704342711559699508093042880177904174497792", 0 },
        { "\"seed\": 77", "\"seed\": 4294967295", 0 },
        { "\"seed\": 77", "\"seed\": 4294967296", 0 },
        { "\"seed\": 77", "\"seed\": -1", 0 },
        { "\"seed\": 77", "\"seed\": -0", 0 },
        { "\"seed\": 77", "\"seed\": 77.0", 0 },
        { "\"milestones\": 3", "\"milestones\": 8", 0 },
        { "\"uniqueWins\": 5,", "", 0 },
        { "\"uniqueWins\": 5", "\"uniqueWins\": 32", 0 },
        { "\"challengeWins\": 9", "\"challengeWins\": 1000000000", 0 },
        { "\"challengeWins\": 9", "\"challengeWins\": 1000000001", 0 },
        { "\"division\": 1,\n    \"milestones\"", "\"division\": 3,\n    \"milestones\"", 0 },
        { "\"lastChallengeEndHours\": 30.25,", "", 0 },
        { "\"hasChallengeEnd\": true,", "", 0 },
        { "\"paidRefreshCount\": 2,", "", 0 },
        { "\"paidRefreshNonce\": 7", "\"paidRefreshNonce\": 7, \"zz\": 1", 0 },
        { ",\n    \"paidRefreshNonce\": 7", "", 0 },
        { "\"hasChallengeEnd\": true", "\"hasChallengeEnd\": 1", 0 },
        { "\"paidRefreshCount\": 2", "\"paidRefreshCount\": -2", 0 },
        // history
        { "\"playerHistory\": [", "\"playerHistory\": [[\"a\"],[\"b\"],[\"c\"],[\"d\"],[\"e\"],", 0 },
        { "\"playerHistory\": [", "\"playerHistory\": [[],", 0 },
        { "\"playerHistory\": [", "\"playerHistory\": [[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\",\"9\"],", 0 },
        { "\"playerHistory\": [", "\"playerHistory\": [[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\"],", 0 },
        { "\"playerHistory\": [", "\"playerHistory\": [[\"\"],", 0 },
        { "\"playerHistory\": [", "\"playerHistory\": [[1],", 0 },
        { "\"playerHistory\": [", "\"playerHistory\": [\"x\",", 0 },
        { "\"playerHistory\": [", "\"playerHistory\": {", 0 },
        // offers
        { "\"epoch\": 10", "\"epoch\": -3", 0 },
        { "\"epoch\": 10", "\"epoch\": 1999999", 0 },
        { "\"epoch\": 10", "\"epoch\": 2000000", 0 },
        { "\"encounter\": 4", "\"encounter\": 9", 0 },
        { "\"encounter\": 4", "\"encounter\": 5", 0 },
        { "\"format\": 4", "\"format\": 6", 0 },
        { "\"format\": 6", "\"format\": 7", 0 },
        { "\"mode\": 0,", "\"mode\": 2,", 0 },
        { "\"mode\": 0,", "\"mode\": 3,", 0 },
        { "\"mode\": 0,", "", 0 },
        { "\"mode\": 0,", "\"mode\": 1,", 0 },
        { "\"state\": 0", "\"state\": 3", 0 },
        { "\"generated\": false", "\"generated\": 0", 0 },
        { "\"draw\": 100", "\"draw\": 9999", 0 },
        { "\"draw\": 100", "\"draw\": 10000", 0 },
        { "\"draw\": 100", "\"draw\": -1", 0 },
        { "\"playerCount\": 0", "\"playerCount\": 4", 0 },
        { "\"enemyCount\": 0", "\"enemyCount\": 5", 0 },
        { "\"context\": \"\"", "\"context\": 0", 0 },
        { "\"opponents\": [", "\"opponents\": {", 0 },
        { "\"role\": \"96-Proving Grounds.mod\"", "\"role\": \"999-Proving Grounds.mod\"", 0 },
        { "\"role\": \"96-Proving Grounds.mod\"", "\"role\": \"623-Proving Grounds.mod\"", 0 },
        { "\"role\": \"96-Proving Grounds.mod\"", "\"role\": \"300-Proving Grounds.mod\"", 0 },
        { "\"role\": \"96-Proving Grounds.mod\"", "\"role\": 96", 0 },
        { "\"id\": \"op-1\",\n            \"name\": \"One\"", "\"id\": \"op-1\",\n            \"nom\": \"One\"", 0 },
        { "\"id\": \"op-1\"", "\"id\": \"\"", 0 },
        { "\"id\": \"g-2\"", "\"id\": \"\"", 0 },
        { "\"enemyCount\": 2", "\"enemyCount\": 1", 0 },
        { "\"enemyCount\": 2", "\"enemyCount\": 3", 0 },
        { "\"playerCount\": 2", "\"playerCount\": 0", 0 },
        { "\"context\": \"ctx\"", "\"context\": \"\"", 0 },
        { "\"state\": 1", "\"state\": 2", 0 },
        { "\"skarnGenerated\": true", "\"skarnGenerated\": false", 0 },
        { "\"offers\": [", "\"offers\": [{},", 0 },
        { "\"offers\": [", "\"offers\": 1, \"o\": [", 0 },
        { "\"skarnOffer\": {", "\"skarnOffer\": [", 0 },
        // diagnostic (an invalid subsection keeps the rest: Ready with diagnosticError)
        { "\"status\": 1", "\"status\": 5", 0 },
        { "\"status\": 1", "\"status\": -1", 0 },
        { "\"target\": 150", "\"target\": 0", 0 },
        { "\"target\": 150", "\"target\": 10001", 0 },
        { "\"target\": 150", "\"target\": 4", 0 },
        { "\"models\": [\n        true,\n        true,\n        true\n      ]", "\"models\": [false, false, false]", 0 },
        { "\"coverage\": [\n        true,\n        true,\n        true,\n        true\n      ]", "\"coverage\": [false, false, false, false]", 0 },
        { "\"models\": [\n        true,", "\"models\": [\n", 0 },
        { "\"models\": [\n        true,", "\"models\": [\n        1,", 0 },
        { "\"reducedInfluence\": 0.25", "\"reducedInfluence\": 1.0000000000000002", 0 },
        { "\"underdogPercent\": 20", "\"underdogPercent\": 101", 0 },
        { "\"modelCompleted\": [\n        0,", "\"modelCompleted\": [\n        -1,", 0 },
        { "\"modelCompleted\": [\n        0,", "\"modelCompleted\": [\n", 0 },
        { "\"scoreA\": [\n        0.5,", "\"scoreA\": [\n        -0.5,", 0 },
        { "\"scoreA\": [\n        0.5,", "\"scoreA\": [\n        \"x\",", 0 },
        { "\"probabilityA\": [\n        0.0,\n        0.75", "\"probabilityA\": [\n        0.0,\n        1.75", 0 },
        { "\"sequence\": 8", "\"sequence\": 0", 0 },
        { "\"model\": 0", "\"model\": 3", 0 },
        { "\"relaxation\": 0", "\"relaxation\": 4", 0 },
        { "\"sizeA\": 0", "\"sizeA\": 5", 0 },
        { "\"announcedHours\": 40.0", "\"announcedHours\": 24000001", 0 },
        { "\"lastCompletedHours\": 0.0", "\"lastCompletedHours\": 24000001", 0 },
        { "\"schedulingSamples\": 0", "\"schedulingSamples\": -1", 0 },
        { "\"pending\": {", "\"pendings\": {", 0 },
        { "\"diagnostic\": {", "\"diagnostic\": [", 0 },
        { "\"diagnostic\": {", "\"diagnostic\": 1, \"d\": {", 0 },
        // credits and planned bout
        { "\"bookieCredit\": 0", "\"bookieCredit\": 1", 0 },
        { "\"challengeCredit\": 1500", "\"challengeCredit\": 20000", 0 },
        { "\"challengeCredit\": 1500", "\"challengeCredit\": 20001", 0 },
        { "\"stake\": 100", "\"stake\": 105", 0 },
        { "\"stake\": 100", "\"stake\": 0", 0 },
        { "\"stake\": 100", "\"stake\": 429496729", 0 },
        { "\"stake\": 100", "\"stake\": 429496730", 0 },
        { "\"payout\": 250", "\"payout\": 50", 0 },
        { "\"payout\": 250", "\"payout\": 50001", 0 },
        { "\"wagerSide\": 1", "\"wagerSide\": -1", 0 },
        { "\"wagerSide\": 1", "\"wagerSide\": -2", 0 },
        { "\"market\": true", "\"market\": false", 0 },
        { "\"active\": true,\n    \"registryId\"", "\"active\": false,\n    \"registryId\"", 0 },
        { "\"registryId\": \"reg-1\"", "\"registryId\": \"\"", 0 },
        { "\"startHours\": 41.5", "\"startHours\": 0.0", 0 },
        { "\"startHours\": 41.5", "\"startHours\": 24000000.5", 0 },
        { "\"scoreA\": 210.0", "\"scoreA\": 0", 0 },
        { "\"teamA\": [\n      \"na-1\"\n    ]", "\"teamA\": []", 0 },
        { "\"teamA\": [\n      \"na-1\"\n    ]", "\"teamA\": [\"1\",\"2\",\"3\",\"4\",\"5\"]", 0 },
        { "\"teamA\": [\n      \"na-1\"\n    ]", "\"teamA\": [\"1\",\"2\",\"3\",\"4\"]", 0 },
        { "\"teamA\": [\n      \"na-1\"\n    ]", "\"teamA\": [\"\"]", 0 },
        { "\"teamA\": [\n      \"na-1\"\n    ]", "\"teamA\": \"na-1\"", 0 },
        { "\"wagerPending\": true", "\"wagerPending\": \"yes\"", 0 },
        { "\"plannedNpc\": {", "\"plannedNpc\": null, \"p\": {", 0 },
        // rewards and unlocks
        { "\"armour.warlord.cloak\": 0", "\"armour.warlord.cloak\": 1", 0 },
        { "\"armour.warlord.cloak\": 0", "\"armour.warlord.cloak\": 4", 0 },
        { "\"armour.warlord.cloak\": 0", "\"armour.warlord.cloak\": -1", 0 },
        { "\"armour.warlord.cloak\": 0", "\"armour.warlord.cloak\": \"0\"", 0 },
        { "\"armour.warlord.cloak\": 0", "\"\": 0", 0 },
        { "\"armour.warlord.cloak\": 0", "\"armour.warlord.cloak\": 0, \"armour.warlord.cloak\": 0", 0 },
        { "\"rewardTiers\": {", "\"rewardTiers\": [", 0 },
        { "\"marks\": 250", "\"marks\": -250", 0 },
        { "\"weapon.all\"", "\"weapon.none\"", 0 },
        { "\"weapon.all\"", "\"\"", 0 },
        { "\"weapon.all\"", "7", 0 },
        // strings: escapes, encodings, control characters
        { "\"Rich Save\"", "\"Rich \\\"Save\\\" \\\\ \\/ \\b\\f\\n\\r\\t\"", 0 },
        { "\"Rich Save\"", "\"\\u00e9\\u65e5\\uD83D\\uDE00\"", 0 },
        { "\"Rich Save\"", "\"\\uD800\"", 0 },
        { "\"Rich Save\"", "\"\\uD800x\"", 0 },
        { "\"Rich Save\"", "\"\\uD800\\u0041\"", 0 },
        { "\"Rich Save\"", "\"\\uDC00\"", 0 },
        { "\"Rich Save\"", "\"\\uDBFF\\uDFFF\"", 0 },
        { "\"Rich Save\"", "\"\\u12G4\"", 0 },
        { "\"Rich Save\"", "\"\\u12\"", 0 },
        { "\"Rich Save\"", "\"\\x41\"", 0 },
        { "\"Rich Save\"", "\"\\'\"", 0 },
        { "\"Rich Save\"", "\"a\x01" "b\"", 0 },
        { "\"Rich Save\"", "\"a\x1f" "b\"", 0 },
        { "\"Rich Save\"", "\"a\x7f" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xff" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xc0\x80" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xc1\xbf" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xc3\xa9" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xe2\x82" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xe0\x80\xaf" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xed\xa0\x80" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xed\x9f\xbf" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xef\xbf\xbf" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xf0\x90\x80\x80" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xf4\x8f\xbf\xbf" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xf4\x90\x80\x80" "b\"", 0 },
        { "\"Rich Save\"", "\"a\xf8\x88\x80\x80\x80" "b\"", 0 },
        { "\"Rich Save\"", "\"a\x80" "b\"", 0 },
        { "\"saveKey\"", "\"save\xffKey\"", 0 },
        { "\"saveKey\"", "\"s\\u00e9veKey\"", 0 },
        { "\"Rich Save\"", "\"Rich Save", 0 },
        { "\"Rich Save\"", "'Rich Save'", 0 },
        { "\"Rich Save\"", "\xff", 0 },
        // syntax
        { "\"version\": 16,", "\"version\" 16,", 0 },
        { "\"version\": 16,", "\"version\": 16", 0 },
        { "\"version\": 16,", "\"version\": 16,,", 0 },
        { "\"version\": 16,", "version: 16,", 0 },
        { "\"version\": 16,", "\"version\":", 0 },
        { "\"version\": 16,", "\"version\": tru,", 0 },
        { "\"version\": 16,", "\"version\": nul,", 0 },
        { "\"version\": 16,", "\"version\": falsy,", 0 },
        { "\"version\": 16,", "\"version\": 16, // c\n", 0 },
        { "\"version\": 16,", "\"version\": 16, /* c */", 0 },
        { "\"wins\": 3,", "\"wins\": 3,}, \"x\": {", 0 },
        { "\"op-2\",", "\"op-2\",,", 0 },
        { "\"op-2\",", "\"op-2\" ", 0 },
        { "\"op-2\",", "\"op-2\":", 0 },
        { "\"op-2\",", "\"op-2\", ]", 0 },
        { "[\n        \"op-2\",", "[,\n        \"op-2\",", 0 },
        { "\"factionUnlocks\": [", "\"factionUnlocks\": [\n    \"weapon.all\"\n  ],\n  \"q\": [", 0 },
        { "\n}\n", "\n}", 0 },
        { "\n}\n", "\n} ", 0 },
        { "\n}\n", "\n}x", 0 },
        { "\n}\n", "\n}{}", 0 },
        { "\n}\n", "\n}\n// end\n", 0 },
        { "\n}\n", "\n}\r\n\t ", 0 },
        { "\n}\n", "\n", 0 },
        { "\n}\n", "\n]", 0 },
        { "\n}\n", ",\n}", 0 },
        { "{\n  \"version\"", "\xef\xbb\xbf{\n  \"version\"", 0 },
        { "{\n  \"version\"", " \t\r\n{\n  \"version\"", 0 },
        { "{\n  \"version\"", "{,\n  \"version\"", 0 },
        { "{\n  \"version\"", "{\n  \"\xff\"", 0 },
        { "{\n  \"version\"", "{\n  \"a\": 1 \"version\"", 0 },
    };
    for (size_t i = 0; i < sizeof mutations / sizeof mutations[0]; ++i)
        Dec(Replace(rich, mutations[i].find, mutations[i].repl, mutations[i].nth), richBase);
    {
        // opponent slot counts and the rewardTiers type, kept valid JSON so the codec's own rule answers (spec review 1.7a)
        const std::string slot = "{\n            \"id\": \"\",\n            \"name\": \"\",\n            \"role\": \"\"\n          }";
        const std::string lastSlot = ",\n          " + slot + "\n        ]";
        Dec(Replace(rich, lastSlot, "\n        ]"), richBase);                                         // 3 slots: valid
        Dec(Replace(rich, ",\n          " + slot + lastSlot, "\n        ]"), richBase);                 // 2 slots
        Dec(Replace(rich, lastSlot, lastSlot.substr(0, lastSlot.size() - 10) + ",\n          " + slot + "\n        ]"), richBase); // 5 slots
        Dec(Replace(Replace(rich, lastSlot, "\n        ]"), "\"enemyCount\": 0", "\"enemyCount\": 4"), richBase); // 4 enemies, 3 slots
        Dec(Replace(Replace(rich, lastSlot, "\n        ]"), "\"enemyCount\": 0", "\"enemyCount\": 3"), richBase); // 3 enemies, 3 slots
        const std::string tiers = "\"rewardTiers\": {\n        \"armour.warlord.cloak\": 0,\n        \"weapon.katana\": 1\n      }";
        Dec(Replace(rich, tiers, "\"rewardTiers\": [\"armour.warlord.cloak\"]"), richBase);
        Dec(Replace(rich, tiers, "\"rewardTiers\": \"weapon.katana\""), richBase);
        Dec(Replace(rich, tiers, "\"rewardTiers\": {}"), richBase);
    }

    // whole documents
    const char* const whole[] = { "", " ", "\n\t", "[]", "{}", "null", "16", "\"x\"", "{\"version\":16}", "{\"version\":10}", "{\"version\":0}",
        "{\"version\":17}", "{\"version\":2147483647}", "{\"version\":-1}", "{\"version\":11}", "{\"version\":12}", "{\"version\":15}", "{\"a\":1}",
        "{\"version\":5,\"version\":16}", "{\"version\":5,\"junk\":1}", "{\"junk\":{\"version\":1},\"version\":3}", "[1,]", "{\"a\":1,}", "{", "[", "{\"a\"", "{\"a\":",
        "{\"a\":1", "[1", "[1 2]", "{\"a\" 1}", "{1:1}", "{\"a\":1 \"b\":2}", "{\"version\":16}x", "\xef\xbb\xbf{}", "{\"version\":10} {}", "{\"version\":1.5}" };
    for (size_t i = 0; i < sizeof whole / sizeof whole[0]; ++i) Dec(whole[i], -1);
    Dec(std::string("{\"version\":16}\0", 15), -1);
    Dec(Replace(rich, "Rich Save", std::string("Rich\0Save", 9)), richBase);

    // legacy and older schemas: every version from 0 to 18, with the key sets ArenaSnapshotJson.cpp:358-363 accepts
    const std::string noPlanned = RemovePlanned(def);
    const std::string withPerf = Replace(noPlanned, "\"generation\": \"g\",", "\"generation\": \"g\",\n  \"performanceProfile\": \"normal\",\n  \"performanceWelcomeSeen\": true,");
    for (int v = 0; v <= 18; ++v) {
        const std::string tag = "\"version\": " + I(v);
        Dec(Replace(def, "\"version\": 16", tag), defaultBase);
        Dec(Replace(noPlanned, "\"version\": 16", tag), defaultBase);
        Dec(Replace(withPerf, "\"version\": 16", tag), defaultBase);
    }
    const std::string& keyed = def;
    const int keyedBase = defaultBase;
    const std::string kNo = RemovePlanned(keyed);
    const std::string kPerf = Replace(kNo, "\"generation\": \"g\",", "\"generation\": \"g\",\n  \"performanceProfile\": \"normal\",\n  \"performanceWelcomeSeen\": true,");
    for (int v = 10; v <= 17; ++v) {
        const std::string tag = "\"version\": " + I(v);
        Dec(Replace(keyed, "\"version\": 16", tag), keyedBase);
        Dec(Replace(kNo, "\"version\": 16", tag), keyedBase);
        Dec(Replace(kPerf, "\"version\": 16", tag), keyedBase);
    }
    const char* const profiles[] = { "\"high\"", "\"potato\"", "\"ultra\"", "\"High\"", "\"\"", "1" };
    for (size_t i = 0; i < sizeof profiles / sizeof profiles[0]; ++i)
        Dec(Replace(Replace(kPerf, "\"version\": 16", "\"version\": 13"), "\"normal\"", profiles[i]), keyedBase);
    Dec(Replace(Replace(kPerf, "\"version\": 16", "\"version\": 12"), "\"performanceWelcomeSeen\": true", "\"performanceWelcomeSeen\": 1"), keyedBase);
    Dec(Replace(Replace(kPerf, "\"version\": 16", "\"version\": 14"), ",\n  \"performanceWelcomeSeen\": true", ""), keyedBase);
    Dec(Replace(Replace(kPerf, "\"version\": 16", "\"version\": 12"), "\"performanceProfile\": \"normal\",\n  ", ""), keyedBase);
    Dec(Replace(Replace(kNo, "\"version\": 16", "\"version\": 11"), "\"uniqueWins\": 0,", ""), keyedBase);
    Dec(Replace(Replace(kNo, "\"version\": 16", "\"version\": 15"), "\"bookieCredit\": 0", "\"bookieCredit\": 7"), keyedBase);
    Dec(Replace(keyed, "\"diagnostic\": {", "\"diagnosticX\": {"), keyedBase);
    Dec(Replace(Replace(keyed, "\"diagnostic\": {", "\"diagnosticX\": {"), "\"saveKey\": \"k\"", "\"saveKey\": \"\""), keyedBase);

    // nesting depth (32 allowed) and size (16 MiB allowed)
    for (int depth = 29; depth <= 33; ++depth) {
        std::string open, close;
        for (int i = 0; i < depth - 2; ++i) { open += "["; close += "]"; }
        Dec(Replace(keyed, "\"factionUnlocks\": []", "\"factionUnlocks\": [" + open + close + "]"), keyedBase);
        Dec(Replace(keyed, "\"factionUnlocks\": []", "\"factionUnlocks\": [" + open), keyedBase);
    }
    {
        std::string deep = "\"x\"";
        for (int i = 0; i < 40; ++i) deep = "{\"a\":" + deep + "}";
        Dec(Replace(keyed, "\"factionUnlocks\": []", "\"factionUnlocks\": [" + deep + "]"), keyedBase);
    }
    {
        const size_t limit = kMaxSnapshotBytes;
        Dec(keyed + std::string(limit - keyed.size(), ' '), keyedBase, limit - keyed.size());
        Dec(keyed + std::string(limit - keyed.size() + 1, ' '), keyedBase, limit - keyed.size() + 1);
    }

    // 3. repository: LoadSnapshot and PublishSnapshot over scripted storage
    struct Named { std::string key, gen; };
    const Named idents[] = { { "k", "g" }, { "k", "h" }, { "other", "g" }, { "other", "h" }, { "k", "" }, { "x", "g" } };
    std::vector<std::string> valid;
    for (size_t i = 0; i < sizeof idents / sizeof idents[0]; ++i) {
        Snapshot s; s.saveKey = idents[i].key.empty() ? "k" : idents[i].key; s.generation = idents[i].gen.empty() ? "g" : idents[i].gen;
        std::string t, e; EncodeSnapshot(s, t, e); valid.push_back(t);
    }
    const std::string legacy = Replace(keyed, "\"version\": 16", "\"version\": 3");
    const std::string newer = Replace(keyed, "\"version\": 16", "\"version\": 17");
    const std::string broken = "{\"version\":16";
    const std::string invalid = Replace(keyed, "\"bookieCredit\": 0", "\"bookieCredit\": -1");
    std::vector<std::string> texts = valid; texts.push_back(legacy); texts.push_back(newer); texts.push_back(broken); texts.push_back(invalid);
    const char* const paths[] = { "main", "b1", "b2", "b3" };
    for (int n = 0; n < 400; ++n) {
        Fake f;
        const bool hand = false; (void)hand;
        for (int p = 0; p < 4; ++p) {
            const unsigned kind = Rn(10);
            if (kind == 0) continue; // absent -> NotFound
            FakeFile file; file.code = kind == 1 ? ReadFailed : kind == 2 ? NotFound : ReadOk; file.setError = Rp(50); file.error = Rp(50) ? "" : "err:" + std::string(paths[p]);
            file.text = file.code == ReadOk ? texts[Rn(static_cast<unsigned>(texts.size()))] : std::string();
            if (file.code == ReadOk && Rp(40)) file.text = valid[Rn(static_cast<unsigned>(valid.size()))];
            f.files[paths[p]] = file;
        }
        f.listOk = !Rp(10); f.listSetError = Rp(50); f.listError = Rp(50) ? "" : "list failed";
        const unsigned nb = Rn(4);
        for (unsigned i = 0; i < nb; ++i) f.list.push_back(paths[1 + Rn(3)]);
        if (Rp(10)) f.list.push_back("missing");
        f.preserveOk = !Rp(15); f.preserveSetError = Rp(50); f.preserveError = Rp(50) ? "" : "preserve failed";
        f.replaceOk = !Rp(15); f.replaceSetError = Rp(50); f.replaceError = Rp(50) ? "" : "replace failed";
        const std::string in = FakeIn(f, keyedBase);
        if (n % 2 == 0) {
            const Named& id = idents[Rn(sizeof idents / sizeof idents[0])];
            const LoadResult r = LoadSnapshot(f, "main", id.key, id.gen);
            Line("{\"k\":\"load\"," + in + ",\"key\":" + Q(id.key) + ",\"gen\":" + Q(id.gen) + ",\"code\":" + I(r.code) + ",\"field\":" + Q(r.field) + ",\"msg\":" + Q(r.message) +
                ",\"snap\":" + Dump(r.snapshot) + ",\"log\":" + Strs(f.log) + "}");
        } else {
            Snapshot s; s.saveKey = "k"; s.generation = "g"; s.challengeCredit = static_cast<int>(Rn(100));
            if (Rp(10)) s.version = 15;
            std::string error = Rp(50) ? "" : "stale error";
            const std::string e0 = error;
            const bool ok = PublishSnapshot(f, "main", s, error);
            std::map<std::string, FakeFile>::const_iterator w = f.files.find("main");
            const std::string written = w != f.files.end() && w->second.code == ReadOk ? w->second.text : std::string();
            Line("{\"k\":\"pub\"," + in + ",\"in\":" + Dump(s) + ",\"e0\":" + Q(e0) + ",\"ok\":" + B(ok) + ",\"err\":" + Q(error) + ",\"main\":" + T(written, keyedBase) +
                ",\"log\":" + Strs(f.log) + "}");
        }
    }

    {
        // the recovery branch "copied from save '<key>' at <backup>": no main sidecar, a backup of the same generation from another slot
        Fake f; f.list.push_back("b1"); f.list.push_back("b2");
        FakeFile other = { ReadOk, valid[2], false, "" }; f.files["b1"] = other;      // saveKey "other", generation "g"
        FakeFile wrongGen = { ReadOk, valid[1], false, "" }; f.files["b2"] = wrongGen; // saveKey "k", generation "h"
        const std::string in = FakeIn(f, keyedBase);
        const LoadResult r = LoadSnapshot(f, "main", "k", "g");
        Line("{\"k\":\"load\"," + in + ",\"key\":" + Q("k") + ",\"gen\":" + Q("g") + ",\"code\":" + I(r.code) + ",\"field\":" + Q(r.field) + ",\"msg\":" + Q(r.message) +
            ",\"snap\":" + Dump(r.snapshot) + ",\"log\":" + Strs(f.log) + "}");
    }

    // 4. rapidjson probes
    const char* const rdr[] = { "", " ", "{}", "[]", "[[]]", "{\"a\":{}}", "{\"a\":[1,2,{\"b\":null}]}", "[true,false,null]", "\"s\"", "1", "-0", "1.5e-3",
        "[1,]", "{\"a\":1,}", "{,}", "[,]", "{\"a\"}", "{\"a\":}", "{\"a\":1 1}", "[1 1]", "[1:1]", "{\"a\",1}", "{1}", "{]", "[}", "]", "}", ":", ",",
        "nul", "nulL", "tru", "fals", "x", "-", "-a", "01", "1.", "1.a", "1e", "1e+", "1E-5", "1e309", "-1e309", "1e-400", "123456789012345678901234567890",
        "18446744073709551615", "18446744073709551616", "-9223372036854775808", "-9223372036854775809", "4294967295", "4294967296", "-2147483648", "-2147483649",
        "\"\\u0041\"", "\"\\u00\"", "\"\\uD834\\uDD1E\"", "\"\\uD834\"", "\"\\uD834\\u0041\"", "\"\\uD834x\"", "\"\\uDD1E\"", "\"\\z\"", "\"a", "\"a\x01\"",
        "\"\xc3\xa9\"", "\"\xc3\"", "\"\xff\"", "\"\xe0\x80\x80\"", "\"\xed\xa0\x80\"", "\"\xf4\x90\x80\x80\"", "[1]x", "[1] ", "[1]\n[2]", "\xef\xbb\xbf[]",
        "{\"\\u0000\":\"\\u0000\"}", "[1e308, 1e-308, 0.1, 2.5e+10]", "  [  1 , 2  ]  ", "{\"a\":1,\"a\":2}", "{\"\xff\":1}", "[\"\\/\\b\\f\\n\\r\\t\\\"\\\\\"]" };
    for (size_t i = 0; i < sizeof rdr / sizeof rdr[0]; ++i) Rdr(rdr[i]);
    Rdr(std::string("[1,\0 2]", 7));
    const char* const nums[] = { "0", "-0", "1", "-1", "0.0", "-0.0", "1.0", "1e0", "1E2", "2147483647", "2147483648", "-2147483648", "-2147483649",
        "4294967295", "4294967296", "9223372036854775807", "9223372036854775808", "-9223372036854775808", "-9223372036854775809",
        "18446744073709551615", "18446744073709551616", "18446744073709551617", "123456789012345678901234567890", "1e308", "1.7976931348623157e308",
        "1.7976931348623158e308", "1.7976931348623159e308", "10e308", "1e309", "1e-307", "2.2250738585072011e-308", "2.2250738585072012e-308",
        "2.2250738585072014e-308", "4.9406564584124654e-324", "2.4703282292062328e-324", "2.4703282292062327e-324", "1e-324", "1e-400", "-0.000e-400",
        "0.1", "0.2", "0.3", "1.1", "3.141592653589793", "2.718281828459045", "1.000000000000000111", "1.0000000000000001110223024625156540423631668090820312500001",
        "9007199254740993", "9007199254740993.0", "9007199254740992.5", "123.456e-2", "0.000001", "1e21", "1e22", "1e23", "8.98846567431158e307",
        "4.4501477170144023e-308", "2.4703282292062327208828439643411068618252990130716238221279284125033775363510437593264991818081799618989828234772285886546332835517796989819938739800539093906315035659515570226392290858392449105184435931802849936536152500319370457678249219365623669863658480757001585769269903706311928279558551332927834338409351978015531246597263579574622766465272827220056374006485499977096599470454020828166226237857393450736339007967761930577506740176324673600968951340535537458516661134223766678604162159680461914467291840300530057530849048765391711386591646239524912623653881879636239373280423891018672348497668235089863388587925628302755995657524455507255189313690836254779186948667994968324049705821028513185451396213837722826145437693412532098591327667236328125e-324" };
    for (size_t i = 0; i < sizeof nums / sizeof nums[0]; ++i) Num(nums[i]);
    for (int i = 0; i < 3000; ++i) {
        // significands of 1..40 digits (StrtodDiyFp reads at most 19, rounding on the next), exponents across the whole range
        const unsigned digits = 1 + Rn(40);
        std::string lit = Rp(10) ? "-" : "";
        for (unsigned j = 0; j < digits; ++j) lit += static_cast<char>('0' + (j == 0 ? 1 + Rn(9) : Rn(10)));
        unsigned intDigits = digits;
        if (Rp(60)) { const unsigned at = 1 + Rn(digits); lit.insert(lit.size() - digits + at, at == digits ? ".0" : "."); if (at == digits) lit.erase(lit.size() - 1); intDigits = at; }
        // decimal magnitude kept within 1e-300..1e300: rapidjson 1.1.0 StrtodDiyFp indexes its power table out of bounds for some
        // literals below ~1e-305 (undefined behaviour in the original, see docs/divergences.md), so the random ones stay clear of it
        if (Rp(60)) lit += "e" + I(static_cast<int>(Rn(600)) - 300 - static_cast<int>(intDigits));
        Num(lit);
    }
    // near-halfway literals: the exact midpoint between two neighbouring doubles (an 80-bit long double holds it exactly), printed to
    // 17..61 significant digits. They reach StrtodDiyFp's "not sure" answer and the BigInteger comparison (strtod.h:203-219).
    for (int i = 0; i < 1500; ++i) {
        double x;
        switch (Rn(4)) {
        case 0: x = FromBits(((static_cast<uint64_t>(R()) << 32) | R()) & 0x7fefffffffffffffull); break;
        case 1: x = RU() * 1e6; break;
        case 2: x = std::ldexp(RU() + 1.0, static_cast<int>(Rn(200)) - 100); break;
        default: x = FromBits(((static_cast<uint64_t>(R()) << 32) | R()) % 0x000fffffffffffffull + 1); break; // subnormal
        }
        if (!(x > 0.0) || std::isinf(std::nextafter(x, INFINITY))) continue;
        const long double m = (static_cast<long double>(x) + static_cast<long double>(std::nextafter(x, INFINITY))) / 2;
        char b[128]; std::snprintf(b, sizeof b, "%.*Le", 16 + static_cast<int>(Rn(45)), m);
        Num(b);
    }
    const double dbls[] = { 0.0, -0.0, 1.0, -1.0, 0.1, 0.5, 2.0, 10.0, 100.0, 1e21, 1e22, 1e-6, 1e-7, 123456789012345678901.0, 1234567890123456789012.0,
        5e-324, 1e-323, 2.2250738585072014e-308, DBL_MAX, -DBL_MAX, 1.7976931348623157e307, 3.4028234663852886e38, 1.401298464324817e-45, 24000000.0, 1e300,
        0.3, 1.0 / 3.0, 2.0 / 3.0, 100.5, 1234.5, 99.75, 1e15, 1e16, 1e17, 9007199254740993.0, 4.35, 0.000123, 1e-5, NAN, INFINITY, -INFINITY };
    for (size_t i = 0; i < sizeof dbls / sizeof dbls[0]; ++i) Dbl(dbls[i]);
    for (int i = 0; i < 4000; ++i) {
        const uint64_t bits = (static_cast<uint64_t>(R()) << 32) | R();
        double v = FromBits(bits);
        if (std::isnan(v) || std::isinf(v)) v = FromBits(bits & 0x7fefffffffffffffull);
        if (i % 4 == 1) v = static_cast<double>(static_cast<float>(RU() * 5000.0));
        if (i % 4 == 2) v = RU() * 24000000.0;
        if (i % 4 == 3) v = std::ldexp(RU(), static_cast<int>(Rn(80)) - 60);
        Dbl(v);
    }
    std::printf("]\n");
    std::fprintf(stderr, "gen_snapshot: %d enc, %d dec\n", g_enc, g_dec);
    return 0;
}
