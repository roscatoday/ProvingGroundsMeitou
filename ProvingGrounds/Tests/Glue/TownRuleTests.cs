using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The town membership rules (src/Engine/Towns.cs): <see cref="TownRule.CurrentTown"/> = Meitou's <c>AiSystem.TownAt</c> (the
/// original's getCurrentTownLocation), <see cref="TownRule.MemberOf"/> = Meitou's <c>TownBuildingBuilder.MemberOf</c> (a level building's town).</summary>
public class TownRuleTests
{
    static TownCircle T(float x, float z, float r, TownKind k = TownKind.Placed) => new(x, z, r, k);

    [Fact]
    public void InsideTheRadius() => Assert.Equal(0, TownRule.CurrentTown(10, 10, [T(0, 0, 100)]));

    [Fact]
    public void OnTheRadiusCounts() => Assert.Equal(0, TownRule.CurrentTown(100, 0, [T(0, 0, 100)]));

    [Fact]
    public void OutsideEveryRadius() => Assert.Equal(-1, TownRule.CurrentTown(500, 0, [T(0, 0, 100), T(1000, 0, 100)]));

    [Fact]
    public void NoTowns() => Assert.Equal(-1, TownRule.CurrentTown(0, 0, []));

    [Fact]
    public void NearestCentreWins()
    {
        // both radii hold the point; the nearer centre wins
        Assert.Equal(1, TownRule.CurrentTown(60, 0, [T(0, 0, 1000), T(100, 0, 1000)]));
    }

    [Fact]
    public void OnlyTheNearestTownIsAsked()
    {
        // AiSystem.TownAt: the nearest town overall, then its own radius. The point is inside the far big town's radius but nearer the small
        // one, outside it: no town (MemberOf would say the big one).
        TownCircle[] towns = [T(0, 0, 50), T(300, 0, 1000)];
        Assert.Equal(-1, TownRule.CurrentTown(100, 0, towns));
        Assert.Equal(1, TownRule.MemberOf(100, 0, towns, hasInterior: false));
    }

    [Fact]
    public void HeightIsIgnored() => Assert.Equal(0, TownRule.CurrentTown(30, 40, [T(0, 0, 50)]));

    [Fact]
    public void NestMarkersAndNullTownsAreSkipped()
    {
        TownCircle[] towns = [T(0, 0, 0, TownKind.NestMarker), T(0, 0, 0, TownKind.Null), T(200, 0, 300)];
        Assert.Equal(2, TownRule.CurrentTown(0, 0, towns));
    }

    [Fact]
    public void PlayerTownsCount()
    {
        TownCircle[] towns = [T(0, 0, 100), T(500, 0, 2500, TownKind.Player)];
        Assert.Equal(1, TownRule.CurrentTown(450, 0, towns));
    }

    [Fact]
    public void TiesKeepTheFirst() => Assert.Equal(0, TownRule.CurrentTown(50, 0, [T(0, 0, 100), T(100, 0, 100)]));

    [Fact]
    public void MemberOfSkipsOnlyNestMarkers()
    {
        // a Null town has radius 0: it holds only its own centre
        Assert.Equal(0, TownRule.MemberOf(5, 5, [T(5, 5, 0, TownKind.Null)], hasInterior: false));
        Assert.Equal(-1, TownRule.MemberOf(5, 5, [T(5, 5, 0, TownKind.NestMarker)], hasInterior: false));
    }

    [Fact]
    public void MemberOfInteriorReach()
    {
        TownCircle[] towns = [T(0, 0, 100), T(20000, 0, 100)];
        Assert.Equal(-1, TownRule.MemberOf(9000, 0, towns, hasInterior: false));
        Assert.Equal(0, TownRule.MemberOf(9000, 0, towns, hasInterior: true));     // nearest within 10000
        Assert.Equal(-1, TownRule.MemberOf(-10000, 0, towns, hasInterior: true));  // not strictly within 10000
        Assert.Equal(1, TownRule.MemberOf(12000, 0, towns, hasInterior: true));
    }

    [Fact]
    public void SameTown()
    {
        Assert.True(TownRule.SameTown(3, 3));
        Assert.False(TownRule.SameTown(3, 4));
        Assert.False(TownRule.SameTown(-1, -1));   // "no town" is never the same town
    }

