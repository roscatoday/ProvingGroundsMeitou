using System.Text;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.ArenaPersistence;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The in-save arena data (task 2.4): the version byte framing, the size cap, the bad-data decision, the recovered-file name, the
/// fresh world and the capture for a save, all through the real Core codec.</summary>
public class ArenaSaveDataTests
{
    static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    static Snapshot Rich()
    {
        var s = new Snapshot { SaveKey = ArenaSaveData.InSaveKey, Generation = "6f1c2c1e-0d6e-4a6c-9b0a-2d7f3c9e1a55", BookieCredit = 40, ChallengeCredit = 1500 };
        s.Fighters.PlayerStandings.Add(new LeaderboardData.Standing { Id = "0a1b2c3d-greenlander", Name = "Beta", Mmr = 150.0f, Wins = 3, Losses = 2, Matches = 5 });
        s.Fighters.TownStandings.Add(new LeaderboardData.Standing { Id = "t-1", Name = "Town \"Q\"", Mmr = 99.75f });
        var p = new LeaderboardData.Progression { Id = "0a1b2c3d-greenlander", Name = "Beta", Marks = 250 };
        p.RewardTiers.Add(new RewardProgression.TierEntry(ArenaRewards.StableId(ArenaRewards.Piece.Katana), 1));
        s.Fighters.Progression.Add(p);
        s.Fighters.FactionUnlocks.Add(ArenaRewards.GetWeaponLicence().StableId);
        s.Challenges.Seed = 77; s.Challenges.Division = 1;
        s.PlannedNpc.Active = true; s.PlannedNpc.RegistryId = "reg-1"; s.PlannedNpc.StartHours = 41.5; s.PlannedNpc.ScoreA = 210.0; s.PlannedNpc.ScoreB = 190.0; s.PlannedNpc.TeamA.Add("na-1"); s.PlannedNpc.TeamB.Add("nb-1");
        return s;
    }

    static Snapshot Empty() => new Snapshot { SaveKey = ArenaSaveData.InSaveKey, Generation = "g" };

    static byte[] Json(Snapshot s)
    {
        byte[] json = Array.Empty<byte>();
        Assert.True(EncodeSnapshot(s, ref json, out string error), error);
        return json;
    }

    static byte[] Written(Snapshot s, params string[] tokens)
    {
        Assert.True(ArenaSaveData.TryWrite(s, tokens, out byte[] payload, out string error), error);
        Assert.Equal("", error);
        return payload;
    }

    [Fact]
    public void RoundTripThroughTheRealCodec()
    {
        Snapshot s = Rich();
        byte[] payload = Written(s, "0a1b2c3d-greenlander", "ffff0000-hiver");
        ArenaSaveData.ReadResult r = ArenaSaveData.Read(payload);
        Assert.Equal(ArenaSaveData.ReadKind.Loaded, r.Kind);
        Assert.Equal("", r.Reason);
        Assert.Equal(new[] { "0a1b2c3d-greenlander", "ffff0000-hiver" }, r.Tokens);
        // what came back encodes to the same bytes as what went in
        Assert.Equal(Json(s), Json(r.Snapshot));
        Assert.Equal(150.0f, r.Snapshot.Fighters.PlayerStandings[0].Mmr);
        Assert.Equal(250, r.Snapshot.Fighters.Progression[0].Marks);
        Assert.Equal("reg-1", r.Snapshot.PlannedNpc.RegistryId);
    }

    [Fact]
    public void LayoutIsVersionByteThenTheCodecJsonByteForByteThenNulThenTheIdentityList()
    {
        Snapshot s = Rich();
        byte[] payload = Written(s, "b", "a");
        byte[] json = Json(s);
        Assert.Equal(ArenaSaveData.FormatVersion, payload[0]);
        Assert.Equal((byte)1, payload[0]);
        Assert.Equal(json, payload.AsSpan(1, json.Length).ToArray());
        Assert.Equal((byte)0, payload[1 + json.Length]);
        Assert.Equal("{\"fighterIdentities\":[\"a\",\"b\"]}\n", Encoding.UTF8.GetString(payload, 2 + json.Length, payload.Length - 2 - json.Length));
    }

