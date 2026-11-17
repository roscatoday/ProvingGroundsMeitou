using ProvingGrounds.Core;
using static ProvingGrounds.Core.FightTargeting;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Hand cases for Core/FightTargeting.cs (FightStarter.cpp:132-135, 176-277 and the dispatch of :487-523 / :623-656): ties, all down,
/// one alive and the <c>slot mod living</c> wrap. The golden test covers random cases.</summary>
public class FightTargetingTests
{
    static Fighter Up(float x = 0, float z = 0) => new() { Present = true, Valid = true, X = x, Z = z };
    static Fighter Ko(float x = 0, float z = 0) => new() { Present = true, Valid = true, Unconscious = true, X = x, Z = z };
    static Fighter Dead(float x = 0, float z = 0) => new() { Present = true, Valid = true, Dead = true, Unconscious = true, X = x, Z = z };

    [Fact]
    public void LivingNeedsPointerValidityAndConsciousness()
    {
        // C++ :134 c && c->isValid() && !c->isDead() && !c->isUnconcious()
        Assert.True(IsLivingFighter(Up()));
        Assert.False(IsLivingFighter(default));
        Assert.False(IsLivingFighter(new Fighter { Present = true }));
        Assert.False(IsLivingFighter(Ko()));
        Assert.False(IsLivingFighter(Dead()));
    }

    [Fact]
    public void NearestTieKeepsTheFirstIndex()
    {
        // C++ :233 `distSq < nearestDistSq` (strict): of two opponents at the same distance the lower index wins
        Fighter[] f = [Up(0, 0), Up(3, 4), Up(-3, -4)];
        MatchTeam[] t = [MatchTeam.None, MatchTeam.None, MatchTeam.None];
        Assert.Equal(1, FindNearestLivingOpponent(MatchMode.LastStanding, 0, f, t, 3));
        Assert.Equal(1, Choose(MatchMode.LastStanding, f, t, 3, 0));
    }

    [Fact]
    public void NearestIgnoresHeight()
    {
        // C++ :205-210 horizontal: y is not in the distance
        Fighter[] f = [Up(0, 0), new Fighter { Present = true, Valid = true, X = 1, Y = 500 }, Up(2, 0)];
        MatchTeam[] t = new MatchTeam[3];
        Assert.Equal(1, FindNearestLivingOpponent(MatchMode.LastStanding, 0, f, t, 3));
    }

    [Fact]
    public void AllDownGivesMinusOne()
    {
        Fighter[] f = [Up(), Ko(1), Dead(2), default];
        MatchTeam[] t = [MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamB, MatchTeam.TeamB];
        foreach (var mode in new[] { MatchMode.TeamAvB, MatchMode.Teams1v1, MatchMode.LastStanding, (MatchMode)3 })
            Assert.Equal(-1, Choose(mode, f, t, 4, 0));
        Assert.Equal(-1, FindMatchedLivingTeamOpponent(MatchMode.TeamAvB, 0, f, t, 4));
    }

    [Fact]
    public void OneAliveIsChosenWhereverItStands()
    {
        Fighter[] f = [Up(), Ko(1), Up(50, 50), Ko(2)];
        MatchTeam[] t = [MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamB, MatchTeam.TeamB];
        Assert.Equal(2, FindMatchedLivingTeamOpponent(MatchMode.TeamAvB, 0, f, t, 4));
        Assert.Equal(2, FindNearestLivingOpponent(MatchMode.LastStanding, 0, f, t, 4));
    }

    [Fact]
    public void TeamSlotWrapsOverTheLivingOpponents()
    {
        // C++ :249-276: attackerSlot = how many of its team come before it; target = the (slot mod living)-th living opponent in index order
        // A A A B B: the three A's are slots 0, 1, 2; two living B's: slot 0 -> B#3, slot 1 -> B#4, slot 2 wraps -> B#3
        Fighter[] f = [Up(), Up(), Up(), Up(), Up()];
        MatchTeam[] t = [MatchTeam.TeamA, MatchTeam.TeamA, MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamB];
        Assert.Equal(3, FindMatchedLivingTeamOpponent(MatchMode.TeamAvB, 0, f, t, 5));
        Assert.Equal(4, FindMatchedLivingTeamOpponent(MatchMode.TeamAvB, 1, f, t, 5));
        Assert.Equal(3, FindMatchedLivingTeamOpponent(MatchMode.TeamAvB, 2, f, t, 5));
        // B's: slots 0 and 1 over three living A's
        Assert.Equal(0, FindMatchedLivingTeamOpponent(MatchMode.TeamAvB, 3, f, t, 5));
        Assert.Equal(1, FindMatchedLivingTeamOpponent(MatchMode.TeamAvB, 4, f, t, 5));
    }

