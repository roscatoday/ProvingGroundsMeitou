using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.MatchRules;

namespace ProvingGrounds.Tests.Glue;

/// <summary>A fake of everything SparSession and KOWatcher call (ids are ints, -1 is none): the characters, and every module call recorded in
/// order as a short text in <see cref="Calls"/>.</summary>
sealed class SparFakeHost : ISparHost<int>
{
    public sealed class Ch
    {
        public bool Exists = true, Dead, Unconscious, Npc;
        public string Name = "";
        public float X, Y, Z;
        public (float X, float Y, float Z)? Moving;   // isCurrentlyMoving + destination
    }

    public readonly Dictionary<int, Ch> C = [];
    public readonly List<string> Calls = [];
    public readonly SparPodium.Snapshot Snapshot = new();
    public uint Now { get; set; } = 3_600_000;
    public bool SaveInFlight { get; set; }
    public bool Aftercare, ControlAvailable = true;
    public string ReturnStatus = "";
    public Func<string, bool>? ThrowOn;   // a call text prefix to throw at

    public Ch Add(int id, string name, float x = 0, bool npc = false)
    {
        var c = new Ch { Name = name, X = x, Npc = npc };
        C[id] = c;
        return c;
    }

    void Record(string call)
    {
        Calls.Add(call);
        if (ThrowOn?.Invoke(call) == true) throw new InvalidOperationException("thrown at " + call);
    }

    static string Ids(int[] f, int n) => string.Join(",", f.Take(n));

    Ch? Get(int id) => C.TryGetValue(id, out var c) && c.Exists ? c : null;

    public int None => -1;
    public bool IsNone(int c) => c < 0;
    public bool Exists(int c) => Get(c) is not null;
    public bool ThrowOnRead;   // IsDead throws (a host member that throws every scan)
    public bool IsDead(int c) => ThrowOnRead ? throw new InvalidOperationException("read failed") : Get(c) is { Dead: true };
    public bool IsUnconscious(int c) => Get(c) is { Unconscious: true };
    public string Name(int c) => C.TryGetValue(c, out var ch) ? ch.Name : "";
    public bool TryGetPosition(int c, out float x, out float y, out float z)
    {
        var ch = Get(c);
        x = ch?.X ?? 0; y = ch?.Y ?? 0; z = ch?.Z ?? 0;
        return ch is not null;
    }
    public uint NowMs => Now;

    public bool BenchControlAvailable => ControlAvailable;
    public bool ReserveBench(int c) { Record("reserve:" + c); return Get(c) is not null; }
    public void ReleaseBench(int c) => Record("release:" + c);
    public void SetBenchStance(int c, bool hold) => Calls.Add("stance:" + c + (hold ? ":hold" : ":nohold"));
    public bool TryGetMoveDestination(int c, out float x, out float y, out float z)
    {
        if (Get(c)?.Moving is { } d) { x = d.X; y = d.Y; z = d.Z; return true; }
        x = y = z = 0;
        return false;
    }
    public void Recall(int c, float x, float y, float z) => Record("recall:" + c + "@" + x);
    public void LookAt(int c, float x, float y, float z) => Calls.Add("look:" + c);

    public void RememberMatchFighters(int[] fighters, int count) => Record("remember:" + Ids(fighters, count));
    public void EngageMatch(int[] fighters, MatchTeam[] teams, int count, MatchMode mode) => Record("engage:" + Ids(fighters, count) + ":" + mode);
    public void DisengageMatch(int[] fighters, int count) => Record("disengage:" + Ids(fighters, count));
    public void RetargetMatchAfterElimination(int[] all, int allCount, int[]? rem, MatchTeam[]? remTeams, int remCount, MatchMode mode) =>
        Record("retarget:" + Ids(all, allCount) + "|" + (rem is null ? "null" : Ids(rem, remCount)));
    public void KeepEngaged(int a, int b) => Record("keep:" + a + "," + b);
    public void KeepEngagedMatch(int[] fighters, MatchTeam[] teams, int count, MatchMode mode) => Record("keepmatch:" + Ids(fighters, count));

    // the walk-in (task 4.3's ArenaIngress): settable; BeginWalkIn starts one (pending, that fighter) when WalkInStarts
    public int WalkInFighter = -1;
    public bool WalkInPending, WalkInArrived, WalkInStarts;
    public Func<bool>? PendingSequence;   // when set, answers IsWalkInPending instead of WalkInPending
    public int GetWalkInFighter() => WalkInFighter;
    public void ClearWalkIn() { Record("clearwalkin"); WalkInFighter = -1; WalkInPending = false; WalkInArrived = false; }
    public bool IsWalkInPending() => PendingSequence?.Invoke() ?? WalkInPending;
    public bool BeginWalkIn(int c, MatchTeam team)
    {
        Record("walkin:" + c);
        if (!WalkInStarts) return false;
        WalkInFighter = c; WalkInPending = true;
        return true;
    }
    public void TickWalkIn() => Record("tickwalkin");
    public bool IsWalkInArrived() => WalkInArrived;

    public bool TownIsAftercare() => Aftercare;
    public void TownCancelPlannedNpcBout(string reason) => Record("cancelbout:" + reason);
    public void TownOnMatchStarted() => Record("townstarted");
    public bool TownOwnsMatch() => false;
    public bool TownIsPlayerMatch() => false;
    public float TownActiveMarksMultiplier() => 1.0f;
    public int TownActiveMarksBonus() => 0;
    public void TownOnMatchResult(SparPodium.Snapshot snapshot) => Record("townresult");
    public void TownOnMatchAborted() => Record("townaborted");

    public void ResultsCancel() => Record("resultscancel");
    public void ResultsClose() => Record("resultsclose");
    public void ResultsScheduleShow(float delaySeconds) => Record("resultsshow:" + delaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void StatsResetForMatch() => Record("statsreset");
    public void StatsFreeze(SparPodium.OutcomeKind outcome, MatchMode mode, int lastStanding) => Record("freeze:" + outcome + ":" + lastStanding);
    public SparPodium.Snapshot StatsSnapshot() => Snapshot;
    public void StatsOnEliminated(int c) => Calls.Add("elim:" + c);

    public void ApplyFromSnapshot(SparPodium.Snapshot snapshot, LeaderboardData.Kind kind, float marksMultiplier, int marksTeamBonus) =>
        Record("apply:" + kind);

    public void CombatLogStart(MatchMode mode, int[] fighters, MatchTeam[] teams, int count) => Record("logstart:" + mode);
    public void CombatLogFinish(SparPodium.Snapshot snapshot, SparStopReason reason) => Record("logfinish:" + reason);
    public void CombatLogAbort(string reason) => Record("logabort:" + reason);
    public void CombatLogAbandon() => Record("logabandon");

    public string BeginReturnToCages() { Record("return"); return ReturnStatus; }
}
