// sources:
// (header-only: LeaderboardLayout.h, ResultsLayout.h, RewardsLayout.h and TownCardLayout.h are inline functions with no .cpp, so nothing is linked)
// Every public function of the four layout headers, as JSON lines in one array. Each line has "k" plus its inputs and outputs. All ints.
//   meta  the probe lists used by the grid kinds: idx (indices for Column/Row/Left) and cnt (counts for ContentWidth)
//   fc  LeaderboardLayout::FitColumns(w, g, min[5])              -> st (stacked), l[5] (left), wd[5] (width)
//   co  LeaderboardLayout::ClampOffset(off, ch, vh)              -> o
//   fv  LeaderboardLayout::FitVertical(h, g, body, ctx, close)   -> o[7] = contextHeight, headingTop, headingHeight, scrollTop,
//                                                                    scrollHeight, closeTop, closeHeight
//   cm  LeaderboardLayout::FitCeremonialMetrics(body, g)         -> o[5] = championPortrait, otherPortrait, rowPortrait, rowGap, edge
//   tr  LeaderboardLayout::FitTitleRails(w, g, title)            -> o[5] = titleLeft, titleWidth, leftWidth, rightLeft, rightWidth
//   fp  LeaderboardLayout::FitPodium(w, g, n, champ, other)      -> st, cnt, ch (contentHeight), s[3][6] = per slot sourceIndex, rank,
//                                                                    left, top, width, portrait (all three slots, unused ones zero)
//   ce  ResultsLayout::FitCeremony(w, g, body, n, title)         -> m[5] (metrics, as cm), t[5] (title rails, as tr), st, cnt, ch, s[3][6]
//   pi  ResultsLayout::PortraitInitialUtf8(bytes)                -> out. in/out are HEX strings of the bytes ("in" is null for a null
//                                                                    pointer); the C string ends at the first 00 byte of "in", or at its end
//   mn / mx  RewardsLayout::Min / Max(a, b)                      -> o
//   ss  RewardsLayout::ShouldStack(cw, body)                     -> o (0/1)
//   fw  RewardsLayout::FighterWidth(cw, body)                    -> o
//   gc  RewardsLayout::GridColumns(w, minCard, g, maxCols)       -> o
//   fr  RewardsLayout::FillRowHeight(vh, rows, g, minH)          -> o
//   ri  RewardsLayout::RarityIndex(tier, complete)               -> o
//   cr  RewardsLayout::CompactRowHeight(roomy, minH)             -> o
//   fs  RewardsLayout::FightSupplyColumns(n)                     -> o
//   tf  TownCardLayout::Fit(avail, min, sp)                      -> c, wd, gp (columns, width, gap) + grid probes
//   ts  TownCardLayout::SingleRow(avail, min, sp, n)             -> c, wd, gp + grid probes
//   tg  a TownCardLayout::Grid built directly {c, wd, gp}        -> grid probes only
//   tv  TownCardLayout::ClampViewOffset(off, content, view)      -> o
// Grid probes: "p" = [Column(i), Row(i), Left(i)] for each i in meta.idx, "cw" = ContentWidth(n) for each n in meta.cnt.
// Every input stays within +-1,000,000 and every product of two inputs is below 2^31: signed overflow is undefined in C++ and wraps
// in C#, so no overflow is covered. A Grid with columns == 0 (a division by zero in C++, an exception in C#) is never produced.
#include "ResultsLayout.h"
#include "RewardsLayout.h"
#include "TownCardLayout.h"
#include <cstdio>
#include <string>

static unsigned s = 20261111u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }   // 24 bits; never call twice in one expression
static int Range(int lo, int hi) { unsigned span = (unsigned)(hi - lo + 1); unsigned r = R(); return lo + (int)(r % span); }
static bool first = true;
static void Next() { std::fputs(first ? "" : ",\n", stdout); first = false; }
static void Ints(const int* v, int n)
{
    std::putchar('[');
    for (int i = 0; i < n; ++i) std::printf(i ? ",%d" : "%d", v[i]);
    std::putchar(']');
}

// ---- input pools ------------------------------------------------------------------------------------------------------------------
static const int kEdge[] = { -1000000, -65536, -4096, -721, -100, -24, -8, -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16, 20,
    24, 32, 48, 64, 96, 100, 159, 160, 161, 719, 720, 721, 800, 1000, 1024, 3840, 65536, 999999, 1000000 };
