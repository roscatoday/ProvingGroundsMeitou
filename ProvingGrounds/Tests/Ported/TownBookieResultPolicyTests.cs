using ProvingGrounds.Core;
using B = ProvingGrounds.Core.TownBettingPolicy;

namespace ProvingGrounds.Tests.Ported;

/// <summary>Port of upstream tests/TownBookieResultPolicyTests.cpp (one [Fact] per block, same values).</summary>
public class TownBookieResultPolicyTests
{
    [Fact]   // TownBookieResultPolicyTests.cpp:7-14
    public void Stake_adjustment_and_acceptance_follow_the_configured_range()
    {
        var open = new B.Ticket();
        Assert.Equal(110, B.AdjustStake(100, 10));
        Assert.Equal(10, B.AdjustStake(100, -100));
        Assert.Equal(10000, B.AdjustStake(9500, 1000));
        Assert.Equal(10, B.AdjustStake(10, -10));
        Assert.True(B.CanAccept(open, true, true, 0, 10, 10000));
        Assert.True(B.CanAccept(open, true, true, 0, 10000, 10000));
        Assert.False(B.CanAccept(open, true, true, 0, 10010, 20000));
        Assert.Equal(18000, B.ReturnCats(10000, 100.0, 100.0));
    }

    static B.Ticket Winning()
    {
        var t = new B.Ticket();
        t.Side = 0; t.Stake = 500; t.Payout = 1250; t.Pending = true;
        return t;
    }

    [Fact]   // TownBookieResultPolicyTests.cpp:16-24
    public void A_winning_ticket_returns_its_payout()
    {
        B.Result won = B.Resolve(Winning(), 0);
        Assert.Equal(B.ResultOutcome.Won, won.Outcome);
        Assert.Equal(1250, won.Returned);
        Assert.Equal(750, won.Net);
    }

    [Fact]   // TownBookieResultPolicyTests.cpp:26-30
    public void A_losing_ticket_returns_nothing()
    {
        B.Result lost = B.Resolve(Winning(), 1);
        Assert.Equal(B.ResultOutcome.Lost, lost.Outcome);
        Assert.Equal(0, lost.Returned);
        Assert.Equal(-500, lost.Net);
    }

    [Fact]   // TownBookieResultPolicyTests.cpp:32-36
    public void A_cancelled_bout_refunds_the_stake()
    {
        B.Result draw = B.Resolve(Winning(), -1);
        Assert.Equal(B.ResultOutcome.Refunded, draw.Outcome);
        Assert.Equal(500, draw.Returned);
        Assert.Equal(0, draw.Net);
    }

    [Fact]   // TownBookieResultPolicyTests.cpp:38-42
    public void A_ticket_that_is_not_pending_resolves_to_nothing()
    {
        B.Result none = B.Resolve(new B.Ticket(), 0);
        Assert.Equal(B.ResultOutcome.None, none.Outcome);
        Assert.Equal(0, none.Returned);
        Assert.Equal(0, none.Net);
    }
}
