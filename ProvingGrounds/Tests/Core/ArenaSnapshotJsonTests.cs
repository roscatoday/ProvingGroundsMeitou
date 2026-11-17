using System.Text;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaPersistence;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/ArenaSnapshot.cs, Core/ArenaSnapshotJson.cs and Core/RapidJson.cs (task 1.7a). Every expected value and message
// is read off ArenaSnapshotJson.cpp / rapidjson v1.1.0; Tests/Golden/SnapshotGoldenTests.cs pins the same code against the real C++.
public class ArenaSnapshotJsonTests
{
    static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    static string S(byte[] b) => Encoding.UTF8.GetString(b);

    static Snapshot Full()
    {
        var s = new Snapshot { SaveKey = "Rich Save", Generation = "gen-rich", BookieCredit = 0, ChallengeCredit = 1500 };
        s.Fighters.PlayerStandings.Add(new LeaderboardData.Standing { Id = "p-b", Name = "Beta", Mmr = 1234.5f, Wins = 3, Losses = 2, Matches = 5 });
        s.Fighters.PlayerStandings.Add(new LeaderboardData.Standing { Id = "p-a", Name = "Alpha", Mmr = 99.75f, Wins = 1, Losses = 0, Matches = 1 });
        s.Fighters.TownStandings.Add(new LeaderboardData.Standing { Id = "t-1", Name = "Town \"Q\"", Mmr = 100.0f });
        var p = new LeaderboardData.Progression { Id = "p-a", Name = "Alpha", Marks = 250 };
        p.RewardTiers.Add(new RewardProgression.TierEntry(ArenaRewards.StableId(ArenaRewards.Piece.Katana), 1));
        p.RewardTiers.Add(new RewardProgression.TierEntry(ArenaRewards.StableId(ArenaRewards.Piece.Cloak), 0));
        s.Fighters.Progression.Add(p);
        s.Fighters.FactionUnlocks.Add(ArenaRewards.GetWeaponLicence().StableId);
        s.Fighters.FactionUnlocks.Add(ArenaRewards.GetLicence(ArenaRewards.ArmourSet.T1).StableId);
        TownChallengePolicy.Card c = s.Challenges;
        c.Seed = 77; c.AmbientSeed = 88; c.Division = 1; c.Milestones = 3; c.UniqueWins = 5; c.ChallengeWins = 9;
        c.PlayerHistory.Add(new List<string> { "op-1" });
        c.AmbientHistory.Add(new List<string> { "op-2", "op-3" });
        for (int i = 0; i < 5; ++i) { c.Offers[i].Epoch = 10 + i; c.Offers[i].Encounter = i; c.Offers[i].Format = i; c.Offers[i].Draw = (uint)(100 + i); }
        c.Offers[0].Opponent = "op-1"; c.Offers[0].Name = "One"; c.Offers[0].Roles[0] = TownChallengePolicy.Role(0);
        ref TownChallengePolicy.Offer g = ref c.Offers[1];
        g.Generated = true; g.PlayerCount = 2; g.EnemyCount = 2; g.Context = "ctx"; g.State = TownArenaPolicy.OfferState.Booked;
        g.Opponent = "g-1"; g.Name = "G1"; g.Roles[0] = TownChallengePolicy.Role(1); g.SecondOpponent = "g-2"; g.SecondName = "G2"; g.Roles[1] = "219-Proving Grounds.mod";
        c.SkarnOffer.Encounter = 5; c.SkarnOffer.Format = 6; c.SkarnGenerated = true; c.SkarnLastEnd = 12.5; c.LastChallengeEndHours = 30.25;
        c.HasChallengeEnd = true; c.PaidRefreshCount = 2; c.PaidRefreshNonce = 7;
        s.Diagnostic.Status = (int)TownDiagnosticData.RunStatus.Active; s.Diagnostic.RunId = 4; s.Diagnostic.NextSequence = 9;
        s.Diagnostic.Counters.Opened = 6; s.Diagnostic.Counters.Completed = 5;
        s.Diagnostic.Pending.Active = true; s.Diagnostic.Pending.Sequence = 8; s.Diagnostic.Pending.ScoreA[0] = 0.5;
        s.Diagnostic.Pending.ProbabilityA[1] = 0.75; s.Diagnostic.Pending.AnnouncedHours = 40.0;
        PlannedNpcBout n = s.PlannedNpc;
        n.Active = true; n.RegistryId = "reg-1"; n.StartHours = 41.5; n.ScoreA = 210.0; n.ScoreB = 190.0;
        n.TeamA.Add("na-1"); n.TeamB.Add("nb-1"); n.TeamB.Add("nb-2"); n.Market = true; n.WagerPending = true; n.WagerSide = 1; n.Stake = 100; n.Payout = 250;
        return s;
    }

    static byte[] Encode(Snapshot s)
    {
        byte[] json = System.Array.Empty<byte>();
        Assert.True(EncodeSnapshot(s, ref json, out string error), error);
        Assert.Equal("", error);
        return json;
    }

    static string Text(Snapshot s) => S(Encode(s));

    static LoadResult Decode(string text) => DecodeSnapshot(B(text));

    static void Rejected(string text, LoadCode code, string field, string message)
    {
        LoadResult r = Decode(text);
        Assert.Equal((code, field, message), (r.Code, r.Field, r.Message));
        Assert.Equal("", r.Snapshot.SaveKey);   // a failed decode returns the default snapshot
    }

    static string Edit(string text, string find, string replace)
    {
        int at = text.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{find}' not in the text");
        return text.Substring(0, at) + replace + text.Substring(at + find.Length);
    }

