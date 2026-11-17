using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The fighter identity map (task 2.3, Core/Glue/FighterIdentityMap.cs) with fakes: an int stands for a live character's id
/// (negative = no character), a token for its PortraitSerial + RecordId. Expected values are read off FighterIdentity.cpp.</summary>
public class FighterIdentityMapTests
{
    readonly List<string> log = [];
    FighterIdentityMap<int> Map() => new(k => k < 0, log.Add);
    /// <summary>A scan of the fake table: these (key, token) characters are live.</summary>
    static void Scan(FighterIdentityMap<int> m, long spawns, int count, params (int Key, string? Token)[] cs) =>
        m.Rebuild(cs.Select(c => c.Key), k => cs.First(c => c.Key == k).Token, spawns, count);

    [Fact]
    public void TokenIsSerialAsEightLowercaseHexDigitsDashRecordId()
    {
        Assert.Equal("00abcdef-1234-gamedata.base", FighterIdentityMap<int>.Token(0xABCDEFu, "1234-gamedata.base"));
        Assert.Equal("ffffffff-x", FighterIdentityMap<int>.Token(uint.MaxValue, "x"));
        Assert.Equal("00000001-", FighterIdentityMap<int>.Token(1u, null));
    }

    [Fact]   // LeaderboardStore.cpp:38-45, 254-259: no identity -> the fighter is skipped
    public void NoPortraitSerialMeansNoIdentity()
    {
        Assert.Null(FighterIdentityMap<int>.Token(null, "rec"));
        Assert.Null(FighterIdentityMap<int>.Token(0u, "rec"));
        var m = Map();
        Scan(m, 1, 1, (1, null));
        Assert.False(m.GetOrCreate(1, null, out string id));
        Assert.Equal("", id);
        Assert.False(m.Find(1, null, out id));
        Assert.False(m.IsBlocked(1));
        Assert.False(m.GetOrCreate(-1, "00000001-r", out _));   // no character
        Assert.Empty(m.Registered);
    }

    [Fact]   // FighterIdentity.cpp:192-218 GetOrCreate creates, 220-225 Find only finds
    public void FindAnswersOnlyAfterGetOrCreate()
    {
        var m = Map();
        Scan(m, 1, 1, (1, "00000001-r"));
        Assert.False(m.Find(1, "00000001-r", out string id));
        Assert.Equal("", id);
        Assert.True(m.GetOrCreate(1, "00000001-r", out id));
        Assert.Equal("00000001-r", id);
        Assert.True(m.Find(1, "00000001-r", out id));
        Assert.Equal("00000001-r", id);
        Assert.True(m.GetOrCreate(1, "00000001-r", out id));   // second call: the saved id, nothing new
        Assert.Equal(["00000001-r"], m.Registered);
        Assert.Empty(log);
    }

    [Fact]   // FighterIdentityPolicy.h:47-65 + FighterIdentity.cpp:212-217: same token twice -> both blocked, logged once
    public void SameTokenOnTwoLiveCharactersBlocksBoth()
    {
        var m = Map();
        Scan(m, 3, 3, (1, "0000000a-r"), (2, "0000000a-r"), (3, "0000000b-r"));
        Assert.False(m.GetOrCreate(1, "0000000a-r", out string id));
        Assert.Equal("", id);
        Assert.False(m.Find(2, "0000000a-r", out _));
        Assert.True(m.IsBlocked(1));
        Assert.True(m.IsBlocked(2));
        Assert.True(m.GetOrCreate(3, "0000000b-r", out id));   // the other fighter is not touched
        Assert.False(m.IsBlocked(3));
        Assert.Equal(["Proving Grounds fighter identity: duplicate generated fighter ID 0000000a-r; careers blocked"], log);
    }

    [Fact]   // CAP 9.2: a serial clash with another record id is two fighters
    public void SameSerialOtherRecordIdIsTwoFighters()
    {
        var m = Map();
        string a = FighterIdentityMap<int>.Token(77u, "bandit")!, b = FighterIdentityMap<int>.Token(77u, "hungry bandit")!;
        Scan(m, 2, 2, (1, a), (2, b));
        Assert.True(m.GetOrCreate(1, a, out string ia));
        Assert.True(m.GetOrCreate(2, b, out string ib));
        Assert.NotEqual(ia, ib);
        Assert.Empty(log);
    }

