using System.Text.Json;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using ProvingGrounds.Tests.Glue;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Task 1.13b's pure TownArena.cpp parts against Tests/Golden/townarena.json (golden/gen_townarena.cpp: verbatim copies over
/// plain values, linked with the real policy headers; its header lists the case kinds). Strings, ints, flags exactly; doubles and floats
/// bit for bit.</summary>
public class TownArenaGoldenTests
{
    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("townarena"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    static int Int(JsonElement c, string n) => c.GetProperty(n).GetInt32();
    static uint U(JsonElement c, string n) => c.GetProperty(n).GetUInt32();
    static bool Flag(JsonElement c, string n) => c.GetProperty(n).GetInt32() != 0;
    static string Str(JsonElement c, string n) => c.GetProperty(n).GetString()!;
    // a double's bits: the generator prints them as unsigned (%llu) or as signed (I((long long)Bits(d)))
    static double D(JsonElement e) => BitConverter.Int64BitsToDouble(e.TryGetInt64(out long b) ? b : unchecked((long)e.GetUInt64()));
    static double D(JsonElement c, string n) => D(c.GetProperty(n));
    static float F(JsonElement c, string n) => BitConverter.Int32BitsToSingle(unchecked((int)c.GetProperty(n).GetUInt32()));
    static void Same(double expected, double actual, JsonElement c, string what)
    {
        if (BitConverter.DoubleToInt64Bits(expected) != BitConverter.DoubleToInt64Bits(actual))
            Assert.Fail($"{what}: expected {expected:R}, actual {actual:R}; case {c.GetRawText()}");
    }
    static void Eq<T>(T expected, T actual, JsonElement c, string field)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Assert.Fail($"{field}: expected {expected}, actual {actual}; case {c.GetRawText()}");
    }
    static List<string> Strings(JsonElement a) => a.EnumerateArray().Select(e => e.GetString()!).ToList();

    static TownArena.Actor ReadActor(JsonElement a)
    {
        var flags = a.GetProperty("flags"); var state = a.GetProperty("state");
        return new TownArena.Actor
        {
            Valid = Flag(a, "valid"), InstanceUid = Flag(a, "inst") ? Str(a, "uid") : null, StringId = Flag(a, "data") ? Str(a, "sid") : null,
            Name = Str(a, "name"), Identity = Str(a, "identity"), Quarantined = Flag(a, "quar"), PlayerCharacter = Flag(a, "pc"),
            Dead = flags[0].GetInt32() != 0, Unconscious = flags[1].GetInt32() != 0, InCombat = flags[2].GetInt32() != 0,
            NeedsAid = flags[3].GetInt32() != 0, RosterOk = Flag(a, "rok"),
            Health = new RosterStatus.Snapshot { LowestLimb = F(a, "lowest"), Blood = F(a, "blood") },
            RosterError = Str(a, "rerr"), MedicallyAble = Flag(a, "able"),
            InAftercare = state[0].GetInt32() != 0, InLimbShop = state[1].GetInt32() != 0, Fighter = state[2].GetInt32() != 0,
            Ability = D(a, "ability"), TownRating = F(a, "rating"), SelectionIdentity = Str(a, "sel"), InRegistryTown = Flag(a, "town"),
        };
    }

    static TownChallengePolicy.Offer ReadOffer(JsonElement o)
    {
        var r = new TownChallengePolicy.Offer
        {
            Epoch = Int(o, "epoch"), Encounter = Int(o, "enc"), Format = Int(o, "format"), Mode = Int(o, "mode"),
            State = (TownArenaPolicy.OfferState)Int(o, "state"), Generated = Flag(o, "gen"), Draw = U(o, "draw"), Division = Int(o, "div"),
            PlayerCount = Int(o, "pc"), EnemyCount = Int(o, "ec"), Context = Str(o, "ctx"),
        };
        var ids = Strings(o.GetProperty("ids")); var names = Strings(o.GetProperty("names")); var roles = Strings(o.GetProperty("roles"));
        for (int i = 0; i < 4; ++i)
        {
            TownChallengePolicy.OpponentId(ref r, i) = ids[i];
            TownChallengePolicy.OpponentName(ref r, i) = names[i];
            r.Roles[i] = roles[i];
        }
        return r;
    }

