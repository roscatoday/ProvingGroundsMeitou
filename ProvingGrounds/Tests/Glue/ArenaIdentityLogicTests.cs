using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>ArenaIdentity.cpp's predicates and ArenaIngress.cpp:175-233's site search (Core/Glue/ArenaIdentityLogic.cs) over plain building
/// records (E3): every record id of the original, the function-record match, "finished", the 25,000 radius, horizontal nearest, first of a tie,
/// the 512 cap, the origin rule and the C++ log line.</summary>
public class ArenaIdentityLogicTests
{
    static ArenaBuilding B(string record, bool finished = true, float x = 0, float y = 0, float z = 0, string? func = null, string key = "k",
        ArenaBuildingSource source = ArenaBuildingSource.Player) => new(source, key, record, func, finished, new Vector3(x, y, z), 0f);

    const string M = "-Proving Grounds.mod";

    [Theory]
    [InlineData("45" + M, true)]
    [InlineData("47" + M, true)]       // the function record, as the building's own data (IsRegistryData)
    [InlineData("195" + M, true)]      // the town registry is a registry (IsTownRegistry first, :109)
    [InlineData("42" + M, false)]
    [InlineData("53" + M, false)]
    [InlineData("", false)]
    public void IsRegistry(string record, bool expected) => Assert.Equal(expected, ArenaIdentityLogic.IsRegistry(B(record)));

    [Fact]
    public void RegistryByItsFunctionRecord() =>
        Assert.True(ArenaIdentityLogic.IsRegistry(B("999-x.mod", func: PgRecords.RegistryFunc)));

    [Fact]
    public void TownRegistryIsMatchedByTheBuildingRecordOnly()
    {
        Assert.True(ArenaIdentityLogic.IsTownRegistry(B(PgRecords.TownRegistry)));
        Assert.False(ArenaIdentityLogic.IsTownRegistry(B(PgRecords.Registry)));
        Assert.False(ArenaIdentityLogic.IsTownRegistry(B("999-x.mod", func: PgRecords.TownRegistry)));   // GetStringId(obj) only
    }

    [Fact]
    public void NullIsNothing()
    {
        ArenaBuilding? none = null;
        var kind = LeaderboardData.Kind.Town;
        Assert.False(ArenaIdentityLogic.IsRegistry(none));
        Assert.False(ArenaIdentityLogic.IsTownRegistry(none));
        Assert.False(ArenaIdentityLogic.IsArena(none));
        Assert.False(ArenaIdentityLogic.IsSmallArena(none));
        Assert.False(ArenaIdentityLogic.IsBanner(none));
        Assert.False(ArenaIdentityLogic.IsMatchUiOpener(none));
        Assert.False(ArenaIdentityLogic.IsLeaderboard(none));
        Assert.False(ArenaIdentityLogic.GetLeaderboardKind(none, ref kind));
        Assert.Equal(LeaderboardData.Kind.Town, kind);   // untouched on failure
        Assert.False(ArenaIdentityLogic.IsBuildingFinished(none));
        Assert.Equal("", ArenaIdentityLogic.GetStringId(none));
        Assert.False(ArenaIdentityLogic.MatchesId(none, PgRecords.Registry));
        Assert.Null(ArenaIdentityLogic.InteractIdentityText(none, "RMB"));
    }

    [Theory]
    [InlineData("36" + M, true)]       // the old arena, accepted
    [InlineData("183" + M, true)]
    [InlineData("334" + M, false)]     // the small arena is not an Arena
    public void IsArena(string record, bool expected) => Assert.Equal(expected, ArenaIdentityLogic.IsArena(B(record)));

    [Fact]
    public void IsArenaIgnoresTheFunctionRecord() => Assert.False(ArenaIdentityLogic.IsArena(B("1-x.mod", func: PgRecords.Arena)));

    [Fact]
    public void SmallArena()
    {
        Assert.True(ArenaIdentityLogic.IsSmallArena(B(PgRecords.SmallArena)));
        Assert.False(ArenaIdentityLogic.IsSmallArena(B(PgRecords.Arena)));
    }

    [Theory]
    [InlineData("42" + M, null, true)]
    [InlineData("60" + M, null, true)]
    [InlineData("9-x.mod", "60" + M, true)]
    [InlineData("45" + M, null, false)]
    public void IsBanner(string record, string? func, bool expected) => Assert.Equal(expected, ArenaIdentityLogic.IsBanner(B(record, func: func)));

