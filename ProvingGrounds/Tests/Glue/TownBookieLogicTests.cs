using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using MT = ProvingGrounds.Core.MatchRules.MatchTeam;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 8.1a: TownBookie.cpp's decisions (Core/Glue/TownBookieLogic.cs) over a fake world: the market at BeginNpcBout (side mapping, the
/// fighter cards, the market rule), the wager (its three errors byte for byte, the stake taken), every settlement branch (win = quoted return, loss
/// 0, cancel / draw / invalid fighter = stake back) into the saved credit and PayCredit's INT_MAX guard, the result 1 s later closing the registry
/// and betting windows, the hold for bets and NpcAssembling, Capture / Restore / RefundUnrestoredWager, Show near / far, the window commands
/// re-checked on arrival (seq gate, enabled rules, drawn stake and side), Close and AbandonWorldState.</summary>
public class TownBookieLogicTests
{
    const int Bookie = 50, P1 = 0, A1 = 10, A2 = 11, B1 = 20, B2 = 21;
    const long Town = 7, Other = 8;

    sealed class Npc
    {
        public long Town = TownBookieLogicTests.Town;
        public Vector3 Pos;
        public bool Player, Dead, Ko, Gone, Bookie;
        public double Ability = 10;
        public string Name = "";
        public bool Ready = true;
        public bool Throw;
    }

    sealed class FakeHost : ITownBookieHost<int>
    {
        public readonly Dictionary<int, Npc> Npcs = [];
        public readonly List<string> Calls = [];
        public readonly List<string> Debugs = [], Errors = [], Messages = [];
        public long Purse = 1000;
        public bool Save = true;
        public int Credit;
        public uint Now = 5000;
        public bool Planned;
        public double PlannedHours, Hours;
        public bool RegistryNear = true;
        public string Status = "town status";

        public FakeHost()
        {
            Npcs[P1] = new Npc { Player = true, Pos = new(100, 0, 0) };
            Npcs[Bookie] = new Npc { Bookie = true, Pos = new(0, 0, 0) };
            foreach (int f in new[] { A1, A2, B1, B2 }) Npcs[f] = new Npc { Name = "F" + f, Pos = new(500, 0, 0) };
        }

        public int None => -1;
        public bool IsNull(int c) => c < 0;
        public bool Valid(int c) => Npcs.TryGetValue(c, out var n) && !n.Gone;
        public bool IsDead(int c) => Npcs[c].Dead;
        public bool IsUnconscious(int c) => Npcs[c].Ko;
        public bool IsPlayer(int c) => Npcs.TryGetValue(c, out var n) && n.Player;
        public IReadOnlyList<int> PlayerCharacters() => [.. Npcs.Where(kv => kv.Value.Player).Select(kv => kv.Key)];
        public long CurrentTown(int c) => Npcs[c].Town;
        public bool Position(int c, out Vector3 position) { position = Npcs[c].Pos; return true; }
        public bool IsBookieRecord(int c) => Valid(c) && Npcs[c].Bookie;
        public long Money => Purse;
        public void AddMoney(long delta) { Purse += delta; Calls.Add("money " + delta); }
        public bool HasActiveSave => Save;
        public int BookieCredit { get => Save ? Credit : 0; set { if (Save) Credit = value; } }
        public IReadOnlyList<LeaderboardData.Standing> TownStandings() =>
            [new LeaderboardData.Standing { Id = "id" + A1, Mmr = 1234.4f, Wins = 3, Losses = 1, Matches = 4 }];
        public string Name(int c) => Npcs[c].Name;
        public ArenaCombatProfile.Profile Profile(int c)
        {
            if (Npcs.TryGetValue(c, out var n) && n.Throw) throw new InvalidOperationException("profile broke");
            return new ArenaCombatProfile.Profile { Style = "Katana" };
        }
        public string Identity(int c) => "id" + c;
        public string SelectionIdentity(int c) => "key" + c;
        public double Ability(int c) => Npcs[c].Ability;
        public bool IsNpcReadyForPreparation(int c) => Npcs[c].Ready;
        public bool HasPlannedNpcBout => Planned;
        public double PlannedNpcStartHours => PlannedHours;
        public double NowHours => Hours;
        public bool BindRegistryNear(int c) { Calls.Add("bind " + c); return RegistryNear; }
        public void BeginTrial() => Calls.Add("trial");
        public string TownStatus() => Status;
        public void CloseTownRegistryWindow() => Calls.Add("close registry");
        public uint NowMs => Now;
        public void LogOpenMarket(int[] fighters, MT[] teams, int count, double scoreA, double scoreB, bool eligible) =>
            Calls.Add($"open_market {count} {scoreA} {scoreB} {eligible}");
        public void LogMarketEvent(string ev, string fields) => Calls.Add("event " + ev + fields);
        public void LogCloseMarket(int winner, int side, int stake, int quotedReturn, int credit, bool hadTicket) =>
            Calls.Add($"close_market {winner} {side} {stake} {quotedReturn} {credit} {hadTicket}");
        public void LogAbandon() => Calls.Add("abandon");
        public void Debug(string line) => Debugs.Add(line);
        public void Error(string line) => Errors.Add(line);
        public void Message(string text) => Messages.Add(text);
    }

