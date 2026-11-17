using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownDiagnosticData;
using static ProvingGrounds.Core.TownDiagnosticRun;
using Match = ProvingGrounds.Core.TownMatchmakingPolicy.Match;

namespace ProvingGrounds.Tests.Core;

/// <summary>Hand tests for Core/TownDiagnosticRun.cs and Core/TownDiagnosticData.cs: TownDiagnosticData.h (defaults, value copies; the name functions are also in
/// Tests/Golden/matchmaking.json) and the planning logic of TownDiagnosticRun.cpp. TownDiagnosticRun.cpp includes Windows.h,
/// LeaderboardStore, TownArena and CombatBalanceLog, so it cannot be linked into a golden generator: every expected value here is
/// read off the C++ by hand (cited per test).</summary>
public class TownDiagnosticRunTests
{
    sealed class Host : IHost
    {
        public bool Save = true, Busy, Booking, Planned, CancelClears = true, ResetOk = true;
        public readonly List<string> Calls = new List<string>();
        public bool HasActiveSave() { Calls.Add("save"); return Save; }
        public bool IsBusy() { Calls.Add("busy"); return Busy; }
        public bool HasBooking() { Calls.Add("booking"); return Booking; }
        public bool HasPlannedNpcBout() { Calls.Add("planned"); return Planned; }
        public void CancelPlannedNpcBout(string reason) { Calls.Add("cancel:" + reason); if (CancelClears) Planned = false; }
        public bool ResetTownStandings() { Calls.Add("reset"); return ResetOk; }
        public void ClearDiagnosticError() { Calls.Add("clear"); }
    }

    sealed class Log
    {
        public readonly List<(string name, string fields)> Events = new List<(string, string)>();
        public void Add(string name, string fields) => Events.Add((name, fields));
    }

    static State Active(uint runId = 77)
    {
        var s = new State(); s.Status = (int)RunStatus.Active; s.RunId = runId; return s;
    }

    static Match DiagnosticMatch(int sizeA, int sizeB, int model = 1, double probabilityA = .7, int relaxation = 0)
    {
        var m = new Match { Valid = true, Diagnostic = true, DiagnosticModel = model, RequestedCoverage = 1, ActualCoverage = 2,
            Relaxation = relaxation, DiagnosticUnderdog = true };
        for (int i = 0; i < sizeA; ++i) m.A.Add("a" + i);
        for (int i = 0; i < sizeB; ++i) m.B.Add("b" + i);
        for (int i = 0; i < 3; ++i) { m.ModelScoreA[i] = 10 + i; m.ModelScoreB[i] = 20 + i; m.ModelProbabilityA[i] = probabilityA; }
        return m;
    }

    [Fact]   // TownDiagnosticData.h:8-56
    public void Defaults_are_the_cpp_constructors()
    {
        var s = new State();
        Assert.Equal((int)RunStatus.Off, s.Status); Assert.Equal(0u, s.RunId); Assert.Equal(1u, s.NextSequence);
        Assert.Equal(150, s.Config.Target); Assert.Equal(.25, s.Config.ReducedInfluence); Assert.Equal(20, s.Config.UnderdogPercent);
        Assert.True(s.Config.BalanceStyleTier);
        for (int i = 0; i < ModelCount; ++i) { Assert.True(s.Config.Models[i]); Assert.Equal(0, s.Counters.ModelCompleted[i]); }
        for (int i = 0; i < CoverageCount; ++i) { Assert.True(s.Config.Coverage[i]); Assert.Equal(0, s.Counters.CoverageCompleted[i]); }
        Assert.False(s.Pending.Active); Assert.Equal((int)Model.StatsOnly, s.Pending.Model); Assert.Equal((int)Coverage.Duel, s.Pending.ActualCoverage);
        Assert.Equal(0, s.Counters.Opened); Assert.Equal(0.0, s.Counters.SchedulingHours);
        Assert.Equal(3, ModelCount); Assert.Equal(4, CoverageCount);
    }