    [Fact]
    public void MatchUiOpenerIsRegistryOrBanner()
    {
        Assert.True(ArenaIdentityLogic.IsMatchUiOpener(B(PgRecords.Registry)));
        Assert.True(ArenaIdentityLogic.IsMatchUiOpener(B(PgRecords.TownRegistry)));
        Assert.True(ArenaIdentityLogic.IsMatchUiOpener(B(PgRecords.Banner)));
        Assert.False(ArenaIdentityLogic.IsMatchUiOpener(B(PgRecords.Leaderboard)));
        Assert.False(ArenaIdentityLogic.IsMatchUiOpener(B(PgRecords.Arena)));
    }

    [Theory]
    [InlineData("53" + M, null, LeaderboardData.Kind.Player)]
    [InlineData("57" + M, null, LeaderboardData.Kind.Player)]
    [InlineData("272" + M, null, LeaderboardData.Kind.Town)]
    [InlineData("273" + M, null, LeaderboardData.Kind.Town)]
    [InlineData("8-x.mod", "273" + M, LeaderboardData.Kind.Town)]
    [InlineData("8-x.mod", "57" + M, LeaderboardData.Kind.Player)]
    public void LeaderboardKinds(string record, string? func, LeaderboardData.Kind expected)
    {
        var kind = expected == LeaderboardData.Kind.Player ? LeaderboardData.Kind.Town : LeaderboardData.Kind.Player;
        Assert.True(ArenaIdentityLogic.GetLeaderboardKind(B(record, func: func), ref kind));
        Assert.Equal(expected, kind);
        Assert.True(ArenaIdentityLogic.IsLeaderboard(B(record, func: func)));
    }

    [Fact]
    public void BuildingRecordWinsOverTheFunctionRecord()
    {
        // C++ :167-178: the building's own data first; a (theoretical) town board with a player function stays Town
        var kind = LeaderboardData.Kind.Player;
        Assert.True(ArenaIdentityLogic.GetLeaderboardKind(B(PgRecords.TownLeaderboard, func: PgRecords.LeaderboardFunc), ref kind));
        Assert.Equal(LeaderboardData.Kind.Town, kind);
    }

    [Fact]
    public void NotALeaderboard()
    {
        var kind = LeaderboardData.Kind.Town;
        Assert.False(ArenaIdentityLogic.GetLeaderboardKind(B(PgRecords.Registry), ref kind));
        Assert.Equal(LeaderboardData.Kind.Town, kind);
    }

    [Fact]
    public void FinishedTwins()
    {
        Assert.True(ArenaIdentityLogic.IsRegistryFinished(B(PgRecords.Registry)));
        Assert.False(ArenaIdentityLogic.IsRegistryFinished(B(PgRecords.Registry, finished: false)));
        Assert.False(ArenaIdentityLogic.IsRegistryFinished(B(PgRecords.Banner)));
        Assert.True(ArenaIdentityLogic.IsMatchUiOpenerFinished(B(PgRecords.Banner)));
        Assert.False(ArenaIdentityLogic.IsMatchUiOpenerFinished(B(PgRecords.Banner, finished: false)));
        Assert.True(ArenaIdentityLogic.IsLeaderboardFinished(B(PgRecords.TownLeaderboard)));
        Assert.False(ArenaIdentityLogic.IsLeaderboardFinished(B(PgRecords.Leaderboard, finished: false)));
        var kind = LeaderboardData.Kind.Player;
        Assert.False(ArenaIdentityLogic.GetFinishedLeaderboardKind(B(PgRecords.TownLeaderboard, finished: false), ref kind));
        Assert.Equal(LeaderboardData.Kind.Town, kind);   // C++ :215-216: the kind is written before the finished test
        Assert.True(ArenaIdentityLogic.IsBuildingFinished(B("anything")));
    }

    [Theory]
    [InlineData("45" + M, ArenaBuildingKind.Registry)]
    [InlineData("195" + M, ArenaBuildingKind.TownRegistry)]
    [InlineData("42" + M, ArenaBuildingKind.Banner)]
    [InlineData("53" + M, ArenaBuildingKind.Leaderboard)]
    [InlineData("272" + M, ArenaBuildingKind.TownLeaderboard)]
    [InlineData("183" + M, ArenaBuildingKind.Arena)]
    [InlineData("36" + M, ArenaBuildingKind.Arena)]
    [InlineData("334" + M, ArenaBuildingKind.SmallArena)]
    [InlineData("1783-gamedata.base", ArenaBuildingKind.None)]
    public void KindOf(string record, ArenaBuildingKind kind) => Assert.Equal(kind, ArenaIdentityLogic.KindOf(B(record)));

