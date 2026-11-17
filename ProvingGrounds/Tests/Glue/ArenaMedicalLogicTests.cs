using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.ArenaMedicalPolicy;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>ArenaMedical.cpp's decisions (task 4.7) in Core/Glue/ArenaMedicalLogic.cs over fakes: SetProtocol's lock and range rule
/// (:214-220), the treatment block over the published match view (BlockFor :157-165 = ShouldBlockTreatment over SparSession's IsActive /
/// IsParticipant / IsEliminated), the kept AvB quirk (nobody eliminated, so a KO'd AvB fighter gets no treatment until the end) driven by the
/// real session and KO watcher logic, ActionableAidNeed / NeedsStabilization / IssueStabilizeOrder (:227-267), the test verb, and that a
/// prisoner stabilise order the C++ issues is never one its own block refuses (E3).</summary>
public class ArenaMedicalLogicTests
{
    static readonly MatchTeam A = MatchTeam.TeamA, B = MatchTeam.TeamB, N = MatchTeam.None;

    sealed class Rig
    {
        public readonly SparFakeHost H = new();
        public readonly SparSessionLogic<int> S = new(-1);
        public readonly KOWatcherLogic<int> W = new();

        public Rig(int count)
        {
            for (int i = 0; i < count; i++) H.Add(i, "F" + i, x: i * 100);
        }

        public void Scan()
        {
            H.Now += 100;
            W.Tick(H, S, H.Now);
        }

        /// <summary>The view the glue publishes (src/Game/CombatHooks.cs Publish).</summary>
        public CombatMatchView<int> View()
        {
            var f = S.IsActive ? S.Fighters.ToArray() : [];
            return new CombatMatchView<int>(-1, S.IsActive, S.IsActive ? S.GetMode() : MatchMode.TeamAvB, f, f.Select(S.GetTeam).ToArray(),
                f.Select(c => S.IsEliminated(H, c)).ToArray(), S.GetActiveA(-1), S.GetActiveB(-1), S.HasPendingWalkIn(H), [], []);
        }
    }

    // ---------- SetProtocol (C++ :214-220) ----------

    [Theory]
    [InlineData(1, 2, false, 2)]
    [InlineData(2, 1, false, 1)]
    [InlineData(2, 0, false, 1)]   // out of range -> Ringside Aid
    [InlineData(1, 7, false, 1)]
    [InlineData(1, 2, true, 1)]    // locked while a match runs: unchanged
    [InlineData(2, 1, true, 2)]
    [InlineData(2, 0, true, 2)]
    public void SetProtocol_RangeAndLock(int current, int requested, bool active, int expected) =>
        Assert.Equal((Protocol)expected, ArenaMedicalLogic.SetProtocol((Protocol)current, (Protocol)requested, active));

    // ---------- the view ----------

    [Fact]
    public void View_IsEliminated_AsSparSession()
    {
        var r = new Rig(3);
        r.S.StartMatch(r.H, MatchMode.LastStanding, [0, 1, 2], [N, N, N], 3);
        r.S.EliminateFighter(r.H, 1);
        var v = r.View();
        for (int c = -1; c < 5; c++) Assert.Equal(r.S.IsEliminated(r.H, c), v.IsEliminated(c));
        Assert.True(v.IsEliminated(1));
        Assert.False(CombatMatchView<int>.Idle(-1).IsEliminated(1));
    }