    [Fact]
    public void EmptyWorldRoundTrips()
    {
        byte[] payload = Written(Empty());
        var r = ArenaSaveData.Read(payload);
        Assert.Equal(ArenaSaveData.ReadKind.Loaded, r.Kind);
        Assert.Empty(r.Tokens);
        Assert.Empty(r.Snapshot.Fighters.PlayerStandings);
    }

    [Fact]
    public void WriteRefusesAnInvalidSnapshotWithTheCodecMessage()
    {
        Snapshot s = Rich();
        s.Fighters.PlayerStandings.Add(s.Fighters.PlayerStandings[0].Clone());   // duplicate id
        Assert.False(ArenaSaveData.TryWrite(s, Array.Empty<string>(), out byte[] payload, out string error));
        Assert.Empty(payload);
        Assert.StartsWith("$.playerStandings", error);
    }

    [Fact]
    public void TheCodecNeedsANonEmptySaveKeyAndGeneration()
    {
        // ArenaSnapshotJson.cpp:365 reads both with nonempty=true, so a "" save key cannot be written (the reason InSaveKey exists)
        Assert.False(ArenaSaveData.TryWrite(new Snapshot { Generation = "g" }, [], out _, out string e1));
        Assert.Equal("$.saveKey: expected a nonempty identifier", e1);
        Assert.False(ArenaSaveData.TryWrite(new Snapshot { SaveKey = "k" }, [], out _, out string e2));
        Assert.Equal("$.generation: expected a nonempty identifier", e2);
    }

    [Fact]
    public void WriteSortsAndDropsRepeatedOrEmptyTokens()
    {
        byte[] payload = Written(Empty(), "b", "a", "b", "");
        Assert.Equal(new[] { "a", "b" }, ArenaSaveData.Read(payload).Tokens);
    }

    static void Unreadable(byte[] payload, string reasonPart)
    {
        var r = ArenaSaveData.Read(payload);
        Assert.Equal(ArenaSaveData.ReadKind.Unreadable, r.Kind);
        Assert.Contains(reasonPart, r.Reason);
        Assert.Empty(r.Tokens);
        Assert.Empty(r.Snapshot.Fighters.PlayerStandings);   // a fresh snapshot, never half of the bad one
    }

    [Fact]
    public void EmptyDataIsUnreadable() => Unreadable(Array.Empty<byte>(), "empty");

    [Fact]
    public void WrongVersionByteIsUnreadable()
    {
        byte[] payload = Written(Rich());
        payload[0] = 2;
        Unreadable(payload, "format version 2");
        payload[0] = 0;
        Unreadable(payload, "format version 0");
    }

    [Fact]
    public void TruncatedInsideTheJsonIsUnreadableWithTheCodecsReason()
    {
        byte[] payload = Written(Rich(), "a");
        byte[] cut = payload.AsSpan(0, 40).ToArray();
        Unreadable(cut, "no fighter identity list");   // no NUL: the frame is cut before the JSON ends
        // with the separator kept but the JSON cut: the codec's own message
        byte[] json = Json(Rich());
        byte[] framed = new byte[] { 1 }.Concat(json.AsSpan(0, 40).ToArray()).Concat(new byte[] { 0 }).Concat(B("{\"fighterIdentities\":[]}\n")).ToArray();
        var r = ArenaSaveData.Read(framed);
        Assert.Equal(ArenaSaveData.ReadKind.Unreadable, r.Kind);
        Assert.StartsWith("$", r.Reason);
        Assert.Contains(": ", r.Reason);
    }

    [Fact]
    public void TruncatedInsideTheIdentityListIsUnreadable()
    {
        byte[] payload = Written(Rich(), "a", "b");
        Unreadable(payload.AsSpan(0, payload.Length - 6).ToArray(), "fighter identity list");
        Unreadable(payload.AsSpan(0, payload.Length - (payload.Length - Array.IndexOf(payload, (byte)0)) + 1).ToArray(), "fighter identity list");
    }