    static void SameOffer(JsonElement e, in TownChallengePolicy.Offer a, JsonElement c, string what)
    {
        TownChallengePolicy.Offer x = ReadOffer(e);
        Eq(x.Epoch, a.Epoch, c, what + " epoch"); Eq(x.Encounter, a.Encounter, c, what + " encounter"); Eq(x.Format, a.Format, c, what + " format");
        Eq(x.Mode, a.Mode, c, what + " mode"); Eq(x.State, a.State, c, what + " state"); Eq(x.Generated, a.Generated, c, what + " generated");
        Eq(x.Draw, a.Draw, c, what + " draw"); Eq(x.Division, a.Division, c, what + " division"); Eq(x.PlayerCount, a.PlayerCount, c, what + " pc");
        Eq(x.EnemyCount, a.EnemyCount, c, what + " ec"); Eq(x.Context, a.Context, c, what + " context");
        for (int i = 0; i < 4; ++i)
        {
            Eq(TownChallengePolicy.OpponentIdConst(x, i), TownChallengePolicy.OpponentIdConst(a, i), c, $"{what} id{i}");
            Eq(TownChallengePolicy.OpponentNameConst(x, i), TownChallengePolicy.OpponentNameConst(a, i), c, $"{what} name{i}");
            Eq(x.Roles[i], a.Roles[i], c, $"{what} role{i}");
        }
    }

    static List<TownMatchmakingPolicy.Fighter> ReadFighters(JsonElement a) =>
        a.EnumerateArray().Select(f => new TownMatchmakingPolicy.Fighter
            { Id = f[0].GetString()!, Role = f[1].GetString()!, Ability = D(f[2]), Mmr = D(f[3]) }).ToList();

    static TownArena.MatchmakingSnapshot ReadSnapshot(JsonElement s)
    {
        var actors = s.GetProperty("actors").EnumerateArray().Select(ReadActor).ToList();
        return new TownArena.MatchmakingSnapshot
        {
            PlayersReady = Flag(s, "ready"), PlayerBlocker = Str(s, "blocker"), Players = ReadFighters(s.GetProperty("players")),
            Npcs = ReadFighters(s.GetProperty("npcs")), MedicCount = Int(s, "medics"), Availability = Str(s, "avail"),
            Characters = s.GetProperty("chars").EnumerateArray().Select(i => i.GetInt32() < 0 ? null : actors[i.GetInt32()]).ToList(),
        };
    }

    static TownChallengePolicy.Card ReadCard(JsonElement c)
    {
        var card = new TownChallengePolicy.Card
        {
            Seed = U(c, "seed"), AmbientSeed = U(c, "aseed"), Division = Int(c, "div"), UniqueWins = U(c, "uw"), ChallengeWins = U(c, "cw"),
            SkarnWon = Flag(c, "skarnWon"), SkarnLastEnd = D(c, "skarnEnd"), PaidRefreshCount = Int(c, "prc"), PaidRefreshNonce = U(c, "nonce"),
        };
        int i = 0;
        foreach (JsonElement o in c.GetProperty("offers").EnumerateArray()) card.Offers[i++] = ReadOffer(o);
        card.SkarnOffer = ReadOffer(c.GetProperty("skarn"));
        return card;
    }

    static TownChallengeBuyInPolicy.Quote Quote(JsonElement q) => new() { BuyIn = q[0].GetInt32(), Payout = q[1].GetInt32() };

    static readonly int[][] Bases = { new[] { 100, 250, 500 }, new[] { 120, 300, 800 } };