    [Fact]
    public void BlocksTreatment_IsShouldBlockTreatment_OverTheView()
    {
        var r = new Rig(4);
        r.S.StartMatch(r.H, MatchMode.LastStanding, [0, 1, 2], [N, N, N], 3);
        r.S.EliminateFighter(r.H, 2);
        var v = r.View();
        foreach (var p in new[] { Protocol.RingsideAid, Protocol.Bloodsport })
            for (int c = -1; c < 5; c++)
                Assert.Equal(ShouldBlockTreatment(p, r.S.IsActive, r.S.IsParticipant(r.H, c), r.S.IsEliminated(r.H, c)),
                    ArenaMedicalLogic.BlocksTreatment(v, p, c));
        Assert.True(ArenaMedicalLogic.BlocksTreatment(v, Protocol.RingsideAid, 0));    // active fighter
        Assert.False(ArenaMedicalLogic.BlocksTreatment(v, Protocol.RingsideAid, 2));   // eliminated: allowed
        Assert.True(ArenaMedicalLogic.BlocksTreatment(v, Protocol.Bloodsport, 2));     // Bloodsport: never during the match
        Assert.False(ArenaMedicalLogic.BlocksTreatment(v, Protocol.Bloodsport, 3));    // not a participant
    }

    [Fact]
    public void NoMatch_NothingBlocked()
    {
        var idle = CombatMatchView<int>.Idle(-1);
        for (int c = -1; c < 4; c++)
        {
            Assert.False(ArenaMedicalLogic.BlocksTreatment(idle, Protocol.RingsideAid, c));
            Assert.False(ArenaMedicalLogic.BlocksTreatment(idle, Protocol.Bloodsport, c));
        }
    }

    // ---------- the kept quirk (PA §2.11 INFERRED): AvB eliminates nobody ----------

    [Theory]
    [InlineData(Protocol.RingsideAid)]
    [InlineData(Protocol.Bloodsport)]
    public void AvB_KOdFighter_NoTreatmentUntilTheEnd(Protocol protocol)
    {
        var r = new Rig(4);
        r.S.StartMatch(r.H, MatchMode.TeamAvB, [0, 1, 2, 3], [A, A, B, B], 4);
        r.Scan();
        r.H.C[0].Unconscious = true;
        r.Scan();
        Assert.True(r.S.IsActive);
        Assert.False(r.S.IsEliminated(r.H, 0));
        Assert.True(ArenaMedicalLogic.BlocksTreatment(r.View(), protocol, 0));   // KO'd, not eliminated: blocked
        // the other side still fights; team A wiped -> the match ends -> treatment allowed
        r.H.C[1].Unconscious = true;
        r.Scan();
        Assert.False(r.S.IsActive);
        Assert.False(ArenaMedicalLogic.BlocksTreatment(r.View(), protocol, 0));
        Assert.False(ArenaMedicalLogic.BlocksTreatment(r.View(), protocol, 1));
    }

    [Fact]
    public void LastStanding_KOdFighter_RingsideAllowsOnceEliminated_BloodsportNot()
    {
        var r = new Rig(3);
        r.S.StartMatch(r.H, MatchMode.LastStanding, [0, 1, 2], [N, N, N], 3);
        r.Scan();
        Assert.True(ArenaMedicalLogic.BlocksTreatment(r.View(), Protocol.RingsideAid, 0));
        r.H.C[0].Unconscious = true;
        r.Scan();
        Assert.True(r.S.IsActive);
        Assert.True(r.S.IsEliminated(r.H, 0));
        Assert.False(ArenaMedicalLogic.BlocksTreatment(r.View(), Protocol.RingsideAid, 0));
        Assert.True(ArenaMedicalLogic.BlocksTreatment(r.View(), Protocol.Bloodsport, 0));
        Assert.True(ArenaMedicalLogic.BlocksTreatment(r.View(), Protocol.RingsideAid, 1));   // still fighting
    }

    // ---------- the aid need and the stabilise order (C++ :227-267) ----------

