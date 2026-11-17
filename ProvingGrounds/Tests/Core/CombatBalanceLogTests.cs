using ProvingGrounds.Core;
using Xunit;

namespace ProvingGrounds.Tests.Core;

/// <summary>Core/CombatBalanceLog.cs by hand (CombatBalanceLog.cpp includes kenshi/*.h and Windows.h; no golden vectors). Every
/// expected line is written out from the C++ concatenations at the lines cited; numbers follow <c>Number()</c>
/// (CombatBalanceLog.cpp:46-49: classic locale, setprecision(10), default float format = %.10g). Model numbers
/// (TeamScore, WinProbability, ReturnCats) come from their own ported, golden-tested functions; only their text is pinned here.</summary>
public class CombatBalanceLogTests
{
    sealed class Host : CombatBalanceLog.IHost
    {
        public double Hours = 12.5;
        public string Save = "save-1";
        public TownDiagnosticData.State Diag = new TownDiagnosticData.State();
        public double GameHours() => Hours;
        public string ActiveSaveKey() => Save;
        public TownDiagnosticData.State Diagnostic() => Diag;
    }

    const string Dir = "C:\\Kenshi\\mods\\ProvingGrounds\\";
    const string Combat = Dir + "pg_combat_debug.log";
    const string Challenge = Dir + "pg_challenge_debug.log";
    const string Pre = "{\"utc\":\"2026-11-11T03:04:05.006Z\",\"session\":\"01db2f3a0000abcd-1234\",";
    const string Post = "}\r\n";
    // ",<BalanceTuning::WriteJson at shipped values>" (Tests/Core/BalanceTuningTests.cs, g++ output of the original)
    const string Tuning =
        ",\"ability_exponent\":2.5000,\"rating_influence\":1.0000,\"zero_sum_rating\":1,\"downed_percent\":0,\"npc_lead_min\":1.000," +
        "\"npc_lead_spread\":1.000,\"showcase_percent\":0,\"pricing_exponent\":6.6500,\"pricing_knee\":1.1000,\"ambient_close_cap\":1.1000," +
        "\"ambient_close_span\":0.1000,\"ambient_ratio_cap\":1.1500,\"ambient_underdog_span\":0.0500,\"ring_near_radius\":145.0," +
        "\"ring_out_radius\":147.5,\"ring_return_radius\":142.5";

    // Both swaps are required: PGLog's (a fresh sink, session and NextId counter, so Install never touches the process-wide state)
    // and CombatBalanceLog's (fresh ids / record / match state), so parallel tests never share either.
    static IDisposable Fresh(out RecordingSink sink, CombatBalanceLog.State? state = null)
    {
        var s = new RecordingSink();
        var log = PGLog.UseForTest(PGLog.State.Shipped());
        PGLog.Install(s, 0x01DB2F3Au, 0x0000ABCDu, 1234u);
        var cbl = CombatBalanceLog.UseForTest(state ?? CombatBalanceLog.State.Shipped());
        sink = s;
        return new Both(cbl, log);
    }

    sealed class Both : IDisposable
    {
        readonly IDisposable _a, _b;
        public Both(IDisposable a, IDisposable b) { _a = a; _b = b; }
        public void Dispose() { _a.Dispose(); _b.Dispose(); }
    }

    static string N(double v) => CombatBalanceLog.Number(v);

    [Fact]
    public void Number_is_percent_10g_classic_and_null_when_not_finite()
    {
        Assert.Equal("0", N(0));
        Assert.Equal("-0", N(-0.0));
        Assert.Equal("1", N(true ? 1 : 0));
        Assert.Equal("0.3333333333", N(1.0 / 3));
        Assert.Equal("0.1000000015", N(0.1f));   // a float widened to double, as Field(float) does in C++
        Assert.Equal("4294967295", N(uint.MaxValue));
        Assert.Equal("1.23456789e+010", N(12345678901));   // msvcr100: three-digit exponent
        Assert.Equal("1e-005", N(0.00001));
        Assert.Equal("0.0001", N(0.0001));
        Assert.Equal("-2.5", N(-2.5));
        Assert.Equal("null", N(double.NaN));
        Assert.Equal("null", N(double.PositiveInfinity));
        Assert.Equal("null", N(double.NegativeInfinity));
        Assert.Equal(",\"x\":null", CombatBalanceLog.Field("x", double.NaN));
    }

    [Fact]
    public void ChallengeCard_defaults()
    {
        // CombatBalanceLog.h:29-30
        var c = new CombatBalanceLog.ChallengeCard();
        Assert.Equal(-1, c.Slot);
        Assert.Equal(0, c.Division);
        Assert.Equal(0, c.Rarity);
        Assert.Equal(0, c.Encounter);
        Assert.Equal(0, c.Format);
        Assert.Equal(0, c.Mode);
        Assert.Equal(0, c.Players);
        Assert.Equal(0, c.Enemies);
        Assert.Equal(0.0, c.OwnScore);
        Assert.Equal(0.0, c.EnemyScore);
        Assert.Equal(0, c.Stake);
        Assert.Equal(0, c.Payout);
        Span<string> o = c.Opponents, n = c.OpponentNames, r = c.OpponentRoles;
        Assert.Equal(4, o.Length); Assert.Equal(4, n.Length); Assert.Equal(4, r.Length);
        Assert.Equal(new[] { "", "", "", "" }, o.ToArray());
        Assert.Equal(new[] { "", "", "", "" }, n.ToArray());
        Assert.Equal(new[] { "", "", "", "" }, r.ToArray());
    }

