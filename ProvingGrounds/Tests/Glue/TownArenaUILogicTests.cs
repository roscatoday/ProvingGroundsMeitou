using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 7.5: the town window's sim half (TownArenaUI.cpp:235-333, 417-577, 904-965) over <see cref="TownFake"/>: the team, the handlers
/// re-checked on arrival, the refresh's captions.</summary>
public class TownArenaUILogicTests
{
    sealed class Ui(TownFake f) : ITownArenaUIHost<int>
    {
        public IReadOnlyList<int> PlayerSquad() => f.Players ?? [];
        public ArenaCombatProfile.Profile Profile(int c) => new() { Valid = true, Style = "Polearms", Attack = 41.5, Defence = 30, Strength = 20, Toughness = 25, Dexterity = 33, WeaponSkill = 44 };
        public bool Recovery(int c, out float recovery, out RosterStatusPolicy.Band band)
        {
            if (Throw) throw new InvalidOperationException("a broken read");
            recovery = 1; band = RosterStatusPolicy.Band.Green; return true;
        }
        public bool Throw;
        public readonly List<string> Errors = [];
        public void Error(string line) => Errors.Add(line);
    }

    static string Sel(int c) => "player:0:0:0:" + c + ":1";

    static (TownArenaLogic<int> T, TownFake F, TownArenaUILogic<int> W, Ui U) Open()
    {
        var f = new TownFake();
        f.Buildings.Add(TownFake.TownRegistry());
        f.Buildings.Add(TownFake.Building("10.34:40000", "183-Proving Grounds.mod", pos: TownFake.RegistryPos + new Vector3(600, 0, 0)));
        f.Player(0); f.Player(1, dx: 12); f.Player(2, dx: 14); f.Player(3, dx: 16);
        foreach (int p in new[] { 0, 1, 2, 3 }) f.AbilityOf[p] = 90;
        for (int i = 0; i < 6; ++i) f.Npc(10 + i, "219-Proving Grounds.mod", dx: 30 + i, ability: 70);
        for (int i = 0; i < 4; ++i) f.Npc(30 + i, TownFake.Medic, dx: 60 + i);
        var t = new TownArenaLogic<int>(TownFake.NoCharacter);
        f.Logic = t;
        t.State.SchedulePaused = true;
        t.BindRegistry(TownFake.TownRegistry().Building);
        t.Tick(f); f.Now += 11000; t.Tick(f);
        var w = new TownArenaUILogic<int>();
        var u = new Ui(f);
        w.Show(t, f, u, TownFake.TownRegistry().Building, true);
        return (t, f, w, u);
    }

    static void Refresh(TownArenaLogic<int> t, TownFake f, TownArenaUILogic<int> w, Ui u) { f.Now += 1000; w.Tick(t, f, u); }

    [Fact]
    public void ShowOpensWithTheSelectionPromptAndTheRows()
    {
        var (_, _, w, _) = Open();
        var v = w.View;
        Assert.True(v.Open);
        Assert.True(v.SelectionPrompt);
        Assert.Equal("Select a recovered fighter", v.Cards[0].Heading);
        Assert.Equal("Choose 1-3 recovered squad fighters to reveal the current challenge cards.", v.Cards[0].Text);
        Assert.False(v.Cards[0].BookVisible);
        Assert.All(v.Cards.Skip(1), c => Assert.False(c.Visible));
        Assert.Equal(4, v.Rows.Count);
        Assert.Equal("Player 0 - Ready", v.Rows[0].Caption);
        Assert.Equal("Selected team: 0 / 3", v.Squad);
        Assert.Equal("Book direct 1v1", v.BookingButton);
        Assert.False(v.BookingEnabled);
        Assert.Equal("Buy Refresh (500 Cats)", v.RefreshButton);
        Assert.Equal("SKARN\nLOCKED", v.SkarnPlaceholder);
        Assert.Equal("Incomplete - Veyr the Gilded", v.SkarnTip[5]);
    }

    [Fact]
    public void RowsToggleUpToThreeReadyFightersAndSayWhyNot()
    {
        var (t, f, w, u) = Open();
        w.OnRowReleased(t, f, u, 101, Sel(0));
        Assert.Equal([Sel(0)], w.Lineup);
        w.OnRowReleased(t, f, u, 102, Sel(1));
        w.OnRowReleased(t, f, u, 103, Sel(2));
        w.OnRowReleased(t, f, u, 104, Sel(3));
        Assert.Equal(3, w.Lineup.Count);
        Assert.Equal("Town teams can contain up to three fighters.", w.View.Feedback);
        w.OnRowReleased(t, f, u, 105, Sel(2));   // a second release removes
        Assert.Equal(2, w.Lineup.Count);
        f.Health[3] = (0.4f, 1f);
        w.OnRowReleased(t, f, u, 106, Sel(3));
        Assert.Equal("recovering", w.View.Feedback);
        Refresh(t, f, w, u);
        Assert.Equal("Player 0 - Ready\nSelected", w.View.Rows[0].Caption);
        Assert.Equal("Player 3 - Recovering", w.View.Rows[3].Caption);
        Assert.Equal("Selected team: 2 / 3", w.View.Squad);
        Assert.False(w.View.SelectionPrompt);
    }

