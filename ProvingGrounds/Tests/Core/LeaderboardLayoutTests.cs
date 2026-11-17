using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand-computed cases for Core/LeaderboardLayout.cs (worked by reading LeaderboardLayout.h) and the value semantics of its
/// structs. The C++ vectors are in Tests/Golden/LayoutsGoldenTests.cs.</summary>
public class LeaderboardLayoutTests
{
    [Fact]
    public void FitColumns_lays_the_columns_out_left_to_right_and_gives_the_slack_to_the_fighter_column()
    {
        // required = 4 gaps of 8 + 40+150+60+70+50 = 402; 500 available leaves 98 spare for the fighter column
        LeaderboardLayout.Columns r = LeaderboardLayout.FitColumns(500, 8, new[] { 40, 150, 60, 70, 50 });
        Assert.False(r.Stacked);
        Assert.Equal(new[] { 0, 48, 304, 372, 450 }, ((ReadOnlySpan<int>)r.Left).ToArray());
        Assert.Equal(new[] { 40, 248, 60, 70, 50 }, ((ReadOnlySpan<int>)r.Width).ToArray());
    }

    [Fact]
    public void FitColumns_stacks_below_the_required_width_and_returns_all_zeros()
    {
        LeaderboardLayout.Columns r = LeaderboardLayout.FitColumns(401, 8, new[] { 40, 150, 60, 70, 50 });
        Assert.True(r.Stacked);
        Assert.All(((ReadOnlySpan<int>)r.Left).ToArray(), v => Assert.Equal(0, v));
        Assert.All(((ReadOnlySpan<int>)r.Width).ToArray(), v => Assert.Equal(0, v));
        // exactly the required width does not stack
        Assert.False(LeaderboardLayout.FitColumns(402, 8, new[] { 40, 150, 60, 70, 50 }).Stacked);
    }

    [Fact]
    public void Column_count_is_five()
    {
        // the [InlineArray(5)] length of LeaderboardLayout.ColumnInts must equal the enum's sentinel
        Assert.Equal(5, (int)LeaderboardLayout.Column.ColumnCount);
        LeaderboardLayout.ColumnInts probe = default;
        Assert.Equal(5, ((ReadOnlySpan<int>)probe).Length);
    }

    [Fact]
    public void Equals_on_a_struct_holding_an_InlineArray_throws_on_this_runtime()
    {
        // pins the .NET 10 behaviour behind the "do not call Equals" remark on Columns/Podium/Ceremony: if the runtime changes, this shows it
        Assert.Throws<NotSupportedException>(() => default(LeaderboardLayout.Columns).Equals(default(LeaderboardLayout.Columns)));
    }

    [Fact]
    public void Column_enum_keeps_the_original_members_and_values()
    {
        Assert.Equal(0, (int)LeaderboardLayout.Column.Rank);
        Assert.Equal(1, (int)LeaderboardLayout.Column.Fighter);
        Assert.Equal(2, (int)LeaderboardLayout.Column.Rating);
        Assert.Equal(3, (int)LeaderboardLayout.Column.Record);
        Assert.Equal(4, (int)LeaderboardLayout.Column.Matches);
        Assert.Equal(5, (int)LeaderboardLayout.Column.ColumnCount);
    }

    [Fact]
    public void ClampOffset_clamps_between_zero_and_the_scrollable_height()
    {
        Assert.Equal(0, LeaderboardLayout.ClampOffset(-5, 1000, 400));
        Assert.Equal(100, LeaderboardLayout.ClampOffset(100, 1000, 400));
        Assert.Equal(600, LeaderboardLayout.ClampOffset(900, 1000, 400));
        Assert.Equal(0, LeaderboardLayout.ClampOffset(50, 300, 400));   // content shorter than the view: nothing scrolls
    }

    [Fact]
    public void FitVertical_splits_the_height_into_context_heading_scroll_and_close_bands()
    {
        LeaderboardLayout.Vertical v = LeaderboardLayout.FitVertical(600, 8, 24, 40, 40);
        Assert.Equal(40, v.ContextHeight);
        Assert.Equal(56, v.HeadingTop);
        Assert.Equal(24, v.HeadingHeight);
        Assert.Equal(88, v.ScrollTop);
        Assert.Equal(456, v.ScrollHeight);
        Assert.Equal(552, v.CloseTop);
        Assert.Equal(40, v.CloseHeight);
        Assert.Equal(default, LeaderboardLayout.FitVertical(0, 8, 24, 40, 40));   // height <= 0: all zeros
    }