    [Fact]
    public void Challenge_accepted_settled_abandoned()
    {
        var host = new Host();
        using (Fresh(out var sink))
        {
            var card = new CombatBalanceLog.ChallengeCard
            {
                Slot = 2, Division = 1, Rarity = 3, Encounter = 2, Format = 1, Mode = 1, Players = 2, Enemies = 2,
                OwnScore = 1.5, EnemyScore = 1.5, Stake = 300, Payout = 750,
            };
            string[] ids = { "o1", "o2", "o3", "" }, names = { "Al \"Q\"", "Bo", "C", "" }, roles = { "r1", "r2", "r3", "" };
            for (int i = 0; i < 4; ++i) { card.Opponents[i] = ids[i]; card.OpponentNames[i] = names[i]; card.OpponentRoles[i] = roles[i]; }
            CombatBalanceLog.ChallengeAccepted(host, card);
            CombatBalanceLog.ChallengeSettled(host, 1, 750);
            CombatBalanceLog.ChallengeSettled(host, 1, 750); // cleared: nothing
            card.Enemies = 0; card.Stake = 0; card.Payout = 0;
            CombatBalanceLog.ChallengeAccepted(host, card);
            CombatBalanceLog.Abandon(host);
            CombatBalanceLog.ChallengeSettled(host, 0, 0); // cleared by Abandon: nothing

            Assert.Equal(new[]
            {
                // CombatBalanceLog.cpp:251-276 (only opponents below `enemies` and below 4)
                (Challenge, Pre + "\"event\":\"challenge_accepted\",\"record\":1,\"game_hours\":12.5,\"save\":\"save-1\",\"challenge_id\":1,\"slot\":2," +
                    "\"division\":1,\"division_name\":\"Medium\",\"rarity\":3,\"rarity_name\":\"Legendary\",\"encounter\":2,\"encounter_title\":\"Champion\"," +
                    "\"format\":1,\"mode\":1,\"mode_name\":\"Teams 1v1\",\"players\":2,\"enemies\":2,\"own_score\":1.5,\"enemy_score\":1.5," +
                    "\"stake\":300,\"quoted_return\":750,\"odds\":2.5,\"implied_p\":0.4,\"model_p\":0.5" + Tuning +
                    ",\"opponents\":[{\"id\":\"o1\",\"name\":\"Al \\\"Q\\\"\",\"role\":\"r1\",\"slot\":0},{\"id\":\"o2\",\"name\":\"Bo\",\"role\":\"r2\",\"slot\":1}]" + Post),
                // :277-285
                (Challenge, Pre + "\"event\":\"challenge_settled\",\"record\":2,\"game_hours\":12.5,\"save\":\"save-1\",\"challenge_id\":1,\"outcome\":1," +
                    "\"credited\":750,\"stake\":300,\"quoted_return\":750,\"player_net\":450" + Post),
                // stake 0 / payout 0: odds and implied_p fall back to 0
                (Challenge, Pre + "\"event\":\"challenge_accepted\",\"record\":3,\"game_hours\":12.5,\"save\":\"save-1\",\"challenge_id\":2,\"slot\":2," +
                    "\"division\":1,\"division_name\":\"Medium\",\"rarity\":3,\"rarity_name\":\"Legendary\",\"encounter\":2,\"encounter_title\":\"Champion\"," +
                    "\"format\":1,\"mode\":1,\"mode_name\":\"Teams 1v1\",\"players\":2,\"enemies\":0,\"own_score\":1.5,\"enemy_score\":1.5," +
                    "\"stake\":0,\"quoted_return\":0,\"odds\":0,\"implied_p\":0,\"model_p\":0.5" + Tuning + ",\"opponents\":[]" + Post),
                // :287-292: no game_hours / save on an abandon line
                (Challenge, Pre + "\"event\":\"challenge_abandoned\",\"record\":4,\"challenge_id\":2,\"stake\":0" + Post),
            }, sink.Lines);
        }
    }

    static CombatBalanceLog.FighterInfo Ann() => new()
    {
        Identity = "id-a", Role = "223-Proving Grounds.mod", Valid = true, Name = "Ann", ProfileValid = true, Style = "Katana",
        Attack = 50, Defence = 40.5, Strength = 30, Toughness = 20, Dexterity = 10, WeaponSkill = 60.25, Ability = 45.123456789012,
        Rating = 101.5f, HasHealth = true, LowestLimb = 0.75f, Blood = 0.5f, Recovery = 0.1f,
    };

    static CombatBalanceLog.FighterInfo Ghost() => new()
    {
        Identity = "", Role = "nope", Valid = false, Name = "Ghost", Ability = -1, Rating = 120,
    };

