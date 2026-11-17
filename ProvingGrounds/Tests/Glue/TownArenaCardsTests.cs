using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 7.5 (TownArena.cpp:177-295, 347-366, 466-573, 691-1072, 1098-1295, 1360-1404, the player half of BeginEvent; TW §2.5): the card, its
/// previews and quotes, booking, the direct 1v1, the paid refresh, settlement and progress, over <see cref="TownFake"/>. The C++ NULL is -1 and the
/// first player character is id 0 (as Meitou's default(CharacterId)): a test that would pass with a default slipping in for NULL fails here.</summary>
public class TownArenaCardsTests
{
    const string Polearm = "219-Proving Grounds.mod";
    const string RessaVane = "616-Proving Grounds.mod";
    static readonly Vector3 ArenaPos = TownFake.RegistryPos + new Vector3(600, 0, 0);
    static readonly PlacedBuilding Arena = TownFake.Building("10.34:40000", "183-Proving Grounds.mod", pos: ArenaPos);

    static string Sel(int c) => "player:0:0:0:" + c + ":1";

    /// <summary>Scratch: registry, Rust Crucible, players 0 and 1, six Polearm fighters (10-15), four medics (30-33); the registry bound and the roster
    /// settled, the schedule paused so no ambient bout is planned (tests that need one resume it).</summary>
    static (TownArenaLogic<int> L, TownFake F) Town(int medics = 4)
    {
        var f = new TownFake();
        f.Buildings.Add(TownFake.TownRegistry());
        f.Buildings.Add(Arena);
        f.Player(0);
        f.Player(1, dx: 12);
        f.AbilityOf[0] = 90; f.AbilityOf[1] = 90;   // the Easy direct-1v1 band against ability 70 (measured over SelectPlayer)
        for (int i = 0; i < 6; ++i) f.Npc(10 + i, Polearm, dx: 30 + i, ability: 70);
        for (int i = 0; i < medics; ++i) f.Npc(30 + i, TownFake.Medic, dx: 60 + i);
        var l = new TownArenaLogic<int>(TownFake.NoCharacter);
        f.Logic = l;
        l.State.SchedulePaused = true;           // no ambient bout planned (a booking still runs: TownArena.cpp:1944)
        l.BindRegistry(TownFake.TownRegistry().Building);
        l.Tick(f);                               // starts the settle clock
        f.Now += 11000; l.Tick(f);               // settled
        Assert.True(l.RosterSettled(f));
        Assert.NotNull(l.State.TownRegistry);
        return (l, f);
    }

    static void Preview(TownArenaLogic<int> l, TownFake f, params int[] players)
    {
        f.Now += 1000;
        l.UpdateMatchmakingPreview(f, players);
    }

    /// <summary>The worker found <paramref name="b"/> for slot <paramref name="slot"/> against the players <paramref name="a"/>.</summary>
    static void Found(TownArenaLogic<int> l, TownFake f, int slot, string[] a, string[] b, bool bestAvailable = false) =>
        f.Results.Enqueue(new TownMatchmakingSearch.Result
        {
            Generation = l.State.SearchGeneration, Slot = slot,
            Match = new TownMatchmakingPolicy.Match { Valid = true, A = [.. a], B = [.. b], BestAvailable = bestAvailable },
        });

    /// <summary>A visible, available slot of the card.</summary>
    static int OpenSlot(TownArenaLogic<int> l, TownFake f, int players = 1)
    {
        var card = l.Challenges(f);
        for (int slot = 0; slot < 5; ++slot)
            if (TownChallengePolicy.Visible(card, slot) && card.Offers[slot].State == TownArenaPolicy.OfferState.Available) return slot;
        throw new InvalidOperationException("no open slot");
    }

    /// <summary>One player (0) previewed, slot <c>slot</c> resolved against Polearm 10 with a valid quote.</summary>
    static (TownArenaLogic<int> L, TownFake F, int Slot) Quoted(int[]? players = null, string[]? opponents = null)
    {
        var (l, f) = Town();
        players ??= [0];
        Preview(l, f, players);
        int slot = OpenSlot(l, f);
        Found(l, f, slot, [.. players.Select(Sel)], opponents ?? ["fid-10"]);
        Preview(l, f, players);
        Assert.True(TownChallengePolicy.Resolved(l.State.Display.Previews[slot]));
        Assert.True(l.GetChallengeBuyIn(f, slot).Valid(), "quote " + l.GetChallengeBuyIn(f, slot).BuyIn + " reason " + l.GetChallengeUnavailableReason(f, slot));
        return (l, f, slot);
    }