    static readonly int[] Fighters = [A1, A2, B1, B2];
    static readonly MT[] Teams = [MT.TeamA, MT.TeamA, MT.TeamB, MT.TeamB];

    static TownBookieLogic<int> Open(FakeHost h, double a = 2.0, double b = 1.0)
    {
        var l = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        l.Show(h, Bookie);
        l.BeginNpcBout(h, Fighters, Teams, 4, a, b);
        Refresh(l, h);
        return l;
    }

    /// <summary>One window refresh (Tick's 250 ms cadence).</summary>
    static void Refresh(TownBookieLogic<int> l, FakeHost h) { h.Now += TownBookieLogic<int>.RefreshMs; l.Tick(h); }

    static void Side(TownBookieLogic<int> l, FakeHost h, int side) => Assert.True(l.OnSelectSide(h, l.LastSeq + 1, side));
    static bool Bet(TownBookieLogic<int> l, FakeHost h) => l.OnPlaceBet(h, l.LastSeq + 1, l.View.View.SelectedSide, l.View.View.Stake);

    [Fact]
    public void BeginNpcBout_opens_the_market_with_the_side_mapping_cards_and_the_log_line()
    {
        var h = new FakeHost();
        var l = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        l.BeginNpcBout(h, [A1, B1, A2, 99, B2], [MT.TeamA, MT.TeamB, MT.TeamA, MT.None, MT.TeamB], 5, 2.0, 1.0);
        Assert.True(l.IsMarketOpen);
        Assert.Equal([A1, A2], l.Team(0));
        Assert.Equal([B1, B2], l.Team(1));
        Assert.Equal(TownBookieLogic<int>.Text.MarketOpen, l.Message);
        Assert.Contains("open_market 5 2 1 True", h.Calls);
        Assert.True(l.HoldForBets());
        // the card's record comes from the town standings (MMR %.0f), an unrated fighter "Unrated | 0 W / 0 L"
        l.Show(h, Bookie);   // publishes the view
        Assert.Equal("MMR 1234 | 3 W / 1 L", l.View.Cards[0][0].View.Record);
        Assert.Equal("Unrated | 0 W / 0 L", l.View.Cards[1][0].View.Record);
        Assert.Equal("key" + A1, l.View.Cards[0][0].PortraitKey);
        Assert.Equal("F" + A1 + "", l.View.Cards[0][0].View.Name);
    }

    [Fact]
    public void A_fighter_read_that_throws_gives_the_unavailable_card_no_market_and_one_error()
    {
        var h = new FakeHost();
        h.Npcs[A2].Throw = true; h.Npcs[B2].Throw = true;
        var l = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        l.BeginNpcBout(h, Fighters, Teams, 4, 2.0, 1.0);
        Assert.Equal(4, l.Team(0).Count + l.Team(1).Count);   // one bad entry never stops the rest
        Assert.False(l.IsMarketOpen);                         // its ability 0: TeamAbility of that side 0
        Assert.Equal(TownBookieLogic<int>.Text.MarketIncomplete, l.Message);
        Assert.Equal(2, l.FighterReadFailures);
        Assert.Single(h.Errors);
    }