    [Fact]   // FighterIdentityPolicy.h:18-20: sticky within a world, even after the duplicate is gone
    public void BlockIsStickyAfterTheDuplicateLeaves()
    {
        var m = Map();
        Scan(m, 2, 2, (1, "0000000a-r"), (2, "0000000a-r"));
        m.GetOrCreate(1, "0000000a-r", out _);
        Scan(m, 2, 1, (1, "0000000a-r"));   // 2 died and was removed
        Assert.False(m.Find(1, "0000000a-r", out _));
        Assert.False(m.GetOrCreate(1, "0000000a-r", out _));
        Assert.True(m.IsBlocked(1));
        m.AbandonWorldState();                    // C++ Clear: the block goes with the world
        Scan(m, 2, 1, (1, "0000000a-r"));
        Assert.True(m.Find(1, "0000000a-r", out _));
    }

    [Fact]   // FighterIdentity.cpp:125-144 (Load): a later character carrying a registered token conflicts at its load
    public void ACharacterThatAppearsWithARegisteredTokenIsBoundAndADuplicateIsLogged()
    {
        var m = Map();
        Scan(m, 1, 1, (1, "0000000a-r"));
        Assert.True(m.GetOrCreate(1, "0000000a-r", out _));
        Scan(m, 2, 2, (1, "0000000a-r"), (5, "0000000a-r"));
        Assert.False(m.Find(1, "0000000a-r", out _));
        Assert.False(m.Find(5, "0000000a-r", out _));
        Assert.Equal(["Proving Grounds fighter identity: duplicate live fighter ID 0000000a-r; both careers blocked until corrected reload"], log);
    }

    [Fact]   // a new generation of a slot (another key) is a new lifetime, bound again from its token; the old one is ended
    public void ANewLifetimeOfTheSameCharacterKeepsItsIdentity()
    {
        var m = Map();
        Scan(m, 1, 1, (10, "0000000c-r"));
        m.GetOrCreate(10, "0000000c-r", out _);
        Scan(m, 2, 1, (11, "0000000c-r"));   // respawned (stored squad): same CharacterCold, same serial, new CharacterId
        Assert.True(m.Find(11, "0000000c-r", out string id));
        Assert.Equal("0000000c-r", id);
        // (10 is no live character: callers ask only about live ids, src/Game/FighterIdentity.cs checks Exists first)
        Assert.Empty(log);
    }

    [Fact]   // a character spawned after the last scan is bound on first use, no rebuild needed
    public void ACharacterNotYetScannedIsBoundOnFirstUse()
    {
        var m = Map();
        Scan(m, 1, 1, (1, "0000000a-r"));
        m.GetOrCreate(1, "0000000a-r", out _);
        Assert.True(m.Find(1, "0000000a-r", out _));
        Assert.False(m.Find(7, "0000000a-r", out _));   // 7 shares the token: both blocked at once
        Assert.True(m.IsBlocked(1));
        Assert.True(m.GetOrCreate(8, "0000000d-r", out string id));
        Assert.Equal("0000000d-r", id);
    }

    [Fact]   // the registered set is the world's saved state (task 2.4): restored after a load, it binds the live characters again
    public void RestoredTokensBindLikeSavedIds()
    {
        var first = Map();
        Scan(first, 2, 2, (1, "0000000a-r"), (2, "0000000b-r"));
        first.GetOrCreate(1, "0000000a-r", out _);
        var loaded = Map();
        loaded.Restore(first.Registered);
        Scan(loaded, 2, 2, (4, "0000000a-r"), (6, "0000000b-r"));   // new CharacterIds after the load
        Assert.True(loaded.Find(4, "0000000a-r", out string id));
        Assert.Equal("0000000a-r", id);
        Assert.False(loaded.Find(6, "0000000b-r", out _));   // never created: Find has nothing, as in the C++
    }