    [Fact]
    public void GoldenHasEveryCase()
    {
        var expected = new Dictionary<string, int>
        {
            ["ready"] = 500, ["quote"] = 1300, ["readiness"] = 300, ["mode"] = 5, ["refresh"] = 300, ["card"] = 400, ["direct"] = 120,
            ["progress"] = 120, ["lineup"] = 80, ["pkey"] = 200, ["savekey"] = 200, ["paid"] = 200, ["search"] = 200, ["plan"] = 200,
            ["result"] = 150, ["marks"] = 200, ["canbuy"] = 120, ["msg"] = 1090, ["const"] = 54,
        };
        Assert.Equal(expected.Values.Sum(), All.Value.GetArrayLength());
        var actual = All.Value.EnumerateArray().GroupBy(c => c.GetProperty("k").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(expected.OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
    }

    [Fact]
    public void ReadyMatchesTheOriginal()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        foreach (JsonElement c in Cases("ready"))
        {
            JsonElement a = c.GetProperty("actor");
            TownArena.Actor? actor = a.ValueKind == JsonValueKind.Null ? null : ReadActor(a);
            bool ready = TownArena.Ready(actor, Flag(c, "doctor"), Flag(c, "booked"), Flag(c, "ui"), out string reason, out string? detail);
            Eq(Flag(c, "out"), ready, c, "ready");
            if (Flag(c, "withReason")) Eq(Str(c, "reason"), reason, c, "reason");
            JsonElement d = c.GetProperty("detail");
            Eq(d.ValueKind == JsonValueKind.Null ? null : d.GetString(), detail, c, "detail");
        }
    }

    [Fact]
    public void NamedPreviewAndQuoteMatchTheOriginal()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        foreach (JsonElement c in Cases("quote"))
        {
            TownChallengePolicy.Offer offer = ReadOffer(c.GetProperty("offer"));
            TownArena.MatchmakingSnapshot s = ReadSnapshot(c.GetProperty("snap"));
            bool ui = Flag(c, "ui");
            int[] b = Bases[Int(c, "bi")];
            using var _ = PGConfig.UseChallengeBaseForTest(b[0], b[1], b[2]);
            Eq(Str(c, "named"), TownArena.NamedOpponentUnavailableReason(offer, s, ui), c, "named");
            Eq(Str(c, "preview"), TownArena.PreviewUnavailableReason(offer, s, ui), c, "preview");
            double own = -7.5, enemy = -7.5;
            TownChallengeBuyInPolicy.Quote q = TownArena.QuoteFor(offer, s, Int(c, "division"), ui, ref own, ref enemy);
            JsonElement eq = c.GetProperty("quote");
            Eq(eq[0].GetInt32(), q.BuyIn, c, "buyIn"); Eq(eq[1].GetInt32(), q.Payout, c, "payout");
            Same(D(c, "own"), own, c, "own score"); Same(D(c, "enemy"), enemy, c, "enemy score");
            Eq(false, Flag(c, "logged"), c, "the named reason's Ready is quiet");
        }
    }

