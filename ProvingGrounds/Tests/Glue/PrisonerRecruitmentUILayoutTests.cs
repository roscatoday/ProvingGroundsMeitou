using ProvingGrounds.Core.Glue;
using N = ProvingGrounds.Core.Glue.PrisonerRecruitmentUINames;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The recruitment window's pixel arithmetic (PrisonerRecruitmentUI.cpp:93-444, task 6.4b) over a fake text measure (10 px a character,
/// 20 px a line, the painted text the same; a button caption 11 px a character), font height 20 (gap 10, body 20). Expected numbers worked by
/// hand from the C++ (each step's arithmetic in the comments).</summary>
public class PrisonerRecruitmentUILayoutTests
{
    sealed class Fake : IRewardsTextMeasure
    {
        public (int Width, int Height) Standard(string text, int wrapWidth)
        {
            int widest = 0, lines = 0;
            foreach (var part in text.Split('\n'))
            {
                int w = part.Length * 10;
                if (wrapWidth > 0 && w > wrapWidth) { lines += (w + wrapWidth - 1) / wrapWidth; widest = Math.Max(widest, wrapWidth); }
                else { lines++; widest = Math.Max(widest, w); }
            }
            return (widest, lines * 20);
        }
        public (int Width, int Height) Painted(string text, int wrapWidth) => Standard(text, wrapWidth);
        public int ButtonWidth(string caption) => caption.Length * 11;
    }

    static readonly Fake M = new();

    static PrisonerRecruitmentUiView<int> View(params string[] names) => new()
    {
        Rows = new(names.Select((n, i) => new PrisonerRecruitmentRowView<int>(10 + i, i, n, "130 Marks", i == 0))),
        HasSelected = names.Length > 0, Selected = names.Length > 0 ? 10 : 0, PortraitVisible = names.Length > 0,
        SelectedName = names.Length > 0 ? names[0] : PrisonerRecruitmentUiTexts.NoPrisonerSelected,
        Stats = "Combat 40  |  130 / 100 Arena Marks",          // 35 characters: one line
        Explanation = "This prisoner can earn freedom now.",       // 35 characters
        ActionCaption = "Grant Freedom - 100 Marks", Status = PrisonerRecruitmentUiTexts.StatusIdle,
    };

    static RecruitmentWindowLayout Compute(PrisonerRecruitmentUiView<int> v, int w = 1920, int h = 1080, string? windowStatus = null) =>
        PrisonerRecruitmentUILayout<int>.Compute(v, new RecruitmentLayoutInput { ViewWidth = w, ViewHeight = h, FontHeight = 20, WindowStatus = windowStatus }, M);

    [Fact]   // FitWindowToScreen :367-380 and LayoutWindow :382-444, side by side (1663 >= 55 bodies)
    public void At_1920_by_1080_the_window_has_the_buttons_status_subtitle_and_two_columns()
    {
        var l = Compute(View("Dust Bandit"));
        Assert.Equal(10, l.Gap);
        Assert.Equal(20, l.Body);
        // 1920 * 88 / 100 = 1689, 1080 * 88 / 100 = 950, centred: (231 / 2, 130 / 2)
        Assert.Equal(new LayoutRect(115, 65, 1689, 950), l.Window);
        Assert.Equal(1683, l.ClientWidth);   // Kenshi_WindowCX chrome 6 x 45
        Assert.Equal(905, l.ClientHeight);
        // width 1663; buttonHeight max(RowHeight("Recruitment"), RowHeight(caption)) = 20 + 2 gaps = 40; closeWidth min(1663 / 3 = 554, 13 * 11 + 40 = 183)
        // buttonTop max(10, 905 - 10 - 40) = 855; the action button fills the rest: 1663 - 183 - 10 = 1470
        Assert.Equal(new LayoutRect(10, 855, 183, 40), l.Rects[N.Close]);
        Assert.Equal(new LayoutRect(203, 855, 1470, 40), l.Rects[N.Action]);
        // status: insets min(30, 2 * 20 + 2 * 10 = 60) = 30, the idle line (51 characters) one line in 1663 - 30; min(70, max(20, 20 + 30)) = 50, top 855 - 10 - 50
        Assert.Equal(new LayoutRect(10, 795, 1663, 50), l.Rects[N.Status]);
        // subtitle (62 characters) one line: max(20, 20 + 30) = 50; contentTop 10 + 50 + 10 = 70, contentHeight 795 - 10 - 70 = 715
        Assert.Equal(new LayoutRect(10, 10, 1663, 50), l.Rects[N.Subtitle]);
        Assert.False(l.Stacked);
        // rosterWidth max(20 * 15 = 300, 1663 * 32 / 100 = 532) = 532
        Assert.Equal(new LayoutRect(10, 70, 532, 20), l.Rects[N.RosterHeading]);
        Assert.Equal(new LayoutRect(10, 100, 532, 685), l.Rects[N.RosterScroll]);   // 715 - 20 - 10
        Assert.Equal(new LayoutRect(552, 70, 1121, 715), l.Rects[N.DetailScroll]);  // 1663 - 532 - 10
        // the metrics label stays where EnsureWindow made it, hidden (:507-509)
        Assert.Equal(new LayoutRect(0, 0, 100, 24), l.Rects[N.Metrics]);
        Assert.Contains(N.Metrics, l.Hidden);
    }