    static (TownArenaLogic<int> L, TownFake F, int Slot, TownChallengeBuyInPolicy.Quote Quote) Booked(int[]? players = null, string[]? opponents = null)
    {
        var (l, f, slot) = Quoted(players, opponents);
        var quote = l.GetChallengeBuyIn(f, slot);
        Assert.True(l.BookChallenge(f, players ?? [0], slot), l.GetStatus());
        return (l, f, slot, quote);
    }

    /// <summary>The booking's next idle tick begins the player event (TownArena.cpp:1963-2001).</summary>
    static void BeginBooked(TownArenaLogic<int> l, TownFake f)
    {
        f.Now += 5000;
        l.Tick(f);
        Assert.Equal(TownPhase.Preparing, l.State.Phase);
    }

    static void StartFight(TownArenaLogic<int> l, TownFake f)
    {
        f.Active = true;
        for (int i = 0; i < l.State.FighterCount; ++i) f.Participants.Add(l.State.Fighters[i]);
        f.Pending = false;
        f.Now += 16; l.Tick(f);
        Assert.Equal(TownPhase.Fighting, l.State.Phase);
    }

    static void EndFight(TownArenaLogic<int> l, TownFake f, SparPodium.OutcomeKind outcome)
    {
        l.OnMatchResult(f, new SparPodium.Snapshot { Outcome = outcome });
        f.Active = false; f.Participants.Clear();
        f.Now += 16; l.Tick(f);   // the fight is over: aftercare, then (no M8 aftercare yet) Finish
        f.Now += 2000; l.Tick(f);
        Assert.Equal(TownPhase.Idle, l.State.Phase);
    }

    // ---------- the card: epoch, seed, refresh (TW §2.5 "Refresh schedule") ----------

    [Fact]
    public void FirstCardIsSeededFromTheSaveGenerationOnceThenKeepsItsSeed()
    {
        var (l, f) = Town();
        f.Card = new TownChallengePolicy.Card();
        f.SeedKey = "0f6e1c2a-generation";
        var expected = new TownChallengePolicy.Card();
        TownArena.FoldSaveKey(expected, f.SeedKey);
        TownChallengePolicy.RefreshGenerated(expected, TownArenaPolicy.ChallengeCardEpoch(f.Hours, 12), -1);
        var card = l.Challenges(f);
        Assert.Equal(expected.Seed, card.Seed);
        uint seed = card.Seed;
        f.SeedKey = "another-generation";   // a later save's generation never re-seeds a generated card
        l.Challenges(f);
        Assert.Equal(seed, f.Card.Seed);
    }

    [Fact]
    public void StaleSlotsRegenerateAtTheNextEpochExceptTheBookedOne()
    {
        var (l, f, slot, _) = Booked();
        int epoch = TownArenaPolicy.ChallengeCardEpoch(f.Hours, 12);
        Assert.All(f.Card.Offers, o => Assert.Equal(epoch, o.Epoch));
        f.Hours += 12;   // 06:00 -> 18:00
        var card = l.Challenges(f);
        for (int i = 0; i < 5; ++i)
            Assert.Equal(i == slot ? epoch : epoch + 1, card.Offers[i].Epoch);
        Assert.Equal(TownArena.NextChallengeRefreshCaption(true, f.Hours, 12), l.GetNextChallengeRefreshCaption(f));
    }

    [Fact]
    public void NoCardWorkWithoutAnActiveSave()
    {
        var (l, f) = Town();
        f.ActiveSave = false;
        var card = l.Challenges(f);
        Assert.All(card.Offers, o => Assert.Equal(-2, o.Epoch));
    }

    // ---------- previews (TW §2.5 "What a card shows") ----------

    [Fact]
    public void PreviewQueuesOneSearchPerGeneratedSlotAndSaysSearching()
    {
        var (l, f) = Town();
        Preview(l, f, 0);
        var card = l.Challenges(f);
        int visible = Enumerable.Range(0, 5).Count(i => TownChallengePolicy.Visible(card, i));
        Assert.Equal(visible, f.Enqueued.Count);
        var request = f.Enqueued[0];
        Assert.Equal(l.State.SearchGeneration, request.Generation);
        Assert.Equal([Sel(0)], request.Players.Select(p => p.Id));
        Assert.Equal(4, request.MaxTotal);   // the medics
        Assert.Equal(TownArena.SearchRequestSeed(card.Offers[request.Slot], request.Slot, card, out _, out _), request.Seed);
        Assert.True(l.IsChallengeSearching(request.Slot));
        Assert.Equal("Searching eligible opponents...", l.GetChallengeUnavailableReason(f, request.Slot));
        Assert.False(l.GetChallengeBuyIn(f, request.Slot).Valid());
        Preview(l, f, 0);   // a second refresh queues nothing new for the same generation
        Assert.Equal(visible, f.Enqueued.Count);
    }