    [Fact]   // TownDiagnosticRun.cpp:121 (const Pending p = s.pending) and :63 (s = State(); s.config = config): value copies
    public void Structs_copy_by_value_including_the_fixed_arrays()
    {
        var s = new State();
        State copy = s;
        copy.Config.Models[1] = false; copy.Counters.ModelCompleted[2] = 9; copy.Pending.ScoreA[0] = 4;
        Assert.True(s.Config.Models[1]); Assert.Equal(0, s.Counters.ModelCompleted[2]); Assert.Equal(0.0, s.Pending.ScoreA[0]);
    }

    [Fact]   // TownDiagnosticRun.cpp:10-36
    public void Masks_counts_and_least_used()
    {
        var s = new State();
        Assert.True(Mutable(s));
        s.Config.Models[1] = false; s.Config.Coverage[0] = false; s.Config.Coverage[3] = false;
        Assert.Equal(2, EnabledModels(s.Config)); Assert.Equal(2, EnabledCoverage(s.Config));
        Assert.Equal(0b101, ModelMask(s.Config)); Assert.Equal(0b0110, CoverageMask(s.Config));
        s.Counters.ModelCompleted[0] = 3; s.Counters.ModelCompleted[1] = 0; s.Counters.ModelCompleted[2] = 3;
        Assert.Equal(0, LeastModel(s));              // ties keep the first enabled (strict <); model 1 is disabled
        s.Counters.CoverageCompleted[1] = 5; s.Counters.CoverageCompleted[2] = 4;
        Assert.Equal(2, LeastCoverage(s));
        for (int i = 0; i < ModelCount; ++i) s.Config.Models[i] = false;
        Assert.Equal(-1, LeastModel(s));
        s.Status = (int)RunStatus.Paused; Assert.False(Mutable(s));
        s.Status = (int)RunStatus.Complete; Assert.True(Mutable(s));
        s.Status = (int)RunStatus.Active; Assert.False(Mutable(s)); Assert.True(IsActive(s));
    }

    [Fact]   // TownDiagnosticRun.cpp:42-50
    public void Validation_error_texts_in_the_cpp_order()
    {
        var s = new State();
        Assert.Equal("Load or start a game first.", ValidationError(s, false, true));
        Assert.Equal("Wait for the active bout to finish or cancel the player booking.", ValidationError(s, true, true));
        s.Config.Target = 0; Assert.Equal("Target must be between 1 and 10000.", ValidationError(s, true, false));
        s.Config.Target = 10001; Assert.Equal("Target must be between 1 and 10000.", ValidationError(s, true, false));
        s.Config.Target = 10000;
        for (int i = 0; i < ModelCount; ++i) s.Config.Models[i] = false;
        Assert.Equal("Enable at least one MMR model.", ValidationError(s, true, false));
        s.Config.Models[2] = true;
        for (int i = 0; i < CoverageCount; ++i) s.Config.Coverage[i] = false;
        Assert.Equal("Enable at least one roster bucket.", ValidationError(s, true, false));
        s.Config.Coverage[0] = true;
        Assert.Equal("", ValidationError(s, true, false));
    }

    [Fact]   // TownDiagnosticRun.cpp:42-50 through the host (the glue's ValidationError, task 8.7a): the C++'s reads, in its order, only as needed
    public void Validation_error_through_the_host_asks_in_the_cpp_order()
    {
        var s = new State();
        var host = new Host { Save = false };
        Assert.Equal("Load or start a game first.", ValidationError(s, host));
        Assert.Equal(new[] { "save" }, host.Calls);
        host = new Host { Busy = true };
        Assert.Equal("Wait for the active bout to finish or cancel the player booking.", ValidationError(s, host));
        Assert.Equal(new[] { "save", "busy" }, host.Calls);
        host = new Host { Booking = true };
        Assert.Equal("Wait for the active bout to finish or cancel the player booking.", ValidationError(s, host));
        Assert.Equal(new[] { "save", "busy", "booking" }, host.Calls);
        host = new Host();
        s.Config.Target = 0;
        Assert.Equal("Target must be between 1 and 10000.", ValidationError(s, host));
        s.Config.Target = 150;
        Assert.Equal("", ValidationError(s, host));
        Assert.Equal(new[] { "save", "busy", "booking", "save", "busy", "booking" }, host.Calls);   // nothing else is asked or written
    }