    [Fact]   // LayoutRows :299-337: the row button fills the roster's view width, the portrait 3 bodies, the name and Marks beside it
    public void A_row_is_a_button_with_its_portrait_name_and_marks()
    {
        var l = Compute(View("Dust Bandit"));
        // view 532 - 15 = 517 (no bar), 685 - 15 = 670; image max(48, 60) = 60; textX 60 + 20 = 80; textWidth 517 - 80 - 10 = 427
        Assert.Equal(new LayoutRect(80, 10, 427, 20), l.Rects[N.RowName(0)]);
        Assert.Equal(new LayoutRect(80, 40, 427, 20), l.Rects[N.RowMarks(0)]);     // gap + 20 + gap
        Assert.Equal(new LayoutRect(0, 0, 517, 80), l.Rects[N.Row(0)]);            // max(60, 20 + 10 + 20) + 2 gaps
        Assert.Equal(new LayoutRect(10, 10, 60, 60), l.Rects[N.RowPortrait(0)]);
        Assert.Equal((517, 670), l.Canvas[N.RosterScroll]);                         // max(90, 670)
        Assert.Equal(new RecruitmentCanvasInfo(90, 670, false), l.Roster);
    }

    [Fact]   // a name wider than its column wraps (the painted Kenshi_PaintedWordWrapEmpty), the Marks below it, the row taller
    public void A_long_name_wraps_and_makes_its_row_taller()
    {
        var l = Compute(View("Dust Bandit", new string('x', 60)));   // 600 px in 427: two lines
        Assert.Equal(new LayoutRect(0, 0, 517, 80), l.Rects[N.Row(0)]);
        Assert.Equal(new LayoutRect(80, 10, 427, 40), l.Rects[N.RowName(1)]);
        Assert.Equal(new LayoutRect(80, 60, 427, 20), l.Rects[N.RowMarks(1)]);     // gap + 40 + gap
        Assert.Equal(new LayoutRect(0, 90, 517, 90), l.Rects[N.Row(1)]);           // y 80 + 10; max(60, 40 + 10 + 20) + 20
        Assert.Equal(190, l.Roster.Content);                                        // 90 + 90 + 10
    }

    [Fact]   // LayoutDetails :339-365: the portrait (7 bodies), the name, the stats line and the explanation, one under the other
    public void The_details_column_stacks_portrait_name_stats_and_explanation()
    {
        var l = Compute(View("Dust Bandit"));
        // view 1121 - 15 = 1106, 715 - 15 = 700; width 1106 - 20 = 1086; portrait min(max(80, 140), 1086) = 140
        Assert.Equal(new LayoutRect(10, 10, 140, 140), l.Rects[N.Portrait]);
        Assert.Equal(new LayoutRect(10, 160, 1086, 20), l.Rects[N.Name]);
        Assert.Equal(new LayoutRect(10, 190, 1086, 20), l.Rects[N.Stats]);
        Assert.Equal(new LayoutRect(10, 220, 1086, 20), l.Rects[N.Explanation]);
        Assert.Equal((1106, 700), l.Canvas[N.DetailScroll]);
        Assert.Equal(new RecruitmentCanvasInfo(250, 700, false), l.Detail);
    }

    [Fact]   // LayoutWindow :419-430: narrower than 55 bodies: the heading, two fifths for the roster, the details below
    public void A_narrow_window_stacks_the_roster_over_the_details()
    {
        var l = Compute(View("Dust Bandit"), 1000, 800);
        // 880 x 704 at (60, 48); client 874 x 659; width 854 < 1100
        Assert.Equal(new LayoutRect(60, 48, 880, 704), l.Window);
        Assert.True(l.Stacked);
        Assert.Equal(new LayoutRect(10, 609, 183, 40), l.Rects[N.Close]);           // 659 - 10 - 40
        Assert.Equal(new LayoutRect(203, 609, 661, 40), l.Rects[N.Action]);         // 854 - 183 - 10
        Assert.Equal(new LayoutRect(10, 549, 854, 50), l.Rects[N.Status]);          // 609 - 10 - 50
        // contentHeight 549 - 10 - 70 = 469; splitHeight 469 - 20 - 20 = 429; rosterHeight 429 * 2 / 5 = 171
        Assert.Equal(new LayoutRect(10, 70, 854, 20), l.Rects[N.RosterHeading]);
        Assert.Equal(new LayoutRect(10, 100, 854, 171), l.Rects[N.RosterScroll]);
        // detailTop 70 + 20 + 20 + 171 = 281; height 70 + 469 - 281 = 258
        Assert.Equal(new LayoutRect(10, 281, 854, 258), l.Rects[N.DetailScroll]);
    }

