// sources:
// (header-only: TownAnnouncerPolicy.h has no .cpp, so nothing is linked)
// The town announcer policy, as JSON lines in one array; each line has "k".
//   "eq"    {a, b, r, sa, sb}  Equals(a, b) and IsSpot(a), IsSpot(b); a/b may be null (JSON null).
//   "sel"   {pm, slot, roles, n, r}  Select(playerMatch, challengeSlot, roles, n). roles is null (a NULL pointer) or an array of
//                                    ids (each may be null); n <= the array length (never past it: that would be undefined).
//   "prog"  {p, v, rs, vc, var, sc, cc, sp, cs, ct, du}  one program p (0..3) and variant v: RequiredSpeakers, VariantCount,
//           Variant(p, (unsigned)v), SetupCueCount, CueCount, SpeakerRole(p, -1..3) [sp], CueSpeaker(p, -1..6, v) [cs],
//           CueText(p, v, -1..6) [ct; null = NULL], Duration(p, v) [du]. v covers -9..20 and INT_MIN/INT_MAX-ish values
//           (Variant casts a negative variant to unsigned, so -1 is roll 0xffffffff).
//   "vroll" {p, roll, r}    Variant(p, roll) for unsigned rolls (large ones included).
//   "start" {p, v, cue, r}  CueStart(p, cue, v).
//   "at"    {p, v, last, e, at, due}  CueAt and CuesDue for lastCue -3..6 and elapsed at, just below and just above each cue start
//           (nextafterf), plus random and negative values.
//   "res"   {p, spot, n, r, nf}  Resolve(p, hasSpot, speakerCount -2..4) and NextFallback(p).
//   "chain" {req, spot, have, tried, r}  the cast-fallback loop of TownAnnouncer.cpp:300-317 written with the policy functions:
//           have = the speaker roles that can be found (a subset of 260/167/169/168); tried = the candidates the loop
//           looked at; r = Resolve(candidate, spot, found). Every requested program x spot x every subset.
//   "adv"   {e, d, pz, r}   AdvanceApproach(elapsed, delta, paused).
//   "stall" {steps}         an ApproachProgress (C++ default 0,0) driven by reset {d} (ResetApproachProgress) and
//           step {d, dt, pz, r} (ApproachStalled); each step prints the struct after it [bestDistance, stalledElapsed].
//   "rel"   {o, d, s, r, pd, pv, dv, cf}  PlanRelease(owned, directed, safe) [r = release, resume, defer],
//           PendingReleaseFor(valid=o, dead=d, safe=s) [pd], CountdownElapsedAfterFallback(lost=o, elapsed=pv) [cf].
#include <climits>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include "TownAnnouncerPolicy.h"

using namespace TownAnnouncerPolicy;

static unsigned s_seed = 1110002u;
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

static const char* const kIds[] = {
    "169-Proving Grounds.mod", "168-Proving Grounds.mod", "167-Proving Grounds.mod", "260-Proving Grounds.mod",
    "261-Proving Grounds.mod", "616-Proving Grounds.mod", "169-Proving Grounds.mo", "169-proving grounds.mod", "", NULL
};
static const int kIdCount = 10;

