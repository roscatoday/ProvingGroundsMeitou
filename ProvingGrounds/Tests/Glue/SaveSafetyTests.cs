using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The save-safety state machine copied from WorldLifecycle.cpp (task 2.5): what a manual save and an autosave do while the arena
/// is busy, in the original's order, with the original's texts.</summary>
public class SaveSafetyTests
{
    internal sealed class FakeWorld : ISaveSafetyWorld
    {
        public readonly List<string> Calls = [];
        public readonly List<string> Messages = [];
        public readonly List<string> Logs = [];
        public bool IngressPending { get; set; }
        public bool WalkInPending { get; set; }
        public bool MatchActive { get; set; }
        public bool PrisonerRuntimeActivity { get; set; }
        public bool PendingCageLocks { get; set; }
        public bool CageReturnFailure { get; set; }
        public bool TownBusy { get; set; }
        public bool TownHasBooking { get; set; }
        public bool LimbShopVisitActive { get; set; }
        public bool LimbShopPhaseActive { get; set; }
        public string Status = "cage 3 not locked";
        public string ReturnStatus = "";
        public string CageReturnStatus() => Status;
        public void LimbShopSuspendVisit() => Calls.Add("TownLimbShop.SuspendVisit");
        public void TownCancel() => Calls.Add("TownArena.Cancel");
        public void TownCancelForSave() { Calls.Add("TownArena.CancelForSave"); }
        public void IngressCancel() => Calls.Add("ArenaIngress.Cancel");
        public void SparAbortForSave() { Calls.Add("SparSession.AbortForSave"); MatchActive = false; }
        public string PrisonersReturnAllToCages() { Calls.Add("PrisonerUtil.ReturnAllToCages"); return ReturnStatus; }
        public void ResultsClose() => Calls.Add("ResultsUI.Close");
        public void SparStatsAbandon() => Calls.Add("SparStats.AbandonWorldState");
        public void IngressAbandon() => Calls.Add("ArenaIngress.AbandonWorldState");
        public void SparSessionAbandon() => Calls.Add("SparSession.AbandonWorldState");
        public void PrisonerAbandon() => Calls.Add("PrisonerUtil.AbandonWorldState");
        public void FightStarterAbandon() => Calls.Add("FightStarter.AbandonWorldState");
        public void ArenaUiAbandon() => Calls.Add("ArenaUI.AbandonWorldState");
        public void DebugMenuAbandon() => Calls.Add("DebugMenu.AbandonWorldState");
        public void Message(string text) => Messages.Add(text);
        public void Log(string text) => Logs.Add(text);
    }

    static readonly string[] CancelSequence =
    [
        "TownLimbShop.SuspendVisit", "TownArena.CancelForSave", "ArenaIngress.Cancel", "SparSession.AbortForSave", "ResultsUI.Close",
        "SparStats.AbandonWorldState",
    ];
    static readonly string[] FinishCleanup =
    [
        "ArenaIngress.AbandonWorldState", "SparSession.AbandonWorldState", "PrisonerUtil.AbandonWorldState", "FightStarter.AbandonWorldState",
        "ArenaUI.AbandonWorldState", "DebugMenu.AbandonWorldState",
    ];

    [Fact]
    public void BusyIsAnyArenaActivity()
    {
        var w = new FakeWorld();
        var s = new SaveSafety(w);
        Assert.False(s.IsArenaOperationBusy());
        foreach (Action<FakeWorld, bool> set in new Action<FakeWorld, bool>[]
                 { (f, v) => f.IngressPending = v, (f, v) => f.WalkInPending = v, (f, v) => f.MatchActive = v, (f, v) => f.TownBusy = v,
                   (f, v) => f.PendingCageLocks = v })
        {
            set(w, true);
            Assert.True(s.IsArenaOperationBusy());
            Assert.True(s.HasSaveSensitiveActivity());
            set(w, false);
        }
        // prisoner activity holds only while the return is not failed (LeaderboardSavePolicy.HoldForPrisoners)
        w.PrisonerRuntimeActivity = true;
        Assert.True(s.IsArenaOperationBusy());
        w.CageReturnFailure = true;
        Assert.False(s.IsArenaOperationBusy());
        w.PrisonerRuntimeActivity = false; w.CageReturnFailure = false;
        // a blocked arena ledger is not busy: nothing here reads the ledger. The limb shop visit makes a save sensitive, not the arena busy.
        w.LimbShopVisitActive = true;
        Assert.False(s.IsArenaOperationBusy());
        Assert.True(s.HasSaveSensitiveActivity());
    }

