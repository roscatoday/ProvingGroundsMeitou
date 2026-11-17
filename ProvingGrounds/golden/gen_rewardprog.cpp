// sources:
// (header-only: RewardProgression.h and LeaderboardData.h have no .cpp, so nothing is linked)
// RewardProgression and LeaderboardData, as JSON lines in one array; each line has "k" plus its inputs and outputs.
//   "dt"   DecodeTiers(text) into a vector pre-filled with one marker entry: ok, the vector after, and EncodeTiers of it
//   "du"   DecodeUnlocks(text) likewise (marker unlock "marker"), EncodeUnlocks of the result
//   "et"   EncodeTiers / EncodeUnlocks of random vectors (ids may hold ':', '=', ';'; tiers may be negative)
//   "op"   a random sequence of HighestTier / SetHighestTier / RestoreHighestTier / HasUnlock / AddUnlock / RemoveUnlock on one
//          tier vector and one unlock vector: each op with its inputs and result, then the final vectors
//   "lb"   a random sequence of LeaderboardData calls (ApplyCompetitiveResult, AddMarks, EnsureStanding, EnsureProgression,
//          ResetFighters) on one Collections, then every table
//   "id"   IsLeaderboardId / KindForId over the four ids, near misses and null
//   "ver"  ShouldResetFighters over versions -1..12, ForMatch, AwardedTotal edges
// Texts never hold a run of 10+ digits: strtoul/atoi overflow differs between MSVC (32-bit unsigned long, atoi saturates) and
// glibc (64-bit unsigned long, atoi truncates), and MSVC is the shipped build. Tests/Ported cover those by hand.
#include "LeaderboardData.h"
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
using namespace RewardProgression;
static unsigned s = 5150u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }
static void Str(const std::string& v)
{
    std::putchar('"');
    for (unsigned char c : v)
    {
        if (c == '"' || c == '\\') { std::putchar('\\'); std::putchar(c); }
        else if (c < 0x20) std::printf("\\u%04x", c);
        else std::putchar(c);
    }
    std::putchar('"');
}
static void Tiers(const std::vector<TierEntry>& e)
{
    std::printf("[");
    for (size_t i = 0; i < e.size(); ++i) { std::printf(i ? ",[" : "["); Str(e[i].id); std::printf(",%d]", e[i].tier); }
    std::printf("]");
}
static void Strings(const std::vector<std::string>& e)
{
    std::printf("[");
    for (size_t i = 0; i < e.size(); ++i) { if (i) std::printf(","); Str(e[i]); }
    std::printf("]");
}
static bool LongDigitRun(const std::string& t)
{
    int run = 0;
    for (char c : t) { run = (c >= '0' && c <= '9') ? run + 1 : 0; if (run >= 10) return true; }
    return false;
}
static const char kAlpha[] = "0123456789:=;ab -+\t";
static std::string RandomText(int maxLen)
{
    for (;;)
    {
        std::string t; int n = (int)(R() % (unsigned)(maxLen + 1));
        for (int i = 0; i < n; ++i) t += kAlpha[R() % (sizeof kAlpha - 1)];
        if (!LongDigitRun(t)) return t;
    }
}
static const char* kIds[] = { "weapon.mace", "armour.warlord.cloak", "a", "b:c", "x=y", "s;t", "blueprint.arena1", "" };
static std::string RandomId() { return kIds[R() % 8]; }
static std::string ValidTiers(int n)
{
    std::vector<TierEntry> e;
    for (int i = 0; i < n; ++i) { std::string id = RandomId(); if (id.empty()) id = "z"; e.push_back(TierEntry(id, (int)(R() % 6))); }
    return EncodeTiers(e);
}
static std::string ValidUnlocks(int n)
{
    std::vector<std::string> e;
    for (int i = 0; i < n; ++i) { std::string id = RandomId(); if (id.empty()) id = "z"; e.push_back(id); }
    return EncodeUnlocks(e);
}
static std::string Perturb(std::string t)
{
    int kind = (int)(R() % 6);
    if (t.empty() || kind == 0) return t;
    size_t at = R() % t.size();
    if (kind == 1) t.erase(at, 1);
    else if (kind == 2) t.insert(at, 1, kAlpha[R() % (sizeof kAlpha - 1)]);
    else if (kind == 3) t[at] = kAlpha[R() % (sizeof kAlpha - 1)];
    else if (kind == 4) t = t.substr(0, at);
    else t += kAlpha[R() % (sizeof kAlpha - 1)];
    return LongDigitRun(t) ? std::string("1:a=1;") : t;
}
int main()
{
    std::printf("[\n");
    // --- dt
    const char* fixed[] = { "", "1:a=0;", "1:a=0", "0:=1;", ":a=1;", "1:a=-1;", "1:a=-0;", "1:a=;", "1:a= 3;", "1:a=+3;",
        "1:a=3x;", "1:a=x;", "2:ab=1;1:a=2;2:ab=3;", "3:a=b=1;", "3:a;b=2;", "5:a=1;", "1:a1;", "1:a=1;;", "01:a=1;", "1x:a=1;",
        "1:a=\t7;", "1:a=- 1;", "1:a=1;2:", "1:a", "1", "999999999:a=1;", "1:a=999999999;", "1:a=-999999999;" };
    for (int c = 0; c < 1228; ++c)
    {
        std::string text;
        if (c < 28) text = fixed[c];
        else if (c % 3 == 0) text = RandomText(24);
        else text = Perturb(ValidTiers(1 + (int)(R() % 4)));
        std::vector<TierEntry> e; e.push_back(TierEntry("marker", 9));
        bool ok = DecodeTiers(text, e);
        std::printf("%s{\"k\":\"dt\",\"text\":", Sep()); Str(text); std::printf(",\"ok\":%d,\"out\":", ok ? 1 : 0); Tiers(e);
        std::printf(",\"enc\":"); Str(EncodeTiers(e)); std::printf("}\n");
    }
    // --- du
    const char* ufixed[] = { "", "1:a;", "1:a", "0:;", ":a;", "1:a;1:a;", "2:a;b;", "1:;;", "3:a;b;", "1:a;2:b", "01:a;", "1x:a;",
        "999999999:a;", "1:a;;" };
    for (int c = 0; c < 614; ++c)
    {
        std::string text;
        if (c < 14) text = ufixed[c];
        else if (c % 3 == 0) text = RandomText(20);
        else text = Perturb(ValidUnlocks(1 + (int)(R() % 4)));
        std::vector<std::string> e; e.push_back("marker");
        bool ok = DecodeUnlocks(text, e);
        std::printf("%s{\"k\":\"du\",\"text\":", Sep()); Str(text); std::printf(",\"ok\":%d,\"out\":", ok ? 1 : 0); Strings(e);
        std::printf(",\"enc\":"); Str(EncodeUnlocks(e)); std::printf("}\n");
    }
    // --- et
    for (int c = 0; c < 100; ++c)
    {
        std::vector<TierEntry> e; std::vector<std::string> u;
        int n = (int)(R() % 4);
        for (int i = 0; i < n; ++i) { e.push_back(TierEntry(RandomId(), (int)(R() % 2000001) - 1000000)); u.push_back(RandomId()); }
        std::printf("%s{\"k\":\"et\",\"tiers\":", Sep()); Tiers(e); std::printf(",\"unl\":"); Strings(u);
        std::printf(",\"te\":"); Str(EncodeTiers(e)); std::printf(",\"ue\":"); Str(EncodeUnlocks(u)); std::printf("}\n");
    }
    // --- op
    for (int c = 0; c < 300; ++c)
    {
        std::vector<TierEntry> e; std::vector<std::string> u;
        int n = 1 + (int)(R() % 12);
        std::printf("%s{\"k\":\"op\",\"ops\":[", Sep());
        for (int i = 0; i < n; ++i)
        {
            int op = (int)(R() % 6);
            std::string id = RandomId();
            int a = (int)(R() % 7) - 2, b = (int)(R() % 7) - 2;
            int r = 0;
            if (op == 0) r = HighestTier(e, id);
            else if (op == 1) r = SetHighestTier(e, id, a, b) ? 1 : 0;
            else if (op == 2) r = RestoreHighestTier(e, id, a) ? 1 : 0;
            else if (op == 3) r = HasUnlock(u, id) ? 1 : 0;
            else if (op == 4) r = AddUnlock(u, id) ? 1 : 0;
            else r = RemoveUnlock(u, id) ? 1 : 0;
            std::printf(i ? ",[%d," : "[%d,", op); Str(id); std::printf(",%d,%d,%d]", a, b, r);
        }
        std::printf("],\"tiers\":"); Tiers(e); std::printf(",\"unl\":"); Strings(u); std::printf("}\n");
    }
    // --- lb
    const char* names[] = { "", "Beep", "Ruka", "Beep II" };
    const float mmrs[] = { 100.0f, 0.0f, -12.5f, 1234.5678f, 99.99f };
    for (int c = 0; c < 300; ++c)
    {
        LeaderboardData::Collections data;
        int n = 1 + (int)(R() % 12);
        std::printf("%s{\"k\":\"lb\",\"ops\":[", Sep());
        for (int i = 0; i < n; ++i)
        {
            int op = (int)(R() % 9);
            std::string id = RandomId();
            std::string name = names[R() % 4];
            int kind = (int)(R() % 2);
            float mmr = mmrs[R() % 5];
            int a, b;
            int pick = (int)(R() % 4);
            if (pick == 0) { a = 2147483647 - (int)(R() % 3); b = (int)(R() % 4); }
            else if (pick == 1) { a = -(int)(R() % 5); b = 2147483647 - (int)(R() % 3); }
            else { a = (int)(R() % 7) - 2; b = (int)(R() % 7) - 2; }
            if (op <= 2) LeaderboardData::ApplyCompetitiveResult(data, (LeaderboardData::Kind)kind, id, name, mmr, a, b);
            else if (op <= 4) LeaderboardData::AddMarks(data, id, name, a);
            else if (op == 5) LeaderboardData::EnsureStanding(data, (LeaderboardData::Kind)kind, id, name).wins = a;   // a write through the returned row
            else if (op == 6) LeaderboardData::EnsureProgression(data, id, name).marks = a;
            else if (op == 7) { a = (R() % 4 == 0) ? 1 : 0; if (a) LeaderboardData::ResetFighters(data); }   // a = whether it reset
            else data.factionUnlocks.push_back(id);
            std::printf(i ? ",[%d,%d," : "[%d,%d,", op, kind); Str(id); std::printf(","); Str(name);
            std::printf(",%.9g,%d,%d]", mmr, a, b);
        }
        std::printf("]");
        for (int k = 0; k < 2; ++k)
        {
            const std::vector<LeaderboardData::Standing>& rows = LeaderboardData::Standings(data, (LeaderboardData::Kind)k);
            std::printf(k == 0 ? ",\"player\":[" : ",\"town\":[");
            for (size_t i = 0; i < rows.size(); ++i)
            {
                std::printf(i ? ",[" : "["); Str(rows[i].id); std::printf(","); Str(rows[i].name);
                std::printf(",%.9g,%d,%d,%d]", rows[i].mmr, rows[i].wins, rows[i].losses, rows[i].matches);
            }
            std::printf("]");
        }
        std::printf(",\"prog\":[");
        for (size_t i = 0; i < data.progression.size(); ++i)
        {
            std::printf(i ? ",[" : "["); Str(data.progression[i].id); std::printf(","); Str(data.progression[i].name);
            std::printf(",%d,", data.progression[i].marks); Tiers(data.progression[i].rewardTiers); std::printf("]");
        }
        std::printf("],\"fu\":"); Strings(data.factionUnlocks); std::printf("}\n");
    }
    // --- id
    const char* ids[] = { "53-Proving Grounds.mod", "57-Proving Grounds.mod", "272-Proving Grounds.mod", "273-Proving Grounds.mod",
        "53-Proving Grounds.mo", "53-Proving Grounds.modx", "", "272", "45-Proving Grounds.mod", 0 };
    for (int i = 0; i < 10; ++i)
    {
        std::printf("%s{\"k\":\"id\",\"id\":", Sep());
        if (ids[i]) Str(ids[i]); else std::printf("null");
        std::printf(",\"lb\":%d,\"kind\":%d}\n", LeaderboardData::IsLeaderboardId(ids[i]) ? 1 : 0, (int)LeaderboardData::KindForId(ids[i]));
    }
    // --- ver
    for (int v = -1; v <= 12; ++v)
        std::printf("%s{\"k\":\"ver\",\"v\":%d,\"reset\":%d}\n", Sep(), v, LeaderboardData::ShouldResetFighters(v) ? 1 : 0);
    std::printf("%s{\"k\":\"fm\",\"t0\":%d,\"t1\":%d}\n", Sep(), (int)LeaderboardData::ForMatch(false), (int)LeaderboardData::ForMatch(true));
    const int at[][2] = { { 0, 0 }, { 5, -3 }, { 5, 3 }, { 2147483647, 1 }, { 2147483646, 1 }, { 2147483640, 7 }, { 2147483640, 8 },
        { -5, 3 }, { -2147483647 - 1, 2147483647 }, { 0, 2147483647 }, { 1, 2147483647 }, { 7, -2147483647 - 1 } };
    for (auto& p : at)
        std::printf("%s{\"k\":\"at\",\"v\":%d,\"inc\":%d,\"r\":%d}\n", Sep(), p[0], p[1], LeaderboardData::AwardedTotal(p[0], p[1]));
    std::printf("]\n");
    return 0;
}