    [Fact]
    public void No_market_for_a_player_fighter_or_zero_scores()
    {
        var h = new FakeHost();
        var l = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        l.BeginNpcBout(h, [P1, B1], [MT.TeamA, MT.TeamB], 2, 2.0, 1.0);
        Assert.False(l.IsMarketOpen);
        l.BeginNpcBout(h, Fighters, Teams, 4, 0, 1.0);
        Assert.False(l.IsMarketOpen);
        Assert.False(l.HoldForBets());
    }

    [Fact]
    public void Bet_500_on_team_A_takes_the_stake_and_quotes_the_return_byte_for_byte()
    {
        var h = new FakeHost();
        var l = Open(h);
        Assert.Equal(100, l.View.View.Stake);   // the default stake
        Assert.True(l.OnChangeStake(h, l.LastSeq + 1, 100, 100));
        Assert.True(l.OnChangeStake(h, l.LastSeq + 1, 100, 200));
        Assert.True(l.OnChangeStake(h, l.LastSeq + 1, 100, 300));
        Assert.True(l.OnChangeStake(h, l.LastSeq + 1, 100, 400));
        Assert.Equal(500, l.View.View.Stake);
        Side(l, h, 0);
        int payout = TownBettingPolicy.ReturnCats(500, 2.0, 1.0);
        Assert.Equal("YOUR BET: Team A | Stake 500 Cats | Total return " + payout + " Cats | Profit +" + (payout - 500) + " Cats", l.View.View.Slip);
        Assert.True(Bet(l, h));
        Assert.Equal(500, h.Purse);
        Assert.True(l.Ticket.Pending);
        Assert.Equal((0, 500, payout), (l.Ticket.Side, l.Ticket.Stake, l.Ticket.Payout));
        Assert.Equal("Accepted: 500 Cats. Winning total return: " + payout + " Cats.", l.Message);
        Assert.Contains("event wager_accepted,\"bet_side\":0,\"stake\":500,\"quoted_return\":" + payout, h.Calls);
        Assert.StartsWith("ACCEPTED: Team A | Stake 500 Cats | Quoted return " + payout, l.View.View.Slip);
        Assert.True(l.HasPendingWager());
        // one wager per bout: the bet button is now disabled
        Assert.False(l.View.View.CanBet);
        Assert.False(Bet(l, h));
    }

    [Fact]
    public void The_odds_text_is_the_return_per_1000_with_two_decimals()
    {
        var h = new FakeHost();
        var l = Open(h);
        Assert.Equal(CMath.FormatF(TownBettingPolicy.ReturnCats(1000, 2.0, 1.0) / 1000.0, 2) + "x return", l.View.View.Odds[0]);
        Assert.Matches(@"^\d\.\d\dx return$", l.View.View.Odds[1]);
        var idle = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        idle.Show(h, Bookie);
        Assert.Equal("Awaiting fighters", idle.View.View.Odds[0]);
    }

    [Fact]
    public void Stake_buttons_clamp_to_the_config_and_are_disabled_at_the_limits()
    {
        var h = new FakeHost { Purse = 100_000 };
        var l = Open(h);
        Assert.True(l.OnChangeStake(h, l.LastSeq + 1, -100, 100));
        Assert.Equal(10, l.View.View.Stake);                                     // 100 - 100 = 0 -> the minimum 10
        Assert.False(l.View.AdjustmentEnabled[0] || l.View.AdjustmentEnabled[1] || l.View.AdjustmentEnabled[2]);   // at the floor
        Assert.False(l.OnChangeStake(h, l.LastSeq + 1, -10, 10));                // disabled as drawn: not run
        for (int i = 0; i < 12; i++) l.OnChangeStake(h, l.LastSeq + 1, 1000, l.View.View.Stake);
        Assert.Equal(10_000, l.View.View.Stake);                                 // the maximum
        Assert.False(l.View.AdjustmentEnabled[5]);
        Assert.False(l.OnChangeStake(h, l.LastSeq + 1, 7, 10_000));              // not one of the six buttons
        Assert.Equal(0, l.View.View.Stake % 10);
    }