    // ---- round trip ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_full_snapshot_round_trips_and_the_writer_sorts_by_id()
    {
        Snapshot source = Full();
        string text = Text(source);
        LoadResult r = Decode(text);
        Assert.Equal(LoadCode.Ready, r.Code);
        Assert.Equal("", r.Field + r.Message + r.Snapshot.DiagnosticError);
        Snapshot d = r.Snapshot;
        Assert.Equal(16, d.Version);
        Assert.Equal(("Rich Save", "gen-rich"), (d.SaveKey, d.Generation));
        Assert.Equal(new[] { "p-a", "p-b" }, d.Fighters.PlayerStandings.Select(x => x.Id));   // sorted by id (ById, .cpp:407/412)
        Assert.Equal(1234.5f, d.Fighters.PlayerStandings[1].Mmr);
        Assert.Equal("Town \"Q\"", d.Fighters.TownStandings[0].Name);
        Assert.Equal(new[] { "armour.warlord.cloak", "weapon.katana" }, d.Fighters.Progression[0].RewardTiers.Select(t => t.Id));
        Assert.Equal(new[] { "armour.retainer", "weapon.all" }, d.Fighters.FactionUnlocks);
        Assert.Equal((77u, 88u, 1, 3u, 5u, 9u), (d.Challenges.Seed, d.Challenges.AmbientSeed, d.Challenges.Division, d.Challenges.Milestones,
            d.Challenges.UniqueWins, d.Challenges.ChallengeWins));
        Assert.Equal("g-2", d.Challenges.Offers[1].SecondOpponent);
        Assert.Equal(TownArenaPolicy.OfferState.Booked, d.Challenges.Offers[1].State);
        Assert.Equal((12.5, 30.25, true, 2, 7u), (d.Challenges.SkarnLastEnd, d.Challenges.LastChallengeEndHours, d.Challenges.HasChallengeEnd,
            d.Challenges.PaidRefreshCount, d.Challenges.PaidRefreshNonce));
        Assert.Equal(0.75, d.Diagnostic.Pending.ProbabilityA[1]);
        Assert.Equal(new[] { "nb-1", "nb-2" }, d.PlannedNpc.TeamB);
        Assert.Equal((true, 1, 100, 250), (d.PlannedNpc.WagerPending, d.PlannedNpc.WagerSide, d.PlannedNpc.Stake, d.PlannedNpc.Payout));
        // the source is not touched by the sort (the C++ sorts copies): every list keeps its order
        Assert.Equal(new[] { "p-b", "p-a" }, source.Fighters.PlayerStandings.Select(x => x.Id));
        Assert.Equal(new[] { "weapon.katana", "armour.warlord.cloak" }, source.Fighters.Progression[0].RewardTiers.Select(t => t.Id));
        Assert.Equal(new[] { "weapon.all", "armour.retainer" }, source.Fighters.FactionUnlocks);
        // and the decoded snapshot writes the same bytes again
        Assert.Equal(text, Text(d));
    }

    [Fact]
    public void Writer_format_is_rapidjson_PrettyWriter_with_two_space_indent()
    {
        var s = new Snapshot { SaveKey = "k", Generation = "g" };
        string text = Text(s);
        Assert.StartsWith("{\n  \"version\": 16,\n  \"saveKey\": \"k\",\n  \"generation\": \"g\",\n  \"playerStandings\": [],\n", text);
        Assert.Contains("\n    \"skarnLastEnd\": 0.0,\n", text);                        // doubles always carry a fraction
        Assert.Contains("\"models\": [\n        true,\n        true,\n        true\n      ],", text);
        Assert.Contains("\"reducedInfluence\": 0.25,", text);
        Assert.Contains("\"wagerSide\": -1,", text);
        Assert.EndsWith("    \"payout\": 0\n  }\n}\n", text);                            // EncodeSnapshot appends '\n'
        Assert.Equal(6235, B(text).Length);   // the length of the original's text (golden base 0 of Tests/Golden/snapshot.json)
    }

