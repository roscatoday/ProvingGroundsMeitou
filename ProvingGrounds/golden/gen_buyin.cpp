// sources: pgconfig_stub.cpp
// TownChallengeBuyInPolicy.h (header-only; the PGConfig stub supplies ChallengeBase) as JSON lines in one array. Each line has "k" plus
// its inputs and outputs. Doubles are printed as their IEEE bits (decimal uint64, keys ending in "b"); ints as %d.
// Shared table, printed first: cb  id, d0, d1, d2 = the challenge base per division set through PGConfigStub_SetChallengeBase before the
//   cases that name it (ci = index). Row 0 is the shipped 500/1000/2500; 1 changes every division; 2 is inside 200-2500; 3 and 4 are
//   outside it (prices Valid() refuses); 5 is zero.
// Kinds:
//   price  Price(division, own, enemy) under ci -> buyIn, payout, valid
//   valid  Quote{buyIn, payout}.Valid()
//   ca     CanAccept(account [credit, escrow], ticket [quote buyIn, payout, pending], quote [buyIn, payout], balance)
//   acc    Accept(...) -> returned bool, account and ticket after
//   settle Settle(account, ticket, outcome) -> credit, account and ticket after
//   save   SaveCredit(account)
// Overflow bound: the largest base used is 9000; buyIn <= 2 * base and odds <= 5.0, so every intermediate stays far inside int.
#include "TownChallengeBuyInPolicy.h"
#include "pgconfig_stub.h"
#include <cstdio>
#include <cstring>
#include <cmath>
#include <vector>
#include <limits>

static unsigned s = 717171u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }
static unsigned long long Bits(double v) { unsigned long long b; std::memcpy(&b, &v, 8); return b; }

static const int CB[][3] = { {500, 1000, 2500}, {300, 800, 2000}, {200, 200, 2500}, {50, 4000, 9000}, {10, 150, 20000 / 3}, {0, 0, 0} };
static const int NCB = sizeof CB / sizeof CB[0];
static void SetCb(int i) { for (int d = 0; d < 3; ++d) PGConfigStub_SetChallengeBase(d, CB[i][d]); }

static void Pq(const TownChallengeBuyInPolicy::Quote& q) { std::printf("[%d,%d]", q.buyIn, q.payout); }