static int Edge() { return kEdge[R() % (sizeof kEdge / sizeof kEdge[0])]; }
// window sizes the UI runs at, and fractions of them (a half window, a third, 60 %, a quarter, two thirds, minus a margin)
static const int kWinW[9] = { 800, 1024, 1280, 1366, 1600, 1920, 2560, 3440, 3840 };
static const int kWinH[9] = { 600, 768, 720, 768, 900, 1080, 1440, 1440, 2160 };
static int Fraction(int w)
{
    unsigned m = R() % 7;
    switch (m)
    {
    case 0: return w;
    case 1: return w / 2;
    case 2: return w / 3;
    case 3: return (int)(w * 0.6);
    case 4: { int margin = Range(0, 120); return w - margin; }
    case 5: return w / 4;
    default: return w * 2 / 3;
    }
}
static int WinW() { unsigned i = R() % 9; return Fraction(kWinW[i]); }
static int WinH() { unsigned i = R() % 9; return Fraction(kWinH[i]); }
// NativeUI::Spacing = max(4, fontHeight / 2); BodyHeight = max(1, max(fontHeight, textHeight))
static const int kFont[18] = { 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 22, 24, 28, 32, 40, 48 };
static int Font() { unsigned i = R() % 18; return kFont[i]; }
static int GapFor(int f) { int h = f / 2; return h > 4 ? h : 4; }
static int BodyFor(int f)
{
    unsigned m = R() % 3;
    int extra = 0;
    if (m == 0) extra = Range(0, 6);
    return f + extra;
}

// ---- LeaderboardLayout ------------------------------------------------------------------------------------------------------------
static void CaseColumns(int w, int g, const int* min)
{
    LeaderboardLayout::Columns c = LeaderboardLayout::FitColumns(w, g, min);
    Next();
    std::printf("{\"k\":\"fc\",\"w\":%d,\"g\":%d,\"min\":", w, g);
    Ints(min, 5);
    std::printf(",\"st\":%d,\"l\":", c.stacked ? 1 : 0);
    Ints(c.left, 5);
    std::printf(",\"wd\":");
    Ints(c.width, 5);
    std::printf("}");
}
static void CaseClampOffset(int off, int ch, int vh)
{
    Next();
    std::printf("{\"k\":\"co\",\"off\":%d,\"ch\":%d,\"vh\":%d,\"o\":%d}", off, ch, vh, LeaderboardLayout::ClampOffset(off, ch, vh));
}
static void CaseVertical(int h, int g, int body, int ctx, int close)
{
    LeaderboardLayout::Vertical v = LeaderboardLayout::FitVertical(h, g, body, ctx, close);
    int o[7] = { v.contextHeight, v.headingTop, v.headingHeight, v.scrollTop, v.scrollHeight, v.closeTop, v.closeHeight };
    Next();
    std::printf("{\"k\":\"fv\",\"h\":%d,\"g\":%d,\"body\":%d,\"ctx\":%d,\"close\":%d,\"o\":", h, g, body, ctx, close);
    Ints(o, 7);
    std::printf("}");
}
static void PrintMetrics(const LeaderboardLayout::CeremonialMetrics& m)
{
    int o[5] = { m.championPortrait, m.otherPortrait, m.rowPortrait, m.rowGap, m.edge };
    Ints(o, 5);
}
static void CaseMetrics(int body, int g)
{
    Next();
    std::printf("{\"k\":\"cm\",\"body\":%d,\"g\":%d,\"o\":", body, g);
    PrintMetrics(LeaderboardLayout::FitCeremonialMetrics(body, g));
    std::printf("}");
}
static void PrintTitle(const LeaderboardLayout::TitleRails& t)
{
    int o[5] = { t.titleLeft, t.titleWidth, t.leftWidth, t.rightLeft, t.rightWidth };
    Ints(o, 5);
}
static void CaseTitle(int w, int g, int title)
{
    Next();
    std::printf("{\"k\":\"tr\",\"w\":%d,\"g\":%d,\"title\":%d,\"o\":", w, g, title);
    PrintTitle(LeaderboardLayout::FitTitleRails(w, g, title));
    std::printf("}");
}
static void PrintPodium(const LeaderboardLayout::Podium& p)
{
    std::printf("\"st\":%d,\"cnt\":%d,\"ch\":%d,\"s\":[", p.stacked ? 1 : 0, p.count, p.contentHeight);
    for (int i = 0; i < 3; ++i)
    {
        const LeaderboardLayout::PodiumSlot& t = p.slots[i];
        int o[6] = { t.sourceIndex, t.rank, t.left, t.top, t.width, t.portrait };
        if (i) std::putchar(',');
        Ints(o, 6);
    }
    std::putchar(']');
}
static void CasePodium(int w, int g, int n, int champ, int other)
{
    Next();
    std::printf("{\"k\":\"fp\",\"w\":%d,\"g\":%d,\"n\":%d,\"champ\":%d,\"other\":%d,", w, g, n, champ, other);
    PrintPodium(LeaderboardLayout::FitPodium(w, g, n, champ, other));
    std::printf("}");
}
static void CaseCeremony(int w, int g, int body, int n, int title)
{
    ResultsLayout::Ceremony c = ResultsLayout::FitCeremony(w, g, body, n, title);
    Next();
    std::printf("{\"k\":\"ce\",\"w\":%d,\"g\":%d,\"body\":%d,\"n\":%d,\"title\":%d,\"m\":", w, g, body, n, title);
    PrintMetrics(c.metrics);
    std::printf(",\"t\":");
    PrintTitle(c.title);
    std::printf(",");
    PrintPodium(c.podium);
    std::printf("}");
}

