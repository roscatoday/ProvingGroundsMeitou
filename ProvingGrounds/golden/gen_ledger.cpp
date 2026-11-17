// sources:
// (header-only: ArenaLedger.h, RewardTransaction.h and everything ArenaSnapshot.h includes have no .cpp, so nothing is linked)
// ArenaLedger::Ledger and RewardTransaction, as JSON lines in one array; each line has "k" plus its inputs and outputs.
//   "seq"  {i, steps}  a random sequence of calls on two Ledgers (L0, L1), four token slots (slot 3 stays a default Reservation)
//                      and two ReservedAccounting objects bound to scripted Bindings. Each step {op, ...args, r, s}: r = result,
//                      s = the touched ledger after the step [ready, reserved, epoch, sequence, rows, unlocks, saveKey].
//                      Ops: act (Activate a snapshot "src"), blk (Block), cnt (set epoch/sequence near ~0ULL: private, see below),
//                      marks, tier, find (and write "w" through the row when found), spend, refund, btier, rtier, bunl, runl,
//                      res (Reserve into a slot; "tok" = slot after [owner,epoch,sequence]), pend/can/acc (a slot against a ledger),
//                      comp, ready, cap (Capture into a snapshot pre-filled with saveKey "pre"; "out" = it after; then the out
//                      copy is scribbled on, which must not reach the ledger), mutsrc (scribble on the last Activate source (first row marks = -55, unlock "scribble"),
//                      likewise), edit (write through State(): marks / tier tv / unlock add / unlock remove / new row / remove row),
//                      bind (set the ids a Binding matches), rares / racomp / raroll / raacc (the ReservedAccounting calls).
//   "dlv"  {b, r, log} Deliver over a scripted Inventory and Accounting: b = 7 bits (RemovePrevious, DeliverNew, RemoveNew,
//                      RestorePrevious, Complete, Accept, Rollback), plus "def" = Accounting uses the default Accept.
//   "cact" {b, r, log} CompleteAction: b = 4 bits (Complete, Apply, Accept, Rollback), "def" as above.
//   "cncl" {ir, rb, f, r, log}  Cancel(accounting whose Rollback returns rb, inventoryRestored ir, failure f) for every Result f.
// The epoch / sequence counters and the reserved flag are private in the C++; this generator reads and sets them by compiling the
// two headers with `#define class struct` + `#define private public` (no layout or behaviour change). Previous tiers never reach
// INT_MAX (expectedPrevious + 1 is signed overflow there, UB under g++); costs are never added where the C++ would overflow.
#include "ArenaSnapshot.h"
#include <climits>
#include <cstdio>
#include <string>
#include <vector>
// Every header must be included BEFORE the two #defines below (the patched headers' own includes too): a std or project header
// first included under them would compile with `class` -> `struct` and `private` -> `public` and could break or change.
#define class struct
#define private public
#include "ArenaLedger.h"
#include "RewardTransaction.h"
#undef private
#undef class

using namespace ArenaLedger;
using ArenaPersistence::Snapshot;

static unsigned s_seed = 777001u;
static unsigned R() { s_seed = s_seed * 1664525u + 1013904223u; return s_seed >> 8; }
static unsigned Rn(unsigned n) { return R() % n; }
static bool Rp(unsigned pct) { return Rn(100) < pct; }

static bool g_first = true;
static void Line(const std::string& s) { std::printf("%s%s\n", g_first ? "[" : ",", s.c_str()); g_first = false; }

static std::string Q(const std::string& v) {
    std::string o = "\"";
    for (unsigned char c : v) {
        if (c == '"' || c == '\\') { o += '\\'; o += static_cast<char>(c); }
        else if (c < 0x20) { char b[8]; std::snprintf(b, sizeof b, "\\u%04x", c); o += b; }
        else o += static_cast<char>(c);
    }
    return o + "\"";
}
static std::string I(long long v) { char b[32]; std::snprintf(b, sizeof b, "%lld", v); return b; }
static std::string U(unsigned long long v) { char b[32]; std::snprintf(b, sizeof b, "%llu", v); return b; }
static std::string B(bool v) { return v ? "true" : "false"; }