    [Fact]
    public void ResultResolvesThePreviewWithAQuoteFromTheTeamScores()
    {
        var (l, f, slot) = Quoted();
        var offer = l.State.Display.Previews[slot];
        Assert.Equal("fid-10", offer.Opponent);
        Assert.Equal("Npc 10", offer.Name.Split(" (")[0]);
        Assert.Equal(Polearm, offer.Roles[0]);
        Assert.Equal(1, offer.PlayerCount);
        Assert.Equal("", l.GetChallengeUnavailableReason(f, slot));
        Assert.False(l.IsChallengeSearching(slot));
        // own: player 0 ability 90 rating 100; enemy: Polearm 10 ability 70 rating 100, influence 1.0
        double own = TownMatchmakingPolicy.TeamScore([90.0], [100.0], 1), enemy = TownMatchmakingPolicy.TeamScore([70.0], [100.0], 1);
        var price = TownChallengeBuyInPolicy.Price(f.Card.Division, own, enemy);
        Assert.Equal(price.BuyIn, l.GetChallengeBuyIn(f, slot).BuyIn);
        Assert.Equal(price.Payout, l.GetChallengeBuyIn(f, slot).Payout);
        Assert.Contains("Book", l.GetChallengeCaption(f, slot));
    }

    [Fact]
    public void FailedSearchesGiveTheTooWeakOrNoOpponentReason()
    {
        var (l, f) = Town();
        Preview(l, f, 0);
        int a = f.Enqueued[0].Slot, b = f.Enqueued[1].Slot;
        f.Results.Enqueue(new() { Generation = l.State.SearchGeneration, Slot = a, Match = new() { Failure = TownMatchmakingPolicy.Match.FailureCode.PlayersTooWeak } });
        f.Results.Enqueue(new() { Generation = l.State.SearchGeneration, Slot = b, Match = new() });
        f.Results.Enqueue(new() { Generation = l.State.SearchGeneration + 7, Slot = b, Match = new() { Valid = true, A = [Sel(0)], B = ["fid-10"] } });   // stale
        Preview(l, f, 0);
        Assert.Equal("Selected fighter is too weak for this challenge.", l.GetChallengeUnavailableReason(f, a));
        string reason = l.GetChallengeUnavailableReason(f, b);
        Assert.True(reason == "No suitable recovered opponents for this challenge." || reason.Contains(": "), reason);
        Assert.False(TownChallengePolicy.Resolved(l.State.Display.Previews[b]));
    }

    [Fact]
    public void BestAvailableIsMarkedAndAPreviewNeedingMedicsWaits()
    {
        var (l, f) = Town(medics: 1);
        Preview(l, f, 0);
        int slot = f.Enqueued[0].Slot;
        Found(l, f, slot, [Sel(0)], ["fid-10"], bestAvailable: true);
        Preview(l, f, 0);
        Assert.True(l.IsBestAvailableChallenge(slot));
        Assert.Equal("Waiting for medical capacity", l.GetChallengeUnavailableReason(f, slot));   // 1 + 1 fighters need 2 medics
        Assert.False(l.GetChallengeBuyIn(f, slot).Valid());
    }

    [Fact]
    public void ARecoveringPlayerBlocksEveryCard()
    {
        var (l, f) = Town();
        f.Health[0] = (0.5f, 1f);
        Preview(l, f, 0);
        Assert.Empty(f.Enqueued);
        Assert.False(l.GetPlayerReadiness(f, 0, out string why));
        Assert.Equal("recovering", why);
        Assert.StartsWith("Player 0: ", l.GetChallengeUnavailableReason(f, OpenSlot(l, f)));
    }

    [Fact]
    public void DirectPreviewNeedsExactlyOneFighter()
    {
        var (l, f) = Town();
        Preview(l, f, 0);
        Assert.True(TownChallengePolicy.Resolved(l.State.Display.DirectPreview));
        Assert.StartsWith("Book Easy 1v1 vs Npc 1", l.GetDirectCaption(f));
        var (l2, f2) = Town();
        Preview(l2, f2, 0, 1);
        Assert.False(TownChallengePolicy.Resolved(l2.State.Display.DirectPreview));
        Assert.Equal("1v1: select one fighter with suitable recovered opposition", l2.GetDirectCaption(f2));
    }

