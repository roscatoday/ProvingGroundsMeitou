using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>A fake <see cref="IArenaUIHost{TId}"/> (int ids, -1 = none): a squad, positions, stats, health, prisoners you set by hand, and a
/// log of every write (Calls) so the tests can say what was called in which order.</summary>
sealed class ArenaUIFakeHost : IArenaUIHost<int>
{
    public readonly List<int> Squad = [];
    public readonly Dictionary<int, string> Names = [];
    public readonly Dictionary<int, Vector3> Pos = [];
    public readonly HashSet<int> Gone = [], Dead = [], Down = [];
    public readonly Dictionary<int, ArenaCardStats> StatsOf = [];
    public readonly Dictionary<int, float> RatingOf = [];
    public readonly Dictionary<int, RosterStatus.Snapshot> HealthOf = [];
    public readonly HashSet<int> Prisoners = [], Caged = [];
    public readonly List<int> CagePrisoners = [], MatchPrisonerList = [], MatchHandlerList = [];
    public readonly List<string> Calls = [];
    public ArenaBuilding? Registry;
    public bool Pending, Spar, Town, Booking, Returning, Site = true, BeginOk = true, PrepareOk = true, MatchHasPrisoner;
    public string IngressStatusText = "Idle", SparStatusText = "Match started", PrepareStatus = "prep failed", ReturnStatus = "";
    public ArenaLocationMode Location = ArenaLocationMode.Arena;
    public ArenaMedicalPolicy.Protocol Protocol = ArenaMedicalPolicy.Protocol.RingsideAid;
    public int Opener = -1, SiteChecks;
    public (MatchRules.MatchMode Mode, int[] Fighters, MatchRules.MatchTeam[] Teams, int[] Escorts)? Begun;
    public ArenaBuilding? Bound;

    public static ArenaBuilding RegistryAt(float x, float z, string recordId = ArenaIdentityLogic.RegistryStringId) =>
        new(ArenaBuildingSource.Player, "7", recordId, null, true, new Vector3(x, 0f, z), 0f);

    /// <summary>A squad member at (x, z) with a combat skill.</summary>
    public void Add(int id, string name, float combat = 50f, float x = 0f, float z = 0f)
    {
        Squad.Add(id);
        Names[id] = name;
        Pos[id] = new Vector3(x, 0f, z);
        StatsOf[id] = new ArenaCardStats(combat, 40f, 30f, 20f);
    }

    public int None => -1;
    public bool IsNone(int c) => c < 0;
    public bool IsValid(int c) => c >= 0 && Names.ContainsKey(c) && !Gone.Contains(c);
    public bool IsDead(int c) => Dead.Contains(c);
    public bool IsUnconscious(int c) => Down.Contains(c);
    public string? Name(int c) => IsValid(c) ? Names[c] : null;
    public Vector3? Position(int c) => IsValid(c) && Pos.TryGetValue(c, out var p) ? p : null;
    public bool Stats(int c, out ArenaCardStats stats) => StatsOf.TryGetValue(c, out stats) && IsValid(c);
    public bool Health(int c, out RosterStatus.Snapshot status)
    {
        if (HealthOf.TryGetValue(c, out status)) return true;
        status = default;
        return false;
    }
    public float Rating(int c) => RatingOf.GetValueOrDefault(c, MmrRating.DefaultMmr);
    public void CollectPlayerSquad(List<int> into)
    {
        foreach (int c in Squad) if (IsValid(c) && !IsDead(c)) into.Add(c);
    }

    public bool IsRosterPrisoner(int c) => Prisoners.Contains(c);
    public bool HasCage(int c) => Caged.Contains(c);
    public bool MatchIncludesPrisoner() => MatchHasPrisoner;
    public void CollectNearbyCagePrisoners(ArenaBuilding registry, List<int> into) => into.AddRange(CagePrisoners);
    public IReadOnlyList<int> MatchPrisoners() => MatchPrisonerList;
    public IReadOnlyList<int> MatchHandlers() => MatchHandlerList;
    public bool PrisonersReturning() => Returning;
    public bool PrepareMatch(IReadOnlyList<int> prisoners, IReadOnlyList<int> handlers, out string status)
    {
        Calls.Add("PrepareMatch " + string.Join(",", prisoners) + " / " + string.Join(",", handlers));
        status = PrepareStatus;
        return PrepareOk;
    }
    public string ReturnAllToCages() { Calls.Add("ReturnAllToCages"); return ReturnStatus; }
    public void ClearMatchState() => Calls.Add("ClearMatchState");

    public ArenaBuilding? BoundRegistry() => Registry;
    public void BindRegistry(ArenaBuilding? registry) { Calls.Add("BindRegistry"); Bound = registry; Registry = registry; }
    public bool IngressPending() => Pending;
    public string IngressStatus() => IngressStatusText;
    public bool HasLocationSite() { SiteChecks++; return Site; }
    public string MissingLocationMessage() => Location == ArenaLocationMode.Banner ? "No banner placed near registry"
        : Location == ArenaLocationMode.SmallArena ? "No small arena placed near registry" : "No arena placed near registry";
    public ArenaLocationMode LocationMode() => Location;
    public void SetLocationMode(ArenaLocationMode mode) { Calls.Add("SetLocationMode " + mode); Location = mode; }
    public bool IngressBegin(MatchRules.MatchMode mode, int[] fighters, MatchRules.MatchTeam[] teams, int[] escorts)
    {
        Calls.Add("IngressBegin");
        Begun = (mode, fighters, teams, escorts);
        if (!BeginOk) IngressStatusText = "No arena placed near registry";
        return BeginOk;
    }
    public void IngressCancel() { Calls.Add("IngressCancel"); Pending = false; }
    public int UiOpener() => Opener;

    public bool SparActive() => Spar;
    public string SparStatus() => SparStatusText;
    public void SparStopManual() { Calls.Add("SparStopManual"); Spar = false; }

    public bool TownBusy() => Town;
    public bool TownHasBooking() => Booking;
    public void TownCancel() { Calls.Add("TownCancel"); Town = false; }

    public ArenaMedicalPolicy.Protocol GetProtocol() => Protocol;
    public void SetProtocol(ArenaMedicalPolicy.Protocol protocol)
    {
        Calls.Add("SetProtocol " + protocol);
        if (!Spar) Protocol = protocol;
    }

    public void ShowRecruitment() => Calls.Add("ShowRecruitment");
    public void TownBookieClose() => Calls.Add("TownBookieClose");
    public void RecruitmentAbandon() => Calls.Add("RecruitmentAbandon");
    public void TownArenaShow(ArenaBuilding registry, bool bindRegistry) => Calls.Add("TownArenaShow");
    public void TownArenaClose() => Calls.Add("TownArenaClose");
    public void OtherWindowsAbandon() => Calls.Add("OtherWindowsAbandon");
}