    [Fact]
    public void A_sidecar_written_by_hand_in_the_writers_order_parses()
    {
        // compact, written from the C++ writer's key order (ArenaSnapshotJson.cpp:444-471); escapes, a UTF-8 name, -0.0, an int mmr
        string offer = "{\"epoch\":-2,\"encounter\":0,\"format\":0,\"mode\":0,\"state\":0,\"generated\":false,\"draw\":0,\"division\":0," +
            "\"playerCount\":0,\"enemyCount\":0,\"context\":\"\",\"opponents\":[{\"id\":\"\",\"name\":\"\",\"role\":\"\"}," +
            "{\"id\":\"\",\"name\":\"\",\"role\":\"\"},{\"id\":\"\",\"name\":\"\",\"role\":\"\"}]}";
        string text = "{\"version\":16,\"saveKey\":\"slot \\u00e9\",\"generation\":\"5f0c\"," +
            "\"playerStandings\":[{\"id\":\"pg:1\",\"name\":\"Gr\u00f6\\u00dfe\",\"mmr\":120,\"wins\":2,\"losses\":1,\"matches\":3}]," +
            "\"townStandings\":[]," +
            "\"progression\":[{\"id\":\"pg:1\",\"name\":\"\",\"marks\":40,\"rewardTiers\":{\"weapon.katana\":2}}]," +
            "\"factionUnlocks\":[\"weapon.all\"]," +
            "\"challenges\":{\"seed\":1,\"ambientSeed\":2463534242,\"division\":0,\"milestones\":0,\"uniqueWins\":0,\"challengeWins\":0," +
            "\"playerHistory\":[[\"pg:2\"]],\"ambientHistory\":[],\"offers\":[" + offer + "," + offer + "," + offer + "," + offer + "," + offer + "]," +
            "\"skarnOffer\":" + offer + ",\"skarnGenerated\":false,\"skarnWon\":false,\"skarnAttempted\":true,\"skarnInProgress\":false," +
            "\"skarnLastEnd\":-0.0,\"lastChallengeEndHours\":1e2,\"hasChallengeEnd\":true,\"paidRefreshCount\":0,\"paidRefreshNonce\":1}," +
            "\"diagnostic\":{\"status\":0,\"runId\":0,\"nextSequence\":1,\"config\":{\"target\":150,\"models\":[true,false,false]," +
            "\"coverage\":[true,true,true,true],\"reducedInfluence\":0.25,\"underdogPercent\":20,\"balanceStyleTier\":true}," +
            "\"counters\":{\"opened\":0,\"completed\":0,\"cancelled\":0,\"aborted\":0,\"teamAWins\":0,\"teamBWins\":0,\"largerWins\":0," +
            "\"smallerWins\":0,\"equalWins\":0,\"favoriteWins\":0,\"modelCompleted\":[0,0,0],\"coverageCompleted\":[0,0,0,0]," +
            "\"schedulingHours\":0.0,\"lastCompletedHours\":0.0,\"schedulingSamples\":0},\"pending\":{\"active\":false,\"underdog\":false," +
            "\"sequence\":0,\"model\":0,\"requestedCoverage\":0,\"actualCoverage\":0,\"relaxation\":0,\"sizeA\":0,\"sizeB\":0," +
            "\"scoreA\":[0,0,0],\"scoreB\":[0,0,0],\"probabilityA\":[0,0,0],\"announcedHours\":0}}," +
            "\"bookieCredit\":120,\"challengeCredit\":0," +
            "\"plannedNpc\":{\"active\":false,\"registryId\":\"\",\"startHours\":0,\"scoreA\":0,\"scoreB\":0,\"teamA\":[],\"teamB\":[]," +
            "\"market\":false,\"wagerPending\":false,\"wagerSide\":-1,\"stake\":0,\"payout\":0}}";
        LoadResult r = Decode(text);
        Assert.True(r.Code == LoadCode.Ready, r.Field + ": " + r.Message);
        Assert.Equal("slot \u00e9", r.Snapshot.SaveKey);
        Assert.Equal("Gr\u00f6\u00dfe", r.Snapshot.Fighters.PlayerStandings[0].Name);
        Assert.Equal(120.0f, r.Snapshot.Fighters.PlayerStandings[0].Mmr);
        Assert.Equal(2, r.Snapshot.Fighters.Progression[0].RewardTiers[0].Tier);
        Assert.True(double.IsNegative(r.Snapshot.Challenges.SkarnLastEnd));   // -0.0 is a valid nonnegative number (-0.0 >= 0.0)
        Assert.Equal(100.0, r.Snapshot.Challenges.LastChallengeEndHours);
        Assert.Equal(120, r.Snapshot.BookieCredit);
        Assert.False(r.Snapshot.Diagnostic.Config.Models[1]);
        // the three-slot offers read back as four (the fourth stays the default empty slot) and write back in the writer's own form
        string again = Text(r.Snapshot);
        Assert.Contains("\"skarnLastEnd\": -0.0,", again);
        Assert.Contains("\"lastChallengeEndHours\": 100.0,", again);
        Assert.Contains("\"mmr\": 120.0,", again);
        Assert.Equal(LoadCode.Ready, Decode(again).Code);
    }

    // ---- rejection rules (field and message as the C++ builds them) ------------------------------------------------------------

    [Fact]
    public void Unknown_fields_are_rejected_at_every_level()
    {
        string t = Text(Full());
        Rejected(Edit(t, "\"version\": 16,", "\"version\": 16, \"extra\": 1,"), LoadCode.InvalidData, "$.extra", "unknown field");
        Rejected(Edit(t, "\"wins\": 1,", "\"wins\": 1, \"rank\": 2,"), LoadCode.InvalidData, "$.playerStandings[0].rank", "unknown field");
        Rejected(Edit(t, "\"epoch\": 10,", "\"epoch\": 10, \"x\": 0,"), LoadCode.InvalidData, "$.challenges.offers[0].x", "unknown field");
        Rejected(Edit(t, "\"registryId\": \"reg-1\",", "\"registryId\": \"reg-1\", \"r\": 1,"), LoadCode.InvalidData, "$.plannedNpc.r", "unknown field");
        Rejected(Edit(t, "\"version\": 16,", "\"version\": 16, \"a|b\": 1,"), LoadCode.InvalidData, "$.a|b", "unknown field");
        Rejected(Edit(t, "\"version\": 16,", "\"version\": 16, \"\": 1,"), LoadCode.InvalidData, "$.", "unknown field");
        // a field of an older schema is unknown in v16
        Rejected(Edit(t, "\"version\": 16,", "\"version\": 16, \"performanceProfile\": \"high\","), LoadCode.InvalidData, "$.performanceProfile", "unknown field");
    }

    [Fact]
    public void Duplicate_keys_and_ids_are_rejected()
    {
        string t = Text(Full());
        Rejected(Edit(t, "\"saveKey\": \"Rich Save\",", "\"saveKey\": \"Rich Save\", \"saveKey\": \"x\","), LoadCode.InvalidData, "$.saveKey", "duplicate object member");
        // decoded keys: an escaped alias is the same key
        Rejected(Edit(t, "\"saveKey\": \"Rich Save\",", "\"saveKey\": \"Rich Save\", \"save\\u004bey\": \"x\","), LoadCode.InvalidData, "$.saveKey", "duplicate object member");
        Rejected(Edit(t, "\"id\": \"p-b\"", "\"id\": \"p-a\""), LoadCode.InvalidData, "$.playerStandings[1].id", "duplicate identifier");
        Rejected(Edit(t, "\"weapon.all\"\n", "\"weapon.all\", \"weapon.all\"\n"), LoadCode.InvalidData, "$.factionUnlocks[2]", "duplicate identifier");
        Rejected(Edit(t, "\"nb-2\"", "\"na-1\""), LoadCode.InvalidData, "$.plannedNpc.teamB[1]", "duplicate identifier");
        Rejected(Edit(t, "\"op-3\"", "\"op-2\""), LoadCode.InvalidData, "$.challenges.ambientHistory[0][1]", "duplicate identifier");
    }

