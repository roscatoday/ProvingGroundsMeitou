using ProvingGrounds.Core.Glue;
using N = ProvingGrounds.Core.Glue.ArenaUINames;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 4.5b: the Arena window's pixel arithmetic (ArenaUI.cpp RefreshResponsiveMode :2512-2530, PlaceLabel :2580-2587, PlaceButtons
/// :2594-2615, LayoutColumn :2617-2703, LayoutPanel :2705-2756, LayoutRows :687-728, RebuildList's empty label :740-747, the settings tip
/// :1405-1450), each expected value worked out by hand from the C++ with a fake text measurer.</summary>
public class ArenaUILayoutTests
{
    /// <summary>A fake MyGUI text measure: 7 px per character for the standard font, 8 for the painted font, 9 on a button; each line one font
    /// height (17); words are not kept whole (enough for the arithmetic).</summary>
    sealed class FakeMeasure : IArenaTextMeasure
    {
        public int Line = 17;
        static int Lines(string text, int perChar, int width) =>
            text.Split('\n').Sum(part => Math.Max(1, (part.Length * perChar + width - 1) / Math.Max(1, width)));
        public int LabelHeight(string text, int width) => Line * Lines(text, 7, width);
        public int PaintedHeight(string text, int width) => Line * Lines(text, 8, width);
        public int ButtonTextWidth(string caption) => caption.Length * 9;
    }

    static readonly FakeMeasure M = new();
    static ArenaLayout Lay(ArenaLayoutInput i) => ArenaUILayout.Compute(i, M);
    static ArenaLayoutRow R(string caption, bool status = true, string? handler = null) => new(caption, status, handler is not null, handler ?? "");

    [Fact]
    public void WindowIsTwentyThreeTwentyFifthsAndClientLosesTheTemplateMargins()
    {
        // :2519-2520 setCoord(1920/25, 1080/25, 1920*23/25, 1080*23/25) = (76, 43, 1766, 993); Kenshi_WindowCX client = 1766-6 x 993-45
        var l = Lay(new ArenaLayoutInput());
        Assert.Equal(new LayoutRect(76, 43, 1766, 993), l.Window);
        Assert.Equal(1760, l.ClientWidth);
        Assert.Equal(948, l.ClientHeight);
    }

    [Theory]
    [InlineData(1920, 1080, 17, false)]   // client 1760 >= 76 x 17 = 1292
    [InlineData(1366, 768, 17, true)]     // window 1256, client 1250 < 1292
    [InlineData(1366, 768, 16, false)]    // 1250 >= 1216
    [InlineData(1280, 720, 17, true)]     // client 1171
    public void CompactWhenTheClientIsNarrowerThanSeventySixFontHeights(int w, int h, int font, bool compact) =>
        Assert.Equal(compact, ArenaUILayout.CompactFor(w, h, font));

    [Fact]
    public void WideLayoutPlacesThreeColumnsHeaderAndFooter()
    {
        // gap = 17/2 = 8, body 17, buttonH = RowHeight = 17 + 16 = 33, headerH = 49; width = 1760 - 16 = 1744
        // footerH = min(948/3 = 316, 4*17 + 16 = 84) = 84; footerY = max(49, 948 - 84 - 8) = 856; contentH = 856 - 49 - 8 = 799
        // sideW = (1744 - 16) / 4 = 432
        var l = Lay(new ArenaLayoutInput());
        Assert.Equal(new LayoutRect(8, 49, 432, 799), l.Rects[N.Column(0)]);
        Assert.Equal(new LayoutRect(16 + 432, 49, 1744 - 2 * 432 - 16, 799), l.Rects[N.Column(1)]);
        Assert.Equal(new LayoutRect(8 + 1744 - 432, 49, 432, 799), l.Rects[N.Column(2)]);
        Assert.True(l.HeroVisible);
        Assert.False(l.DrawerButtonsVisible);
        Assert.All(l.ColumnVisible, Assert.True);
        // actionW = min(872, max(9*"Stop Fight".Length, 9*"Start Fight".Length) + 32) = 99 + 32 = 131
        Assert.Equal(new LayoutRect(8, 856, 1744 - 131 - 8, 84), l.Rects[N.Status]);
        Assert.Equal(new LayoutRect(8 + 1744 - 131, 856, 131, 33), l.Rects[N.Start]);
        Assert.Equal(l.Rects[N.Start], l.Rects[N.Stop]);
        // the presentation text: PlaceLabel(text, gap, gap, cell - 2 gap) -> (8, 8, 115, 17)
        Assert.Equal(new LayoutRect(8, 8, 115, 17), l.Rects[N.TextOf(N.Start)]);
        // recruitWidth = min(872, 9*17 + 32 = 185); hero PlaceLabel(gap, gap, 1744 - 185 - 8)
        Assert.Equal(new LayoutRect(8 + 1744 - 185, 8, 185, 33), l.Rects[N.Recruit]);
        Assert.Equal(new LayoutRect(8, 8, 1744 - 185 - 8, 17), l.Rects[N.Hero]);
    }

