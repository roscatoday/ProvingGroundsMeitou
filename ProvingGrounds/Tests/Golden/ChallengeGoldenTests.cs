using System.Text;
using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownChallengePolicy;
using OfferState = ProvingGrounds.Core.TownArenaPolicy.OfferState;
using TA = ProvingGrounds.Core.TownArenaPolicy;
using TR = ProvingGrounds.Core.TownArenaRuntimePolicy;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/TownArenaPolicy.cs, TownArenaRuntimePolicy.cs, TownChallengePolicy.cs (+ .Codec.cs), TownRosterSelection.cs,
/// TownFurniturePolicy.cs and TownFighterCatalog.cs against Tests/Golden/challenge.json (golden/gen_challenge.cpp; its header lists the
/// case kinds and the offer/card layouts). One [Fact] per function group.</summary>
public class ChallengeGoldenTests
{
    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("challenge"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    static int Int(JsonElement c, string n) => c.GetProperty(n).GetInt32();
    static uint UInt(JsonElement c, string n) => c.GetProperty(n).GetUInt32();
    static bool Bool(JsonElement c, string n) => c.GetProperty(n).GetInt32() != 0;
    static string Str(JsonElement c, string n) => c.GetProperty(n).GetString()!;
    static float F(JsonElement c, string n) => (float)c.GetProperty(n).GetDouble();
    static double Bits(JsonElement c, string n) => BitConverter.Int64BitsToDouble(unchecked((long)c.GetProperty(n).GetUInt64()));
    static void Same(double expected, double actual, string what) =>
        Assert.True(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual),
            $"{what}: expected {expected:R}, actual {actual:R}");

    // ---- offers and cards (layout in golden/gen_challenge.cpp PrintOffer / PrintCard) ----
    internal static Offer ReadOffer(JsonElement a)
    {
        var o = new Offer
        {
            Epoch = a[0].GetInt32(), Encounter = a[1].GetInt32(), Format = a[2].GetInt32(), Mode = a[3].GetInt32(),
            State = (OfferState)a[4].GetInt32(),
            Opponent = a[5].GetString()!, Name = a[6].GetString()!, SecondOpponent = a[7].GetString()!, SecondName = a[8].GetString()!,
            ThirdOpponent = a[9].GetString()!, ThirdName = a[10].GetString()!, FourthOpponent = a[11].GetString()!, FourthName = a[12].GetString()!,
            Generated = a[13].GetInt32() != 0, Draw = a[14].GetUInt32(), Division = a[15].GetInt32(), PlayerCount = a[16].GetInt32(),
            EnemyCount = a[17].GetInt32(), Context = a[18].GetString()!,
        };
        for (int i = 0; i < 4; ++i) o.Roles[i] = a[19 + i].GetString()!;
        return o;
    }

    internal static void AssertOffer(JsonElement e, in Offer o, string what)
    {
        Assert.True(e[0].GetInt32() == o.Epoch, what + " epoch");
        Assert.True(e[1].GetInt32() == o.Encounter, what + " encounter");
        Assert.True(e[2].GetInt32() == o.Format, what + " format");
        Assert.True(e[3].GetInt32() == o.Mode, what + " mode");
        Assert.True(e[4].GetInt32() == (int)o.State, what + " state");
        string[] names = { "opponent", "name", "secondOpponent", "secondName", "thirdOpponent", "thirdName", "fourthOpponent", "fourthName" };
        string[] values = { o.Opponent, o.Name, o.SecondOpponent, o.SecondName, o.ThirdOpponent, o.ThirdName, o.FourthOpponent, o.FourthName };
        for (int i = 0; i < 8; ++i) Assert.True(e[5 + i].GetString() == values[i], $"{what} {names[i]}: '{e[5 + i].GetString()}' vs '{values[i]}'");
        Assert.True((e[13].GetInt32() != 0) == o.Generated, what + " generated");
        Assert.True(e[14].GetUInt32() == o.Draw, what + " draw");
        Assert.True(e[15].GetInt32() == o.Division, what + " division");
        Assert.True(e[16].GetInt32() == o.PlayerCount, what + " playerCount");
        Assert.True(e[17].GetInt32() == o.EnemyCount, what + " enemyCount");
        Assert.True(e[18].GetString() == o.Context, what + " context");
        for (int i = 0; i < 4; ++i) Assert.True(e[19 + i].GetString() == o.Roles[i], $"{what} roles[{i}]");
    }