    [Fact]
    public void A_command_with_an_old_seq_or_a_changed_stake_is_dropped()
    {
        var h = new FakeHost();
        var l = Open(h);
        Assert.True(l.OnChangeStake(h, 5, 100, 100));
        Assert.False(l.OnChangeStake(h, 5, 100, 200));      // seq <= LastSeq
        Assert.False(l.OnChangeStake(h, 6, 100, 100));      // the stake is 200 now, the window drew 100
        Assert.Equal(200, l.View.View.Stake);
        Side(l, h, 1);
        Assert.False(l.OnPlaceBet(h, l.LastSeq + 1, 0, 200));   // drawn side A, selected B
        Assert.Equal(1000, h.Purse);
        l.Close(h);
        Assert.False(l.OnSelectSide(h, l.LastSeq + 1, 0));   // the window is closed
    }

    [Theory]
    [InlineData(0, true)]    // the bet side wins: the quoted return
    [InlineData(1, false)]   // the other side wins: 0
    public void Settle_win_pays_the_quoted_return_and_loss_nothing(int winner, bool won)
    {
        var h = new FakeHost();
        var l = Open(h);
        Side(l, h, 0);
        Assert.True(Bet(l, h));
        int payout = l.Ticket.Payout;
        l.CombatStarted(h);
        Assert.Contains("event market_combat_started", h.Calls);
        Assert.False(l.HoldForBets());
        l.Settle(h, winner);
        Assert.Contains($"close_market {winner} 0 100 {payout} {(won ? payout : 0)} True", h.Calls);
        Assert.Equal(won ? 900 + payout : 900, h.Purse);   // credited and paid at once
        Assert.Equal(0, h.Credit);
        Assert.Equal(won ? $"Team A wins. Bet won: {payout} Cats returned." : "Team B wins. Bet lost: 0 Cats returned.", l.Message);
        Assert.True(l.ResultScheduledNow);
        Assert.Contains(TownBookieLogic<int>.Text.ResultScheduled, h.Debugs);
        Assert.False(l.IsMarketOpen);
        Assert.True(l.IsCompleted);
    }

    [Fact]
    public void Settle_cancel_draw_or_an_invalid_fighter_gives_the_stake_back()
    {
        foreach (var (winner, invalid) in new[] { (-1, false), (0, true) })
        {
            var h = new FakeHost();
            var l = Open(h);
            Side(l, h, 1);
            Assert.True(Bet(l, h));
            int payout = l.Ticket.Payout;
            if (invalid) h.Npcs[A2].Gone = true;   // Eligible() false: winner forced to -1
            l.Settle(h, winner);
            Assert.Contains($"close_market -1 1 100 {payout} 100 True", h.Calls);
            Assert.Equal(1000, h.Purse);
            Assert.Equal("Bout cancelled/drawn: 100 Cats refunded.", l.Message);
            Assert.Equal(TownBettingPolicy.ResultOutcome.Refunded, l.Result.Outcome);
        }
    }

    [Fact]
    public void Settle_without_a_wager_says_who_won_and_nothing_moves()
    {
        var h = new FakeHost();
        var l = Open(h);
        l.Settle(h, 1);
        Assert.Equal("Team B wins. No wager placed.", l.Message);
        Assert.Equal(1000, h.Purse);
        Assert.False(l.ResultScheduledNow);
        l.Settle(h, 0);   // no bout any more: nothing (hadBout false)
        Assert.Equal("Team B wins. No wager placed.", l.Message);
    }