    [Fact]
    public void PlaceButtonsFitsColumnsByTheWidestCaptionAndWrapsRows()
    {
        // column 1: view = 864 - 15 (no bar) = 849 wide, width = 849 - 16 = 833. Mode buttons: widest "Last Man Standing" 17*9 + 32 = 185;
        // columns = min(3, (833 + 8) / (185 + 8) = 4) = 3; cell = (833 - 16) / 3 = 272
        var l = Lay(new ArenaLayoutInput());
        int y0 = 8 + 17 + 8;   // gap, PG_LblMode one line, gap
        Assert.Equal(new LayoutRect(8, 8, 833, 17), l.Rects[N.LblMode]);
        Assert.Equal(new LayoutRect(8, y0, 272, 33), l.Rects[N.ModeAvB]);
        Assert.Equal(new LayoutRect(8 + 280, y0, 272, 33), l.Rects[N.ModeTeams1v1]);
        Assert.Equal(new LayoutRect(8 + 560, y0, 272, 33), l.Rects[N.ModeLast]);
    }

    [Fact]
    public void NarrowColumnWrapsButtonsOntoRows()
    {
        // compact with the roster drawer open: column 1 = width - (sideW + gap); here only the arithmetic of PlaceButtons is checked on a narrow
        // column 2 (the settings column, 432 wide: view 417, width 401): locations "Arena", "Small", "Banner": widest 6*9+32 = 86,
        // columns = min(3, 409 / 94 = 4) = 3, cell = (401 - 16) / 3 = 128
        var l = Lay(new ArenaLayoutInput());
        var arena = l.Rects[N.LocArena];
        Assert.Equal(128, arena.W);
        Assert.Equal(arena.Y, l.Rects[N.LocBanner].Y);
        Assert.Equal(8 + 2 * 136, l.Rects[N.LocBanner].X);
        // the medical button alone on its row, full width
        Assert.Equal(401, l.Rects[N.Medical].W);
        Assert.Equal(arena.Y + 33 + 8, l.Rects[N.Medical].Y);
    }