    [Fact]   // TownDiagnosticRun.cpp:51-76
    public void Start_resets_the_state_keeps_the_config_and_logs_the_run()
    {
        var s = Active(5);
        var host = new Host();
        Assert.False(StartConfigured(ref s, false, 1000, host));     // not mutable while active
        Assert.Empty(host.Calls);

        s = new State(); s.RunId = 5; s.Counters.Opened = 9; s.Config.Target = 40; s.Config.Models[0] = false; s.Config.ReducedInfluence = .5;
        s.Config.UnderdogPercent = 30; s.Config.BalanceStyleTier = false; s.NextSequence = 12;
        host.Planned = true;
        var log = new Log();
        Assert.True(StartConfigured(ref s, true, 0x12345678u, host, log.Add));
        Assert.Equal(new[] { "save", "busy", "booking", "planned", "cancel:Replaced by controlled diagnostic run", "planned", "reset", "clear" }, host.Calls);
        // (0x12345678 ^ (5 * 1664525 + 1013904223 = 1022226848)) | 1, worked by hand
        const uint expected = 786016217u;
        Assert.Equal(expected, s.RunId);
        Assert.Equal((int)RunStatus.Active, s.Status); Assert.Equal(1u, s.NextSequence); Assert.Equal(0, s.Counters.Opened);
        Assert.Equal(40, s.Config.Target); Assert.False(s.Config.Models[0]);
        var e = Assert.Single(log.Events);
        Assert.Equal("diagnostic_run_started", e.name);
        Assert.Equal(",\"diagnostic_run_id\":" + expected + ",\"target\":40,\"reset_town_records\":1,\"model_mask\":6,\"coverage_mask\":15," +
            "\"reduced_influence\":0.500000,\"underdog_percent\":30,\"balance_style_tier\":0", e.fields);
    }

    [Fact]   // TownDiagnosticRun.cpp:53, :57-59, :62
    public void Start_refuses_on_validation_a_planned_bout_that_stays_or_a_failed_reset()
    {
        var s = new State();
        var host = new Host { Save = false };
        Assert.False(StartConfigured(ref s, false, 1, host));
        Assert.Equal(new[] { "save" }, host.Calls);   // IsBusy / HasBooking are not asked without a save
        host = new Host { Busy = true };
        Assert.False(StartConfigured(ref s, false, 1, host));
        Assert.Equal(new[] { "save", "busy" }, host.Calls);
        host = new Host { Booking = true };
        Assert.False(StartConfigured(ref s, false, 1, host));
        Assert.Equal(new[] { "save", "busy", "booking" }, host.Calls);
        host = new Host { Planned = true, CancelClears = false };
        Assert.False(StartConfigured(ref s, false, 1, host));
        host = new Host { ResetOk = false };
        Assert.False(StartConfigured(ref s, true, 1, host));
        Assert.Equal((int)RunStatus.Off, s.Status);
        Assert.True(StartConfigured(ref s, false, 1, new Host()));   // no reset asked when resetTownRecords is false
        Assert.Equal(1013904223u, s.RunId);                          // (1 ^ (0 * 1664525 + 1013904223)) | 1
    }

    [Fact]   // TownDiagnosticRun.cpp:71-76
    public void Preset_replaces_the_config_with_the_defaults()
    {
        var s = new State(); s.Config.Target = 7; s.Config.Coverage[2] = false; s.Config.UnderdogPercent = 0;
        Assert.True(StartPreset(ref s, false, 3, new Host()));
        Assert.Equal(150, s.Config.Target); Assert.True(s.Config.Coverage[2]); Assert.Equal(20, s.Config.UnderdogPercent);
        var active = Active();
        Assert.False(StartPreset(ref active, false, 3, new Host()));
    }

