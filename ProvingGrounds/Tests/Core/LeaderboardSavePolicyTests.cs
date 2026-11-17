using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/LeaderboardSavePolicy.cs (task 1.5); each expected value is read off the C++.

public class LeaderboardSavePolicyTests
{
    [Fact]   // LeaderboardSavePolicy.h:9-21
    public void World_context_modes()
    {
        var w = new LeaderboardSavePolicy.WorldContext();   // AwaitingLoad
        Assert.False(w.IsFresh()); Assert.True(w.CanReadSidecar()); Assert.False(w.CanUseMemory());
        w.Block();
        Assert.False(w.IsFresh()); Assert.False(w.CanReadSidecar()); Assert.False(w.CanUseMemory());
        w.CompleteTransition(true);
        Assert.True(w.IsFresh()); Assert.False(w.CanReadSidecar()); Assert.True(w.CanUseMemory());
        w.MarkLoaded();
        Assert.False(w.IsFresh()); Assert.False(w.CanReadSidecar()); Assert.True(w.CanUseMemory());
        w.CompleteTransition(false);
        Assert.False(w.IsFresh()); Assert.True(w.CanReadSidecar()); Assert.False(w.CanUseMemory());
    }

    [Fact]   // LeaderboardSavePolicy.h:30-88
    public void Save_keys()
    {
        Assert.False(LeaderboardSavePolicy.ShouldWriteSidecar(LeaderboardSavePolicy.Event.GameplayMutation));
        Assert.True(LeaderboardSavePolicy.ShouldWriteSidecar(LeaderboardSavePolicy.Event.NativeSaveCompleted));
        Assert.Equal("cur", LeaderboardSavePolicy.ActiveSaveKey("cur", "pending"));
        Assert.Equal("my save_01 [ok]é", LeaderboardSavePolicy.NormaliseSaveKey("My SAVE_01 [OK]é"));
        Assert.Equal("É", LeaderboardSavePolicy.NormaliseSaveKey("É"));   // "C" locale tolower: only A-Z
        Assert.True(LeaderboardSavePolicy.SameSaveKey("Quick", "quICK"));
        Assert.False(LeaderboardSavePolicy.SameSaveKey("", ""));
        Assert.False(LeaderboardSavePolicy.SameSaveKey("a", ""));
        Assert.False(LeaderboardSavePolicy.SameSaveKey("a", "b"));

        Assert.False(LeaderboardSavePolicy.ShouldAcceptSidecar("", "x", 3));
        Assert.True(LeaderboardSavePolicy.ShouldAcceptSidecar("slot", "other", 5));
        Assert.True(LeaderboardSavePolicy.ShouldAcceptSidecar("slot", "", -1));
        Assert.False(LeaderboardSavePolicy.ShouldAcceptSidecar("slot", "other", 6));
        Assert.False(LeaderboardSavePolicy.ShouldAcceptSidecar("slot", "", 16));
        Assert.True(LeaderboardSavePolicy.ShouldAcceptSidecar("Slot", "SLOT", 16));

        Assert.False(LeaderboardSavePolicy.IsForkSave("", "b"));
        Assert.False(LeaderboardSavePolicy.IsForkSave("a", ""));
        Assert.False(LeaderboardSavePolicy.IsForkSave("Save1", "save1"));
        Assert.True(LeaderboardSavePolicy.IsForkSave("save1", "quicksave"));
    }

    [Fact]   // LeaderboardSavePolicy.h:90-108
    public void Prisoner_hold_and_manual_save_gate()
    {
        Assert.False(LeaderboardSavePolicy.HoldForPrisoners(false, false, false));
        Assert.True(LeaderboardSavePolicy.HoldForPrisoners(true, false, false));
        Assert.False(LeaderboardSavePolicy.HoldForPrisoners(true, false, true));
        Assert.True(LeaderboardSavePolicy.HoldForPrisoners(false, true, true));
        Assert.True(LeaderboardSavePolicy.HoldForPrisoners(true, true, true));

        var g = new LeaderboardSavePolicy.ManualSaveGate();
        Assert.False(g.Waiting());
        Assert.Equal(LeaderboardSavePolicy.ManualSaveDecision.AllowManualSave, g.Request(false));
        Assert.Equal(LeaderboardSavePolicy.ManualSaveDecision.WarnUnsecuredSave, g.Request(true));
        Assert.Equal(LeaderboardSavePolicy.ManualSaveDecision.NoDeferredSave, g.Poll(true, true));
        g.Defer();
        Assert.True(g.Waiting());
        Assert.Equal(LeaderboardSavePolicy.ManualSaveDecision.DeferManualSave, g.Request(true));
        Assert.Equal(LeaderboardSavePolicy.ManualSaveDecision.DeferManualSave, g.Poll(true, false));
        Assert.True(g.Waiting());
        Assert.Equal(LeaderboardSavePolicy.ManualSaveDecision.ResumeManualSave, g.Poll(false, false));
        Assert.False(g.Waiting());
        g.Defer();
        Assert.Equal(LeaderboardSavePolicy.ManualSaveDecision.CancelManualSave, g.Poll(false, true));
        g.Defer(); g.Abandon();
        Assert.Equal(LeaderboardSavePolicy.ManualSaveDecision.NoDeferredSave, g.Poll(false, false));

        Assert.True(LeaderboardSavePolicy.NativeDispatchReady(0, true));
        Assert.True(LeaderboardSavePolicy.NativeDispatchReady(-3, true));
        Assert.False(LeaderboardSavePolicy.NativeDispatchReady(1, true));
        Assert.False(LeaderboardSavePolicy.NativeDispatchReady(0, false));
    }
}