    // ---------- booking (TW §2.5 steps 1-5) ----------

    [Fact]
    public void BookingTakesTheBuyInIntoEscrowAndReservesTheSlot()
    {
        var (l, f, slot) = Quoted();
        f.Card.PaidRefreshCount = 3;
        var quote = l.GetChallengeBuyIn(f, slot);
        Assert.True(l.BookChallenge(f, [0], slot), l.GetStatus());
        Assert.Equal(20000 - quote.BuyIn, f.Money);
        Assert.Equal(quote.BuyIn, f.Account.Escrow);
        Assert.True(l.State.Display.BuyInTicket.Pending);
        Assert.Equal(0, f.Card.PaidRefreshCount);
        Assert.Equal(3, l.State.PaidRefreshCountBeforeBooking);
        Assert.True(l.HasBooking());
        Assert.Equal(slot, l.State.QueuedChallenge);
        Assert.Equal(0, l.State.BookedPlayer);
        Assert.Equal([0], l.State.BookedTeam);
        Assert.Equal(TownArenaPolicy.OfferState.Booked, f.Card.Offers[slot].State);
        Assert.Equal("fid-10", l.State.Display.ChallengeReservation.Opponent);
        Assert.NotNull(f.Accepted);
        Assert.Equal(quote.BuyIn, f.Accepted!.Stake);
        Assert.Equal(quote, l.GetChallengeBuyIn(f, slot));   // the ticket's quote is shown for the booked slot
        Assert.StartsWith("Challenge reserved: ", l.GetStatus());
        Assert.EndsWith(" | Buy-in: " + quote.BuyIn + " Cats | Winning total return: " + quote.Payout + " Cats", l.GetStatus());
        Assert.True(l.IsLineupLocked());
    }

    [Fact]
    public void BookingCancelsTheAnnouncedNpcBout()
    {
        var (l, f, slot) = Quoted();
        l.State.PlannedNpc = new TownMatchmakingPolicy.Match { Valid = true, A = ["fid-11"], B = ["fid-12"] };
        l.State.NextNpcHours = f.Hours + 1;
        Assert.True(l.BookChallenge(f, [0], slot));
        Assert.False(l.State.PlannedNpc.Valid);
        Assert.Contains("bookie cancel: " + TownArena.Text.PlayerTookSlot, f.Calls);
    }

    [Fact]
    public void BookingRefusesAChangedQuoteAndTakesNothing()
    {
        var (l, f, slot) = Quoted();
        l.State.Display.BuyInQuotes[slot].BuyIn += 10;   // what the card shows no longer equals the fresh quote
        Assert.False(l.BookChallenge(f, [0], slot));
        Assert.Equal(TownArena.Text.BookReviewBuyIn, l.GetStatus());
        Assert.Equal(20000, f.Money);
        Assert.False(l.HasBooking());
        Assert.Equal(TownArenaPolicy.OfferState.Available, f.Card.Offers[slot].State);
    }

    [Fact]
    public void BookingRefusesTooLittleMoneyTheWrongCountAndANotReadyFighter()
    {
        var (l, f, slot) = Quoted();
        int buyIn = l.GetChallengeBuyIn(f, slot).BuyIn;
        f.Money = buyIn - 1;
        Assert.False(l.BookChallenge(f, [0], slot));
        Assert.Equal(TownArena.ChallengeRequiresMessage(buyIn, buyIn - 1), l.GetStatus());
        f.Money = 20000;
        Assert.False(l.BookChallenge(f, [0, 1], slot));
        Assert.Equal(TownArena.SelectExactlyMessage(1), l.GetStatus());
        f.TownOf[0] = TownFake.Elsewhere;
        Assert.False(l.BookChallenge(f, [0], slot));
        Assert.Equal(TownArena.Text.BookFightersNotReady, l.GetStatus());
        Assert.Equal(20000, f.Money);
        Assert.False(l.HasBooking());
    }

    [Fact]
    public void BookingRefusesWhenTheWalletWillNotPay()
    {
        var (l, f, slot) = Quoted();
        f.TakeFails = true;
        Assert.False(l.BookChallenge(f, [0], slot));
        Assert.Equal(TownArena.Text.BookCollectFailed, l.GetStatus());
        Assert.False(l.HasBooking());
        Assert.Equal(-1, l.State.QueuedChallenge);
        Assert.Equal(TownArenaPolicy.OfferState.Available, f.Card.Offers[slot].State);   // CancelBooking gave the slot back
        Assert.Equal(0, f.Account.Escrow);
    }

