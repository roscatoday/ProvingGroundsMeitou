using ProvingGrounds.Core;
using TS = ProvingGrounds.Core.TownSpectators;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand cases for Core/TownSpectators.cs, worked from TownSpectators.cpp:140-335.</summary>
public class TownSpectatorsTests
{
    [Fact]
    public void ChatterRandom_is_xorshift_with_a_tick_based_zero_fix()
    {
        uint seed = 1;
        // 1 ^ 1<<13 = 8193; ^ 8193>>17 (0) = 8193; ^ 8193<<5 = 8193 ^ 262176 = 270369
        Assert.Equal(270369u, TS.ChatterRandom(ref seed, 12345));
        Assert.Equal(270369u, seed);
        uint zero = 0, fixedSeed = 12345u ^ 0x9e3779b9u;
        Assert.Equal(TS.ChatterRandom(ref fixedSeed, 0), TS.ChatterRandom(ref zero, 12345));   // the tick is read only at seed 0
        uint differs = 0;
        uint h = 0x9e3779b9u;   // Hashing.Xorshift32's own zero fix uses the constant alone: the same only when the tick is 0
        Assert.Equal(Hashing.Xorshift32(ref h), TS.ChatterRandom(ref differs, 0));
        Assert.Equal(12345u ^ unchecked(3u * 2654435761u), TS.ChatterStartSeed(12345, 3));
        Assert.Equal(77u, TS.ChatterStartSeed(77, 0));
    }

    [Fact]
    public void Voices_are_one_per_25_seated_capped_by_the_policy()
    {
        var policy = new ArenaPerformance.Policy(120, 0, 4.0f, 4.0f, 5, true);   // (cap, maintenanceMs, chatterMin, chatterMax, voices, chatter); min == max: no duration draw
        uint seed = 1;
        Assert.Single(TS.ScheduleChatterCycle(policy, 25, -1, 0, ref seed, 0).Cues);
        seed = 1;
        Assert.Equal(2, TS.ScheduleChatterCycle(policy, 26, -1, 0, ref seed, 0).Cues.Count);
        seed = 1;
        Assert.Equal(5, TS.ScheduleChatterCycle(policy, 1000, -1, 0, ref seed, 0).Cues.Count);
        seed = 1;
        // Policy(cap, maintenanceMs, chatterMin, chatterMax, voices, chatter)
        TS.Cycle none = TS.ScheduleChatterCycle(new ArenaPerformance.Policy(120, 0, 4.0f, 4.0f, 0, true), 30, -1, 10, ref seed, 0);
        Assert.Empty(none.Cues);
        Assert.Equal(14.0f, none.NextChatterCycle);
        Assert.Equal(1u, seed);                                   // no voices: nothing drawn after the duration (none drawn at min == max)
    }

    [Fact]
    public void Cues_are_spaced_evenly_and_skip_the_last_speaker()
    {
        var policy = new ArenaPerformance.Policy(120, 0, 4.0f, 4.0f, 5, true);   // (cap, maintenanceMs, chatterMin, chatterMax, voices, chatter)
        uint seed = 1;   // first draw 270369; 270369 % 50 = 19
        TS.Cycle c = TS.ScheduleChatterCycle(policy, 50, -1, 100, ref seed, 0);
        Assert.Equal(new[] { 19, 44 }, c.Cues.Select(q => q.SeatedIndex));   // step 50 / 2 = 25
        Assert.Equal(new[] { 101.0f, 103.0f }, c.Cues.Select(q => q.Due));   // 100 + (i + .5) * (4 / 2)
        seed = 1;
        c = TS.ScheduleChatterCycle(policy, 50, 19, 100, ref seed, 0);
        Assert.Equal(new[] { 20, 45 }, c.Cues.Select(q => q.SeatedIndex));   // index 19 spoke last: start moves on by one
    }

    static SparPodium.Snapshot Result(SparPodium.OutcomeKind outcome, int last, params (int id, MatchRules.MatchTeam team, string name)[] f)
    {
        var s = new SparPodium.Snapshot { Outcome = outcome, LastStandingId = last, FighterCount = f.Length };
        for (int i = 0; i < f.Length; ++i) { s.Fighters[i].Id = f[i].id; s.Fighters[i].Team = f[i].team; s.Fighters[i].Name = f[i].name; }
        return s;
    }

    [Fact]
    public void Winners_are_the_winning_team_or_the_last_standing_with_a_name()
    {
        var a = MatchRules.MatchTeam.TeamA; var b = MatchRules.MatchTeam.TeamB;
        Assert.Equal(new[] { "Ruka", "Kang" }, TS.ResultWinners(Result(SparPodium.OutcomeKind.TeamAWins, 0, (1, a, "Ruka"), (2, b, "Burn"), (3, a, "Kang"), (4, a, ""))));
        Assert.Equal(new[] { "Burn" }, TS.ResultWinners(Result(SparPodium.OutcomeKind.TeamBWins, 0, (1, a, "Ruka"), (2, b, "Burn"))));
        Assert.Equal(new[] { "Burn" }, TS.ResultWinners(Result(SparPodium.OutcomeKind.LastStanding, 2, (1, a, "Ruka"), (2, b, "Burn"))));
        Assert.Empty(TS.ResultWinners(Result(SparPodium.OutcomeKind.Draw, 1, (1, a, "Ruka"))));
    }

    [Fact]
    public void A_draw_or_no_winner_gets_a_draw_line_and_stopped_matches_say_nothing()
    {
        uint seed = 1;   // 270369 % 4 = 1
        var draw = Result(SparPodium.OutcomeKind.Draw, 0);
        Assert.Equal("Again! We need a winner!", TS.ResultChatterLine(draw, new List<string>(), ref seed, 0));
        var policy = new ArenaPerformance.Policy();   // the defaults: cap 120, 4-8 s, 5 voices, chatter on
        seed = 1;
        Assert.Empty(TS.ReactToResult(policy, Result(SparPodium.OutcomeKind.Stopped, 0), 5, 5, -1, ref seed, 0));
        Assert.Empty(TS.ReactToResult(policy, draw, 0, 5, -1, ref seed, 0));
        Assert.Empty(TS.ReactToResult(new ArenaPerformance.Policy(120, 0, 4, 8, 5, false), draw, 5, 5, -1, ref seed, 0));   // Policy(.., chatter: false)
        Assert.Equal(1u, seed);
        Assert.Single(TS.ReactToResult(policy, draw, 5, 5, -1, ref seed, 0));
    }

    [Fact]
    public void Fight_lines_without_fighters_are_generic()
    {
        uint seed = 1;   // 270369 % 10 = 9 (not < 4) but no fighters: generic[67634689 % 10 = 9]
        Assert.Equal("Finish it!", TS.ChatterLine(new List<string>(), ref seed, 0));
        Assert.Equal(67634689u, seed);
    }

    [Fact]
    public void A_draw_gets_a_draw_line_even_with_winners_passed_in()   // (mutation pass: the Draw test is not implied by winners)
    {
        uint seed = 1;   // 270369 % 4 = 1
        Assert.Equal("Again! We need a winner!",
            TS.ResultChatterLine(Result(SparPodium.OutcomeKind.Draw, 0), new List<string> { "Ruka" }, ref seed, 0));
    }
}