    [Fact]
    public void FitCeremonialMetrics_applies_the_floors()
    {
        LeaderboardLayout.CeremonialMetrics small = LeaderboardLayout.FitCeremonialMetrics(16, 8);
        Assert.Equal((96, 72, 40, 4, 2), (small.ChampionPortrait, small.OtherPortrait, small.RowPortrait, small.RowGap, small.Edge));
        LeaderboardLayout.CeremonialMetrics big = LeaderboardLayout.FitCeremonialMetrics(30, 20);
        Assert.Equal((150, 120, 60, 10, 5), (big.ChampionPortrait, big.OtherPortrait, big.RowPortrait, big.RowGap, big.Edge));
    }

    [Fact]
    public void FitTitleRails_centres_the_title_between_two_rails()
    {
        LeaderboardLayout.TitleRails r = LeaderboardLayout.FitTitleRails(1000, 8, 300);
        Assert.Equal((350, 300, 334, 666, 334), (r.TitleLeft, r.TitleWidth, r.LeftWidth, r.RightLeft, r.RightWidth));
        // a title too wide for rails: both rails vanish together
        LeaderboardLayout.TitleRails wide = LeaderboardLayout.FitTitleRails(1000, 8, 5000);
        Assert.Equal((8, 984, 0, 1008, 0), (wide.TitleLeft, wide.TitleWidth, wide.LeftWidth, wide.RightLeft, wide.RightWidth));
        Assert.Equal(default, LeaderboardLayout.FitTitleRails(0, 8, 300));
    }

    [Fact]
    public void FitPodium_puts_first_place_in_the_middle_of_three()
    {
        LeaderboardLayout.Podium p = LeaderboardLayout.FitPodium(1000, 8, 3, 100, 80);
        Assert.False(p.Stacked);
        Assert.Equal(3, p.Count);
        Assert.Equal(164, p.ContentHeight);
        // cell = min((1000 - 16) / 3, 100 + 160) = 260; used = 796; first left = 102
        Assert.Equal((1, 2, 102, 0, 260, 80), Of(p.Slots[0]));
        Assert.Equal((0, 1, 370, 0, 260, 100), Of(p.Slots[1]));
        Assert.Equal((2, 3, 638, 0, 260, 80), Of(p.Slots[2]));
    }

    [Fact]
    public void FitPodium_stacks_when_the_cells_do_not_fit_side_by_side()
    {
        LeaderboardLayout.Podium p = LeaderboardLayout.FitPodium(300, 8, 3, 100, 80);
        Assert.True(p.Stacked);
        Assert.Equal(468, p.ContentHeight);
        Assert.Equal((0, 1, 0, 0, 300, 100), Of(p.Slots[0]));
        Assert.Equal((1, 2, 0, 172, 300, 80), Of(p.Slots[1]));
        Assert.Equal((2, 3, 0, 324, 300, 80), Of(p.Slots[2]));
    }

    [Fact]
    public void FitPodium_with_two_standings_keeps_the_natural_order_and_leaves_the_third_slot_zero()
    {
        LeaderboardLayout.Podium p = LeaderboardLayout.FitPodium(1000, 8, 2, 100, 80);
        Assert.Equal(2, p.Count);
        Assert.Equal((0, 1), (p.Slots[0].SourceIndex, p.Slots[0].Rank));
        Assert.Equal((1, 2), (p.Slots[1].SourceIndex, p.Slots[1].Rank));
        Assert.Equal(default, p.Slots[2]);
        Assert.Equal(0, LeaderboardLayout.FitPodium(1000, 8, 0, 100, 80).Count);
        Assert.Equal(3, LeaderboardLayout.FitPodium(1000, 8, 9, 100, 80).Count);   // clamped to three places
        Assert.Equal(0, LeaderboardLayout.FitPodium(1000, 8, -2, 100, 80).Count);
    }

    [Fact]
    public void Structs_with_fixed_arrays_copy_by_value()
    {
        // the C++ structs hold int[5] / PodiumSlot[3] by value; a copy must not alias the original
        LeaderboardLayout.Columns a = LeaderboardLayout.FitColumns(500, 8, new[] { 40, 150, 60, 70, 50 });
        LeaderboardLayout.Columns b = a;
        b.Left[0] = 99;
        b.Width[4] = 77;
        Assert.Equal(0, a.Left[0]);
        Assert.Equal(50, a.Width[4]);
        LeaderboardLayout.Podium p = LeaderboardLayout.FitPodium(1000, 8, 3, 100, 80);
        LeaderboardLayout.Podium q = p;
        q.Slots[1].Width = 1;
        Assert.Equal(260, p.Slots[1].Width);
        // a default struct is the C++ constructor's all-zero one
        LeaderboardLayout.Columns zero = default;
        Assert.False(zero.Stacked);
        Assert.Equal(0, zero.Left[4]);
        Assert.Equal(0, zero.Width[4]);
    }

    static (int, int, int, int, int, int) Of(LeaderboardLayout.PodiumSlot s) => (s.SourceIndex, s.Rank, s.Left, s.Top, s.Width, s.Portrait);
}