    [Fact]
    public void The_credit_waits_while_it_cannot_be_paid_and_is_paid_on_a_later_tick()
    {
        var h = new FakeHost();
        var l = Open(h);
        Side(l, h, 0);
        Assert.True(Bet(l, h));
        h.Purse = int.MaxValue - 50;   // INT_MAX - credit < money: the credit stays saved (TownBookie.cpp:90)
        l.Settle(h, 0);
        Assert.Equal(l.Result.Returned, h.Credit);
        Assert.Equal(int.MaxValue - 50, h.Purse);
        h.Purse = -5; l.Tick(h);       // a negative purse: not paid either
        Assert.Equal(-5, h.Purse);
        h.Purse = 10; l.Tick(h);       // PayCredit each tick
        Assert.Equal(10 + l.Result.Returned, h.Purse);
        Assert.Equal(0, h.Credit);
        // a pending credit blocks the next bet
        h = new FakeHost { Credit = 30, Purse = int.MaxValue };
        l = Open(h);
        Side(l, h, 0);
        Assert.False(l.View.View.CanBet);
        Assert.Equal("Pending Cats return must be collected before betting.", l.View.View.Status);
    }

    [Fact]
    public void Wager_errors_are_the_original_texts()
    {
        var h = new FakeHost();
        var l = Open(h);
        Side(l, h, 0);
        h.Npcs[P1].Pos = new(1000, 0, 0);   // too far: canBet false; the C++ text is reached only through PlaceBet's own check
        Refresh(l, h);
        Assert.False(l.View.View.CanBet);
        Assert.Equal("Bring a squad member close to this town's bookie; an active save is required.", l.View.View.Status);
        h.Npcs[P1].Pos = new(199, 0, 0);
        h.Purse = 50;
        Refresh(l, h);
        Assert.Equal("Not enough Cats for this stake.", l.View.View.Status);
        Assert.Equal("Bring a squad member close to this town's bookie.", TownBookieLogic<int>.Text.WagerNotNear);
        Assert.Equal("Bet unavailable: check Cats, existing wager, and that combat has not started.", TownBookieLogic<int>.Text.WagerUnavailable);
        Assert.Equal("Could not accept the Cats wager.", TownBookieLogic<int>.Text.WagerNotAccepted);
    }

    [Fact]
    public void The_result_shows_one_second_later_and_closes_the_registry_and_betting_windows()
    {
        var h = new FakeHost();
        var l = Open(h);
        Side(l, h, 0);
        Assert.True(Bet(l, h));
        l.Settle(h, 0);
        h.Now += 999; l.Tick(h);
        Assert.False(l.ResultWindowVisible);
        Assert.True(l.BetWindowVisible);
        h.Now += 1; l.Tick(h);
        Assert.True(l.ResultWindowVisible);
        Assert.False(l.BetWindowVisible);
        Assert.Contains("close registry", h.Calls);
        Assert.Contains(TownBookieLogic<int>.Text.ResultShown, h.Debugs);
        Assert.Equal("WAGER WON", l.ResultView.View.Title);
        Assert.True(l.ResultView.Open);
        Assert.Equal(2, l.ResultView.Cards[0].Count);
        Assert.False(l.View.Open);
        // closing the result dismisses it
        Assert.True(l.OnClose(h, l.RequestSerial));
        Assert.False(l.IsVisible);
        Assert.Contains(TownBookieLogic<int>.Text.ResultDismissed, h.Debugs);
        Assert.False(l.OnClose(h, l.RequestSerial));   // already closed
    }

    [Fact]
    public void Hold_for_bets_runs_20_seconds_of_market_time_and_assembling_ends_it()
    {
        var h = new FakeHost();
        var l = Open(h);
        for (int i = 0; i < 19; i++) { h.Now += 1000; l.Tick(h); }
        Assert.True(l.HoldForBets());
        h.Now += 1000; l.Tick(h);
        Assert.False(l.HoldForBets());
        l.BeginNpcBout(h, Fighters, Teams, 4, 2.0, 1.0);
        Assert.True(l.HoldForBets());
        l.NpcAssembling();
        Assert.False(l.HoldForBets());
        Assert.Equal("Scheduled bout assembling. Betting closes when combat begins.", l.Message);
    }

