using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownChallengePolicy;
using TR = ProvingGrounds.Core.TownArenaRuntimePolicy;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for what the golden vectors (Tests/Golden/challenge.json) cannot reach: the port's own UTF-8 rule, the
/// iostream reader on inputs the generator does not produce, value semantics of Offer/Card, and the generic runtime helpers.</summary>
public class TownChallengePolicyHandTests
{
    [Fact]
    public void Unhex_refuses_bytes_that_are_not_UTF8()
    {
        // The C++ returns true and stores the raw bytes; a C# string cannot hold them (docs/divergences.md).
        string r = "junk";
        Assert.False(Unhex("ff", ref r));
        Assert.False(Unhex("c3", ref r));                 // cut sequence
        Assert.False(Unhex("c0af", ref r));               // overlong '/'
        Assert.False(Unhex("eda080", ref r));             // UTF-16 surrogate D800
        Assert.True(Unhex("c3a9", ref r)); Assert.Equal("é", r);
        Assert.True(Unhex("00", ref r)); Assert.Equal("\0", r);   // NUL is valid UTF-8; std::string holds it too
    }

    [Fact]
    public void Hex_writes_UTF8_bytes()
    {
        Assert.Equal("-", Hex(""));
        Assert.Equal("c3a9", Hex("é"));
        Assert.Equal("e5908de5898d", Hex("名前"));
        Assert.Equal("610062", Hex("a\0b"));
    }

    [Fact]
    public void A_decode_that_meets_invalid_UTF8_fails_and_leaves_the_card()
    {
        var card = new Card();
        string text = EncodeLegacy(new Card()).Replace("5 1 0 0 0 0 0 0 0 0 -2 0 0 - -", "5 1 0 0 0 0 0 0 0 0 -2 0 0 ff -");
        Assert.NotEqual(EncodeLegacy(new Card()), text);
        card.Seed = 99;
        Assert.False(DecodeLegacy(text, card));
        Assert.Equal(99u, card.Seed);
    }

    [Fact]
    public void Stream_skips_every_C_whitespace_and_reads_words()
    {
        var s = new CStream(" \t\n\v\f\r12\r\nabc");
        Assert.True(s.Int(out int v)); Assert.Equal(12, v);
        Assert.True(s.Word(out string w)); Assert.Equal("abc", w);
        Assert.False(s.Word(out _));       // end of text
        Assert.False(s.Int(out _));        // stays failed
    }

    [Fact]
    public void Stream_int_reads_digits_only_and_fails_on_overflow()
    {
        var s = new CStream("1e3 +7 -2147483648 2147483648");
        Assert.True(s.Int(out int a)); Assert.Equal(1, a);          // stops at 'e'
        Assert.True(s.Word(out string rest)); Assert.Equal("e3", rest);
        Assert.True(s.Int(out int b)); Assert.Equal(7, b);
        Assert.True(s.Int(out int c)); Assert.Equal(int.MinValue, c);
        Assert.False(s.Int(out _));
        Assert.False(new CStream("x").Int(out _));
        Assert.False(new CStream("-").Int(out _));
        Assert.False(new CStream("").Int(out _));
    }

    [Fact]
    public void Stream_unsigned_wraps_a_minus_sign_like_libstdcxx()
    {
        Assert.True(new CStream("-1").UInt(out uint a)); Assert.Equal(4294967295u, a);
        Assert.True(new CStream("4294967295").UInt(out uint b)); Assert.Equal(4294967295u, b);
        Assert.False(new CStream("4294967296").UInt(out _));
        Assert.False(new CStream("-4294967296").UInt(out _));
    }

    [Fact]
    public void Stream_bool_takes_0_and_1_only()
    {
        Assert.True(new CStream("1").Bool(out bool t)); Assert.True(t);
        Assert.True(new CStream("0").Bool(out bool f)); Assert.False(f);
        Assert.True(new CStream("01").Bool(out bool t2)); Assert.True(t2);   // read as the long 1
        Assert.True(new CStream("-0").Bool(out bool f2)); Assert.False(f2);
        Assert.False(new CStream("2").Bool(out _));
        Assert.False(new CStream("-1").Bool(out _));
        Assert.False(new CStream("true").Bool(out _));
    }

    [Fact]
    public void Stream_double_collects_like_num_get()
    {
        Assert.True(new CStream("1.5e3").Double(out double a)); Assert.Equal(1500.0, a);
        Assert.True(new CStream(".5").Double(out double b)); Assert.Equal(0.5, b);
        Assert.True(new CStream("5.").Double(out double c)); Assert.Equal(5.0, c);
        var s = new CStream("2.5.5");
        Assert.True(s.Double(out double d)); Assert.Equal(2.5, d);
        Assert.True(s.Word(out string rest)); Assert.Equal(".5", rest);
        Assert.False(new CStream("1e").Double(out _));
        Assert.False(new CStream("1e+").Double(out _));
        Assert.False(new CStream(".").Double(out _));
        Assert.False(new CStream("e5").Double(out _));
        Assert.False(new CStream("1e400").Double(out _));   // overflow
        Assert.False(new CStream("nan").Double(out _));
        Assert.False(new CStream("inf").Double(out _));
    }