    [Fact]
    public void TeamSlotCountsDownedTeammatesButNotDownedOpponents()
    {
        // The slot counts every earlier teammate (alive or not, C++ :251-255); the living count skips downed opponents.
        Fighter[] f = [Ko(), Up(), Up(), Ko(), Up()];
        MatchTeam[] t = [MatchTeam.TeamA, MatchTeam.TeamA, MatchTeam.TeamB, MatchTeam.TeamB, MatchTeam.TeamB];
        // fighter 1 is A slot 1; living B's are 2 and 4: 1 % 2 = 1 -> index 4
        Assert.Equal(4, FindMatchedLivingTeamOpponent(MatchMode.TeamAvB, 1, f, t, 5));
    }

    [Fact]
    public void TeamNoneHasNoOpponentInTeamModes()
    {
        Fighter[] f = [Up(), Up()];
        MatchTeam[] t = [MatchTeam.None, MatchTeam.TeamB];
        Assert.Equal(-1, Choose(MatchMode.TeamAvB, f, t, 2, 0));
        Assert.Equal(1, Choose(MatchMode.LastStanding, f, t, 2, 0));   // every other fighter is an opponent
    }

    [Fact]
    public void NoAttackerPointerHasNoNearest()
    {
        // C++ :219-221: a null attacker returns -1 (the matched search has no such check)
        Fighter[] f = [default, Up()];
        MatchTeam[] t = [MatchTeam.TeamA, MatchTeam.TeamB];
        Assert.Equal(-1, FindNearestLivingOpponent(MatchMode.LastStanding, 0, f, t, 2));
        Assert.Equal(1, FindMatchedLivingTeamOpponent(MatchMode.TeamAvB, 0, f, t, 2));
    }

    [Fact]
    public void GenericModeTakesTheFirstOpponent()
    {
        Fighter[] f = [Up(), Ko(), Up(9), Up(1)];
        MatchTeam[] t = new MatchTeam[4];
        Assert.Equal(2, Choose((MatchMode)3, f, t, 4, 0));
    }

    [Fact]
    public void KeepAliveHealthyOnlyWithValidTargetAndCombat()
    {
        // C++ :619-621
        Assert.True(KeepsTarget(true, true, false));
        Assert.True(KeepsTarget(true, false, true));
        Assert.False(KeepsTarget(true, false, false));   // an NPC whose combat task stalled
        Assert.False(KeepsTarget(false, true, true));
    }

    [Fact]
    public void ReactOnlyInOpenModesToAnOpponentNotAlreadyTargeted()
    {
        // C++ :575-584
        Assert.True(ShouldReact(true, true, true, MatchMode.TeamAvB, false, false, true, false));
        Assert.True(ShouldReact(true, true, true, MatchMode.LastStanding, false, false, true, false));
        Assert.False(ShouldReact(true, true, true, MatchMode.Teams1v1, false, false, true, false));
        Assert.False(ShouldReact(true, true, true, MatchMode.TeamAvB, false, false, true, true));
        Assert.False(ShouldReact(true, true, false, MatchMode.TeamAvB, false, false, true, false));
        Assert.False(ShouldReact(false, true, true, MatchMode.TeamAvB, false, false, true, false));
        Assert.False(ShouldReact(true, false, true, MatchMode.TeamAvB, false, false, true, false));
        Assert.False(ShouldReact(true, true, true, MatchMode.TeamAvB, true, false, true, false));
        Assert.False(ShouldReact(true, true, true, MatchMode.TeamAvB, false, true, true, false));
        Assert.False(ShouldReact(true, true, true, MatchMode.TeamAvB, false, false, false, false));
    }
}