    [Fact]
    public void Enemies_above_four_list_four_opponents()
    {
        // CombatBalanceLog.cpp:270 `i < card.enemies && i < 4`
        var host = new Host();
        using (Fresh(out var sink))
        {
            var card = new CombatBalanceLog.ChallengeCard { Enemies = 5 };
            for (int i = 0; i < 4; ++i) card.Opponents[i] = "o" + i;
            CombatBalanceLog.ChallengeAccepted(host, card);
            Assert.EndsWith(",\"enemies\":5,\"own_score\":0,\"enemy_score\":0,\"stake\":0,\"quoted_return\":0,\"odds\":0,\"implied_p\":0,\"model_p\":0" + Tuning +
                ",\"opponents\":[{\"id\":\"o0\",\"name\":\"\",\"role\":\"\",\"slot\":0},{\"id\":\"o1\",\"name\":\"\",\"role\":\"\",\"slot\":1}," +
                "{\"id\":\"o2\",\"name\":\"\",\"role\":\"\",\"slot\":2},{\"id\":\"o3\",\"name\":\"\",\"role\":\"\",\"slot\":3}]" + Post, sink.Lines.Single().Line);
        }
    }

    [Fact]
    public void Unavailable_is_a_null_character()
    {
        var u = CombatBalanceLog.FighterInfo.Unavailable;
        Assert.Equal(-1.0, u.Ability);
        Assert.Equal(100f, u.Rating);
        var host = new Host();
        using (Fresh(out var sink))
        {
            CombatBalanceLog.OpenMarket(host, new[] { u, u }, AB, 2, 1, 1, false, 0, Standings());
            // Fighter() over a null Character* (:136-157): no catalog row for role "", mmr 0 (not valid), no health block
            const string json = "{\"id\":\"\",\"template\":\"\",\"name\":\"Unavailable\",\"slot\":0,\"team\":1,\"style\":\"\",\"profile_valid\":0," +
                "\"attack\":0,\"defence\":0,\"strength\":0,\"toughness\":0,\"dexterity\":0,\"weapon_skill\":0,\"ability\":-1," +
                "\"catalog_type\":\"\",\"catalog_tier\":-1,\"mmr\":0,\"wins\":0,\"losses\":0,\"matches\":0}";
            Assert.EndsWith(",\"fighters\":[" + json + "," + json.Replace("\"slot\":0,\"team\":1", "\"slot\":1,\"team\":2") + "]" + Post, sink.Lines.Single().Line);
            // StartAbility (:97): ability -1 stops the start scores even though the rating is 100
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.TeamAvB, new[] { u, u }, AB, 2, true, true, null, 0);
            Assert.Contains(Tuning + ",\"start_scores_available\":0,\"fighters\":[", sink.Lines[1].Line);
        }
    }

    [Fact]
    public void Start_score_line_literal()
    {
        // ability 40 and 20, rating 100 (factor 10^0 = 1): 40^2.5, 20^2.5, 1/(1+(1/2)^5); %.10g
        var host = new Host();
        var state = CombatBalanceLog.State.Shipped();
        state.ChallengeId = 9;
        using (Fresh(out var sink, state))
        {
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.TeamAvB, new[] { Plain(40, 100), Plain(20, 100) }, AB, 2, true, true, null, 0);
            Assert.Contains(Tuning + ",\"start_scores_available\":1,\"start_score_a_full\":10119.28851,\"start_score_b_full\":1788.854382," +
                "\"start_predicted_p_a_full\":0.9696969697,\"fighters\":[", sink.Lines.Single().Line);
        }
    }

    const string AnnJson =
        "{\"id\":\"id-a\",\"template\":\"223-Proving Grounds.mod\",\"name\":\"Ann\",\"slot\":0,\"team\":1,\"style\":\"Katana\",\"profile_valid\":1," +
        "\"attack\":50,\"defence\":40.5,\"strength\":30,\"toughness\":20,\"dexterity\":10,\"weapon_skill\":60.25,\"ability\":45.12345679," +
        "\"catalog_type\":\"Polearm\",\"catalog_tier\":1,\"mmr\":101.5,\"wins\":3,\"losses\":2,\"matches\":5," +
        "\"lowest_limb\":0.75,\"blood\":0.5,\"recovery\":0.1000000015}";
    const string GhostJson =
        "{\"id\":\"\",\"template\":\"nope\",\"name\":\"Unavailable\",\"slot\":1,\"team\":2,\"style\":\"\",\"profile_valid\":0," +
        "\"attack\":0,\"defence\":0,\"strength\":0,\"toughness\":0,\"dexterity\":0,\"weapon_skill\":0,\"ability\":-1," +
        "\"catalog_type\":\"\",\"catalog_tier\":-1,\"mmr\":0,\"wins\":0,\"losses\":0,\"matches\":0}";

    static List<LeaderboardData.Standing> Standings() => new()
    {
        new LeaderboardData.Standing { Id = "x", Wins = 9, Losses = 9, Matches = 18 },
        new LeaderboardData.Standing { Id = "id-a", Wins = 3, Losses = 2, Matches = 5 },
        new LeaderboardData.Standing { Id = "id-a", Wins = 99, Losses = 99, Matches = 198 }, // only the first match counts (:150-152)
        new LeaderboardData.Standing { Id = "", Wins = 7, Losses = 7, Matches = 14 },         // an empty id never matches
    };

    static readonly MatchRules.MatchTeam[] AB = { MatchRules.MatchTeam.TeamA, MatchRules.MatchTeam.TeamB };

    [Fact]
    public void Market_open_wager_settled()
    {
        var host = new Host();
        using (Fresh(out var sink))
        {
            CombatBalanceLog.OpenMarket(host, new[] { Ann(), Ghost() }, AB, 2, 3, 1, true, 13.25, Standings());
            CombatBalanceLog.MarketEvent(host, "market_wager", ",\"stake\":100");
            CombatBalanceLog.CloseMarket(host, 1, 2, 100, 250, 0, true);
            CombatBalanceLog.MarketEvent(host, "market_wager"); // closed: nothing
            CombatBalanceLog.CloseMarket(host, 2, 0, 0, 0, 5, false); // still nothing (MarketEvent needs a market)

            int r1 = TownBettingPolicy.ReturnCats(1000, 3, 1), r2 = TownBettingPolicy.ReturnCats(1000, 1, 3);
            Assert.Equal(new[]
            {
                // CombatBalanceLog.cpp:235-242, Fighter :136-157
                (Combat, Pre + "\"event\":\"market_open\",\"record\":1,\"game_hours\":12.5,\"save\":\"save-1\",\"market_id\":1,\"eligible\":1," +
                    "\"ability_a\":3,\"ability_b\":1,\"scheduled_game_hours\":13.25,\"predicted_p_a\":0.9," +
                    "\"return_per_1000_a\":" + r1 + ",\"return_per_1000_b\":" + r2 + Tuning +
                    ",\"fighters\":[" + AnnJson + "," + GhostJson + "]" + Post),
                // :243-245
                (Combat, Pre + "\"event\":\"market_wager\",\"record\":2,\"game_hours\":12.5,\"save\":\"save-1\",\"market_id\":1,\"stake\":100" + Post),
                // :246-250
                (Combat, Pre + "\"event\":\"market_settled\",\"record\":3,\"game_hours\":12.5,\"save\":\"save-1\",\"market_id\":1,\"winner_side\":1," +
                    "\"had_ticket\":1,\"bet_side\":2,\"stake\":100,\"quoted_return\":250,\"credited\":0,\"player_net\":-100" + Post),
            }, sink.Lines);
        }
    }

    [Fact]
    public void Diagnostic_block_when_a_bout_is_pending()
    {
        var host = new Host();
        var d = new TownDiagnosticData.State { RunId = 77 };
        d.Pending.Active = true; d.Pending.Sequence = 3; d.Pending.Model = 1; d.Pending.RequestedCoverage = 2; d.Pending.ActualCoverage = 9;
        d.Pending.Relaxation = 1; d.Pending.Underdog = true; d.Pending.SizeA = 2; d.Pending.SizeB = 3;
        d.Pending.ScoreA[0] = 1; d.Pending.ScoreA[1] = 2; d.Pending.ScoreA[2] = 3;
        d.Pending.ScoreB[0] = 4; d.Pending.ScoreB[1] = 5; d.Pending.ScoreB[2] = 6;
        d.Pending.ProbabilityA[0] = 0.1; d.Pending.ProbabilityA[1] = 0.25; d.Pending.ProbabilityA[2] = 0.5;
        host.Diag = d;
        var state = CombatBalanceLog.State.Shipped();
        state.MarketId = 5;
        using (Fresh(out var sink, state))
        {
            CombatBalanceLog.MarketEvent(host, "market_wager", ",\"x\":1");
            // CombatBalanceLog.cpp:69-86
            Assert.Equal(Pre + "\"event\":\"market_wager\",\"record\":1,\"game_hours\":12.5,\"save\":\"save-1\",\"market_id\":5,\"x\":1," +
                "\"diagnostic_run_id\":77,\"diagnostic_sequence\":3,\"diagnostic_model\":\"reduced_mmr\"," +
                "\"diagnostic_requested_coverage\":\"difference_one\",\"diagnostic_actual_coverage\":\"invalid\"," +
                "\"diagnostic_relaxation\":1,\"diagnostic_underdog\":1,\"diagnostic_size_a\":2,\"diagnostic_size_b\":3," +
                "\"score_a_stats\":1,\"score_b_stats\":4,\"predicted_p_a_stats\":0.1," +
                "\"score_a_reduced\":2,\"score_b_reduced\":5,\"predicted_p_a_reduced\":0.25," +
                "\"score_a_full\":3,\"score_b_full\":6,\"predicted_p_a_full\":0.5" + Post, sink.Lines.Single().Line);
        }
    }

    static SparPodium.Snapshot Result()
    {
        var snap = new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.LastStanding, FighterCount = 2, LastStandingId = 0 };
        snap.Fighters[0] = new SparPodium.FighterRow
        {
            Id = 0, Team = MatchRules.MatchTeam.TeamA, Name = "Ann", DamageDealt = 30f, DamageTaken = 10f, DamageMitigated = 2.5f,
            HitsLanded = 5, Blocks = 1, Misses = 2, Dodges = 3, EliminationIndex = -1, RatingUpdated = true, RatingBefore = 100f, RatingAfter = 104.5f,
        };
        snap.Fighters[1] = new SparPodium.FighterRow
        {
            Id = 1, Team = MatchRules.MatchTeam.TeamB, Name = null, DamageDealt = 10f, DamageTaken = 30f, HitsLanded = 1, EliminationIndex = 0,
        };
        return snap;
    }

    static readonly CombatBalanceLog.ParticipantInfo[] Participants =
    {
        new() { Identity = "id-a", HasHealth = true, LowestLimb = 0.5f, Blood = 0.25f, Recovery = 0.75f, Valid = true, Dead = false, Unconscious = true },
        new() { Identity = "" },
    };

    [Fact]
    public void Arena_match_start_and_result()
    {
        var host = new Host();
        using (Fresh(out var sink))
        {
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.LastStanding, new[] { Ann(), Ghost() }, AB, 2, false, false, Standings(), 1000);
            host.Hours = 13.0;
            CombatBalanceLog.FinishMatch(host, Result(), 7, Participants, MatchRules.MatchEndKind.LastStanding, 4500);
            CombatBalanceLog.FinishMatch(host, Result(), 7, Participants, MatchRules.MatchEndKind.LastStanding, 4500); // inactive: nothing
            CombatBalanceLog.AbortMatch(host, "x"); // inactive: nothing

            Assert.Equal(new[]
            {
                // CombatBalanceLog.cpp:165-181 (arena: no market, no challenge, no diagnostic, no start scores)
                (Combat, Pre + "\"event\":\"match_start\",\"record\":1,\"game_hours\":12.5,\"save\":\"save-1\",\"match_id\":1,\"market_id\":0," +
                    "\"challenge_id\":0,\"mode\":2,\"kind\":\"arena\"" + Tuning + ",\"fighters\":[" + AnnJson + "," + GhostJson + "]" + Post),
                // :182-226
                (Combat, Pre + "\"event\":\"match_result\",\"record\":2,\"game_hours\":13,\"save\":\"save-1\",\"match_id\":1,\"challenge_id\":0," +
                    "\"outcome\":2,\"stop_reason\":7,\"end_kind\":\"last_standing\",\"game_minutes\":30,\"wall_seconds\":3.5," +
                    "\"last_standing_slot\":0,\"outcome_name\":\"last_standing\",\"team_damage_a\":30,\"team_damage_b\":10," +
                    "\"top_damage_share\":0.75,\"total_damage\":40,\"fighters\":[" +
                    "{\"id\":\"id-a\",\"name\":\"Ann\",\"slot\":0,\"team\":1,\"damage_dealt\":30,\"damage_taken\":10,\"damage_mitigated\":2.5," +
                    "\"damage_share\":0.75,\"hits\":5,\"blocks\":1,\"misses\":2,\"dodges\":3,\"elimination_order\":-1,\"rating_updated\":1," +
                    "\"mmr_before\":100,\"mmr_after\":104.5,\"lowest_limb\":0.5,\"blood\":0.25,\"recovery\":0.75,\"dead\":0,\"unconscious\":1}," +
                    "{\"id\":\"\",\"name\":\"\",\"slot\":1,\"team\":2,\"damage_dealt\":10,\"damage_taken\":30,\"damage_mitigated\":0," +
                    "\"damage_share\":0.25,\"hits\":1,\"blocks\":0,\"misses\":0,\"dodges\":0,\"elimination_order\":0,\"rating_updated\":0," +
                    "\"mmr_before\":0,\"mmr_after\":0}]" +
                    ",\"damage_mitigated_available\":0" + Tuning + Post),
            }, sink.Lines);
        }
    }

    [Fact]
    public void Outcome_names_and_zero_damage()
    {
        var host = new Host();
        string[] expected = { "team_a_wins", "team_b_wins", "last_standing", "stopped", "draw", "stopped" };
        for (int o = 0; o < 6; ++o)
            using (Fresh(out var sink))
            {
                CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.TeamAvB, new[] { Ghost(), Ghost() }, AB, 2, false, false, null, 0);
                var snap = new SparPodium.Snapshot { Outcome = (SparPodium.OutcomeKind)o, FighterCount = 1 };
                snap.Fighters[0] = new SparPodium.FighterRow { Team = MatchRules.MatchTeam.None };
                CombatBalanceLog.FinishMatch(host, snap, 0, new[] { new CombatBalanceLog.ParticipantInfo() }, MatchRules.MatchEndKind.None, ulong.MaxValue);
                string line = sink.Lines[1].Line;
                Assert.Contains(",\"outcome_name\":\"" + expected[o] + "\"", line);
                // zero total damage: both shares 0; team None counts as team B; wall clock wraps like ULONGLONG
                Assert.Contains(",\"team_damage_a\":0,\"team_damage_b\":0,\"top_damage_share\":0,\"total_damage\":0,", line);
                Assert.Contains(",\"damage_share\":0,", line);
                Assert.Contains(",\"end_kind\":\"none\",\"game_minutes\":0,\"wall_seconds\":1.844674407e+016,", line);
            }
    }

    static CombatBalanceLog.FighterInfo Plain(double ability, float rating) => new()
    {
        Identity = "p", Role = "r", Valid = true, Name = "P", ProfileValid = true, Style = "", Ability = ability, Rating = rating,
    };

    static string PlainJson(int slot, int team, double ability, float rating) =>
        "{\"id\":\"p\",\"template\":\"r\",\"name\":\"P\",\"slot\":" + slot + ",\"team\":" + team + ",\"style\":\"\",\"profile_valid\":1," +
        "\"attack\":0,\"defence\":0,\"strength\":0,\"toughness\":0,\"dexterity\":0,\"weapon_skill\":0,\"ability\":" + N(ability) +
        ",\"catalog_type\":\"\",\"catalog_tier\":-1,\"mmr\":" + N(rating) + ",\"wins\":0,\"losses\":0,\"matches\":0}";

    [Fact]
    public void Player_challenge_then_superseded_then_teardown()
    {
        var host = new Host();
        var state = CombatBalanceLog.State.Shipped();
        state.ChallengeId = 9; state.MarketId = 4;
        using (Fresh(out var sink, state))
        {
            var fighters = new[] { Plain(40, 100), Plain(20, 110) };
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.TeamAvB, fighters, AB, 2, true, true, null, 0);
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.Teams1v1, fighters, AB, 2, true, false, null, 0);
            CombatBalanceLog.Abandon(host);

            double sa = TownMatchmakingPolicy.TeamScore(new double[] { 40 }, new double[] { 100 }, 1, 1.0);
            double sb = TownMatchmakingPolicy.TeamScore(new double[] { 20 }, new double[] { 110 }, 1, 1.0);
            string start = ",\"start_scores_available\":1,\"start_score_a_full\":" + N(sa) + ",\"start_score_b_full\":" + N(sb) +
                ",\"start_predicted_p_a_full\":" + N(TownBettingPolicy.WinProbability(sa, sb));
            string fj = ",\"fighters\":[" + PlainJson(0, 1, 40, 100) + "," + PlainJson(1, 2, 20, 110) + "]";
            Assert.Equal(new[]
            {
                // :172-180: the challenge file, challenge_id from the live challenge, no market, no diagnostic block
                (Challenge, Pre + "\"event\":\"match_start\",\"record\":1,\"game_hours\":12.5,\"save\":\"save-1\",\"match_id\":1,\"market_id\":0," +
                    "\"challenge_id\":9,\"mode\":0,\"kind\":\"town_player\"" + Tuning + start + fj + Post),
                // :166 -> :227-234 on the challenge file (no game_hours / save), with the held start scores
                (Challenge, Pre + "\"event\":\"match_aborted\",\"record\":2,\"match_id\":1,\"reason\":\"superseded\"" + start + Post),
                // town NPC bout: the open market's id; the diagnostic is not pending, so no diagnostic / start blocks
                (Combat, Pre + "\"event\":\"match_start\",\"record\":3,\"game_hours\":12.5,\"save\":\"save-1\",\"match_id\":2,\"market_id\":4," +
                    "\"challenge_id\":0,\"mode\":1,\"kind\":\"town_npc\"" + Tuning + fj + Post),
                // :287-292
                (Combat, Pre + "\"event\":\"match_aborted\",\"record\":4,\"match_id\":2,\"reason\":\"world_teardown\"" + Post),
                (Combat, Pre + "\"event\":\"market_abandoned\",\"record\":5,\"market_id\":4" + Post),
                (Challenge, Pre + "\"event\":\"challenge_abandoned\",\"record\":6,\"challenge_id\":9,\"stake\":0" + Post),
            }, sink.Lines);
        }
    }

    [Fact]
    public void Match_result_names_its_challenge_after_ChallengeSettled()
    {
        var host = new Host();
        var state = CombatBalanceLog.State.Shipped();
        state.ChallengeId = 9;
        using (Fresh(out var sink, state))
        {
            var fighters = new[] { Plain(40, 100), Plain(-1, 100) }; // an unusable ability: no start scores
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.TeamAvB, fighters, AB, 2, true, true, null, 0);
            CombatBalanceLog.ChallengeSettled(host, 0, 0);
            var snap = new SparPodium.Snapshot { FighterCount = 0 };
            CombatBalanceLog.FinishMatch(host, snap, 1, Array.Empty<CombatBalanceLog.ParticipantInfo>(), MatchRules.MatchEndKind.TeamWipeB, 0);
            Assert.Contains(Tuning + ",\"start_scores_available\":0,\"fighters\":[", sink.Lines[0].Line);
            Assert.Equal(Challenge, sink.Lines[2].Path);
            Assert.StartsWith(Pre + "\"event\":\"match_result\",\"record\":3,\"game_hours\":12.5,\"save\":\"save-1\",\"match_id\":1,\"challenge_id\":9,", sink.Lines[2].Line);
            Assert.EndsWith(",\"fighters\":[],\"damage_mitigated_available\":0" + Tuning + ",\"start_scores_available\":0" + Post, sink.Lines[2].Line);
        }
    }

    [Fact]
    public void Diagnostic_start_scores_for_all_three_models()
    {
        var host = new Host();
        var d = new TownDiagnosticData.State { RunId = 5 };
        d.Pending.Active = true;
        d.Config.ReducedInfluence = 0.5;
        host.Diag = d;
        using (Fresh(out var sink))
        {
            var fighters = new[] { Plain(40, 100), Plain(20, 110) };
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.TeamAvB, fighters, AB, 2, true, false, null, 0);
            string start = ",\"start_scores_available\":1";
            string[] suffix = { "stats", "reduced", "full" };
            double[] influence = { 0, 0.5, 1 };
            for (int i = 0; i < 3; ++i)
            {
                double sa = TownMatchmakingPolicy.TeamScore(new double[] { 40 }, new double[] { 100 }, 1, influence[i]);
                double sb = TownMatchmakingPolicy.TeamScore(new double[] { 20 }, new double[] { 110 }, 1, influence[i]);
                start += ",\"start_score_a_" + suffix[i] + "\":" + N(sa) + ",\"start_score_b_" + suffix[i] + "\":" + N(sb) +
                    ",\"start_predicted_p_a_" + suffix[i] + "\":" + N(TownBettingPolicy.WinProbability(sa, sb));
            }
            string line = sink.Lines.Single().Line;
            // :178: DiagnosticFields, then the start scores, then the fighters
            Assert.Contains(Tuning + ",\"diagnostic_run_id\":5,\"diagnostic_sequence\":0,", line);
            Assert.Contains(",\"predicted_p_a_full\":0" + start + ",\"fighters\":[", line);
        }
    }

    [Fact]
    public void Diagnostic_events_from_TownDiagnosticRun_go_to_the_combat_file()
    {
        var host = new Host();
        using (Fresh(out var sink))
        {
            var d = new TownDiagnosticData.State { Status = (int)TownDiagnosticData.RunStatus.Active, RunId = 42 };
            Assert.True(TownDiagnosticRun.Pause(ref d, CombatBalanceLog.DiagnosticSink(host)));
            CombatBalanceLog.DiagnosticEvent(host, "plain");
            Assert.Equal(new[]
            {
                (Combat, Pre + "\"event\":\"diagnostic_run_paused\",\"record\":1,\"game_hours\":12.5,\"save\":\"save-1\",\"diagnostic_run_id\":42" + Post),
                (Combat, Pre + "\"event\":\"plain\",\"record\":2,\"game_hours\":12.5,\"save\":\"save-1\"" + Post),
            }, sink.Lines);
        }
    }

    [Fact]
    public void Record_and_ids_are_separate_counters()
    {
        // record is CombatBalanceLog's own (:29); match / market / challenge ids come from PGLog::NextId (:167, :236, :252)
        var host = new Host();
        using (Fresh(out var sink))
        {
            PGLog.NextId(); PGLog.NextId();
            CombatBalanceLog.DiagnosticEvent(host, "a");
            CombatBalanceLog.ChallengeAccepted(host, new CombatBalanceLog.ChallengeCard());
            Assert.Contains("\"record\":1,", sink.Lines[0].Line);
            Assert.Contains("\"record\":2,", sink.Lines[1].Line);
            Assert.Contains(",\"challenge_id\":3,\"slot\":-1,", sink.Lines[1].Line);
        }
    }

    // Every event kind CombatBalanceLog writes, with the channel the C++ sends it to (CombatBalanceLog.cpp:63, :67, :180, :224, :231, :287-291,
    // and the callers: TownBookie.cpp:110, :361, :371; TownDiagnosticRun.cpp:68, :77-79, :117, :134; ArenaNativePersistence.cpp:370).
    static readonly (string Event, bool ChallengeFile)[] AllEvents =
    {
        ("match_start", false), ("match_start", true), ("match_result", false), ("match_result", true), ("match_aborted", false), ("match_aborted", true),
        ("market_open", false), ("wager_accepted", false), ("market_combat_started", false), ("market_cancelled", false), ("market_settled", false),
        ("market_abandoned", false), ("challenge_accepted", true), ("challenge_settled", true), ("challenge_abandoned", true),
        ("diagnostic_run_started", false), ("diagnostic_run_paused", false), ("diagnostic_run_resumed", false), ("diagnostic_run_ended", false),
        ("diagnostic_assignment_cancelled", false), ("diagnostic_run_completed", false), ("diagnostic_persistence_error", false),
    };

    // Walks every object of a parsed line: no property name twice in one object (JsonDocument keeps both, a reader keeps one).
    static void AssertNoDuplicateNames(System.Text.Json.JsonElement e)
    {
        if (e.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var names = new HashSet<string>();
            foreach (var p in e.EnumerateObject())
            {
                Assert.True(names.Add(p.Name), "duplicate property " + p.Name);
                AssertNoDuplicateNames(p.Value);
            }
        }
        else if (e.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var item in e.EnumerateArray()) AssertNoDuplicateNames(item);
    }

    [Fact]
    public void Every_event_kind_is_one_parsable_JSON_line_on_its_channel()
    {
        // Task 8.7c: the offline half of the town-diagnostic scenario's "every line parsable JSON" check. Each line is the envelope
        // (PGLog.cpp:75-77) around one event, CRLF-terminated, and parses as one JSON object whose "event" names it.
        var host = new Host();
        using (Fresh(out var sink))
        {
            var sinkFn = CombatBalanceLog.DiagnosticSink(host);
            var fighters = new[] { Ann(), Ghost() };
            var players = new[] { Plain(40, 100), Plain(20, 110) };

            // the diagnostic run's events (the text TownDiagnosticRun builds; StartConfigured passes StartFields)
            var d = new TownDiagnosticData.State { Status = (int)TownDiagnosticData.RunStatus.Active, RunId = 4242 };
            d.Config.Target = 1;
            CombatBalanceLog.DiagnosticEvent(host, "diagnostic_run_started", TownDiagnosticRun.StartFields(d, true));
            Assert.True(TownDiagnosticRun.Pause(ref d, sinkFn));
            Assert.True(TownDiagnosticRun.Resume(ref d, sinkFn));
            d.Pending.Active = true; d.Pending.Sequence = 1; d.Pending.SizeA = 1; d.Pending.SizeB = 1; d.Pending.ProbabilityA[0] = 0.5;
            host.Diag = d;   // an assignment is pending: the market and NPC-bout lines carry the diagnostic block

            // an NPC bout's market and match (ambient file)
            CombatBalanceLog.OpenMarket(host, fighters, AB, 2, 3, 1, true, 13.25, Standings());
            CombatBalanceLog.MarketEvent(host, "wager_accepted", TownBookie.WagerFields(0, 100, 240));
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.TeamAvB, players, AB, 2, true, false, Standings(), 1000);
            CombatBalanceLog.MarketEvent(host, "market_combat_started");
            CombatBalanceLog.FinishMatch(host, Result(), 1, Participants, MatchRules.MatchEndKind.LastStanding, 2000);
            CombatBalanceLog.CloseMarket(host, 0, 0, 100, 240, 240, true);
            TownDiagnosticRun.CompletePending(ref d, new SparPodium.Snapshot { Outcome = SparPodium.OutcomeKind.TeamAWins }, 14, sinkFn);
            host.Diag = d;
            CombatBalanceLog.OpenMarket(host, fighters, AB, 2, 3, 1, false, 0, Standings());
            CombatBalanceLog.MarketEvent(host, "market_cancelled", ",\"reason\":" + PGLog.Quote("Player match took priority"));
            CombatBalanceLog.CloseMarket(host, -1, -1, 0, 0, 0, false);
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.Teams1v1, players, AB, 2, false, false, null, 3000);
            CombatBalanceLog.AbortMatch(host, "invalid_fighter");

            // a player challenge (challenge file)
            var card = new CombatBalanceLog.ChallengeCard { Slot = 0, Enemies = 1, Players = 1, OwnScore = 2, EnemyScore = 1, Stake = 100, Payout = 180 };
            card.Opponents[0] = "o1"; card.OpponentNames[0] = "Oz \"the\" \\ One"; card.OpponentRoles[0] = "r1";
            CombatBalanceLog.ChallengeAccepted(host, card);
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.TeamAvB, players, AB, 2, true, true, null, 4000);
            CombatBalanceLog.FinishMatch(host, Result(), 2, Participants, MatchRules.MatchEndKind.TeamWipeB, 5000);
            CombatBalanceLog.ChallengeSettled(host, 1, 180);
            CombatBalanceLog.ChallengeAccepted(host, card);
            CombatBalanceLog.StartMatch(host, MatchRules.MatchMode.TeamAvB, players, AB, 2, true, true, null, 6000);
            CombatBalanceLog.AbortMatch(host, "safe_save");

            // the teardown: an NPC market and a challenge still open
            CombatBalanceLog.OpenMarket(host, fighters, AB, 2, 3, 1, true, 15, Standings());
            CombatBalanceLog.Abandon(host);

            // the rest of the run's events
            d = new TownDiagnosticData.State { Status = (int)TownDiagnosticData.RunStatus.Active, RunId = 7 };
            d.Pending.Active = true; d.Pending.Sequence = 3;
            TownDiagnosticRun.RecoverPendingAfterLoad(ref d, sinkFn);
            Assert.True(TownDiagnosticRun.End(ref d, sinkFn));
            // ArenaNativePersistence.cpp:369-370 (Core/Glue/ArenaWorldState.cs PersistenceErrorFields)
            CombatBalanceLog.DiagnosticEvent(host, "diagnostic_persistence_error", ",\"error\":\"invalid diagnostic state; see arena persistence log\"");

            var seen = new HashSet<(string, bool)>();
            uint lastRecord = 0;
            foreach (var (path, line) in sink.Lines)
            {
                Assert.True(path == Combat || path == Challenge, path);
                Assert.EndsWith("}\r\n", line);
                Assert.Equal(1, line.Split("\r\n").Length - 1);   // one line per event
                using var doc = System.Text.Json.JsonDocument.Parse(line[..^2]);
                var root = doc.RootElement;
                AssertNoDuplicateNames(root);   // a repeated field would parse but lose one value
                Assert.Equal(System.Text.Json.JsonValueKind.Object, root.ValueKind);
                Assert.Equal("2026-11-11T03:04:05.006Z", root.GetProperty("utc").GetString());
                Assert.Equal("01db2f3a0000abcd-1234", root.GetProperty("session").GetString());
                string ev = root.GetProperty("event").GetString()!;
                uint record = root.GetProperty("record").GetUInt32();
                Assert.Equal(lastRecord + 1, record);   // one record counter over both files (:29)
                lastRecord = record;
                bool challengeFile = path == Challenge;
                Assert.Contains((ev, challengeFile), AllEvents);
                seen.Add((ev, challengeFile));
            }
            Assert.Equal(AllEvents.Length, seen.Count);   // every event kind on its channel at least once
            Assert.Equal(AllEvents.Length + 6, sink.Lines.Count);   // 28 lines = 22 kinds + 6 repeats: market_open x3, market_settled x2, ambient match_start x2 (town_npc, arena), player match_start x2, challenge_accepted x2
        }
    }

    [Fact]
    public void State_clone_copies_every_field()
    {
        var s = new CombatBalanceLog.State
        {
            MatchId = 1, MarketId = 2, ChallengeId = 3, Record = 4, Active = true, StartedHours = 5.5, StartedWall = 6, StartFields = "x",
            PlayerMatch = true, MatchChallengeId = 7, ChallengeStake = 8, ChallengePayout = 9,
        };
        var c = s.Clone();
        Assert.NotSame(s, c);
        Assert.Equal((1u, 2u, 3u, 4u, true, 5.5, 6ul, "x", true, 7u, 8, 9),
            (c.MatchId, c.MarketId, c.ChallengeId, c.Record, c.Active, c.StartedHours, c.StartedWall, c.StartFields, c.PlayerMatch,
                c.MatchChallengeId, c.ChallengeStake, c.ChallengePayout));
    }
}