    [Fact]
    public void TeamListsSideBySideOrStackedByWidth()
    {
        // wide: width 833 >= 32 * 17 = 544 -> side by side, half = (833 - 8) / 2 = 412
        var l = Lay(new ArenaLayoutInput { TeamMode = true });
        Assert.Equal(412, l.Rects[N.TeamA].W);
        Assert.Equal(8 + 8 + 412, l.Rects[N.TeamB].X);
        Assert.Equal(l.Rects[N.TeamA].Y, l.Rects[N.TeamB].Y);
        // narrow (view 1000 x 600, compact, no drawer): client 914, width 898, column 1 = 898 wide -> view 883, width 867 >= 544: side by side
        // stacked needs a column narrower than 544: compact with a drawer open (sideW = 898*2/5 = 359, column 1 = 898 - 367 = 531, width 516 - 16 = 500 without the bar)
        var s = Lay(new ArenaLayoutInput { ViewWidth = 1000, ViewHeight = 600, Compact = true, RosterDrawerOpen = true, TeamMode = true });
        Assert.Equal(new LayoutRect(8 + 359 + 8, s.Rects[N.Column(1)].Y, 898 - 367, s.Rects[N.Column(1)].H), s.Rects[N.Column(1)]);
        Assert.Contains(N.Column(1), s.VerticalBar);   // the column (358 high, view 343) scrolls: its bar narrows the view to 516 - 20
        Assert.Equal(480, s.Rects[N.TeamA].W);
        Assert.Equal(s.Rects[N.TeamA].X, s.Rects[N.TeamB].X);
        Assert.True(s.Rects[N.TeamB].Y > s.Rects[N.TeamA].Bottom);
    }

    [Fact]
    public void PoolModeShowsThePoolAndTwoEditButtons()
    {
        var l = Lay(new ArenaLayoutInput { TeamMode = false });
        Assert.Equal(833, l.Rects[N.Pool].W);
        // poolEdit = Balance, Clear: widest "Clear Teams" 99 + 32 = 131 -> 2 columns, cell (833 - 8) / 2 = 412
        Assert.Equal(412, l.Rects[N.AutoBalance].W);
        Assert.Equal(8 + 420, l.Rects[N.Clear].X);
        // the destination row holds one button (Assign A / Add Selected Fighter) at full width
        Assert.Equal(833, l.Rects[N.DestA].W);
        Assert.Equal(new LayoutRect(0, 0, 200, 32), l.Rects[N.DestB]);   // never placed: its creation coordinate (hidden)
    }

    [Fact]
    public void BalanceMenuReplacesTheEditButtons()
    {
        var l = Lay(new ArenaLayoutInput { BalanceMenuOpen = true, Captions = new Dictionary<string, string> { [N.AutoBalance] = "Back" } });
        Assert.Equal(l.Rects[N.AutoBalance].Y, l.Rects[N.BalanceSkill].Y);
        Assert.Equal(l.Rects[N.AutoBalance].Y, l.Rects[N.BalanceRating].Y);
        Assert.Equal(new LayoutRect(0, 0, 200, 32), l.Rects[N.Swap]);
    }

    [Fact]
    public void RosterRowsStackWithStatusBarAndGaps()
    {
        // column 0: view 432 - 15 = 417 (no bar yet), width 401. LblRoster, RosterCount one line each (17), search buttonH + gap = 41,
        // the hint: "Double-click or drag to assign. Right-click to remove." (54 chars * 7 = 378 <= 401: one line)
        // search at y = 8 + 25 + 25 = 58; y = 58 + 49 + 25 = 132; listH = max(9*17, 799 - 15 - 132 - 8 = 644) = 644
        var rows = new[] { R("Ann"), R("Bob\nUnavailable - Too far away") };
        var l = Lay(new ArenaLayoutInput { Roster = rows });
        Assert.Equal(new LayoutRect(8, 58, 401, 41), l.Rects[N.Search]);
        Assert.Equal(new LayoutRect(8, 132, 401, 644), l.Rects[N.Roster]);
        // rows: view 401 - 15 = 386 wide (no bar), textX = 48 + 16 = 64, label width 386 - 64 - 8 = 314
        // row 0: labelH 17, status at y = 8 + 17 + 8 = 33, textH = 17 + 8 + 6 = 31, height = max(48, 31) + 16 = 64
        string r0 = N.Row(ArenaList.Roster, 0), r1 = N.Row(ArenaList.Roster, 1);
        Assert.Equal(new LayoutRect(0, 0, 386, 64), l.Rects[r0]);
        Assert.Equal(new LayoutRect(64, 8, 314, 17), l.Rects[N.RowLabel(r0)]);
        Assert.Equal(new LayoutRect(8, 8, 48, 48), l.Rects[N.RowImage(r0)]);
        Assert.Equal(new LayoutRect(64, 33, 314, 6), l.Rects[N.RowStatusBack(r0)]);
        // row 1: two lines (34) -> status at 8 + 34 + 8 = 50, textH 48, height 64; y = 64 + 8
        Assert.Equal(new LayoutRect(0, 72, 386, 64), l.Rects[r1]);
        Assert.Equal(new LayoutRect(64, 50, 314, 6), l.Rects[N.RowStatusBack(r1)]);
        Assert.Equal((386, Math.Max(144, 644 - 15)), l.Canvas[N.Roster]);
        Assert.DoesNotContain(N.Roster, l.VerticalBar);
    }