    [Fact]
    public void Show_too_far_gives_the_original_message_and_opens_nothing()
    {
        var h = new FakeHost();
        h.Npcs[P1].Pos = new(201, 0, 0);
        var l = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        l.Show(h, Bookie);
        Assert.Equal(["Move a squad member closer to the Scratch bookie."], h.Messages);
        Assert.False(l.IsVisible);
        h.Npcs[P1].Pos = new(200, 0, 0); h.Npcs[P1].Town = Other;   // in range but another town
        l.Show(h, Bookie);
        Assert.False(l.IsVisible);
        h.Npcs[P1].Town = Town;
        l.Show(h, Bookie);
        Assert.True(l.IsVisible);
        Assert.Equal(["bind " + Bookie, "close registry"], h.Calls);
        Assert.True(l.View.Open);
        l.Show(h, A1);   // not the bookie record: nothing
        Assert.Equal(Bookie, l.BookieCharacter);
    }

    [Fact]
    public void Arrange_binds_the_registry_and_begins_a_trial_when_no_bout_is_open()
    {
        var h = new FakeHost();
        var l = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        l.Show(h, Bookie);
        Assert.True(l.View.View.CanArrange);
        Assert.True(l.OnArrange(h, l.LastSeq + 1));
        Assert.Contains("trial", h.Calls);
        Assert.Equal("town status", l.View.View.Status);
        l.BeginNpcBout(h, Fighters, Teams, 4, 2.0, 1.0);
        Refresh(l, h);
        Assert.False(l.View.View.CanArrange);              // a market is open
        Assert.False(l.OnArrange(h, l.LastSeq + 1));
        Assert.Equal("Finish the current bout first.", TownBookieLogic<int>.Text.ArrangeBusy);
    }

    [Fact]
    public void Capture_and_restore_carry_the_ticket_and_refuse_a_wager_without_a_market()
    {
        var h = new FakeHost();
        var l = Open(h);
        Side(l, h, 0);
        Assert.True(Bet(l, h));
        var saved = new ArenaPersistence.PlannedNpcBout();
        l.CapturePlannedNpcBout(saved);
        Assert.Equal((true, true, 0, 100, l.Ticket.Payout), (saved.Market, saved.WagerPending, saved.WagerSide, saved.Stake, saved.Payout));
        // after a load: the same lineup installed, the ticket restored
        var loaded = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        loaded.BeginNpcBout(h, Fighters, Teams, 4, 2.0, 1.0);
        Assert.True(loaded.RestorePlannedNpcBout(saved));
        Assert.True(loaded.Ticket.Pending);
        Assert.Equal(0, loaded.SelectedSide);
        Assert.Equal("Accepted wager restored with the announced lineup and quoted return.", loaded.Message);
        Assert.False(loaded.RestorePlannedNpcBout(saved));   // a ticket already pending
        // a saved wager whose lineup has no market now: refused (TownArena then cancels and refunds)
        var noMarket = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        noMarket.BeginNpcBout(h, Fighters, Teams, 4, 0, 1.0);
        Assert.False(noMarket.RestorePlannedNpcBout(saved));
        // a saved bout without a wager always restores (8.7's diagnostic assignment is not cancelled by it)
        Assert.True(noMarket.RestorePlannedNpcBout(new ArenaPersistence.PlannedNpcBout { Market = true }));
    }

    [Fact]
    public void RefundUnrestoredWager_credits_the_stake_and_pays_it_with_its_guard()
    {
        var h = new FakeHost();
        var l = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        var saved = new ArenaPersistence.PlannedNpcBout { WagerPending = true, Stake = 300 };
        l.RefundUnrestoredWager(h, saved);
        Assert.Equal(1300, h.Purse);
        h.Credit = int.MaxValue - 100;   // the stake would overflow the credit: not added
        h.Purse = int.MaxValue;
        l.RefundUnrestoredWager(h, saved);
        Assert.Equal(int.MaxValue - 100, h.Credit);
        h.Save = false; h.Credit = 0; h.Purse = 0;
        l.RefundUnrestoredWager(h, saved);   // no active save: nothing
        Assert.Equal(0, h.Purse);
        l.RefundUnrestoredWager(new FakeHost(), new ArenaPersistence.PlannedNpcBout { WagerPending = false, Stake = 5 });
    }

