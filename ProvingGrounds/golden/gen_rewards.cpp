// sources: ArenaRewards.cpp pgconfig_stub.cpp
// ArenaRewards: the whole catalogue and every public function, as JSON lines in one array; each line has "k" (what it is), "m" (the
// marks multiplier the catalogue was stamped with) plus inputs and outputs.
// ApplyPriceOverrides() stamps the static tables IN PLACE (golden/README.md), so one process cannot stamp twice: main() forks one
// child per multiplier (0.5, 1, 2.5); each child sets PGConfigStub_SetMarksMultiplier, calls ApplyPriceOverrides once and prints its
// cases. The parent prints the brackets and waits for each child in turn, so the output order is fixed. gen.sh runs one binary with
// no arguments; this needs nothing else from it.
//   "rw"   kRewards[piece][tier] for every valid (piece, tier), read through Get (the weapons' unused Standard slots cannot be
//          reached through Get, which substitutes the first tier; the C# test checks those empty rows by hand)
//   "gi"   kGeneralRewards via GetGeneral, items -2..9
//   "li"   GetLicence(set) for set -2..5 ("w": 0), and the weapon licence ("w": 1, "set": 6)
//   "pf"   per-piece functions, piece -2..26: TiersFor, FirstTier, IsWeapon, SetFor, RowFor, StableId
//   "sr"   SetForRow, row -2..5
//   "gt"   Get(piece, tier) -> the row it resolves to (piece, tier, cost), piece -2..26 x tier -2..6
//   "vt"   IsValidTier, piece -2..26 x tier -2..6
//   "nt"   NextTier / IsComplete / HighestClaimedTier, piece -2..26 x highestTier -3..6 and random
//   "cc"   CanClaim, random
//   "ro"   ResolveOffer, every piece x highestTier -2..5 x the four inventory flags, plus random
//   "cl"   CanPurchaseLicence / CanPurchaseWeaponLicence, random
//   "gv"   IsGeneralVisible, item -2..9 x flag
//   "rc"   ReplaceCost over -5..520 and edge values (INT_MAX itself is signed overflow in (fullCost + 1) / 2: not printed)
//   "sm"   PGConfig::ScaleMarks for edge marks at this multiplier (stub = PGConfig.cpp:931-943 verbatim)
// Only children with m == 1 print the multiplier-independent kinds (pf, sr, vt, gv, rc) to keep the file small; costs-dependent
// kinds are printed for every multiplier.
#include "ArenaRewards.h"
#include "pgconfig_stub.h"
#include <climits>
#include <cstdio>
#include <sys/wait.h>
#include <unistd.h>
using namespace ArenaRewards;
static unsigned s = 77031u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }
// JSON string or null (no catalogue string holds a quote or backslash, but escape them anyway)
static void Str(const char* v)
{
    if (!v) { std::printf("null"); return; }
    std::putchar('"');
    for (const char* p = v; *p; ++p) { if (*p == '"' || *p == '\\') std::putchar('\\'); std::putchar(*p); }
    std::putchar('"');
}
static void RewardFields(const Reward& r)
{
    std::printf("\"rp\":%d,\"rt\":%d,\"pn\":", (int)r.piece, (int)r.tier); Str(r.pieceName);
    std::printf(",\"tn\":"); Str(r.tierName);
    std::printf(",\"sid\":"); Str(r.stringId);
    std::printf(",\"mdl\":"); Str(r.modelStringId);
    std::printf(",\"mf\":"); Str(r.manufacturerStringId);
    std::printf(",\"q\":%d,\"cost\":%d", r.qualityLevel, r.cost);
}
static void Child(double m, bool independent)
{
    PGConfigStub_SetMarksMultiplier(m);
    ApplyPriceOverrides();
    char mm[32]; std::snprintf(mm, sizeof mm, "%.9g", m);
    // --- rw: every valid row through Get; the invalid weapon Standard rows cannot be reached through Get (it substitutes the
    // first tier), so they are not printed here: the C# test checks those empty rows by hand.
    for (int p = 0; p < (int)PieceCount; ++p)
        for (int t = 0; t < (int)TierMaxCount; ++t)
        {
            if (!IsValidTier((Piece)p, (Tier)t)) continue;
            std::printf("%s{\"k\":\"rw\",\"m\":%s,\"p\":%d,\"t\":%d,", Sep(), mm, p, t);
            RewardFields(Get((Piece)p, (Tier)t)); std::printf("}\n");
        }
    // --- gi
    for (int i = -2; i <= 9; ++i)
    {
        const GeneralReward& g = GetGeneral((GeneralItem)i);
        std::printf("%s{\"k\":\"gi\",\"m\":%s,\"i\":%d,\"item\":%d,\"sid\":", Sep(), mm, i, (int)g.item); Str(g.stableId);
        std::printf(",\"name\":"); Str(g.name);
        std::printf(",\"str\":"); Str(g.stringId);
        std::printf(",\"bp\":"); Str(g.blueprintResearchStringId);
        std::printf(",\"cost\":%d,\"ch\":%d,\"rep\":%d,\"pre\":", g.cost, g.charges, g.repeatable ? 1 : 0); Str(g.prerequisiteId);
        std::printf(",\"gr\":"); Str(g.grantsUnlockId); std::printf("}\n");
    }
    // --- li
    for (int set = -2; set <= 6; ++set)
    {
        const bool weapon = set == 6;
        const Licence& l = weapon ? GetWeaponLicence() : GetLicence((ArmourSet)set);
        std::printf("%s{\"k\":\"li\",\"m\":%s,\"w\":%d,\"set\":%d,\"ls\":%d,\"sid\":", Sep(), mm, weapon ? 1 : 0, set, (int)l.set);
        Str(l.stableId); std::printf(",\"name\":"); Str(l.name);
        std::printf(",\"cost\":%d,\"pre\":", l.cost); Str(l.prerequisiteId); std::printf("}\n");
    }
    // --- gt
    for (int p = -2; p <= 26; ++p)
        for (int t = -2; t <= 6; ++t)
        {
            const Reward& r = Get((Piece)p, (Tier)t);
            std::printf("%s{\"k\":\"gt\",\"m\":%s,\"p\":%d,\"t\":%d,\"rp\":%d,\"rt\":%d,\"cost\":%d}\n", Sep(), mm, p, t,
                (int)r.piece, (int)r.tier, r.cost);
        }
    // --- nt (structured)
    for (int p = -2; p <= 26; ++p)
        for (int h = -3; h <= 6; ++h)
            std::printf("%s{\"k\":\"nt\",\"m\":%s,\"p\":%d,\"h\":%d,\"next\":%d,\"done\":%d,\"hi\":%d}\n", Sep(), mm, p, h,
                (int)NextTier(h, (Piece)p), IsComplete(h, (Piece)p) ? 1 : 0, (int)HighestClaimedTier(h, (Piece)p));
    // --- nt (random highestTier, incl. large values; INT_MAX excluded: highestTier + 1 overflows)
    for (int c = 0; c < 200; ++c)
    {
        int p = (int)(R() % 29) - 2;
        int h = (int)(R() % 2000001) - 1000000;
        if (c % 10 == 0) h = INT_MIN;
        std::printf("%s{\"k\":\"nt\",\"m\":%s,\"p\":%d,\"h\":%d,\"next\":%d,\"done\":%d,\"hi\":%d}\n", Sep(), mm, p, h,
            (int)NextTier(h, (Piece)p), IsComplete(h, (Piece)p) ? 1 : 0, (int)HighestClaimedTier(h, (Piece)p));
    }
    // --- cc
    for (int c = 0; c < 500; ++c)
    {
        int p = (int)(R() % 29) - 2;
        int h = (int)(R() % 8) - 2;
        int t = (int)(R() % 8) - 2;
        int marks = (int)(R() % 520) - 20;
        if (c % 7 == 0 && IsValidTier((Piece)p, (Tier)t)) marks = Get((Piece)p, (Tier)t).cost + (int)(R() % 3) - 1;   // at the price
        std::printf("%s{\"k\":\"cc\",\"m\":%s,\"p\":%d,\"h\":%d,\"t\":%d,\"marks\":%d,\"ok\":%d}\n", Sep(), mm, p, h, t, marks,
            CanClaim(h, (Piece)p, (Tier)t, marks) ? 1 : 0);
    }
    // --- ro (structured)
    for (int p = -1; p <= 25; ++p)
        for (int h = -2; h <= 5; ++h)
            for (int f = 0; f < 4; ++f)
            {
                Offer o = ResolveOffer(h, (Piece)p, (f & 1) != 0, (f & 2) != 0);
                std::printf("%s{\"k\":\"ro\",\"m\":%s,\"p\":%d,\"h\":%d,\"ex\":%d,\"any\":%d,\"kind\":%d,\"tier\":%d,\"cost\":%d}\n",
                    Sep(), mm, p, h, f & 1, (f >> 1) & 1, (int)o.kind, (int)o.tier, o.cost);
            }
    // --- ro (random tier histories and inventories)
    for (int c = 0; c < 600; ++c)
    {
        int p = (int)(R() % 25);
        int h = (int)(R() % 10) - 3;
        bool ex = (R() % 2) != 0;
        bool any = (R() % 2) != 0;
        Offer o = ResolveOffer(h, (Piece)p, ex, any);
        std::printf("%s{\"k\":\"ro\",\"m\":%s,\"p\":%d,\"h\":%d,\"ex\":%d,\"any\":%d,\"kind\":%d,\"tier\":%d,\"cost\":%d}\n",
            Sep(), mm, p, h, ex ? 1 : 0, any ? 1 : 0, (int)o.kind, (int)o.tier, o.cost);
    }
    // --- cl (licence chain checks)
    for (int c = 0; c < 600; ++c)
    {
        int set = (int)(R() % 7) - 2;
        bool un = (R() % 3) == 0;
        bool pre = (R() % 3) != 0;
        int marks = (int)(R() % 400) - 20;
        if (c % 5 == 0) marks = (set >= 0 && set <= 2 ? GetLicence((ArmourSet)set).cost : GetWeaponLicence().cost) + (int)(R() % 3) - 1;
        std::printf("%s{\"k\":\"cl\",\"m\":%s,\"set\":%d,\"un\":%d,\"pre\":%d,\"marks\":%d,\"ok\":%d,\"wok\":%d}\n", Sep(), mm, set,
            un ? 1 : 0, pre ? 1 : 0, marks, CanPurchaseLicence((ArmourSet)set, un, pre, marks) ? 1 : 0,
            CanPurchaseWeaponLicence(un, marks) ? 1 : 0);
    }
    // --- sm
    const int sms[] = { 0, 1, 2, 3, 5, 7, 15, 25, 99, 101, 150, -1, -5, -100, 1000000, 858993459, 858993460, 2147483647, INT_MIN };
    for (int v : sms)
        std::printf("%s{\"k\":\"sm\",\"m\":%s,\"v\":%d,\"r\":%d}\n", Sep(), mm, v, PGConfig::ScaleMarks(v));
    if (!independent) return;
    // --- pf
    for (int p = -2; p <= 26; ++p)
    {
        std::printf("%s{\"k\":\"pf\",\"p\":%d,\"tiers\":%d,\"first\":%d,\"wpn\":%d,\"set\":%d,\"row\":%d,\"sid\":", Sep(), p,
            TiersFor((Piece)p), (int)FirstTier((Piece)p), IsWeapon((Piece)p) ? 1 : 0, (int)SetFor((Piece)p), (int)RowFor((Piece)p));
        Str(StableId((Piece)p)); std::printf("}\n");
    }
    // --- sr
    for (int r = -2; r <= 5; ++r)
        std::printf("%s{\"k\":\"sr\",\"r\":%d,\"set\":%d}\n", Sep(), r, (int)SetForRow((ArmourRow)r));
    // --- vt
    for (int p = -2; p <= 26; ++p)
        for (int t = -2; t <= 6; ++t)
            std::printf("%s{\"k\":\"vt\",\"p\":%d,\"t\":%d,\"ok\":%d}\n", Sep(), p, t, IsValidTier((Piece)p, (Tier)t) ? 1 : 0);
    // --- gv
    for (int i = -2; i <= 9; ++i)
        for (int f = 0; f < 2; ++f)
            std::printf("%s{\"k\":\"gv\",\"i\":%d,\"a1\":%d,\"ok\":%d}\n", Sep(), i, f, IsGeneralVisible((GeneralItem)i, f != 0) ? 1 : 0);
    // --- rc
    for (int v = -5; v <= 520; ++v)
        std::printf("%s{\"k\":\"rc\",\"v\":%d,\"r\":%d}\n", Sep(), v, ReplaceCost(v));
    const int rcs[] = { INT_MIN, -1000000, 1000001, 2147483645, 2147483646 };
    for (int v : rcs)
        std::printf("%s{\"k\":\"rc\",\"v\":%d,\"r\":%d}\n", Sep(), v, ReplaceCost(v));
}
int main()
{
    std::printf("[\n");
    const double ms[] = { 0.5, 1.0, 2.5 };
    for (int i = 0; i < 3; ++i)
    {
        std::fflush(stdout);
        pid_t pid = fork();
        if (pid < 0) return 1;
        if (pid == 0)
        {
            first = i == 0;   // only the very first line of the array has no leading comma
            s = 77031u + (unsigned)i;
            Child(ms[i], ms[i] == 1.0);
            std::fflush(stdout);
            _exit(0);
        }
        int status = 0;
        if (waitpid(pid, &status, 0) != pid || !WIFEXITED(status) || WEXITSTATUS(status) != 0) return 1;
    }
    std::printf("]\n");
    return 0;
}