    [Fact]
    public void ManyRowsShowTheBarAndNarrowTheRows()
    {
        var rows = Enumerable.Range(0, 12).Select(i => R("Fighter " + i)).ToArray();   // 12 x 72 = 864 > 629
        var l = Lay(new ArenaLayoutInput { Roster = rows });
        Assert.Contains(N.Roster, l.VerticalBar);
        Assert.Equal(386 - 20, l.Rects[N.Row(ArenaList.Roster, 0)].W);
        Assert.Equal((366, 12 * 72), l.Canvas[N.Roster]);
    }

    [Fact]
    public void TeamPrisonerRowWithHandlerAddsTheHandlerLine()
    {
        // team A: width 412 -> view 397; row label width 397 - 72 = 325; no status bar on team rows: height = max(48, 17) + 16 = 64
        // handler: hx = 22 + 16 = 38, label at (38, 64, 397 - 46 = 351, 17), image (8, 64), height += max(22, 17) + 8 = 30 -> 94
        var l = Lay(new ArenaLayoutInput { TeamA = [R("Prisoner", status: false, handler: "Handler: Ann")] });
        string r = N.Row(ArenaList.TeamA, 0);
        Assert.Equal(new LayoutRect(0, 0, 397, 94), l.Rects[r]);
        Assert.Equal(new LayoutRect(38, 64, 351, 17), l.Rects[N.RowHandlerLabel(r)]);
        Assert.Equal(new LayoutRect(8, 64, 22, 22), l.Rects[N.RowHandlerImage(r)]);
        Assert.False(l.Rects.ContainsKey(N.RowStatusBack(r)));
    }

    [Fact]
    public void EmptyListShowsItsLabel()
    {
        var l = Lay(new ArenaLayoutInput());
        // team A view 397: (8, 8, 381, max(24, height of "Drop fighters here" at 381 = 17)) = 24 high
        Assert.Equal(new LayoutRect(8, 8, 381, 24), l.Rects[N.EmptyList(ArenaList.TeamA)]);
        Assert.Equal(new LayoutRect(8, 8, 370, 24), l.Rects[N.EmptyList(ArenaList.Roster)]);
    }

    [Fact]
    public void CompactPutsTheDrawerButtonsInTheHeaderAndHidesTheSideColumns()
    {
        var l = Lay(new ArenaLayoutInput { ViewWidth = 1280, ViewHeight = 720, Compact = true });
        // window 1177 x 662, client 1171 x 617, width 1155; header: "Squad Roster" 108+32, "Match Settings" 126+32 = 158, "Recruit Prisoners"
        // 153+32 = 185: columns = min(3, 1163 / 193 = 6) = 3, cell = (1155 - 16) / 3 = 379
        Assert.False(l.HeroVisible);
        Assert.True(l.DrawerButtonsVisible);
        Assert.False(l.ColumnVisible[0]);
        Assert.False(l.ColumnVisible[2]);
        Assert.Equal(new LayoutRect(8, 8, 379, 33), l.Rects[N.RosterDrawer]);
        Assert.Equal(new LayoutRect(8 + 387, 8, 379, 33), l.Rects[N.SettingsDrawer]);
        Assert.Equal(new LayoutRect(8 + 774, 8, 379, 33), l.Rects[N.Recruit]);
        // top = max(49, 33 + 16) = 49; footerH = min(617/3 = 205, 84) = 84; footerY = 617 - 92 = 525; no drawer: column 1 full width
        Assert.Equal(new LayoutRect(8, 49, 1155, 525 - 49 - 8), l.Rects[N.Column(1)]);
        Assert.Equal(new LayoutRect(8, 49, 1, 525 - 49 - 8), l.Rects[N.Column(0)]);
    }