    [Fact]
    public void GarbageIsUnreadable()
    {
        Unreadable(B("hello"), "format version 104");
        Unreadable(new byte[] { 1, 0xff, 0xfe, 0x00, 0x7b }, "$");
        byte[] json = Json(Rich());
        byte[] withBadList(string list) => new byte[] { 1 }.Concat(json).Concat(new byte[] { 0 }).Concat(B(list)).ToArray();
        Unreadable(withBadList("[\"a\"]"), "fighter identity list");
        Unreadable(withBadList("{\"fighterIdentities\":[1]}"), "fighter identity list");
        Unreadable(withBadList("{\"fighterIdentities\":[\"a\"],\"more\":1}"), "fighter identity list");
        Unreadable(withBadList("{}"), "fighter identity list");
        Unreadable(withBadList("{\"fighterIdentities\":[\"a\",\"a\"]}"), "fighter identity list: repeated entry a");
        Unreadable(withBadList("{\"fighterIdentities\":[\"a\",null]}"), "fighter identity list: an entry is not a non-empty string");
        Unreadable(withBadList("{\"fighterIdentities\":[\"\"]}"), "fighter identity list: an entry is not a non-empty string");
        Unreadable(withBadList("{\"fighterIdentities\":[],\"fighterIdentities\":[]}"), "fighter identity list: repeated fighterIdentities");
        Unreadable(new byte[] { 1 }.Concat(json).Concat(new byte[] { 0, (byte)'{', (byte)'"', 0xff, (byte)'"', (byte)':', (byte)'1', (byte)'}' }).ToArray(), "fighter identity list");
        Unreadable(withBadList("{\"fighterIdentities\":[\"a\"]}\u0000x"), "fighter identity list");
    }

    [Fact]
    public void VersionsFollowTheCodec()
    {
        byte[] framed(string jsonText) => new byte[] { 1 }.Concat(B(jsonText)).Concat(new byte[] { 0 }).Concat(B("{\"fighterIdentities\":[]}")).ToArray();
        var legacy = ArenaSaveData.Read(framed("{\"version\":10}"));
        Assert.Equal(ArenaSaveData.ReadKind.LegacyReset, legacy.Kind);
        Assert.Equal("$.version: legacy arena progression must reset; preserve the original sidecar", legacy.Reason);
        Assert.Empty(legacy.Snapshot.Fighters.PlayerStandings);
        Unreadable(framed("{\"version\":17}"), "$.version: snapshot schema is newer than supported version 16");
    }

    [Fact]
    public void OversizedDataIsUnreadableWithoutDecoding()
    {
        byte[] payload = Written(Rich());
        var r = ArenaSaveData.Read(payload, maxBytes: payload.Length - 1);
        Assert.Equal(ArenaSaveData.ReadKind.Unreadable, r.Kind);
        Assert.Contains("larger than", r.Reason);
        Assert.Equal(ArenaSaveData.ReadKind.Loaded, ArenaSaveData.Read(payload, maxBytes: payload.Length).Kind);
    }

    [Fact]
    public void AJsonOverTheCodecLimitGetsTheCodecsMessage()
    {
        var big = new byte[MaxSnapshotBytes + 3];
        big[0] = 1;
        for (int i = 1; i < big.Length - 1; i++) big[i] = (byte)' ';
        big[^1] = 0;
        var r = ArenaSaveData.Read(big);
        Assert.Equal(ArenaSaveData.ReadKind.Unreadable, r.Kind);
        Assert.Equal("$: JSON exceeds the 16 MiB safety limit", r.Reason);
    }

    /// <summary>A stream that hands out at most 7 bytes per Read and counts what it gave.</summary>
    sealed class Trickle(byte[] data) : MemoryStream(data)
    {
        public int Given;
        public override int Read(byte[] buffer, int offset, int count) { int n = base.Read(buffer, offset, Math.Min(count, 7)); Given += n; return n; }
        public override int Read(Span<byte> buffer) { int n = base.Read(buffer[..Math.Min(buffer.Length, 7)]); Given += n; return n; }
    }

    [Fact]
    public void ReadBoundedReadsEverythingUpToTheLimitPlusOne()
    {
        var data = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        Assert.Equal(data, ArenaSaveData.ReadBounded(new Trickle(data), 100));
        Assert.Equal(data, ArenaSaveData.ReadBounded(new Trickle(data), 1000));
        var t = new Trickle(data);
        byte[] capped = ArenaSaveData.ReadBounded(t, 10);
        Assert.Equal(11, capped.Length);
        Assert.Equal(data.Take(11), capped);
        Assert.True(t.Given <= 14);   // never reads the whole stream past the cap
        Assert.Empty(ArenaSaveData.ReadBounded(new MemoryStream(), 10));
    }

