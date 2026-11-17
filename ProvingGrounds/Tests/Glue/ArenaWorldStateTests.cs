using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.ArenaPersistence;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The world's arena state and its load / save outcomes (task 2.4, ArenaNativePersistence.cpp ActivateLoad / Save, LeaderboardStore
/// activate / block / SetRating / SetMarks).</summary>
public class ArenaWorldStateTests
{
    readonly List<string> logs = [];
    readonly List<byte[]> recovered = [];

    ArenaWorldState New() => new(logs.Add, b => { recovered.Add(b); return "C:/mod/recovered/20261112-150405.json"; });

    static byte[] Payload(Snapshot s, params string[] tokens)
    {
        Assert.True(ArenaSaveData.TryWrite(s, tokens, out byte[] p, out string e), e);
        return p;
    }

    static Snapshot Rated()
    {
        var s = new Snapshot { SaveKey = ArenaSaveData.InSaveKey, Generation = "gen-1" };
        s.Challenges.Seed = 4242;
        s.Fighters.PlayerStandings.Add(new LeaderboardData.Standing { Id = "tok-a", Name = "Mu", Mmr = 150.0f });
        s.Fighters.Progression.Add(new LeaderboardData.Progression { Id = "tok-a", Name = "Mu", Marks = 7 });
        return s;
    }

    static byte[] Garbage() => [1, (byte)'{', (byte)'x', 0, (byte)'{', (byte)'}'];

    [Fact]
    public void NewGameSeedsTheCardFromTheTickCount()
    {
        var w = New();
        w.Resolve(loadedFromSave: false, tickCount: 1234);
        Assert.True(w.Ready);
        Assert.Equal(1235u, w.Ledger.State().Challenges.Seed);
        Assert.Equal("", w.PersistenceError);
        w.Resolve(false, 9999);   // only the first step decides
        Assert.Equal(1235u, w.Ledger.State().Challenges.Seed);
    }

    [Fact]
    public void ASaveWithoutModDataStartsFromTheDefaultSnapshotSeedOne()
    {
        var w = New();
        w.Resolve(loadedFromSave: true, tickCount: 1234);
        Assert.True(w.Ready);
        Assert.Equal(1u, w.Ledger.State().Challenges.Seed);
        Assert.Empty(w.Data.PlayerStandings);
    }

    [Fact]
    public void LoadedDataIsAppliedAtTheFirstStepIntoTheSameObjects()
    {
        var w = New();
        var data = w.Data;
        var r = w.Read(Payload(Rated(), "tok-a"));
        Assert.Equal(new[] { "tok-a" }, r.Tokens);
        Assert.Empty(w.Data.PlayerStandings);   // nothing live changes in Read
        w.Resolve(true, 5);
        Assert.Same(data, w.Data);
        Assert.Equal(150.0f, w.Data.PlayerStandings[0].Mmr);
        Assert.Equal(4242u, w.Ledger.State().Challenges.Seed);   // the saved card, not a new seed
        Assert.Contains("Proving Grounds persistence: Loaded arena snapshot from the save generation=gen-1", logs);
        Assert.Equal("", w.TakeNotice());
    }

    [Fact]
    public void TheAnnouncedTownBoutIsCapturedIntoTheSaveAndRestoredAfterTheLoadedSnapshot()   // task 7.3: ArenaNativePersistence.cpp:205-206, :360-361
    {
        var w = New();
        w.CapturePlannedNpc = p => { p.Active = true; p.RegistryId = "town:10.34:30290"; p.TeamA.Add("fid-1"); p.TeamB.Add("fid-2"); p.StartHours = 101.5; p.ScoreA = 50; p.ScoreB = 51; return true; };
        w.Resolve(false, 1);
        Assert.True(w.TryWrite("gen-2", [], out byte[] payload, out string writeError), writeError);
        var saved = ArenaSaveData.Read(payload).Snapshot.PlannedNpc;
        Assert.True(saved.Active);
        Assert.Equal("town:10.34:30290", saved.RegistryId);
        Assert.Equal(101.5, saved.StartHours);

        var loaded = New();
        PlannedNpcBout? restored = null;
        int seedAtRestore = 0;
        loaded.RestorePlannedNpc = p => { restored = p; seedAtRestore = (int)loaded.Ledger.State().Challenges.Seed; logs.Add("restored"); };
        loaded.Read(payload);
        Assert.Null(restored);   // applied at the first step, not in Read
        loaded.Resolve(true, 5);
        Assert.NotNull(restored);
        Assert.Equal(new[] { "fid-1" }, restored!.TeamA);
        Assert.NotEqual(0, seedAtRestore);   // the snapshot was activated before the restore
        Assert.True(logs.IndexOf("restored") < logs.FindIndex(l => l.Contains("Loaded arena snapshot")));   // C++ order: activate, restore, log
    }

