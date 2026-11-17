using ProvingGrounds.Core;
using static ProvingGrounds.Core.RewardProgression;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for the parse rules the golden vectors leave out on purpose: number overflow (MSVC strtoul is 32-bit and
/// saturates; MSVC atoi saturates), plus LeaderboardData's C-string id compare (RewardsDialoguePolicy is in RewardsDialoguePolicyTests).</summary>
public class RewardProgressionTests
{
    [Fact]
    public void Atoi_follows_msvc()
    {
        Assert.Equal(0, AtoiMsvc(""));
        Assert.Equal(0, AtoiMsvc("x"));
        Assert.Equal(12, AtoiMsvc(" \t\n\v\f\r12abc"));
        Assert.Equal(-7, AtoiMsvc("-7"));
        Assert.Equal(7, AtoiMsvc("+7"));
        Assert.Equal(0, AtoiMsvc("- 7"));
        Assert.Equal(int.MaxValue, AtoiMsvc("2147483647"));
        Assert.Equal(int.MaxValue, AtoiMsvc("2147483648"));
        Assert.Equal(int.MaxValue, AtoiMsvc("99999999999999999999999"));
        Assert.Equal(int.MinValue, AtoiMsvc("-2147483648"));
        Assert.Equal(int.MinValue, AtoiMsvc("-2147483649"));
    }

    [Fact]
    public void Huge_lengths_are_rejected()
    {
        var e = new List<TierEntry> { new TierEntry("keep", 1) };
        // strtoul saturates at 4294967295 (MSVC unsigned long); cursor + length > size -> false (ReadLength, RewardProgression.h:131)
        Assert.False(DecodeTiers("99999999999999999999:a=1;", e));
        Assert.False(DecodeTiers("4294967296:a=1;", e));
        Assert.False(DecodeUnlocks("18446744073709551615:a;", new List<string>()));
        Assert.Single(e);
        Assert.Equal("keep", e[0].Id);
    }

    [Fact]
    public void Saturated_tier_parses_as_int_max()
    {
        var e = new List<TierEntry>();
        Assert.True(DecodeTiers("1:a=99999999999;", e));
        Assert.Equal(int.MaxValue, e[0].Tier);
        Assert.False(DecodeTiers("1:a=-99999999999;", e));   // INT_MIN < 0
    }

    [Fact]
    public void Default_entry_is_the_cpp_default()
    {
        var t = new TierEntry();
        Assert.Equal("", t.Id);
        Assert.Equal(-1, t.Tier);
    }

    [Fact]
    public void SetHighestTier_chain()
    {
        var e = new List<TierEntry>();
        Assert.False(SetHighestTier(e, "", -1, 0));
        Assert.False(SetHighestTier(e, "a", -1, -1));
        Assert.False(SetHighestTier(e, "a", 0, 0));      // not previous + 1
        Assert.False(SetHighestTier(e, "a", 0, 1));      // no row and previous != -1
        Assert.True(SetHighestTier(e, "a", -1, 2));      // -1 skips the +1 rule
        Assert.False(SetHighestTier(e, "a", -1, 3));     // row tier is 2, not -1
        Assert.True(SetHighestTier(e, "a", 2, 3));
        Assert.Equal(3, HighestTier(e, "a"));
        Assert.True(RestoreHighestTier(e, "a", -1));
        Assert.Empty(e);
        Assert.False(RestoreHighestTier(e, "a", -1));
        Assert.False(RestoreHighestTier(e, "", 1));
    }

    [Fact]
    public void Leaderboard_id_compare_is_a_c_string_compare()
    {
        Assert.True(LeaderboardData.IdEquals("53-Proving Grounds.mod\0x", "53-Proving Grounds.mod"));
        Assert.False(LeaderboardData.IdEquals(null, null));
        Assert.True(LeaderboardData.IdEquals("", ""));
        Assert.Equal(LeaderboardData.Kind.Town, LeaderboardData.KindForId("273-Proving Grounds.mod"));
        Assert.Equal(LeaderboardData.Kind.Player, LeaderboardData.KindForId(null));
        Assert.Equal(9, LeaderboardData.CurrentVersion);
        var s = new LeaderboardData.Standing();
        Assert.Equal(100.0f, s.Mmr);
        var data = new LeaderboardData.Collections();
        LeaderboardData.EnsureStanding(data, LeaderboardData.Kind.Town, "id", "Old");
        LeaderboardData.EnsureStanding(data, LeaderboardData.Kind.Town, "id", "");     // empty name keeps the old one
        Assert.Equal("Old", data.TownStandings[0].Name);
        Assert.Empty(data.PlayerStandings);
    }

    [Fact]
    public void Collections_DeepClone_shares_no_rows_or_lists()
    {
        var data = new LeaderboardData.Collections();
        LeaderboardData.ApplyCompetitiveResult(data, LeaderboardData.Kind.Player, "p", "P", 120.5f, 1, 0);
        LeaderboardData.ApplyCompetitiveResult(data, LeaderboardData.Kind.Town, "t", "T", 90f, 0, 1);
        LeaderboardData.AddMarks(data, "p", "P", 30);
        data.Progression[0].RewardTiers.Add(new TierEntry("weapon.mace", 2));
        data.FactionUnlocks.Add("armour.retainer");

        LeaderboardData.Collections copy = data.DeepClone();
        Assert.NotSame(data.PlayerStandings, copy.PlayerStandings);
        Assert.NotSame(data.TownStandings, copy.TownStandings);
        Assert.NotSame(data.Progression, copy.Progression);
        Assert.NotSame(data.FactionUnlocks, copy.FactionUnlocks);
        Assert.NotSame(data.PlayerStandings[0], copy.PlayerStandings[0]);
        Assert.NotSame(data.TownStandings[0], copy.TownStandings[0]);
        Assert.NotSame(data.Progression[0], copy.Progression[0]);
        Assert.NotSame(data.Progression[0].RewardTiers, copy.Progression[0].RewardTiers);
        Assert.Equal(120.5f, copy.PlayerStandings[0].Mmr);
        Assert.Equal(30, copy.Progression[0].Marks);

        copy.PlayerStandings[0].Wins = 99;
        copy.Progression[0].RewardTiers[0] = new TierEntry("weapon.mace", 3);
        copy.Progression[0].Marks = 1;
        copy.FactionUnlocks.Clear();
        Assert.Equal(1, data.PlayerStandings[0].Wins);
        Assert.Equal(2, data.Progression[0].RewardTiers[0].Tier);
        Assert.Equal(30, data.Progression[0].Marks);
        Assert.Single(data.FactionUnlocks);
    }
}