    static List<List<string>> ReadHist(JsonElement h) =>
        h.EnumerateArray().Select(b => b.EnumerateArray().Select(s => s.GetString()!).ToList()).ToList();

    static void AssertHistory(JsonElement e, List<List<string>> h, string what)
    {
        Assert.True(e.GetArrayLength() == h.Count, what + " count");
        for (int i = 0; i < h.Count; ++i)
            Assert.True(e[i].EnumerateArray().Select(s => s.GetString()!).SequenceEqual(h[i]), $"{what}[{i}]");
    }

    internal static Card ReadCard(JsonElement c)
    {
        var card = new Card
        {
            Seed = UInt(c, "seed"), AmbientSeed = UInt(c, "as"), Division = Int(c, "div"),
            Milestones = UInt(c, "mi"), UniqueWins = UInt(c, "uw"), ChallengeWins = UInt(c, "cw"),
            SkarnOffer = ReadOffer(c.GetProperty("sk")),
            SkarnGenerated = Bool(c, "sg"), SkarnWon = Bool(c, "sw"), SkarnAttempted = Bool(c, "sa"), SkarnInProgress = Bool(c, "sp"),
            SkarnLastEnd = Bits(c, "sleb"), LastChallengeEndHours = Bits(c, "lceb"), HasChallengeEnd = Bool(c, "hce"),
            PaidRefreshCount = Int(c, "prc"), PaidRefreshNonce = UInt(c, "prn"),
        };
        card.PlayerHistory.AddRange(ReadHist(c.GetProperty("ph")));   // readonly lists: filled, never replaced
        card.AmbientHistory.AddRange(ReadHist(c.GetProperty("ah")));
        for (int i = 0; i < 5; ++i) card.Offers[i] = ReadOffer(c.GetProperty("of")[i]);
        return card;
    }

    internal static void AssertCard(JsonElement c, Card card, string what)
    {
        Assert.True(UInt(c, "seed") == card.Seed, what + " seed");
        Assert.True(UInt(c, "as") == card.AmbientSeed, what + " ambientSeed");
        Assert.True(Int(c, "div") == card.Division, what + " division");
        AssertHistory(c.GetProperty("ph"), card.PlayerHistory, what + " playerHistory");
        AssertHistory(c.GetProperty("ah"), card.AmbientHistory, what + " ambientHistory");
        Assert.True(UInt(c, "mi") == card.Milestones, what + " milestones");
        Assert.True(UInt(c, "uw") == card.UniqueWins, what + " uniqueWins");
        Assert.True(UInt(c, "cw") == card.ChallengeWins, what + " challengeWins");
        for (int i = 0; i < 5; ++i) AssertOffer(c.GetProperty("of")[i], card.Offers[i], $"{what} offers[{i}]");
        AssertOffer(c.GetProperty("sk"), card.SkarnOffer, what + " skarnOffer");
        Assert.True(Bool(c, "sg") == card.SkarnGenerated, what + " skarnGenerated");
        Assert.True(Bool(c, "sw") == card.SkarnWon, what + " skarnWon");
        Assert.True(Bool(c, "sa") == card.SkarnAttempted, what + " skarnAttempted");
        Assert.True(Bool(c, "sp") == card.SkarnInProgress, what + " skarnInProgress");
        Same(Bits(c, "sleb"), card.SkarnLastEnd, what + " skarnLastEnd");
        Same(Bits(c, "lceb"), card.LastChallengeEndHours, what + " lastChallengeEndHours");
        Assert.True(Bool(c, "hce") == card.HasChallengeEnd, what + " hasChallengeEnd");
        Assert.True(Int(c, "prc") == card.PaidRefreshCount, what + " paidRefreshCount");
        Assert.True(UInt(c, "prn") == card.PaidRefreshNonce, what + " paidRefreshNonce");
    }

    static void AssertDefaultCard(Card card, string what) => AssertCard(Cases("dflt")[0].GetProperty("c"), card, what);