    [Fact]
    public void Wrong_types_and_ranges_are_rejected()
    {
        string t = Text(Full());
        Rejected(Edit(t, "\"wins\": 3", "\"wins\": 3.0"), LoadCode.InvalidData, "$.playerStandings[1].wins", "expected an integer in [0, 2147483647]");
        Rejected(Edit(t, "\"wins\": 3", "\"wins\": \"3\""), LoadCode.InvalidData, "$.playerStandings[1].wins", "expected an integer in [0, 2147483647]");
        Rejected(Edit(t, "\"name\": \"Beta\"", "\"name\": 1"), LoadCode.InvalidData, "$.playerStandings[1].name", "expected a string");
        Rejected(Edit(t, "\"mmr\": 1234.5", "\"mmr\": -1"), LoadCode.InvalidData, "$.playerStandings[1].mmr", "expected a finite number in [0, 3.40282e+038]");
        Rejected(Edit(t, "\"seed\": 77", "\"seed\": 4294967296"), LoadCode.InvalidData, "$.challenges.seed", "expected an unsigned integer in [0, 4294967295]");
        Rejected(Edit(t, "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 25000000"), LoadCode.InvalidData, "$.challenges.skarnLastEnd", "expected a finite number in [0, 2.4e+007]");
        Rejected(Edit(t, "\"hasChallengeEnd\": true", "\"hasChallengeEnd\": 1"), LoadCode.InvalidData, "$.challenges.hasChallengeEnd", "expected a boolean");
        Rejected(Edit(t, "\"factionUnlocks\": [\n    \"armour.retainer\",\n    \"weapon.all\"\n  ]", "\"factionUnlocks\": {}"), LoadCode.InvalidData, "$.factionUnlocks", "expected an array");
        Rejected(Edit(t, "\"saveKey\": \"Rich Save\"", "\"saveKey\": \"\""), LoadCode.InvalidData, "$.saveKey", "expected a nonempty identifier");
        Rejected(Edit(t, "\"saveKey\": \"Rich Save\"", "\"saveKey\": \"a\\u0000b\""), LoadCode.InvalidData, "$.saveKey", "embedded NUL is not supported");
        Rejected(Edit(t, "\"challengeCredit\": 1500", "\"challengeCredit\": 20001"), LoadCode.InvalidData, "$.challengeCredit", "expected an integer in [0, 20000]");
        Rejected(Edit(t, "\"stake\": 100", "\"stake\": 105"), LoadCode.InvalidData, "$.plannedNpc", "pending wager requires a valid announced market and payable frozen terms");
        Rejected(Edit(t, "\"bookieCredit\": 0", "\"bookieCredit\": 10"), LoadCode.InvalidData, "$.bookieCredit", "a pending wager cannot coexist with a bookie credit");
        Rejected(Edit(t, "\"weapon.katana\": 1", "\"weapon.katana\": 0"), LoadCode.InvalidData, "$.progression[0].rewardTiers.weapon.katana", "tier is not supported for this reward");
        Rejected(Edit(t, "\"weapon.all\"\n", "\"weapon.none\"\n"), LoadCode.InvalidData, "$.factionUnlocks[1]", "unknown faction unlock ID");
        Rejected(Edit(t, "\"skarnGenerated\": true", "\"skarnGenerated\": true, \"x\": 0"), LoadCode.InvalidData, "$.challenges.x", "unknown field");
    }

    [Fact]
    public void Nonfinite_huge_and_too_small_numbers_are_rejected()
    {
        string t = Text(Full());
        // rapidjson reports a bad value at the byte it could not read, NumberTooBig at the number's first byte (reader.h:1214, :1290)
        int at = B(t).AsSpan().IndexOf(B("1234.5"));
        Rejected(Edit(t, "\"mmr\": 1234.5", "\"mmr\": NaN"), LoadCode.InvalidJson, "$.playerStandings[1].mmr", "Invalid value. at byte " + at);
        Rejected(Edit(t, "\"mmr\": 1234.5", "\"mmr\": Infinity"), LoadCode.InvalidJson, "$.playerStandings[1].mmr", "Invalid value. at byte " + at);
        Rejected(Edit(t, "\"mmr\": 1234.5", "\"mmr\": -x"), LoadCode.InvalidJson, "$.playerStandings[1].mmr", "Invalid value. at byte " + (at + 1));
        Rejected(Edit(t, "\"mmr\": 1234.5", "\"mmr\": 1e400"), LoadCode.InvalidJson, "$.playerStandings[1].mmr", "Number too big to be stored in double. at byte " + at);
        Rejected(Edit(t, "\"mmr\": 1234.5", "\"mmr\": 1e-400"), LoadCode.InvalidData, "$.playerStandings[1].mmr", "nonzero number is too small to represent as a double");
        Rejected(Edit(t, "\"mmr\": 1234.5", "\"mmr\": 1e-46"), LoadCode.InvalidData, "$.playerStandings[1].mmr", "positive rating is too small to represent as a float");
        // an exact zero written with a tiny exponent stays a valid edit
        Assert.Equal(LoadCode.Ready, Decode(Edit(t, "\"mmr\": 1234.5", "\"mmr\": -0.000e-400")).Code);
        // rapidjson 1.1.0 turns a literal above DBL_MAX that passes its exponent check into a negative garbage double: still rejected
        Rejected(Edit(t, "\"skarnLastEnd\": 12.5", "\"skarnLastEnd\": 10e308"), LoadCode.InvalidData, "$.challenges.skarnLastEnd", "expected a finite number in [0, 2.4e+007]");
    }

