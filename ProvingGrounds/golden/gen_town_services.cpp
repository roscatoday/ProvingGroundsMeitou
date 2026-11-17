// sources:
// (header-only: TownSpectatorPolicy.h and TownCorpseCleanupPolicy.h have no .cpp, so nothing is linked)
// The spectator and corpse-cleanup policies, as JSON lines in one array; each line has "k". Boolean inputs are exhaustive.
//   "s.role"    {id, r}              TownSpectatorPolicy::RoleFor(id); id may be null.
//   "s.watch"   {role, p, j, h, r}   CanWatch(role -1..2, player, permanentJob, healthy).
//   "s.reserve" {f, rs, a, t, r}     CanReserve(free, reserved, audience, target) at the 0 / 120 edges.
//   "s.next"    {c, rs, h, cur, r}   NextRole(civilians, residents, handlers, cursor -1..2) over counts -1..2.
//   "s.release" {v, d, p, h, r}      ReleaseFor(valid, dead, player, healthy).
//   "s.arena"   {st, d, o, r}        BelongsToArena(sameTown, distanceSquared, otherArenaDistanceSquared) at the 0 / 250000
//                                    edges (and the next double each side), ties and infinities.
//   "c.elig"    {a, r}  EligibleCorpse(4 bools).       "c.handler" {a, r}  HandlerReady(6 bools).
//   "c.furnace" {a, r}  FurnaceReady(5 bools).         "c.next"    {a, r}  NextAction(4 bools).
//   "c.release" {a, r}  ReleaseFor(8 bools).
//   "c.timed"   {e, l, r, b0, b1}  TimedOut(elapsed, limit), BatchTimedOut(false / true, elapsed, limit).
//   "c.adv"     {e, d, pz, r}      AdvanceElapsed(elapsed, delta, paused) (delta at 0, 1 and next floats each side).
//   "c.select"  {el, rs, n, cur, r}  SelectCorpse(eligible, resolved, count, current); el/rs null = a NULL pointer;
//                                    count never above the array length.
#include <cfloat>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include <string>
#include <vector>
#include "TownSpectatorPolicy.h"
#include "TownCorpseCleanupPolicy.h"

static unsigned s_seed = 1110004u;
static unsigned R() { s_seed = s_seed * 1664525u + 1013904223u; return s_seed >> 8; }
static unsigned Rn(unsigned n) { return R() % n; }

static bool g_first = true;
static void Line(const std::string& s) { std::printf("%s%s\n", g_first ? "[" : ",", s.c_str()); g_first = false; }
static std::string I(long long v) { char b[32]; std::snprintf(b, sizeof b, "%lld", v); return b; }
static std::string F(float v) { char b[48]; std::snprintf(b, sizeof b, "%.9g", static_cast<double>(v)); return b; }
// doubles: %.17g, infinities as the strings "inf" / "-inf" (JSON has no infinity)
static std::string D(double v)
{
    if (std::isinf(v)) return v > 0 ? "\"inf\"" : "\"-inf\"";
    char b[48]; std::snprintf(b, sizeof b, "%.17g", v); return b;
}
static std::string Fx(float v)
{
    if (std::isinf(v)) return v > 0 ? "\"inf\"" : "\"-inf\"";
    return F(v);
}
static std::string B(bool v) { return v ? "1" : "0"; }
static std::string S(const char* s) { return s ? "\"" + std::string(s) + "\"" : "null"; }
static std::string Bits(unsigned bits, int n)
{
    std::string o = "[";
    for (int i = 0; i < n; ++i) o += (i ? "," : "") + B(((bits >> i) & 1u) != 0);
    return o + "]";
}
static bool Bit(unsigned bits, int i) { return ((bits >> i) & 1u) != 0; }