    sealed class Med : IMedicalWorld<int>
    {
        public sealed class P { public bool Valid = true, Dead, Medical = true; public int Wounds; public float Bleed, Organic, Robot; }
        public readonly Dictionary<int, P> C = [];
        public readonly List<string> Orders = [];
        P? Get(int c) => C.TryGetValue(c, out var p) ? p : null;
        public bool IsValid(int c) => Get(c) is { Valid: true };
        public bool IsDead(int c) => Get(c) is { Dead: true };
        public bool HasMedical(int c) => Get(c) is { Medical: true };
        public int WoundCount(int c) => Get(c)!.Wounds;
        public float BleedRate(int c) => Get(c)!.Bleed;
        public float AidNeed(int c, bool robot) => robot ? Get(c)!.Robot : Get(c)!.Organic;
        public bool GiveTreatOrder(int medic, int target, bool robotAid) { Orders.Add($"{medic}->{target}:{(robotAid ? "robot" : "organic")}"); return true; }
    }

    [Fact]
    public void ActionableAidNeed_Cases()
    {
        var w = new Med();
        w.C[0] = new() { Wounds = 1, Organic = 0.4f, Robot = 0.1f };
        w.C[1] = new() { Wounds = 0, Bleed = 0.0001f, Organic = 0.6f };   // stale score: no wound, bleeding at the floor -> 0
        w.C[2] = new() { Wounds = 0, Bleed = 0.0002f, Organic = 0.6f };   // bleeding above the floor -> the score
        w.C[3] = new() { Wounds = 2, Organic = -0.1f };                   // negative score -> 0
        w.C[4] = new() { Dead = true, Wounds = 1, Organic = 0.5f };
        w.C[5] = new() { Valid = false, Wounds = 1, Organic = 0.5f };
        w.C[6] = new() { Medical = false };
        w.C[7] = new() { Wounds = 1, Organic = float.NaN };
        Assert.Equal(0.4f, ArenaMedicalLogic.ActionableAidNeed(w, 0, false));
        Assert.Equal(0.1f, ArenaMedicalLogic.ActionableAidNeed(w, 0, true));
        Assert.Equal(0f, ArenaMedicalLogic.ActionableAidNeed(w, 1, false));
        Assert.Equal(0.6f, ArenaMedicalLogic.ActionableAidNeed(w, 2, false));
        Assert.Equal(0f, ArenaMedicalLogic.ActionableAidNeed(w, 3, false));
        Assert.Equal(0f, ArenaMedicalLogic.ActionableAidNeed(w, 4, false));
        Assert.Equal(0f, ArenaMedicalLogic.ActionableAidNeed(w, 5, false));
        Assert.Equal(0f, ArenaMedicalLogic.ActionableAidNeed(w, 6, false));
        Assert.Equal(0f, ArenaMedicalLogic.ActionableAidNeed(w, 7, false));   // NaN > 0 is false in C++ too
        Assert.Equal(0f, ArenaMedicalLogic.ActionableAidNeed(w, 99, false));
    }

    [Fact]
    public void NeedsStabilization_EitherKitAbove001()
    {
        var w = new Med();
        w.C[0] = new() { Wounds = 1, Organic = 0.01f, Robot = 0.01f };    // not above 0.01
        w.C[1] = new() { Wounds = 1, Organic = 0.011f };
        w.C[2] = new() { Wounds = 1, Robot = 0.02f };
        Assert.False(ArenaMedicalLogic.NeedsStabilization(w, 0));
        Assert.True(ArenaMedicalLogic.NeedsStabilization(w, 1));
        Assert.True(ArenaMedicalLogic.NeedsStabilization(w, 2));
    }