    [Fact]
    public void Syntax_errors_carry_rapidjsons_text_and_byte_offset()
    {
        Rejected("", LoadCode.InvalidJson, "$", "The document is empty. at byte 0");
        Rejected("{\"version\":16", LoadCode.InvalidJson, "$", "Missing a comma or '}' after an object member. at byte 13");
        Rejected("{\"version\":16}x", LoadCode.InvalidJson, "$", "The document root must not be followed by other values. at byte 14");
        Rejected("{\"a\":1,}", LoadCode.InvalidJson, "$", "Missing a name for object member. at byte 7");
        Rejected("[1,]", LoadCode.InvalidJson, "$[1]", "Invalid value. at byte 3");
        Rejected("{\"a\" 1}", LoadCode.InvalidJson, "$.a", "Missing a colon after a name of object member. at byte 5");
        Rejected("{\"version\":16} // c", LoadCode.InvalidJson, "$", "The document root must not be followed by other values. at byte 15");   // no comments
        Rejected("\uFEFF{}", LoadCode.InvalidJson, "$", "Invalid value. at byte 0");   // no BOM
        Rejected("{\"a\":\"\\x\"}", LoadCode.InvalidJson, "$.a", "Invalid escape character in string. at byte 6");
        Rejected("{\"a\":\"\\uD800\"}", LoadCode.InvalidJson, "$.a", "The surrogate pair in string is invalid. at byte 6");
        Rejected("[]", LoadCode.InvalidData, "$", "expected a snapshot object");
        Rejected("{}", LoadCode.InvalidData, "$.version", "required field is missing");
    }

    [Fact]
    public void Invalid_UTF8_and_lone_surrogates_are_rejected()
    {
        byte[] t = B("{\"a\":\"x\"}");
        t[6] = 0xFF;
        LoadResult r = DecodeSnapshot(t);
        Assert.Equal((LoadCode.InvalidJson, "$.a", "Invalid encoding in string. at byte 6"), (r.Code, r.Field, r.Message));
        // an escaped lone low surrogate passes the reader and is caught by the guard's scalar check
        Rejected("{\"a\":\"\\uDC00\"}", LoadCode.InvalidJson, "$.a", "invalid Unicode scalar or UTF-8 encoding");
        // a C# string holding a lone surrogate is written as its 3-byte form, which the codec's own check rejects
        var s = new Snapshot { SaveKey = "k\uD800", Generation = "g" };
        byte[] json = { 1 };
        Assert.False(EncodeSnapshot(s, ref json, out string error));
        Assert.Equal("$.saveKey: Invalid encoding in string. at byte 34", error);
        Assert.Equal(new byte[] { 1 }, json);   // unchanged on failure
    }

    [Fact]
    public void Size_depth_and_NUL_limits()
    {
        string t = Text(new Snapshot { SaveKey = "k", Generation = "g" });
        Assert.Equal(LoadCode.Ready, Decode(t + new string(' ', MaxSnapshotBytes - B(t).Length)).Code);
        Rejected(t + new string(' ', MaxSnapshotBytes - B(t).Length + 1), LoadCode.InvalidJson, "$", "JSON exceeds the 16 MiB safety limit");
        Rejected("{\"version\":16}\0", LoadCode.InvalidJson, "$", "unescaped NUL byte in JSON");
        // 32 container levels are allowed: the root object, factionUnlocks and 30 more arrays
        string ok = Edit(t, "\"factionUnlocks\": []", "\"factionUnlocks\": [" + new string('[', 30) + new string(']', 30) + "]");
        Rejected(ok, LoadCode.InvalidData, "$.factionUnlocks[0]", "expected a string");
        // the 33rd fails at the path it would have had
        string deep = Edit(t, "\"factionUnlocks\": []", "\"factionUnlocks\": [" + new string('[', 31) + new string(']', 31) + "]");
        Rejected(deep, LoadCode.InvalidJson, "$.factionUnlocks" + string.Concat(Enumerable.Repeat("[0]", 31)), "JSON nesting exceeds 32 levels");
    }

    [Fact]
    public void Versions_below_11_reset_and_above_16_are_unsupported()
    {
        for (int v = 0; v <= 10; ++v)
        {
            LoadResult r = Decode("{\"version\":" + v + "}");
            Assert.Equal((LoadCode.LegacyReset, "$.version", "legacy arena progression must reset; preserve the original sidecar"), (r.Code, r.Field, r.Message));
        }
        LoadResult n = Decode("{\"version\":17,\"anything\":true}");
        Assert.Equal((LoadCode.UnsupportedVersion, "$.version", "snapshot schema is newer than supported version 16"), (n.Code, n.Field, n.Message));
        Rejected("{\"version\":\"16\"}", LoadCode.InvalidData, "$.version", "expected an integer in [0, 2147483647]");
        Rejected("{\"version\":1.5}", LoadCode.InvalidData, "$.version", "expected an integer in [0, 2147483647]");
        Rejected("{\"version\":-1}", LoadCode.InvalidData, "$.version", "expected an integer in [0, 2147483647]");
        // the version is read before the key set: a legacy file with any other content resets
        Assert.Equal(LoadCode.LegacyReset, Decode("{\"version\":3,\"junk\":{}}").Code);
    }