int main()
{
    using namespace TownSpectatorPolicy;
    {
        const char* ids[] = { "103-Proving Grounds.mod", "258-Proving Grounds.mod", "99-Proving Grounds.mod",
            "257-Proving Grounds.mod", "108-Proving Grounds.mod", "99-Proving Grounds.mo", "103-proving grounds.mod", "", NULL };
        for (size_t i = 0; i < sizeof ids / sizeof ids[0]; ++i)
            Line("{\"k\":\"s.role\",\"id\":" + S(ids[i]) + ",\"r\":" + I(RoleFor(ids[i])) + "}");
    }
    for (int role = -1; role <= 2; ++role)
        for (unsigned b = 0; b < 8u; ++b)
            Line("{\"k\":\"s.watch\",\"role\":" + I(role) + ",\"p\":" + B(Bit(b, 0)) + ",\"j\":" + B(Bit(b, 1)) + ",\"h\":" +
                B(Bit(b, 2)) + ",\"r\":" + B(CanWatch(static_cast<AudienceRole>(role), Bit(b, 0), Bit(b, 1), Bit(b, 2))) + "}");
    {
        const int as[] = { -1, 0, 1, 59, 60, 119, 120, 121 };
        const int ts[] = { -1, 0, 1, 60, 119, 120, 121 };
        for (unsigned b = 0; b < 4u; ++b)
            for (size_t a = 0; a < sizeof as / sizeof as[0]; ++a)
                for (size_t t = 0; t < sizeof ts / sizeof ts[0]; ++t)
                    Line("{\"k\":\"s.reserve\",\"f\":" + B(Bit(b, 0)) + ",\"rs\":" + B(Bit(b, 1)) + ",\"a\":" + I(as[a]) + ",\"t\":" +
                        I(ts[t]) + ",\"r\":" + B(CanReserve(Bit(b, 0), Bit(b, 1), as[a], ts[t])) + "}");
    }
    for (int c = -1; c <= 2; ++c)
        for (int rs = -1; rs <= 2; ++rs)
            for (int h = -1; h <= 2; ++h)
                for (int cur = -1; cur <= 2; ++cur)
                    Line("{\"k\":\"s.next\",\"c\":" + I(c) + ",\"rs\":" + I(rs) + ",\"h\":" + I(h) + ",\"cur\":" + I(cur) + ",\"r\":" +
                        I(NextRole(c, rs, h, static_cast<AudienceRole>(cur))) + "}");
    for (unsigned b = 0; b < 16u; ++b)
        Line("{\"k\":\"s.release\",\"v\":" + B(Bit(b, 0)) + ",\"d\":" + B(Bit(b, 1)) + ",\"p\":" + B(Bit(b, 2)) + ",\"h\":" + B(Bit(b, 3)) +
            ",\"r\":" + I(TownSpectatorPolicy::ReleaseFor(Bit(b, 0), Bit(b, 1), Bit(b, 2), Bit(b, 3))) + "}");
    {
        const double inf = std::numeric_limits<double>::infinity();
        const double ds[] = { -1.0, std::nextafter(0.0, -1.0), -0.0, 0.0, std::nextafter(0.0, 1.0), 1.0, 1000.5, 249999.0,
            std::nextafter(250000.0, 0.0), 250000.0, std::nextafter(250000.0, 1e9), 250001.0, inf, -inf };
        const double os[] = { -1.0, 0.0, 1.0, 1000.5, 250000.0, 250001.0, inf };
        for (int st = 0; st < 2; ++st)
            for (size_t d = 0; d < sizeof ds / sizeof ds[0]; ++d)
                for (size_t o = 0; o < sizeof os / sizeof os[0]; ++o)
                    Line("{\"k\":\"s.arena\",\"st\":" + B(st != 0) + ",\"d\":" + D(ds[d]) + ",\"o\":" + D(os[o]) + ",\"r\":" +
                        B(BelongsToArena(st != 0, ds[d], os[o])) + "}");
    }

    using namespace TownCorpseCleanupPolicy;
    for (unsigned b = 0; b < 16u; ++b)
        Line("{\"k\":\"c.elig\",\"a\":" + Bits(b, 4) + ",\"r\":" + B(EligibleCorpse(Bit(b, 0), Bit(b, 1), Bit(b, 2), Bit(b, 3))) + "}");
    for (unsigned b = 0; b < 64u; ++b)
        Line("{\"k\":\"c.handler\",\"a\":" + Bits(b, 6) + ",\"r\":" +
            B(HandlerReady(Bit(b, 0), Bit(b, 1), Bit(b, 2), Bit(b, 3), Bit(b, 4), Bit(b, 5))) + "}");
    for (unsigned b = 0; b < 32u; ++b)
        Line("{\"k\":\"c.furnace\",\"a\":" + Bits(b, 5) + ",\"r\":" +
            B(FurnaceReady(Bit(b, 0), Bit(b, 1), Bit(b, 2), Bit(b, 3), Bit(b, 4))) + "}");
    for (unsigned b = 0; b < 16u; ++b)
        Line("{\"k\":\"c.next\",\"a\":" + Bits(b, 4) + ",\"r\":" + I(NextAction(Bit(b, 0), Bit(b, 1), Bit(b, 2), Bit(b, 3))) + "}");
    for (unsigned b = 0; b < 256u; ++b)
        Line("{\"k\":\"c.release\",\"a\":" + Bits(b, 8) + ",\"r\":" + I(TownCorpseCleanupPolicy::ReleaseFor(Bit(b, 0), Bit(b, 1),
            Bit(b, 2), Bit(b, 3), Bit(b, 4), Bit(b, 5), Bit(b, 6), Bit(b, 7))) + "}");
    {
        const float inf = std::numeric_limits<float>::infinity();
        const float es[] = { -1.0f, -0.0f, 0.0f, 0.5f, std::nextafterf(180.0f, 0.0f), 180.0f, std::nextafterf(180.0f, 1e9f), 200.0f, inf };
        const float ls[] = { -1.0f, std::nextafterf(0.0f, -1.0f), -0.0f, 0.0f, 15.0f, 180.0f, inf };
        for (size_t e = 0; e < sizeof es / sizeof es[0]; ++e)
            for (size_t l = 0; l < sizeof ls / sizeof ls[0]; ++l)
                Line("{\"k\":\"c.timed\",\"e\":" + Fx(es[e]) + ",\"l\":" + Fx(ls[l]) + ",\"r\":" + B(TimedOut(es[e], ls[l])) + ",\"b0\":" +
                    B(BatchTimedOut(false, es[e], ls[l])) + ",\"b1\":" + B(BatchTimedOut(true, es[e], ls[l])) + "}");
    }
    {
        const float es[] = { 0.0f, 3.5f, -2.0f, 179.25f, 16777216.0f };
        const float dsv[] = { -1.0f, -0.0f, 0.0f, std::nextafterf(0.0f, 1.0f), 0.1f, 0.5f, std::nextafterf(1.0f, 0.0f), 1.0f,
            std::nextafterf(1.0f, 2.0f), 2.0f, 1000.0f };
        for (size_t e = 0; e < sizeof es / sizeof es[0]; ++e)
            for (size_t d = 0; d < sizeof dsv / sizeof dsv[0]; ++d)
                for (int pz = 0; pz < 2; ++pz)
                    Line("{\"k\":\"c.adv\",\"e\":" + F(es[e]) + ",\"d\":" + F(dsv[d]) + ",\"pz\":" + B(pz != 0) + ",\"r\":" +
                        F(AdvanceElapsed(es[e], dsv[d], pz != 0)) + "}");
    }
    {
        // hand cases: NULL pointers, count <= 0, current outside, the current one still open, the first open one
        bool e4[] = { true, false, true, true }, r4[] = { false, false, true, false };
        const int curs[] = { -2, -1, 0, 1, 2, 3, 4, 5 };
        for (int nulls = 0; nulls < 3; ++nulls)
            for (int n = -1; n <= 4; ++n)
                for (size_t c = 0; c < sizeof curs / sizeof curs[0]; ++c)
                {
                    const bool* el = nulls == 1 ? NULL : e4;
                    const bool* rs = nulls == 2 ? NULL : r4;
                    Line("{\"k\":\"c.select\",\"el\":" + (el ? Bits(0xdu, 4) : std::string("null")) + ",\"rs\":" +
                        (rs ? Bits(0x4u, 4) : std::string("null")) + ",\"n\":" + I(n) + ",\"cur\":" + I(curs[c]) + ",\"r\":" +
                        I(SelectCorpse(el, rs, n, curs[c])) + "}");
                }
        for (int r = 0; r < 120; ++r)
        {
            const int n = 1 + static_cast<int>(Rn(12));
            bool el[16], rs[16];
            unsigned eb = 0, rb = 0;
            for (int i = 0; i < n; ++i)
            {
                el[i] = Rn(3) != 0; rs[i] = Rn(3) == 0;
                if (el[i]) eb |= 1u << i;
                if (rs[i]) rb |= 1u << i;
            }
            const int cur = static_cast<int>(Rn(static_cast<unsigned>(n + 3))) - 1;
            Line("{\"k\":\"c.select\",\"el\":" + Bits(eb, n) + ",\"rs\":" + Bits(rb, n) + ",\"n\":" + I(n) + ",\"cur\":" + I(cur) +
                ",\"r\":" + I(SelectCorpse(el, rs, n, cur)) + "}");
        }
    }

    std::printf("]\n");
    return 0;
}