    [Fact]
    public void ReadOfTheCappedStreamIsUnreadable()
    {
        byte[] payload = Written(Rich());
        byte[] read = ArenaSaveData.ReadBounded(new MemoryStream(payload), payload.Length - 5);
        Assert.Equal(ArenaSaveData.ReadKind.Unreadable, ArenaSaveData.Read(read, maxBytes: payload.Length - 5).Kind);
    }

    [Fact]
    public void MaxPayloadHoldsTheLargestSnapshotAndList()
    {
        Assert.Equal(1 + MaxSnapshotBytes + 1 + ArenaSaveData.MaxIdentityListBytes, ArenaSaveData.MaxPayloadBytes);
    }

    [Fact]
    public void RecoveredFileName()
    {
        var at = new DateTime(2026, 11, 12, 15, 4, 5, DateTimeKind.Utc);
        Assert.Equal("20261112-150405.dat", ArenaSaveData.RecoveredFileName(at, _ => false));
        var taken = new HashSet<string> { "20261112-150405.dat", "20261112-150405-2.dat" };
        Assert.Equal("20261112-150405-3.dat", ArenaSaveData.RecoveredFileName(at, taken.Contains));
        // a local time is turned into UTC
        var local = new DateTime(2026, 11, 12, 15, 4, 5, DateTimeKind.Local);
        Assert.Equal(local.ToUniversalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".dat",
            ArenaSaveData.RecoveredFileName(local, _ => false));
    }

    [Fact]
    public void FreshSnapshotSeedsTheCardLikeTheNewGameHook()
    {
        Snapshot fresh = ArenaSaveData.FreshSnapshot(1234u);
        Assert.Equal(1235u, fresh.Challenges.Seed);   // GetTickCount() | 1u
        Assert.Equal(16, fresh.Version);
        Assert.Empty(fresh.Fighters.PlayerStandings);
        Assert.Equal(5u, ArenaSaveData.FreshSnapshot(5u).Challenges.Seed);
    }

    [Fact]
    public void CaptureCopiesTheLedgerCancelsBookingsAndStampsTheSave()
    {
        var ledger = new ArenaLedger.Ledger();
        Snapshot live = Rich();
        live.Challenges.Offers[1].State = TownArenaPolicy.OfferState.Booked;
        live.Challenges.Offers[2].State = TownArenaPolicy.OfferState.Consumed;
        live.Challenges.SkarnOffer.State = TownArenaPolicy.OfferState.Booked;
        ledger.Activate(live);
        Snapshot? taken = ArenaSaveData.Capture(ledger, "gen-1");
        Assert.NotNull(taken);
        Assert.Equal("meitou", taken!.SaveKey);
        Assert.Equal("gen-1", taken.Generation);
        Assert.Equal(TownArenaPolicy.OfferState.Available, taken.Challenges.Offers[1].State);
        Assert.Equal(TownArenaPolicy.OfferState.Consumed, taken.Challenges.Offers[2].State);
        Assert.Equal(TownArenaPolicy.OfferState.Available, taken.Challenges.SkarnOffer.State);
        // the live ledger keeps its booking and is not shared with the copy
        Assert.Equal(TownArenaPolicy.OfferState.Booked, ledger.State().Challenges.Offers[1].State);
        taken.Fighters.PlayerStandings[0].Mmr = 1;
        Assert.Equal(150.0f, ledger.State().Fighters.PlayerStandings[0].Mmr);
    }

    [Fact]
    public void CaptureRefusesWhileAPurchaseIsReserved()
    {
        var ledger = new ArenaLedger.Ledger();
        ledger.Activate(Rich());
        var token = new ArenaLedger.Reservation();
        Assert.True(ledger.Reserve("0a1b2c3d-greenlander", "Beta", new ArenaLedger.Purchase(10), ref token));
        Assert.Null(ArenaSaveData.Capture(ledger, "g"));
        Assert.Null(ArenaSaveData.Capture(new ArenaLedger.Ledger(), "g"));   // not ready
    }
}