    [Fact]
    public void Older_schemas_use_their_own_key_sets()
    {
        string v16 = Text(new Snapshot { SaveKey = "k", Generation = "g" });
        int cut = v16.IndexOf(",\n  \"plannedNpc\": {", StringComparison.Ordinal);
        string noPlanned = v16.Substring(0, cut) + "\n}\n";
        string perf = Edit(noPlanned, "\"generation\": \"g\",", "\"generation\": \"g\",\n  \"performanceProfile\": \"potato\",\n  \"performanceWelcomeSeen\": false,");
        foreach (int v in new[] { 11, 15 })
            Assert.Equal(LoadCode.Ready, Decode(Edit(noPlanned, "\"version\": 16", "\"version\": " + v)).Code);
        foreach (int v in new[] { 12, 13, 14 })
        {
            LoadResult r = Decode(Edit(perf, "\"version\": 16", "\"version\": " + v));
            Assert.Equal(LoadCode.Ready, r.Code);
            Assert.Equal(16, r.Snapshot.Version);   // the decoded snapshot is always schema 16
        }
        Rejected(Edit(Edit(perf, "\"version\": 16", "\"version\": 13"), "\"potato\"", "\"ultra\""), LoadCode.InvalidData, "$.performanceProfile", "expected high, normal, or potato");
        Rejected(Edit(noPlanned, "\"version\": 16", "\"version\": 13"), LoadCode.InvalidData, "$.performanceProfile", "required field is missing");
        Rejected(Edit(v16, "\"version\": 16", "\"version\": 15"), LoadCode.InvalidData, "$.plannedNpc", "unknown field");
        Rejected(Edit(perf, "\"version\": 16", "\"version\": 15"), LoadCode.InvalidData, "$.performanceProfile", "unknown field");
        Rejected(noPlanned, LoadCode.InvalidData, "$.plannedNpc", "required field is missing");
    }

    [Fact]
    public void An_invalid_diagnostic_subsection_is_reset_with_a_warning()
    {
        string t = Text(Full());
        LoadResult r = Decode(Edit(t, "\"target\": 150", "\"target\": 4"));
        Assert.Equal(LoadCode.Ready, r.Code);
        Assert.Equal("$.diagnostic.counters.completed: completed fights exceed target", r.Snapshot.DiagnosticError);
        Assert.Equal(150, r.Snapshot.Diagnostic.Config.Target);          // the safe default State
        Assert.Equal(0u, r.Snapshot.Diagnostic.RunId);
        Assert.Equal("Rich Save", r.Snapshot.SaveKey);                   // the rest is kept
        // a missing diagnostic member is a diagnostic failure too (At(v,"diagnostic","$") throws inside the try)
        LoadResult m = Decode(Edit(t, "\"diagnostic\": {", "\"diagnosticX\": {"));
        Assert.Equal((LoadCode.InvalidData, "$.diagnosticX", "unknown field"), (m.Code, m.Field, m.Message));
        // and the encoder refuses to write state that would only load with that warning
        Snapshot bad = Full();
        bad.Diagnostic.Config.Target = 3;
        byte[] json = System.Array.Empty<byte>();
        Assert.False(EncodeSnapshot(bad, ref json, out string error));
        Assert.Equal("$.diagnostic.counters.completed: completed fights exceed target", error);
    }

    [Fact]
    public void Encode_refuses_other_versions_nonfinite_numbers_and_state_the_reader_rejects()
    {
        byte[] json = { 9 };
        Snapshot s = Full(); s.Version = 15;
        Assert.False(EncodeSnapshot(s, ref json, out string e1)); Assert.Equal("$.version: only schema version 16 can be written", e1);
        s = Full(); s.Fighters.PlayerStandings[0].Mmr = float.NaN;
        Assert.False(EncodeSnapshot(s, ref json, out string e2)); Assert.Equal("$.playerStandings[1].mmr: expected a finite number", e2);   // index after the sort
        s = Full(); s.Diagnostic.Pending.ScoreB[2] = double.PositiveInfinity;
        Assert.False(EncodeSnapshot(s, ref json, out string e3)); Assert.Equal("$.diagnostic.pending.scoreB[2]: expected a finite number", e3);
        s = Full(); s.Fighters.PlayerStandings.Add(new LeaderboardData.Standing { Id = "p-a" });
        Assert.False(EncodeSnapshot(s, ref json, out string e4)); Assert.Equal("$.playerStandings[1].id: duplicate identifier", e4);
        s = Full(); s.Fighters.Progression[0].RewardTiers.Add(new RewardProgression.TierEntry("weapon.katana", 2));
        Assert.False(EncodeSnapshot(s, ref json, out string e5)); Assert.Equal("$.progression[0].rewardTiers.weapon.katana: duplicate object member", e5);
        Assert.Equal(new byte[] { 9 }, json);
    }

    [Fact]
    public void Writer_sorts_ids_by_UTF8_bytes_like_std_string()
    {
        // U+FFFF (EF BF BF) sorts before U+10000 (F0 90 80 80) in bytes; UTF-16 ordinal order would put the surrogate pair first
        var s = new Snapshot { SaveKey = "k", Generation = "g" };
        s.Fighters.TownStandings.Add(new LeaderboardData.Standing { Id = "\U00010000" });
        s.Fighters.TownStandings.Add(new LeaderboardData.Standing { Id = "\uFFFF" });
        s.Fighters.TownStandings.Add(new LeaderboardData.Standing { Id = "Z" });
        s.Fighters.TownStandings.Add(new LeaderboardData.Standing { Id = "a" });
        LoadResult r = Decode(Text(s));
        Assert.Equal(new[] { "Z", "a", "\uFFFF", "\U00010000" }, r.Snapshot.Fighters.TownStandings.Select(x => x.Id));
    }

