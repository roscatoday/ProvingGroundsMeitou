// sources: BalanceTuning.cpp pgconfig_stub.cpp
// TownBettingPolicy.h (header-only; links the real BalanceTuning.cpp for the pricing levers and the PGConfig stub for the bookie
// range) as JSON lines in one array. Each line has "k" plus its inputs and outputs. Doubles are printed as their IEEE bits (decimal
// uint64, keys ending in "b") so NaN, -0 and infinities round-trip exactly; ints as %d.
// Shared tables, printed first and referenced by index:
//   bk    id, mn, mx = a bookie range set through PGConfigStub_Bookie() before the cases that name it (shipped, narrow, unaligned...)
//   tune  id, ex (bits), kn (bits) = BalanceTuning pricingExponent / pricingKnee set before the cases that name it
// Kinds (bi / ti = index of the bk / tune row in force):
//   res   Resolve(ticket [side, stake, payout, pending], winner) -> outcome, winningSide, selectedSide, stake, returned, net
//   adj   AdjustStake(current, delta) under bi
//   wp    WinProbability(own, other) and PricedProbability(own, other) under ti (bits)
//   rc    ReturnCats(stake, own, other) under bi and ti
//   ta    TeamAbility(members[], count) -> bits (n = array length; null = NULL pointer)
//   ca    CanAccept(ticket, npcOnly, beforeCombat, side, stake, balance) under bi
//   st    Settle(ticket, winner) -> return, pending after
// Overflow bound: stake <= 10000 (the largest bookie maximum used) times odds <= 5.0 is 50000, far inside int.
#include "TownBettingPolicy.h"
#include "pgconfig_stub.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cstdint>
#include <cmath>
#include <vector>
#include <limits>

static unsigned s = 424242u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }
static unsigned long long Bits(double v) { unsigned long long b; std::memcpy(&b, &v, 8); return b; }

struct Bk { int mn, mx; };
static const Bk BKS[] = { {10, 10000}, {50, 2000}, {20, 500}, {30, 990}, {100, 100}, {15, 95}, {0, 10000} };
static const int NBK = sizeof BKS / sizeof BKS[0];
struct Tune { double ex, kn; };
static const Tune TUNES[] = { {6.65, 1.10}, {2.0, 0.0}, {2.0, 1.3}, {3.0, 0.0}, {4.5, 1.5}, {6.65, 0.5}, {2.0, -1.0}, {1.0, 2.0} };
static const int NTUNE = sizeof TUNES / sizeof TUNES[0];

static void SetBk(int i) { PGConfigStub_Bookie().minimum = BKS[i].mn; PGConfigStub_Bookie().maximum = BKS[i].mx; }
static void SetTune(int i) { BalanceTuning::Get().pricingExponent = TUNES[i].ex; BalanceTuning::Get().pricingKnee = TUNES[i].kn; }

static std::vector<double> Abilities() {
    std::vector<double> a;
    const double inf = std::numeric_limits<double>::infinity();
    const double nan = std::numeric_limits<double>::quiet_NaN();
    const double base[] = { 0.0, -0.0, -1.0, -100.0, nan, inf, -inf, 1e-300, 1e300, 1e-12, 0.5, 1.0, 37.5, 100.0, 250.0, 1000.0 };
    for (double v : base) a.push_back(v);
    return a;
}

// pairs: ratio sweep in and outside 1.0..3.0, both orders, equal scores, degenerate values, random
static std::vector<std::pair<double, double>> Pairs() {
    std::vector<std::pair<double, double>> p;
    const std::vector<double> ab = Abilities();
    for (size_t i = 0; i < ab.size(); ++i) for (size_t j = 0; j < ab.size(); ++j) p.push_back({ ab[i], ab[j] });
    for (int i = 0; i <= 40; ++i) { const double r = 1.0 + i * 0.05; p.push_back({ 100.0, 100.0 * r }); p.push_back({ 100.0 * r, 100.0 }); }
    const double outside[] = { 0.01, 0.1, 0.5, 0.9, 0.99, 3.01, 3.5, 4.0, 5.0, 8.0, 10.0, 50.0, 1000.0 };
    for (double r : outside) { p.push_back({ 100.0, 100.0 * r }); p.push_back({ 100.0 * r, 100.0 }); }
    for (int i = 0; i < 300; ++i) { const double x = 1.0 + (R() % 50000) / 100.0; const double y = 1.0 + (R() % 50000) / 100.0; p.push_back({ x, y }); }
    for (int i = 0; i < 100; ++i) { const double x = 50.0 + (R() % 1000) / 10.0; p.push_back({ x, x * (1.0 + (R() % 400) / 1000.0) }); }
    return p;
}