    [Fact]
    public void Decode_with_the_marker_right_after_the_version_takes_the_rest()
    {
        // value.substr(2, marker - 2) with marker 1: the size_t count wraps and substr reads to the end ("MM ..."), which is no card.
        var card = new Card();
        Assert.False(Decode("8 MM 0 1", card));
    }

    [Fact]
    public void A_default_offer_is_the_CXX_one_and_default_struct_is_not()
    {
        var o = new Offer();
        Assert.Equal(-2, o.Epoch);
        Assert.Equal("", o.Opponent);
        Assert.Equal("", o.Roles[3]);
        Offer d = default;               // never used by the port: shows why `new Offer()` is required
        Assert.Equal(0, d.Epoch);
        Assert.Null(d.Opponent);
        var card = new Card();
        for (int i = 0; i < 5; ++i) Assert.Equal(-2, card.Offers[i].Epoch);
        Assert.Equal(-2, card.SkarnOffer.Epoch);
    }

    [Fact]
    public void Offer_and_card_copies_are_independent()
    {
        var card = new Card();
        card.PlayerHistory.Add(new List<string> { "a" });
        card.Offers[0].Roles[0] = "r";
        Card copy = card.Clone();
        copy.PlayerHistory[0][0] = "changed";
        copy.Offers[0].Roles[0] = "changed";
        Assert.Equal("a", card.PlayerHistory[0][0]);
        Assert.Equal("r", card.Offers[0].Roles[0]);
        Offer source = PreviewSource(card, 0);
        source.Roles[0] = "x";
        Assert.Equal("r", card.Offers[0].Roles[0]);
        ref Offer at = ref At(card, 5);
        at.Epoch = 77;
        Assert.Equal(77, card.SkarnOffer.Epoch);
        ref string id = ref OpponentId(ref card.Offers[1], 2);
        id = "third";
        Assert.Equal("third", card.Offers[1].ThirdOpponent);
    }

    [Fact]
    public void ReadHistory_appends_to_what_is_there()
    {
        var history = new List<List<string>> { new() { "pre" } };
        var s = new CStream(" 1 2 61 62 tail");
        Assert.True(ReadHistory(s, history));
        Assert.Equal(2, history.Count);
        Assert.Equal(new[] { "a", "b" }, history[1]);
        Assert.Equal(" tail", s.Rest);
    }

    sealed class Prep : TR.IPreparation
    {
        public bool IsReady; public int Cancels;
        public bool Ready() => IsReady;
        public void Cancel() => ++Cancels;
    }

    struct PrepValue : TR.IPreparation
    {
        public int Cancels;
        public bool Ready() => false;
        public void Cancel() => ++Cancels;
    }

    [Fact]   // TownArenaRuntimePolicy.h:88-93
    public void GuardPreparation_cancels_only_an_unready_npc_preparation()
    {
        var p = new Prep { IsReady = false };
        Assert.True(TR.GuardPreparation(false, ref p)); Assert.Equal(0, p.Cancels);
        Assert.False(TR.GuardPreparation(true, ref p)); Assert.Equal(1, p.Cancels);
        p.IsReady = true;
        Assert.True(TR.GuardPreparation(true, ref p)); Assert.Equal(1, p.Cancels);
        var v = new PrepValue();
        Assert.False(TR.GuardPreparation(true, ref v));
        Assert.Equal(1, v.Cancels);   // by reference, as the C++ Preparation&
    }

    sealed class Actor { }

    [Fact]   // TownArenaRuntimePolicy.h:23-36
    public void DiscoveryIndex_marks_a_shared_key_ambiguous()
    {
        var index = new TR.DiscoveryIndex<Actor>();
        Actor a = new(), b = new();
        Assert.False(index.Add(null, "k"));
        Assert.True(index.Add(a, "k"));
        Assert.False(index.Add(a, "other"));   // same actor again
        Assert.True(index.Add(b, "k"));
        Assert.Null(index.ByIdentity["k"]);
        Assert.True(index.Add(new Actor(), ""));  // empty key: listed, not indexed
        Assert.Equal(3, index.Actors.Count);
        Assert.Single(index.ByIdentity);
    }