    [Fact]
    public void CancelNpcBout_logs_the_reason_before_the_refund_and_prefixes_the_message()
    {
        var h = new FakeHost();
        var l = Open(h);
        Side(l, h, 0);
        Assert.True(Bet(l, h));
        l.CancelNpcBout(h, "Every player left");
        int cancel = h.Calls.IndexOf("event market_cancelled,\"reason\":\"Every player left\"");
        int close = h.Calls.FindIndex(c => c.StartsWith("close_market", StringComparison.Ordinal));
        Assert.True(cancel >= 0 && close > cancel);
        Assert.Equal(1000, h.Purse);
        Assert.Equal("Every player left. Bout cancelled/drawn: 100 Cats refunded.", l.Message);
    }

    [Fact]
    public void CombatStarted_is_ignored_without_a_market_and_settles_an_ineligible_one()
    {
        var h = new FakeHost();
        var l = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        l.CombatStarted(h);   // a player challenge: no market, nothing
        Assert.Empty(h.Calls);
        l = Open(h);
        h.Npcs[B1].Gone = true;
        l.CombatStarted(h);
        Assert.True(l.IsCompleted);
        Assert.Equal("Bout cancelled or drawn. No wager placed.", l.Message);
    }

    [Fact]
    public void PreparationValid_needs_every_fighter_eligible_and_ready()
    {
        var h = new FakeHost();
        var l = Open(h);
        Assert.True(l.PreparationValid(h));
        h.Npcs[A2].Ready = false;
        Assert.False(l.PreparationValid(h));
        h.Npcs[A2].Ready = true; h.Npcs[B2].Player = true;
        Assert.False(l.PreparationValid(h));
    }

    [Fact]
    public void AbandonWorldState_logs_first_moves_no_money_and_forgets_the_market()
    {
        var h = new FakeHost();
        var l = Open(h);
        Side(l, h, 0);
        Assert.True(Bet(l, h));
        h.Calls.Clear();
        long purse = h.Purse;
        l.AbandonWorldState(h);
        Assert.Equal(["abandon"], h.Calls);
        Assert.Equal(purse, h.Purse);
        Assert.False(l.Ticket.Pending);
        Assert.False(l.IsMarketOpen);
        Assert.False(l.IsVisible);
        Assert.Equal("NPC bouts only. Choose a side before combat.", l.Message);
        Assert.Equal(-1, l.BookieCharacter);
    }

    [Fact]
    public void The_stake_survives_a_world_change_as_the_cpp_global_does()
    {
        var h = new FakeHost();
        var process = new TownBookieProcessState();
        var l = new TownBookieLogic<int>(-1, process);
        l.Show(h, Bookie);
        l.BeginNpcBout(h, Fighters, Teams, 4, 2.0, 1.0);
        Refresh(l, h);
        Assert.True(l.OnChangeStake(h, l.LastSeq + 1, 1000, 100));
        l.AbandonWorldState(h);   // the old world goes (TownBookie.cpp:405-418 keeps stake, elapsed and ability)
        var next = new TownBookieLogic<int>(-1, process);   // the loaded world's
        Assert.Equal(1100, next.Stake);
        Assert.Equal(-1, next.SelectedSide);   // selectedSide is reset (:414)
        Assert.Equal("NPC bouts only. Choose a side before combat.", next.Message);   // AbandonWorldState's text (:417) carries over the load
        Assert.Equal("NPC bouts only. Arrange a bout to open betting.", new TownBookieProcessState().Message);   // a fresh process (:43)
        next.Show(h, Bookie);
        Assert.Equal(1100, next.View.View.Stake);
    }