    [Fact]
    public void IssueStabilizeOrder_RobotWhenRobotScoreHigher_NothingForBadPair()
    {
        var w = new Med();
        w.C[0] = new();                                  // medic
        w.C[1] = new() { Organic = 0.3f, Robot = 0.5f };
        w.C[2] = new() { Organic = 0.5f, Robot = 0.5f };   // equal -> organic
        w.C[3] = new() { Dead = true };
        w.C[4] = new() { Medical = false };               // no medical: organic order (C++ medical && ...)
        w.C[5] = new() { Dead = true };                   // dead medic
        Assert.True(ArenaMedicalLogic.IssueStabilizeOrder(w, 0, 1));
        Assert.True(ArenaMedicalLogic.IssueStabilizeOrder(w, 0, 2));
        Assert.False(ArenaMedicalLogic.IssueStabilizeOrder(w, 0, 3));
        Assert.True(ArenaMedicalLogic.IssueStabilizeOrder(w, 0, 4));
        Assert.False(ArenaMedicalLogic.IssueStabilizeOrder(w, 5, 1));
        Assert.False(ArenaMedicalLogic.IssueStabilizeOrder(w, 0, 99));
        Assert.Equal(["0->1:robot", "0->2:organic", "0->4:organic"], w.Orders);
    }

    /// <summary>The C++ issues a prisoner stabilise order only when ShouldStabilizePrisoner holds and (during the match) the prisoner is
    /// eliminated (PrisonerUtil.cpp:1311-1335), or after the match (:1542-1548); either way its own block (BlockFor) never applies to that
    /// patient, so no exemption is needed (none in the C++).</summary>
    [Theory]
    [InlineData(MatchMode.LastStanding)]
    [InlineData(MatchMode.Teams1v1)]
    [InlineData(MatchMode.TeamAvB)]
    public void PrisonerStabiliseOrder_NeverBlockedByOurBlock(MatchMode mode)
    {
        foreach (var protocol in new[] { Protocol.RingsideAid, Protocol.Bloodsport })
        {
            var r = new Rig(4);
            if (mode == MatchMode.LastStanding) r.S.StartMatch(r.H, mode, [0, 1, 2], [N, N, N], 3);
            else r.S.StartMatch(r.H, mode, [0, 1, 2, 3], [A, A, B, B], 4);
            r.Scan();
            r.H.C[0].Unconscious = true;
            r.Scan();
            var v = r.View();
            int issued = 0;
            for (int c = 0; c < 4; c++)
            {
                bool cppIssuesDuringMatch = r.S.IsActive && ShouldStabilizePrisoner(protocol) && r.S.IsEliminated(r.H, c);
                if (!cppIssuesDuringMatch) continue;
                issued++;
                Assert.False(ArenaMedicalLogic.BlocksTreatment(v, protocol, c));
            }
            // AvB eliminates nobody, so the C++ issues no stabilise order during an AvB match (the set is empty; the knocked-out prisoner waits
            // for the match end, the kept quirk); Bloodsport issues none at all; Ringside Aid in the other modes issues one for the KO'd fighter.
            int expected = mode != MatchMode.TeamAvB && protocol == Protocol.RingsideAid ? 1 : 0;
            Assert.Equal(expected, issued);
            Assert.False(ArenaMedicalLogic.BlocksTreatment(CombatMatchView<int>.Idle(-1), protocol, 0));   // after the match
        }
    }

    // ---------- the test verb (pg medical, stands in for the Arena window's toggle until 4.5) ----------

    [Theory]
    [InlineData("status", 1, false, false, true, 1, null)]
    [InlineData("bloodsport", 1, false, false, true, 2, null)]
    [InlineData("ringside", 2, false, false, true, 1, null)]
    [InlineData("bloodsport", 1, true, false, false, 1, "locked: a match is running")]
    [InlineData("bloodsport", 1, false, true, false, 1, "locked: an arena start is pending")]
    [InlineData("status", 2, true, true, true, 2, null)]
    [InlineData("ringside-aid", 1, false, false, false, 1, "usage pg medical <ringside|bloodsport|status>")]
    public void Command_SetsOrRefuses(string word, int current, bool active, bool pending, bool ok, int after, string? error)
    {
        var r = ArenaMedicalLogic.Command(word, (Protocol)current, active, pending);
        Assert.Equal(ok, r.Ok);
        Assert.Equal((Protocol)after, r.Protocol);
        Assert.Equal(error, r.Error);
    }
}
