using System.Text;
using System.Text.Json;
using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/LeaderboardLayout.cs, ResultsLayout.cs, RewardsLayout.cs and TownCardLayout.cs against Tests/Golden/layouts.json
/// (golden/gen_layouts.cpp; its header comment lists every kind and field). Everything is an int or a byte string, so every value is
/// compared exactly. One [Fact] per function, so a failure names the function and prints the case's inputs.</summary>
public class LayoutsGoldenTests
{
    static readonly Lazy<JsonElement> s_file = new(() => GoldenFile.Load("layouts"));
    static readonly Lazy<JsonElement> s_meta = new(() => s_file.Value.EnumerateArray().First(e => e.GetProperty("k").GetString() == "meta"));

    static List<JsonElement> Cases(string kind)
    {
        Assert.Equal(31761, s_file.Value.GetArrayLength());   // the whole golden file, so a truncated or regenerated file is noticed
        List<JsonElement> list = s_file.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        Assert.NotEmpty(list);
        return list;
    }

    static int I(JsonElement c, string name) => c.GetProperty(name).GetInt32();
    static int[] Ints(JsonElement c, string name) => c.GetProperty(name).EnumerateArray().Select(e => e.GetInt32()).ToArray();
    static string S(int[] v) => "[" + string.Join(",", v) + "]";

    static void Same(int[] expected, ReadOnlySpan<int> actual, Func<string> what)
    {
        if (!actual.SequenceEqual(expected))
            Assert.Fail($"{what()}: expected {S(expected)}, got {S(actual.ToArray())}");
    }

    static void Same(int expected, int actual, Func<string> what)
    {
        if (expected != actual)
            Assert.Fail($"{what()}: expected {expected}, got {actual}");
    }

    // ---- LeaderboardLayout ------------------------------------------------------------------------------------------------------

    [Fact]
    public void FitColumns_matches()
    {
        foreach (JsonElement c in Cases("fc"))
        {
            int w = I(c, "w"), g = I(c, "g");
            int[] min = Ints(c, "min");
            string what() => $"fc w={w} g={g} min={S(min)}";
            LeaderboardLayout.Columns r = LeaderboardLayout.FitColumns(w, g, min);
            Same(I(c, "st"), r.Stacked ? 1 : 0, () => what() + " stacked");
            Same(Ints(c, "l"), r.Left, () => what() + " left");
            Same(Ints(c, "wd"), r.Width, () => what() + " width");
        }
    }

    [Fact]
    public void ClampOffset_matches()
    {
        foreach (JsonElement c in Cases("co"))
        {
            int off = I(c, "off"), ch = I(c, "ch"), vh = I(c, "vh");
            Same(I(c, "o"), LeaderboardLayout.ClampOffset(off, ch, vh), () => $"co off={off} ch={ch} vh={vh}");
        }
    }

    [Fact]
    public void FitVertical_matches()
    {
        foreach (JsonElement c in Cases("fv"))
        {
            int h = I(c, "h"), g = I(c, "g"), body = I(c, "body"), ctx = I(c, "ctx"), close = I(c, "close");
            LeaderboardLayout.Vertical v = LeaderboardLayout.FitVertical(h, g, body, ctx, close);
            int[] actual = { v.ContextHeight, v.HeadingTop, v.HeadingHeight, v.ScrollTop, v.ScrollHeight, v.CloseTop, v.CloseHeight };
            Same(Ints(c, "o"), actual, () => $"fv h={h} g={g} body={body} ctx={ctx} close={close}");
        }
    }

    static int[] Of(LeaderboardLayout.CeremonialMetrics m) => new[] { m.ChampionPortrait, m.OtherPortrait, m.RowPortrait, m.RowGap, m.Edge };
    static int[] Of(LeaderboardLayout.TitleRails t) => new[] { t.TitleLeft, t.TitleWidth, t.LeftWidth, t.RightLeft, t.RightWidth };

    [Fact]
    public void FitCeremonialMetrics_matches()
    {
        foreach (JsonElement c in Cases("cm"))
        {
            int body = I(c, "body"), g = I(c, "g");
            Same(Ints(c, "o"), Of(LeaderboardLayout.FitCeremonialMetrics(body, g)), () => $"cm body={body} g={g}");
        }
    }

    [Fact]
    public void FitTitleRails_matches()
    {
        foreach (JsonElement c in Cases("tr"))
        {
            int w = I(c, "w"), g = I(c, "g"), title = I(c, "title");
            Same(Ints(c, "o"), Of(LeaderboardLayout.FitTitleRails(w, g, title)), () => $"tr w={w} g={g} title={title}");
        }
    }