    [Fact]
    public void ABoutLoadedButAppliedByASaveBeforeTheFirstStepIsWrittenBackAndRestoredOnceAtTheFirstStep()   // task 7.3 quality review
    {
        var w = New();
        w.CapturePlannedNpc = p => { p.Active = true; p.RegistryId = "town:10.34:30290"; p.TeamA.Add("fid-1"); p.TeamB.Add("fid-2"); p.StartHours = 101.5; p.ScoreA = 50; p.ScoreB = 51; return true; };
        w.Resolve(false, 1);
        Assert.True(w.TryWrite("gen-2", [], out byte[] payload, out _));

        var loaded = New();
        var restored = new List<PlannedNpcBout>();
        loaded.RestorePlannedNpc = restored.Add;
        loaded.CapturePlannedNpc = p => { p.AssignFrom(new PlannedNpcBout()); return true; };   // TownArena before the restore: nothing planned
        loaded.Read(payload);
        Assert.True(loaded.TryWrite("gen-3", [], out byte[] early, out _));   // a save before the first step applies the snapshot ...
        Assert.Empty(restored);                                               // ... holds the bout (no world yet) ...
        Assert.True(loaded.HoldsLateRestore);
        var written = ArenaSaveData.Read(early).Snapshot.PlannedNpc;          // ... and writes it back (C++ TownArena.cpp:597)
        Assert.True(written.Active);
        Assert.Equal(new[] { "fid-1" }, written.TeamA);
        Assert.Contains(logs, l => l.Contains("its announced town bout waits for that step"));
        loaded.Resolve(true, 5);
        Assert.Single(restored);
        Assert.Equal(101.5, restored[0].StartHours);
        Assert.False(loaded.HoldsLateRestore);
        loaded.Resolve(true, 6);
        Assert.Single(restored);   // once
    }

    [Fact]
    public void AFailedTownBoutCaptureLeavesTheArenaPartUnwritten()   // task 7.3: ArenaNativePersistence.cpp:205-209
    {
        var w = New();
        w.Resolve(false, 1);
        w.CapturePlannedNpc = _ => false;
        Assert.False(w.TryWrite("gen-2", [], out byte[] payload, out string error));
        Assert.Empty(payload);
        Assert.Equal("the arena ledger, announced lineup identity, or snapshot generation could not be captured", error);
        Assert.StartsWith("Arena portion of save unavailable: ", w.TakeNotice());
    }

    [Fact]
    public void AWriteBeforeTheFirstStepWritesTheLoadedData()
    {
        var w = New();
        w.Read(Payload(Rated(), "tok-a"));
        Assert.True(w.TryWrite("gen-2", ["tok-a"], out byte[] p, out _));
        var back = ArenaSaveData.Read(p);
        Assert.Equal(150.0f, back.Snapshot.Fighters.PlayerStandings[0].Mmr);
        Assert.Equal("gen-2", back.Snapshot.Generation);
    }

    [Fact]
    public void UnreadableDataBlocksTheArenaKeepsTheBytesAndReports()
    {
        var w = New();
        byte[] bad = Garbage();
        var r = w.Read(bad);
        Assert.Equal(ArenaSaveData.ReadKind.Unreadable, r.Kind);
        Assert.Single(recovered);
        Assert.Same(bad, recovered[0]);   // kept at load
        Assert.Contains(logs, l => l.StartsWith("PG: saved arena data unreadable: "));
        w.Resolve(true, 5);
        Assert.False(w.Ready);
        Assert.StartsWith("C:/mod/recovered/20261112-150405.json $", w.PersistenceError);
        Assert.False(w.ResetOffered);   // reset only for WrongSnapshot
        string notice = w.TakeNotice();
        Assert.Equal("Arena progress blocked: " + w.PersistenceError
            + ". Preserve the file/backups or use the leaderboard's explicit reset to start arena progress from zero for this slot.", notice);
        Assert.False(w.ResetUnmatched());
        Assert.False(w.Ready);
    }