    [Fact]
    public void SkarnCooldownBlocksASecondSkarnAttempt()
    {
        var (l, f, slot) = Quoted();
        ref var preview = ref l.State.Display.Previews[slot];
        preview.Encounter = (int)TownChallengePolicy.Encounter.Skarn;
        f.Card.Offers[slot].Encounter = (int)TownChallengePolicy.Encounter.Skarn;
        f.Card.SkarnAttempted = true; f.Card.SkarnLastEnd = f.Hours - 4;
        f.Card.Division = 2; preview.Division = 2; f.Card.Offers[slot].Division = 2;
        f.Card.UniqueWins = (uint)TownChallengePolicy.UniqueVictory.AllUniqueVictories;   // Skarn unlocked
        Assert.False(l.BookChallenge(f, [0], slot));
        Assert.Equal(TownArena.SkarnCooldownMessage(24, f.Hours, f.Card.SkarnLastEnd), l.GetStatus());
        Assert.Equal("Skarn cooldown: 20.0 in-game hours remaining", l.GetStatus());
        Assert.Equal(20000, f.Money);
    }

    [Fact]
    public void ALineupChangedSinceThePreviewIsRefusedAndRefreshed()
    {
        var (l, f, slot) = Quoted([0]);
        l.State.Display.Previews[slot].Context = "|stale";
        Assert.False(l.BookChallenge(f, [0], slot));
        Assert.Equal(TownArena.Text.BookLineupChanged, l.GetStatus());
    }

    // ---------- the player event (TW §2.5 step 6) ----------

    [Fact]
    public void TheNextIdleTickBeginsThePlayerEventPlayersTeamA()
    {
        var (l, f, slot, _) = Booked();
        BeginBooked(l, f);
        var st = l.State;
        Assert.True(st.PlayerMatch);
        Assert.Equal(slot, st.ActiveChallenge);
        Assert.Equal(-1, st.QueuedChallenge);
        Assert.False(st.BookingPending);
        Assert.Equal(TownFake.NoCharacter, st.BookedPlayer);
        Assert.Equal(1, st.PlayerCount);
        Assert.Equal(new[] { 0, 10 }, f.Begun!.Value.Fighters.Take(f.Begun.Value.Count));
        Assert.Equal(new[] { MatchRules.MatchTeam.TeamA, MatchRules.MatchTeam.TeamB }, f.Begun.Value.Teams.Take(2));
        Assert.Equal(TownArena.MatchModeFor(st.Display.ChallengeReservation), f.Begun.Value.Mode);
        int rarity = TownChallengePolicy.OfferRarity(st.Display.ChallengeReservation, f.Card.Division);
        TownArena.ActiveMarks(true, f.Card.Division, slot, rarity, st.Display.ChallengeReservation.Encounter, out float mult, out int bonus);
        Assert.Equal(ArenaMarks.ChallengeMultiplier(f.Card.Division, rarity), st.ActiveMarksMultiplier);
        Assert.Equal(mult, st.ActiveMarksMultiplier);
        Assert.Equal(bonus, st.ActiveMarksBonus);
        Assert.DoesNotContain("bookie assembling", f.Calls);   // only an NPC bout opens the market
        Assert.StartsWith("Town ", l.GetStatus());
        Assert.EndsWith("assembling: 1 vs 1", l.GetStatus());
        StartFight(l, f);
        Assert.Equal(TownArenaPolicy.OfferState.Consumed, f.Card.Offers[slot].State);
        Assert.Equal(st.ActiveMarksMultiplier, l.GetActiveMarksMultiplier(f));
    }

    [Fact]
    public void ATwoFighterTeams1v1BooksBothPlayers()
    {
        var (l, f, slot, _) = Booked([0, 1], ["fid-10", "fid-11"]);
        f.Card.Offers[slot].Mode = l.State.Display.ChallengeReservation.Mode;
        BeginBooked(l, f);
        Assert.Equal(2, l.State.PlayerCount);
        Assert.Equal(new[] { 0, 1, 10, 11 }, f.Begun!.Value.Fighters.Take(4));
        Assert.Equal(new[] { MatchRules.MatchTeam.TeamA, MatchRules.MatchTeam.TeamA, MatchRules.MatchTeam.TeamB, MatchRules.MatchTeam.TeamB },
            f.Begun.Value.Teams.Take(4));
        Assert.Equal(TownArena.MatchModeFor(l.State.Display.ChallengeReservation), f.Begun.Value.Mode);
    }