    [Fact]
    public void ManualSaveWhileIdleSavesAtOnce()
    {
        var w = new FakeWorld();
        var s = new SaveSafety(w);
        var r = s.ManualSave(@"C:\save\a", announced: false);
        Assert.True(r.Requested);
        Assert.Equal(@"C:\save\a", r.Folder);
        Assert.Equal(["TownLimbShop.SuspendVisit"], w.Calls);
        Assert.Empty(w.Messages);
    }

    [Fact]
    public void ManualSaveWhileIdleCancelsATownBooking()
    {
        var w = new FakeWorld { TownHasBooking = true };
        var s = new SaveSafety(w);
        Assert.True(s.NeedsManualPreparation());
        Assert.True(s.ManualSave(null, false).Requested);
        Assert.Equal(["TownLimbShop.SuspendVisit", "TownArena.Cancel"], w.Calls);
    }

    [Fact]
    public void TheGateStateMatchesThePublicChecks()
    {
        var w = new FakeWorld();
        var s = new SaveSafety(w);
        Assert.Equal(SaveGateState.Idle, s.GateState());
        foreach (Action<FakeWorld> set in new Action<FakeWorld>[] { f => f.MatchActive = true, f => f.LimbShopVisitActive = true,
                     f => f.LimbShopPhaseActive = true, f => f.TownHasBooking = true, f => f.CageReturnFailure = true })
        {
            var f = new FakeWorld(); set(f);
            var x = new SaveSafety(f);
            Assert.Equal(new SaveGateState(x.IsArenaOperationBusy(), x.HasSaveSensitiveActivity(), x.NeedsManualPreparation(), x.CancelNoticeDeferred()),
                x.GateState());
        }
    }

    sealed class ThrowingWorld(bool onIngress, bool onCancel) : ISaveSafetyWorld
    {
        public bool IngressPending => onIngress ? throw new InvalidOperationException("ingress") : false;
        public bool WalkInPending => false;
        public bool MatchActive => true;
        public bool PrisonerRuntimeActivity => false;
        public bool PendingCageLocks => false;
        public bool CageReturnFailure => false;
        public bool TownBusy => false;
        public bool TownHasBooking => false;
        public bool LimbShopVisitActive => false;
        public bool LimbShopPhaseActive => false;
        public string CageReturnStatus() => "";
        public void LimbShopSuspendVisit() { }
        public void TownCancel() { }
        public void TownCancelForSave() { if (onCancel) throw new InvalidOperationException("cancel"); }
        public void IngressCancel() { }
        public void SparAbortForSave() { }
        public string PrisonersReturnAllToCages() => "";
        public void ResultsClose() { }
        public void SparStatsAbandon() { }
        public void IngressAbandon() { }
        public void SparSessionAbandon() { }
        public void PrisonerAbandon() { }
        public void FightStarterAbandon() { }
        public void ArenaUiAbandon() { }
        public void DebugMenuAbandon() { }
        public void Message(string text) { }
        public void Log(string text) { }
    }

    [Fact]
    public void ABrokenReadPublishesIdle()
    {
        var s = new SaveSafety(new ThrowingWorld(onIngress: true, onCancel: false));
        Exception? seen = null;
        Assert.Equal(SaveGateState.Idle, s.GateStateOrIdle(e => seen = e));
        Assert.Equal("ingress", seen?.Message);
    }

    [Fact]
    public void ABrokenCancelStillSaves()
    {
        var s = new SaveSafety(new ThrowingWorld(onIngress: false, onCancel: true));
        Exception? seen = null;
        Assert.Equal(SaveRequest.Save(@"C:\save\x"), s.ManualSaveOrSave(@"C:\save\x", false, e => seen = e));
        Assert.Equal("cancel", seen?.Message);
        Assert.Equal(SaveRequest.Save(null), new SaveSafety(new FakeWorld { MatchActive = true }).ManualSaveOrSave(null, false, _ => throw new Exception()));
    }

