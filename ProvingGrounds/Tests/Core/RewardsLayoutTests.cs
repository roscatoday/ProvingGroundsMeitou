using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand-computed cases for Core/RewardsLayout.cs (worked by reading RewardsLayout.h). The C++ vectors are in
/// Tests/Golden/LayoutsGoldenTests.cs.</summary>
public class RewardsLayoutTests
{
    [Fact]
    public void ShouldStack_below_the_larger_of_720_and_fifty_bodies()
    {
        Assert.True(RewardsLayout.ShouldStack(719, 10));
        Assert.False(RewardsLayout.ShouldStack(720, 10));
        Assert.True(RewardsLayout.ShouldStack(999, 20));    // 20 * 50 = 1000
        Assert.False(RewardsLayout.ShouldStack(1000, 20));
    }

    [Fact]
    public void FighterWidth_is_21_percent_of_the_content_between_a_floor_and_320()
    {
        Assert.Equal(210, RewardsLayout.FighterWidth(1000, 10));
        Assert.Equal(320, RewardsLayout.FighterWidth(2000, 10));
        Assert.Equal(220, RewardsLayout.FighterWidth(100, 20));   // floor = max(160, 20 * 11)
        Assert.Equal(160, RewardsLayout.FighterWidth(0, 0));
    }

    [Fact]
    public void GridColumns_counts_whole_cards_and_falls_back_to_one_on_bad_input()
    {
        Assert.Equal(4, RewardsLayout.GridColumns(900, 200, 10, 4));
        Assert.Equal(2, RewardsLayout.GridColumns(900, 200, 10, 2));
        Assert.Equal(1, RewardsLayout.GridColumns(100, 200, 10, 4));
        Assert.Equal(1, RewardsLayout.GridColumns(900, 0, 10, 4));
        Assert.Equal(1, RewardsLayout.GridColumns(900, 200, 10, 0));
        Assert.Equal(4, RewardsLayout.GridColumns(900, 200, -5, 4));   // a negative gap counts as zero
    }

    [Fact]
    public void FillRowHeight_shares_the_view_between_the_rows_but_never_goes_below_the_minimum()
    {
        Assert.Equal(235, RewardsLayout.FillRowHeight(500, 2, 10, 100));   // (500 - 3 * 10) / 2
        Assert.Equal(100, RewardsLayout.FillRowHeight(100, 2, 10, 100));
        Assert.Equal(100, RewardsLayout.FillRowHeight(500, 0, 10, 100));
        Assert.Equal(1, RewardsLayout.FillRowHeight(500, 0, 10, -5));
    }

    [Fact]
    public void RarityIndex_is_three_when_complete_otherwise_the_tier_clamped_to_0_3()
    {
        Assert.Equal(2, RewardsLayout.RarityIndex(2, false));
        Assert.Equal(3, RewardsLayout.RarityIndex(7, false));
        Assert.Equal(0, RewardsLayout.RarityIndex(-1, false));
        Assert.Equal(3, RewardsLayout.RarityIndex(0, true));
    }

    [Fact]
    public void CompactRowHeight_halves_the_roomy_height_down_to_the_minimum()
    {
        Assert.Equal(117, RewardsLayout.CompactRowHeight(235, 100));
        Assert.Equal(200, RewardsLayout.CompactRowHeight(300, 200));
        Assert.Equal(1, RewardsLayout.CompactRowHeight(0, 0));
    }

    [Fact]
    public void FightSupplyColumns_is_two_from_two_supplies()
    {
        Assert.Equal(1, RewardsLayout.FightSupplyColumns(0));
        Assert.Equal(1, RewardsLayout.FightSupplyColumns(1));
        Assert.Equal(2, RewardsLayout.FightSupplyColumns(2));
    }

    [Fact]
    public void Min_and_Max()
    {
        Assert.Equal(-3, RewardsLayout.Min(-3, 4));
        Assert.Equal(4, RewardsLayout.Max(-3, 4));
        Assert.Equal(5, RewardsLayout.Min(5, 5));
        Assert.Equal(5, RewardsLayout.Max(5, 5));
    }
}
