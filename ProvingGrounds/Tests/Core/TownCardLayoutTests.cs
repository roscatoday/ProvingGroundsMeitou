using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand-computed cases for Core/TownCardLayout.cs (worked by reading TownCardLayout.h). The C++ vectors are in
/// Tests/Golden/LayoutsGoldenTests.cs.</summary>
public class TownCardLayoutTests
{
    [Fact]
    public void Fit_packs_as_many_minimum_width_cards_as_fit_up_to_five()
    {
        TownCardLayout.Grid g = TownCardLayout.Fit(1000, 300, 10);   // (1000 + 10) / 310 = 3 columns, (1000 - 20) / 3 = 326 wide
        Assert.Equal((3, 326, 10), (g.Columns, g.Width, g.Gap));
        TownCardLayout.Grid many = TownCardLayout.Fit(1000, 100, 0);   // 10 columns, capped at 5
        Assert.Equal((5, 200, 0), (many.Columns, many.Width, many.Gap));
    }

    [Fact]
    public void Fit_clamps_bad_input_to_one_pixel_one_column_no_gap()
    {
        TownCardLayout.Grid g = TownCardLayout.Fit(0, 0, -3);
        Assert.Equal((1, 1, 0), (g.Columns, g.Width, g.Gap));
    }

    [Fact]
    public void SingleRow_keeps_the_fitted_width_for_any_number_of_cards()
    {
        TownCardLayout.Grid g = TownCardLayout.SingleRow(1000, 300, 10, 7);
        Assert.Equal((7, 326, 10), (g.Columns, g.Width, g.Gap));
        Assert.Equal(1, TownCardLayout.SingleRow(1000, 300, 10, 0).Columns);
        Assert.Equal(1, TownCardLayout.SingleRow(1000, 300, 10, -4).Columns);
        Assert.Equal(0, TownCardLayout.SingleRow(1000, 300, -10, 2).Gap);
    }

    [Fact]
    public void Grid_members_place_cards_in_rows_and_columns()
    {
        var g = new TownCardLayout.Grid { Columns = 3, Width = 326, Gap = 10 };
        Assert.Equal((1, 1, 336), (g.Column(4), g.Row(4), g.Left(4)));
        Assert.Equal((0, 2, 0), (g.Column(6), g.Row(6), g.Left(6)));
        Assert.Equal(998, g.ContentWidth(3));   // 3 * 326 + 2 * 10
        Assert.Equal(326, g.ContentWidth(1));
        Assert.Equal(0, g.ContentWidth(0));
        Assert.Equal(0, g.ContentWidth(-2));
        // a negative index truncates toward zero like the C++ % and /
        Assert.Equal((-1, 0), (g.Column(-1), g.Row(-1)));
    }

    [Fact]
    public void ClampViewOffset_keeps_the_scroll_offset_between_minus_the_scrollable_size_and_zero()
    {
        Assert.Equal(-50, TownCardLayout.ClampViewOffset(-50, 1000, 400));
        Assert.Equal(-600, TownCardLayout.ClampViewOffset(-900, 1000, 400));
        Assert.Equal(0, TownCardLayout.ClampViewOffset(30, 1000, 400));    // a positive offset is not a scroll
        Assert.Equal(0, TownCardLayout.ClampViewOffset(-10, 300, 400));    // content shorter than the view
    }
}