int main() {
    std::printf("[\n");
    std::printf("%s{\"k\":\"bk\",\"v\":[", Sep());
    for (int i = 0; i < NBK; ++i) std::printf("%s[%d,%d,%d]", i ? "," : "", i, BKS[i].mn, BKS[i].mx);
    std::printf("]}\n");
    std::printf("%s{\"k\":\"tune\",\"v\":[", Sep());
    for (int i = 0; i < NTUNE; ++i) std::printf("%s[%d,%llu,%llu]", i ? "," : "", i, Bits(TUNES[i].ex), Bits(TUNES[i].kn));
    std::printf("]}\n");

    // res: every ticket state and winner
    const int sides[] = { -1, 0, 1, 2 };
    const int stakes[] = { 0, 10, 500, 10000 };
    const int payouts[] = { 0, 11, 1250, 50000 };
    const int winners[] = { -5, -1, 0, 1, 2, 7 };
    for (int pend = 0; pend < 2; ++pend) for (int sd : sides) for (int sk : stakes) for (int po : payouts) for (int w : winners) {
        TownBettingPolicy::Ticket t; t.side = sd; t.stake = sk; t.payout = po; t.pending = pend != 0;
        const TownBettingPolicy::Result r = TownBettingPolicy::Resolve(t, w);
        std::printf("%s{\"k\":\"res\",\"t\":[%d,%d,%d,%d],\"w\":%d,\"o\":%d,\"ws\":%d,\"ss\":%d,\"st\":%d,\"rt\":%d,\"n\":%d}\n", Sep(),
            sd, sk, po, pend, w, (int)r.outcome, r.winningSide, r.selectedSide, r.stake, r.returned, r.net);
    }

    // st: Settle
    for (int pend = 0; pend < 2; ++pend) for (int sd : sides) for (int sk : stakes) for (int po : payouts) for (int w : winners) {
        TownBettingPolicy::Ticket t; t.side = sd; t.stake = sk; t.payout = po; t.pending = pend != 0;
        const int ret = TownBettingPolicy::Settle(t, w);
        std::printf("%s{\"k\":\"st\",\"t\":[%d,%d,%d,%d],\"w\":%d,\"r\":%d,\"p\":%d}\n", Sep(), sd, sk, po, pend, w, ret, t.pending ? 1 : 0);
    }

    // adj
    const int currents[] = { -50, 0, 5, 10, 11, 100, 9500, 9999, 10000, 10010, 20000, 15, 95, 500 };
    const int deltas[] = { -20000, -1000, -100, -10, -1, 0, 1, 10, 100, 1000, 20000 };
    for (int bi = 0; bi < NBK; ++bi) { SetBk(bi);
        for (int c : currents) for (int d : deltas)
            std::printf("%s{\"k\":\"adj\",\"bi\":%d,\"c\":%d,\"d\":%d,\"r\":%d}\n", Sep(), bi, c, d, TownBettingPolicy::AdjustStake(c, d)); }

    // wp
    const std::vector<std::pair<double, double>> pairs = Pairs();
    for (int ti = 0; ti < NTUNE; ++ti) { SetTune(ti);
        for (const auto& pr : pairs)
            std::printf("%s{\"k\":\"wp\",\"ti\":%d,\"ob\":%llu,\"tb\":%llu,\"wb\":%llu,\"pb\":%llu}\n", Sep(), ti, Bits(pr.first), Bits(pr.second),
                Bits(TownBettingPolicy::WinProbability(pr.first, pr.second)), Bits(TownBettingPolicy::PricedProbability(pr.first, pr.second))); }

    // rc: every tune x every bookie, over a stake list and a pair subset
    const int rstakes[] = { -10, 0, 5, 10, 15, 20, 30, 50, 95, 100, 500, 990, 1000, 2000, 9990, 10000, 10010 };
    for (int bi = 0; bi < NBK; ++bi) { SetBk(bi);
        for (int ti = 0; ti < NTUNE; ++ti) { SetTune(ti);
            for (size_t i = 0; i < pairs.size(); i += (ti == 0 && bi == 0) ? 3 : 11)
                for (int sk : rstakes) {
                    if ((ti != 0 || bi != 0) && ((i / 11 + sk) % 3 != 0)) continue;
                    std::printf("%s{\"k\":\"rc\",\"bi\":%d,\"ti\":%d,\"sk\":%d,\"ob\":%llu,\"tb\":%llu,\"r\":%d}\n", Sep(), bi, ti, sk,
                        Bits(pairs[i].first), Bits(pairs[i].second), TownBettingPolicy::ReturnCats(sk, pairs[i].first, pairs[i].second));
                } } }
    SetTune(0); SetBk(0);

    // ta: team sizes 0..max (and beyond), random abilities
    for (int n = 0; n <= 6; ++n) for (int count = -1; count <= 6; ++count) for (int rep = 0; rep < 12; ++rep) {
        std::vector<double> m;
        for (int i = 0; i < n; ++i) {
            const unsigned v = R() % 12;
            m.push_back(v == 0 ? 0.0 : v == 1 ? -5.0 : v == 2 ? std::numeric_limits<double>::quiet_NaN() : v == 3 ? std::numeric_limits<double>::infinity()
                : v == 4 ? 1e300 : 1.0 + (R() % 20000) / 100.0);
        }
        if (count > n) continue; // the C++ reads past the end otherwise
        const double r = TownBettingPolicy::TeamAbility(m.empty() ? nullptr : m.data(), count);
        std::printf("%s{\"k\":\"ta\",\"n\":%d,\"count\":%d,\"m\":[", Sep(), m.empty() ? -1 : n, count);
        for (int i = 0; i < n; ++i) std::printf("%s%llu", i ? "," : "", Bits(m[i]));
        std::printf("],\"r\":%llu}\n", Bits(r));
    }
    { // null pointer with a positive count
        const double r = TownBettingPolicy::TeamAbility(nullptr, 3);
        std::printf("%s{\"k\":\"ta\",\"n\":-1,\"count\":3,\"m\":[],\"r\":%llu}\n", Sep(), Bits(r));
    }

    // ca
    const int csides[] = { -1, 0, 1, 2 };
    const int cstakes[] = { -10, 0, 5, 10, 15, 20, 30, 50, 100, 990, 2000, 9990, 10000, 10010 };
    const int cbal[] = { 0, 9, 10, 100, 9999, 10000, 20000 };
    for (int bi = 0; bi < NBK; ++bi) { SetBk(bi);
        for (int pend = 0; pend < 2; ++pend) for (int np = 0; np < 2; ++np) for (int bc = 0; bc < 2; ++bc)
            for (int sd : csides) for (int sk : cstakes) for (int bal : cbal) {
                if (bi != 0 && (R() % 5)) continue;
                TownBettingPolicy::Ticket t; t.pending = pend != 0;
                std::printf("%s{\"k\":\"ca\",\"bi\":%d,\"p\":%d,\"np\":%d,\"bc\":%d,\"sd\":%d,\"sk\":%d,\"bal\":%d,\"r\":%d}\n", Sep(), bi, pend, np, bc, sd, sk, bal,
                    TownBettingPolicy::CanAccept(t, np != 0, bc != 0, sd, sk, bal) ? 1 : 0);
            } }
    std::printf("]\n");
    return 0;
}