    [Fact]   // TownDiagnosticRun.cpp:77-79
    public void Pause_resume_end()
    {
        var log = new Log();
        var s = Active(41);
        s.Pending.Active = true;
        Assert.False(Pause(ref s, log.Add)); Assert.False(End(ref s, log.Add));
        s.Pending.Active = false;
        Assert.True(Pause(ref s, log.Add)); Assert.Equal((int)RunStatus.Paused, s.Status);
        Assert.False(Pause(ref s, log.Add));
        Assert.True(Resume(ref s, log.Add)); Assert.False(Resume(ref s, log.Add));
        s.Counters.Completed = 12;
        Assert.True(End(ref s, log.Add)); Assert.Equal((int)RunStatus.Ended, s.Status);
        Assert.False(End(ref s, log.Add));
        Assert.Equal(new[]
        {
            ("diagnostic_run_paused", ",\"diagnostic_run_id\":41"),
            ("diagnostic_run_resumed", ",\"diagnostic_run_id\":41"),
            ("diagnostic_run_ended", ",\"diagnostic_run_id\":41,\"completed\":12"),
        }, log.Events);
        var paused = Active(); paused.Status = (int)RunStatus.Paused;
        Assert.True(End(ref paused));
    }

    [Fact]   // TownDiagnosticRun.cpp:80-85
    public void Settings_change_only_while_mutable()
    {
        var s = new State();
        Assert.False(SetTarget(ref s, 0)); Assert.False(SetTarget(ref s, 10001)); Assert.True(SetTarget(ref s, 10000)); Assert.Equal(10000, s.Config.Target);
        Assert.False(ToggleModel(ref s, -1)); Assert.False(ToggleModel(ref s, 3));
        Assert.True(ToggleModel(ref s, 0)); Assert.True(ToggleModel(ref s, 1));
        Assert.False(ToggleModel(ref s, 2)); Assert.True(s.Config.Models[2]);   // the last enabled model is kept on
        Assert.True(ToggleModel(ref s, 1)); Assert.True(s.Config.Models[1]);
        Assert.True(ToggleCoverage(ref s, 0)); Assert.True(ToggleCoverage(ref s, 1)); Assert.True(ToggleCoverage(ref s, 2));
        Assert.False(ToggleCoverage(ref s, 3)); Assert.True(s.Config.Coverage[3]); Assert.False(ToggleCoverage(ref s, 4));
        double[] seen = new double[5];
        s.Config.ReducedInfluence = .1;
        for (int i = 0; i < 5; ++i) { Assert.True(CycleReducedInfluence(ref s)); seen[i] = s.Config.ReducedInfluence; }
        Assert.Equal(new[] { .25, .50, .75, 0, .25 }, seen);
        s.Config.ReducedInfluence = .74; CycleReducedInfluence(ref s); Assert.Equal(0.0, s.Config.ReducedInfluence);
        int[] pct = new int[7];
        for (int i = 0; i < 7; ++i) { CycleUnderdogPercent(ref s); pct[i] = s.Config.UnderdogPercent; }
        Assert.Equal(new[] { 30, 40, 50, 0, 10, 20, 30 }, pct);
        Assert.True(ToggleStyleTierBalance(ref s)); Assert.False(s.Config.BalanceStyleTier);
        var active = Active();
        Assert.False(SetTarget(ref active, 5)); Assert.False(ToggleModel(ref active, 0)); Assert.False(ToggleCoverage(ref active, 0));
        Assert.False(CycleReducedInfluence(ref active)); Assert.False(CycleUnderdogPercent(ref active)); Assert.False(ToggleStyleTierBalance(ref active));
        Assert.Equal(150, active.Config.Target);
    }