static const char* kIds[] = { "a", "b", "c", "" };
static const char* kNames[] = { "", "Al", "Bo" };
static const char* kRewards[] = { "", "r1", "r2" };
static const char* kUnlocks[] = { "", "u1", "u2" };
static const int kCosts[] = { 0, 1, 2, 5, 10, 100, -1, INT_MIN, INT_MAX, INT_MAX - 1 };
static const int kMarks[] = { 0, 1, 5, 10, 50, 1000, -3, INT_MAX, INT_MAX - 1, INT_MAX - 5 };
static const int kPrev[] = { -1, -1, 0, 1, 2, 3, -2 };
static const int kNext[] = { 0, 1, 2, 3, 4, -1 };
static const unsigned long long kCounters[] = { 0ULL, 5ULL, ~0ULL - 2, ~0ULL - 1, ~0ULL };

static bool g_scn = false;   // scenario sequences: valid ids, small costs, rich rows, so reservations succeed and Cancel / Accept run
static std::string Id() { return g_scn ? kIds[Rn(3)] : Rp(12) ? "" : kIds[Rn(3)]; }
static std::string Name() { return kNames[Rn(3)]; }
static std::string Reward() { return kRewards[Rn(3)]; }
static std::string Unlock() { return kUnlocks[Rn(3)]; }
static int Cost() { return g_scn ? kCosts[Rn(5)] : Rp(70) ? kCosts[Rn(6)] : kCosts[Rn(10)]; }

static std::string Rows(const LeaderboardData::Collections& f) {
    std::string o = "[";
    for (size_t i = 0; i < f.progression.size(); ++i) {
        const LeaderboardData::Progression& p = f.progression[i];
        o += std::string(i ? "," : "") + "[" + Q(p.id) + "," + Q(p.name) + "," + I(p.marks) + ",[";
        for (size_t t = 0; t < p.rewardTiers.size(); ++t)
            o += std::string(t ? "," : "") + "[" + Q(p.rewardTiers[t].id) + "," + I(p.rewardTiers[t].tier) + "]";
        o += "]]";
    }
    return o + "]";
}
static std::string Strs(const std::vector<std::string>& v) {
    std::string o = "[";
    for (size_t i = 0; i < v.size(); ++i) o += std::string(i ? "," : "") + Q(v[i]);
    return o + "]";
}
static std::string SnapIn(const Snapshot& s) {
    return "{\"rows\":" + Rows(s.fighters) + ",\"un\":" + Strs(s.fighters.factionUnlocks) + ",\"key\":" + Q(s.saveKey) + "}";
}
static std::string State(Ledger& l) {
    return "[" + B(l.ready) + "," + B(l.reserved) + "," + U(l.epoch) + "," + U(l.sequence) + "," + Rows(l.snapshot.fighters) + "," +
        Strs(l.snapshot.fighters.factionUnlocks) + "," + Q(l.snapshot.saveKey) + "]";
}

static Ledger g_ledger[2];
static Snapshot g_src[2];
static Reservation g_tok[4];
static unsigned g_lastSlot = 0, g_lastRa = 0;

struct ScriptBinding : RewardTransaction::Binding {
    std::vector<std::string> ids;
    bool Matches(const std::string& id) const { for (size_t i = 0; i < ids.size(); ++i) if (ids[i] == id) return true; return false; }
};
static ScriptBinding g_bind[2];
static RewardTransaction::ReservedAccounting* g_ra[2];

static Snapshot RandomSnapshot() {
    Snapshot s;
    s.saveKey = Rp(50) ? "k" : "k2";
    const unsigned rows = Rn(5) == 0 ? 0 : 1 + Rn(3);
    for (unsigned i = 0; i < rows; ++i) {
        LeaderboardData::Progression p;
        p.id = kIds[Rn(3)];                 // duplicates possible: Find takes the first
        p.name = Name();
        p.marks = g_scn ? (Rp(85) ? kMarks[4 + Rn(2)] : kMarks[7 + Rn(3)]) : Rp(60) ? kMarks[2 + Rn(4)] : kMarks[Rn(10)];
        const unsigned tiers = Rn(3);
        for (unsigned t = 0; t < tiers; ++t) p.rewardTiers.push_back(RewardProgression::TierEntry(kRewards[1 + Rn(2)], static_cast<int>(Rn(4))));
        s.fighters.progression.push_back(p);
    }
    const unsigned un = Rn(3);
    for (unsigned i = 0; i < un; ++i) s.fighters.factionUnlocks.push_back(kUnlocks[1 + Rn(2)]);
    return s;
}