    [Fact]
    public void ARecoverFailureNamesTheSavedData()
    {
        var w = new ArenaWorldState(logs.Add, _ => null);
        w.Read(Garbage());
        w.Resolve(true, 5);
        Assert.StartsWith("the saved arena data $", w.PersistenceError);
    }

    [Fact]
    public void ABlockedWorldRefusesRatingsAndMarksWithoutMakingAnIdentity()
    {
        var w = New();
        w.Read(Garbage());
        w.Resolve(true, 5);
        bool asked = false;
        Assert.Equal(ArenaWorldState.SetResult.NotReady, w.SetRating(() => { asked = true; return "tok-a"; }, "Mu", 150));
        Assert.Equal(ArenaWorldState.SetResult.NotReady, w.SetMarks(() => { asked = true; return "tok-a"; }, "Mu", 7));
        Assert.False(asked);
        Assert.Empty(w.Data.PlayerStandings);
        Assert.Empty(w.Data.Progression);
    }

    [Fact]
    public void ABlockedWorldWritesNothingAndReportsUnavailable()
    {
        var w = New();
        w.Read(Garbage());
        Assert.False(w.TryWrite("gen-2", [], out byte[] p, out string error));   // also before the first step
        Assert.Empty(p);
        Assert.Equal(w.PersistenceError, error);
        Assert.Equal("Arena portion of save unavailable: " + error
            + ". Wait for any reward delivery to finish, then retry saving. Native game save will continue; arena data will not be published.", w.TakeNotice());
        w.Resolve(true, 5);
        Assert.False(w.TryWrite("gen-3", [], out _, out _));
        Assert.Single(recovered);   // the write recovers nothing more
    }

    [Fact]
    public void LegacyDataStartsFreshAndIsKeptAtTheNextSave()
    {
        var w = New();
        byte[] legacy = [1, .. System.Text.Encoding.UTF8.GetBytes("{\"version\":10}"), 0, .. System.Text.Encoding.UTF8.GetBytes("{\"fighterIdentities\":[]}")];
        var r = w.Read(legacy);
        Assert.Equal(ArenaSaveData.ReadKind.LegacyReset, r.Kind);
        Assert.Empty(recovered);   // not at load
        Assert.Equal("Legacy arena progress reset. Original JSON will be backed up when this slot is next saved.", w.TakeNotice());
        w.Resolve(true, 5);
        Assert.True(w.Ready);
        Assert.Equal(1u, w.Ledger.State().Challenges.Seed);
        Assert.False(w.ResetOffered);
        Assert.True(w.TryWrite("gen-2", [], out _, out _));
        Assert.Single(recovered);
        Assert.Same(legacy, recovered[0]);
        Assert.True(w.TryWrite("gen-3", [], out _, out _));
        Assert.Single(recovered);   // once
    }

    [Fact]
    public void SettersClampAndRefuseWithoutIdentity()
    {
        var w = New();
        w.Resolve(false, 1);
        Assert.Equal(ArenaWorldState.SetResult.NoIdentity, w.SetRating(() => null, "Mu", 150));
        Assert.Equal(ArenaWorldState.SetResult.NoIdentity, w.SetMarks(() => "", "Mu", 7));
        Assert.Equal(ArenaWorldState.SetResult.Updated, w.SetRating(() => "tok-a", "Mu", 12000));
        Assert.Equal(9999.0f, w.Data.PlayerStandings[0].Mmr);
        w.SetRating(() => "tok-a", "Mu", -5);
        Assert.Equal(0.0f, w.Data.PlayerStandings[0].Mmr);
        w.SetRating(() => "tok-a", "Mu", 150.5f);
        Assert.Equal(150.5f, w.Data.PlayerStandings[0].Mmr);
        Assert.Single(w.Data.PlayerStandings);
        w.SetMarks(() => "tok-a", "Mu", 10001);
        Assert.Equal(9999, w.Data.Progression[0].Marks);
        w.SetMarks(() => "tok-a", "Mu", -1);
        Assert.Equal(0, w.Data.Progression[0].Marks);
        Assert.Contains("Proving Grounds: debug rating updated", logs);
        Assert.Contains("Proving Grounds: debug Arena Marks updated", logs);
    }