    [Fact]   // lazy rebuild: only when the live set changed
    public void IsStaleOnlyWhenTheTableChanged()
    {
        var m = Map();
        Assert.True(m.IsStale(0, 0));   // never scanned
        Scan(m, 5, 3);
        Assert.False(m.IsStale(5, 3));
        Assert.True(m.IsStale(6, 3));   // a spawn (a slot freed and used again keeps the count)
        Assert.True(m.IsStale(5, 2));   // a removal
        m.AbandonWorldState();
        Assert.True(m.IsStale(5, 3));
    }

    [Fact]   // Restore after the scan: binds the live holders; two holders = the load-time duplicate line
    public void RestoreAfterTheScanBindsAndLogsALoadDuplicate()
    {
        var m = Map();
        Scan(m, 3, 3, (1, "0000000a-r"), (2, "0000000a-r"), (3, "0000000b-r"));
        m.Restore(["0000000a-r", "0000000b-r"]);
        Assert.False(m.Find(1, "0000000a-r", out _));
        Assert.True(m.IsBlocked(2));
        Assert.True(m.Find(3, "0000000b-r", out string id));
        Assert.Equal("0000000b-r", id);
        Assert.Equal(["Proving Grounds fighter identity: duplicate live fighter ID 0000000a-r; both careers blocked until corrected reload"], log);
    }

    [Fact]
    public void RestoreSkipsEmptyAndAlreadyRegisteredTokens()
    {
        var m = Map();
        Scan(m, 1, 1, (1, "0000000a-r"));
        m.GetOrCreate(1, "0000000a-r", out _);
        m.Restore(["", "0000000a-r", "0000000a-r"]);
        Assert.Equal(["0000000a-r"], m.Registered);
        Assert.True(m.Find(1, "0000000a-r", out _));
        Assert.Empty(log);
    }

    [Fact]   // FighterIdentity.cpp:227-231: bindings go, the created ids stay (they live in the save)
    public void AbandonWorldStateKeepsTheCreatedTokens()
    {
        var m = Map();
        Scan(m, 1, 1, (1, "0000000a-r"));
        m.GetOrCreate(1, "0000000a-r", out _);
        m.AbandonWorldState();
        Assert.Equal(["0000000a-r"], m.Registered);
        Assert.False(m.IsBlocked(1));
    }

    [Fact]
    public void IsBlockedOnAnUnseenKeyIsFalseAndAddsNothing()
    {
        var m = Map();
        Assert.False(m.IsBlocked(42));
        Assert.False(m.IsBlocked(-1));
        Scan(m, 1, 1, (42, "0000000a-r"));
        Assert.False(m.IsBlocked(42));
    }

    [Fact]   // the scan asks the token only of characters it has not seen
    public void RebuildAsksTokensOnlyOfNewKeys()
    {
        var m = Map();
        var asked = new List<int>();
        string Tok(int k) { asked.Add(k); return $"{k:x8}-r"; }
        m.Rebuild([1, 2], Tok, 2, 2);
        m.Rebuild([1, 2, 3], Tok, 3, 3);
        Assert.Equal([1, 2, 3], asked);
    }

    [Fact]   // a key asked about with another token than it had is another fighter: new lifetime, no old binding
    public void ATokenChangeOnALiveKeyStartsANewLifetime()
    {
        var m = Map();
        Scan(m, 1, 1, (1, "0000000a-r"));
        Assert.True(m.GetOrCreate(1, "0000000a-r", out _));
        Assert.False(m.Find(1, "0000000b-r", out _));   // 0000000b-r was never created
        Assert.True(m.GetOrCreate(1, "0000000b-r", out string id));
        Assert.Equal("0000000b-r", id);
        Assert.Empty(log);
    }

    [Fact]
    public void RegisteredSortedIsOrdinal()
    {
        var m = Map();
        Scan(m, 3, 3, (1, "b-r"), (2, "B-r"), (3, "a-r"));
        m.GetOrCreate(1, "b-r", out _); m.GetOrCreate(2, "B-r", out _); m.GetOrCreate(3, "a-r", out _);
        Assert.Equal(["B-r", "a-r", "b-r"], m.RegisteredSorted());
    }

    [Fact]
    public void CanPersistAlways() => Assert.True(Map().CanPersist);
}