    [Fact]   // TownDiagnosticRun.cpp:86-98
    public void Next_request_rotates_models_coverage_types_and_tiers()
    {
        var s = new State();
        Assert.False(NextRequest(s).Active);
        s = Active(1000);
        s.Config.ReducedInfluence = .5; s.Config.Coverage[0] = false; s.Config.UnderdogPercent = 20;
        s.Counters.ModelCompleted[0] = 2; s.Counters.CoverageCompleted[1] = 1;
        s.NextSequence = 1;
        TownMatchmakingPolicy.DiagnosticRequest r = NextRequest(s);
        Assert.True(r.Active); Assert.Equal(1, r.Model); Assert.Equal(.5, r.Influence); Assert.Equal(.5, r.ReducedInfluence);
        Assert.Equal(2, r.Coverage); Assert.False(r.EnabledCoverage[0]); Assert.True(r.EnabledCoverage[1]);
        Assert.True(r.Underdog);                    // (0 * 37) % 100 = 0 < 20
        Assert.Equal(0, r.DesiredType); Assert.Equal(0, r.DesiredTier); Assert.Equal(1000u ^ 1u, r.Seed); Assert.True(r.BalanceStyleTier);
        s.NextSequence = 2; r = NextRequest(s);
        Assert.False(r.Underdog);                   // 37 >= 20
        Assert.Equal(1, r.DesiredType);
        Assert.Equal(8, TownMatchmakingPolicy.Detail.TypeCount());   // the type round-robin below assumes eight types
        s.NextSequence = 9 + 8; r = NextRequest(s);   // (16 * 37) % 100 = 92; type 16 % 8 = 0, tier (16 / 8) % 3 = 2
        Assert.False(r.Underdog); Assert.Equal(0, r.DesiredType); Assert.Equal(2, r.DesiredTier);
        s.NextSequence = 0; r = NextRequest(s);       // unsigned wrap: 0 - 1 = 4294967295
        // 4294967295 * 37 mod 2^32 = 4294967259, % 100 = 59 (not < 20); 4294967295 % 8 = 7; 4294967295 / 8 = 536870911, % 3 = 1
        Assert.False(r.Underdog);
        Assert.Equal(7, r.DesiredType); Assert.Equal(1, r.DesiredTier);
        s.Counters.ModelCompleted[1] = 5; s.Counters.ModelCompleted[2] = 1; s.NextSequence = 3; r = NextRequest(s);
        Assert.Equal(2, r.Model); Assert.Equal(1.0, r.Influence);
        s.Counters.ModelCompleted[0] = 0; r = NextRequest(s);
        Assert.Equal(0, r.Model); Assert.Equal(0.0, r.Influence);
        s.Pending.Active = true; Assert.False(NextRequest(s).Active);
        s.Pending.Active = false; s.Counters.Completed = 150; Assert.False(NextRequest(s).Active);
    }

    [Fact]   // TownDiagnosticRun.cpp:99-106
    public void Assign_opens_a_pending_bout_and_measures_downtime()
    {
        var s = Active();
        Match m = DiagnosticMatch(2, 3);
        Assign(ref s, m, 10);
        Assert.True(s.Pending.Active); Assert.Equal(1u, s.Pending.Sequence); Assert.Equal(2u, s.NextSequence);
        Assert.Equal(1, s.Pending.Model); Assert.Equal(1, s.Pending.RequestedCoverage); Assert.Equal(2, s.Pending.ActualCoverage);
        Assert.True(s.Pending.Underdog); Assert.Equal(2, s.Pending.SizeA); Assert.Equal(3, s.Pending.SizeB); Assert.Equal(10.0, s.Pending.AnnouncedHours);
        Assert.Equal(12.0, s.Pending.ScoreA[2]); Assert.Equal(21.0, s.Pending.ScoreB[1]); Assert.Equal(.7, s.Pending.ProbabilityA[0]);
        Assert.Equal(1, s.Counters.Opened); Assert.Equal(0, s.Counters.SchedulingSamples);   // lastCompletedHours is 0
        Assign(ref s, m, 11); Assert.Equal(1, s.Counters.Opened);                            // one pending at a time
        s.Pending = new Pending(); s.Counters.LastCompletedHours = 8;
        Assign(ref s, m, 9.5);
        Assert.Equal(1.5, s.Counters.SchedulingHours); Assert.Equal(1, s.Counters.SchedulingSamples);
        s.Pending = new Pending();
        Assign(ref s, m, 7);                          // earlier than the last completion: not a sample
        Assert.Equal(1, s.Counters.SchedulingSamples);
        s.Pending = new Pending();
        Match notDiagnostic = DiagnosticMatch(1, 1); notDiagnostic.Diagnostic = false;
        Assign(ref s, notDiagnostic, 20); Assert.False(s.Pending.Active);
        Match invalid = DiagnosticMatch(1, 1); invalid.Valid = false;
        Assign(ref s, invalid, 20); Assert.False(s.Pending.Active);
        var paused = Active(); paused.Status = (int)RunStatus.Paused;
        Assign(ref paused, m, 1); Assert.False(paused.Pending.Active);
    }