    [Fact]
    public void ALimbShopVisitInAnyPhaseButIdleNeedsPreparation()
    {
        // TownLimbShop::SuspendVisit acts whenever g_phase != Idle (TownLimbShop.cpp:586-597), wider than IsVisitActive (:581-584)
        var w = new FakeWorld();
        var s = new SaveSafety(w);
        Assert.False(s.NeedsManualPreparation());
        w.LimbShopPhaseActive = true;
        Assert.True(s.NeedsManualPreparation());
        Assert.False(s.IsArenaOperationBusy());
        Assert.True(s.ManualSave(null, false).Requested);
        Assert.Equal(["TownLimbShop.SuspendVisit"], w.Calls);
    }

    [Fact]
    public void TheCancelNoticeIsLeftToTheSimWhileWaitingOrAfterAWarning()
    {
        var w = new FakeWorld { MatchActive = true };
        var s = new SaveSafety(w);
        Assert.False(s.CancelNoticeDeferred());
        w.CageReturnFailure = true;               // Warn: the warning comes first (C++ :90-97, then :111-113)
        Assert.True(s.CancelNoticeDeferred());
        w.CageReturnFailure = false; w.PendingCageLocks = true;
        Assert.False(s.ManualSave(null, false).Requested);
        w.Messages.Clear();
        Assert.True(s.CancelNoticeDeferred());    // the gate waits: the next press returns before any message (C++ :88-89)
        Assert.False(s.ManualSave(null, false).Requested);
        Assert.Empty(w.Messages);
    }

    [Fact]
    public void ManualSaveDuringAMatchCancelsItThenSaves()
    {
        var w = new FakeWorld { MatchActive = true };
        var s = new SaveSafety(w);
        var r = s.ManualSave(null, announced: false);
        Assert.True(r.Requested);
        Assert.Null(r.Folder);
        Assert.Equal([.. CancelSequence, .. FinishCleanup], w.Calls);
        Assert.Equal(["Proving Grounds match cancelled to safely save the game."], w.Messages);
        Assert.Equal("Proving Grounds: manual save requested during arena operation - safely aborting", w.Logs[0]);
        Assert.Equal("Proving Grounds: safe-save combat/AI cleanup complete; cage locks verified", w.Logs[^1]);
    }

    [Fact]
    public void TheCancelMessageIsNotShownTwiceWhenTheFrameAnnouncedIt()
    {
        var w = new FakeWorld { MatchActive = true };
        var s = new SaveSafety(w);
        Assert.True(s.ManualSave(null, announced: true).Requested);
        Assert.Empty(w.Messages);
    }

    [Fact]
    public void PrisonersGoBackToTheirCagesBeforeTheSave()
    {
        var w = new FakeWorld { PrisonerRuntimeActivity = true, ReturnStatus = "2 returned" };
        var s = new SaveSafety(w);
        Assert.True(s.ManualSave(null, false).Requested);
        Assert.Equal(["TownLimbShop.SuspendVisit", "TownArena.CancelForSave", "ArenaIngress.Cancel", "SparSession.AbortForSave",
            "PrisonerUtil.ReturnAllToCages", "ResultsUI.Close", "SparStats.AbandonWorldState", .. FinishCleanup], w.Calls);
        Assert.Contains("Proving Grounds: safe-save prisoner cleanup - 2 returned", w.Logs);
    }

    [Fact]
    public void PendingCageLocksDeferTheSaveUntilTheyAreVerified()
    {
        var w = new FakeWorld { MatchActive = true, PendingCageLocks = true };
        var s = new SaveSafety(w);
        var r = s.ManualSave(@"C:\save\b", false);
        Assert.False(r.Requested);
        Assert.True(s.ManualSaveWaiting);
        Assert.Equal("Manual save waiting for cage locks. Unpause so the handler can finish locking; a timeout will cancel this save.", w.Messages[^1]);
        // the waiting gate keeps the arena busy (C++ IsArenaOperationBusy: || g_manualGate.Waiting())
        w.PendingCageLocks = false;
        Assert.True(s.IsArenaOperationBusy());
        // a second manual save while waiting replaces the name and still waits
        w.PendingCageLocks = true;
        Assert.False(s.ManualSave(@"C:\save\c", false).Requested);
        Assert.False(s.Tick().Requested);   // still pending
        w.PendingCageLocks = false;
        w.Calls.Clear();
        var resumed = s.Tick();
        Assert.True(resumed.Requested);
        Assert.Equal(@"C:\save\c", resumed.Folder);
        Assert.Equal(FinishCleanup, w.Calls);
        Assert.False(s.ManualSaveWaiting);
        Assert.False(s.Tick().Requested);
    }