int main()
{
    // eq
    for (int a = 0; a < kIdCount; ++a)
        for (int b = 0; b < kIdCount; ++b)
            Line("{\"k\":\"eq\",\"a\":" + S(kIds[a]) + ",\"b\":" + S(kIds[b]) + ",\"r\":" + B(Equals(kIds[a], kIds[b])) +
                ",\"sa\":" + B(IsSpot(kIds[a])) + ",\"sb\":" + B(IsSpot(kIds[b])) + "}");

    // sel: hand cases (every slot with empty / null lists, each id alone, both orders) + random lists
    {
        std::vector<std::vector<int> > lists;   // indices into kIds; -1 entry list = NULL pointer
        lists.push_back(std::vector<int>());
        for (int i = 0; i < kIdCount; ++i) lists.push_back(std::vector<int>(1, i));
        { int a[] = { 5, 0 }; lists.push_back(std::vector<int>(a, a + 2)); }
        { int a[] = { 3, 1 }; lists.push_back(std::vector<int>(a, a + 2)); }
        { int a[] = { 9, 2, 6 }; lists.push_back(std::vector<int>(a, a + 3)); }
        { int a[] = { 2, 3, 4, 5 }; lists.push_back(std::vector<int>(a, a + 4)); }
        for (int r = 0; r < 60; ++r)
        {
            std::vector<int> l;
            const int n = static_cast<int>(Rn(6));
            for (int i = 0; i < n; ++i) l.push_back(static_cast<int>(Rn(static_cast<unsigned>(kIdCount))));
            lists.push_back(l);
        }
        for (int slot = -2; slot <= 7; ++slot)
            for (int pm = 0; pm < 2; ++pm)
            {
                // NULL pointer with counts 0..3
                for (int n = 0; n <= 3; ++n)
                    Line("{\"k\":\"sel\",\"pm\":" + B(pm != 0) + ",\"slot\":" + I(slot) + ",\"roles\":null,\"n\":" + I(n) +
                        ",\"r\":" + I(Select(pm != 0, slot, NULL, n)) + "}");
                for (size_t li = 0; li < lists.size(); ++li)
                {
                    // the full list, and (if longer than 0) a shorter count; plus a negative count
                    std::vector<const char*> ptrs;
                    for (size_t i = 0; i < lists[li].size(); ++i) ptrs.push_back(kIds[lists[li][i]]);
                    const char* const* data = ptrs.empty() ? NULL : &ptrs[0];
                    std::string arr = "[";
                    for (size_t i = 0; i < ptrs.size(); ++i) arr += (i ? "," : "") + S(ptrs[i]);
                    arr += "]";
                    const int full = static_cast<int>(ptrs.size());
                    std::vector<int> counts; counts.push_back(full);
                    if (full > 0) counts.push_back(static_cast<int>(Rn(static_cast<unsigned>(full))));
                    if (li % 7 == 0) counts.push_back(-1);
                    for (size_t c = 0; c < counts.size(); ++c)
                    {
                        // an empty list is passed as a non-NULL pointer here only when one exists; a std::vector's
                        // data() of an empty vector may be NULL, so the empty list is printed as [] and passed as NULL:
                        // the C++ loop does not read the pointer with count <= 0 either way.
                        Line("{\"k\":\"sel\",\"pm\":" + B(pm != 0) + ",\"slot\":" + I(slot) + ",\"roles\":" + arr + ",\"n\":" +
                            I(counts[c]) + ",\"r\":" + I(Select(pm != 0, slot, data, counts[c])) + "}");
                    }
                }
            }
    }

    // prog
    {
        std::vector<int> variants;
        for (int v = -9; v <= 20; ++v) variants.push_back(v);
        variants.push_back(INT_MAX); variants.push_back(INT_MAX - 1); variants.push_back(INT_MIN); variants.push_back(INT_MIN + 1);
        variants.push_back(1000003); variants.push_back(-1000003);
        for (int p = 0; p <= 3; ++p)
            for (size_t vi = 0; vi < variants.size(); ++vi)
            {
                const Program prog = static_cast<Program>(p);
                const int v = variants[vi];
                std::string sp = "[", cs = "[", ct = "[";
                for (int i = -1; i <= 3; ++i) sp += (i > -1 ? "," : "") + S(SpeakerRole(prog, i));
                for (int c = -1; c <= 6; ++c)
                {
                    cs += (c > -1 ? "," : "") + I(CueSpeaker(prog, c, v));
                    ct += (c > -1 ? "," : "") + S(CueText(prog, v, c));
                }
                sp += "]"; cs += "]"; ct += "]";
                Line("{\"k\":\"prog\",\"p\":" + I(p) + ",\"v\":" + I(v) + ",\"rs\":" + I(RequiredSpeakers(prog)) +
                    ",\"vc\":" + I(VariantCount(prog)) + ",\"var\":" + I(Variant(prog, static_cast<unsigned>(v))) +
                    ",\"sc\":" + I(SetupCueCount(prog, v)) + ",\"cc\":" + I(CueCount(prog, v)) +
                    ",\"sp\":" + sp + ",\"cs\":" + cs + ",\"ct\":" + ct + ",\"du\":" + F(Duration(prog, v)) + "}");
            }
        // CueCount / CueSpeaker / CueStart / CueAt / CuesDue / Duration with the default variant 0 equal v = 0 above.
    }

    // vroll
    {
        const unsigned rolls[] = { 0u, 1u, 3u, 4u, 5u, 7u, 8u, 9u, 39u, 40u, 0x7fffffffu, 0x80000000u, 0xfffffffeu, 0xffffffffu };
        for (int p = 0; p <= 3; ++p)
        {
            for (size_t i = 0; i < sizeof rolls / sizeof rolls[0]; ++i)
                Line("{\"k\":\"vroll\",\"p\":" + I(p) + ",\"roll\":" + U(rolls[i]) + ",\"r\":" +
                    I(Variant(static_cast<Program>(p), rolls[i])) + "}");
            for (int r = 0; r < 20; ++r)
            {
                const unsigned roll = R() * 2654435761u;
                Line("{\"k\":\"vroll\",\"p\":" + I(p) + ",\"roll\":" + U(roll) + ",\"r\":" +
                    I(Variant(static_cast<Program>(p), roll)) + "}");
            }
        }
    }

    // start
    {
        const int vs[] = { 0, 1, 2, 3, -1 };
        for (int p = 0; p <= 3; ++p)
            for (size_t vi = 0; vi < sizeof vs / sizeof vs[0]; ++vi)
                for (int cue = -2; cue <= 7; ++cue)
                    Line("{\"k\":\"start\",\"p\":" + I(p) + ",\"v\":" + I(vs[vi]) + ",\"cue\":" + I(cue) + ",\"r\":" +
                        F(CueStart(static_cast<Program>(p), cue, vs[vi])) + "}");
    }

    // at
    {
        const int vs[] = { 0, 1, 2, 3, -1 };
        for (int p = 0; p <= 3; ++p)
            for (size_t vi = 0; vi < sizeof vs / sizeof vs[0]; ++vi)
                for (int last = -3; last <= 6; ++last)
                {
                    std::vector<float> es;
                    es.push_back(-1.0f); es.push_back(0.0f); es.push_back(-0.0f); es.push_back(1.0e9f);
                    es.push_back(std::nextafterf(0.0f, -1.0f));
                    for (int c = 0; c <= 6; ++c)
                    {
                        const float s = static_cast<float>(c) * 3.25f;
                        es.push_back(s); es.push_back(std::nextafterf(s, -1.0e9f)); es.push_back(std::nextafterf(s, 1.0e9f));
                    }
                    es.push_back(static_cast<float>(Rn(2400)) / 100.0f);
                    for (size_t ei = 0; ei < es.size(); ++ei)
                    {
                        const Program prog = static_cast<Program>(p);
                        Line("{\"k\":\"at\",\"p\":" + I(p) + ",\"v\":" + I(vs[vi]) + ",\"last\":" + I(last) + ",\"e\":" +
                            F(es[ei]) + ",\"at\":" + I(CueAt(prog, last, es[ei], vs[vi])) + ",\"due\":" +
                            I(CuesDue(prog, last, es[ei], vs[vi])) + "}");
                    }
                }
    }

    // res
    for (int p = 0; p <= 3; ++p)
        for (int spot = 0; spot < 2; ++spot)
            for (int n = -2; n <= 4; ++n)
                Line("{\"k\":\"res\",\"p\":" + I(p) + ",\"spot\":" + B(spot != 0) + ",\"n\":" + I(n) + ",\"r\":" +
                    I(Resolve(static_cast<Program>(p), spot != 0, n)) + ",\"nf\":" + I(NextFallback(static_cast<Program>(p))) + "}");

    // chain (TownAnnouncer.cpp:300-317)
    {
        const char* const castIds[] = { "260-Proving Grounds.mod", "167-Proving Grounds.mod", "169-Proving Grounds.mod",
            "168-Proving Grounds.mod" };
        for (int req = 0; req <= 3; ++req)
            for (int spot = 0; spot < 2; ++spot)
                for (unsigned have = 0; have < 16u; ++have)
                {
                    std::string tried = "[";
                    Program candidate = static_cast<Program>(req);
                    int found = 0;
                    bool firstTry = true;
                    while (spot && candidate != LegacyCountdown)
                    {
                        tried += (firstTry ? "" : ",") + I(candidate);
                        firstTry = false;
                        found = 0;
                        const int candidateCount = RequiredSpeakers(candidate);
                        for (int i = 0; i < candidateCount; ++i)
                        {
                            const char* role = SpeakerRole(candidate, i);
                            for (int c = 0; c < 4; ++c)
                                if ((have & (1u << c)) && Equals(role, castIds[c])) { ++found; break; }
                        }
                        if (found == candidateCount) break;
                        candidate = NextFallback(candidate);
                    }
                    tried += "]";
                    Line("{\"k\":\"chain\",\"req\":" + I(req) + ",\"spot\":" + B(spot != 0) + ",\"have\":" + U(have) +
                        ",\"tried\":" + tried + ",\"r\":" + I(Resolve(candidate, spot != 0, found)) + "}");
                }
    }

    // adv
    for (int r = 0; r < 40; ++r)
    {
        const float e = static_cast<float>(static_cast<int>(Rn(20000)) - 2000) / 97.0f;
        const float d = static_cast<float>(static_cast<int>(Rn(3000)) - 500) / 1013.0f;
        const bool pz = Rn(3) == 0;
        Line("{\"k\":\"adv\",\"e\":" + F(e) + ",\"d\":" + F(d) + ",\"pz\":" + B(pz) + ",\"r\":" + F(AdvanceApproach(e, d, pz)) + "}");
    }

    // stall
    {
        // a hand sequence for the exact boundaries: progress of exactly 2.0, stalled exactly 20.0
        {
            ApproachProgress pr;
            std::string st = "[";
            struct Op { int reset; float d, dt; bool pz; };
            const Op ops[] = {
                { 0, 10.0f, 1.0f, false },   // from the default 0: 10 is not <= -2, stall 1
                { 1, 100.0f, 0.0f, false },  // reset 100
                { 0, 98.0f, 5.0f, false },   // exactly 2.0 progress: best 98, stalled 0
                { 0, 96.5f, 5.0f, false },   // 1.5: stall 5
                { 0, 96.5f, 5.0f, true },    // paused: nothing
                { 0, 96.5f, 15.0f, false },  // stall 20 -> true
                { 0, 96.0f, 1.0f, false },   // 98-2 = 96: progress, reset
                { 0, 200.0f, 19.999998f, false },
                { 0, 200.0f, 0.000002f, false },
                { 0, 200.0f, -30.0f, false },// a negative delta subtracts
                { 0, 200.0f, 50.0f, false },
            };
            for (size_t i = 0; i < sizeof ops / sizeof ops[0]; ++i)
            {
                if (i) st += ",";
                if (ops[i].reset)
                {
                    ResetApproachProgress(pr, ops[i].d);
                    st += "{\"op\":\"reset\",\"d\":" + F(ops[i].d);
                }
                else
                {
                    const bool r = ApproachStalled(pr, ops[i].d, ops[i].dt, ops[i].pz);
                    st += "{\"op\":\"step\",\"d\":" + F(ops[i].d) + ",\"dt\":" + F(ops[i].dt) + ",\"pz\":" + B(ops[i].pz) +
                        ",\"r\":" + B(r);
                }
                st += ",\"s\":[" + F(pr.bestDistance) + "," + F(pr.stalledElapsed) + "]}";
            }
            Line("{\"k\":\"stall\",\"steps\":" + st + "]}");
        }
        for (int seq = 0; seq < 30; ++seq)
        {
            ApproachProgress pr;
            std::string st = "[";
            float dist = static_cast<float>(Rn(400));
            const int n = 5 + static_cast<int>(Rn(40));
            for (int i = 0; i < n; ++i)
            {
                if (i) st += ",";
                if (Rn(12) == 0)
                {
                    ResetApproachProgress(pr, dist);
                    st += "{\"op\":\"reset\",\"d\":" + F(dist);
                }
                else
                {
                    const unsigned m = Rn(4);
                    if (m == 0) dist -= static_cast<float>(Rn(500)) / 100.0f;
                    else if (m == 1) dist -= 2.0f;
                    else if (m == 2) dist += static_cast<float>(Rn(100)) / 100.0f;
                    const float dt = static_cast<float>(Rn(400)) / 97.0f;
                    const bool pz = Rn(8) == 0;
                    const bool r = ApproachStalled(pr, dist, dt, pz);
                    st += "{\"op\":\"step\",\"d\":" + F(dist) + ",\"dt\":" + F(dt) + ",\"pz\":" + B(pz) + ",\"r\":" + B(r);
                }
                st += ",\"s\":[" + F(pr.bestDistance) + "," + F(pr.stalledElapsed) + "]}";
            }
            Line("{\"k\":\"stall\",\"steps\":" + st + "]}");
        }
    }

    // rel
    for (int o = 0; o < 2; ++o)
        for (int d = 0; d < 2; ++d)
            for (int s = 0; s < 2; ++s)
            {
                const ReleasePlan plan = PlanRelease(o != 0, d != 0, s != 0);
                const float pv = static_cast<float>(1 + o * 3 + d * 5 + s * 7) * 1.25f;
                Line("{\"k\":\"rel\",\"o\":" + B(o != 0) + ",\"d\":" + B(d != 0) + ",\"s\":" + B(s != 0) + ",\"r\":[" +
                    B(plan.releaseOwnership) + "," + B(plan.resumeNative) + "," + B(plan.deferNative) + "],\"pd\":" +
                    I(PendingReleaseFor(o != 0, d != 0, s != 0)) + ",\"pv\":" + F(pv) + ",\"cf\":" +
                    F(CountdownElapsedAfterFallback(o != 0, pv)) + "}");
            }

    std::printf("]\n");
    return 0;
}