int main() {
    const double inf = std::numeric_limits<double>::infinity();
    const double nan = std::numeric_limits<double>::quiet_NaN();
    std::printf("[\n");
    std::printf("%s{\"k\":\"cb\",\"v\":[", Sep());
    for (int i = 0; i < NCB; ++i) std::printf("%s[%d,%d,%d,%d]", i ? "," : "", i, CB[i][0], CB[i][1], CB[i][2]);
    std::printf("]}\n");

    std::vector<std::pair<double, double>> pairs;
    const double odd[] = { 0.0, -0.0, -1.0, -100.0, nan, inf, -inf, 1e-300, 1e300, 1e-12, 0.5, 1.0, 100.0, 5000.0 };
    for (double a : odd) for (double b : odd) pairs.push_back({ a, b });
    // ratio sweep 0.05 .. 8 step 0.025 (crosses every clamp edge for each division's target .70/1.00/1.30 and the odds band)
    for (int i = 1; i <= 320; ++i) { const double r = i * 0.025; pairs.push_back({ 100.0, 100.0 * r }); }
    const double edges[] = { 0.35, 0.7, 1.4, 1.0, 2.0, 2.6, 1.3, 0.65, 1.15, 3.9, 4.0, 4.1 };
    for (double r : edges) { pairs.push_back({ 100.0, 100.0 * r }); pairs.push_back({ 37.0, 37.0 * r }); }
    for (int i = 0; i < 300; ++i) { const double x = 1.0 + (R() % 30000) / 100.0; const double y = 1.0 + (R() % 30000) / 100.0; pairs.push_back({ x, y }); }

    for (int ci = 0; ci < NCB; ++ci) { SetCb(ci);
        for (int div = -2; div <= 4; ++div)
            for (size_t i = 0; i < pairs.size(); i += (ci == 0 ? 1 : 3)) {
                const TownChallengeBuyInPolicy::Quote q = TownChallengeBuyInPolicy::Price(div, pairs[i].first, pairs[i].second);
                std::printf("%s{\"k\":\"price\",\"ci\":%d,\"d\":%d,\"ob\":%llu,\"eb\":%llu,\"q\":", Sep(), ci, div, Bits(pairs[i].first), Bits(pairs[i].second));
                Pq(q); std::printf(",\"v\":%d}\n", q.Valid() ? 1 : 0);
            } }
    SetCb(0);

    // valid: boundary sweep
    const int bis[] = { -1, 0, 99, 100, 101, 2500, 5000, 5001, 6000 };
    const int pos[] = { -1, 0, 99, 100, 101, 2500, 5000, 5001, 19999, 20000, 20001, 50000 };
    for (int b : bis) for (int p : pos) {
        TownChallengeBuyInPolicy::Quote q; q.buyIn = b; q.payout = p;
        std::printf("%s{\"k\":\"valid\",\"q\":[%d,%d],\"v\":%d}\n", Sep(), b, p, q.Valid() ? 1 : 0);
    }

    // ca / acc / settle
    const int credits[] = { 0, 1, 500 };
    const int escrows[] = { 0, 1, 700 };
    const int balances[] = { -1, 0, 99, 100, 499, 500, 501, 5000, 100000 };
    const int qs[][2] = { {500, 1250}, {100, 100}, {100, 99}, {5000, 20000}, {5000, 20001}, {99, 500}, {0, 0}, {5010, 6000} };
    for (int cr : credits) for (int es : escrows) for (int pend = 0; pend < 2; ++pend) for (const auto& qq : qs) for (int bal : balances) {
        TownChallengeBuyInPolicy::Account a; a.credit = cr; a.escrow = es;
        TownChallengeBuyInPolicy::Ticket t; t.pending = pend != 0; t.quote.buyIn = 7; t.quote.payout = 9;
        TownChallengeBuyInPolicy::Quote q; q.buyIn = qq[0]; q.payout = qq[1];
        std::printf("%s{\"k\":\"ca\",\"a\":[%d,%d],\"t\":[%d,%d,%d],\"q\":[%d,%d],\"bal\":%d,\"r\":%d}\n", Sep(), cr, es, t.quote.buyIn, t.quote.payout, pend,
            qq[0], qq[1], bal, TownChallengeBuyInPolicy::CanAccept(a, t, q, bal) ? 1 : 0);
        const bool ok = TownChallengeBuyInPolicy::Accept(a, t, q, bal);
        std::printf("%s{\"k\":\"acc\",\"a\":[%d,%d],\"t\":[%d,%d,%d],\"q\":[%d,%d],\"bal\":%d,\"r\":%d,\"a2\":[%d,%d],\"t2\":[%d,%d,%d]}\n", Sep(), cr, es, 7, 9, pend,
            qq[0], qq[1], bal, ok ? 1 : 0, a.credit, a.escrow, t.quote.buyIn, t.quote.payout, t.pending ? 1 : 0);
    }
    const int outcomes[] = { -3, -1, 0, 1, 2, 9 };
    for (int cr : credits) for (int es : escrows) for (int pend = 0; pend < 2; ++pend) for (const auto& qq : qs) for (int o : outcomes) {
        TownChallengeBuyInPolicy::Account a; a.credit = cr; a.escrow = es;
        TownChallengeBuyInPolicy::Ticket t; t.pending = pend != 0; t.quote.buyIn = qq[0]; t.quote.payout = qq[1];
        const int c = TownChallengeBuyInPolicy::Settle(a, t, o);
        std::printf("%s{\"k\":\"settle\",\"a\":[%d,%d],\"t\":[%d,%d,%d],\"o\":%d,\"r\":%d,\"a2\":[%d,%d],\"t2\":[%d,%d,%d]}\n", Sep(), cr, es, qq[0], qq[1], pend, o, c,
            a.credit, a.escrow, t.quote.buyIn, t.quote.payout, t.pending ? 1 : 0);
    }
    for (int cr : credits) for (int es : escrows) {
        TownChallengeBuyInPolicy::Account a; a.credit = cr; a.escrow = es;
        std::printf("%s{\"k\":\"save\",\"a\":[%d,%d],\"r\":%d}\n", Sep(), cr, es, TownChallengeBuyInPolicy::SaveCredit(a));
    }
    std::printf("]\n");
    return 0;
}