    [Fact]
    public void Bookie_credit_ceiling_follows_the_configured_stake_maximum()
    {
        string t = Text(new Snapshot { SaveKey = "k", Generation = "g" });
        string credit = Edit(t, "\"bookieCredit\": 0", "\"bookieCredit\": 99999");
        Rejected(credit, LoadCode.InvalidData, "$.bookieCredit", "expected an integer in [0, 50000]");
        using (PGConfig.UseBookieForTest(new PGConfig.Bookie { Minimum = 10, Maximum = 20000 }))
            Assert.Equal(LoadCode.Ready, Decode(credit).Code);
    }

    [Fact]
    public void String_overload_reads_the_UTF8_of_the_text()
    {
        LoadResult r = DecodeSnapshot(Text(Full()));
        Assert.Equal(LoadCode.Ready, r.Code);
    }

    // ---- rapidjson pieces with undefined behaviour in 1.1.0 (docs/divergences.md): defined answers here ------------------------

    static double Number(string literal)
    {
        byte[] t = B(literal);
        var doc = new RapidJson.Document().Parse(t, 0, t.Length, RapidJson.ParseFlags.FullPrecision | RapidJson.ParseFlags.Iterative);
        Assert.False(doc.HasParseError());
        return doc.Root.GetDouble();
    }

    [Fact]
    public void Undefined_behaviour_inputs_get_the_correctly_rounded_answer()
    {
        // zero significand that misses the fast path (clz of 0 in the C++)
        foreach (string zero in new[] { "0e300", "0.0e-30", "0e-100", "0.000e50", "0e38" })
            Assert.Equal(0.0, Number(zero));
        // a tiny value with many digits (an out-of-bounds GetCachedPower10 read in the C++)
        Assert.Equal(0.0, Number("1.234567890123456789e-342"));
        // and the codec then reports it as a nonzero number too small for a double
        string t = Text(new Snapshot { SaveKey = "k", Generation = "g" });
        Rejected(Edit(t, "\"skarnLastEnd\": 0.0", "\"skarnLastEnd\": 1.234567890123456789e-342"), LoadCode.InvalidData, "$.challenges.skarnLastEnd",
            "nonzero number is too small to represent as a double");
    }

    [Fact]
    public void Strtod_copies_1_1_0_where_it_is_not_correctly_rounded()
    {
        // pinned by the golden too (kind "num"); here as a reminder that these are the original's answers, not IEEE rounding
        Assert.Equal(4.94065645841246544e-324, Number("2.4703282292062327e-324"));   // just below half the smallest subnormal: IEEE gives 0
        Assert.True(Number("10e308") < 0);                                          // above DBL_MAX: IEEE gives +inf
    }

    // ---- Clone guards: a new field must be added to Clone (and these counts) ---------------------------------------------------

    const System.Reflection.BindingFlags Inst =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    [Fact]
    public void PlannedNpcBout_Clone_copies_every_field()
    {
        // C++ PlannedNpcBout (ArenaSnapshot.h:6-15) has 12 members
        Assert.True(typeof(PlannedNpcBout).GetFields(Inst).Length == 12, "PlannedNpcBout gained or lost a field: update Clone (and this count)");
        var b = new PlannedNpcBout { Active = true, RegistryId = "r", StartHours = 1.5, ScoreA = 2.5, ScoreB = 3.5, Market = true, WagerPending = true,
            WagerSide = 1, Stake = 20, Payout = 40 };
        b.TeamA.Add("a"); b.TeamB.Add("b");
        PlannedNpcBout c = b.Clone();
        foreach (var f in typeof(PlannedNpcBout).GetFields(Inst))
        {
            object? x = f.GetValue(b), y = f.GetValue(c);
            if (x is List<string> lx) { Assert.False(ReferenceEquals(lx, y), f.Name + " shares its list"); Assert.Equal(lx, (List<string>)y!); }
            else Assert.True(Equals(x, y), f.Name);
        }
        Assert.Equal(-1, new PlannedNpcBout().WagerSide);
    }