    [Fact]
    public void InteractIdentityText()
    {
        Assert.Equal("Proving Grounds: RMB stringID=45-Proving Grounds.mod func=(none) [registry]",
            ArenaIdentityLogic.InteractIdentityText(B(PgRecords.Registry), "RMB"));
        Assert.Equal("Proving Grounds: interact stringID=(none) func=57-Proving Grounds.mod [leaderboard]",
            ArenaIdentityLogic.InteractIdentityText(B("", func: PgRecords.LeaderboardFunc), null));
        Assert.Equal("Proving Grounds: x stringID=42-Proving Grounds.mod func=(none) [banner]",
            ArenaIdentityLogic.InteractIdentityText(B(PgRecords.Banner), "x"));
    }

    // ---------- the site search (ArenaIngress.cpp:175-233) ----------

    [Fact]
    public void NearestByHorizontalDistance()
    {
        // b is nearer in 3D (height differs at a), a nearer in x/z
        var a = B(PgRecords.Arena, x: 100, y: 5000, key: "a");
        var b = B(PgRecords.Arena, x: 200, key: "b");
        var hit = ArenaIdentityLogic.FindSite(ArenaLocationMode.Arena, Vector3.Zero, [b, a], out _);
        Assert.Equal("a", hit?.Key);
    }

    [Fact]
    public void TieGoesToTheFirstHit()
    {
        var a = B(PgRecords.Arena, x: 100, key: "a");
        var b = B(PgRecords.Arena, x: -100, key: "b");
        Assert.Equal("a", ArenaIdentityLogic.FindNearestMatching(Vector3.Zero, [a, b], ArenaIdentityLogic.IsArena, out _)?.Key);
        Assert.Equal("b", ArenaIdentityLogic.FindNearestMatching(Vector3.Zero, [b, a], ArenaIdentityLogic.IsArena, out _)?.Key);
    }

    [Fact]
    public void UnfinishedSitesCountAsTheCDoes()
    {
        // ArenaIngress.cpp:229-232 match with IsArena / IsBanner / IsSmallArena: no finished test
        var a = B(PgRecords.Arena, finished: false, x: 10, key: "unfinished");
        Assert.Equal("unfinished", ArenaIdentityLogic.FindSite(ArenaLocationMode.Arena, Vector3.Zero, [a], out _)?.Key);
    }

    [Fact]
    public void ModesPickTheirKind()
    {
        var arena = B(PgRecords.Arena, x: 50, key: "arena");
        var small = B(PgRecords.SmallArena, x: 60, key: "small");
        var banner = B(PgRecords.Banner, x: 70, key: "banner");
        var reg = B(PgRecords.Registry, x: 1, key: "reg");
        IReadOnlyList<ArenaBuilding> all = [reg, arena, small, banner];
        Assert.Equal("arena", ArenaIdentityLogic.FindSite(ArenaLocationMode.Arena, Vector3.Zero, all, out _)?.Key);
        Assert.Equal("small", ArenaIdentityLogic.FindSite(ArenaLocationMode.SmallArena, Vector3.Zero, all, out _)?.Key);
        Assert.Equal("banner", ArenaIdentityLogic.FindSite(ArenaLocationMode.Banner, Vector3.Zero, all, out _)?.Key);
        Assert.Null(ArenaIdentityLogic.FindSite(ArenaLocationMode.Arena, Vector3.Zero, [reg, small], out _));
    }

    [Fact]
    public void TheSphereIs25000In3D()
    {
        var inside = B(PgRecords.Arena, x: 24999, key: "in");
        var edge = B(PgRecords.Arena, x: 25000, key: "edge");
        var highUp = B(PgRecords.Arena, x: 20000, y: 16000, key: "high");   // 25,612 away in 3D, 20,000 in x/z
        var hits = ArenaIdentityLogic.SphereHits([highUp, edge, inside], Vector3.Zero);
        Assert.Equal(["in", "edge"], hits.Select(h => h.Key));
        Assert.Null(ArenaIdentityLogic.FindSite(ArenaLocationMode.Arena, Vector3.Zero, [highUp], out _));
    }

    [Fact]
    public void CapOf512KeepsTheNearest()
    {
        var list = new List<ArenaBuilding>();
        for (int i = 0; i < 600; i++) list.Add(B("1-filler.mod", x: 100 + i, key: "f" + i));
        list.Add(B(PgRecords.Arena, x: 10, key: "near-arena"));
        list.Add(B(PgRecords.Arena, x: 20000, key: "far-arena"));   // past the 512 nearest
        var hits = ArenaIdentityLogic.SphereHits(list, Vector3.Zero);
        Assert.Equal(512, hits.Count);
        Assert.Equal("near-arena", hits[0].Key);
        Assert.DoesNotContain(hits, h => h.Key == "far-arena");
        Assert.Equal("near-arena", ArenaIdentityLogic.FindSite(ArenaLocationMode.Arena, Vector3.Zero, list, out string line)?.Key);
        Assert.Equal("Proving Grounds: site search hits=512 matched=1 bestDistXZ=10.0 (radius=25000)", line);
    }