    /// <summary>Task 7.5 review: the glue's copies of NamedOpponentUnavailableReason, PreviewUnavailableReason and QuoteFor (TownArenaLogic.Cards.cs,
    /// over the host and a TownRosterSnapshot of TIds) give Core's answers (over Actors) on every golden "quote" case. Each Actor becomes one fake
    /// character (its index; a null entry the host's None), with the same reads.</summary>
    [Fact]
    public void GlueCopiesGiveCoresAnswers()
    {
        using var glibc = CMath.UseGlibcForTest();
        int n = 0, noData = 0;
        foreach (JsonElement c in Cases("quote"))
        {
            TownChallengePolicy.Offer offer = ReadOffer(c.GetProperty("offer"));
            TownArena.MatchmakingSnapshot cs = ReadSnapshot(c.GetProperty("snap"));
            bool ui = Flag(c, "ui");
            // The C++'s Id(c) is NULL for a character without game data, which the glue cannot see (every Meitou character has a record id; the
            // host's Role is a string). Only an offer whose role is "" (an encounter out of range, never generated) tells NULL from "" apart:
            // those cases are counted and left out, nothing else.
            if (TownChallengePolicy.Role(offer.Encounter).Length == 0 && cs.Characters.Any(a => a != null && (a.StringId == null || a.StringId.Length == 0)))
            { ++noData; continue; }
            int[] b = Bases[Int(c, "bi")];
            using var _ = PGConfig.UseChallengeBaseForTest(b[0], b[1], b[2]);
            // The snapshot's Characters hold the same Actor objects (ReadSnapshot), read again here so this test can align them: the generator gives
            // some invalid actors an identity, which TownMatchmakingRuntime::Identity never returns for an invalid character (TownMatchmakingRuntime.cpp:21-25,
            // the glue's Identity); Core is fed the identity the C++ would read ("").
            var all = c.GetProperty("snap").GetProperty("actors").EnumerateArray().Count();
            var byIndex = new TownArena.Actor?[all];
            int ci = 0;
            foreach (JsonElement e in c.GetProperty("snap").GetProperty("chars").EnumerateArray()) { int x = e.GetInt32(); if (x >= 0) byIndex[x] = cs.Characters[ci]; ++ci; }
            var actors = byIndex.Select((a, i) => a ?? ReadActor(c.GetProperty("snap").GetProperty("actors")[i])).ToList();
            foreach (var a in actors) if (!a.Valid) a.Identity = "";
            var f = new TownFake { Players = ui ? [] : null };
            var l = new TownArenaLogic<int>(TownFake.NoCharacter);
            f.Logic = l;
            int fighters = 0;
            for (int i = 0; i < actors.Count; ++i)
            {
                TownArena.Actor a = actors[i];
                if (!a.Valid) f.Invalid.Add(i);
                f.UidOf[i] = a.InstanceUid ?? "";
                f.RoleOf[i] = a.StringId ?? "";
                f.NameOf[i] = a.Name;
                if (a.Identity.Length != 0) { f.TokenOf[i] = a.Identity; f.Created.Add(a.Identity); } else f.NoIdentity.Add(i);
                if (a.Quarantined) l.State.IngressQuarantine.Add(i);
                if (a.PlayerCharacter) f.Players?.Add(i);
                if (a.Dead) f.DeadSet.Add(i);
                if (a.Unconscious) f.KoSet.Add(i);
                if (a.InCombat) f.Fighting.Add(i);
                if (a.NeedsAid) f.NeedsAid.Add(i);
                if (!a.RosterOk) { f.NoHealth.Add(i); f.HealthError[i] = a.RosterError; }
                f.Health[i] = (a.Health.LowestLimb, a.Health.Blood);
                if (!a.MedicallyAble) f.NotMedicallyAble.Add(i);
                if (a.InAftercare) f.Aftercare.Add(i);
                if (a.InLimbShop) f.InLimbShop.Add(i);
                if (a.Fighter) l.State.Fighters[fighters++] = i;   // TownArena::IsFighter: a fighter of the running event
                f.AbilityOf[i] = a.Ability;
                f.RatingOf[i] = a.TownRating;
            }
            l.State.FighterCount = fighters;
            l.State.Phase = fighters > 0 ? TownPhase.Preparing : TownPhase.Idle;
            var gs = new TownRosterSnapshot<int>
            {
                Players = cs.Players, Npcs = cs.Npcs, PlayersReady = cs.PlayersReady, PlayerBlocker = cs.PlayerBlocker, Availability = cs.Availability,
            };
            for (int i = 0; i < cs.MedicCount; ++i) gs.Medics.Add(10_000 + i);
            int k = 0;
            foreach (JsonElement e in c.GetProperty("snap").GetProperty("chars").EnumerateArray())
            {
                int index = e.GetInt32();
                gs.Characters["k" + (k++).ToString("D5", System.Globalization.CultureInfo.InvariantCulture)] =
                    index < 0 ? new TownRosterSnapshot<int>.Entry(false, TownFake.NoCharacter) : new TownRosterSnapshot<int>.Entry(true, index);
            }
            Eq(TownArena.NamedOpponentUnavailableReason(offer, cs, ui), l.NamedOpponentUnavailableReason(f, offer, gs), c, "glue named");
            Eq(TownArena.PreviewUnavailableReason(offer, cs, ui), l.PreviewUnavailableReason(f, offer, gs), c, "glue preview");
            double own = -7.5, enemy = -7.5;
            TownChallengeBuyInPolicy.Quote core = TownArena.QuoteFor(offer, cs, Int(c, "division"), ui, ref own, ref enemy);
            TownChallengeBuyInPolicy.Quote glue = l.QuoteFor(f, offer, gs, Int(c, "division"), out double gown, out double genemy);
            Eq(core.BuyIn, glue.BuyIn, c, "glue buyIn"); Eq(core.Payout, glue.Payout, c, "glue payout");
            if (own != -7.5) { Same(own, gown, c, "glue own score"); Same(enemy, genemy, c, "glue enemy score"); }   // the glue's outs start at 0 (its callers do)
            ++n;
        }
        Assert.Equal(1300, n + noData);
        Assert.Equal(87, noData);   // measured: the out-of-range encounters that meet a data-less (or empty-id) actor
    }

