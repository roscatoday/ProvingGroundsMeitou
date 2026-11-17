using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>A fake <see cref="IIngressHost{TId}"/> (int ids, -1 = none): positions you move by hand, a clock you advance, and a log of every
/// write (Calls) so the tests can say who got which order when.</summary>
sealed class IngressFakeHost : IIngressHost<int>
{
    public readonly Dictionary<int, Vec3> Pos = new();
    public readonly HashSet<int> Dead = new();
    public readonly Dictionary<int, string> Names = new();
    public readonly List<string> Calls = new();
    public readonly List<(int Id, Vec3 At, uint Ms)> Moves = new();
    public readonly List<(int Id, string Text, uint Ms)> Says = new();
    public readonly List<(int Id, Vec3 At)> Faces = new();
    public readonly List<int> Parked = new();
    public readonly Dictionary<ArenaBuildingRef, ArenaBuilding> Buildings = new();
    public ArenaBuilding? Site;   // what FindSiteForMode answers (any mode)
    public ArenaBuildingRef? RingSite;
    public uint Ms;
    public bool SparActive, StartOk = true, SaveInFlightValue, Aftercare, TownBusy, TownPlayerMatch, ValidatePrep = true, HoldBets, PendingWager;
    public string SparStatus = "Match started";
    public int FirstSquad = -1;
    public int ThrowMoveOn = -1;   // IssueMove of this id throws
    public bool ThrowDisengage, ThrowStart;
    public readonly HashSet<int> TownFighters = new();
    public readonly List<int> Quarantined = new();
    public (MatchRules.MatchMode Mode, int[] Fighters, MatchRules.MatchTeam[] Teams)? Started;
    public int[] Selected = [];   // what CollectSelectedCharacters answers (task 4.2)
    public readonly Dictionary<ArenaBuildingRef, Vec3> Markers = new();   // PositionMarker per building (none: null)
    public readonly List<(ArenaBuildingRef Building, bool Bind)> ArenaShown = new();
    public readonly List<(LeaderboardData.Kind Kind, ArenaBuildingRef Board)> BoardShown = new();
    public readonly Dictionary<int, float> Speeds = new();

    public static ArenaBuilding Arena(float x, float z, float yaw = 0f, string key = "1-site") =>
        new(ArenaBuildingSource.Player, key, ArenaIdentityLogic.ArenaStringId, null, true, new Vector3(x, 0f, z), yaw);

    public void AddSite(ArenaBuilding b) { Site = b; Buildings[b.Ref] = b; }

    public void Add(int id, float x, float z, string? name = null) { Pos[id] = new Vec3(x, 0f, z); Names[id] = name ?? "F" + id; }

    public int None => -1;
    public bool IsNone(int c) => c < 0;
    public bool Exists(int c) => Pos.ContainsKey(c);
    public bool IsDead(int c) => Dead.Contains(c);
    public string Name(int c) => Names.TryGetValue(c, out var n) ? n : "";
    public bool TryGetPosition(int c, out Vec3 position) => Pos.TryGetValue(c, out position);
    public float MovementSpeed(int c) => Speeds.TryGetValue(c, out var v) ? v : 0f;
    public bool IsMoving(int c) => false;
    public Vec3 Destination(int c) => new(0, 0, 0);
    public uint NowMs => Ms;
    public bool Paused => false;
    public int FirstSquadMember() => FirstSquad;
    public int[] CollectSelectedCharacters() => [.. Selected.Where(c => Exists(c) && !IsDead(c))];

    public void IssueMove(int c, Vec3 location, bool townFighter)
    {
        if (c == ThrowMoveOn) throw new InvalidOperationException("move failed");
        Moves.Add((c, location, Ms)); Calls.Add("move:" + c);
    }
    public void RemoveMoveJob(int c) => Calls.Add("removejob:" + c);
    public void Park(int c, bool standStillGoal) { Parked.Add(c); Calls.Add("park:" + c + (standStillGoal ? ":still" : "")); }
    public void ReleaseParked() => Calls.Add("releaseparked");
    public void Face(int c, Vec3 point) { Faces.Add((c, point)); }
    public void Say(int c, string text) { Says.Add((c, text, Ms)); }