    [Fact]
    public void CompactDrawerTakesTwoFifths()
    {
        var l = Lay(new ArenaLayoutInput { ViewWidth = 1280, ViewHeight = 720, Compact = true, SettingsDrawerOpen = true });
        int sideW = 1155 * 2 / 5;
        Assert.True(l.ColumnVisible[2]);
        Assert.False(l.ColumnVisible[0]);
        Assert.Equal(new LayoutRect(8, 49, sideW, 468), l.Rects[N.Column(2)]);
        Assert.Equal(new LayoutRect(8 + sideW + 8, 49, 1155 - sideW - 8, 468), l.Rects[N.Column(1)]);
    }

    [Fact]
    public void PreviewCardPlacesTextBesideThePortrait()
    {
        var l = Lay(new ArenaLayoutInput());
        // column 2: width 401; LblPreview at y 8 (17), image at (8, 33); text x = 16 + 48 = 64, width 401 - 48 - 8 = 345
        Assert.Equal(new LayoutRect(8, 33, 48, 48), l.Rects[N.PreviewImg]);
        Assert.Equal(new LayoutRect(64, 33, 345, 17), l.Rects[N.PreviewName]);
        Assert.Equal(new LayoutRect(64, 33 + 25, 345, 17), l.Rects[N.PreviewMeta]);
        // y = max(33 + 25 + 25 = 83, 33 + 48 + 8 = 89) = 89: the stats line
        Assert.Equal(89, l.Rects[N.PreviewStats].Y);
    }

    [Fact]
    public void LongerCaptionsPushTheColumnDown()
    {
        var a = Lay(new ArenaLayoutInput());
        var b = Lay(new ArenaLayoutInput { Captions = new Dictionary<string, string> { [N.PreviewStats] = new string('x', 120) } });   // 840 px: 3 lines
        Assert.Equal(a.Rects[N.Remove].Y + 34, b.Rects[N.Remove].Y);
        Assert.Equal(a.Rects[N.Summary].Y + 34, b.Rects[N.Summary].Y);
    }

    [Fact]
    public void SettingsTipFollowsThePointerAndFlipsAtTheEdges()
    {
        // client 1760 x 948, font 17: width min(1744, 476) = 476, height min(932, 34 + 24) = 58
        var tip = ArenaUILayout.SettingsTip(1760, 948, 17, 100, 200, _ => 34);
        Assert.Equal(new LayoutRect(114, 218, 476, 58), tip);
        var flipped = ArenaUILayout.SettingsTip(1760, 948, 17, 1500, 920, _ => 34);
        Assert.Equal(new LayoutRect(1500 - 476 - 8, 920 - 58 - 8, 476, 58), flipped);
        var clamped = ArenaUILayout.SettingsTip(400, 300, 17, 0, 0, _ => 34);
        Assert.Equal(8, clamped.X);
    }

    [Fact]
    public void StatusFillRoundsHalfUp() => Assert.Equal(157, ArenaUILayout.StatusFillWidth(314, .4999f));