    // ---------- the character's town (task 7.2 review): getCurrentTownLocation = the status town ----------

    const long None = TownKey.None, Scratch = 304;

    /// <summary>A fake AiSystem.StatusTownAt: with a current town, step 2's hold (1.5 x radius) of Scratch at (0, 0) radius 100; with none, entry at
    /// 1 x. The point is (x, 0).</summary>
    static Func<long, long> StepsAt(float x, List<string> asked) => current =>
    {
        asked.Add("statusTownAt(" + (current == None ? "none" : current.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ")");
        var scratch = new TownCircle(0, 0, 100, TownKind.Placed);
        if (current == Scratch) return TownRule.Holds(x, 0, scratch) ? Scratch : None;
        return TownRule.CurrentTown(x, 0, [scratch]) == 0 ? Scratch : None;
    };

    static long Read(long status, float x, long building, ref long memo, List<string> asked) =>
        TownRule.CharacterTown(status, StepsAt(x, asked), () => { asked.Add("building"); return building; }, t => t == None, ref memo, None);

    [Fact]
    public void TheKeptStatusTownWinsDropsTheMemoAndNothingElseIsAsked()
    {
        var asked = new List<string>();
        long memo = 12;
        Assert.Equal(Scratch, Read(Scratch, 140, None, ref memo, asked));
        Assert.Empty(asked);
        Assert.Equal(None, memo);
    }

    [Fact]
    public void NoKeptTownAsksTheStepsThenTheBuilding()
    {
        var asked = new List<string>();
        long memo = None;
        Assert.Equal(Scratch, Read(None, 50, 7, ref memo, asked));   // not updated yet (just after a load): steps 1, 2, 4 enter at 1 x
        Assert.Equal(["statusTownAt(none)"], asked);
        memo = None; asked.Clear();
        Assert.Equal(7, Read(None, 500, 7, ref memo, asked));   // steps 1, 2, 4 find nothing: step 5, the building stood in
        Assert.Equal(["statusTownAt(none)", "building"], asked);
        memo = None; asked.Clear();
        Assert.Equal(None, Read(None, 500, None, ref memo, asked));
        Assert.Equal(None, memo);
    }

    [Fact]
    public void AStepFiveTownIsHeldToOneAndAHalfRadiiThenDropped()
    {
        var asked = new List<string>();
        long memo = None;
        Assert.Equal(Scratch, Read(None, 120, Scratch, ref memo, asked));   // indoors in its Scratch house, beyond 1 x: step 5
        Assert.Equal(Scratch, memo);
        asked.Clear();
        Assert.Equal(Scratch, Read(None, 140, None, ref memo, asked));      // walked out, 1.4 x: step 2 holds the current town
        Assert.Equal(["statusTownAt(304)"], asked);
        Assert.Equal(Scratch, memo);
        asked.Clear();
        Assert.Equal(None, Read(None, 151, None, ref memo, asked));         // past 1.5 x: dropped
        Assert.Equal(["statusTownAt(304)", "building"], asked);
        Assert.Equal(None, memo);
        Assert.Equal(None, Read(None, 140, None, ref memo, asked));         // back to 1.4 x: no current town, so 1 x is needed again
    }

    [Fact]
    public void MeitousStatusTownReplacesTheMemo()
    {
        var asked = new List<string>();
        long memo = Scratch;
        Assert.Equal(12, Read(12, 10, None, ref memo, asked));
        Assert.Equal(None, memo);
    }

    [Fact]
    public void TheMemoStoreKeepsOnePerCharacterAndAWorldChangeClearsIt()
    {
        var store = new StatusTownMemo<int, long>(None);
        store.Set(1, Scratch, false);
        store.Set(2, 7, false);
        Assert.Equal(Scratch, store.Get(1));
        Assert.Equal(None, store.Get(3));
        store.Set(2, None, true);
        Assert.Equal(1, store.Count);
        store.Clear();   // a new world builds a new Towns, so a new store; Clear is the same reset
        Assert.Equal(None, store.Get(1));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void HoldsIsStrictlyInsideOneAndAHalfRadii()
    {
        var t = new TownCircle(0, 0, 100, TownKind.Placed);
        Assert.True(TownRule.Holds(149.9f, 0, t));
        Assert.False(TownRule.Holds(150, 0, t));
    }
}
