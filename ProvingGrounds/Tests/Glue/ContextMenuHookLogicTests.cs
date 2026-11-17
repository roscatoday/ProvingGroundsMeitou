using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>ContextMenuHooks.cpp's decisions (task 4.2): which right presses are ours, the use rows, the "use" pointer and its reset.
/// The window glue around them (the PlayerOrders.Give gate BeforeOrder, its order.Site lookup through world.Buildings.Find, the compiled
/// interface reads of the right press and the pointer) needs the game and is checked live only (tools/scenarios/arena-open.sh).</summary>
public class ContextMenuHookLogicTests
{
    static readonly HashSet<string> Ours = [PgRecords.Registry, PgRecords.Banner, PgRecords.Leaderboard];

    [Fact]
    public void UseRows_AreTheCppFour()
    {
        Assert.True(ContextMenuHookLogic.IsRegistryUseTask(97));
        Assert.True(ContextMenuHookLogic.IsRegistryUseTask(87));
        Assert.True(ContextMenuHookLogic.IsRegistryUseTask(152));
        Assert.True(ContextMenuHookLogic.IsRegistryUseTask(221));
        Assert.False(ContextMenuHookLogic.IsRegistryUseTask(29));   // MoveCusOrdered
        Assert.False(ContextMenuHookLogic.IsRegistryUseTask(0));
    }

    [Fact]
    public void Press_FinishedOurs_Approach_UnfinishedSwallowed()
    {
        Assert.Equal(InteractPress.Approach, ContextMenuHookLogic.DecidePress(true, PgRecords.Registry, true, false, false, 1, Ours));
        Assert.Equal(InteractPress.Approach, ContextMenuHookLogic.DecidePress(true, PgRecords.Leaderboard, true, false, false, 3, Ours));
        Assert.Equal(InteractPress.Unfinished, ContextMenuHookLogic.DecidePress(true, PgRecords.Banner, false, false, false, 1, Ours));
    }