// ---- ResultsLayout::PortraitInitialUtf8 -----------------------------------------------------------------------------------------------
static std::string Hex(const std::string& b)
{
    static const char d[] = "0123456789abcdef";
    std::string o;
    for (unsigned char c : b) { o += d[c >> 4]; o += d[c & 15]; }
    return o;
}
static std::string Bytes(const char* hex)   // "c3a9" -> two bytes
{
    std::string o;
    for (int i = 0; hex[i]; i += 2)
    {
        int v = 0;
        for (int j = 0; j < 2; ++j)
        {
            char c = hex[i + j];
            v = v * 16 + (c <= '9' ? c - '0' : c - 'a' + 10);
        }
        o += (char)v;
    }
    return o;
}
static void CasePortrait(const std::string& bytes)   // may hold 00 bytes: the C string ends at the first one
{
    std::string out = ResultsLayout::PortraitInitialUtf8(bytes.c_str());
    Next();
    std::printf("{\"k\":\"pi\",\"in\":\"%s\",\"out\":\"%s\"}", Hex(bytes).c_str(), Hex(out).c_str());
}
static void CaseNullPortrait()
{
    std::string out = ResultsLayout::PortraitInitialUtf8(nullptr);
    Next();
    std::printf("{\"k\":\"pi\",\"in\":null,\"out\":\"%s\"}", Hex(out).c_str());
}
static std::string Utf8Of(unsigned long cp)   // the textbook encoder, run past U+10FFFF and over the surrogates on purpose
{
    std::string o;
    if (cp < 0x80) o += (char)cp;
    else if (cp < 0x800) { o += (char)(0xc0 | (cp >> 6)); o += (char)(0x80 | (cp & 0x3f)); }
    else if (cp < 0x10000) { o += (char)(0xe0 | (cp >> 12)); o += (char)(0x80 | ((cp >> 6) & 0x3f)); o += (char)(0x80 | (cp & 0x3f)); }
    else { o += (char)(0xf0 | (cp >> 18)); o += (char)(0x80 | ((cp >> 12) & 0x3f)); o += (char)(0x80 | ((cp >> 6) & 0x3f)); o += (char)(0x80 | (cp & 0x3f)); }
    return o;
}
static char RandomByte() { unsigned r = R(); return (char)((r >> 4) & 0xff); }
static std::string RandomBytes()
{
    std::string b;
    unsigned mode = R() % 3;
    if (mode == 0)   // fully random, 0..6 bytes
    {
        int n = Range(0, 6);
        for (int i = 0; i < n; ++i) b += RandomByte();
    }
    else if (mode == 1)   // an interesting lead, then interesting followers, then a random tail
    {
        static const unsigned char leads[] = { 0x41, 0x7f, 0xc2, 0xdf, 0xe0, 0xe1, 0xec, 0xed, 0xee, 0xef, 0xf0, 0xf1, 0xf3, 0xf4, 0xf5,
            0xc0, 0xc1, 0x80, 0xbf, 0xff, 0xf8 };
        static const unsigned char follow[] = { 0x80, 0x8f, 0x90, 0x9f, 0xa0, 0xbf, 0xc0, 0x41, 0x00, 0xbe };
        unsigned li = R() % (sizeof leads);
        b += (char)leads[li];
        int k = Range(0, 4);
        for (int i = 0; i < k; ++i) { unsigned fi = R() % (sizeof follow); b += (char)follow[fi]; }
        int tail = Range(0, 2);
        for (int i = 0; i < tail; ++i) b += RandomByte();
    }
    else   // a well-formed encoding of a code point from around every rule's edge, sometimes with one byte corrupted, and a tail
    {
        static const unsigned long cps[] = { 0x24, 0x7f, 0x80, 0xa9, 0x7ff, 0x800, 0x9fff, 0xd7ff, 0xd800, 0xdbff, 0xdc00, 0xdfff,
            0xe000, 0xfffd, 0xfffe, 0xffff, 0x10000, 0x1f94a, 0x10ffff, 0x110000, 0x13ffff, 0x1fffff };
        unsigned ci = R() % (sizeof cps / sizeof cps[0]);
        b = Utf8Of(cps[ci]);
        unsigned corrupt = R() % 4;
        if (corrupt == 0) { int pos = Range(0, (int)b.size() - 1); b[pos] = RandomByte(); }
        int tail = Range(0, 3);
        for (int i = 0; i < tail; ++i) b += RandomByte();
    }
    return b;
}

