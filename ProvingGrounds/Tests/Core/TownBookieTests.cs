using ProvingGrounds.Core;
using TB = ProvingGrounds.Core.TownBookie;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for Core/TownBookie.cs, worked from TownBookie.cpp:107-230 and :383-401.</summary>
public class TownBookieTests
{
    static TB.ViewInputs Open() => new TB.ViewInputs
    {
        HasWallet = true, Money = 5000, Market = true, Eligible = true, Nearby = true, SameTown = true, HasActiveSave = true,
        TeamCounts = new[] { 1, 1 }, Ability = new[] { 1.0, 1.0 },
    };

    [Fact]
    public void Open_market_view_and_its_status_order()
    {
        TB.View v = TB.MakeView(Open(), out int stake);
        Assert.Equal(100, stake);
        Assert.Equal("BETTING OPEN | 5000 Cats | NPC bout 1v1", v.Header);
        Assert.Equal("", v.Timer);
        Assert.Equal("Select a team, then choose your stake. One wager per bout.", v.Status);
        Assert.Equal("YOUR BET: Choose a team and stake. Place bet confirms your wager.", v.Slip);
        Assert.True(v.CanSelect && v.CanStake && !v.CanBet && !v.CanArrange);
        var g = Open(); g.SelectedSide = 1; g.Money = 50;
        v = TB.MakeView(g, out _);
        Assert.Equal("Not enough Cats for this stake.", v.Status);
        g.BookieCredit = 7;
        Assert.Equal("Pending Cats return must be collected before betting.", TB.MakeView(g, out _).Status);
        g.Eligible = false;
        Assert.Equal("Betting unavailable: fighter data is incomplete.", TB.MakeView(g, out _).Status);
        g.SameTown = false;
        Assert.Equal("Bring a squad member close to this town's bookie; an active save is required.", TB.MakeView(g, out _).Status);
        g.Fighting = true;
        v = TB.MakeView(g, out _);
        Assert.Equal("NPC bouts only. Arrange a bout to open betting.", v.Status);   // the status checks run before combat only
        Assert.Equal("Bets closed. No ticket placed.", v.Slip);
        Assert.StartsWith("COMBAT - BETS CLOSED | 0 Cats", TB.MakeView(new TB.ViewInputs { Fighting = true }, out _).Header);
    }

    [Fact]
    public void Stake_is_clamped_to_the_bookie_range()
    {
        using var _ = PGConfig.UseBookieForTest(new PGConfig.Bookie { Minimum = 200, Maximum = 300 });
        var g = Open(); g.Stake = 100;
        Assert.Equal(200, TB.MakeView(g, out int stake).Stake);
        Assert.Equal(200, stake);
        g.Stake = 900; g.Ticket = new TownBettingPolicy.Ticket { Side = 0, Stake = 250, Payout = 400, Pending = true };
        TB.View v = TB.MakeView(g, out stake);
        Assert.Equal(300, stake);
        Assert.Equal(250, v.Stake);   // a ticket's stake wins
        Assert.Equal("ACCEPTED: Team A | Stake 250 Cats | Quoted return 400 Cats | Potential profit +150 Cats", v.Slip);
    }

    [Fact]
    public void Timer_counts_down_and_pads_like_printf()
    {
        var g = Open(); g.PlannedBout = true; g.PlannedStartHours = 30.5; g.NowHours = 28.0;   // 30.5 h = 06:30 next day, 2.5 h left
        Assert.Equal("NEXT BOUT  06:30  |  STARTS IN 2h 30m", TB.MakeView(g, out _).Timer);
        g.NowHours = 31.0;
        Assert.Equal("NEXT BOUT  06:30  |  START TIME REACHED - WAITING FOR ARENA / MEDICS", TB.MakeView(g, out _).Timer);
        g.PlannedStartHours = -0.5; g.NowHours = -1.0;   // a negative minute of day prints with its sign, never "0-"
        Assert.Equal("NEXT BOUT  00:-30  |  STARTS IN 0h 30m", TB.MakeView(g, out _).Timer);
    }

    [Fact]
    public void Odds_print_two_decimals_and_wait_for_fighters()
    {
        TB.View v = TB.MakeView(Open(), out _);
        Assert.Equal("1.80x return", v.Odds[0]);   // ReturnCats(1000, 1, 1) = 1800 at the shipped bookie and pricing levers
        Assert.Equal("1.80x return", v.Odds[1]);
        var g = Open(); g.TeamCounts = new[] { 0, 2 };
        Assert.Equal("Awaiting fighters", TB.MakeView(g, out _).Odds[0]);
        g.Ability = new[] { 0.0, 1.0 };
        Assert.Equal("Awaiting fighters", TB.MakeView(g, out _).Odds[1]);   // ReturnCats 0 for a non-positive ability
    }