static Purchase RandomPurchase(std::string& in) {
    Purchase p(Cost());
    if (g_scn) { if (Rp(55)) { p.reward = kRewards[1 + Rn(2)]; p.previousTier = static_cast<int>(Rn(4)) - 1; p.nextTier = p.previousTier + 1; } }
    else if (Rp(50)) { p.reward = Reward(); p.previousTier = kPrev[Rn(7)]; p.nextTier = Rp(60) ? p.previousTier + 1 : kNext[Rn(6)]; }
    if (Rp(45)) p.unlock = Unlock();
    p.requireNewUnlock = Rp(25);
    in = "\"p\":[" + I(p.cost) + "," + I(p.previousTier) + "," + I(p.nextTier) + "," + Q(p.reward) + "," + Q(p.unlock) + "," + B(p.requireNewUnlock) + "]";
    return p;
}

static std::string Step(int forced = -1) {
    const unsigned li = Rp(g_scn ? 92 : 80) ? 0 : 1;
    Ledger& L = g_ledger[li];
    const std::string l = "\"l\":" + I(li);
    std::string o;
    std::string r;
    const unsigned op = forced >= 0 ? static_cast<unsigned>(forced) : Rn(100);
    if (op < 8) {
        g_src[li] = RandomSnapshot();
        L.Activate(g_src[li]);
        o = "\"op\":\"act\"," + l + ",\"src\":" + SnapIn(g_src[li]); r = "null";
    } else if (op < 10) {
        L.Block(); o = "\"op\":\"blk\"," + l; r = "null";
    } else if (op < 12) {
        L.epoch = kCounters[Rn(5)]; L.sequence = kCounters[Rn(5)];
        o = "\"op\":\"cnt\"," + l + ",\"e\":" + U(L.epoch) + ",\"q\":" + U(L.sequence); r = "null";
    } else if (op < 15) {
        const std::string id = Id(); o = "\"op\":\"marks\"," + l + ",\"id\":" + Q(id); r = I(L.Marks(id));
    } else if (op < 18) {
        const std::string id = Id(), rw = Reward(); o = "\"op\":\"tier\"," + l + ",\"id\":" + Q(id) + ",\"rw\":" + Q(rw); r = I(L.Tier(id, rw));
    } else if (op < 21) {
        const std::string id = Id(); const int w = kMarks[Rn(10)];
        LeaderboardData::Progression* row = L.Find(id);
        if (row) row->marks = w;
        o = "\"op\":\"find\"," + l + ",\"id\":" + Q(id) + ",\"w\":" + I(w); r = B(row != 0);
    } else if (op < 28) {
        const std::string id = Id(), nm = Name(); const int c = Cost();
        o = "\"op\":\"spend\"," + l + ",\"id\":" + Q(id) + ",\"nm\":" + Q(nm) + ",\"c\":" + I(c); r = B(L.Spend(id, nm, c));
    } else if (op < 33) {
        const std::string id = Id(), nm = Name(); const int c = Cost();
        o = "\"op\":\"refund\"," + l + ",\"id\":" + Q(id) + ",\"nm\":" + Q(nm) + ",\"c\":" + I(c); r = B(L.Refund(id, nm, c));
    } else if (op < 39) {
        const std::string id = Id(), nm = Name(), rw = Reward(); const int c = Cost(); const int pv = kPrev[Rn(7)];
        const int nx = Rp(60) ? pv + 1 : kNext[Rn(6)];
        o = "\"op\":\"btier\"," + l + ",\"id\":" + Q(id) + ",\"nm\":" + Q(nm) + ",\"c\":" + I(c) + ",\"rw\":" + Q(rw) + ",\"pv\":" + I(pv) + ",\"nx\":" + I(nx);
        r = B(L.BeginTier(id, nm, c, rw, pv, nx));
    } else if (op < 43) {
        const std::string id = Id(), rw = Reward(); const int c = Cost(); const int pv = kPrev[Rn(7)];
        o = "\"op\":\"rtier\"," + l + ",\"id\":" + Q(id) + ",\"c\":" + I(c) + ",\"rw\":" + Q(rw) + ",\"pv\":" + I(pv);
        r = B(L.RollbackTier(id, c, rw, pv));
    } else if (op < 47) {
        const std::string id = Id(), nm = Name(), un = Unlock(); const int c = Cost();
        o = "\"op\":\"bunl\"," + l + ",\"id\":" + Q(id) + ",\"nm\":" + Q(nm) + ",\"c\":" + I(c) + ",\"un\":" + Q(un); r = B(L.BeginUnlock(id, nm, c, un));
    } else if (op < 51) {
        const std::string id = Id(), nm = Name(), un = Unlock(); const int c = Cost();
        o = "\"op\":\"runl\"," + l + ",\"id\":" + Q(id) + ",\"nm\":" + Q(nm) + ",\"c\":" + I(c) + ",\"un\":" + Q(un); r = B(L.RollbackUnlock(id, nm, c, un));
    } else if (op < 61) {
        const unsigned slot = Rn(3); g_lastSlot = slot; const std::string id = Id(), nm = Name(); std::string pin;
        const Purchase p = RandomPurchase(pin);
        const bool ok = L.Reserve(id, nm, p, g_tok[slot]);
        const Reservation& t = g_tok[slot];
        const int owner = t.owner == &g_ledger[0] ? 0 : t.owner == &g_ledger[1] ? 1 : -1;
        o = "\"op\":\"res\"," + l + ",\"slot\":" + I(slot) + ",\"id\":" + Q(id) + ",\"nm\":" + Q(nm) + "," + pin +
            ",\"tok\":[" + I(owner) + "," + U(t.epoch) + "," + U(t.sequence) + "]";
        r = B(ok);
    } else if (op < 75) {
        const unsigned slot = g_scn || Rp(70) ? g_lastSlot : Rn(4); const unsigned which = Rn(3);
        const char* names[] = { "pend", "can", "acc" };
        const bool v = which == 0 ? L.Pending(g_tok[slot]) : which == 1 ? L.Cancel(g_tok[slot]) : L.Accept(g_tok[slot]);
        o = std::string("\"op\":\"") + names[which] + "\"," + l + ",\"slot\":" + I(slot); r = B(v);
    } else if (op < 77) {
        o = "\"op\":\"comp\"," + l; r = B(L.Complete());
    } else if (op < 78) {
        o = "\"op\":\"ready\"," + l; r = B(L.Ready());
    } else if (op < 82) {
        Snapshot outS; outS.saveKey = "pre";
        const bool ok = L.Capture(outS);
        o = "\"op\":\"cap\"," + l + ",\"out\":" + SnapIn(outS); r = B(ok);
        if (!outS.fighters.progression.empty()) outS.fighters.progression[0].marks = -77;
        outS.fighters.factionUnlocks.push_back("scribble");
    } else if (op < 84) {
        if (!g_src[li].fighters.progression.empty()) g_src[li].fighters.progression[0].marks = -55;
        g_src[li].fighters.factionUnlocks.push_back("scribble");
        o = "\"op\":\"mutsrc\"," + l; r = "null";
    } else if (op < 89) {
        Snapshot& st = L.State();
        const unsigned kind = Rn(6);
        const std::string id = Id(), rw = kRewards[1 + Rn(2)], un = kUnlocks[1 + Rn(2)];
        const int v = kMarks[Rn(6)];
        const int tv = static_cast<int>(Rn(5)) - 1;
        if (kind == 0) { for (size_t i = 0; i < st.fighters.progression.size(); ++i) if (st.fighters.progression[i].id == id) { st.fighters.progression[i].marks = v; break; } }
        else if (kind == 1) { for (size_t i = 0; i < st.fighters.progression.size(); ++i) if (st.fighters.progression[i].id == id) { RewardProgression::RestoreHighestTier(st.fighters.progression[i].rewardTiers, rw, tv); break; } }
        else if (kind == 2) RewardProgression::AddUnlock(st.fighters.factionUnlocks, un);
        else if (kind == 3) RewardProgression::RemoveUnlock(st.fighters.factionUnlocks, un);
        else if (kind == 4) { LeaderboardData::Progression p; p.id = id; p.marks = v; st.fighters.progression.push_back(p); }
        else { for (size_t i = 0; i < st.fighters.progression.size(); ++i) if (st.fighters.progression[i].id == id) { st.fighters.progression.erase(st.fighters.progression.begin() + i); break; } }
        o = "\"op\":\"edit\"," + l + ",\"kind\":" + I(kind) + ",\"id\":" + Q(id) + ",\"rw\":" + Q(rw) + ",\"un\":" + Q(un) + ",\"v\":" + I(v) + ",\"tv\":" + I(tv);
        r = "null";
    } else if (op < 91) {
        const unsigned ra = Rn(2); g_bind[ra].ids.clear();
        const unsigned n = Rn(3); for (unsigned i = 0; i < n; ++i) g_bind[ra].ids.push_back(kIds[Rn(4)]);
        o = "\"op\":\"bind\"," + l + ",\"ra\":" + I(ra) + ",\"ids\":" + Strs(g_bind[ra].ids); r = "null";
    } else if (op < 95) {
        const unsigned ra = Rn(2); g_lastRa = ra; const std::string id = Id(), nm = Name(); std::string pin;
        const Purchase p = RandomPurchase(pin);
        o = "\"op\":\"rares\"," + l + ",\"ra\":" + I(ra) + ",\"id\":" + Q(id) + ",\"nm\":" + Q(nm) + "," + pin;
        r = B(g_ra[ra]->Reserve(L, id, nm, p));
    } else {
        const unsigned ra = g_scn || Rp(70) ? g_lastRa : Rn(2); const unsigned which = Rn(3);
        const char* names[] = { "racomp", "raroll", "raacc" };
        const bool v = which == 0 ? g_ra[ra]->Complete() : which == 1 ? g_ra[ra]->Rollback() : g_ra[ra]->Accept();
        o = std::string("\"op\":\"") + names[which] + "\"," + l + ",\"ra\":" + I(ra); r = B(v);
    }
    return "{" + o + ",\"r\":" + r + ",\"s\":" + State(L) + "}";
}