    [Fact]
    public void PlayerReadinessMatchesTheOriginal()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        foreach (JsonElement c in Cases("readiness"))
        {
            bool ok = TownArena.PlayerReadiness(ReadActor(c.GetProperty("actor")), Flag(c, "ui"), out string reason);
            Eq(Flag(c, "out"), ok, c, "ok"); Eq(Str(c, "reason"), reason, c, "reason");
            Eq(false, Flag(c, "logged"), c, "quiet");
        }
    }

    [Fact]
    public void MatchModeAndRefreshCaptionMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("mode"))
            Eq(Int(c, "out"), (int)TownArena.MatchModeFor(new TownChallengePolicy.Offer { Mode = Int(c, "mode") }), c, "mode");
        foreach (JsonElement c in Cases("refresh"))
            Eq(Str(c, "text"), TownArena.NextChallengeRefreshCaption(Flag(c, "ou"), D(c, "hours"), Int(c, "refresh")), c, "caption");
    }

    static TownArena.DisplayState ReadState(JsonElement s)
    {
        var st = new TownArena.DisplayState
        {
            QueuedChallenge = Int(s, "queued"), ActiveChallenge = Int(s, "active"), BookingPending = Flag(s, "pending"),
            ChallengeReservation = ReadOffer(s.GetProperty("reservation")),
        };
        int i = 0;
        foreach (JsonElement o in s.GetProperty("previews").EnumerateArray()) st.Previews[i++] = ReadOffer(o);
        var reasons = Strings(s.GetProperty("reasons"));
        for (i = 0; i < 6; ++i) st.UnavailableReasons[i] = reasons[i];
        st.BuyInTicket.Quote = Quote(s.GetProperty("ticket"));
        i = 0;
        foreach (JsonElement q in s.GetProperty("quotes").EnumerateArray()) st.BuyInQuotes[i++] = Quote(q);
        return st;
    }

    [Fact]
    public void CardCaptionsAndPortraitsMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("card"))
        {
            TownChallengePolicy.Card card = ReadCard(c.GetProperty("card"));
            TownArena.DisplayState s = ReadState(c.GetProperty("state"));
            foreach (JsonElement e in c.GetProperty("slots").EnumerateArray())
            {
                int slot = Int(e, "slot");
                string w = $"slot {slot}";
                Eq(Int(e, "rarity"), TownArena.ChallengeRarity(card, s, slot), c, w + " rarity");
                Eq(Flag(e, "unique"), TownArena.IsUniqueLegendaryChallenge(card, s, slot), c, w + " unique");
                Eq(Str(e, "unavail"), TownArena.ChallengeUnavailableReason(card, s, slot), c, w + " unavailable");
                Eq(Str(e, "caption"), TownArena.ChallengeCaption(card, s, slot, Flag(e, "lineup")), c, w + " caption");
                TownChallengeBuyInPolicy.Quote q = TownArena.ChallengeBuyIn(card, s, slot);
                JsonElement eq = e.GetProperty("buyin");
                Eq(eq[0].GetInt32(), q.BuyIn, c, w + " buyIn"); Eq(eq[1].GetInt32(), q.Payout, c, w + " payout");
                if (slot >= 0 && slot < 5)
                {
                    TownChallengePolicy.Offer d = TownArena.DisplayedOffer(card, s, slot);
                    SameOffer(e.GetProperty("displayed"), d, c, w + " displayed");
                    var names = Strings(e.GetProperty("pnames"));
                    for (int m = 0; m < 4; ++m) Eq(names[m], TownArena.StoredPortraitName(d, m), c, $"{w} portrait name {m}");
                }
            }
            var actors = c.GetProperty("actors").EnumerateArray().Select(ReadActor).ToList();
            TownArena.ChallengePortraits(card, s, Flag(c, "avail"), actors, out List<int>[] opponents, out List<string>[] pnames);
            int slotIndex = 0;
            foreach (JsonElement p in c.GetProperty("portraits").EnumerateArray())
            {
                Eq(string.Join(",", p[0].EnumerateArray().Select(x => x.GetInt32())), string.Join(",", opponents[slotIndex]), c, $"portraits {slotIndex}");
                Eq(string.Join("|", Strings(p[1])), string.Join("|", pnames[slotIndex]), c, $"portrait names {slotIndex}");
                ++slotIndex;
            }
            Assert.Equal(6, slotIndex);
        }
    }

    [Fact]
    public void DirectAndProgressCaptionsMatchTheOriginal()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        foreach (JsonElement c in Cases("direct"))
        {
            var s = new TownArena.DisplayState { BookingPending = Flag(c, "pending"), DirectPreview = ReadOffer(c.GetProperty("offer")) };
            Eq(Str(c, "text"), TownArena.DirectCaption(s, Int(c, "div")), c, "direct");
        }
        foreach (JsonElement c in Cases("progress"))
        {
            var card = new TownChallengePolicy.Card { UniqueWins = U(c, "uw"), ChallengeWins = U(c, "cw") };
            Eq(Str(c, "text"), TownArena.ProgressCaption(card), c, "progress");
        }
    }

    [Fact]
    public void KeysAndSeedsMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("lineup"))
            Eq(Str(c, "key"), TownArena.PlayerLineupKey(Strings(c.GetProperty("ids"))), c, "lineup key");
        foreach (JsonElement c in Cases("pkey"))
            Eq(Str(c, "key"), TownArena.PreviewKey(Str(c, "lineup"), ReadCard(c.GetProperty("card"))), c, "preview key");
        foreach (JsonElement c in Cases("savekey"))
        {
            var card = new TownChallengePolicy.Card { Seed = U(c, "seed") };
            card.Offers[0].Epoch = Int(c, "epoch0");
            TownArena.FoldSaveKey(card, Str(c, "save"));
            Eq(U(c, "out"), card.Seed, c, "seed");
        }
    }

    static void SameCard(JsonElement e, TownChallengePolicy.Card a, JsonElement c, string what)
    {
        TownChallengePolicy.Card x = ReadCard(e);
        Eq(x.Seed, a.Seed, c, what + " seed"); Eq(x.AmbientSeed, a.AmbientSeed, c, what + " ambient seed"); Eq(x.Division, a.Division, c, what + " division");
        Eq(x.PaidRefreshCount, a.PaidRefreshCount, c, what + " paid count"); Eq(x.PaidRefreshNonce, a.PaidRefreshNonce, c, what + " nonce");
        Eq(x.UniqueWins, a.UniqueWins, c, what + " unique wins"); Eq(x.ChallengeWins, a.ChallengeWins, c, what + " challenge wins");
        Eq(x.SkarnWon, a.SkarnWon, c, what + " skarn won"); Same(x.SkarnLastEnd, a.SkarnLastEnd, c, what + " skarn last end");
        int i = 0;
        foreach (JsonElement o in e.GetProperty("offers").EnumerateArray()) { SameOffer(o, a.Offers[i], c, $"{what} offer {i}"); ++i; }
        SameOffer(e.GetProperty("skarn"), a.SkarnOffer, c, what + " skarn");
    }

    [Fact]
    public void PaidRefreshMatchesTheOriginal()
    {
        foreach (JsonElement c in Cases("paid"))
        {
            TownChallengePolicy.Card card = ReadCard(c.GetProperty("card"));
            TownArena.ApplyPaidRefresh(card, Int(c, "epoch"));
            SameCard(c.GetProperty("out"), card, c, "paid");
        }
    }

    [Fact]
    public void SearchRulesMatchTheOriginal()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        foreach (JsonElement c in Cases("search"))
        {
            TownChallengePolicy.Card card = ReadCard(c.GetProperty("card"));
            TownChallengePolicy.Offer offer = ReadOffer(c.GetProperty("offer"));
            TownArena.MatchmakingSnapshot s = ReadSnapshot(c.GetProperty("snap"));
            uint seed = TownArena.SearchRequestSeed(offer, Int(c, "slot"), card, out string role, out bool solo);
            Eq(U(c, "seed"), seed, c, "seed"); Eq(Str(c, "role"), role, c, "role"); Eq(Flag(c, "solo"), solo, c, "solo");
            var match = new TownMatchmakingPolicy.Match();
            if (Flag(c, "weak")) match.Failure = TownMatchmakingPolicy.Match.FailureCode.PlayersTooWeak;
            Eq(Str(c, "failure"), TownArena.SearchFailureReason(match, offer, s, card, true), c, "failure");
            Eq(Str(c, "searching"), TownArena.SearchingReason(s), c, "searching");
            Eq(U(c, "direct"), TownArena.DirectSearchSeed(card), c, "direct seed");
        }
    }

    [Fact]
    public void PlanRulesMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("plan"))
        {
            Eq(Int(c, "capacity"), TownArena.AmbientCapacity(Int(c, "medics"), Flag(c, "aftercare"), Int(c, "fighters")), c, "capacity");
            var card = new TownChallengePolicy.Card { AmbientSeed = U(c, "aseed") };
            Same(D(c, "start"), TownArena.NextNpcStartHours(card, D(c, "now")), c, "start");
        }
    }

    [Fact]
    public void ResultRulesMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("result"))
        {
            var outcome = (SparPodium.OutcomeKind)Int(c, "outcome");
            Eq(Flag(c, "winner"), TownArena.MarksWinner(outcome, (MatchRules.MatchTeam)Int(c, "team")), c, "winner");
            Eq(Int(c, "side"), TownArena.BookieSide(outcome), c, "side");
            Eq(Int(c, "challenge"), TownArena.ChallengeOutcome(outcome), c, "challenge");
            var live = c.GetProperty("live").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? null : x.GetString()).ToList();
            Eq(U(c, "defeated"), TownArena.DefeatedUniques(ReadOffer(c.GetProperty("offer")), live), c, "defeated");
        }
    }

    [Fact]
    public void ActiveMarksMatchTheOriginal()
    {
        foreach (JsonElement c in Cases("marks"))
        {
            PGConfig.Challenges v = PGConfig.ChallengeValues();
            v.WinMarksMultiplier = D(c, "win"); v.UniqueBonusMultiplier = D(c, "unique"); v.SkarnBonusMultiplier = D(c, "skarn");
            using (PGConfig.UseChallengesForTest(v))
            {
                TownArena.ActiveMarks(Flag(c, "player"), Int(c, "div"), Int(c, "active"), Int(c, "rarity"), Int(c, "enc"),
                    out float multiplier, out int bonus);
                Eq(U(c, "mult"), unchecked((uint)BitConverter.SingleToInt32Bits(multiplier)), c, "multiplier");
                Eq(Int(c, "bonus"), bonus, c, "bonus");
            }
        }
    }

    [Fact]
    public void CanBuyRefreshMatchesTheOriginal()
    {
        foreach (JsonElement c in Cases("canbuy"))
        {
            var s = new TownArena.DisplayState { BookingPending = Flag(c, "pending"), QueuedChallenge = Int(c, "queued"), ActiveChallenge = Int(c, "active") };
            Eq(Flag(c, "out"), TownArena.CanBuyChallengeRefresh(Flag(c, "save"), Flag(c, "ou"), Flag(c, "registry"), s, Int(c, "phase"),
                Int(c, "cost"), Int(c, "balance")), c, "can buy");
        }
    }

    static string Built(JsonElement c, out bool? force)
    {
        JsonElement a = c.GetProperty("a");
        string S(int i) => a[i].GetString()!;
        int N(int i) => a[i].GetInt32();
        bool Bf(int i) => a[i].GetInt32() != 0;
        force = null;
        switch (Str(c, "m"))
        {
            case "status":
            {
                string status = "previous status", message = S(0);
                string? log = TownArena.UpdateStatus(ref status, message);
                Assert.True(status == message, "status replaced");
                Assert.Null(TownArena.UpdateStatus(ref status, message));   // the same message again: nothing logged
                return log!;
            }
            case "resolved": return TownArena.OpponentResolvedLog(S(0), S(1));
            case "settle": return TownArena.SettleChallengeMessage(N(0), N(1));
            case "quarantine": return TownArena.QuarantineLog(S(0), S(1));
            case "prepcancel": return TownArena.PreparationCancelMessage(Bf(0), S(1), Bf(2));
            case "refreshcost": return TownArena.RefreshCostMessage(N(0), N(1));
            case "refreshed": return TownArena.RefreshedMessage(N(0), N(1));
            case "searchlog": return TownArena.SearchResolvedLog(N(0), Bf(1));
            case "discovery": return TownArena.MatchmakingDiscoveryLog(S(0));
            case "progresslog": return TownArena.ProgressionLog(S(0));
            case "bookunavail":
            {
                string text = TownArena.BookUnavailableMessage(N(0), (TownArenaPolicy.OfferState)N(1), ReadOffer(a[2]), S(3), out bool f);
                force = f;
                return text;
            }
            case "cooldown": return TownArena.SkarnCooldownMessage(N(0), D(a[1]), D(a[2]));
            case "exactly": return TownArena.SelectExactlyMessage(N(0));
            case "requires": return TownArena.ChallengeRequiresMessage(N(0), N(1));
            case "reserved": return TownArena.ChallengeReservedMessage(S(0), N(1), N(2));
            case "schedule": return TownArena.ScheduleMessage(Bf(0));
            case "slot": return TownArena.SlotReservedMessage(S(0));
            case "direct": return TownArena.DirectReservedMessage(S(0));
            case "underway": return TownArena.UnderwayMessage(Bf(0));
            case "waitingfor": return TownArena.ChallengeWaitingMessage(S(0), N(1));
            case "waitingblocker": return TownArena.ChallengeWaitingMessage(S(0), S(1));
            case "boutwaiting": return TownArena.BoutWaitingMessage(S(0), N(1), N(2));
            case "ambientlog": return TownArena.AmbientMatchupLog(N(0), N(1), D(a[2]), D(a[3]));
            case "medics": return TownArena.MedicsWaitingMessage(N(0), N(1));
            case "stagingfailed": return TownArena.StagingFailedMessage(S(0));
            case "stagingended": return TownArena.StagingEndedMessage(S(0));
            case "assembling": return TownArena.AssemblingMessage((MatchRules.MatchMode)N(0), Bf(1), N(2), N(3), N(4));
            case "aftercare": return TownArena.AftercareDoneMessage(S(0), Bf(1));
            default: Assert.Fail("unknown builder " + Str(c, "m")); return "";
        }
    }

    [Fact]
    public void FormattedMessagesMatchTheOriginal()
    {
        using var glibc = CMath.UseGlibcForTest();   // the golden text was printed by g++/glibc
        var seen = new HashSet<string>();
        foreach (JsonElement c in Cases("msg"))
        {
            seen.Add(Str(c, "m"));
            string text = Built(c, out bool? force);
            Eq(Str(c, "out"), text, c, Str(c, "m"));
            if (force.HasValue) Eq(Flag(c, "force"), force.Value, c, "force");
        }
        Assert.Equal(28, seen.Count);
    }

    // The C# constant for each fixed text the generator lists, by TownArena.cpp line.
    static readonly Dictionary<int, string> Fixed = new()
    {
        [97] = TownArena.Text.Idle, [468] = TownArena.Text.RefreshUnavailableCaption, [493] = TownArena.Text.RefreshNoSave,
        [498] = TownArena.Text.RefreshNoRegistry, [503] = TownArena.Text.RefreshBooked, [511] = TownArena.Text.RefreshBadClock,
        [524] = TownArena.Text.RefreshCollectFailed, [618] = TownArena.Text.SavedLineupWaiting, [688] = TownArena.Text.NpcLineupAnnounced,
        [692] = TownArena.Text.PlayerTookSlot, [695] = TownArena.Text.UpcomingCancelledForPlayer, [787] = TownArena.Text.SearchStartedLog,
        [901] = TownArena.Text.MatchmakingUnavailable, [1187] = TownArena.Text.BookNoSave, [1192] = TownArena.Text.BookBusy,
        [1201] = TownArena.Text.BookWrongDivision, [1208] = TownArena.Text.BookReservedOrUnavailable,
        [1235] = TownArena.Text.BookFightersNotReady, [1245] = TownArena.Text.BookLineupChanged,
        [1254] = TownArena.Text.BookReviewBuyIn, [1271] = TownArena.Text.BookCollectFailed, [1347] = TownArena.Text.BookingCancelled,
        [1355] = TownArena.Text.SchedulingPaused, [1364] = TownArena.Text.SlotTaken, [1371] = TownArena.Text.ReserveNeedsRecovered,
        [1384] = TownArena.Text.ProgressUnavailable, [1385] = TownArena.Text.PlayerBookingBusy, [1391] = TownArena.Text.DirectLineupChanged,
        [1396] = TownArena.Text.DirectUnavailable, [1479] = TownArena.Text.WagerStarting, [1490] = TownArena.Text.ArenaBusy,
        [1496] = TownArena.Text.NeedRegistry, [1518] = TownArena.Text.NoCrucible, [1535] = TownArena.Text.ReservationInvalid,
        [1602] = TownArena.Text.ReservedRecovering, [1622] = TownArena.Text.AnnouncedUnavailable, [1632] = TownArena.Text.AnnouncedIneligible,
        [1650] = TownArena.Text.WaitingOpponents, [1655] = TownArena.Text.NoMedic, [1667] = TownArena.Text.WaitingSquad,
        [1686] = TownArena.Text.MedicControlUnavailable, [1770] = TownArena.Text.PlayerHoldsSlot, [1774] = TownArena.Text.BoutUnderway,
        [1777] = TownArena.Text.WaitingToAnnounce, [1783] = TownArena.Text.ScheduledCancelled, [1794] = TownArena.Text.TrialCancelled,
        [1870] = TownArena.Text.EventStopped, [1879] = TownArena.Text.ScheduledNoPlayers, [1933] = TownArena.Text.SavedLineupRestored,
        [1934] = TownArena.Text.SavedLineupNoMarket, [1936] = TownArena.Text.SavedLineupExpired, [1978] = TownArena.Text.QueueReturnToRegistry,
        [1986] = TownArena.Text.BookingWaitingFighter, [2045] = TownArena.Text.FightFinished,
    };

    [Fact]
    public void FixedTextsMatchTheOriginal()
    {
        var lines = new HashSet<int>();
        foreach (JsonElement c in Cases("const"))
        {
            int line = Int(c, "line");
            Assert.True(Fixed.TryGetValue(line, out string? text), $"no constant for TownArena.cpp:{line}");
            Eq(Str(c, "text"), text, c, $"TownArena.cpp:{line}");
            lines.Add(line);
        }
        Assert.Equal(Fixed.Count, lines.Count);
    }

    [Fact]
    public void EveryFixedTextConstantIsChecked()
    {
        var checkedTexts = new HashSet<string>(Fixed.Values);
        var constants = typeof(TownArena.Text).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral).ToList();
        Assert.NotEmpty(constants);
        foreach (var f in constants)
            Assert.True(checkedTexts.Contains((string)f.GetRawConstantValue()!), $"TownArena.Text.{f.Name} is not in Fixed");
        Assert.Equal(constants.Count, Fixed.Count);
    }
}