    public ArenaBuilding? FindSiteForMode(ArenaLocationMode mode, Vector3 origin) { Calls.Add("findsite:" + mode); return Site; }
    public ArenaBuilding? Resolve(ArenaBuildingRef held) => Buildings.TryGetValue(held, out var b) ? b : null;
    public void SetRingSite(ArenaBuildingRef site) { RingSite = site; Calls.Add("ring"); }
    public Vec3? PositionMarker(ArenaBuilding building, Vec3 from) => Markers.TryGetValue(building.Ref, out var m) ? m : null;
    public void ArenaShowFromRegistry(ArenaBuilding registry, bool bindRegistry) { ArenaShown.Add((registry.Ref, bindRegistry)); Calls.Add("arena-show:" + registry.Key + (bindRegistry ? ":bind" : ":nobind")); }
    public void LeaderboardShow(LeaderboardData.Kind kind, ArenaBuilding board) { BoardShown.Add((kind, board.Ref)); Calls.Add("board-show:" + kind + ":" + board.Key); }

    public void RememberMatchFighters(int[] fighters, int count) => Calls.Add("remember:" + string.Join(",", fighters.Take(count)));
    public void DisengageMatch(int[] fighters, int count)
    {
        Calls.Add("disengage:" + string.Join(",", fighters.Take(count)));
        if (ThrowDisengage) throw new InvalidOperationException("disengage failed");
    }
    public void LeaveStaleSweep(int c) => Calls.Add("leavesweep:" + c);

    public bool SparIsActive => SparActive;
    public bool SparStartMatch(MatchRules.MatchMode mode, int[] fighters, MatchRules.MatchTeam[] teams, int count)
    {
        if (ThrowStart) throw new InvalidOperationException("start failed");
        Started = (mode, fighters.Take(count).ToArray(), teams.Take(count).ToArray());
        Calls.Add("start");
        return StartOk;
    }
    public string SparGetStatus() => SparStatus;
    public bool SaveInFlight => SaveInFlightValue;

    public void ResultsCancel() => Calls.Add("results-cancel");
    public void ResultsClose() => Calls.Add("results-close");

    public bool TownIsAftercare() => Aftercare;
    public bool TownIsBusy() => TownBusy;
    public bool TownIsPlayerMatch() => TownPlayerMatch;
    public bool TownIsFighter(int c) => TownFighters.Contains(c);
    public void TownCancelPlannedNpcBout(string reason) => Calls.Add("cancel-npc-bout:" + reason);
    public bool TownValidatePreparation() => ValidatePrep;
    public void TownQuarantineIngressFighter(int c) => Quarantined.Add(c);

    public void AnnouncerRelease() => Calls.Add("announcer-release");
    public void AnnouncerAbandonWorldState() => Calls.Add("announcer-abandon");
    public void AnnouncerTickApproach() { }
    public bool AnnouncerWaitingForPosition() => false;
    public bool AnnouncerHasAnnouncement() => false;
    public void AnnouncerBeginAnnouncement() { }
    public void AnnouncerTickAnnouncement(float elapsed) { }
    public float AnnouncerAnnouncementDuration() => 0f;
    public string AnnouncerGetStatus() => "";

    public bool BookieHoldForBets() => HoldBets;
    public bool BookieHasPendingWager() => PendingWager;

    public bool IsMatchPrisoner(int c) => false;
    public bool IsMatchPrisonerReleased(int c) => false;
    public bool AllMatchPrisonersReleased() => true;
    public bool MatchIncludesPrisoner() => false;
    public void SendPairToArena(int prisoner, Vec3 target) => Calls.Add("sendpair:" + prisoner);
    public void BeginHandlerUnlocks() => Calls.Add("handler-unlocks");
    public void TickHandlerUnlocks() { }
    public bool ConsumeNewlyReleased(int prisoner) => false;
    public void ParkMatchHandlers() => Calls.Add("park-handlers");
    public void ReinforceParkedHandlers() { }
    public void BeginReturnToCages(out string status) => status = "";
}