    [Fact]   // quality review: a late close from the old betting window never closes the result window Tick just opened
    public void A_stale_close_does_not_close_the_new_result_window()
    {
        var h = new FakeHost();
        var l = Open(h);
        Side(l, h, 0);
        Assert.True(Bet(l, h));
        int betSerial = l.View.RequestSerial;   // the serial the betting window was drawn under
        l.Settle(h, 0);
        h.Now += 1000; l.Tick(h);               // the result shows, a new serial
        Assert.True(l.ResultWindowVisible);
        Assert.NotEqual(betSerial, l.RequestSerial);
        Assert.False(l.OnClose(h, betSerial));  // the old window's Closed report
        Assert.True(l.ResultWindowVisible);
        Assert.True(l.OnClose(h, l.ResultView.RequestSerial));
        Assert.False(l.IsVisible);
    }

    [Fact]   // quality review: a refused command's enabled check changes nothing (the clamped stake is not written back)
    public void A_refused_command_leaves_the_stake_alone()
    {
        var h = new FakeHost();
        var process = new TownBookieProcessState { Stake = 20_000 };   // above the configured 10,000 (e.g. a lowered ceiling)
        var l = new TownBookieLogic<int>(-1, process);
        l.Show(h, Bookie);   // Show refreshes: MakeView clamps the module's stake, as the C++
        Assert.Equal(10_000, process.Stake);
        process.Stake = 20_000;
        Assert.False(l.OnPlaceBet(h, l.LastSeq + 1, 0, 10_000));   // refused (no market): the check runs on a copy
        Assert.Equal(20_000, process.Stake);
    }

    [Fact]   // kept bug K8: Settle's credit add has no guard; an int that overflows wraps, the negative credit is never paid and Wager refuses
    public void An_overflowing_credit_wraps_and_is_never_paid()
    {
        var h = new FakeHost();
        var l = Open(h);
        Side(l, h, 0);
        Assert.True(Bet(l, h));
        int payout = l.Ticket.Payout;
        h.Credit = int.MaxValue;   // an unpaid credit already at the int limit
        l.Settle(h, 0);
        Assert.Equal(unchecked(int.MaxValue + payout), h.Credit);
        Assert.True(h.Credit < 0);
        long purse = h.Purse;
        l.Tick(h);
        Assert.Equal(purse, h.Purse);   // PayCredit: credit <= 0 is never paid
        h.Now += 1000; l.Tick(h); l.Close(h); l.Show(h, Bookie);   // the result shown and dismissed
        l.BeginNpcBout(h, Fighters, Teams, 4, 2.0, 1.0);
        Refresh(l, h);
        Side(l, h, 0);
        Assert.False(l.View.View.CanBet);   // GetBookieCredit() != 0: no bet, for good
    }

    [Fact]   // a wager whose bout is no longer planned (it began staging) is not in a save: the stake stays taken, as the C++'s design
    public void A_wager_without_a_planned_bout_is_lost_with_a_save_that_cannot_carry_it()
    {
        var h = new FakeHost();
        var l = Open(h);
        Side(l, h, 0);
        Assert.True(Bet(l, h));
        Assert.True(l.HasPendingWager());   // TownArena::CapturePlannedNpcBout then refuses the arena data (!HasPendingWager, TownArena.cpp:595)
        long purse = h.Purse;
        l.AbandonWorldState(h);             // the world goes: no money is moved (:406-407)
        var loaded = new TownBookieLogic<int>(-1, new TownBookieProcessState());
        Assert.False(loaded.HasPendingWager());
        Assert.Equal(purse, h.Purse);
        Assert.Equal(0, h.Credit);
    }

    [Fact]
    public void The_view_is_republished_only_when_it_changed()
    {
        var h = new FakeHost();
        var l = Open(h);
        var v = l.View;
        h.Now += 300; l.Tick(h);
        Assert.Same(v, l.View);
        h.Purse = 999;   // the header shows the money
        h.Now += 300; l.Tick(h);
        Assert.NotSame(v, l.View);
        Assert.StartsWith("BETTING OPEN | 999 Cats | NPC bout 2v2", l.View.View.Header);
    }
}
