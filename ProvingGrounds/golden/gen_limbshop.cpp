// sources:
// (header-only: TownLimbShopPolicy.h, TownFighterCatalog.h and TownChallengePolicy.h have no .cpp, so nothing is linked)
// The town limb shop policy, as JSON lines in one array; each line has "k".
//   "grade" {div, leg, roll, r}   GradeFor(division, legendary, roll): every division -3..4 x legendary x the boundary rolls
//                                 (69/70, 94/95, 14/15, 89/90, 9/10, 79/80, the same +100 and +4294967200) and random rolls.
//   "g"     {g, q, name, price}   QualityLevel(g), GradeName(g) and Price(g, n) for n in the list "ns" (INT_MAX / perLimb
//                                 and one above it, 0, negative, 1..5), for g 0..7 (5..7 = out of range, inside the enum's
//                                 value range so not undefined).
//   "kind"  {mask, kind, name}    KindFor(mask) and OrderName(KindFor(mask)) for masks 0..32 and large values.
//   "oname" {kind, r}             OrderName(kind) for kind 0..3.
//   "line"  {g, var, kv, t, f}    TraderLine(g, var, kv) and FighterLine(g, var, kv): g 0..7, variants 0..5 and big ones,
//                                 kind values -2..4 (out of range = arm).
//   "seed"  {id, m, mask, r}      Seed(identity, matches, limbMask) (also covered by hash.json; repeated here to show
//                                 Hashing.LimbShopSeed is the same function the shop uses).
//   "order" {role, id, m, mask, div, leg, seed, g, var, q, t, f}  the queue step of TownLimbShop.cpp:415-431 written with
//           the policy functions: role -> TownFighterCatalog::Find / TownChallengePolicy::NamedRoleDivision -> division and
//           legendary; seed; grade; variant = (seed >> 8) % 3; the item quality and both lines. Every catalog role, every named
//           role, unknown roles and a null role (NamedRoleDivision(role ? role : "")), each with several identities, match
//           counts and limb masks.
//   "app"   {steps}               an ApproachProgress (C++ default 0, 1e30, 0) driven by reset {d} (ResetApproach) and
//           update {d, dt, st} (UpdateApproach, then ApproachStalled); s = [elapsed, bestDistance, stalled] after each step.
#include <climits>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include "TownLimbShopPolicy.h"
#include "TownFighterCatalog.h"
#include "TownChallengePolicy.h"

using namespace TownLimbShopPolicy;

static unsigned s_seed = 1110003u;
static unsigned R() { s_seed = s_seed * 1664525u + 1013904223u; return s_seed >> 8; }
static unsigned Rn(unsigned n) { return R() % n; }

static bool g_first = true;
static void Line(const std::string& s) { std::printf("%s%s\n", g_first ? "[" : ",", s.c_str()); g_first = false; }
static std::string I(long long v) { char b[32]; std::snprintf(b, sizeof b, "%lld", v); return b; }
static std::string U(unsigned long long v) { char b[32]; std::snprintf(b, sizeof b, "%llu", v); return b; }
static std::string F(float v) { char b[48]; std::snprintf(b, sizeof b, "%.9g", static_cast<double>(v)); return b; }
static std::string B(bool v) { return v ? "1" : "0"; }
static std::string S(const char* s)
{
    if (!s) return "null";
    std::string o = "\"";
    for (const unsigned char* p = reinterpret_cast<const unsigned char*>(s); *p; ++p)
    {
        if (*p == '"' || *p == '\\') { o += '\\'; o += static_cast<char>(*p); }
        else if (*p < 0x20) { char b[8]; std::snprintf(b, sizeof b, "\\u%04x", *p); o += b; }
        else o += static_cast<char>(*p);
    }
    return o + "\"";
}
static std::string S(const std::string& s) { return S(s.c_str()); }