    [Fact]
    public void ALockFailureCancelsThatSaveOnceAndTheNextGoesThrough()
    {
        var w = new FakeWorld { MatchActive = true, PendingCageLocks = true };
        var s = new SaveSafety(w);
        Assert.False(s.ManualSave(null, false).Requested);
        w.PendingCageLocks = false; w.CageReturnFailure = true;
        Assert.False(s.Tick().Requested);
        Assert.Equal("Manual save CANCELLED: prisoner return was not secured: cage 3 not locked. Check the prisoner/cage. A later manual save "
            + "can save the current unsecured world.", w.Messages[^1]);
        // the next manual save warns and saves (WarnUnsecuredSave), without forcing prisoners back
        w.Calls.Clear(); w.PrisonerRuntimeActivity = true; w.MatchActive = true;
        var r = s.ManualSave(null, false);
        Assert.True(r.Requested);
        Assert.Equal("Saving current world, but prisoner return was NOT secured: cage 3 not locked. Check the prisoner and cage after saving.",
            w.Messages[^2]);
        Assert.DoesNotContain("PrisonerUtil.ReturnAllToCages", w.Calls);
    }

    [Fact]
    public void AFailureFoundDuringPreparationCancelsThatSave()
    {
        // the failure appears during the cancel (C++ :129-134 reads HasCageReturnFailure after the cancel sequence)
        var w = new FailingOnAbort();
        var s = new SaveSafety(w);
        Assert.False(s.ManualSave(null, false).Requested);
        Assert.StartsWith("Manual save CANCELLED: prisoner return was not secured: ", w.Messages[^1]);
        Assert.False(s.ManualSaveWaiting);
    }

    sealed class FailingOnAbort : ISaveSafetyWorld
    {
        public readonly List<string> Messages = [];
        bool failed;
        public bool IngressPending => false;
        public bool WalkInPending => false;
        public bool MatchActive => !failed;
        public bool PrisonerRuntimeActivity => false;
        public bool PendingCageLocks => false;
        public bool CageReturnFailure => failed;
        public bool TownBusy => false;
        public bool TownHasBooking => false;
        public bool LimbShopVisitActive => false;
        public bool LimbShopPhaseActive => false;
        public string CageReturnStatus() => "x";
        public void LimbShopSuspendVisit() { }
        public void TownCancel() { }
        public void TownCancelForSave() { }
        public void IngressCancel() { }
        public void SparAbortForSave() => failed = true;
        public string PrisonersReturnAllToCages() => "";
        public void ResultsClose() { }
        public void SparStatsAbandon() { }
        public void IngressAbandon() { }
        public void SparSessionAbandon() { }
        public void PrisonerAbandon() { }
        public void FightStarterAbandon() { }
        public void ArenaUiAbandon() { }
        public void DebugMenuAbandon() { }
        public void Message(string text) => Messages.Add(text);
        public void Log(string text) { }
    }

    [Fact]
    public void AnAutosaveWhileBusyWaitsAndRunsWhenIdle()
    {
        var w = new FakeWorld { MatchActive = true };
        var s = new SaveSafety(w);
        s.DeferAutosave(@"C:\save\autosave1");
        Assert.True(s.AutosavePostponed);
        Assert.Equal("Proving Grounds: queued autosave postponed until arena operation completes", w.Logs[^1]);
        Assert.False(s.AutosaveTick().Requested);
        Assert.Equal("Proving Grounds: autosave postponed until arena operation completes", w.Logs[^1]);
        int logs = w.Logs.Count;
        Assert.False(s.AutosaveTick().Requested);
        Assert.Equal(logs, w.Logs.Count);   // logged once while the hold lasts
        w.MatchActive = false;
        var r = s.AutosaveTick();
        Assert.True(r.Requested);
        Assert.Equal(@"C:\save\autosave1", r.Folder);
        Assert.Equal(["Proving Grounds: arena operation complete - autosave hold released", "Proving Grounds: running postponed autosave"],
            w.Logs.Skip(logs));
        Assert.False(s.AutosavePostponed);
        Assert.False(s.AutosaveTick().Requested);
    }