    [Fact]   // :305-332: rows taller than the view bring the bar, which narrows the view: the second pass lays the rows at the narrower width
    public void Rows_taller_than_the_roster_take_the_bar_width_in_the_second_pass()
    {
        var l = Compute(View("A", "B", "C"), 1000, 800);
        // view height 171 - 15 = 156; pass 0 at 854 - 15 = 839: content 3 * 90 = 270 > 156: bar; pass 1 at 839 - 20 = 819
        Assert.Equal(new LayoutRect(0, 0, 819, 80), l.Rects[N.Row(0)]);
        Assert.Equal(new LayoutRect(0, 180, 819, 80), l.Rects[N.Row(2)]);
        Assert.Equal(new LayoutRect(80, 10, 729, 20), l.Rects[N.RowName(2)]);      // 819 - 80 - 10
        Assert.Equal((819, 270), l.Canvas[N.RosterScroll]);
        Assert.Equal(new RecruitmentCanvasInfo(270, 156, true), l.Roster);
        // :333-336 the offset clamped to [0, 270 - 156]
        Assert.Equal(114, l.Roster.Clamp(500));
        Assert.Equal(0, l.Roster.Clamp(-7));
        Assert.Equal(40, l.Roster.Clamp(40));
    }

    [Fact]   // :400-407: the status line is at most two lines tall; the window lays it out for the text it had at the last LayoutWindow
    public void The_status_line_is_capped_at_two_lines_and_follows_the_window_status()
    {
        var v = View("Dust Bandit") with { Status = new string('s', 200) };   // 2000 px in 1633: two lines
        var l = Compute(v);
        Assert.Equal(new LayoutRect(10, 775, 1663, 70), l.Rects[N.Status]);        // min(70, max(20, 40 + 30)) = 70; 855 - 10 - 70
        Assert.Equal(new LayoutRect(10, 100, 532, 665), l.Rects[N.RosterScroll]);   // contentHeight 775 - 80 = 695; 695 - 30
        var three = Compute(View("Dust Bandit") with { Status = new string('s', 400) });   // three lines: still 70
        Assert.Equal(new LayoutRect(10, 775, 1663, 70), three.Rects[N.Status]);
        // a new status text without a LayoutWindow (RefreshAll's else branch) keeps the old rectangles
        var stale = Compute(v, windowStatus: PrisonerRecruitmentUiTexts.StatusIdle);
        Assert.Equal(new LayoutRect(10, 795, 1663, 50), stale.Rects[N.Status]);
        Assert.Equal(new LayoutRect(10, 100, 532, 685), stale.Rects[N.RosterScroll]);
    }

    [Fact]   // no prisoner: no row, the detail column still laid out (its portrait hidden by the window half)
    public void No_rows_gives_an_empty_roster_canvas()
    {
        var l = Compute(View());
        Assert.DoesNotContain(N.Row(0), l.Rects.Keys);
        Assert.Equal((517, 670), l.Canvas[N.RosterScroll]);
        Assert.Equal(new RecruitmentCanvasInfo(0, 670, false), l.Roster);
        Assert.Equal(new LayoutRect(10, 160, 1086, 20), l.Rects[N.Name]);
    }

    [Fact]
    public void Row_names_parse_back_to_their_index()
    {
        Assert.Equal("PG_RecruitmentRow_3", N.Row(3));
        Assert.Equal("PG_RecruitmentPortrait_3", N.RowPortrait(3));
        Assert.Equal("PG_RecruitmentName_3", N.RowName(3));
        Assert.Equal("PG_RecruitmentMarks_3", N.RowMarks(3));
        Assert.True(N.TryRow("PG_RecruitmentRow_12", out int i));
        Assert.Equal(12, i);
        Assert.False(N.TryRow("PG_RecruitmentRow_", out _));
        Assert.False(N.TryRow("PG_RecruitmentRow_1x", out _));
        Assert.False(N.TryRow("PG_RecruitmentName_1", out _));
        Assert.Equal(UiButtonEvent.Click, N.ButtonEvent(N.Action));
    }
}