    [Fact]
    public void ResetOnlyForAWrongSnapshot()
    {
        var w = New();
        w.Resolve(false, 1);
        w.SetRating(() => "tok-a", "Mu", 150);
        Assert.False(w.ResetUnmatched());
        Assert.Single(w.Data.PlayerStandings);
        w.SetWrongSnapshotForTest();
        Assert.True(w.ResetOffered);
        Assert.True(w.ResetUnmatched());
        Assert.False(w.ResetOffered);
        Assert.Empty(w.Data.PlayerStandings);
        Assert.Contains("Proving Grounds persistence: Player explicitly reset unmatched arena progress; save this slot now to publish a matching sidecar.", logs);
    }

    [Fact]
    public void AThrowingDecoderFailsClosed()
    {
        var w = new ArenaWorldState(logs.Add, b => { recovered.Add(b); return "C:/mod/recovered/x.dat"; },
            _ => throw new NullReferenceException("boom"));
        byte[] bytes = Payload(Rated());
        var r = w.Read(bytes);
        Assert.Equal(ArenaSaveData.ReadKind.Unreadable, r.Kind);
        Assert.Equal("read failed: NullReferenceException: boom", r.Reason);
        Assert.Same(bytes, Assert.Single(recovered));
        Assert.False(w.TryWrite("g", [], out byte[] p, out string error));   // before the first step too
        Assert.Empty(p);
        Assert.Equal("C:/mod/recovered/x.dat read failed: NullReferenceException: boom", error);
        w.Resolve(true, 5);
        Assert.False(w.Ready);
        Assert.Contains("PG: saved arena data unreadable: read failed: NullReferenceException: boom", logs);
    }