    [Fact]
    public void ALaterAutosaveReplacesTheWaitingOne()
    {
        var w = new FakeWorld { TownBusy = true };
        var s = new SaveSafety(w);
        s.DeferAutosave(@"C:\save\autosave0");
        s.DeferAutosave(@"C:\save\autosave1");
        w.TownBusy = false;
        Assert.Equal(@"C:\save\autosave1", s.AutosaveTick().Folder);
    }
}

/// <summary>The frame thread's decision in the QuickSaveNow prefix (task 2.5): never touches the world, only the published state.</summary>
public class SaveGateTests
{
    static readonly string[] Autos = [@"C:\Users\p\AppData\Local\Meitou\save\autosave0", @"C:\Users\p\AppData\Local\Meitou\save\autosave1",
        @"C:\Users\p\AppData\Local\Meitou\save\autosave2"];
    static readonly SaveGateState Idle = SaveGateState.Idle;
    static readonly SaveGateState Busy = new(true, true, true, false);

    [Fact]
    public void AutosaveFoldersAreKnownByTheirPath()
    {
        Assert.True(SaveGate.IsAutosaveFolder(Autos[1], Autos));
        Assert.True(SaveGate.IsAutosaveFolder(Autos[2].ToUpperInvariant(), Autos));
        Assert.False(SaveGate.IsAutosaveFolder(null, Autos));
        Assert.False(SaveGate.IsAutosaveFolder(@"C:\Users\p\AppData\Local\Meitou\save\pgtest-ss", Autos));
        Assert.False(SaveGate.IsAutosaveFolder(@"C:\elsewhere\autosave0", Autos));
    }

    [Fact]
    public void AFeedbackSaveIsNeverRefusedOrDelayed()
    {
        Assert.Equal(SaveGateDecision.Allow, SaveGate.Decide(true, false, false, false, false, Busy));
        Assert.Equal(SaveGateDecision.Allow, SaveGate.Decide(true, true, false, false, true, Busy));
    }

    [Fact]
    public void IdleSavesGoThrough()
    {
        Assert.Equal(SaveGateDecision.Allow, SaveGate.Decide(false, false, false, false, false, Idle));
        Assert.Equal(SaveGateDecision.Allow, SaveGate.Decide(false, true, false, false, false, Idle));
    }

    [Fact]
    public void AnAutosaveWhileSensitiveIsDelayed()
    {
        Assert.Equal(SaveGateDecision.DelayAutosave, SaveGate.Decide(false, true, false, false, false, new(false, true, true, false)));
        // a manual-only preparation (a town booking) does not delay an autosave (C++: idle reservations do not postpone autosave)
        Assert.Equal(SaveGateDecision.Allow, SaveGate.Decide(false, true, false, false, false, new(false, false, true, false)));
    }

    [Fact]
    public void AManualSaveThatNeedsPreparationIsRefusedOnceAndPrepared()
    {
        Assert.Equal(SaveGateDecision.PrepareFirst, SaveGate.Decide(false, false, false, false, false, Busy));
        Assert.Equal(SaveGateDecision.PrepareFirst, SaveGate.Decide(false, false, false, false, false, new(false, false, true, false)));
        // a manual request still waiting for the sim keeps refusing (the newer one replaces it)
        Assert.Equal(SaveGateDecision.PrepareFirst, SaveGate.Decide(false, false, false, false, true, Idle));
    }

    [Fact]
    public void OurOwnReRequestedSaveWaitsWhileMeitouIsStillWriting()
    {
        Assert.Equal(SaveGateDecision.HoldReRequest, SaveGate.Decide(false, true, true, true, false, Idle));
        Assert.Equal(SaveGateDecision.HoldReRequest, SaveGate.Decide(false, false, true, true, false, Busy));
        Assert.Equal(SaveGateDecision.Allow, SaveGate.Decide(false, true, true, false, false, Idle));
        // a feedback save is still never touched; a player's own save while Meitou writes is Meitou's business
        Assert.Equal(SaveGateDecision.Allow, SaveGate.Decide(true, false, false, true, false, Busy));
        Assert.Equal(SaveGateDecision.Allow, SaveGate.Decide(false, false, false, true, false, Idle));
    }

