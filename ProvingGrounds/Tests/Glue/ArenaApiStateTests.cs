using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 10.1 (port only): the view other mods read through ProvingGroundsApi, and the feed that builds it once per SlowWorld
/// (Core/Glue/ArenaApiState.cs). Ids are (slot, generation) pairs, as Meitou's CharacterId: a reused slot is another character.</summary>
public class ArenaApiStateTests
{
    static readonly (int, int) A = (5, 1), B = (6, 1), AReused = (5, 2);

    static ArenaApiFeed<(int, int)> Feed(long tick, params (int, int)[] inBout)
    {
        var f = new ArenaApiFeed<(int, int)>();
        Step(f, tick, inBout);
        return f;
    }

    static void Step(ArenaApiFeed<(int, int)> f, long tick, params (int, int)[] inBout)
    {
        f.Begin(tick);
        foreach (var id in inBout) f.Seen(id);
        f.End();
    }

    [Fact]
    public void BeforeTheFirstTickNothingIsReadyAndEveryAnswerIsFalse()
    {
        var v = new ArenaApiFeed<(int, int)>().View;
        Assert.False(v.Ready);
        Assert.False(v.InBout(A));
        Assert.False(v.IsTownFighter("Hagar"));
        Assert.False(v.IsTownFighterId(A));
        Assert.Same(ArenaApiView<(int, int)>.NotReady, v);
    }

    [Fact]
    public void TheFirstTickPublishesAReadyViewEvenWithNobodyInABout()
    {
        var f = Feed(10);
        Assert.True(f.View.Ready);
        Assert.Equal(0, f.View.InBoutCount);
    }

    [Fact]
    public void AFighterIsInABoutOnlyWithItsOwnGeneration()
    {
        var f = Feed(10, A);
        Assert.True(f.View.InBout(A));
        Assert.False(f.View.InBout(AReused));   // the slot reused by another character
        Assert.False(f.View.InBout(B));
    }

    [Fact]
    public void ALetGoFighterStaysInTheBoutForTheGraceThenLeaves()
    {
        var f = Feed(100, A, B);
        Step(f, 101, B);   // A let go at tick 101 (last seen 100)
        Assert.True(f.View.InBout(A));
        Step(f, 100 + ArenaApiFeed<(int, int)>.GraceTicks, B);
        Assert.True(f.View.InBout(A));   // exactly the grace: still in
        Step(f, 100 + ArenaApiFeed<(int, int)>.GraceTicks + 1, B);
        Assert.False(f.View.InBout(A));
        Assert.True(f.View.InBout(B));
    }

    [Fact]
    public void SeenAgainWithinTheGraceRestartsIt()
    {
        var f = Feed(100, A);
        Step(f, 500);
        Step(f, 600, A);
        Step(f, 600 + ArenaApiFeed<(int, int)>.GraceTicks);
        Assert.True(f.View.InBout(A));
    }

    [Fact]
    public void AnUnchangedTickKeepsTheSameView()
    {
        var f = Feed(10, A);
        var first = f.View;
        Step(f, 11, A);
        Step(f, 12, A);
        Assert.Same(first, f.View);
        Step(f, 13, A, B);
        Assert.NotSame(first, f.View);
        Assert.False(first.InBout(B));   // a published view never changes
    }

    [Fact]
    public void TheRosterAnswersByIdAndByNameIgnoringCaseAndSpaces()
    {
        var f = new ArenaApiFeed<(int, int)>();
        f.Begin(1);
        f.SetRoster([(A, "Hagar the Wall"), (B, " Ruka ")]);
        f.End();
        Assert.True(f.View.IsTownFighterId(A));
        Assert.False(f.View.IsTownFighterId(AReused));
        Assert.True(f.View.IsTownFighter("hagar the wall"));
        Assert.True(f.View.IsTownFighter("Ruka"));
        Assert.True(f.View.IsTownFighter("  RUKA"));
        Assert.False(f.View.IsTownFighter("Hagar"));
        Assert.False(f.View.IsTownFighter(""));
        Assert.False(f.View.IsTownFighter(null));
        Assert.Equal(2, f.View.TownFighterCount);
    }

    [Fact]
    public void TheSameRosterAgainPublishesNothingButARenameDoes()
    {
        var f = new ArenaApiFeed<(int, int)>();
        f.Begin(1);
        f.SetRoster([(A, "Hagar")]);
        f.End();
        var first = f.View;
        f.Begin(2);
        f.SetRoster([(A, "Hagar")]);
        f.End();
        Assert.Same(first, f.View);
        f.Begin(3);
        f.SetRoster([(A, "HAGAR")]);   // the case changed: the listing shows the game's spelling
        f.End();
        Assert.NotSame(first, f.View);
        f.Begin(4);
        f.SetRoster([(A, "Ruka")]);
        f.End();
        Assert.False(f.View.IsTownFighter("Hagar"));
        Assert.True(f.View.IsTownFighter("ruka"));
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        var f = Feed(10, A);
        f.Clear();
        Assert.False(f.View.Ready);
        Assert.False(f.View.InBout(A));
        Assert.Equal(0, f.Count);
    }

    [Theory]
    [InlineData("219-Proving Grounds.mod", true)]    // a Polearm fighter of the catalog
    [InlineData("679-Proving Grounds.mod", true)]    // a Hacker
    [InlineData("167-Proving Grounds.mod", true)]    // Skarn
    [InlineData("623-Proving Grounds.mod", true)]    // Veyr the Gilded
    [InlineData("100-Proving Grounds.mod", false)]   // a Scratch medic
    [InlineData("111-Proving Grounds.mod", false)]   // the bookie
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TownFighterTemplates(string? record, bool fighter) => Assert.Equal(fighter, ArenaApiRules.IsTownFighterRecord(record));
}
