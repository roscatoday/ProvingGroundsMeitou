using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The grade-recognition rules of RewardDelivery.cpp (LevelsMatch :169-196; FindExactGrade :230-273; HasAnyGrade :275-305;
/// FindPreviousGrade :307-357; the cloak's alternate record :87-93, :359-394; ResolveRewardItemData :711-723) over plain candidate lists
/// (task 5.2a, Core/Glue/RewardGrades.cs).</summary>
public class RewardGradesTests
{
    [Theory]
    [InlineData(40, 40)] [InlineData(55, 55)] [InlineData(95, 95)] [InlineData(0, 0)]
    // weapon colour-band migration (:175-186)
    [InlineData(70, 50)] [InlineData(75, 70)] [InlineData(50, 55)] [InlineData(50, 75)] [InlineData(70, 55)] [InlineData(70, 75)]
    // pre-fix manufacturer indices (:188-194), both ways
    [InlineData(3, 60)] [InlineData(60, 3)] [InlineData(4, 80)] [InlineData(80, 4)] [InlineData(5, 95)] [InlineData(95, 5)]
    public void LevelsMatch(int have, int want) => Assert.True(RewardGrades.LevelsMatch(have, want));

    [Theory]
    [InlineData(40, 60)] [InlineData(55, 75)] [InlineData(75, 95)] [InlineData(55, 70)] [InlineData(75, 55)] [InlineData(50, 70)]
    [InlineData(55, 50)] [InlineData(3, 40)] [InlineData(60, 4)] [InlineData(3, 80)] [InlineData(4, 95)] [InlineData(95, 3)]
    [InlineData(70, 95)] [InlineData(80, 75)]
    public void LevelsDoNotMatch(int have, int want) => Assert.False(RewardGrades.LevelsMatch(have, want));

    [Fact]
    public void FindExactTakesTheFirstMatchingLevel()
    {
        Assert.Equal(1, RewardGrades.FindExact([40, 60, 60], 60));
        Assert.Equal(0, RewardGrades.FindExact([3, 60], 60));   // an old index-3 item counts as High Grade
        Assert.Equal(-1, RewardGrades.FindExact([40, 80], 60));
        Assert.Equal(0, RewardGrades.FindExact([70, 50], 50));   // an older 70 (colour band) comes first and counts as 50
        Assert.Equal(-1, RewardGrades.FindExact([], 60));
    }

    [Fact]
    public void FindPreviousFallsBackToTheFirstCopy()
    {
        Assert.Equal(1, RewardGrades.FindPrevious([40, 60], 60));   // exact wins over the first copy
        Assert.Equal(0, RewardGrades.FindPrevious([40, 80], 60));   // no exact: the first copy (exchange after a confirmed upgrade offer)
        Assert.Equal(-1, RewardGrades.FindPrevious([], 60));
    }

    [Fact]
    public void TheCloakIsAlwaysRecord89()
    {
        // S13: Meitou has no neck section, so the original's choice without its plugin (RewardDelivery.cpp:715-719): 89, never 72
        Assert.Equal(PgRecords.WarlordCloak, RewardGrades.ItemRecord(ArenaRewards.Piece.Cloak));
        Assert.Equal(PgRecords.WarlordCloakNeck, RewardGrades.AlternateRecord(ArenaRewards.Piece.Cloak, PgRecords.WarlordCloak));
        Assert.Equal(PgRecords.WarlordCloak, RewardGrades.AlternateRecord(ArenaRewards.Piece.Cloak, PgRecords.WarlordCloakNeck));
        Assert.Null(RewardGrades.AlternateRecord(ArenaRewards.Piece.PlateArmour, PgRecords.WarlordPlateArmour));
    }

    [Fact]
    public void OtherPiecesUseTheirFirstTiersRecord()
    {
        Assert.Equal(PgRecords.WarlordPlateArmour, RewardGrades.ItemRecord(ArenaRewards.Piece.PlateArmour));
        // weapons start at High Grade (their Standard row is empty)
        Assert.Equal(PgRecords.WarlordLongsword, RewardGrades.ItemRecord(ArenaRewards.Piece.WarlordLongsword));
        Assert.Equal(PgRecords.WarlordCleaver, RewardGrades.ItemRecord(ArenaRewards.Piece.Hacker));
    }

    [Fact]
    public void RewardFindersTryThePrimaryRecordThenTheCloaksAlternate()
    {
        // FindExactRewardGrade :359-370: the primary's list first; only the cloak looks at its alternate record
        Assert.Equal((false, 0), RewardGrades.FindExactReward(ArenaRewards.Piece.Cloak, [60], [60], 60));
        Assert.Equal((true, 1), RewardGrades.FindExactReward(ArenaRewards.Piece.Cloak, [40], [40, 60], 60));
        Assert.Equal((false, -1), RewardGrades.FindExactReward(ArenaRewards.Piece.Helmet, [40], [60], 60));
        // an older level in the primary record (index 3 = High Grade) wins over an exact copy of the alternate record
        Assert.Equal((false, 0), RewardGrades.FindExactReward(ArenaRewards.Piece.Cloak, [3], [60], 60));
        // FindPreviousRewardGrade :383-394: the primary's fallback copy wins over an exact alternate
        Assert.Equal((false, 0), RewardGrades.FindPreviousReward(ArenaRewards.Piece.Cloak, [40], [60], 60));
        Assert.Equal((true, 0), RewardGrades.FindPreviousReward(ArenaRewards.Piece.Cloak, [], [95], 60));
        Assert.Equal((false, -1), RewardGrades.FindPreviousReward(ArenaRewards.Piece.Helmet, [], [95], 60));
        // HasAnyRewardGrade :372-382
        Assert.True(RewardGrades.HasAnyReward(ArenaRewards.Piece.Cloak, 0, 1));
        Assert.False(RewardGrades.HasAnyReward(ArenaRewards.Piece.Helmet, 0, 1));
        Assert.True(RewardGrades.HasAnyReward(ArenaRewards.Piece.Helmet, 2, 0));
    }
}