    // ---- guard: Card.Clone / AssignFrom copy every field (a new field must be added to both) ----
    const System.Reflection.BindingFlags Inst =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    static Offer DistinctOffer(int seed)
    {
        object boxed = new Offer();
        int k = 0;
        foreach (var f in typeof(Offer).GetFields(Inst))
        {
            ++k;
            if (f.FieldType == typeof(int)) f.SetValue(boxed, seed * 100 + k);
            else if (f.FieldType == typeof(uint)) f.SetValue(boxed, (uint)(seed * 100 + k));
            else if (f.FieldType == typeof(bool)) f.SetValue(boxed, true);
            else if (f.FieldType == typeof(string)) f.SetValue(boxed, $"s{seed}_{k}");
            else if (f.FieldType == typeof(TownArenaPolicy.OfferState)) f.SetValue(boxed, TownArenaPolicy.OfferState.Consumed);
            else if (f.FieldType != typeof(RoleIds)) Assert.Fail($"Offer field {f.Name} of type {f.FieldType} not handled: update this test");
        }
        var o = (Offer)boxed;
        for (int r = 0; r < 4; ++r) o.Roles[r] = $"role{seed}_{r}";
        return o;
    }

    static void AssertOfferFieldsEqual(Offer expected, Offer actual, string what)
    {
        foreach (var f in typeof(Offer).GetFields(Inst))
            if (f.FieldType != typeof(RoleIds)) Assert.True(Equals(f.GetValue(expected), f.GetValue(actual)), $"{what}.{f.Name}");
        for (int r = 0; r < 4; ++r) Assert.True(expected.Roles[r] == actual.Roles[r], $"{what}.Roles[{r}]");
    }

    static Card DistinctCard()
    {
        var card = new Card();
        int k = 0;
        foreach (var f in typeof(Card).GetFields(Inst))
        {
            ++k;
            if (f.FieldType == typeof(uint)) f.SetValue(card, (uint)(1000 + k));
            else if (f.FieldType == typeof(int)) f.SetValue(card, 1000 + k);
            else if (f.FieldType == typeof(double)) f.SetValue(card, 1000.5 + k);
            else if (f.FieldType == typeof(bool)) f.SetValue(card, true);
            else if (f.FieldType == typeof(List<List<string>>)) ((List<List<string>>)f.GetValue(card)!).Add(new List<string> { $"bout{k}" });
            else if (f.FieldType == typeof(Offer[])) { var a = (Offer[])f.GetValue(card)!; for (int i = 0; i < a.Length; ++i) a[i] = DistinctOffer(k * 10 + i); }
            else if (f.FieldType == typeof(Offer)) f.SetValue(card, DistinctOffer(k));
            else Assert.Fail($"Card field {f.Name} of type {f.FieldType} not handled: update this test");
        }
        return card;
    }

    static void AssertCardCopied(Card source, Card copy, string what)
    {
        foreach (var f in typeof(Card).GetFields(Inst))
        {
            object? a = f.GetValue(source), b = f.GetValue(copy);
            if (a is List<List<string>> la)
            {
                var lb = (List<List<string>>)b!;
                Assert.False(ReferenceEquals(la, lb), $"{what}: {f.Name} shares its list");
                Assert.Equal(la.Count, lb.Count);
                for (int i = 0; i < la.Count; ++i)
                {
                    Assert.False(ReferenceEquals(la[i], lb[i]), $"{what}: {f.Name}[{i}] shares its bout");
                    Assert.Equal(la[i], lb[i]);
                }
            }
            else if (a is Offer[] oa)
            {
                var ob = (Offer[])b!;
                Assert.False(ReferenceEquals(oa, ob), $"{what}: {f.Name} shares its array");
                for (int i = 0; i < oa.Length; ++i) AssertOfferFieldsEqual(oa[i], ob[i], $"{what}: {f.Name}[{i}]");
            }
            else if (a is Offer o) AssertOfferFieldsEqual(o, (Offer)b!, $"{what}: {f.Name}");
            else Assert.True(Equals(a, b), $"{what}: {f.Name}");
        }
    }

    [Fact]
    public void Card_Clone_and_AssignFrom_copy_every_field()
    {
        // C++ Card (TownChallengePolicy.h:117-136) has 19 members; the C# card mirrors them one for one.
        Assert.True(typeof(Card).GetFields(Inst).Length == 19, "Card gained or lost a field: update Clone/AssignFrom (and this count)");
        Card source = DistinctCard();
        Assert.False(source.SkarnLastEnd == new Card().SkarnLastEnd);   // the values really differ from the defaults
        AssertCardCopied(source, source.Clone(), "Clone");
        var target = new Card();
        var playerHistory = target.PlayerHistory;
        target.AssignFrom(source);
        AssertCardCopied(source, target, "AssignFrom");
        Assert.Same(playerHistory, target.PlayerHistory);   // `card = parsed` keeps the same vectors
    }
}