    [Fact]
    public void AssignFrom_copies_every_field_into_the_same_objects()
    {
        // field-count tripwires: a new field must be added to AssignFrom (and Clone, and these counts)
        Assert.True(typeof(Snapshot).GetFields(Inst).Length == 10, "Snapshot gained or lost a field: update AssignFrom (and this count)");
        Assert.True(typeof(PlannedNpcBout).GetFields(Inst).Length == 12, "PlannedNpcBout gained or lost a field: update AssignFrom (and this count)");
        Assert.True(typeof(LeaderboardData.Collections).GetFields(Inst).Length == 4, "Collections gained or lost a field: update AssignFrom/DeepClone (and this count)");

        Snapshot src = Full();
        src.DiagnosticError = "warn"; src.BookieCredit = 3; src.Version = 15;
        src.PlannedNpc.Active = true; src.PlannedNpc.RegistryId = "r"; src.PlannedNpc.StartHours = 1.5; src.PlannedNpc.ScoreA = 2.5;
        src.PlannedNpc.ScoreB = 3.5; src.PlannedNpc.TeamA.Add("ta"); src.PlannedNpc.TeamB.Add("tb"); src.PlannedNpc.Market = true;
        src.PlannedNpc.WagerPending = true; src.PlannedNpc.WagerSide = 1; src.PlannedNpc.Stake = 20; src.PlannedNpc.Payout = 40;

        var dst = new Snapshot { SaveKey = "old" };
        dst.Fighters.FactionUnlocks.Add("stale"); dst.PlannedNpc.TeamA.Add("stale");
        var keep = new Dictionary<string, object?>();
        foreach (var f in typeof(Snapshot).GetFields(Inst)) keep[f.Name] = f.GetValue(dst);
        var keepFighterLists = typeof(LeaderboardData.Collections).GetFields(Inst).ToDictionary(f => f.Name, f => f.GetValue(dst.Fighters));
        var keepTeams = (dst.PlannedNpc.TeamA, dst.PlannedNpc.TeamB);

        dst.AssignFrom(src);
        foreach (var f in typeof(Snapshot).GetFields(Inst))
        {
            object? x = f.GetValue(src), y = f.GetValue(dst);
            if (f.FieldType.IsValueType || x is string) { if (f.FieldType != typeof(TownDiagnosticData.State)) Assert.True(Equals(x, y), f.Name); continue; }
            Assert.True(ReferenceEquals(keep[f.Name], y), f.Name + " lost its identity");
            Assert.False(ReferenceEquals(x, y), f.Name + " is shared with the source");
        }
        foreach (var f in typeof(LeaderboardData.Collections).GetFields(Inst))
            Assert.True(ReferenceEquals(keepFighterLists[f.Name], f.GetValue(dst.Fighters)), "Fighters." + f.Name + " lost its identity");
        Assert.False(ReferenceEquals(src.Fighters.PlayerStandings[0], dst.Fighters.PlayerStandings[0]), "rows are cloned");
        foreach (var f in typeof(PlannedNpcBout).GetFields(Inst))
        {
            object? x = f.GetValue(src.PlannedNpc), y = f.GetValue(dst.PlannedNpc);
            if (x is List<string> lx) Assert.Equal(lx, (List<string>)y!); else Assert.True(Equals(x, y), "PlannedNpc." + f.Name);
        }
        Assert.True(ReferenceEquals(keepTeams.TeamA, dst.PlannedNpc.TeamA) && ReferenceEquals(keepTeams.TeamB, dst.PlannedNpc.TeamB), "team lists keep identity");
        Assert.Equal(src.Diagnostic.Pending.ProbabilityA[1], dst.Diagnostic.Pending.ProbabilityA[1]);
        // the whole content is a copy: the same bytes as the source (with the version restored)
        src.Version = 16; dst.Version = 16; src.DiagnosticError = ""; dst.DiagnosticError = ""; src.BookieCredit = 0; dst.BookieCredit = 0;
        src.PlannedNpc.StartHours = 1; dst.PlannedNpc.StartHours = 1;
        Assert.Equal(Text(src), Text(dst));
        // later source edits do not reach the target; self-assignment is a no-op
        src.Fighters.FactionUnlocks.Add("later"); src.PlannedNpc.TeamA.Add("later");
        Assert.DoesNotContain("later", dst.Fighters.FactionUnlocks); Assert.DoesNotContain("later", dst.PlannedNpc.TeamA);
        string before = Text(dst); dst.AssignFrom(dst); Assert.Equal(before, Text(dst));
        // assigning a fresh Snapshot resets every part in place
        dst.AssignFrom(new Snapshot());
        Assert.True(dst.SaveKey == "" && dst.Fighters.PlayerStandings.Count == 0 && dst.PlannedNpc.TeamA.Count == 0 && dst.PlannedNpc.WagerSide == -1);
        Assert.True(ReferenceEquals(keep["Fighters"], dst.Fighters) && ReferenceEquals(keep["Challenges"], dst.Challenges));
    }

    [Fact]
    public void Snapshot_and_LoadResult_Clone_copy_every_field()
    {
        // C++ Snapshot (ArenaSnapshot.h:18-30) has 10 members, LoadResult (:31-36) 4
        Assert.True(typeof(Snapshot).GetFields(Inst).Length == 10, "Snapshot gained or lost a field: update Clone (and this count)");
        Assert.True(typeof(LoadResult).GetFields(Inst).Length == 4, "LoadResult gained or lost a field: update Clone (and this count)");
        Snapshot s = Full();
        s.DiagnosticError = "warn"; s.BookieCredit = 3; s.Version = 15;
        Snapshot c = s.Clone();
        foreach (var f in typeof(Snapshot).GetFields(Inst))
        {
            object? x = f.GetValue(s), y = f.GetValue(c);
            if (f.FieldType.IsValueType || x is string) continue;
            Assert.False(ReferenceEquals(x, y), f.Name + " is shared, not copied");
        }
        Assert.Equal((15, "Rich Save", "gen-rich", 3, 1500, "warn"), (c.Version, c.SaveKey, c.Generation, c.BookieCredit, c.ChallengeCredit, c.DiagnosticError));
        Assert.False(ReferenceEquals(s.Fighters.PlayerStandings[0], c.Fighters.PlayerStandings[0]));
        Assert.Equal(s.Challenges.Offers[1].SecondOpponent, c.Challenges.Offers[1].SecondOpponent);
        Assert.Equal(s.Diagnostic.Pending.ProbabilityA[1], c.Diagnostic.Pending.ProbabilityA[1]);
        Assert.Equal(s.PlannedNpc.TeamB, c.PlannedNpc.TeamB);
        // the copy writes the same bytes as the source (with the version restored)
        s.Version = 16; c.Version = 16; s.DiagnosticError = ""; c.DiagnosticError = ""; s.BookieCredit = 0; c.BookieCredit = 0;
        Assert.Equal(Text(s), Text(c));
        var r = new LoadResult { Code = LoadCode.WrongSnapshot, Snapshot = s, Field = "f", Message = "m" };
        LoadResult rc = r.Clone();
        Assert.Equal((LoadCode.WrongSnapshot, "f", "m"), (rc.Code, rc.Field, rc.Message));
        Assert.False(ReferenceEquals(r.Snapshot, rc.Snapshot));
        Assert.Equal(LoadCode.Missing, new LoadResult().Code);
        Assert.Equal(16, new Snapshot().Version);
    }
}