    // A regenerated challenge.json with fewer (or other) cases must be a deliberate change: update this table with it.
    static readonly Dictionary<string, int> ExpectedCounts = new()
    {
        ["aft"] = 1728, ["amb"] = 1936, ["cost"] = 150, ["dec"] = 867, ["decl"] = 1051, ["dflt"] = 1, ["didx"] = 200, ["disc"] = 8,
        ["draw"] = 5, ["efd"] = 800, ["enc"] = 120, ["ep"] = 8, ["exa"] = 8, ["furn"] = 8, ["hex"] = 20, ["hist"] = 300, ["hlp"] = 600,
        ["ids"] = 712, ["iso"] = 8, ["key"] = 300, ["l3"] = 210, ["l7"] = 320, ["l7r"] = 2000, ["mso"] = 414, ["names"] = 15,
        ["nrole"] = 13, ["okey"] = 64, ["orn"] = 210, ["ost"] = 4, ["prev"] = 110, ["rar"] = 1, ["rarg"] = 9, ["rdy"] = 1500,
        ["refr"] = 192, ["res"] = 300, ["rhist"] = 300, ["rip"] = 50, ["rost"] = 300, ["rroll"] = 122, ["rse"] = 1200, ["rvic"] = 800,
        ["same"] = 500, ["sdiv"] = 150, ["sel"] = 8, ["team"] = 192, ["uio"] = 250, ["unhex"] = 23, ["why"] = 250,
    };