    [Theory]
    [InlineData(ArenaList.Roster, 0)]
    [InlineData(ArenaList.Pool, 11)]
    [InlineData(ArenaList.TeamB, 3)]
    public void RowNamesRoundTrip(ArenaList list, int index)
    {
        Assert.Equal(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"PG_Row_{(int)list}_{index}"), N.Row(list, index));
        Assert.True(N.TryParseRow(N.Row(list, index), out var l, out int i));
        Assert.Equal((list, index), (l, i));
        Assert.False(N.TryParseRow(N.RowLabel(N.Row(list, index)), out _, out _));
        Assert.False(N.TryParseRow("PG_Row_9_1", out _, out _));
    }

    // ---------- invariants at odd font heights (task 4.5b review) ----------

    /// <summary>The widget each rectangle is relative to, as ArenaScreen.Build nests them (src/Ui/ArenaUI.cs): "" = the window's client.</summary>
    static string ParentOf(string name)
    {
        for (int i = 0; i < 3; i++)
        {
            if (name == N.Column(i)) return "";
            if (name == N.ColumnContent(i)) return N.Column(i);
        }
        string[] col0 = [N.LblRoster, N.RosterCount, N.Search, N.RosterHint, N.Roster];
        string[] col1 = [N.LblMode, N.ModeAvB, N.ModeTeams1v1, N.ModeLast, N.LblDest, N.DestA, N.DestB, N.LblTeamA, N.LblTeamB, N.LblPool,
            N.TeamA, N.TeamB, N.Pool, N.AutoBalance, N.BalanceSkill, N.BalanceRating, N.Swap, N.Clear, N.BuilderHint, N.Preset(0), N.Preset(1), N.Preset(2)];
        string[] col2 = [N.LblPreview, N.PreviewImg, N.PreviewName, N.PreviewMeta, N.PreviewStats, N.Remove, N.LblSettings, N.LblLoc, N.LocArena,
            N.LocSmall, N.LocBanner, N.Medical, N.LblSummary, N.Summary];
        if (col0.Contains(name)) return N.ColumnContent(0);
        if (col1.Contains(name)) return N.ColumnContent(1);
        if (col2.Contains(name)) return N.ColumnContent(2);
        foreach (var b in ArenaUILayout.AllButtons()) if (name == N.TextOf(b)) return b;
        foreach (var list in new[] { ArenaList.Roster, ArenaList.TeamA, ArenaList.TeamB, ArenaList.Pool })
        {
            if (name == N.EmptyList(list)) return N.ScrollOf(list);
            for (int i = 0; i < 40; i++)
            {
                string row = N.Row(list, i);
                if (name == row) return N.ScrollOf(list);
                if (name == N.RowStatusFill(row)) return N.RowStatusBack(row);
                if (name == N.RowImage(row) || name == N.RowLabel(row) || name == N.RowStatusBack(row) || name == N.RowHandlerImage(row)
                    || name == N.RowHandlerLabel(row)) return row;
            }
        }
        return "";   // Hero, Recruit, Status, Start, Stop, the drawer buttons, the tip, the metrics label
    }

    /// <summary>The widgets the layout placed for this input (a widget LayoutPanel did not place keeps its creation rectangle and is hidden).</summary>
    static bool Placed(string name, ArenaLayoutInput i, ArenaLayout l)
    {
        for (int c = 0; c < 3; c++)
            if (!l.ColumnVisible[c] && (name == N.Column(c) || IsUnder(name, N.Column(c)))) return false;
        if (name == N.Metrics || name == N.SettingsTip) return false;
        if (!l.HeroVisible && name == N.Hero) return false;
        if (!l.DrawerButtonsVisible && (name == N.RosterDrawer || name == N.SettingsDrawer || IsUnder(name, N.RosterDrawer) || IsUnder(name, N.SettingsDrawer))) return false;
        var notPlaced = new List<string>();
        if (i.TeamMode) notPlaced.AddRange([N.LblPool, N.Pool]);
        else notPlaced.AddRange([N.LblTeamA, N.LblTeamB, N.TeamA, N.TeamB, N.DestB]);
        if (i.BalanceMenuOpen) notPlaced.AddRange([N.Swap, N.Clear]);
        else notPlaced.AddRange([N.BalanceSkill, N.BalanceRating]);
        if (!i.BalanceMenuOpen && !i.TeamMode) notPlaced.Add(N.Swap);
        foreach (var n in notPlaced) if (name == n || IsUnder(name, n)) return false;
        return true;
    }

    static bool IsUnder(string name, string ancestor)
    {
        for (string p = ParentOf(name); p.Length > 0; p = ParentOf(p)) if (p == ancestor) return true;
        return false;
    }

    static (int W, int H) ParentSize(ArenaLayout l, string parent) =>
        parent.Length == 0 ? (l.ClientWidth, l.ClientHeight) : l.Canvas.TryGetValue(parent, out var c) ? c : (l.Rects[parent].W, l.Rects[parent].H);

    public static IEnumerable<object[]> OddFonts()
    {
        foreach (int font in new[] { 16, 21 })
            foreach (var (w, h) in new[] { (1920, 1080), (1366, 768), (1280, 720), (2560, 1440) })
                foreach (var (team, balance) in new[] { (true, false), (true, true), (false, false), (false, true) })
                    foreach (int drawer in new[] { 0, 1, 2 })
                        yield return [font, w, h, team, balance, drawer];
    }

    [Theory]
    [MemberData(nameof(OddFonts))]
    public void EveryPlacedRectangleLiesInsideItsParentAndCanvasesHoldTheirContent(int font, int w, int h, bool team, bool balance, int drawer)
    {
        var m = new FakeMeasure { Line = font };
        bool compact = ArenaUILayout.CompactFor(w, h, font);
        if (!compact && drawer != 0) return;   // drawers only exist in compact mode
        ArenaLayoutRow[] Rows(int n, bool status, int handlerAt = -1) =>
            Enumerable.Range(0, n).Select(k => R("Fighter " + k + (k % 3 == 0 ? "\nUnavailable - Too far away" : ""), status, k == handlerAt ? "Handler: Ann" : null)).ToArray();
        var input = new ArenaLayoutInput
        {
            ViewWidth = w, ViewHeight = h, FontHeight = font, SampleTextHeight = font, Compact = compact,
            RosterDrawerOpen = drawer == 1, SettingsDrawerOpen = drawer == 2, TeamMode = team, BalanceMenuOpen = balance,
            Roster = Rows(14, true), TeamA = team ? Rows(3, false, 1) : [], TeamB = [], Pool = team ? [] : Rows(5, false, 0),
        };
        var l = ArenaUILayout.Compute(input, m);
        Assert.True(l.Window.X >= 0 && l.Window.Y >= 0 && l.Window.Right <= w && l.Window.Bottom <= h, $"window {l.Window} in {w}x{h}");
        foreach (var (name, r) in l.Rects)
        {
            if (!Placed(name, input, l)) continue;
            string parent = ParentOf(name);
            var (pw, ph) = ParentSize(l, parent);
            Assert.True(r.X >= 0 && r.Y >= 0 && r.W >= 1 && r.H >= 1 && r.Right <= pw && r.Bottom <= ph,
                $"{name} {r} inside {(parent.Length == 0 ? "the client" : parent)} {pw}x{ph} (font {font}, {w}x{h}, team {team}, balance {balance}, drawer {drawer})");
        }
        foreach (var (scroll, c) in l.Canvas)
        {
            var bottom = l.Rects.Where(kv => Placed(kv.Key, input, l) && ParentOf(kv.Key) == scroll).Select(kv => kv.Value.Bottom).DefaultIfEmpty(0).Max();
            var right = l.Rects.Where(kv => Placed(kv.Key, input, l) && ParentOf(kv.Key) == scroll).Select(kv => kv.Value.Right).DefaultIfEmpty(0).Max();
            Assert.True(c.H >= bottom && c.W >= right, $"canvas of {scroll} {c} holds its content to {right}x{bottom} (font {font}, {w}x{h})");
            var view = l.Rects[scroll];
            Assert.True(c.H >= view.H - ArenaUILayout.ScrollInsetH, $"canvas of {scroll} {c} at least its view {view} (font {font}, {w}x{h})");
        }
    }
}