    [Fact]
    public void LogLineWithNoMatch()
    {
        ArenaIdentityLogic.FindSite(ArenaLocationMode.Banner, new Vector3(1, 2, 3), [B(PgRecords.Arena, x: 3)], out string line);
        Assert.Equal("Proving Grounds: site search hits=1 matched=0 bestDistXZ=-1.0 (radius=25000)", line);
    }

    [Fact]
    public void LogLineDistanceIsFloatSqrt()
    {
        ArenaIdentityLogic.FindSite(ArenaLocationMode.Arena, Vector3.Zero, [B(PgRecords.Arena, x: 3, z: 4), B(PgRecords.Arena, x: 30, z: 40)], out string line);
        Assert.Equal("Proving Grounds: site search hits=2 matched=2 bestDistXZ=5.0 (radius=25000)", line);
    }

    [Fact]
    public void OriginRule()
    {
        var reg = new Vector3(1, 2, 3);
        var fighter = new Vector3(7, 8, 9);
        Assert.Equal(reg, ArenaIdentityLogic.ResolveSearchOrigin(reg, fighter));
        Assert.Equal(fighter, ArenaIdentityLogic.ResolveSearchOrigin(null, fighter));
        Assert.Equal(Vector3.Zero, ArenaIdentityLogic.ResolveSearchOrigin(null, null));
    }

    [Fact]
    public void RefIsIdentityOnly()
    {
        var before = B(PgRecords.Registry, finished: false, x: 1, key: "1-zone10.35-INGAME");
        var after = before with { Finished = true, Position = new Vector3(9, 9, 9), Yaw = 1.5f, FunctionRecordId = PgRecords.RegistryFunc };
        Assert.NotEqual(before, after);   // a snapshot
        Assert.Equal(before.Ref, after.Ref);
        Assert.Equal(new ArenaBuildingRef(ArenaBuildingSource.Player, "1-zone10.35-INGAME", PgRecords.Registry), after.Ref);
    }

    [Fact]
    public void AcceptOnlyTheSameRecordUnderTheKey()
    {
        var held = B(PgRecords.Registry, key: "1-zone10.35-INGAME").Ref;
        var same = B(PgRecords.Registry, finished: false, x: 5, key: "1-zone10.35-INGAME");
        Assert.Equal(same, ArenaIdentityLogic.Accept(held, same));
        // the id handed out again to another building after a dismantle / reload: gone
        Assert.Null(ArenaIdentityLogic.Accept(held, B(PgRecords.Leaderboard, key: "1-zone10.35-INGAME")));
        Assert.Null(ArenaIdentityLogic.Accept(held, null));
        Assert.Null(ArenaIdentityLogic.Accept(held, B(PgRecords.Registry, key: "1-zone10.35-INGAME", source: ArenaBuildingSource.Town)));
        Assert.Null(ArenaIdentityLogic.Accept(held, B(PgRecords.Registry, key: "2-zone10.35-INGAME")));
    }

    [Fact]
    public void LocationModeValuesAreTheCs() // ArenaIngress.h:12-17
    {
        Assert.Equal(0, (int)ArenaLocationMode.Arena);
        Assert.Equal(1, (int)ArenaLocationMode.SmallArena);
        Assert.Equal(2, (int)ArenaLocationMode.Banner);
    }

    [Fact]
    public void RingKindOfASite() // ArenaRingGuard.cpp:40-48 RingScaleForSite over the site building
    {
        Assert.Equal(RingSiteKind.Banner, ArenaIdentityLogic.RingKindOf(B(PgRecords.Banner)));
        Assert.Equal(RingSiteKind.SmallArena, ArenaIdentityLogic.RingKindOf(B(PgRecords.SmallArena)));
        Assert.Equal(RingSiteKind.Arena, ArenaIdentityLogic.RingKindOf(B(PgRecords.Arena)));
        Assert.Equal(RingSiteKind.Arena, ArenaIdentityLogic.RingKindOf(B(PgRecords.Registry)));
        Assert.Equal(RingSiteKind.Arena, ArenaIdentityLogic.RingKindOf(B(PgRecords.TownRegistry)));
        Assert.Equal(RingSiteKind.Banner, ArenaIdentityLogic.RingKindOf(B("1-x.mod", func: PgRecords.BannerFunc)));   // the function record too
    }
}