    /// <summary>Checks one podium against the golden fields st, cnt, ch and s[3][6] of <paramref name="c"/>.</summary>
    static void SamePodium(JsonElement c, LeaderboardLayout.Podium p, Func<string> what)
    {
        Same(I(c, "st"), p.Stacked ? 1 : 0, () => what() + " stacked");
        Same(I(c, "cnt"), p.Count, () => what() + " count");
        Same(I(c, "ch"), p.ContentHeight, () => what() + " contentHeight");
        List<JsonElement> slots = c.GetProperty("s").EnumerateArray().ToList();
        Assert.True(slots.Count == 3, what() + $": {slots.Count} slots in the golden case, expected 3");
        for (int i = 0; i < 3; ++i)
        {
            LeaderboardLayout.PodiumSlot t = p.Slots[i];
            int[] actual = { t.SourceIndex, t.Rank, t.Left, t.Top, t.Width, t.Portrait };
            int slot = i;
            Same(slots[i].EnumerateArray().Select(e => e.GetInt32()).ToArray(), actual, () => what() + $" slot {slot}");
        }
    }

    [Fact]
    public void FitPodium_matches()
    {
        foreach (JsonElement c in Cases("fp"))
        {
            int w = I(c, "w"), g = I(c, "g"), n = I(c, "n"), champ = I(c, "champ"), other = I(c, "other");
            SamePodium(c, LeaderboardLayout.FitPodium(w, g, n, champ, other), () => $"fp w={w} g={g} n={n} champ={champ} other={other}");
        }
    }

    // ---- ResultsLayout ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void FitCeremony_matches()
    {
        foreach (JsonElement c in Cases("ce"))
        {
            int w = I(c, "w"), g = I(c, "g"), body = I(c, "body"), n = I(c, "n"), title = I(c, "title");
            string what() => $"ce w={w} g={g} body={body} n={n} title={title}";
            ResultsLayout.Ceremony r = ResultsLayout.FitCeremony(w, g, body, n, title);
            Same(Ints(c, "m"), Of(r.Metrics), () => what() + " metrics");
            Same(Ints(c, "t"), Of(r.Title), () => what() + " title");
            SamePodium(c, r.Podium, what);
        }
    }

    [Fact]
    public void PortraitInitialUtf8_matches()
    {
        foreach (JsonElement c in Cases("pi"))
        {
            string expected = c.GetProperty("out").GetString()!;
            JsonElement input = c.GetProperty("in");
            if (input.ValueKind == JsonValueKind.Null)
            {
                Assert.Equal(expected, Hex(Encoding.UTF8.GetBytes(ResultsLayout.PortraitInitialUtf8((string?)null))));
                continue;
            }
            string inHex = input.GetString()!;
            byte[] bytes = Convert.FromHexString(inHex);
            string actual = ResultsLayout.PortraitInitialUtf8(bytes);
            // the returned string must re-encode to exactly the original's bytes (lossless: only well-formed UTF-8 is accepted)
            if (Hex(Encoding.UTF8.GetBytes(actual)) != expected)
                Assert.Fail($"pi in={inHex}: expected {expected}, got {Hex(Encoding.UTF8.GetBytes(actual))}");
            // the C string ends at its first 0 byte or at the span's end: a terminator and junk after the bytes change nothing
            string withTail = ResultsLayout.PortraitInitialUtf8(bytes.Concat(new byte[] { 0, 0x41, 0x80 }).ToArray());
            if (withTail != actual)
                Assert.Fail($"pi in={inHex}: a NUL and junk after the bytes changed the result");
        }
    }

    static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    // ---- RewardsLayout ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Min_matches()
    {
        foreach (JsonElement c in Cases("mn"))
            Same(I(c, "o"), RewardsLayout.Min(I(c, "a"), I(c, "b")), () => $"mn a={I(c, "a")} b={I(c, "b")}");
    }

    [Fact]
    public void Max_matches()
    {
        foreach (JsonElement c in Cases("mx"))
            Same(I(c, "o"), RewardsLayout.Max(I(c, "a"), I(c, "b")), () => $"mx a={I(c, "a")} b={I(c, "b")}");
    }

    [Fact]
    public void ShouldStack_matches()
    {
        foreach (JsonElement c in Cases("ss"))
        {
            int cw = I(c, "cw"), body = I(c, "body");
            Same(I(c, "o"), RewardsLayout.ShouldStack(cw, body) ? 1 : 0, () => $"ss cw={cw} body={body}");
        }
    }

    [Fact]
    public void FighterWidth_matches()
    {
        foreach (JsonElement c in Cases("fw"))
        {
            int cw = I(c, "cw"), body = I(c, "body");
            Same(I(c, "o"), RewardsLayout.FighterWidth(cw, body), () => $"fw cw={cw} body={body}");
        }
    }

    [Fact]
    public void GridColumns_matches()
    {
        foreach (JsonElement c in Cases("gc"))
        {
            int w = I(c, "w"), min = I(c, "min"), g = I(c, "g"), max = I(c, "max");
            Same(I(c, "o"), RewardsLayout.GridColumns(w, min, g, max), () => $"gc w={w} min={min} g={g} max={max}");
        }
    }