    [Fact]
    public void AReservedOpponentNotNearTownMakesTheBookingWait()
    {
        var (l, f, _, _) = Booked();
        f.TownOf[10] = TownFake.Elsewhere;
        f.Now += 5000; l.Tick(f);
        Assert.Equal(TownPhase.Idle, l.State.Phase);
        Assert.True(l.HasBooking());
        Assert.Equal(TownArena.ChallengeWaitingMessage(l.State.Display.ChallengeReservation.Name, 0), l.GetStatus());
    }

    [Fact]
    public void ABookedFighterWhoLeftMakesTheBookingWaitAndNoDefaultPlayerIsUsed()
    {
        var (l, f, _, _) = Booked([1]);
        f.TownOf[1] = TownFake.Elsewhere;   // the booked fighter (1) away; player 0 (the default id) is in town and must not stand in
        f.Now += 5000; l.Tick(f);
        Assert.Equal(TownPhase.Idle, l.State.Phase);
        Assert.Equal(TownArena.Text.BookingWaitingFighter, l.GetStatus());
        Assert.Null(f.Begun);
    }

    // ---------- settlement (TW §2.5 "Buy-in and return") ----------

    [Fact]
    public void AWinCreditsThePayoutAndRecordsAChallengeWin()
    {
        var (l, f, slot, quote) = Booked();
        BeginBooked(l, f);
        StartFight(l, f);
        EndFight(l, f, SparPodium.OutcomeKind.TeamAWins);
        Assert.Equal(20000 - quote.BuyIn + quote.Payout, f.Money);
        Assert.Equal(0, f.Account.Escrow);
        Assert.Equal(0, f.Account.Credit);
        Assert.Equal([(1, quote.Payout)], f.Settled);
        Assert.Equal(1u, f.Card.ChallengeWins);
        Assert.Equal(TownArenaPolicy.OfferState.Consumed, f.Card.Offers[slot].State);
        Assert.Equal(0, f.Card.PaidRefreshCount);   // a win keeps the reset
        Assert.Contains(["fid-10"], f.Card.PlayerHistory);
        Assert.Contains(f.Debugs, d => d.StartsWith("Proving Grounds: shared town progression updated - Challenge wins: 1"));
    }

    [Fact]
    public void ALossCreditsNothing()
    {
        var (l, f, _, quote) = Booked();
        BeginBooked(l, f);
        StartFight(l, f);
        EndFight(l, f, SparPodium.OutcomeKind.TeamBWins);
        Assert.Equal(20000 - quote.BuyIn, f.Money);
        Assert.Equal(0, f.Account.Escrow);
        Assert.Equal([(0, 0)], f.Settled);
        Assert.Equal(0u, f.Card.ChallengeWins);
        Assert.Equal("Challenge lost: buy-in consumed.", TownArena.SettleChallengeMessage(0, 0));
    }

    [Fact]
    public void ADrawRefundsTheBuyInAndRestoresThePaidRefreshCount()
    {
        var (l, f, slot) = Quoted();
        f.Card.PaidRefreshCount = 2;
        var quote = l.GetChallengeBuyIn(f, slot);
        Assert.True(l.BookChallenge(f, [0], slot));
        BeginBooked(l, f);
        StartFight(l, f);
        EndFight(l, f, SparPodium.OutcomeKind.Draw);
        Assert.Equal(20000, f.Money);
        Assert.Equal([(-1, quote.BuyIn)], f.Settled);
        Assert.Equal(2, f.Card.PaidRefreshCount);
        Assert.Equal(0u, f.Card.ChallengeWins);
    }

    [Fact]
    public void CancellingTheBookingRefundsAndResetsTheOffers()
    {
        var (l, f, slot, quote) = Booked();
        l.CancelBooking(f);
        Assert.Equal(20000, f.Money);
        Assert.Equal([(-1, quote.BuyIn)], f.Settled);
        Assert.False(l.HasBooking());
        Assert.Equal(TownFake.NoCharacter, l.State.BookedPlayer);
        Assert.Equal(TownArenaPolicy.OfferState.Available, f.Card.Offers[slot].State);
        Assert.False(TownChallengePolicy.Resolved(l.State.Display.ChallengeReservation));   // cpp:1349
        Assert.False(TownChallengePolicy.Resolved(l.State.DirectBooking));                 // cpp:1348
        Assert.Equal(TownArena.Text.BookingCancelled, l.GetStatus());
    }

    [Fact]
    public void AnAbortedPlayerMatchRefundsAtFinish()
    {
        var (l, f, _, quote) = Booked();
        BeginBooked(l, f);
        StartFight(l, f);
        l.OnMatchAborted(f);
        l.CancelForSave(f);
        Assert.Equal(20000, f.Money);
        Assert.Equal([(-1, quote.BuyIn)], f.Settled);
    }