// ---- RewardsLayout ----------------------------------------------------------------------------------------------------------------
static void CaseOne(const char* k, int a, int b, int o)
{
    Next();
    std::printf("{\"k\":\"%s\",\"a\":%d,\"b\":%d,\"o\":%d}", k, a, b, o);
}
static int Max(int a, int b) { return a > b ? a : b; }
static void CaseStack(int cw, int body)
{
    Next();
    std::printf("{\"k\":\"ss\",\"cw\":%d,\"body\":%d,\"o\":%d}", cw, body, RewardsLayout::ShouldStack(cw, body) ? 1 : 0);
}
static void CaseFighter(int cw, int body)
{
    Next();
    std::printf("{\"k\":\"fw\",\"cw\":%d,\"body\":%d,\"o\":%d}", cw, body, RewardsLayout::FighterWidth(cw, body));
}
static void CaseGridColumns(int w, int minCard, int g, int maxCols)
{
    Next();
    std::printf("{\"k\":\"gc\",\"w\":%d,\"min\":%d,\"g\":%d,\"max\":%d,\"o\":%d}", w, minCard, g, maxCols,
        RewardsLayout::GridColumns(w, minCard, g, maxCols));
}
static void CaseFillRow(int vh, int rows, int g, int minH)
{
    Next();
    std::printf("{\"k\":\"fr\",\"vh\":%d,\"rows\":%d,\"g\":%d,\"min\":%d,\"o\":%d}", vh, rows, g, minH,
        RewardsLayout::FillRowHeight(vh, rows, g, minH));
}
static void CaseRarity(int tier, bool complete)
{
    Next();
    std::printf("{\"k\":\"ri\",\"tier\":%d,\"complete\":%d,\"o\":%d}", tier, complete ? 1 : 0, RewardsLayout::RarityIndex(tier, complete));
}
static void CaseCompact(int roomy, int minH)
{
    Next();
    std::printf("{\"k\":\"cr\",\"roomy\":%d,\"min\":%d,\"o\":%d}", roomy, minH, RewardsLayout::CompactRowHeight(roomy, minH));
}
static void CaseSupply(int n)
{
    Next();
    std::printf("{\"k\":\"fs\",\"n\":%d,\"o\":%d}", n, RewardsLayout::FightSupplyColumns(n));
}

// ---- TownCardLayout ---------------------------------------------------------------------------------------------------------------
static const int kIdx[10] = { -6, -1, 0, 1, 2, 4, 5, 7, 12, 31 };
static const int kCnt[6] = { -2, 0, 1, 2, 3, 9 };
static void PrintProbes(const TownCardLayout::Grid& g)
{
    std::printf("\"p\":[");
    for (int i = 0; i < 10; ++i)
        std::printf(i ? ",[%d,%d,%d]" : "[%d,%d,%d]", g.Column(kIdx[i]), g.Row(kIdx[i]), g.Left(kIdx[i]));
    std::printf("],\"cw\":[");
    for (int i = 0; i < 6; ++i)
        std::printf(i ? ",%d" : "%d", g.ContentWidth(kCnt[i]));
    std::printf("]");
}
static void CaseFit(int avail, int min, int sp)
{
    TownCardLayout::Grid g = TownCardLayout::Fit(avail, min, sp);
    Next();
    std::printf("{\"k\":\"tf\",\"avail\":%d,\"min\":%d,\"sp\":%d,\"c\":%d,\"wd\":%d,\"gp\":%d,", avail, min, sp, g.columns, g.width, g.gap);
    PrintProbes(g);
    std::printf("}");
}
static void CaseSingleRow(int avail, int min, int sp, int n)
{
    TownCardLayout::Grid g = TownCardLayout::SingleRow(avail, min, sp, n);
    Next();
    std::printf("{\"k\":\"ts\",\"avail\":%d,\"min\":%d,\"sp\":%d,\"n\":%d,\"c\":%d,\"wd\":%d,\"gp\":%d,", avail, min, sp, n, g.columns, g.width, g.gap);
    PrintProbes(g);
    std::printf("}");
}
static void CaseGrid(int c, int wd, int gp)
{
    TownCardLayout::Grid g = { c, wd, gp };
    Next();
    std::printf("{\"k\":\"tg\",\"c\":%d,\"wd\":%d,\"gp\":%d,", c, wd, gp);
    PrintProbes(g);
    std::printf("}");
}
static void CaseViewOffset(int off, int content, int view)
{
    Next();
    std::printf("{\"k\":\"tv\",\"off\":%d,\"content\":%d,\"view\":%d,\"o\":%d}", off, content, view,
        TownCardLayout::ClampViewOffset(off, content, view));
}