// ---- scripted stubs for Deliver / CompleteAction / Cancel ----
static std::string g_log;
struct ScriptAccounting : RewardTransaction::Accounting {
    bool complete, rollback;
    ScriptAccounting(bool c, bool r) : complete(c), rollback(r) {}
    bool Complete() { g_log += "C"; return complete; }
    bool Rollback() { g_log += "R"; return rollback; }
};
struct ScriptAccountingAccept : ScriptAccounting {
    bool accept;
    ScriptAccountingAccept(bool c, bool a, bool r) : ScriptAccounting(c, r), accept(a) {}
    bool Accept() { g_log += "A"; return accept; }
};
struct ScriptInventory : RewardTransaction::Inventory {
    bool rp, dn, rn, rs;
    ScriptInventory(bool a, bool b, bool c, bool d) : rp(a), dn(b), rn(c), rs(d) {}
    bool RemovePrevious() { g_log += "p"; return rp; }
    bool DeliverNew() { g_log += "d"; return dn; }
    bool RemoveNew() { g_log += "n"; return rn; }
    bool RestorePrevious() { g_log += "s"; return rs; }
    void DestroyNew() { g_log += "x"; }
    void DestroyPrevious() { g_log += "y"; }
};
struct ScriptAction : RewardTransaction::Action {
    bool apply;
    explicit ScriptAction(bool a) : apply(a) {}
    bool Apply() { g_log += "a"; return apply; }
};