    [Fact]
    public void Case_counts_are_pinned()
    {
        Assert.Equal(18337, All.Value.GetArrayLength());
        var actual = All.Value.EnumerateArray().GroupBy(c => c.GetProperty("k").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(ExpectedCounts.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(18337, ExpectedCounts.Values.Sum());
    }

    // ---- TownArenaPolicy ----
    [Fact]
    public void Epochs_match()
    {
        foreach (var c in Cases("ep"))
        {
            int r = Int(c, "r");
            var hb = c.GetProperty("hb");
            for (int i = 0; i < hb.GetArrayLength(); ++i)
            {
                double h = BitConverter.Int64BitsToDouble(unchecked((long)hb[i].GetUInt64()));
                Assert.True(c.GetProperty("ce")[i].GetInt32() == TA.CardEpoch(h), $"CardEpoch({h:R}) [r={r} #{i}]");
                Assert.True(c.GetProperty("cce")[i].GetInt32() == TA.ChallengeCardEpoch(h, r), $"ChallengeCardEpoch({h:R}, {r}) #{i}");
                double hu = BitConverter.Int64BitsToDouble(unchecked((long)c.GetProperty("hu")[i].GetUInt64()));
                Same(hu, TA.HoursUntilChallengeRefresh(h, r), $"HoursUntil({h:R}, {r}) #{i}");
            }
        }
    }

    [Fact]
    public void PaidRefreshCost_matches()
    {
        foreach (var c in Cases("cost"))
        {
            int b = Int(c, "b"), n0 = Int(c, "n0"); double m = Bits(c, "mb");
            var o = c.GetProperty("o");
            for (int i = 0; i < o.GetArrayLength(); ++i)
                Assert.True(o[i].GetInt32() == TA.PaidRefreshCost(b, m, n0 + i), $"PaidRefreshCost({b}, {m:R}, {n0 + i})");
        }
    }

    [Fact]
    public void Event_selection_matches()
    {
        foreach (var c in Cases("sel"))
            Assert.True(Int(c, "o") == (int)TA.SelectNext(Bool(c, "a"), Bool(c, "b"), Bool(c, "c")), $"SelectNext({Bool(c, "a")}, {Bool(c, "b")}, {Bool(c, "c")})");
        foreach (var c in Cases("disc"))
            Assert.True(Bool(c, "o") == TA.ShouldDiscoverRegistry(Bool(c, "a"), Bool(c, "b"), Bool(c, "c")), $"ShouldDiscoverRegistry({Bool(c, "a")}, {Bool(c, "b")}, {Bool(c, "c")})");
        foreach (var c in Cases("amb"))
        {
            int m = Int(c, "m"); double now = Bits(c, "nb"), due = Bits(c, "db");
            Assert.True(Int(c, "o") == (int)TA.AmbientNext((m & 1) != 0, (m & 2) != 0, (m & 4) != 0, (m & 8) != 0, now, due),
                $"AmbientNext(flags {m}, now {now:R}, due {due:R})");
        }
        foreach (var c in Cases("team"))
            Assert.True(Int(c, "o") == TA.SelectNpcTeamSize(Int(c, "n"), UInt(c, "roll")), $"SelectNpcTeamSize({Int(c, "n")}, {UInt(c, "roll")})");
    }

    [Fact]
    public void Offer_state_machine_matches()
    {
        foreach (var c in Cases("ost"))
        {
            OfferState a = (OfferState)Int(c, "s"), b = a, d = a;
            Assert.Equal(Bool(c, "book"), TA.BookOffer(ref a));
            Assert.Equal(Int(c, "bs"), (int)a);
            Assert.Equal(Bool(c, "begin"), TA.BeginOffer(ref b));
            Assert.Equal(Int(c, "gs"), (int)b);
            TA.CancelBooking(ref d);
            Assert.Equal(Int(c, "cs"), (int)d);
        }
    }

    [Fact]
    public void Defaults_match()
    {
        var c = Cases("dflt")[0];
        AssertOffer(c.GetProperty("o"), new Offer(), "Offer()");
        AssertCard(c.GetProperty("c"), new Card(), "Card()");
        Assert.True(Int(c, "per") == TA.OffersPerCard);
    }

    // ---- TownChallengePolicy: generation ----
    [Fact]
    public void RefreshGenerated_matches()
    {
        int n = 0;
        foreach (var c in Cases("refr"))
        {
            Card card = ReadCard(c.GetProperty("in"));
            RefreshGenerated(card, Int(c, "e"), Int(c, "p"));
            AssertCard(c.GetProperty("out"), card, $"refr #{n++} e={Int(c, "e")} p={Int(c, "p")}");
        }
    }

    [Fact]
    public void SetDivision_matches()
    {
        int n = 0;
        foreach (var c in Cases("sdiv"))
        {
            Card card = ReadCard(c.GetProperty("in"));
            Assert.Equal(Bool(c, "ok"), SetDivision(card, Int(c, "d"), Bool(c, "l")));
            AssertCard(c.GetProperty("out"), card, $"sdiv #{n++}");
        }
    }

    [Fact]
    public void OfferRarity_matches()
    {
        var r = Cases("rar")[0].GetProperty("r");
        var o = new Offer { Generated = true };
        for (int d = 0; d < 10000; ++d) { o.Draw = (uint)d; Assert.Equal(r[d].GetInt32(), OfferRarity(o, d % 3)); }
        foreach (var c in Cases("rarg")) { o.Draw = UInt(c, "d"); Assert.Equal(Int(c, "o"), OfferRarity(o, 2)); }
        foreach (var c in Cases("orn"))
        {
            var n = new Offer { Encounter = Int(c, "e"), Draw = UInt(c, "d") };
            Assert.Equal(Int(c, "o"), OfferRarity(n, Int(c, "div")));
        }
    }

    [Fact]
    public void LegendaryEncounter_matches()
    {
        foreach (var c in Cases("l7"))
        {
            var r = c.GetProperty("r");
            int k = 0;
            for (uint uw = 0; uw < 32; ++uw)
                for (uint roll = 0; roll < 16; ++roll)
                    Assert.True(r[k++].GetInt32() == LegendaryEncounter(Int(c, "div"), roll, UInt(c, "rep"), UInt(c, "reg"), uw, Bool(c, "su"), Bool(c, "sw")),
                        $"LegendaryEncounter(div {Int(c, "div")}, roll {roll}, rep {UInt(c, "rep")}, reg {UInt(c, "reg")}, uw {uw}, su {Bool(c, "su")}, sw {Bool(c, "sw")})");
        }
        int n = 0;
        foreach (var c in Cases("l7r"))
            Assert.True(Int(c, "o") == LegendaryEncounter(Int(c, "div"), UInt(c, "a"), UInt(c, "b"), UInt(c, "c"), UInt(c, "uw"), Bool(c, "su"), Bool(c, "sw")),
                $"l7r #{n++}: div {Int(c, "div")}, rolls {UInt(c, "a")}/{UInt(c, "b")}/{UInt(c, "c")}, uw {UInt(c, "uw")}");
        foreach (var c in Cases("l3"))
            Assert.True(Int(c, "o") == LegendaryEncounter(Int(c, "div"), UInt(c, "roll"), Bool(c, "su")),
                $"LegendaryEncounter(div {Int(c, "div")}, roll {UInt(c, "roll")}, su {Bool(c, "su")})");
    }

    [Fact]
    public void EncounterForDivision_matches()
    {
        foreach (var c in Cases("efd"))
            Assert.Equal(Int(c, "r"), EncounterForDivision(ReadOffer(c.GetProperty("o")), Int(c, "slot"), Int(c, "div"), UInt(c, "uw"), Bool(c, "su"), Bool(c, "sw")));
    }

    [Fact]
    public void PreviewSource_CanBookDivision_Visible_match()
    {
        int n = 0;
        foreach (var c in Cases("prev"))
        {
            Card card = ReadCard(c.GetProperty("c"));
            for (int slot = -1; slot <= 6; ++slot)
            {
                AssertOffer(c.GetProperty("r")[slot + 1], PreviewSource(card, slot), $"prev #{n} slot {slot}");
                Assert.Equal(c.GetProperty("bk")[slot + 1].GetInt32() != 0, CanBookDivision(card, slot));
                Assert.Equal(c.GetProperty("vis")[slot + 1].GetInt32() != 0, Visible(card, slot));
            }
            AssertCard(c.GetProperty("c"), card, $"prev #{n} card unchanged");
            n++;
        }
    }

    [Fact]
    public void UnavailableReason_matches()
    {
        foreach (var c in Cases("why"))
        {
            Offer o = ReadOffer(c.GetProperty("o"));
            for (int m = 0; m < 8; ++m)
                Assert.Equal(c.GetProperty("r")[m].GetString(), UnavailableReason(o, (m & 1) != 0, (m & 2) != 0, (m & 4) != 0));
        }
    }

    // ---- codec ----
    static string HexOfBytes(string s) => Convert.ToHexString(Encoding.UTF8.GetBytes(s)).ToLowerInvariant();

    [Fact]
    public void Hex_Unhex_match()
    {
        foreach (var c in Cases("hex")) Assert.Equal(Str(c, "o"), Hex(Str(c, "s")));
        foreach (var c in Cases("unhex"))
        {
            string result = "junk";
            bool ok = Unhex(Str(c, "s"), ref result);
            bool expected = Bool(c, "ok") && !Bool(c, "inv");   // inv: the C++ produced invalid UTF-8, which the port refuses
            Assert.True(expected == ok, $"Unhex('{Str(c, "s")}')");
            if (ok) Assert.Equal(Str(c, "o"), HexOfBytes(result));
        }
    }

    [Fact]
    public void Encode_and_round_trip_match()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        int n = 0;
        foreach (var c in Cases("enc"))
        {
            Card card = ReadCard(c.GetProperty("c"));
            Assert.Equal(Str(c, "e"), Encode(card));
            Assert.Equal(Str(c, "el"), EncodeLegacy(card));
            AssertCard(c.GetProperty("c"), card, $"enc #{n} input unchanged");
            var decoded = new Card();
            bool ok = Decode(Str(c, "e"), decoded);
            Assert.True(Bool(c, "ok") == ok, $"enc #{n} Decode ok");
            if (ok) AssertCard(c.GetProperty("d"), decoded, $"enc #{n} decoded");
            else AssertDefaultCard(decoded, $"enc #{n} untouched");
            n++;
        }
    }

    static void CheckDecode(string kind)
    {
        int n = 0;
        foreach (var c in Cases(kind))
        {
            string text = Str(c, "t");
            var card = new Card();
            bool ok = kind == "decl" ? DecodeLegacy(text, card) : Decode(text, card);
            bool expected = Bool(c, "ok") && !Bool(c, "inv");   // inv: the C++ accepted invalid UTF-8 (docs/divergences.md)
            Assert.True(expected == ok, $"{kind} #{n} ok: '{(text.Length > 300 ? text[..300] : text)}'");
            if (ok) AssertCard(c.GetProperty("c"), card, $"{kind} #{n}");
            else AssertDefaultCard(card, $"{kind} #{n} untouched");
            n++;
        }
    }

    [Fact]
    public void Decode_matches() => CheckDecode("dec");

    [Fact]
    public void DecodeLegacy_matches() => CheckDecode("decl");

    [Fact]
    public void WriteHistory_ReadHistory_match()
    {
        foreach (var c in Cases("hist"))
        {
            var sb = new StringBuilder();
            WriteHistory(sb, ReadHist(c.GetProperty("h")));
            Assert.Equal(Str(c, "t"), sb.ToString());
        }
        int n = 0;
        foreach (var c in Cases("rhist"))
        {
            var history = new List<List<string>>();
            if (Int(c, "pre") == 1) history.Add(new List<string> { "pre" });
            var stream = new CStream(Str(c, "t"));
            bool ok = ReadHistory(stream, history);
            bool expected = Bool(c, "ok") && !Bool(c, "inv");
            Assert.True(expected == ok, $"rhist #{n} '{Str(c, "t")}'");
            if (ok)
            {
                AssertHistory(c.GetProperty("h"), history, $"rhist #{n}");
                Assert.Equal(Str(c, "rest"), stream.Rest);
            }
            n++;
        }
    }

    // ---- small helpers ----
    [Fact]
    public void Offer_helpers_match()
    {
        int h = 0;
        foreach (var c in Cases("hlp"))
        {
            Offer o = ReadOffer(c.GetProperty("o"));
            string at = $"hlp #{h} (generated {o.Generated}, format {o.Format}, encounter {o.Encounter}, players {o.PlayerCount}, enemies {o.EnemyCount})";
            Assert.True(Int(c, "pl") == Players(o), at + " Players");
            Assert.True(Int(c, "op") == Opponents(o), at + " Opponents");
            Assert.True(Int(c, "sr") == SecondRole(o), at + " SecondRole");
            Assert.True(Bool(c, "res") == Resolved(o), at + " Resolved");
            int[] members = { 0, 1, 2, 3, -1 };
            for (int i = 0; i < 5; ++i) Assert.True(c.GetProperty("or")[i].GetInt32() == OpponentRole(o, members[i]), $"{at} OpponentRole({members[i]})");
            Offer cleared = o;
            ClearPreview(ref cleared);
            AssertOffer(c.GetProperty("cl"), cleared, at + " ClearPreview");
            AssertOffer(c.GetProperty("o"), o, at + " ClearPreview's source copy");
            h++;
        }
        int sm = 0;
        foreach (var c in Cases("same"))
            Assert.True(Bool(c, "o") == SameEncounter(ReadOffer(c.GetProperty("a")), ReadOffer(c.GetProperty("b"))), $"same #{sm++}");
        int n = 0;
        foreach (var c in Cases("res"))
        {
            Offer source = ReadOffer(c.GetProperty("s")), preview = ReadOffer(c.GetProperty("p")), reservation = ReadOffer(c.GetProperty("r"));
            Assert.Equal(Bool(c, "ok"), ReserveOffer(ref source, preview, ref reservation));
            AssertOffer(c.GetProperty("so"), source, $"res #{n} source");
            AssertOffer(c.GetProperty("ro"), reservation, $"res #{n} reservation");
            n++;
        }
        int u = 0;
        foreach (var c in Cases("uio"))
        {
            var offers = new Offer[] { new(), new(), new(), new(), new() };
            for (int i = 0; i < 5; ++i) offers[i] = ReadOffer(c.GetProperty("of")[i]);
            Assert.True(Int(c, "o") == UniqueEncountersInOffers(offers, Int(c, "div"), Int(c, "ex")), $"uio #{u++}: div {Int(c, "div")}, except {Int(c, "ex")}");
        }
    }

    [Fact]
    public void Names_and_tables_match()
    {
        foreach (var c in Cases("names"))
        {
            int e = Int(c, "e");
            Assert.True(Str(c, "role") == Role(e), $"Role({e})");
            Assert.True(Str(c, "rar") == Rarity(e), $"Rarity({e})");
            Assert.True(Str(c, "title") == Title(e), $"Title({e})");
            Assert.True(Str(c, "mode") == ModeName(e), $"ModeName({e})");
            Assert.True(Str(c, "rn") == RarityName(e), $"RarityName({e})");
            Assert.True(Str(c, "dn") == DivisionName(e), $"DivisionName({e})");
            Assert.True(Int(c, "nd") == NamedDivision(e), $"NamedDivision({e})");
            Assert.True(UInt(c, "uv") == UniqueVictoryForEncounter(e), $"UniqueVictoryForEncounter({e})");
            Assert.True(Bool(c, "ue") == UniqueEncounter(e), $"UniqueEncounter({e})");
            for (int d = -1; d <= 3; ++d) Assert.True((c.GetProperty("nfd")[d + 1].GetInt32() != 0) == NamedForDivision(e, d), $"NamedForDivision({e}, {d})");
        }
        foreach (var c in Cases("nrole")) Assert.True(Int(c, "o") == NamedRoleDivision(Str(c, "s")), $"NamedRoleDivision('{Str(c, "s")}')");
        foreach (var c in Cases("okey")) Assert.True(Str(c, "o") == OpponentKey(Str(c, "u"), Str(c, "r"), Str(c, "n")), $"OpponentKey('{Str(c, "u")}', '{Str(c, "r")}', '{Str(c, "n")}')");
        foreach (var c in Cases("rroll")) Assert.True(Int(c, "o") == RarityForRoll(UInt(c, "r")), $"RarityForRoll({UInt(c, "r")})");
    }

    [Fact]
    public void Card_progress_helpers_match()
    {
        foreach (var c in Cases("rse"))
        {
            var card = new Card { SkarnAttempted = Bool(c, "sa"), SkarnLastEnd = Bits(c, "sleb") };
            var offer = new Offer { Encounter = Int(c, "e") };
            double now = Bits(c, "nb");
            Assert.Equal(Bool(c, "o"), ChallengeCooldownActive(card, offer, now, Int(c, "cd")));
            RecordSkarnChallengeEnd(card, offer, now);
            Assert.Equal(Bool(c, "sa2"), card.SkarnAttempted);
            Same(Bits(c, "sle2b"), card.SkarnLastEnd, "skarnLastEnd after RecordSkarnChallengeEnd");
        }
        foreach (var c in Cases("rvic"))
        {
            var card = new Card { UniqueWins = UInt(c, "uw"), ChallengeWins = UInt(c, "cw") };
            Assert.Equal(Bool(c, "su0"), SkarnUnlocked(card));
            RecordVictory(card, Bool(c, "ch"), Bool(c, "won"), UInt(c, "def"));
            Assert.Equal(UInt(c, "uw2"), card.UniqueWins);
            Assert.Equal(UInt(c, "cw2"), card.ChallengeWins);
            Assert.Equal(Bool(c, "su"), SkarnUnlocked(card));
        }
        foreach (var c in Cases("draw"))
        {
            var card = new Card { Seed = UInt(c, "s") };
            for (int i = 0; i < 20; ++i) Assert.Equal(c.GetProperty("r")[i].GetUInt32(), Draw(card));
            Assert.Equal(UInt(c, "after"), card.Seed);
        }
    }

    // ---- TownArenaRuntimePolicy, TownFurniturePolicy, TownFighterCatalog ----
    [Fact]
    public void Runtime_policy_matches()
    {
        foreach (var c in Cases("key"))
        {
            var a = c.GetProperty("a");
            Assert.Equal(Str(c, "o"), TR.PlayerSelectionKey(a[0].GetUInt32(), a[1].GetUInt32(), a[2].GetUInt32(), a[3].GetUInt32(), a[4].GetUInt32()));
        }
        foreach (var c in Cases("mso"))
        {
            TR.MedicStandbyOffset(F(c, "dx"), F(c, "dz"), out float x, out float z);
            GoldenFile.Near(F(c, "x"), x, "x");
            GoldenFile.Near(F(c, "z"), z, "z");
        }
        foreach (var c in Cases("rip")) Assert.Equal(Bool(c, "o"), TR.RetainedIncidentPair(Bool(c, "r"), Int(c, "a"), Int(c, "b")));
        foreach (var c in Cases("exa")) { int m = Int(c, "m"); Assert.Equal(Bool(c, "o"), TR.ExemptArenaIncident((m & 1) != 0, (m & 2) != 0, (m & 4) != 0)); }
        foreach (var c in Cases("iso")) { int m = Int(c, "m"); Assert.Equal(Bool(c, "o"), TR.IsolatePair((m & 1) != 0, (m & 2) != 0, (m & 4) != 0)); }
        foreach (var c in Cases("furn")) { int m = Int(c, "m"); Assert.Equal(Bool(c, "o"), TownFurniturePolicy.Occupied((m & 1) != 0, (m & 2) != 0, (m & 4) != 0)); }
        foreach (var c in Cases("aft"))
        {
            float e = F(c, "e");
            float o = TR.AdvanceAftercare(e, F(c, "dt"), Bool(c, "p"));
            GoldenFile.Near(F(c, "o"), o, "AdvanceAftercare");
            Assert.Equal(Bool(c, "x"), TR.AftercareExpired(e));
            Assert.Equal(Bool(c, "xo"), TR.AftercareExpired(o));
        }
        foreach (var c in Cases("rdy"))
        {
            uint m = UInt(c, "m");
            var r = new TR.Readiness
            {
                Valid = Bool(c, "v"), Player = (m & 2) != 0, Dead = (m & 4) != 0, Unconscious = (m & 8) != 0, InCombat = (m & 16) != 0,
                NeedsAid = (m & 32) != 0, MinimumHealth = F(c, "h"), Blood = F(c, "b"),
            };
            Assert.Equal(Bool(c, "cp"), TR.CanParticipate(r));
            Assert.Equal(Bool(c, "ca"), TR.CanProvideAid(r, Bool(c, "able")));
        }
    }

    [Fact]
    public void Ids_and_catalog_match()
    {
        foreach (var c in Cases("ids"))
        {
            string? id = c.GetProperty("id").ValueKind == JsonValueKind.Null ? null : Str(c, "id");
            Assert.Equal(Bool(c, "eq"), TR.IdEquals(id, id == null ? "x" : "96-Proving Grounds.mod"));
            Assert.Equal(Bool(c, "na"), TR.IsNewArena(id));
            Assert.Equal(Bool(c, "tr"), TR.IsTownRegistry(id));
            Assert.Equal(Bool(c, "rf"), TR.IsRegularFighter(id));
            Assert.Equal(Bool(c, "md"), TR.IsMedic(id));
            var entry = TownFighterCatalog.Find(id);
            var cat = c.GetProperty("cat");
            if (cat.ValueKind == JsonValueKind.Null) Assert.Null(entry);
            else
            {
                Assert.NotNull(entry);
                Assert.Equal(Str(cat, "c"), entry!.CharacterId);
                Assert.Equal(Str(cat, "s"), entry.SquadId);
                Assert.Equal(Str(cat, "t"), entry.Type);
                Assert.Equal(Int(cat, "tier"), entry.Tier);
                Assert.Equal(Int(cat, "n"), entry.Count);
            }
        }
    }

    sealed class Dummy { public int Index; }

    [Fact]
    public void DiscoveryIndex_matches()
    {
        var dummies = Enumerable.Range(0, 5).Select(i => new Dummy { Index = i }).ToArray();
        foreach (var c in Cases("didx"))
        {
            var index = new TR.DiscoveryIndex<Dummy>();
            foreach (var st in c.GetProperty("s").EnumerateArray())
            {
                int a = Int(st, "a");
                Assert.Equal(Bool(st, "ok"), index.Add(a < 0 ? null : dummies[a], Str(st, "key")));
                Assert.Equal(st.GetProperty("actors").EnumerateArray().Select(x => x.GetInt32()), index.Actors.Select(d => d.Index));
                var by = st.GetProperty("by").EnumerateArray().Select(p => (p[0].GetString()!, p[1].GetInt32())).ToList();
                Assert.Equal(by, index.ByIdentity.Select(p => (p.Key, p.Value == null ? -1 : p.Value.Index)).ToList());
            }
        }
    }

    [Fact]
    public void RosterSelection_matches()
    {
        foreach (var c in Cases("rost"))
        {
            var team = new List<string>();
            foreach (var st in c.GetProperty("s").EnumerateArray())
            {
                int op = Int(st, "op"); string id = Str(st, "id"); bool ready = Bool(st, "ready"), locked = Bool(st, "locked");
                int ok = -1;
                if (op == 0) ok = TownRosterSelection.Add(team, id, ready, locked) ? 1 : 0;
                else if (op == 1) ok = TownRosterSelection.Remove(team, id, locked) ? 1 : 0;
                else if (op == 2) ok = TownRosterSelection.Toggle(team, id, ready, locked) ? 1 : 0;
                else TownRosterSelection.Prune(team, st.GetProperty("roster").EnumerateArray().Select(x => x.GetString()!).ToList());
                Assert.Equal(Int(st, "ok"), ok);
                Assert.Equal(st.GetProperty("team").EnumerateArray().Select(x => x.GetString()!), team);
            }
        }
    }
}