    [Fact]
    public void TheCancelNoticeIsShownOncePerRefusedSave()
    {
        Assert.True(SaveGate.Announce(Busy, manualPending: false));
        Assert.False(SaveGate.Announce(Busy, manualPending: true));                 // a double press: one notice
        Assert.False(SaveGate.Announce(new(true, true, true, true), false));       // waiting for locks / after a warning: the sim shows it
        Assert.False(SaveGate.Announce(new(false, false, true, false), false));    // a town booking only: nothing to cancel
    }

    [Fact]
    public void ADoublePressOfTheSameSaveIsOnePrepareKeepingTheFirstNotice()
    {
        var p = new PendingManualSave();
        Assert.False(p.Pending);
        p.Offer(@"C:\save\a", announced: true);
        Assert.True(p.Pending);
        p.Offer(@"c:\SAVE\a", announced: false);
        Assert.Equal(new PendingManualSave.Slot(@"c:\SAVE\a", true), p.Take());
        Assert.Null(p.Take());
        Assert.False(p.Pending);
    }

    [Fact]
    public void TwoDifferentSavesAreBothKeptInPressOrder()
    {
        var p = new PendingManualSave();
        p.Offer(null, announced: true);            // F5 while paused
        p.Offer(@"C:\save\named", announced: false); // then a named save: the notice is not shown again (manual save pending)
        Assert.Equal(new PendingManualSave.Slot(null, true), p.Take());
        Assert.Equal(new PendingManualSave.Slot(@"C:\save\named", false), p.Take());
        Assert.Null(p.Take());
    }

    [Fact]
    public void TheSecondDifferentSaveFindsTheArenaIdleAndSavesWithoutAMessage()
    {
        // as the C++: each SaveManager::save prepares; the first cancels the match, the second finds nothing to cancel
        var w = new SaveSafetyTests.FakeWorld { MatchActive = true };
        var s = new SaveSafety(w);
        Assert.Equal(SaveRequest.Save(null), s.ManualSave(null, announced: true));
        int calls = w.Calls.Count;
        Assert.Equal(SaveRequest.Save(@"C:\save\named"), s.ManualSave(@"C:\save\named", announced: false));
        Assert.Empty(w.Messages);
        Assert.Equal(["TownLimbShop.SuspendVisit"], w.Calls.Skip(calls));
    }

    [Fact]
    public void WhileWaitingForCageLocksALaterSaveRenamesTheWaitingOne()
    {
        // C++ :196-200: the gate defers the second save and g_manualSaveName becomes its name; one save resumes
        var w = new SaveSafetyTests.FakeWorld { MatchActive = true, PendingCageLocks = true };
        var s = new SaveSafety(w);
        Assert.False(s.ManualSave(null, false).Requested);
        Assert.False(s.ManualSave(@"C:\save\named", false).Requested);
        w.PendingCageLocks = false;
        Assert.Equal(SaveRequest.Save(@"C:\save\named"), s.Tick());
        Assert.False(s.Tick().Requested);
    }

    [Fact]
    public void OurOwnReRequestedSaveGoesThrough()
    {
        // the save the system re-requests after preparing (the C++ calls SaveManager_save_orig directly)
        Assert.Equal(SaveGateDecision.Allow, SaveGate.Decide(false, false, true, false, false, Busy));
        Assert.Equal(SaveGateDecision.Allow, SaveGate.Decide(false, true, true, false, false, Busy));
    }

    [Fact]
    public void FoldersMatchCaseInsensitivelyAndNullIsTheQuicksaveSlot()
    {
        Assert.True(SaveGate.SameFolder(null, null));
        Assert.True(SaveGate.SameFolder(@"C:\a\B", @"c:\A\b"));
        Assert.False(SaveGate.SameFolder(null, @"C:\a"));
        Assert.False(SaveGate.SameFolder(@"C:\a", @"C:\b"));
    }
}