    [Fact]
    public void Press_OtherCases_AreTheGames()
    {
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecidePress(false, PgRecords.Registry, true, false, false, 1, Ours));   // a character in front
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecidePress(true, null, true, false, false, 1, Ours));                 // the ground
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecidePress(true, PgRecords.OldArena, true, false, false, 1, Ours));   // the arena itself
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecidePress(true, PgRecords.Registry, true, true, false, 1, Ours));    // a town house repair
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecidePress(true, PgRecords.Registry, true, false, true, 1, Ours));    // destroyed
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecidePress(true, PgRecords.Registry, true, false, false, 0, Ours));   // nobody selected
    }

    [Fact]
    public void Press_NobodySelected_WhileTheArenaIsBusy_OpensAtOnce()
    {
        Assert.Equal(InteractPress.Approach, ContextMenuHookLogic.DecidePress(true, PgRecords.Registry, true, false, false, 0, Ours, arenaBusy: true));
        Assert.Equal(InteractPress.Approach, ContextMenuHookLogic.DecidePress(true, PgRecords.Leaderboard, true, false, false, 0, Ours, arenaBusy: true));
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecidePress(true, PgRecords.Registry, false, false, false, 0, Ours, arenaBusy: true));   // unfinished
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecidePress(true, PgRecords.Registry, true, false, false, 0, Ours, arenaBusy: false));
    }

    [Fact]
    public void QuickOrder_DroppedOnlyForAnUnfinishedOneOfOurs()
    {
        Assert.True(ContextMenuHookLogic.DropsQuickOrder(PgRecords.Registry, false, false, false, Ours));
        Assert.False(ContextMenuHookLogic.DropsQuickOrder(PgRecords.Registry, true, false, false, Ours));    // finished: the press never got here
        Assert.False(ContextMenuHookLogic.DropsQuickOrder(PgRecords.OldArena, false, false, false, Ours));   // not ours
        Assert.False(ContextMenuHookLogic.DropsQuickOrder(PgRecords.Registry, false, true, false, Ours));    // a town house repair
        Assert.False(ContextMenuHookLogic.DropsQuickOrder(PgRecords.Registry, false, false, true, Ours));    // a ruin (Repair)
        Assert.False(ContextMenuHookLogic.DropsQuickOrder(null, false, false, false, Ours));
    }

    [Fact]
    public void WorldPick_SkippedOnlyWithNoRecordAndNothingSuppressed()
    {
        Assert.False(ContextMenuHookLogic.NeedsWorldPick(0, null));       // no Registry / Banner / Leaderboard of the player: the game's answer
        Assert.True(ContextMenuHookLogic.NeedsWorldPick(1, null));
        Assert.True(ContextMenuHookLogic.NeedsWorldPick(0, "1-reg"));     // a suppressed pointer still needs the pick to see it leave
        Assert.True(ContextMenuHookLogic.NeedsWorldPick(3, "1-reg"));
    }

    [Fact]
    public void Cursor_UseOverOurs_WithASelection()
    {
        var c = new ArenaCursorState();
        Assert.True(c.WantsUse("1-reg", true, true));
        Assert.False(c.WantsUse("1-reg", true, false));
        Assert.False(c.WantsUse("2-house", false, true));
        Assert.False(c.WantsUse(null, false, true));
    }

    [Fact]
    public void Cursor_AfterTheClick_GamePointerUntilThePointerLeaves()
    {
        var c = new ArenaCursorState();
        c.Suppress("1-reg");
        Assert.False(c.WantsUse("1-reg", true, true));
        Assert.False(c.WantsUse("1-reg", true, true));
        Assert.Equal("1-reg", c.Suppressed);
        Assert.True(c.WantsUse("2-board", true, true));   // another of ours: the suppression ends
        Assert.Null(c.Suppressed);
        Assert.True(c.WantsUse("1-reg", true, true));

        c.Suppress("1-reg");
        Assert.False(c.WantsUse(null, false, true));     // off every building: ends too
        Assert.Null(c.Suppressed);

        c.Suppress("1-reg");
        c.Reset();
        Assert.True(c.WantsUse("1-reg", true, true));
    }

    // ---- task 5.3a: the money trade with Null (showTradeWindow_hook :173-226, the pending trade UI of Tick :290-325) ----

    const string NullId = "166-Proving Grounds.mod";

    [Fact]
    public void Trade_WithNull_IsCaughtForThePlayerSide()
    {
        // Meitou's OpenTrade(target = the player, me = the speaker): first = the player, second = Null
        Assert.Equal(TradeCatch.OpenForFirst, PendingTradeUi.DecideTrade("pg-sc-test", NullId, true, false));
        Assert.Equal(TradeCatch.OpenForSecond, PendingTradeUi.DecideTrade(NullId, "pg-sc-test", false, true));
        Assert.Equal(TradeCatch.PassThrough, PendingTradeUi.DecideTrade("x", "y", true, true));             // nobody is Null
        Assert.Equal(TradeCatch.PassThrough, PendingTradeUi.DecideTrade(NullId, NullId, true, true));       // both: the C++ passes through
        Assert.Equal(TradeCatch.NoPlayer, PendingTradeUi.DecideTrade("npc", NullId, false, true));           // the other side is no player
        Assert.Equal(TradeCatch.PassThrough, PendingTradeUi.DecideTrade(null, null, true, true));
    }

    [Fact]
    public void Trade_WithNull_PassesThroughWhenMeitouWouldOpenNoTradeWindow()
    {
        // DialogueSystem.Effects.cs:158 opens the trade window only for a speaker with a squad (mc is { SquadId: >= 0 }); the C++ hook sat in
        // showTradeWindow, so it never saw a trade that opened no window
        Assert.Equal(TradeCatch.PassThrough, PendingTradeUi.DecideTrade("pg-sc-test", NullId, true, false, windowOpens: false));
        Assert.Equal(TradeCatch.OpenForFirst, PendingTradeUi.DecideTrade("pg-sc-test", NullId, true, false, windowOpens: true));
    }

    [Fact]
    public void PendingTrade_WaitsForTheDialogueThenOpensOnce()
    {
        var p = new PendingTradeUi<int>();
        Assert.Equal(PendingTradeStep.None, p.Tick(false, _ => true, out _));
        p.QueueRewards(7);
        Assert.True(p.Pending);
        Assert.Equal(PendingTradeStep.Wait, p.Tick(true, _ => true, out _));
        Assert.Equal(PendingTradeStep.OpenRewards, p.Tick(false, c => c == 7, out int who));
        Assert.Equal(7, who);
        Assert.False(p.Pending);
        Assert.Equal(PendingTradeStep.None, p.Tick(false, _ => true, out _));
    }

    [Fact]
    public void PendingTrade_AStaleParticipantIsDiscardedEvenDuringTheDialogue()
    {
        var p = new PendingTradeUi<int>();
        p.QueueRewards(7);
        Assert.Equal(PendingTradeStep.Discard, p.Tick(true, _ => false, out _));   // ResolvePending: participant first
        Assert.False(p.Pending);
        p.QueueRewards(8);
        p.Reset();
        Assert.Equal(PendingTradeStep.None, p.Tick(false, _ => true, out _));
    }

    // ---- task 8.1a: the bookie's money trade (showTradeWindow_hook :180-196, Tick :290-325's bookie half) ----

    [Fact]
    public void Bookie_trade_is_caught_for_the_player_side_and_passes_through_without_one()
    {
        // Meitou's OpenTrade(target = the player, me = the bookie): first = the player, second = the bookie
        Assert.Equal(BookieTradeCatch.OpenForFirst, BookieTradeRule.Decide(false, true, true, false));
        Assert.Equal(BookieTradeCatch.OpenForSecond, BookieTradeRule.Decide(true, false, false, true));
        Assert.Equal(BookieTradeCatch.PassThrough, BookieTradeRule.Decide(false, true, false, false));   // no player: the game's trade, no line
        Assert.Equal(BookieTradeCatch.NotBookie, BookieTradeRule.Decide(false, false, true, true));      // nobody is the bookie: Null's test next
        Assert.Equal(BookieTradeCatch.NotBookie, BookieTradeRule.Decide(true, true, true, true));        // both (firstIsBookie == secondIsBookie)
        Assert.Equal(BookieTradeCatch.NotBookie, BookieTradeRule.Decide(false, true, true, false, windowOpens: false));
    }

    [Fact]
    public void Pending_bookie_trade_opens_the_betting_window_for_a_live_bookie_and_drops_a_stale_one()
    {
        var p = new PendingTradeUi<int>();
        p.QueueBookie(7, 111);
        Assert.Equal(PendingTradeStep.Wait, p.Tick(true, _ => true, b => b == 111, out _, out _));
        Assert.Equal(PendingTradeStep.OpenBookie, p.Tick(false, c => c == 7, b => b == 111, out int who, out int bookie));
        Assert.Equal((7, 111), (who, bookie));
        Assert.False(p.Pending);
        p.QueueBookie(7, 111);
        Assert.Equal(PendingTradeStep.Discard, p.Tick(true, _ => true, _ => false, out _, out _));   // :302 IsBookie(bookie) false: stale
        p.QueueRewards(7);   // Null's: the bookie test is not asked (:302 !openBookie)
        Assert.Equal(PendingTradeStep.OpenRewards, p.Tick(false, _ => true, _ => false, out _, out int none));
        Assert.Equal(0, none);   // g_pendingBookie.setNull() (:223)
    }

    [Fact]   // task 7.6: the Town Registry (a level building, record 195) takes a right press with nothing in front, as a player Registry does
    public void A_right_press_on_the_town_registry_approaches_it()
    {
        const string Reg = "195-Proving Grounds.mod";
        Assert.Equal(InteractPress.Approach, ContextMenuHookLogic.DecideTownPress(true, Reg, true, false, true, 2));
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecideTownPress(false, Reg, true, false, true, 2));   // something in front keeps its click
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecideTownPress(true, Reg, false, false, true, 2));   // a player site is the site route's
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecideTownPress(true, Reg, true, true, true, 2));     // a ruin
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecideTownPress(true, "45-Proving Grounds.mod", true, false, true, 2));   // a player Registry record
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecideTownPress(true, null, true, false, true, 2));
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecideTownPress(true, Reg, true, false, true, 0));    // nobody selected
        Assert.Equal(InteractPress.Approach, ContextMenuHookLogic.DecideTownPress(true, Reg, true, false, true, 0, arenaBusy: true));
    }

    [Fact]   // task 7.6 review 2: an unfinished town building (under repair) is not ours (C++ :127-140 "interact RMB ignored (not finished)")
    public void An_unfinished_town_registry_is_left_to_the_game()
    {
        Assert.Equal(InteractPress.Unfinished, ContextMenuHookLogic.DecideTownPress(true, "195-Proving Grounds.mod", true, false, false, 2));
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecideTownPress(true, "195-Proving Grounds.mod", true, false, false, 0, arenaBusy: true));
    }

    [Fact]   // task 7.6 review 2: only the building's own seat counts as nothing in front; a bench in front keeps its click
    public void Only_the_buildings_own_seat_lets_the_press_through()
    {
        const string Key = "10.34:38756-Proving Grounds-INGAME";
        Assert.True(ContextMenuHookLogic.NothingInFront(true, false, null, Key));
        Assert.True(ContextMenuHookLogic.NothingInFront(false, true, Key, Key));
        Assert.False(ContextMenuHookLogic.NothingInFront(false, true, "10.34:31297-Proving Grounds-INGAME", Key));
        Assert.False(ContextMenuHookLogic.NothingInFront(false, true, null, Key));
        Assert.False(ContextMenuHookLogic.NothingInFront(false, false, Key, Key));   // another kind (a character, a door) in front
    }

    [Fact]   // task 7.6 review: the town Leaderboard (272) too (IsInteractUiBuilding :103-106 = IsMatchUiOpener || IsLeaderboard); not its function record
    public void A_right_press_on_the_town_leaderboard_approaches_it()
    {
        Assert.Equal(InteractPress.Approach, ContextMenuHookLogic.DecideTownPress(true, "272-Proving Grounds.mod", true, false, true, 1));
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecideTownPress(false, "272-Proving Grounds.mod", true, false, true, 1));
        Assert.Equal(InteractPress.Vanilla, ContextMenuHookLogic.DecideTownPress(true, "272-Proving Grounds.mod", true, true, true, 1));
    }
}