    [Fact]
    public void ACardShowsItsTermsAndBooksOnlyTheShownQuote()
    {
        var (t, f, w, u) = Open();
        w.OnRowReleased(t, f, u, 107, Sel(0));
        Refresh(t, f, w, u);
        int slot = f.Enqueued[0].Slot;
        f.Results.Enqueue(new() { Generation = t.State.SearchGeneration, Slot = slot, Match = new() { Valid = true, A = [Sel(0)], B = ["fid-10"] } });
        Refresh(t, f, w, u);
        var card = w.View.Cards[slot];
        Assert.True(card.Quote.Valid());
        Assert.StartsWith("Buy-in: " + card.Quote.BuyIn + " Cats\nOdds: ", card.Terms);
        Assert.Contains("\nMarks: ", card.Terms);
        Assert.True(card.BookEnabled);
        Assert.Equal("ATK 42 | DEF 30 | STR 20\nTOU 25 | DEX 33 | WPN 44", card.Portraits[0].Stats);
        var stale = card.Quote; stale.BuyIn += 10;
        Assert.False(w.OnChallenge(t, f, u, 201, slot, card.OfferKey, stale));   // the card no longer shows that quote: nothing runs
        Assert.False(t.HasBooking());
        Assert.False(w.OnChallenge(t, f, u, 201, slot, card.OfferKey, card.Quote));   // seq 201 is already recorded: an old request, dropped
        Assert.True(w.OnChallenge(t, f, u, 202, slot, card.OfferKey, card.Quote));
        Assert.True(t.HasBooking());
        Assert.Equal(202, w.View.LastSeq);
        Assert.StartsWith("Challenge reserved: ", w.View.Feedback);
        w.OnRowReleased(t, f, u, 203, Sel(1));
        Assert.Equal("Cancel the booking before editing the team.", w.View.Feedback);
    }

    [Fact]
    public void TheDirectButtonBooksOrCancelsAsItWasShown()
    {
        var (t, f, w, u) = Open();
        w.OnRowReleased(t, f, u, 109, Sel(0));
        Refresh(t, f, w, u);
        Assert.True(w.View.BookingEnabled);
        Assert.False(w.OnBook(t, f, u, 301, "Cancel booking"));   // drawn before a change: not run
        Assert.True(w.OnBook(t, f, u, 302, "Book direct 1v1"));
        Assert.True(t.HasBooking());
        Assert.Equal(20000, f.Money);
        Refresh(t, f, w, u);
        Assert.Equal("Cancel booking", w.View.BookingButton);
        Assert.True(w.OnBook(t, f, u, 303, "Cancel booking"));
        Assert.False(t.HasBooking());
        Assert.Equal(TownArena.Text.BookingCancelled, w.View.Feedback);
    }

    [Fact]
    public void TheRefreshRunsOnlyAtTheShownCost()
    {
        var (t, f, w, u) = Open();
        Assert.False(w.OnBuyRefresh(t, f, 401, 1000));
        Assert.Equal(20000, f.Money);
        Assert.True(w.OnBuyRefresh(t, f, 402, 500));
        Assert.Equal(19500, f.Money);
        Refresh(t, f, w, u);
        Assert.Equal("Buy Refresh (1000 Cats)", w.View.RefreshButton);
    }

    [Fact]
    public void NothingRunsWhileTheWindowIsClosed()
    {
        var (t, f, w, u) = Open();
        w.Close();
        Assert.False(w.View.Open);
        Assert.False(w.OnBuyRefresh(t, f, 501, 500));
        Assert.Equal(20000, f.Money);
        w.AbandonWorldState();
        Assert.Empty(w.Lineup);
    }

    /// <summary>A card resolved and quoted for player 0's one-fighter team.</summary>
    static (TownArenaLogic<int> T, TownFake F, TownArenaUILogic<int> W, Ui U, int Slot) Quoted()
    {
        var (t, f, w, u) = Open();
        Assert.True(w.OnRowReleased(t, f, u, 1, Sel(0)));
        Refresh(t, f, w, u);
        int slot = f.Enqueued[0].Slot;
        f.Results.Enqueue(new() { Generation = t.State.SearchGeneration, Slot = slot, Match = new() { Valid = true, A = [Sel(0)], B = ["fid-10"] } });
        Refresh(t, f, w, u);
        Assert.True(w.View.Cards[slot].Quote.Valid());
        return (t, f, w, u, slot);
    }

