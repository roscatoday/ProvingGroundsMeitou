using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/FightDisengagePolicy.cs (task 1.5); each expected value is read off the C++.

public class FightDisengagePolicyTests
{
    [Fact]   // FightDisengagePolicy.h:8-53
    public void Plans_and_step_order()
    {
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, Enum.GetValues<FightDisengagePolicy.Step>().Select(s => (int)s));
        var full = FightDisengagePolicy.BuildFullDisengagePlan();
        Assert.True(full.ClearTempEnemies && full.RemoveFocusedMeleeJobs && full.ClearPlayerAttackOrders && full.EndCombatMode
                    && full.ClearAiGoals && full.RethinkAi && full.RestoreStandingOrders);
        var re = FightDisengagePolicy.BuildInMatchRetargetPlan();
        Assert.True(re.ClearTempEnemies && re.RemoveFocusedMeleeJobs && re.ClearPlayerAttackOrders);
        Assert.False(re.EndCombatMode || re.ClearAiGoals || re.RethinkAi || re.RestoreStandingOrders);
    }
}