int main() {
    g_ra[0] = new RewardTransaction::ReservedAccounting(g_bind[0]);
    g_ra[1] = new RewardTransaction::ReservedAccounting(g_bind[1]);
    int seqs = 0, dlv = 0, cact = 0, cncl = 0;
    for (int i = 0; i < 400; ++i) {
        // a fresh world per sequence: ledgers, sources, tokens and accountings start from their defaults
        g_ledger[0] = Ledger(); g_ledger[1] = Ledger(); g_src[0] = Snapshot(); g_src[1] = Snapshot();
        for (int t = 0; t < 4; ++t) g_tok[t] = Reservation();
        delete g_ra[0]; delete g_ra[1];
        g_bind[0].ids.assign(1, "a"); g_bind[0].ids.push_back("b"); g_bind[1].ids.assign(1, "b");
        g_ra[0] = new RewardTransaction::ReservedAccounting(g_bind[0]);
        g_ra[1] = new RewardTransaction::ReservedAccounting(g_bind[1]);
        std::string steps;
        if (Rp(80)) { g_src[0] = RandomSnapshot(); g_ledger[0].Activate(g_src[0]);
            steps = "{\"op\":\"act\",\"l\":0,\"src\":" + SnapIn(g_src[0]) + ",\"r\":null,\"s\":" + State(g_ledger[0]) + "}"; }
        const int n = 6 + static_cast<int>(Rn(18));
        for (int k = 0; k < n; ++k) steps += std::string(steps.empty() ? "" : ",") + Step();
        Line("{\"k\":\"seq\",\"i\":" + I(i) + ",\"steps\":[" + steps + "]}");
        ++seqs;
    }
    // scenarios: activate, reserve (ledger or ReservedAccounting), maybe interfere, then cancel / accept / pending / accounting calls
    static const int kInterfere[] = { 84, 84, 84, 10, 8, 0, 18, 28, 21, 78, 89 };
    for (int i = 0; i < 400; ++i) {
        g_scn = true;
        g_ledger[0] = Ledger(); g_ledger[1] = Ledger(); g_src[0] = Snapshot(); g_src[1] = Snapshot();
        for (int t = 0; t < 4; ++t) g_tok[t] = Reservation();
        delete g_ra[0]; delete g_ra[1];
        g_bind[0].ids.assign(1, "a"); g_bind[0].ids.push_back("b"); g_bind[1].ids.assign(1, "b");
        g_ra[0] = new RewardTransaction::ReservedAccounting(g_bind[0]);
        g_ra[1] = new RewardTransaction::ReservedAccounting(g_bind[1]);
        std::string steps;
        g_src[0] = RandomSnapshot(); g_ledger[0].Activate(g_src[0]);
        steps = "{\"op\":\"act\",\"l\":0,\"src\":" + SnapIn(g_src[0]) + ",\"r\":null,\"s\":" + State(g_ledger[0]) + "}";
        const int rounds = 1 + static_cast<int>(Rn(3));
        for (int k = 0; k < rounds; ++k) {
            const bool viaRa = Rp(40);
            steps += "," + Step(viaRa ? 91 : 51);
            const int interfere = Rp(55) ? 0 : 1 + static_cast<int>(Rn(2));
            for (int j = 0; j < interfere; ++j) steps += "," + Step(kInterfere[Rn(11)]);
            const int finish = 1 + static_cast<int>(Rn(3));
            for (int j = 0; j < finish; ++j) steps += "," + Step(Rp(viaRa ? 15 : 85) ? 61 + static_cast<int>(Rn(14)) : 95);
        }
        Line("{\"k\":\"seq\",\"i\":" + I(400 + i) + ",\"steps\":[" + steps + "]}");
        ++seqs;
    }
    g_scn = false;
    for (int def = 0; def < 2; ++def)
        for (int b = 0; b < 128; ++b) {
            g_log.clear();
            ScriptInventory inv(b & 1, b & 2, b & 4, b & 8);
            RewardTransaction::Result r;
            if (def) { ScriptAccounting acc(b & 16, b & 64); r = RewardTransaction::Deliver(inv, acc); }
            else { ScriptAccountingAccept acc(b & 16, b & 32, b & 64); r = RewardTransaction::Deliver(inv, acc); }
            Line("{\"k\":\"dlv\",\"def\":" + B(def) + ",\"b\":" + I(b) + ",\"r\":" + I(r) + ",\"log\":" + Q(g_log) + "}");
            ++dlv;
        }
    for (int def = 0; def < 2; ++def)
        for (int b = 0; b < 16; ++b) {
            g_log.clear();
            ScriptAction act(b & 2);
            RewardTransaction::Result r;
            if (def) { ScriptAccounting acc(b & 1, b & 8); r = RewardTransaction::CompleteAction(act, acc); }
            else { ScriptAccountingAccept acc(b & 1, b & 4, b & 8); r = RewardTransaction::CompleteAction(act, acc); }
            Line("{\"k\":\"cact\",\"def\":" + B(def) + ",\"b\":" + I(b) + ",\"r\":" + I(r) + ",\"log\":" + Q(g_log) + "}");
            ++cact;
        }
    for (int ir = 0; ir < 2; ++ir)
        for (int rb = 0; rb < 2; ++rb)
            for (int f = 0; f <= RewardTransaction::RollbackFailed; ++f) {
                g_log.clear();
                ScriptAccounting acc(true, rb);
                const RewardTransaction::Result r = RewardTransaction::Cancel(acc, ir, static_cast<RewardTransaction::Result>(f));
                Line("{\"k\":\"cncl\",\"ir\":" + B(ir) + ",\"rb\":" + B(rb) + ",\"f\":" + I(f) + ",\"r\":" + I(r) + ",\"log\":" + Q(g_log) + "}");
                ++cncl;
            }
    std::printf("]\n");
    std::fprintf(stderr, "ledger: %d seq, %d dlv, %d cact, %d cncl\n", seqs, dlv, cact, cncl);
    return 0;
}