    [Fact]   // TownDiagnosticRun.cpp:107-119
    public void Cancel_retry_recover_abort()
    {
        var log = new Log();
        var s = Active(9);
        CancelPending(ref s); RetryPending(ref s); RecoverPendingAfterLoad(ref s, log.Add); AbortPending(ref s);
        Assert.Equal(0, s.Counters.Cancelled); Assert.Equal(0, s.Counters.Aborted); Assert.Empty(log.Events);

        Assign(ref s, DiagnosticMatch(1, 1), 1);
        CancelPending(ref s);
        Assert.False(s.Pending.Active); Assert.Equal(1, s.Counters.Cancelled); Assert.Equal(2u, s.NextSequence);

        Assign(ref s, DiagnosticMatch(1, 1), 1);       // sequence 2
        RetryPending(ref s);
        Assert.Equal(2u, s.NextSequence); Assert.Equal(2, s.Counters.Cancelled);   // the sequence is handed out again
        Assign(ref s, DiagnosticMatch(1, 1), 1);       // sequence 2 again
        s.NextSequence = 7;
        RetryPending(ref s);
        Assert.Equal(7u, s.NextSequence);              // only when it was the last one handed out

        Assign(ref s, DiagnosticMatch(1, 1), 1);       // sequence 7
        RecoverPendingAfterLoad(ref s, log.Add);
        Assert.Equal(4, s.Counters.Cancelled);
        Assert.Equal(("diagnostic_assignment_cancelled", ",\"diagnostic_run_id\":9,\"diagnostic_sequence\":7,\"reason\":\"reload_does_not_restore_announced_bout\""),
            Assert.Single(log.Events));

        Assign(ref s, DiagnosticMatch(1, 1), 1);
        AbortPending(ref s);
        Assert.Equal(1, s.Counters.Aborted); Assert.False(s.Pending.Active);
    }

    static SparPodium.Snapshot Outcome(SparPodium.OutcomeKind kind) => new SparPodium.Snapshot { Outcome = kind };

    [Fact]   // TownDiagnosticRun.cpp:120-136
    public void Complete_counts_winners_sizes_favourites_and_finishes_the_run()
    {
        var log = new Log();
        var s = Active(3); s.Config.Target = 3;
        Assign(ref s, DiagnosticMatch(2, 3, model: 1, probabilityA: .5), 5);
        CompletePending(ref s, Outcome(SparPodium.OutcomeKind.TeamAWins), 6, log.Add);
        Assert.False(s.Pending.Active);
        Assert.Equal(1, s.Counters.Completed); Assert.Equal(1, s.Counters.ModelCompleted[1]); Assert.Equal(1, s.Counters.CoverageCompleted[2]);
        Assert.Equal(1, s.Counters.TeamAWins); Assert.Equal(1, s.Counters.SmallerWins); Assert.Equal(1, s.Counters.FavoriteWins);   // .5 counts as A favoured
        Assert.Equal(6.0, s.Counters.LastCompletedHours);

        Assign(ref s, DiagnosticMatch(2, 3, model: 0, probabilityA: .4), 7);
        CompletePending(ref s, Outcome(SparPodium.OutcomeKind.TeamBWins), 8, log.Add);
        Assert.Equal(1, s.Counters.TeamBWins); Assert.Equal(1, s.Counters.LargerWins); Assert.Equal(2, s.Counters.FavoriteWins);

        Assign(ref s, DiagnosticMatch(2, 2, model: 0, probabilityA: .9), 9);
        CompletePending(ref s, Outcome(SparPodium.OutcomeKind.Draw), 10, log.Add);   // neither side: dropped, nothing counted
        Assert.Equal(2, s.Counters.Completed); Assert.Equal(8.0, s.Counters.LastCompletedHours);

        Assign(ref s, DiagnosticMatch(2, 2, relaxation: 3), 9);
        CompletePending(ref s, Outcome(SparPodium.OutcomeKind.TeamAWins), 10, log.Add);  // relaxation 3 (ordinary fallback) is not counted
        Assert.Equal(2, s.Counters.Completed); Assert.False(s.Pending.Active);
        Assert.Empty(log.Events);

        Assign(ref s, DiagnosticMatch(2, 2, model: 2, probabilityA: .9), 11);
        CompletePending(ref s, Outcome(SparPodium.OutcomeKind.TeamBWins), 12, log.Add);
        Assert.Equal(1, s.Counters.EqualWins); Assert.Equal(2, s.Counters.FavoriteWins);
        Assert.Equal((int)RunStatus.Complete, s.Status);
        var e = Assert.Single(log.Events);
        Assert.Equal("diagnostic_run_completed", e.name);
        Assert.Equal(",\"diagnostic_run_id\":3,\"completed\":3,\"cancelled\":0,\"aborted\":0,\"model_stats\":1,\"model_reduced_mmr\":1,\"model_full_mmr\":1," +
            "\"coverage_1v1\":0,\"coverage_equal_group\":0,\"coverage_difference_one\":3,\"coverage_difference_large\":0", e.fields);

        var idle = Active(); CompletePending(ref idle, Outcome(SparPodium.OutcomeKind.TeamAWins), 1);
        Assert.Equal(0, idle.Counters.Completed);
    }