    [Fact]
    public void ACreditTheWalletCouldNotTakeIsPaidOnALaterTick()
    {
        var (l, f, _, quote) = Booked();
        BeginBooked(l, f);
        StartFight(l, f);
        f.Money = null;   // no player: the credit stays owed
        l.OnMatchResult(f, new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.TeamAWins });
        Assert.Equal(quote.Payout, f.Account.Credit);
        f.Money = 100;
        f.Now += 16; l.Tick(f);   // TownArena.cpp:1884
        Assert.Equal(100 + quote.Payout, f.Money);
        Assert.Equal(0, f.Account.Credit);
    }

    // ---------- progress (TW §2.5 "Progress, unique fighters, Skarn") ----------

    [Fact]
    public void BeatingANamedFighterSetsItsUniqueBitAndAddsTheUniqueBonus()
    {
        var (l, f, slot, _) = Booked();
        ref var r = ref l.State.Display.ChallengeReservation;
        r.Encounter = (int)TownChallengePolicy.Encounter.RessaVane;
        r.Roles[0] = RessaVane;
        BeginBooked(l, f);
        Assert.Equal(ArenaMarks.UniqueChallengeBonus(f.Card.Division, false), l.State.ActiveMarksBonus);
        StartFight(l, f);
        EndFight(l, f, SparPodium.OutcomeKind.TeamAWins);
        Assert.Equal((uint)TownChallengePolicy.UniqueVictory.RessaVane, f.Card.UniqueWins & (uint)TownChallengePolicy.UniqueVictory.RessaVane);
    }

    [Fact]
    public void SkarnsWinIsNeverRecorded()
    {
        // Kept C++ bug (TownArena.cpp:1118-1131 skip EncounterSkarn): beating Skarn sets no bit and never SkarnWon.
        var (l, f, _, _) = Booked();
        ref var r = ref l.State.Display.ChallengeReservation;
        r.Encounter = (int)TownChallengePolicy.Encounter.Skarn;
        r.Roles[0] = TownChallengePolicy.Role((int)TownChallengePolicy.Encounter.Skarn);
        r.Division = 2;
        f.Card.Division = 2;
        f.Card.UniqueWins = (uint)TownChallengePolicy.UniqueVictory.AllUniqueVictories;   // all five beaten: Skarn unlocked
        f.RoleOf[10] = TownChallengePolicy.Role((int)TownChallengePolicy.Encounter.Skarn);
        BeginBooked(l, f);
        Assert.Equal(ArenaMarks.UniqueChallengeBonus(f.Card.Division, true), l.State.ActiveMarksBonus);
        StartFight(l, f);
        EndFight(l, f, SparPodium.OutcomeKind.TeamAWins);
        Assert.False(f.Card.SkarnWon);
        Assert.Equal((uint)TownChallengePolicy.UniqueVictory.AllUniqueVictories, f.Card.UniqueWins);
        Assert.True(f.Card.SkarnAttempted);                // RecordSkarnChallengeEnd: the 24 h cooldown starts
        Assert.Equal(f.Hours, f.Card.SkarnLastEnd);
    }

    [Fact]
    public void ATeamBWinOrAStopNeverGrantsProgress()
    {
        var (l, f, _, _) = Booked();
        ref var r = ref l.State.Display.ChallengeReservation;
        r.Roles[0] = RessaVane;
        BeginBooked(l, f);
        StartFight(l, f);
        EndFight(l, f, SparPodium.OutcomeKind.TeamBWins);
        Assert.Equal(0u, f.Card.UniqueWins);
        Assert.Equal(0u, f.Card.ChallengeWins);
    }

    // ---------- the direct 1v1 (TownArena.cpp:1383-1404) ----------

    [Fact]
    public void ADirect1v1TakesNoBuyInAndPaysCommonMarks()
    {
        var (l, f) = Town();
        Preview(l, f, 0);
        Assert.True(l.BookPlayer(f, 0), l.GetStatus());
        Assert.Equal(20000, f.Money);
        Assert.False(l.State.Display.BuyInTicket.Pending);
        Assert.Equal(-1, l.State.QueuedChallenge);
        Assert.Equal(TownArena.DirectReservedMessage(l.State.DirectBooking.Name), l.GetStatus());
        BeginBooked(l, f);
        Assert.True(l.State.PlayerMatch);
        Assert.Equal(-1, l.State.ActiveChallenge);
        Assert.Equal(MatchRules.MatchMode.TeamAvB, f.Begun!.Value.Mode);
        Assert.Equal(new[] { 0, 10 }, f.Begun.Value.Fighters.Take(2).Select(x => x == 0 ? 0 : 10));
        Assert.Equal(ArenaMarks.ChallengeMultiplier(f.Card.Division, 0), l.State.ActiveMarksMultiplier);
        StartFight(l, f);
        EndFight(l, f, SparPodium.OutcomeKind.TeamAWins);
        Assert.Equal(20000, f.Money);
        Assert.Empty(f.Settled);
        Assert.Equal(0u, f.Card.ChallengeWins);   // RecordVictory(challenge false)
    }

    [Fact]
    public void ADirect1v1IsRefusedWhileBookedOrWhenTheLineupChanged()
    {
        var (l, f) = Town();
        Preview(l, f, 0);
        Assert.False(l.BookPlayer(f, 1));
        Assert.Equal(TownArena.Text.DirectLineupChanged, l.GetStatus());
        Assert.Equal(TownArenaLogic<int>.PlayerLineupKey(f, [1]), l.State.Display.DirectPreview.Context);   // the refusal refreshed the preview
        Preview(l, f, 0);
        Assert.True(l.BookPlayer(f, 0));
        Assert.False(l.BookPlayer(f, 0));
        Assert.Equal(TownArena.Text.PlayerBookingBusy, l.GetStatus());
    }

    // ---------- the paid refresh (TW §2.5 "Paid refresh") ----------

    [Fact]
    public void APaidRefreshCostsRound500Times2PowCountAndReplacesTheOffers()
    {
        var (l, f) = Town();
        var card = l.Challenges(f);
        var before = card.Offers.Select(o => o.Draw).ToArray();
        f.Card.PaidRefreshCount = 2;
        Assert.Equal(2000, l.GetChallengeRefreshCost(f));
        Assert.True(l.CanBuyChallengeRefresh(f));
        Assert.True(l.BuyChallengeRefresh(f), l.GetStatus());
        Assert.Equal(18000, f.Money);
        Assert.Equal(3, f.Card.PaidRefreshCount);
        Assert.Equal(TownArena.RefreshedMessage(2000, 4000), l.GetStatus());
        Assert.NotEqual(before, f.Card.Offers.Select(o => o.Draw).ToArray());
    }

    [Fact]
    public void APaidRefreshIsRefusedWhileBookedOrWithoutMoney()
    {
        var (l, f, _, _) = Booked();
        Assert.False(l.CanBuyChallengeRefresh(f));
        Assert.False(l.BuyChallengeRefresh(f));
        Assert.Equal(TownArena.Text.RefreshBooked, l.GetStatus());
        var (l2, f2) = Town();
        f2.Money = 100;
        Assert.False(l2.CanBuyChallengeRefresh(f2));
        Assert.False(l2.BuyChallengeRefresh(f2));
        Assert.Equal(TownArena.RefreshCostMessage(500, 100), l2.GetStatus());
        Assert.Equal(100, f2.Money);
    }

    [Fact]
    public void DivisionChangesOnlyWhileTheLineupIsFree()
    {
        var (l, f) = Town();
        l.Challenges(f);
        Assert.True(l.SetDivision(f, 1));
        Assert.Equal(1, l.GetDivision(f));
        var (l2, f2, _, _) = Booked();
        Assert.False(l2.SetDivision(f2, 2));
    }

    // ---------- NULL vs the first player (7.3's live defect) ----------

    [Fact]
    public void WithNoBookingTheIdleTickNeverStartsAPlayerEventForPlayerZero()
    {
        var (l, f) = Town();
        l.State.SchedulePaused = false;
        Assert.Equal(TownFake.NoCharacter, l.State.BookedPlayer);
        f.Now += 5000; l.Tick(f);
        Assert.False(l.State.PlayerMatch);
        Assert.False(l.State.Phase == TownPhase.Preparing && l.State.PlayerCount != 0);
    }

    [Fact]
    public void CancellingAQueuedBookingDuringARunningChallengeKeepsTheRunningReservation()
    {
        // cpp:1348-1349: g_directBooking always cleared; g_challengeReservation only when no challenge is running
        var (l, f, _, _) = Booked();
        BeginBooked(l, f);
        string running = l.State.Display.ChallengeReservation.Opponent;
        l.State.BookingPending = true;          // a second (direct) booking queued while the challenge runs
        l.State.DirectBooking.Opponent = "fid-12";
        l.CancelBooking(f);
        Assert.Equal(running, l.State.Display.ChallengeReservation.Opponent);
        Assert.Equal("", l.State.DirectBooking.Opponent);
    }
}