    [Fact]
    public void FillRowHeight_matches()
    {
        foreach (JsonElement c in Cases("fr"))
        {
            int vh = I(c, "vh"), rows = I(c, "rows"), g = I(c, "g"), min = I(c, "min");
            Same(I(c, "o"), RewardsLayout.FillRowHeight(vh, rows, g, min), () => $"fr vh={vh} rows={rows} g={g} min={min}");
        }
    }

    [Fact]
    public void RarityIndex_matches()
    {
        foreach (JsonElement c in Cases("ri"))
        {
            int tier = I(c, "tier");
            bool complete = I(c, "complete") != 0;
            Same(I(c, "o"), RewardsLayout.RarityIndex(tier, complete), () => $"ri tier={tier} complete={complete}");
        }
    }

    [Fact]
    public void CompactRowHeight_matches()
    {
        foreach (JsonElement c in Cases("cr"))
        {
            int roomy = I(c, "roomy"), min = I(c, "min");
            Same(I(c, "o"), RewardsLayout.CompactRowHeight(roomy, min), () => $"cr roomy={roomy} min={min}");
        }
    }

    [Fact]
    public void FightSupplyColumns_matches()
    {
        foreach (JsonElement c in Cases("fs"))
            Same(I(c, "o"), RewardsLayout.FightSupplyColumns(I(c, "n")), () => $"fs n={I(c, "n")}");
    }

    // ---- TownCardLayout ---------------------------------------------------------------------------------------------------------

    /// <summary>Checks a grid against the golden probes: Column/Row/Left at each meta index, ContentWidth at each meta count.</summary>
    static void SameGrid(JsonElement c, TownCardLayout.Grid grid, Func<string> what)
    {
        JsonElement meta = s_meta.Value;
        int[] idx = Ints(meta, "idx"), cnt = Ints(meta, "cnt");
        List<JsonElement> probes = c.GetProperty("p").EnumerateArray().ToList();
        Assert.True(idx.Length == probes.Count, what() + $": {probes.Count} probes in the golden case, expected {idx.Length}");
        for (int i = 0; i < idx.Length; ++i)
        {
            int index = idx[i];
            int[] actual = { grid.Column(index), grid.Row(index), grid.Left(index) };
            Same(probes[i].EnumerateArray().Select(e => e.GetInt32()).ToArray(), actual, () => what() + $" [Column,Row,Left]({index})");
        }
        int[] widths = Ints(c, "cw");
        Assert.True(cnt.Length == widths.Length, what() + $": {widths.Length} widths in the golden case, expected {cnt.Length}");
        for (int i = 0; i < cnt.Length; ++i)
        {
            int count = cnt[i];
            Same(widths[i], grid.ContentWidth(count), () => what() + $" ContentWidth({count})");
        }
    }

    static void SameFitted(JsonElement c, TownCardLayout.Grid grid, Func<string> what)
    {
        Same(I(c, "c"), grid.Columns, () => what() + " columns");
        Same(I(c, "wd"), grid.Width, () => what() + " width");
        Same(I(c, "gp"), grid.Gap, () => what() + " gap");
        SameGrid(c, grid, what);
    }

    [Fact]
    public void TownCard_Fit_matches()
    {
        foreach (JsonElement c in Cases("tf"))
        {
            int avail = I(c, "avail"), min = I(c, "min"), sp = I(c, "sp");
            SameFitted(c, TownCardLayout.Fit(avail, min, sp), () => $"tf avail={avail} min={min} sp={sp}");
        }
    }

    [Fact]
    public void TownCard_SingleRow_matches()
    {
        foreach (JsonElement c in Cases("ts"))
        {
            int avail = I(c, "avail"), min = I(c, "min"), sp = I(c, "sp"), n = I(c, "n");
            SameFitted(c, TownCardLayout.SingleRow(avail, min, sp, n), () => $"ts avail={avail} min={min} sp={sp} n={n}");
        }
    }

    [Fact]
    public void TownCard_Grid_members_match()
    {
        foreach (JsonElement c in Cases("tg"))
        {
            var grid = new TownCardLayout.Grid { Columns = I(c, "c"), Width = I(c, "wd"), Gap = I(c, "gp") };
            SameGrid(c, grid, () => $"tg c={grid.Columns} wd={grid.Width} gp={grid.Gap}");
        }
    }

    [Fact]
    public void TownCard_ClampViewOffset_matches()
    {
        foreach (JsonElement c in Cases("tv"))
        {
            int off = I(c, "off"), content = I(c, "content"), view = I(c, "view");
            Same(I(c, "o"), TownCardLayout.ClampViewOffset(off, content, view), () => $"tv off={off} content={content} view={view}");
        }
    }
}