int main()
{
    // grade
    {
        const unsigned base[] = { 0u, 9u, 10u, 14u, 15u, 69u, 70u, 79u, 80u, 89u, 90u, 94u, 95u, 99u };
        std::vector<unsigned> rolls;
        for (size_t i = 0; i < sizeof base / sizeof base[0]; ++i)
        {
            rolls.push_back(base[i]); rolls.push_back(base[i] + 100u); rolls.push_back(base[i] + 4294967200u);
        }
        rolls.push_back(0xffffffffu);   // 4294967295 % 100 = 95
        for (int r = 0; r < 12; ++r) rolls.push_back(R() * 2654435761u);
        for (int div = -3; div <= 4; ++div)
            for (int leg = 0; leg < 2; ++leg)
                for (size_t i = 0; i < rolls.size(); ++i)
                    Line("{\"k\":\"grade\",\"div\":" + I(div) + ",\"leg\":" + B(leg != 0) + ",\"roll\":" + U(rolls[i]) +
                        ",\"r\":" + I(GradeFor(div, leg != 0, rolls[i])) + "}");
        const int divs[] = { INT_MIN, INT_MAX };
        for (int d = 0; d < 2; ++d)
            for (int leg = 0; leg < 2; ++leg)
                Line("{\"k\":\"grade\",\"div\":" + I(divs[d]) + ",\"leg\":" + B(leg != 0) + ",\"roll\":12,\"r\":" +
                    I(GradeFor(divs[d], leg != 0, 12u)) + "}");
    }

    // g (quality, names, the unused price table)
    {
        const int per[] = { 800, 2500, 6000, 12000, 25000 };
        for (int g = 0; g <= 7; ++g)
        {
            const int safe = g < 5 ? g : 0;
            std::vector<int> ns;
            ns.push_back(INT_MIN); ns.push_back(-1); ns.push_back(0);
            for (int n = 1; n <= 5; ++n) ns.push_back(n);
            ns.push_back(INT_MAX / per[safe]); ns.push_back(INT_MAX / per[safe] + 1); ns.push_back(INT_MAX);
            std::string nsj = "[", pj = "[";
            for (size_t i = 0; i < ns.size(); ++i)
            {
                nsj += (i ? "," : "") + I(ns[i]);
                pj += (i ? "," : "") + I(Price(static_cast<Grade>(g), ns[i]));
            }
            Line("{\"k\":\"g\",\"g\":" + I(g) + ",\"q\":" + I(QualityLevel(static_cast<Grade>(g))) + ",\"name\":" +
                S(GradeName(static_cast<Grade>(g))) + ",\"ns\":" + nsj + "],\"price\":" + pj + "]}");
        }
    }

    // kind
    {
        std::vector<unsigned> masks;
        for (unsigned m = 0; m <= 32u; ++m) masks.push_back(m);
        masks.push_back(0x80000000u); masks.push_back(0x80000001u); masks.push_back(0x80000004u); masks.push_back(0xffffffffu);
        masks.push_back(0x10u); masks.push_back(0x11u); masks.push_back(0x14u); masks.push_back(0x30u);
        for (size_t i = 0; i < masks.size(); ++i)
            Line("{\"k\":\"kind\",\"mask\":" + U(masks[i]) + ",\"kind\":" + I(KindFor(masks[i])) + ",\"name\":" +
                S(OrderName(KindFor(masks[i]))) + "}");
        for (int k = 0; k <= 3; ++k)
            Line("{\"k\":\"oname\",\"kind\":" + I(k) + ",\"r\":" + S(OrderName(static_cast<OrderKind>(k))) + "}");
    }

    // line
    {
        const unsigned vars[] = { 0u, 1u, 2u, 3u, 4u, 5u, 0xffffffffu, 0x80000000u, 1000001u };
        for (int g = 0; g <= 7; ++g)
            for (size_t v = 0; v < sizeof vars / sizeof vars[0]; ++v)
                for (int kv = -2; kv <= 4; ++kv)
                    Line("{\"k\":\"line\",\"g\":" + I(g) + ",\"var\":" + U(vars[v]) + ",\"kv\":" + I(kv) + ",\"t\":" +
                        S(TraderLine(static_cast<Grade>(g), vars[v], kv)) + ",\"f\":" +
                        S(FighterLine(static_cast<Grade>(g), vars[v], kv)) + "}");
    }

    // seed
    {
        const char* ids[] = { "", "a", "Ressa Vane", "219-Proving Grounds.mod|17", "x\x01y", "\xc3\xa9t\xc3\xa9" };
        const int ms[] = { INT_MIN, -1, 0, 1, 255, 256, 65536, INT_MAX };
        const unsigned masks[] = { 0u, 1u, 15u, 16u, 0xffffffffu };
        for (size_t i = 0; i < sizeof ids / sizeof ids[0]; ++i)
            for (size_t m = 0; m < sizeof ms / sizeof ms[0]; ++m)
                for (size_t k = 0; k < sizeof masks / sizeof masks[0]; ++k)
                    Line("{\"k\":\"seed\",\"id\":" + S(ids[i]) + ",\"m\":" + I(ms[m]) + ",\"mask\":" + U(masks[k]) + ",\"r\":" +
                        U(Seed(ids[i], ms[m], masks[k])) + "}");
    }

    // order (TownLimbShop.cpp:415-431)
    {
        const char* roles[] = {
            "219-Proving Grounds.mod", "223-Proving Grounds.mod", "225-Proving Grounds.mod", "206-Proving Grounds.mod",
            "96-Proving Grounds.mod", "207-Proving Grounds.mod", "213-Proving Grounds.mod", "97-Proving Grounds.mod",
            "210-Proving Grounds.mod", "218-Proving Grounds.mod", "98-Proving Grounds.mod", "216-Proving Grounds.mod",
            "232-Proving Grounds.mod", "230-Proving Grounds.mod", "227-Proving Grounds.mod", "238-Proving Grounds.mod",
            "240-Proving Grounds.mod", "236-Proving Grounds.mod", "298-Proving Grounds.mod", "302-Proving Grounds.mod",
            "300-Proving Grounds.mod", "666-Proving Grounds.mod", "665-Proving Grounds.mod", "670-Proving Grounds.mod",
            "673-Proving Grounds.mod", "681-Proving Grounds.mod", "675-Proving Grounds.mod", "677-Proving Grounds.mod",
            "679-Proving Grounds.mod", "678-Proving Grounds.mod",
            "616-Proving Grounds.mod", "619-Proving Grounds.mod", "169-Proving Grounds.mod", "168-Proving Grounds.mod",
            "167-Proving Grounds.mod", "623-Proving Grounds.mod",
            "103-Proving Grounds.mod", "", "nonsense", NULL
        };
        const char* idParts[] = { "Ressa Vane", "Bum-Per", "Senn", "Pit Fighter", "", "Torka|4|9" };
        for (size_t r = 0; r < sizeof roles / sizeof roles[0]; ++r)
            for (int rep = 0; rep < 6; ++rep)
            {
                const char* role = roles[r];
                const TownFighterCatalog::Entry* regular = role ? TownFighterCatalog::Find(role) : NULL;
                const int namedDivision = TownChallengePolicy::NamedRoleDivision(role ? role : "");
                const int division = regular ? regular->tier : namedDivision >= 0 ? namedDivision : 0;
                const bool legendary = !regular && namedDivision == 2;
                std::string identity = std::string(idParts[rep]) + "#" + I(static_cast<long long>(Rn(1000)));
                const int matches = static_cast<int>(Rn(260)) - 10;
                const unsigned mask = 1u + Rn(15);
                const unsigned seed = Seed(identity, matches, mask);
                const Grade grade = GradeFor(division, legendary, seed);
                const unsigned variant = (seed >> 8) % 3u;
                Line("{\"k\":\"order\",\"role\":" + S(role) + ",\"id\":" + S(identity) + ",\"m\":" + I(matches) + ",\"mask\":" +
                    U(mask) + ",\"div\":" + I(division) + ",\"leg\":" + B(legendary) + ",\"seed\":" + U(seed) + ",\"g\":" + I(grade) +
                    ",\"var\":" + U(variant) + ",\"q\":" + I(QualityLevel(grade)) + ",\"t\":" + S(TraderLine(grade, variant, KindFor(mask))) +
                    ",\"f\":" + S(FighterLine(grade, variant, KindFor(mask))) + "}");
            }
    }

    // app
    {
        {
            ApproachProgress pr;
            std::string st = "[";
            struct Op { int reset; float d, dt; };
            const Op ops[] = {
                { 0, 500.0f, 0.5f },      // from the default 1e30: progress
                { 0, 499.0f, 0.5f },      // 499 + 1 < 500 false: stall
                { 0, 498.99f, 0.5f },     // progress
                { 0, 498.0f, -3.0f },     // dt clamps to 0
                { 0, 400.0f, 7.0f },      // dt clamps to 1, progress
                { 1, 50.0f, 0.0f },       // reset
                { 0, 50.0f, 1.0f },
            };
            for (size_t i = 0; i < sizeof ops / sizeof ops[0]; ++i)
            {
                if (i) st += ",";
                if (ops[i].reset)
                {
                    ResetApproach(pr, ops[i].d);
                    st += "{\"op\":\"reset\",\"d\":" + F(ops[i].d);
                }
                else
                {
                    UpdateApproach(pr, ops[i].d, ops[i].dt);
                    st += "{\"op\":\"update\",\"d\":" + F(ops[i].d) + ",\"dt\":" + F(ops[i].dt) + ",\"st\":" + B(ApproachStalled(pr));
                }
                st += ",\"s\":[" + F(pr.elapsed) + "," + F(pr.bestDistance) + "," + F(pr.stalled) + "]}";
            }
            Line("{\"k\":\"app\",\"steps\":" + st + "]}");
        }
        // exactly 20 stalled seconds (dt 1 twenty times, then 0.25 * 80) and just below
        for (int variant = 0; variant < 3; ++variant)
        {
            ApproachProgress pr;
            std::string st = "[";
            ResetApproach(pr, 100.0f);
            st += "{\"op\":\"reset\",\"d\":100,\"s\":[" + F(pr.elapsed) + "," + F(pr.bestDistance) + "," + F(pr.stalled) + "]}";
            const float dt = variant == 0 ? 1.0f : variant == 1 ? 0.25f : 0.1f;
            const int n = variant == 0 ? 21 : variant == 1 ? 81 : 201;
            for (int i = 0; i < n; ++i)
            {
                UpdateApproach(pr, 100.0f, dt);
                st += ",{\"op\":\"update\",\"d\":100,\"dt\":" + F(dt) + ",\"st\":" + B(ApproachStalled(pr)) + ",\"s\":[" +
                    F(pr.elapsed) + "," + F(pr.bestDistance) + "," + F(pr.stalled) + "]}";
            }
            Line("{\"k\":\"app\",\"steps\":" + st + "]}");
        }
        for (int seq = 0; seq < 30; ++seq)
        {
            ApproachProgress pr;
            std::string st = "[";
            float dist = static_cast<float>(Rn(600));
            const int n = 5 + static_cast<int>(Rn(60));
            for (int i = 0; i < n; ++i)
            {
                if (i) st += ",";
                if (Rn(15) == 0)
                {
                    ResetApproach(pr, dist);
                    st += "{\"op\":\"reset\",\"d\":" + F(dist);
                }
                else
                {
                    const unsigned m = Rn(4);
                    if (m == 0) dist -= static_cast<float>(Rn(300)) / 100.0f;
                    else if (m == 1) dist -= 1.0f;
                    else if (m == 2) dist += static_cast<float>(Rn(100)) / 100.0f;
                    const float dt = static_cast<float>(static_cast<int>(Rn(300)) - 50) / 97.0f;
                    UpdateApproach(pr, dist, dt);
                    st += "{\"op\":\"update\",\"d\":" + F(dist) + ",\"dt\":" + F(dt) + ",\"st\":" + B(ApproachStalled(pr));
                }
                st += ",\"s\":[" + F(pr.elapsed) + "," + F(pr.bestDistance) + "," + F(pr.stalled) + "]}";
            }
            Line("{\"k\":\"app\",\"steps\":" + st + "]}");
        }
    }

    std::printf("]\n");
    return 0;
}