int main()
{
    std::printf("[\n");
    first = false;
    std::printf("{\"k\":\"meta\",\"idx\":");
    Ints(kIdx, 10);
    std::printf(",\"cnt\":");
    Ints(kCnt, 6);
    std::printf("}");

    // fc: FitColumns. The UI passes minimum[c] = widest text of the column + gap, and the fighter column at least rowPortrait + gap + body * 8.
    for (int i = 0; i < 2200; ++i)
    {
        int w = 0, g = 0, min[5] = { 0, 0, 0, 0, 0 };
        unsigned mode = R() % 4;
        if (mode == 0 || mode == 1)
        {
            int f = Font();
            g = GapFor(f);
            int body = BodyFor(f);
            min[0] = f * Range(2, 4) + g;
            min[1] = Max(f * Range(5, 14) + g, 2 * body + g + body * 8);
            min[2] = f * Range(3, 6) + g;
            min[3] = f * Range(4, 8) + g;
            min[4] = f * Range(3, 6) + g;
            if (mode == 0) w = WinW();
            else   // required width, and one pixel either side of it
            {
                int required = g * 4;
                for (int c = 0; c < 5; ++c) required += min[c];
                w = required + Range(-2, 2);
            }
        }
        else if (mode == 2)
        {
            for (int c = 0; c < 5; ++c) min[c] = Range(-30, 500);
            g = Range(-10, 40);
            w = Range(-100, 3000);
        }
        else
        {
            for (int c = 0; c < 5; ++c) min[c] = Edge();
            g = Edge();
            w = Edge();
        }
        CaseColumns(w, g, min);
    }

    // co: ClampOffset
    for (int i = 0; i < 2200; ++i)
    {
        int off = 0, ch = 0, vh = 0;
        unsigned mode = R() % 4;
        if (mode == 0)
        {
            vh = Max(1, WinH() - Range(100, 300));
            ch = Range(0, 6000);
            off = Range(-100, ch + 100);
        }
        else if (mode == 1)   // content around the view, offset around the maximum
        {
            vh = Range(1, 1500);
            ch = vh + Range(-2, 2);
            int maximum = ch > vh ? ch - vh : 0;
            off = maximum + Range(-2, 2);
        }
        else if (mode == 2)
        {
            off = Range(-50, 200);
            ch = Range(-20, 200);
            vh = Range(-20, 200);
        }
        else
        {
            off = Edge();
            ch = Edge();
            vh = Edge();
        }
        CaseClampOffset(off, ch, vh);
    }

    // fv: FitVertical
    for (int i = 0; i < 2600; ++i)
    {
        int h = 0, g = 0, body = 0, ctx = 0, close = 0;
        unsigned mode = R() % 5;
        if (mode == 0)
        {
            int f = Font();
            g = GapFor(f);
            body = BodyFor(f);
            h = WinH();
            ctx = Range(0, 220);
            close = Range(20, 80);
        }
        else if (mode == 1)   // small everything: every clamp and the early return
        {
            h = Range(-5, 120);
            g = Range(-3, 24);
            body = Range(-3, 60);
            ctx = Range(-5, 100);
            close = Range(-5, 80);
        }
        else if (mode == 2)   // the intermediate values' own boundaries
        {
            h = Range(1, 400);
            g = Range(0, 24);
            close = Range(0, 100);
            int ch = close < h ? close : h;
            int ct = h - g - ch;
            if (ct < 0) ct = 0;
            int usable = ct > g ? ct - g : 0;
            ctx = usable / 4 + Range(-1, 1);
            int ctxc = ctx < usable / 4 ? ctx : usable / 4;
            int ht = 2 * g + ctxc;
            if (ht > usable) ht = usable;
            int room = usable - ht - g;
            body = room + Range(-1, 1);
        }
        else if (mode == 3)   // closeHeight around height
        {
            h = Range(1, 300);
            g = Range(0, 24);
            body = Range(1, 48);
            ctx = Range(0, 150);
            close = h + Range(-3, 3);
        }
        else
        {
            h = Edge();
            g = Edge();
            body = Edge();
            ctx = Edge();
            close = Edge();
        }
        CaseVertical(h, g, body, ctx, close);
    }

    // cm: FitCeremonialMetrics. Every body 0..50 with every gap 0..26 (each max() threshold falls inside), then random and edge values.
    for (int body = 0; body <= 50; ++body)
        for (int g = 0; g <= 26; ++g)
            CaseMetrics(body, g);
    for (int i = 0; i < 300; ++i)
    {
        int body = 0, g = 0;
        unsigned mode = R() % 3;
        if (mode == 0) { body = Range(-30, 400); g = Range(-30, 400); }
        else if (mode == 1) { body = Edge(); g = Edge(); }
        else { int f = Font(); body = BodyFor(f); g = GapFor(f); }
        CaseMetrics(body, g);
    }

    // tr: FitTitleRails
    for (int i = 0; i < 2800; ++i)
    {
        int w = 0, g = 0, title = 0;
        unsigned mode = R() % 5;
        if (mode == 0)
        {
            int f = Font();
            g = GapFor(f);
            w = WinW();
            title = Range(50, 1500);
        }
        else if (mode == 1)   // the title at the widest it may be, +-2
        {
            g = Range(0, 30);
            w = Range(1, 3000);
            int maximumTitle = w > 2 * g ? w - 2 * g : w;
            title = maximumTitle + Range(-2, 2);
        }
        else if (mode == 2)   // the rails vanish exactly when titleLeft reaches 2 * gap
        {
            g = Range(0, 30);
            w = Range(1, 3000);
            title = w - 4 * g - Range(-2, 3);
        }
        else if (mode == 3)
        {
            w = Range(-5, 100);
            g = Range(-3, 30);
            title = Range(-10, 100);
        }
        else
        {
            w = Edge();
            g = Edge();
            title = Edge();
        }
        CaseTitle(w, g, title);
    }

    // fp: FitPodium. Portraits come from FitCeremonialMetrics for the realistic rows, as ResultsUI passes them.
    for (int i = 0; i < 2600; ++i)
    {
        int w = 0, g = 0, n = 0, champ = 0, other = 0;
        unsigned mode = R() % 5;
        if (mode == 0)
        {
            int f = Font();
            g = GapFor(f);
            int body = BodyFor(f);
            LeaderboardLayout::CeremonialMetrics m = LeaderboardLayout::FitCeremonialMetrics(body, g);
            champ = m.championPortrait;
            other = m.otherPortrait;
            w = WinW();
            n = Range(0, 4);
        }
        else if (mode == 1 || mode == 2)   // the stacked threshold (mode 1) and the widest-cell cap (mode 2), +-1
        {
            int f = Font();
            g = GapFor(f);
            int body = BodyFor(f);
            LeaderboardLayout::CeremonialMetrics m = LeaderboardLayout::FitCeremonialMetrics(body, g);
            champ = m.championPortrait;
            other = m.otherPortrait;
            n = Range(1, 3);
            if (mode == 1) w = n * (champ + 8 * g) + (n - 1) * g + Range(-1, 1);
            else w = n * (champ + 20 * g) + (n - 1) * g + Range(-1, 1);
        }
        else if (mode == 3)
        {
            w = Range(-10, 400);
            g = Range(-3, 16);
            n = Range(-2, 5);
            champ = Range(-10, 120);
            other = Range(-10, 100);
        }
        else
        {
            w = Edge();
            g = Edge();
            n = Edge();
            champ = Edge();
            other = Edge();
        }
        CasePodium(w, g, n, champ, other);
    }

    // ce: FitCeremony (results ceremony: metrics + title rails + podium from one window)
    for (int i = 0; i < 2200; ++i)
    {
        int w = 0, g = 0, body = 0, n = 0, title = 0;
        unsigned mode = R() % 4;
        if (mode <= 1)
        {
            int f = Font();
            g = GapFor(f);
            body = BodyFor(f);
            w = WinW();
            n = Range(-1, 5);
            title = Range(0, 2 * w);
        }
        else if (mode == 2)
        {
            w = Range(-10, 600);
            g = Range(-3, 24);
            body = Range(-3, 60);
            n = Range(-2, 5);
            title = Range(-10, 700);
        }
        else
        {
            w = Edge();
            g = Edge();
            body = Edge();
            n = Edge();
            title = Edge();
        }
        CaseCeremony(w, g, body, n, title);
    }

    // pi: PortraitInitialUtf8
    CaseNullPortrait();
    CasePortrait("");
    static const char* const kExplicit[] = {
        "41", "7a", "7f", "01", "20", "00", "0041", "41ff", "4100ff", "00c3a9",
        "80", "bf", "c0", "c1", "c2", "c200", "c280", "c2bf", "c2c0", "c241", "c2c2", "dfbf", "dfbf41", "df80", "dfc0", "c3a9", "c3a962656e65",
        "e0", "e080", "e08080", "e09fbf", "e0a080", "e0bfbf", "e0c080", "e0a0", "e0a000", "e0a00041", "e0a041",
        "e1", "e180", "e18000", "e18080", "e180c0", "e1c080", "e18041", "e19fbf", "e1bfbf", "e697a5e69cac",
        "ec9fbf", "ec80c0", "ed", "ed80", "ed8080", "ed9fbf", "eda080", "edbfbf", "ee8080", "ef8080", "efbfbd", "efbfbe", "efbfbf", "efbf", "efbf00",
        "f0", "f080", "f08080", "f0808080", "f08fbfbf", "f0908080", "f09f94a5", "f09f9ea5", "f0bfbfbf", "f090", "f0908000", "f09080",
        "f1808080", "f18080", "f1808041", "f1bfbfbf", "f3808080", "f3bfbfbf", "f4808080", "f48fbfbf", "f4908080", "f4bfbfbf", "f48f",
        "f4", "f5808080", "f6808080", "f7bfbfbf", "f8808080", "f880808080", "fc", "fe", "ff", "ff41", "fe41", "f9", "fd8080808080",
        "e2829c", "e282", "e2", "ce91", "d0a4", "f09fa58a", "f09fa5", "f09f", "f0",
    };
    for (size_t i = 0; i < sizeof kExplicit / sizeof kExplicit[0]; ++i)
    {
        CasePortrait(Bytes(kExplicit[i]));
    }
    // every first byte, alone and followed by 1..4 continuation bytes
    for (int lead = 0; lead < 256; ++lead)
    {
        std::string b(1, (char)lead);
        CasePortrait(b);
        for (int k = 0; k < 4; ++k)
        {
            b += (char)0x80;
            CasePortrait(b);
        }
    }
    // every multi-byte lead with every second byte near a rule's edge, then two plain continuations
    static const unsigned char kSecond[11] = { 0x00, 0x41, 0x7f, 0x80, 0x8f, 0x90, 0x9f, 0xa0, 0xbf, 0xc0, 0xff };
    for (int lead = 0xc0; lead < 256; ++lead)
        for (int k = 0; k < 11; ++k)
        {
            std::string b;
            b += (char)lead;
            b += (char)kSecond[k];
            b += (char)0x80;
            b += (char)0x80;
            CasePortrait(b);
        }
    for (int i = 0; i < 2200; ++i) CasePortrait(RandomBytes());

    // mn / mx
    for (int i = 0; i < 500; ++i)
    {
        int a = 0, b = 0;
        unsigned mode = R() % 4;
        if (mode == 0) { a = Range(-20, 20); b = Range(-20, 20); }
        else if (mode == 1) { a = Edge(); b = Edge(); }
        else if (mode == 2) { a = Edge(); b = a; }
        else { a = Range(-1000000, 1000000); b = Range(-1000000, 1000000); }
        CaseOne("mn", a, b, RewardsLayout::Min(a, b));
        CaseOne("mx", a, b, RewardsLayout::Max(a, b));
    }

    // ss: ShouldStack, fw: FighterWidth
    for (int i = 0; i < 800; ++i)
    {
        int cw = 0, body = 0;
        unsigned mode = R() % 4;
        if (mode == 0) { int f = Font(); body = BodyFor(f); cw = WinW(); }
        else if (mode == 1) { body = Range(-3, 60); cw = Max(720, body * 50) + Range(-2, 2); }
        else if (mode == 2) { body = Range(-3, 400); cw = Range(-50, 3000); }
        else { body = Edge(); cw = Edge(); }
        CaseStack(cw, body);
    }
    for (int i = 0; i < 800; ++i)
    {
        int cw = 0, body = 0;
        unsigned mode = R() % 4;
        if (mode == 0) { int f = Font(); body = BodyFor(f); cw = WinW(); }
        else if (mode == 1)   // contentWidth * 21 / 100 around the minimum (and around the 320 cap)
        {
            body = Range(-3, 60);
            int minimum = Max(160, body * 11);
            unsigned pick = R() % 2;
            int target = pick ? 320 : minimum;
            cw = target * 100 / 21 + Range(-3, 3);
        }
        else if (mode == 2) { body = Range(-3, 400); cw = Range(-50, 3000); }
        else { body = Edge(); cw = Edge(); }
        CaseFighter(cw, body);
    }

    // gc: GridColumns
    for (int i = 0; i < 1600; ++i)
    {
        int w = 0, minCard = 0, g = 0, maxCols = 0;
        unsigned mode = R() % 4;
        if (mode == 0)
        {
            int f = Font();
            g = GapFor(f);
            int body = BodyFor(f);
            w = WinW();
            minCard = body * 15;
            maxCols = Range(2, 5);
        }
        else if (mode == 1)   // width exactly k cards wide, +-1
        {
            g = Range(-3, 30);
            minCard = Range(1, 300);
            maxCols = Range(-1, 6);
            int k = Range(0, 8);
            w = k * (minCard + Max(0, g)) - Max(0, g) + Range(-1, 1);
        }
        else if (mode == 2)
        {
            w = Range(-50, 3000);
            minCard = Range(-5, 400);
            g = Range(-10, 40);
            maxCols = Range(-2, 8);
        }
        else
        {
            w = Edge();
            minCard = Edge();
            g = Edge();
            maxCols = Edge();
        }
        CaseGridColumns(w, minCard, g, maxCols);
    }

    // fr: FillRowHeight (rows <= 1000 so (rows + 1) * gap stays below 2^31)
    for (int i = 0; i < 1600; ++i)
    {
        int vh = 0, rows = 0, g = 0, minH = 0;
        unsigned mode = R() % 4;
        if (mode == 0)
        {
            int f = Font();
            g = GapFor(f);
            int body = BodyFor(f);
            vh = WinH();
            rows = Range(1, 6);
            minH = body * 9;
        }
        else if (mode == 1)   // usable / rows around the minimum
        {
            g = Range(0, 30);
            rows = Range(1, 8);
            minH = Range(1, 200);
            vh = (rows + 1) * g + rows * minH + Range(-rows - 1, rows + 1);
        }
        else if (mode == 2)
        {
            vh = Range(-100, 3000);
            rows = Range(-3, 12);
            g = Range(-10, 40);
            minH = Range(-10, 300);
        }
        else
        {
            vh = Edge();
            rows = Range(-5, 1000);
            g = Edge();
            minH = Edge();
        }
        CaseFillRow(vh, rows, g, minH);
    }

    // ri: RarityIndex (every tier -6..10 both ways, plus edge tiers)
    for (int tier = -6; tier <= 10; ++tier)
    {
        CaseRarity(tier, false);
        CaseRarity(tier, true);
    }
    for (int i = 0; i < 60; ++i)
    {
        int tier = Edge();
        CaseRarity(tier, false);
        CaseRarity(tier, true);
    }

    // cr: CompactRowHeight
    for (int i = 0; i < 600; ++i)
    {
        int roomy = 0, minH = 0;
        unsigned mode = R() % 3;
        if (mode == 0) { roomy = Range(-20, 400); minH = Range(-10, 300); }
        else if (mode == 1) { minH = Range(1, 300); roomy = 2 * minH + Range(-3, 3); }
        else { roomy = Edge(); minH = Edge(); }
        CaseCompact(roomy, minH);
    }

    // fs: FightSupplyColumns
    for (int n = -5; n <= 9; ++n) CaseSupply(n);
    for (int i = 0; i < 20; ++i) { int n = Edge(); CaseSupply(n); }

    // tf: Fit
    for (int i = 0; i < 1300; ++i)
    {
        int avail = 0, min = 0, sp = 0;
        unsigned mode = R() % 5;
        if (mode == 0)
        {
            int f = Font();
            sp = GapFor(f);
            avail = WinW();
            min = Range(150, 400);
        }
        else if (mode == 1)   // exactly k cards wide, +-1 (k reaches past the 5-column cap)
        {
            sp = Range(0, 30);
            min = Range(1, 400);
            int k = Range(0, 8);
            avail = k * (min + sp) - sp + Range(-1, 1);
        }
        else if (mode == 2)
        {
            avail = Range(-10, 2000);
            min = Range(-10, 400);
            sp = Range(-5, 40);
        }
        else if (mode == 3)   // a 1 px minimum: the 5-column cap
        {
            avail = Range(1, 4000);
            min = Range(1, 3);
            sp = Range(0, 3);
        }
        else
        {
            avail = Edge();
            min = Edge();
            sp = Edge();
        }
        CaseFit(avail, min, sp);
    }

    // ts: SingleRow
    for (int i = 0; i < 1300; ++i)
    {
        int avail = 0, min = 0, sp = 0, n = 0;
        unsigned mode = R() % 4;
        if (mode == 0)
        {
            int f = Font();
            sp = GapFor(f);
            avail = WinW();
            min = Range(150, 400);
            n = Range(0, 12);
        }
        else if (mode == 1)
        {
            avail = Range(-10, 2000);
            min = Range(-10, 400);
            sp = Range(-5, 40);
            n = Range(-3, 40);
        }
        else if (mode == 2)
        {
            sp = Range(0, 30);
            min = Range(1, 400);
            int k = Range(0, 8);
            avail = k * (min + sp) - sp + Range(-1, 1);
            n = Range(1, 9);
        }
        else
        {
            avail = Edge();
            min = Edge();
            sp = Edge();
            n = Edge();
        }
        CaseSingleRow(avail, min, sp, n);
    }

    // tg: a Grid built directly (any columns >= 1)
    for (int i = 0; i < 400; ++i)
    {
        int c = 0, wd = 0, gp = 0;
        unsigned mode = R() % 2;
        if (mode == 0) { c = Range(1, 12); wd = Range(-10, 500); gp = Range(-5, 40); }
        else { c = Range(1, 100000); wd = Range(-1000, 1000); gp = Range(-1000, 1000); }
        CaseGrid(c, wd, gp);
    }

    // tv: ClampViewOffset (the scroll view offset is <= 0 in use; positive and edge values too)
    for (int i = 0; i < 1600; ++i)
    {
        int off = 0, content = 0, view = 0;
        unsigned mode = R() % 4;
        if (mode == 0)
        {
            view = Range(100, 3840);
            content = Range(0, 6000);
            off = -Range(0, content + 100);
        }
        else if (mode == 1)   // content around the view, offset around -maximum
        {
            view = Range(1, 1500);
            content = view + Range(-2, 2);
            int maximum = content > view ? content - view : 0;
            off = -(maximum + Range(-2, 2));
        }
        else if (mode == 2)
        {
            off = Range(-200, 200);
            content = Range(-20, 200);
            view = Range(-20, 200);
        }
        else
        {
            off = Edge();
            content = Edge();
            view = Edge();
        }
        CaseViewOffset(off, content, view);
    }

    std::printf("\n]\n");
    return 0;
}