    [Fact]
    public void ThePortsOwnLinesGoToTheirOwnLogAndTheCppLinesToTheOther()
    {
        var own = new List<string>();
        var w = new ArenaWorldState(logs.Add, _ => throw new IOException("disk full"), ownLog: own.Add);
        w.Read(Garbage());
        w.Resolve(true, 5);
        // task 2.7: the PG: lines (no C++ counterpart) -> Meitou's log; the C++ report (ArenaNativePersistence.cpp:387-395) -> PGLog
        Assert.Contains("PG: recovered data not kept: IOException: disk full", own);
        Assert.Contains(own, l => l.StartsWith("PG: saved arena data unreadable: ", StringComparison.Ordinal));
        Assert.All(own, l => Assert.StartsWith("PG: ", l));
        Assert.Contains(logs, l => l.StartsWith("Proving Grounds persistence: Arena progress blocked: ", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, l => l.StartsWith("PG: ", StringComparison.Ordinal));
    }

    [Fact]
    public void AThrowAfterALoadedDecodeStillBlocks()
    {
        var w = New();
        w.Read(Payload(Rated(), "tok-a"));            // Loaded: activation pending
        w.ReadFailed([], new InvalidOperationException("restore failed"));   // e.g. the identity restore threw (the glue's catch)
        w.Resolve(true, 5);
        Assert.False(w.Ready);
        Assert.Empty(w.Data.PlayerStandings);
        Assert.Equal("the saved arena data read failed: InvalidOperationException: restore failed", w.PersistenceError);
        Assert.Empty(recovered);   // nothing read: nothing to keep
    }

    [Fact]
    public void AThrowingRecoverCallbackStillBlocks()
    {
        var w = new ArenaWorldState(logs.Add, _ => throw new IOException("disk full"));
        w.Read(Garbage());
        w.Resolve(true, 5);
        Assert.False(w.Ready);
        Assert.StartsWith("the saved arena data $", w.PersistenceError);
        Assert.Contains("PG: recovered data not kept: IOException: disk full", logs);
    }

    static byte[] Legacy() => [1, .. System.Text.Encoding.UTF8.GetBytes("{\"version\":10}"), 0, .. System.Text.Encoding.UTF8.GetBytes("{\"fighterIdentities\":[]}")];

    [Fact]
    public void AFailedLegacyBackupFailsTheSaveAndTheNextSaveRetries()
    {
        bool ok = false;
        var w = new ArenaWorldState(logs.Add, b => { if (!ok) return null; recovered.Add(b); return "C:/mod/recovered/l.dat"; });
        byte[] legacy = Legacy();
        w.Read(legacy);
        w.TakeNotice();
        w.Resolve(true, 5);
        Assert.False(w.TryWrite("g1", [], out byte[] p, out string error));
        Assert.Empty(p);
        Assert.Equal("Could not back up the legacy arena snapshot", error);
        Assert.Equal("Arena portion of save FAILED: Could not back up the legacy arena snapshot. Live arena progress is retained. Preserve this slot and its backups; retry saving before leaving this world.", w.TakeNotice());
        Assert.True(w.Ready);   // live progress kept
        ok = true;
        Assert.True(w.TryWrite("g2", [], out p, out _));
        Assert.NotEmpty(p);
        Assert.Same(legacy, Assert.Single(recovered));
    }

    [Fact]
    public void AThrowingLegacyBackupFailsTheSaveToo()
    {
        var w = new ArenaWorldState(logs.Add, _ => throw new IOException("locked"));
        w.Read(Legacy());
        w.Resolve(true, 5);
        Assert.False(w.TryWrite("g1", [], out _, out _));
        Assert.False(w.TryWrite("g2", [], out _, out _));   // still kept for a retry
    }

    [Fact]
    public void ABadFrameGetsInvalidDataAndACodecErrorKeepsItsCode()
    {
        Assert.Equal(LoadCode.InvalidData, ArenaSaveData.Read([]).Code);
        Assert.Equal(LoadCode.InvalidJson, ArenaSaveData.Read(Garbage()).Code);
        byte[] newer = [1, .. System.Text.Encoding.UTF8.GetBytes("{\"version\":17}"), 0, .. System.Text.Encoding.UTF8.GetBytes("{\"fighterIdentities\":[]}")];
        Assert.Equal(LoadCode.UnsupportedVersion, ArenaSaveData.Read(newer).Code);
    }

    // ---------- task 8.7a: the diagnostic run after a load (ArenaNativePersistence.cpp:360-372) ----------

    readonly List<string> order = [];
    readonly List<(string Name, string Fields)> events = [];

    /// <summary>A world wired as the glue wires it: the bout's restore, the diagnostic recover (Core TownDiagnosticRun over the ledger's
    /// diagnostic state, which is what LeaderboardStore::GetTownDiagnostic returns while Ready) and the balance-log event, all in one order list.</summary>
    ArenaWorldState Wired(Func<byte[], ArenaSaveData.ReadResult>? decode = null)
    {
        var w = new ArenaWorldState(l => { logs.Add(l); order.Add(l); }, b => { recovered.Add(b); return "C:/mod/recovered/x.dat"; }, decode);
        w.RestorePlannedNpc = _ => order.Add("restored");
        w.RecoverDiagnosticAfterLoad = sink =>
        {
            order.Add("recover");
            TownDiagnosticRun.RecoverPendingAfterLoad(ref w.Ledger.State().Diagnostic, sink);
        };
        w.DiagnosticEvent = (name, fields) => { order.Add("event " + name); events.Add((name, fields)); };
        return w;
    }

    /// <summary>A saved run with a pending assignment (run 77, sequence 5, 2 cancelled before), with or without the announced bout it is for.</summary>
    static Snapshot WithPendingAssignment(bool announced)
    {
        var s = Rated();
        s.Diagnostic.Status = (int)TownDiagnosticData.RunStatus.Active; s.Diagnostic.RunId = 77; s.Diagnostic.NextSequence = 6;
        s.Diagnostic.Counters.Opened = 5; s.Diagnostic.Counters.Cancelled = 2;
        s.Diagnostic.Pending.Active = true; s.Diagnostic.Pending.Sequence = 5;
        if (announced)
        {
            s.PlannedNpc.Active = true; s.PlannedNpc.RegistryId = "town:10.34:30290"; s.PlannedNpc.TeamA.Add("fid-1"); s.PlannedNpc.TeamB.Add("fid-2");
            s.PlannedNpc.StartHours = 101.5; s.PlannedNpc.ScoreA = 50; s.PlannedNpc.ScoreB = 51;
        }
        return s;
    }

    const string LoadedLine = "Proving Grounds persistence: Loaded arena snapshot from the save generation=gen-1";
    const string CancelledFields = ",\"diagnostic_run_id\":77,\"diagnostic_sequence\":5,\"reason\":\"reload_does_not_restore_announced_bout\"";

    [Fact]   // ArenaNativePersistence.cpp:363 (if(!snapshot.plannedNpc.active)): the announced bout comes back with its assignment
    public void ALoadWithItsAnnouncedBoutKeepsTheDiagnosticAssignment()
    {
        var w = Wired();
        w.Read(Payload(WithPendingAssignment(announced: true)));
        w.Resolve(true, 5);
        Assert.Equal(new[] { "restored", LoadedLine }, order);
        Assert.Empty(events);
        Assert.True(w.Ledger.State().Diagnostic.Pending.Active);
        Assert.Equal(2, w.Ledger.State().Diagnostic.Counters.Cancelled);
    }

    [Fact]   // ArenaNativePersistence.cpp:360-367 + TownDiagnosticRun.cpp:113-118: no announced bout, so the pending assignment is cancelled, once
    public void ALoadWithNoAnnouncedBoutCancelsThePendingAssignmentOnce()
    {
        var w = Wired();
        w.Read(Payload(WithPendingAssignment(announced: false)));
        Assert.Empty(order);   // nothing in Read
        w.Resolve(true, 5);
        Assert.Equal(new[] { "restored", "recover", "event diagnostic_assignment_cancelled", LoadedLine }, order);   // the C++ order
        Assert.Equal(("diagnostic_assignment_cancelled", CancelledFields), Assert.Single(events));
        var d = w.Ledger.State().Diagnostic;
        Assert.False(d.Pending.Active);
        Assert.Equal(3, d.Counters.Cancelled);   // cancelled + 1
        Assert.Equal((int)TownDiagnosticData.RunStatus.Active, d.Status);   // the run goes on
        w.Resolve(true, 6);
        Assert.Single(events);
        Assert.Equal(3, w.Ledger.State().Diagnostic.Counters.Cancelled);
        Assert.Equal("", w.TakeNotice());
    }

    [Fact]   // ArenaNativePersistence.cpp:368-372: the event and the Report after the "Loaded" line, in the applied step, once
    public void ADiagnosticErrorInTheSnapshotWritesTheEventAndTheReportOnceAfterTheLoadedLine()
    {
        const string error = "$.diagnostic.config.target: out of range";
        var decoded = ArenaSaveData.Read(Payload(Rated()));
        decoded.Snapshot.DiagnosticError = error;   // what the codec sets for an invalid diagnostic block (the state then the default)
        var w = Wired(_ => decoded);
        w.Read([1]);
        Assert.Empty(order);
        Assert.Equal("", w.TakeNotice());   // the Report waits for the applied step, after the "Loaded" line (C++ :365-371)
        w.Resolve(true, 5);
        string report = "Diagnostic selection disabled: " + error + ". Ordinary arena matchmaking remains active.";
        Assert.Equal(new[] { "restored", "recover", LoadedLine, "event diagnostic_persistence_error", "Proving Grounds persistence: " + report }, order);
        Assert.Equal(("diagnostic_persistence_error", ",\"error\":\"invalid diagnostic state; see arena persistence log\""), Assert.Single(events));
        Assert.Equal(report, w.TakeNotice());
        w.Resolve(true, 6);
        Assert.Single(events);
        Assert.Equal("", w.TakeNotice());
    }

    [Fact]   // ArenaNativePersistence.cpp:374-395: the recover and the error event are the Ready branch's only
    public void NoRecoverAndNoEventForTheLegacyMissingAndUnreadableBranches()
    {
        var legacy = Wired();
        legacy.Read(Legacy());
        legacy.Resolve(true, 5);
        var missing = Wired();   // a save made without the mod: no Read
        missing.Resolve(true, 5);
        var unreadable = Wired();
        unreadable.Read(Garbage());
        unreadable.Resolve(true, 5);
        var fresh = Wired();   // a new game
        fresh.Resolve(false, 5);
        Assert.DoesNotContain("recover", order);
        Assert.DoesNotContain("restored", order);
        Assert.Empty(events);
    }

    [Fact]   // task 8.7a: a save before the world's first serial step holds the cancelled assignment; the event waits for that step, once
    public void ASaveBeforeTheFirstStepCancelsTheAssignmentAtOnceAndWritesItsEventAtTheFirstStep()
    {
        var w = Wired();
        w.Read(Payload(WithPendingAssignment(announced: false)));
        Assert.True(w.TryWrite("gen-3", [], out byte[] early, out string writeError), writeError);   // the snapshot applied by the save ...
        var saved = ArenaSaveData.Read(early).Snapshot.Diagnostic;
        Assert.False(saved.Pending.Active);                 // ... which holds the cancelled assignment, as a C++ save after the load would ...
        Assert.Equal(3, saved.Counters.Cancelled);
        Assert.Equal(new[] { "recover", LoadedLine }, order.Where(l => !l.StartsWith("PG: ", StringComparison.Ordinal)));
        Assert.Empty(events);                               // ... and the event waits: no world's combat log before the first serial step
        w.Resolve(true, 5);
        Assert.Equal(("diagnostic_assignment_cancelled", CancelledFields), Assert.Single(events));
        Assert.True(order.IndexOf("restored") < order.IndexOf("event diagnostic_assignment_cancelled"));   // the held restore first, as the C++
        Assert.True(w.TryWrite("gen-4", [], out _, out _));
        w.Resolve(true, 6);
        Assert.Single(events);   // once
        Assert.Equal(3, w.Ledger.State().Diagnostic.Counters.Cancelled);
    }

    [Fact]   // task 8.7a: the same for the persistence error: the Report with the snapshot, the event at the first serial step, once
    public void ASaveBeforeTheFirstStepWritesThePersistenceErrorEventAtTheFirstStep()
    {
        var decoded = ArenaSaveData.Read(Payload(Rated()));
        decoded.Snapshot.DiagnosticError = "$.diagnostic.status: out of range";
        var w = Wired(_ => decoded);
        w.Read([1]);
        Assert.True(w.TryWrite("gen-3", [], out _, out string writeError), writeError);
        Assert.Empty(events);
        Assert.StartsWith("Diagnostic selection disabled: $.diagnostic.status: out of range.", w.TakeNotice());
        w.Resolve(true, 5);
        Assert.Equal("diagnostic_persistence_error", Assert.Single(events).Name);
        int report = order.FindIndex(l => l.StartsWith("Proving Grounds persistence: Diagnostic selection disabled: ", StringComparison.Ordinal));
        int loaded = order.IndexOf(LoadedLine), restored = order.IndexOf("restored"), held = order.IndexOf("event diagnostic_persistence_error");
        Assert.True(loaded >= 0 && loaded < report);   // the "Loaded" line and the Report with the snapshot (at the save) ...
        Assert.True(report < restored && restored < held);   // ... then at the first serial step the held restore, then the held event
        w.Resolve(true, 6);
        Assert.Single(events);
        Assert.Single(order, l => l.StartsWith("Proving Grounds persistence: Diagnostic selection disabled: ", StringComparison.Ordinal));
    }

    [Fact]   // ArenaNativePersistence.cpp:360-372 with both: a pending assignment and no announced bout, and a diagnostic error, in the C++ order
    public void ALoadWithAPendingAssignmentAndADiagnosticErrorKeepsTheFullCppOrder()
    {
        const string error = "$.diagnostic.counters.completed: completed fights exceed target";
        var decoded = ArenaSaveData.Read(Payload(WithPendingAssignment(announced: false)));
        decoded.Snapshot.DiagnosticError = error;
        var w = Wired(_ => decoded);
        w.Read([1]);
        w.Resolve(true, 5);
        string report = "Diagnostic selection disabled: " + error + ". Ordinary arena matchmaking remains active.";
        Assert.Equal(new[] { "restored", "recover", "event diagnostic_assignment_cancelled", LoadedLine, "event diagnostic_persistence_error",
            "Proving Grounds persistence: " + report }, order);
        Assert.Equal(new[] { ("diagnostic_assignment_cancelled", CancelledFields),
            ("diagnostic_persistence_error", ",\"error\":\"invalid diagnostic state; see arena persistence log\"") }, events);
        Assert.Equal(report, w.TakeNotice());
    }
}