    [Fact]   // TownDiagnosticRun.cpp:137-145
    public void Status_text_byte_for_byte()
    {
        var s = Active(123);
        s.Config.Target = 50;
        s.Counters.Completed = 7; s.Counters.ModelCompleted[0] = 1; s.Counters.ModelCompleted[1] = 2; s.Counters.ModelCompleted[2] = 4;
        s.Counters.CoverageCompleted[0] = 3; s.Counters.CoverageCompleted[1] = 1; s.Counters.CoverageCompleted[2] = 2; s.Counters.CoverageCompleted[3] = 1;
        s.Counters.Opened = 9; s.Counters.Cancelled = 1; s.Counters.Aborted = 1; s.Counters.TeamAWins = 4; s.Counters.TeamBWins = 3;
        s.Counters.LargerWins = 2; s.Counters.SmallerWins = 3; s.Counters.FavoriteWins = 5;
        s.Counters.SchedulingHours = 5; s.Counters.SchedulingSamples = 4;   // 1.25 -> "1.3" (msvcr100 %.1f rounds the tie half away from zero)
        Assert.Equal("Diagnostic: active | Run 123 | 7/50 complete\nModels: stats 1, reduced 2, full 4 | Coverage: 1v1 3, equal 1, diff-1 2, diff-2+ 1\n" +
            "Markets: 9 opened, 1 cancelled, 1 aborted | Wins A/B 4/3 | Larger/smaller 2/3 | Favorites 5 | Avg downtime 1.3 game hours", StatusText(s, ""));
        s.Counters.SchedulingSamples = 0; s.Status = 9;
        Assert.EndsWith("Avg downtime 0.0 game hours", StatusText(s, ""));
        Assert.StartsWith("Diagnostic: invalid | Run 123", StatusText(s, ""));
        Assert.Equal("Diagnostic disabled: bad save\nOrdinary arena matchmaking remains active. Configure and start a new run to replace the invalid diagnostic state.",
            StatusText(s, "bad save"));
    }

    [Fact]   // TownDiagnosticRun.cpp:65
    public void Run_id_formula()
    {
        Assert.Equal(1013904223u, RunId(0, 0));                       // (0 ^ 1013904223) | 1
        Assert.Equal(1u, RunId(1013904223u, 0));                      // 1013904223 ^ 1013904223 = 0, then | 1
        // 0xFFFFFFFF * 1664525 + 1013904223 mod 2^32 = 1012239698; 0xFFFFFFFF ^ that = 3282727597 (odd)
        Assert.Equal(3282727597u, RunId(0xFFFFFFFFu, 0xFFFFFFFFu));
    }
}