    [Fact]
    public void Capture_record_uses_the_first_rated_standing()
    {
        var profile = ArenaCombatProfile.FromStats(true, ArenaCombatProfile.WeaponCategory.Sabres, 1, 2, 3, 4, 5, 6);
        var standings = new List<LeaderboardData.Standing>
        {
            new() { Id = "x", Matches = 0, Mmr = 300 },
            new() { Id = "x", Matches = 2, Mmr = 100.5f, Wins = 2, Losses = 0 },
            new() { Id = "x", Matches = 3, Mmr = 900, Wins = 3 },
        };
        TB.FighterView v = TB.Capture(true, "Ruka", profile, "x", standings);
        Assert.Equal("Ruka", v.Name);
        Assert.Equal("Sabre", v.Style);
        Assert.Equal("MMR 101 | 2 W / 0 L", v.Record);   // %.0f (msvcr100): the 100.5 tie rounds half away from zero
        Assert.Equal("Unrated | 0 W / 0 L", TB.Capture(false, "Ruka", profile, "", standings).Record);
        Assert.Equal("Unavailable fighter", TB.Capture(false, "Ruka", profile, "x", standings).Name);
    }

    [Fact]
    public void Result_view_texts()
    {
        var empty = new IReadOnlyList<TB.FighterView>[] { new List<TB.FighterView>(), new List<TB.FighterView>() };
        TB.ResultView won = TB.MakeResultView(new TownBettingPolicy.Result
            { Outcome = TownBettingPolicy.ResultOutcome.Won, WinningSide = 1, SelectedSide = 1, Stake = 100, Returned = 250, Net = 150 }, empty);
        Assert.Equal(("WAGER WON", 1), (won.Title, won.Tone));
        Assert.Equal("Team B claimed the arena. Your pick delivered.", won.Context);
        Assert.Equal("STAKE  100 CATS     RETURNED  250 CATS\nNET WIN  +150 CATS", won.Payout);
        TB.ResultView lost = TB.MakeResultView(new TownBettingPolicy.Result
            { Outcome = TownBettingPolicy.ResultOutcome.Lost, WinningSide = 0, SelectedSide = 1, Stake = 100, Net = -100 }, empty);
        Assert.Equal("Team A claimed the arena. You backed Team B.", lost.Context);
        Assert.Equal("STAKE  100 CATS     RETURNED  0 CATS\nNET LOSS  -100 CATS", lost.Payout);
        TB.ResultView none = TB.MakeResultView(new TownBettingPolicy.Result(), empty);
        Assert.Equal(("WAGER REFUNDED", 3), (none.Title, none.Tone));
    }

    [Fact]
    public void Wager_and_settle_messages()
    {
        Assert.Equal("Accepted: 100 Cats. Winning total return: 240 Cats.", TB.AcceptedMessage(100, 240));
        Assert.Equal(",\"bet_side\":1,\"stake\":100,\"quoted_return\":240", TB.WagerFields(1, 100, 240));
        Assert.Equal("Bout cancelled/drawn: 100 Cats refunded.", TB.SettleMessage(true, -1, 0, 100));
        Assert.Equal("Team B wins. Bet won: 240 Cats returned.", TB.SettleMessage(true, 1, 1, 240));
        Assert.Equal("Team A wins. Bet lost: 0 Cats returned.", TB.SettleMessage(true, 0, 1, 0));
        Assert.Equal("Bout cancelled or drawn. No wager placed.", TB.SettleMessage(false, -1, 0, 0));
        Assert.Equal("Team A wins. No wager placed.", TB.SettleMessage(false, 0, 0, 0));
        Assert.Equal("Team B wins. No wager placed.", TB.SettleMessage(false, 1, 0, 0));
    }

    [Fact]
    public void Exactly_enough_Cats_is_enough()   // (mutation pass: `money < stake`, not `<=`)
    {
        var g = Open(); g.SelectedSide = 0; g.Money = 100; g.Stake = 100;
        TB.View v = TB.MakeView(g, out _);
        Assert.Equal("NPC bouts only. Arrange a bout to open betting.", v.Status);
        Assert.True(v.CanBet);
    }

    [Fact]
    public void Views_start_as_the_cpp_constructors()
    {
        var v = new TB.View();
        Assert.Equal((-1, 100), (v.SelectedSide, v.Stake));
        Assert.False(v.CanSelect || v.CanStake || v.CanBet || v.CanArrange);
        var r = new TB.ResultView();
        Assert.Equal((-1, -1, 0), (r.WinningSide, r.SelectedSide, r.Tone));
    }
}