    [Fact]
    public void ACardClickArrivingAfterADirect1v1StartedIsDropped()
    {
        // TownArenaUI.cpp:948: the Book button is enabled only while !Locked() && !IsPlayerMatch() && GetChallengeBuyIn(i).Valid()
        var (t, f, w, u, slot) = Quoted();
        var drawn = w.View.Cards[slot];
        Assert.True(w.OnBook(t, f, u, 10, "Book direct 1v1"));   // the direct 1v1 is booked (and begins) before the card click arrives
        f.Now += 5000; t.Tick(f);
        Assert.True(t.IsPlayerMatch());
        int money = f.Money!.Value;
        Assert.False(w.OnChallenge(t, f, u, 11, slot, drawn.OfferKey, drawn.Quote));
        Assert.Equal(money, f.Money);
        Assert.Equal(-1, t.State.QueuedChallenge);
        Assert.False(t.State.Display.BuyInTicket.Pending);
    }

    [Fact]
    public void ACardClickForAnOfferThatRolledOverIsDroppedEvenWithAnEqualQuote()
    {
        var (t, f, w, u, slot) = Quoted();
        var drawn = w.View.Cards[slot];
        f.Card.Offers[slot].Epoch += 1;   // the slot was regenerated (a new epoch); the quote may well be the same
        Assert.NotEqual(drawn.OfferKey, TownArenaUILogic<int>.OfferKey(t, f, slot));
        Assert.False(w.OnChallenge(t, f, u, 12, slot, drawn.OfferKey, drawn.Quote));
        Assert.False(t.HasBooking());
        Assert.Equal(20000, f.Money);
    }

    [Fact]
    public void ARepeatedOrLateSelectNeverTogglesTwice()
    {
        var (t, f, w, u) = Open();
        Assert.True(w.OnRowReleased(t, f, u, 5, Sel(0)));
        Assert.False(w.OnRowReleased(t, f, u, 5, Sel(0)));   // the same release again
        Assert.False(w.OnRowReleased(t, f, u, 4, Sel(0)));   // an older one arriving late
        Assert.Equal([Sel(0)], w.Lineup);
    }

    [Fact]
    public void APublishedViewIsNeverChanged()
    {
        var (t, f, w, u) = Open();
        var published = w.View;
        string key = published.Key;
        long seq = published.LastSeq;
        Assert.False(w.OnBuyRefresh(t, f, 77, 99999));   // recorded (LastSeq 77), not run
        Assert.Equal(seq, published.LastSeq);
        Assert.Equal(key, published.Key);
        Assert.NotSame(published, w.View);
        Assert.Equal(77, w.View.LastSeq);
    }

    [Fact]
    public void ARefreshThatThrowsLogsTheCppLineAndAbandonsTheWindow()
    {
        // TownArenaUI.cpp:955-956: the error line, then AbandonWindow (gone; the next Show opens it again)
        var (t, f, w, u) = Open();
        Assert.True(w.OnRowReleased(t, f, u, 1, Sel(0)));
        u.Throw = true;
        Refresh(t, f, w, u);
        Assert.Equal(["Proving Grounds: town registry refresh failed: a broken read"], u.Errors);
        Assert.False(w.IsVisible);
        Assert.False(w.View.Open);
        Assert.Empty(w.View.Cards);
        Assert.Equal([Sel(0)], w.Lineup);   // the team stays, as the C++'s
        Refresh(t, f, w, u);                // closed: no refresh, no second line
        Assert.Single(u.Errors);
        u.Throw = false;
        w.Show(t, f, u, TownFake.TownRegistry().Building, true);
        Assert.True(w.View.Open);
        Assert.Equal(5, w.View.Cards.Count);
    }

    [Fact]
    public void TheDirectButtonIsDroppedUnlessItsEnabledRuleHolds()
    {
        // TownArenaUI.cpp:925: enabled only while !IsPlayerMatch() && (HasBooking() || lineup.size() == 1)
        var (t, f, w, u) = Open();
        Assert.True(w.OnRowReleased(t, f, u, 1, Sel(0)));
        Assert.True(w.OnRowReleased(t, f, u, 2, Sel(1)));   // two fighters: the button is disabled
        Assert.False(w.OnBook(t, f, u, 3, "Book direct 1v1"));
        Assert.False(t.HasBooking());
        Assert.NotEqual("Assign exactly one fighter for a direct 1v1.", w.View.Feedback);   // not run at all
    }
}
