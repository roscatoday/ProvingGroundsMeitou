using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.ArenaMedicalPolicy;

namespace ProvingGrounds.Tests.Glue;

/// <summary>A fake world for TownArenaLogic: ints are characters, <see cref="NoCharacter"/> (-1) is the C++ NULL (as Meitou's CharacterId.None
/// = (-1, 0)), and 0 is a real character, as Meitou's default(CharacterId) (slot 0: the first squad member); towns are longs, buildings ArenaBuildings by key.</summary>
sealed class TownFake : ITownHost<int>
{
    public const long Scratch = 304, Elsewhere = 12;
    public static readonly Vector3 RegistryPos = new(-100064, 1101, 13069);

    public bool World = true, Paused, InFlight, Pending, Active;
    public bool OpBusy { get; set; }
    public uint Now = 100_000;
    public readonly List<string> Debugs = [], Calls = [];
    public readonly Dictionary<int, Vector3> Pos = [];
    public readonly Dictionary<int, long> TownOf = [];
    public readonly HashSet<int> Invalid = [];
    public List<int>? Players = [];
    public readonly List<PlacedBuilding> Buildings = [];
    public readonly List<int> Participants = [];
    public Protocol Medical = Protocol.Bloodsport;
    public int SphereQueries, BusyQueries, SaveInFlightQueries;

    public static PlacedBuilding Building(string key, string record, bool finished = true, long town = Scratch, Vector3? pos = null) =>
        new(new ArenaBuilding(ArenaBuildingSource.Town, key, record, null, finished, pos ?? RegistryPos, 0.7f), town);
    public static PlacedBuilding TownRegistry(bool finished = true, long town = Scratch) => Building("10.34:30290", "195-Proving Grounds.mod", finished, town);

    /// <summary>A player character at registry + (dx, dz) standing in <paramref name="town"/>.</summary>
    public int Player(int id, long town = Scratch, float dx = 10, float dz = 10)
    {
        Pos[id] = RegistryPos + new Vector3(dx, 0, dz);
        TownOf[id] = town;
        Players?.Add(id);
        return id;
    }

    public bool HasWorld => World;
    public bool IsPaused => Paused;
    public uint NowMs => Now;
    public void Debug(string line) => Debugs.Add(line);
    public const int NoCharacter = -1;
    public bool IsNull(int c) => c == NoCharacter;
    public bool Valid(int c) => c != NoCharacter && !Invalid.Contains(c);
    public Vector3 Position(int c) => Pos.TryGetValue(c, out var p) ? p : Vector3.Zero;
    public long CurrentTown(int c) => TownOf.TryGetValue(c, out var t) ? t : TownKey.None;
    public IReadOnlyList<int>? PlayerCharacters() => Players;
    public IReadOnlyList<PlacedBuilding>? BuildingsInSphere(Vector3 origin, float radius, int max)
    {
        SphereQueries++;
        if (!World) return null;
        return [.. Buildings.Where(b => Vector3.Distance(b.Building.Position, origin) <= radius).Take(max)];
    }
    public PlacedBuilding? Resolve(ArenaBuildingRef held)
    {
        foreach (var b in Buildings) if (b.Building.Ref == held) return b;
        return null;
    }
    public bool ArenaOperationBusy() { BusyQueries++; return OpBusy; }
    public bool SaveInFlight { get { SaveInFlightQueries++; return InFlight; } }
    public bool IngressPending() => Pending;
    public void IngressCancel() { Calls.Add("ingress cancel"); Pending = false; }
    public bool SparIsActive() => Active;
    public int SparParticipantCount() => Participants.Count;
    public int SparParticipant(int i) => i < Participants.Count ? Participants[i] : NoCharacter;
    public void SparAbortForSave() { Calls.Add("spar abort"); Active = false; }
    /// <summary>The town rules' record (task 7.4) and its squad memory, used as the glue does (TownCombatRules.BoutEnded: TryEnd, at Finish since
    /// task 8.4b).</summary>
    public TownBoutForgiveness<int>? Rules;
    public ITempEnemyHost<int>? RulesMemory;
    public Exception? RulesError;
    public void TownRulesEventEnded()
    {
        Calls.Add("rules event ended");
        if (Rules is not null && RulesMemory is not null) RulesError = Rules.TryEnd(RulesMemory, true, out _);
    }
    public Protocol GetProtocol() => Medical;
    /// <summary>As ArenaMedical.SetProtocol (ArenaMedicalLogic.SetProtocol, ArenaMedical.cpp:214-220): refused while a spar match is active.</summary>
    public void SetProtocol(Protocol p) { if (Active) { Calls.Add("protocol " + p + " refused"); return; } Calls.Add("protocol " + p); Medical = p; }
    public void AnnouncerRelease() => Calls.Add("announcer release");
    public void AnnouncerTickReleases() => Calls.Add("announcer tick");
    public void AnnouncerAbandon() => Calls.Add("announcer abandon");
    public void SpectatorsRelease() => Calls.Add("spectators release");
    public void SpectatorsTick() => Calls.Add("spectators tick");
    public void SpectatorsAbandon() => Calls.Add("spectators abandon");
    public void CorpseCleanupRelease() => Calls.Add("corpse release");
    public void CorpseCleanupTick() => Calls.Add("corpse tick");
    public void CorpseCleanupAbandon() => Calls.Add("corpse abandon");
    public void AftercareRelease() => Calls.Add("aftercare release");
    public void AftercareTickBedRecovery() => Calls.Add("aftercare bed tick");
    public void AftercareAbandon() => Calls.Add("aftercare abandon");
    public bool LimbShop;
    public bool LimbShopIsActive() => LimbShop;
    public void LimbShopTick() => Calls.Add("limbshop tick");
    public void LimbShopAbandon() => Calls.Add("limbshop abandon");
    public void BookieSettle(int outcome) => Calls.Add("bookie settle " + outcome);
    public void BookieRefundUnrestoredWager(ArenaPersistence.PlannedNpcBout saved) => Calls.Add("bookie refund unrestored");
    public void BookieCancelNpcBout(string reason) => Calls.Add("bookie cancel: " + reason);
    public void BookieAbandon() => Calls.Add("bookie abandon");
    public void DiagnosticCancelPending() => Calls.Add("diagnostic cancel");
    /// <summary>Task 7.2: a real worker can be wired in (the world-change test); otherwise the calls are only recorded.</summary>
    public TownMatchmakingWorker? Worker;
    public void MatchmakingShutdown() { Calls.Add("matchmaking shutdown"); Worker?.Shutdown(); }

    // ---------- task 7.2: the roster reads (ITownRosterHost) ----------
    public const string Medic = "100-Proving Grounds.mod";
    public readonly List<int> Characters = [];                    // the sphere query's characters, in query order
    public readonly Dictionary<int, string> RoleOf = [], NameOf = [], TokenOf = [];
    public readonly Dictionary<int, double> AbilityOf = [], RatingOf = [];
    public readonly Dictionary<int, (float Limb, float Blood)> Health = [];
    public readonly HashSet<int> OtherFaction = [], NoIdentity = [], Aftercare = [], InLimbShop = [], CarryingSet = [], CarriedSet = [],
        DeadSet = [], KoSet = [], Fighting = [], NeedsAid = [], NotMedicallyAble = [], NoHealth = [], NoHandle = [];
    public readonly HashSet<string> Created = [];
    public readonly List<int> Removed = [];
    public TownArenaLogic<int>? Logic;
    public int CharacterQueries;

    /// <summary>A town character at registry + (dx, 0, dz) in <paramref name="town"/> with record <paramref name="role"/>, healthy, ability 50.</summary>
    public int Npc(int id, string role, long town = Scratch, float dx = 20, float dz = 0, double ability = 50, double rating = 100)
    {
        Pos[id] = RegistryPos + new Vector3(dx, 0, dz);
        TownOf[id] = town;
        RoleOf[id] = role;
        NameOf[id] = "Npc " + id;
        AbilityOf[id] = ability;
        RatingOf[id] = rating;
        Characters.Add(id);
        return id;
    }

    string Token(int c) => TokenOf.TryGetValue(c, out var t) ? t : "fid-" + c;
    public IReadOnlyList<int>? CharactersInSphere(Vector3 origin, float radius, int max)
    {
        CharacterQueries++;
        if (!World) return null;
        return [.. Characters.Where(c => Vector3.Distance(Position(c), origin) <= radius).Take(max)];
    }
    public bool IsTownFaction(int c) => !OtherFaction.Contains(c);
    public string Role(int c) => RoleOf.TryGetValue(c, out var r) ? r : "";
    public string Name(int c) => NameOf.TryGetValue(c, out var n) ? n : "Player " + c;
    public bool GetOrCreateIdentity(int c, out string id)
    {
        id = "";
        if (NoIdentity.Contains(c)) return false;
        id = Token(c);
        Created.Add(id);
        return true;
    }
    public bool FindIdentity(int c, out string id)
    {
        id = "";
        if (NoIdentity.Contains(c) || !Created.Contains(Token(c))) return false;
        id = Token(c);
        return true;
    }
    public void RemoveTownNpcProgression(int c) => Removed.Add(c);
    public string SelectionIdentity(int c) => NoHandle.Contains(c) ? "" : "player:0:0:0:" + c + ":1";
    public double Ability(int c) => AbilityOf.TryGetValue(c, out var a) ? a : 40;
    public double TownRating(int c) => RatingOf.TryGetValue(c, out var r) ? r : 100;
    public bool AftercareManaged(int c) => Aftercare.Contains(c);
    public bool LimbShopManaged(int c) => InLimbShop.Contains(c);
    public bool TownArenaFighter(int c) => Logic?.IsFighter(this, c) ?? false;
    public bool IsCarrying(int c) => CarryingSet.Contains(c);
    public bool IsCarried(int c) => CarriedSet.Contains(c);

    // ---------- task 7.2: the background search ----------
    public readonly List<uint> Generations = [];
    public readonly List<TownMatchmakingSearch.Request> Enqueued = [];
    public readonly Queue<TownMatchmakingSearch.Result> Results = new();
    public void MatchmakingBeginGeneration(uint generation) => Generations.Add(generation);
    public bool MatchmakingEnqueue(TownMatchmakingSearch.Request request) { Enqueued.Add(request.Clone()); return true; }
    public bool MatchmakingPoll(out TownMatchmakingSearch.Result? result) => Results.TryDequeue(out result);

    // ---------- task 7.2: readiness ----------
    public bool IsDead(int c) => DeadSet.Contains(c);
    public bool IsUnconscious(int c) => KoSet.Contains(c);
    public bool InCombatMode(int c) => Fighting.Contains(c);
    public bool NeedsStabilization(int c) => NeedsAid.Contains(c);
    public bool ReadHealth(int c, out RosterStatus.Snapshot health, out string error)
    {
        health = default;
        if (NoHealth.Contains(c)) { error = HealthError.TryGetValue(c, out var e) ? e : "anatomy is empty"; return false; }
        var (limb, blood) = Health.TryGetValue(c, out var v) ? v : (1f, 1f);
        health.LowestLimb = limb; health.Blood = blood;
        error = "";
        return true;
    }
    public bool MedicallyAble(int c) => !NotMedicallyAble.Contains(c);
    public bool ActiveSave = true;
    public bool HasActiveSave => ActiveSave;

    // ---------- task 7.3: NPC-vs-NPC bouts ----------
    public int None => NoCharacter;
    public double Hours = 100.0;
    public double TotalHours => Hours;
    public TownChallengePolicy.Card Card = new();
    public TownChallengePolicy.Card TownChallenges() => Card;
    public string BuildingUid(ArenaBuildingRef building) => "uid:" + building.Key;
    public readonly Dictionary<int, string> UidOf = [], HealthError = [];
    public string CharacterUid(int c) => UidOf.TryGetValue(c, out var u) ? u : c == NoCharacter ? "" : "cuid:" + c;
    public readonly List<string> Messages = [];
    public void ShowPlayerMessage(string text) => Messages.Add(text);
    public ArenaLocationMode Location = ArenaLocationMode.SmallArena;
    public ArenaLocationMode IngressGetLocationMode() => Location;
    public void IngressSetLocationMode(ArenaLocationMode mode) { Calls.Add("location " + mode); Location = mode; }
    public bool BeginOk = true;
    public (MatchRules.MatchMode Mode, int[] Fighters, MatchRules.MatchTeam[] Teams, int Count, ArenaBuildingRef Arena, ArenaLocationMode Location)? Begun;
    public bool IngressBegin(MatchRules.MatchMode mode, int[] fighters, MatchRules.MatchTeam[] teams, int count, ArenaBuildingRef arena)
    {
        Calls.Add("ingress begin");
        Begun = (mode, fighters, teams, count, arena, Location);
        if (BeginOk) Pending = true;
        return BeginOk;
    }
    public string IngressStatus = "Walking into arena...";
    public string IngressGetStatus() => IngressStatus;
    public MatchRules.MatchMode Mode = MatchRules.MatchMode.TeamAvB;
    public MatchRules.MatchMode SparMode() => Mode;
    public readonly HashSet<int> Eliminated = [];
    public bool SparIsEliminated(int c) => Eliminated.Contains(c);
    public ArenaBuildingRef? Ring;
    public void RingGuardSetArena(ArenaBuildingRef arena) { Calls.Add("ring"); Ring = arena; }
    public int HoldReleases;
    public void ReleaseTownHolds() { Calls.Add("release holds"); HoldReleases++; }
    public void TownRulesBoutStarted() => Calls.Add("rules bout started");
    public (int[] Fighters, MatchRules.MatchTeam[] Teams, int Count, double A, double B)? BookieBout;
    public void BookieBeginNpcBout(int[] fighters, MatchRules.MatchTeam[] teams, int count, double scoreA, double scoreB)
    { Calls.Add("bookie begin"); BookieBout = (fighters, teams, count, scoreA, scoreB); }
    public bool PreparationOk = true, Wager;
    public bool BookiePreparationValid() => PreparationOk;
    public bool BookieHasPendingWager() => Wager;
    public void BookieNpcAssembling() => Calls.Add("bookie assembling");
    public void BookieCombatStarted() => Calls.Add("bookie combat started");
    public void BookieCapturePlannedNpcBout(ArenaPersistence.PlannedNpcBout output) { Calls.Add("bookie capture"); output.Market = Wager; output.WagerPending = Wager; }
    public bool RestoreOk = true;
    public bool BookieRestorePlannedNpcBout(ArenaPersistence.PlannedNpcBout saved) { Calls.Add("bookie restore"); return RestoreOk; }
    public string[]? AnnouncedRoles;
    public void AnnouncerBegin(ArenaBuildingRef arena, bool playerMatch, int challenge, string[] roles, int roleCount, int[] fighters, int fighterCount)
    { Calls.Add("announcer begin"); AnnouncedRoles = roles.Take(roleCount).ToArray(); }
    public void SpectatorsBegin(ArenaBuildingRef arena) => Calls.Add("spectators begin");
    public void SpectatorsReactToResult(SparPodium.Snapshot result) => Calls.Add("spectators react");
    public void CorpseCleanupBegin(ArenaBuildingRef arena, int[] fighters, int count) => Calls.Add("corpse begin");
    public void CorpseCleanupSeal() => Calls.Add("corpse seal");
    public void CorpseCleanupAdmitArenaDeaths() => Calls.Add("corpse admit");
    public bool CorpsePending;
    public bool CorpseCleanupHasPending() => CorpsePending;
    public string CorpseCleanupGetStatus() => "corpse status";
    public bool Control = true;
    public bool AftercareControlAvailable() => Control;
    public List<int>? Doctors;
    public Vector3? Standby;
    public void AftercareBegin(List<int> doctors, int[] fighters, int count, ArenaBuildingRef arena, Vector3 standby)
    { Calls.Add("aftercare begin"); Doctors = [.. doctors]; Standby = standby; }
    public void AftercareStandby() => Calls.Add("aftercare standby");
    public readonly List<int> Winners = [];
    public void AftercareMarkWinner(int c) => Winners.Add(c);
    public int TreatRounds;   // how many Treat calls still answer "care goes on"
    public bool AftercareTreat(float elapsed, bool eliminatedOnly) { Calls.Add("treat " + (eliminatedOnly ? "eliminated" : "all")); return TreatRounds-- > 0; }
    public string AftercareGetStatus() => "Arena care complete; medics returning to hospital";
    public void LimbShopBegin(ArenaBuildingRef? registry, int[] participants, int count) => Calls.Add("limbshop begin " + count);
    public TownMatchmakingPolicy.DiagnosticRequest DiagnosticNextRequest() => new();
    public void DiagnosticAssign(TownMatchmakingPolicy.Match match, double hours) => Calls.Add("diagnostic assign");
    public void DiagnosticCompletePending(SparPodium.Snapshot result, double hours) => Calls.Add("diagnostic complete");
    public void DiagnosticAbortPending() => Calls.Add("diagnostic abort");
    public void DiagnosticRetryPending() => Calls.Add("diagnostic retry");

    // ---------- task 7.5: the challenge card ----------
    public string SeedKey = "";
    public string CardSeedKey => SeedKey;
    public TownChallengeBuyInPolicy.Account Account;
    public ref TownChallengeBuyInPolicy.Account ChallengeAccount() => ref Account;
    /// <summary>The player's money (Meitou world.Player.Money); null = no player (Balance -1, as TownChallengeWallet with no faction).</summary>
    public int? Money = 20000;
    public bool TakeFails;
    public int WalletBalance() => Money ?? -1;
    public bool WalletTake(int amount)
    {
        if (Money is not { } m || TakeFails || amount <= 0 || m < amount) return false;
        Money = m - amount; Calls.Add("take " + amount); return true;
    }
    public bool WalletCredit(int amount)
    {
        if (Money is not { } m || amount <= 0 || m < 0 || m > int.MaxValue - amount) return false;
        Money = m + amount; Calls.Add("credit " + amount); return true;
    }
    public readonly List<(int Outcome, int Credit)> Settled = [];
    public void LogChallengeSettled(int outcome, int credit) => Settled.Add((outcome, credit));
    public CombatBalanceLog.ChallengeCard? Accepted;
    public void LogChallengeAccepted(CombatBalanceLog.ChallengeCard card) => Accepted = card;
}

public class TownArenaLogicTests
{
    static (TownArenaLogic<int> L, TownFake F) Scratch()
    {
        var f = new TownFake();
        f.Buildings.Add(TownFake.TownRegistry());
        f.Player(0);
        return (new TownArenaLogic<int>(TownFake.NoCharacter), f);
    }

    static void TickAt(TownArenaLogic<int> l, TownFake f, uint now) { f.Now = now; l.Tick(f); }

    // ---------- active (TW §2.1) ----------

    [Fact]
    public void FirstScheduleCheckBindsTheFinishedTownRegistryAndScratchIsActive()
    {
        var (l, f) = Scratch();
        Assert.False(l.IsActive(f));
        l.Tick(f);
        Assert.Equal(TownFake.TownRegistry().Building.Ref, l.State.TownRegistry);
        Assert.True(l.IsActive(f));
        Assert.Equal(1, l.State.ScheduleChecks);
        Assert.Equal(TownArena.Text.Idle, l.GetStatus());
    }

    [Fact]
    public void RegistryOfAnotherTownUnfinishedOrAPlayerRegistryIsNeverBound()
    {
        var f = new TownFake();
        f.Buildings.Add(TownFake.TownRegistry(finished: false));
        f.Buildings.Add(TownFake.Building("other", "195-Proving Grounds.mod", town: TownFake.Elsewhere));
        f.Buildings.Add(TownFake.Building("player-reg", "45-Proving Grounds.mod"));   // the player Registry: not the Town Registry 195
        f.Player(0);
        var l = new TownArenaLogic<int>(TownFake.NoCharacter);
        l.Tick(f);
        Assert.Null(l.State.TownRegistry);
        Assert.False(l.IsActive(f));
    }

    [Fact]
    public void RegistryFartherThan2500IsNotFound()
    {
        var f = new TownFake();
        f.Buildings.Add(TownFake.TownRegistry());
        f.Player(0, dx: 2400, dz: 800);   // 2,530 away
        var l = new TownArenaLogic<int>(TownFake.NoCharacter);
        l.Tick(f);
        Assert.Null(l.State.TownRegistry);
    }

    [Fact]
    public void AnEmptyTownAtTheFirstCheckIsToleratedAndTheRegistryIsFoundWhenItLoads()
    {
        var f = new TownFake();
        f.Player(0);
        var l = new TownArenaLogic<int>(TownFake.NoCharacter);
        l.Tick(f);
        Assert.Null(l.State.TownRegistry);
        f.Buildings.Add(TownFake.TownRegistry());   // the town filled in
        TickAt(l, f, f.Now + 4999);
        Assert.Null(l.State.TownRegistry);          // not before the next 5 s check
        TickAt(l, f, f.Now + 1);
        Assert.True(l.IsActive(f));
    }

    [Fact]
    public void PlayerOutsideEveryTownSearchesNothing()
    {
        var f = new TownFake();
        f.Buildings.Add(TownFake.TownRegistry());
        f.Player(0, town: TownKey.None);
        var l = new TownArenaLogic<int>(TownFake.NoCharacter);
        l.Tick(f);
        Assert.Equal(0, f.SphereQueries);
        Assert.Null(l.State.TownRegistry);
    }

    [Fact]
    public void ActiveNeedsAPlayerInTheRegistrysTownAndAValidCharacter()
    {
        var (l, f) = Scratch();
        l.Tick(f);
        f.TownOf[0] = TownFake.Elsewhere;
        Assert.False(l.IsActive(f));
        f.TownOf[0] = TownFake.Scratch;
        f.Invalid.Add(0);
        Assert.False(l.IsActive(f));
        f.Invalid.Clear();
        f.Players = null;   // no player (!ou->player)
        Assert.False(l.IsActive(f));
    }

    [Fact]
    public void BoundRegistryThatIsGoneIsDroppedAndSearchedAgain()
    {
        var (l, f) = Scratch();
        l.Tick(f);
        f.Buildings.Clear();
        TickAt(l, f, f.Now + 5000);
        Assert.Null(l.State.TownRegistry);
        f.Buildings.Add(TownFake.TownRegistry());
        TickAt(l, f, f.Now + 5000);
        Assert.NotNull(l.State.TownRegistry);
    }

    // ---------- the 5 s schedule clock ----------

    [Fact]
    public void ScheduleClockAdvancesEvery5000MsWrapSafe()
    {
        var (l, f) = Scratch();
        TickAt(l, f, uint.MaxValue - 1000);
        Assert.Equal(1, l.State.ScheduleChecks);
        TickAt(l, f, 3000);                  // 4,001 ms later across the wrap
        Assert.Equal(1, l.State.ScheduleChecks);
        TickAt(l, f, 3999);                  // 5,000 ms later
        Assert.Equal(2, l.State.ScheduleChecks);
        Assert.Equal(3999u, l.State.LastScheduleCheck);
    }

    [Fact]
    public void ScheduleClockStopsWhilePausedOrScheduleDisabledWithoutBooking()
    {
        var (l, f) = Scratch();
        f.Paused = true;
        l.Tick(f);
        Assert.Equal(0, l.State.ScheduleChecks);
        f.Paused = false;
        l.SetScheduleEnabled(f, false);
        TickAt(l, f, f.Now + 10000);
        Assert.Equal(0, l.State.ScheduleChecks);
        l.State.BookingPending = true;       // a booking keeps the clock running while paused
        TickAt(l, f, f.Now + 10000);
        Assert.Equal(1, l.State.ScheduleChecks);
    }

    [Fact]
    public void ScheduleAsksWorldLifecycleOnlyWithAPlayerInTownAndNoSaveInFlight()
    {
        var (l, f) = Scratch();
        l.Tick(f);
        Assert.Equal(1, f.BusyQueries);      // active: PlanNpcBout is guarded by IsArenaOperationBusy
        f.InFlight = true;
        TickAt(l, f, f.Now + 5000);
        Assert.Equal(1, f.BusyQueries);      // a prepared save waits: nothing planned or begun
        f.InFlight = false;
        f.TownOf[0] = TownFake.Elsewhere;
        TickAt(l, f, f.Now + 5000);
        Assert.Equal(1, f.BusyQueries);      // nobody in the registry's town: returns first
    }

    [Fact]
    public void NoWorldDoesNothing()
    {
        var (l, f) = Scratch();
        f.World = false;
        l.Tick(f);
        Assert.Equal(0, l.State.ScheduleChecks);
    }

    // ---------- the stop rules (TownArena.cpp:1864-1881) ----------

    static (TownArenaLogic<int> L, TownFake F) RunningEvent()
    {
        var (l, f) = Scratch();
        l.Tick(f);
        // A stand-in event (no fighters) as BeginEvent leaves the state (:1718-1722): the protocol remembered, Ringside Aid set, Preparing.
        l.State.PreviousProtocol = f.GetProtocol();
        f.SetProtocol(Protocol.RingsideAid);
        l.State.Phase = TownPhase.Preparing;
        l.State.ActiveRegistry = l.State.TownRegistry;
        Assert.True(l.IsBusy());
        f.Calls.Clear();
        return (l, f);
    }

    [Fact]
    public void TheTownRulesRecordLastsThroughTheAftercareAndIsClearedAtTheFinishAfterIt()
    {
        // task 8.4b: the C++'s IsIncidentPair holds through Aftercare (TownArena.cpp:1424-1426), so the after-bout TEMP_ENEMY clearing (task 7.4)
        // runs at the Finish that ends the aftercare, not at the match's end: a tag the bout's pair writes during the aftercare is cleared too
        var (l, f) = RunningEvent();
        l.State.Phase = TownPhase.Aftercare;
        f.TreatRounds = 1;
        TickAt(l, f, f.Now + 1000);   // the first medic poll: the care goes on
        Assert.Contains("treat all", f.Calls);
        Assert.DoesNotContain("rules event ended", f.Calls);
        Assert.True(l.IsBusy());
        TickAt(l, f, f.Now + 1000);   // the care completes: Finish
        Assert.False(l.IsBusy());
        int release = f.Calls.LastIndexOf("aftercare release"), rules = f.Calls.IndexOf("rules event ended");   // Finish's own release
        Assert.True(release >= 0 && rules == release + 1, string.Join(", ", f.Calls));
    }

    [Fact]
    public void RunningEventStopsWhenNoPlayerCharacterRemainsInScratch()
    {
        var (l, f) = RunningEvent();
        f.Pending = true;
        Assert.Equal(Protocol.RingsideAid, f.Medical);   // the stand-in set it as BeginEvent does (:1718-1719)
        TickAt(l, f, f.Now + 16);
        f.Calls.Clear();
        Assert.True(l.IsBusy());                // still in town: keeps running
        f.TownOf[0] = TownFake.Elsewhere;
        f.LimbShop = true;
        TickAt(l, f, f.Now + 16);
        Assert.False(l.IsBusy());
        Assert.Equal(TownArena.Text.EventStopped, l.GetStatus());
        Assert.Equal("Town event stopped: no player characters remain in Scratch", l.GetStatus());
        Assert.Equal(["ingress cancel", "announcer release", "spectators release", "corpse release", "bookie settle -1", "aftercare release",
            "rules event ended", "release holds", "protocol Bloodsport"], f.Calls);
        Assert.Equal(Protocol.Bloodsport, f.Medical);   // the protocol before the event
        Assert.Contains("Proving Grounds: town trial - Town event stopped: no player characters remain in Scratch", f.Debugs);
        Assert.Null(l.State.ActiveRegistry);
    }

    [Fact]
    public void StopAbortsTheSparOnlyWhenTheTownOwnsTheMatch()
    {
        var (l, f) = RunningEvent();
        l.State.Fighters[0] = 7; l.State.Fighters[1] = 8; l.State.FighterCount = 2;
        f.Active = true; f.Participants.AddRange([7, 8]);
        Assert.True(l.OwnsMatch(f));
        Assert.True(l.IsFighter(f, 7));
        f.Players!.Clear();
        l.Tick(f);
        Assert.Contains("spar abort", f.Calls);
        Assert.True(f.Calls.IndexOf("rules event ended") > f.Calls.IndexOf("spar abort"));   // task 7.4 / 8.4b: the clearing at Finish, after the abort
        Assert.DoesNotContain("ingress cancel", f.Calls);   // ingress not pending
        Assert.Equal(0, l.State.FighterCount);
    }

    [Fact]
    public void StopKeepsAForeignSparAndAnIngressOutsidePreparing()
    {
        var (l, f) = RunningEvent();
        l.State.Phase = TownPhase.Fighting;
        f.Pending = true;
        f.Active = true; f.Participants.AddRange([5, 6]);   // a player spar, not the town's
        f.Players!.Clear();
        l.Tick(f);
        Assert.DoesNotContain("spar abort", f.Calls);
        Assert.DoesNotContain("ingress cancel", f.Calls);
        Assert.False(l.IsBusy());
    }

    [Fact]
    public void EventStopsWhenItsRegistryIsGone()
    {
        var (l, f) = RunningEvent();
        f.Buildings.Clear();
        l.Tick(f);
        Assert.Equal(TownArena.Text.EventStopped, l.GetStatus());
    }

    [Fact]
    public void AnnouncedBoutIsCancelledWhenScratchIsUnobservedAndAPaidBookingSurvives()
    {
        var (l, f) = Scratch();
        l.Tick(f);
        l.State.PlannedNpc.Valid = true;
        l.State.PlannedRegistry = l.State.TownRegistry;
        l.State.NextNpcHours = 42;
        l.State.BookingPending = true;
        l.State.QueuedChallenge = 2;
        f.Calls.Clear();
        TickAt(l, f, f.Now + 16);
        Assert.True(l.State.PlannedNpc.Valid);   // someone is in town
        f.Calls.Clear();
        f.TownOf[0] = TownFake.Elsewhere;
        TickAt(l, f, f.Now + 16);
        Assert.False(l.State.PlannedNpc.Valid);
        Assert.Equal(0.0, l.State.NextNpcHours);
        Assert.Null(l.State.PlannedRegistry);
        Assert.Equal(f.Now, l.State.LastScheduleCheck);
        Assert.Equal(["bookie cancel: Scheduled bout cancelled: no player characters remain in Scratch", "diagnostic cancel"], f.Calls);
        Assert.True(l.HasBooking());               // the booking survives
        Assert.Equal(2, l.State.QueuedChallenge);
    }

    [Fact]
    public void RestoredBoutWaitingForItsFightersIsNotCancelledByTheUnobservedRule()
    {
        var (l, f) = Scratch();
        l.Tick(f);
        l.State.PlannedNpc.Valid = true;
        l.State.RestoredNpcPending = true;   // planned registry null: unobserved
        f.Calls.Clear();
        TickAt(l, f, f.Now + 16);
        Assert.True(l.State.PlannedNpc.Valid);
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("bookie", StringComparison.Ordinal));   // not cancelled (the per-tick calls ran)
    }

    [Fact]
    public void BookingSurvivesTheEventStop()
    {
        var (l, f) = RunningEvent();
        l.State.BookingPending = true;
        f.Players!.Clear();
        l.Tick(f);
        Assert.True(l.HasBooking());
        Assert.False(l.IsBusy());
    }

    // ---------- schedule on/off, cancel, booking ----------

    [Fact]
    public void SetScheduleEnabledTextsAndCancelsTheAnnouncedBout()
    {
        var (l, f) = Scratch();
        l.Tick(f);
        l.State.PlannedNpc.Valid = true;
        l.SetScheduleEnabled(f, false);
        Assert.False(l.IsScheduleEnabled());
        Assert.Equal("Ambient NPC bouts paused; current fight continues", l.GetStatus());
        Assert.Contains("bookie cancel: NPC scheduling paused", f.Calls);
        Assert.Equal(0u, l.State.LastScheduleCheck);
        l.SetScheduleEnabled(f, true);
        Assert.True(l.IsScheduleEnabled());
        Assert.Equal("Ambient NPC bouts resumed", l.GetStatus());
    }

    [Fact]
    public void CancelOnIdleCancelsTheBookingAndAnnouncedBoutOnly()
    {
        var (l, f) = Scratch();
        l.State.BookingPending = true;
        l.State.BookedPlayer = 0;
        l.State.BookedTeam.Add(0);
        l.State.BookedRegistry = TownFake.TownRegistry().Building.Ref;
        l.State.PlannedNpc.Valid = true;
        l.Cancel(f);
        Assert.False(l.HasBooking());
        Assert.Equal(TownFake.NoCharacter, l.State.BookedPlayer);   // the C++ setNull
        Assert.Empty(l.State.BookedTeam);
        Assert.Null(l.State.BookedRegistry);
        Assert.False(l.State.PlannedNpc.Valid);
        Assert.Equal("Town booking cancelled", l.GetStatus());
        Assert.Contains("bookie cancel: Scheduled bout cancelled", f.Calls);
        Assert.DoesNotContain("aftercare release", f.Calls);   // nothing was running: no Finish
    }

    [Fact]
    public void CancelForSaveFinishesARunningEventAndKeepsTheAnnouncedBout()
    {
        var (l, f) = RunningEvent();
        l.State.PlannedNpc.Valid = true;
        f.Pending = true;
        l.CancelForSave(f);
        Assert.False(l.IsBusy());
        Assert.True(l.State.PlannedNpc.Valid);   // a future market survives a save
        Assert.Equal("Town trial cancelled; town AI released", l.GetStatus());
        Assert.Contains("ingress cancel", f.Calls);
        Assert.DoesNotContain("bookie settle -1", f.Calls);   // the planned bout's market stays open
    }

    [Fact]
    public void ANormalTickCallsTheOtherSubsystemsInTheCppOrder()
    {
        var (l, f) = Scratch();
        f.LimbShop = true;
        l.Tick(f);
        Assert.Equal(["limbshop tick", "spectators tick", "announcer tick", "corpse tick", "aftercare bed tick"], f.Calls);
        f.Calls.Clear();
        f.LimbShop = false;
        TickAt(l, f, f.Now + 16);
        Assert.Equal(["spectators tick", "announcer tick", "corpse tick", "aftercare bed tick"], f.Calls);   // no visit: no limb-shop tick
    }

    [Fact]
    public void AStaleUnfinishedRegistryStillAnswersThePlayerCheckWhenDiscoveryIsNotTaken()
    {
        // C++ :1948-1961: registryReady false nulls g_townRegistry, but with the schedule paused and a booking pending ShouldDiscoverRegistry is
        // false, so the local ambientRegistry (the unfinished, still valid building) goes on to HasPlayerInRegistryTown.
        var (l, f) = Scratch();
        l.Tick(f);
        f.Buildings[0] = TownFake.TownRegistry(finished: false);   // the same building, no longer finished
        l.State.SchedulePaused = true;
        l.State.BookingPending = true;
        int sphere = f.SphereQueries, inFlight = f.SaveInFlightQueries;
        TickAt(l, f, f.Now + 5000);
        Assert.Null(l.State.TownRegistry);
        Assert.Equal(sphere, f.SphereQueries);                 // no discovery
        Assert.Equal(inFlight + 1, f.SaveInFlightQueries);    // the player check passed on the stale building: the tick went on
        f.TownOf[0] = TownFake.Elsewhere;
        l.State.TownRegistry = TownFake.TownRegistry().Building.Ref;
        TickAt(l, f, f.Now + 5000);
        Assert.Equal(inFlight + 1, f.SaveInFlightQueries);    // nobody in its town: returned at the player check
    }

    [Fact]
    public void OldWorldAbandonEndsTheSparFirstSoTheProtocolRestoreIsNotRefused()
    {
        var (l, f) = RunningEvent();                  // the event set Ringside Aid over Bloodsport
        f.Active = true;                               // a town match runs when the world is left
        OldWorldAbandon.Run(() => f.Active = false, () => l.AbandonWorldState(f), (_, e) => throw e);
        Assert.Equal(Protocol.Bloodsport, f.Medical);
        Assert.Contains("protocol Bloodsport", f.Calls);

        var (l2, f2) = RunningEvent();
        f2.Active = true;
        l2.AbandonWorldState(f2);                      // the wrong order: the lock refuses the restore
        Assert.Equal(Protocol.RingsideAid, f2.Medical);
        Assert.Contains("protocol Bloodsport refused", f2.Calls);
    }

    [Fact]
    public void OldWorldAbandonStillAbandonsTheTownWhenTheSparStepThrows()
    {
        var (l, f) = RunningEvent();
        var failed = new List<string>();
        OldWorldAbandon.Run(() => { f.Active = false; throw new InvalidOperationException("x"); }, () => l.AbandonWorldState(f), (step, _) => failed.Add(step));
        Assert.Equal([OldWorldAbandon.SparStep], failed);
        Assert.False(l.IsBusy());
        Assert.Equal(Protocol.Bloodsport, f.Medical);
    }

    [Fact]
    public void StatusLogsOnlyAChange()
    {
        var (l, f) = Scratch();
        l.SetScheduleEnabled(f, true);
        l.SetScheduleEnabled(f, true);
        Assert.Single(f.Debugs);
    }

    // ---------- accessors and the world reset ----------

    [Fact]
    public void AccessorsAnswerTheIdleWorld()
    {
        var (l, f) = Scratch();
        Assert.False(l.IsBusy());
        Assert.False(l.IsAftercare());
        Assert.False(l.IsPlayerMatch());
        Assert.False(l.OwnsMatch(f));
        Assert.Equal(1.0f, l.GetActiveMarksMultiplier(f));
        Assert.Equal(0, l.GetActiveMarksBonus(f));
        Assert.False(l.IsFighter(f, 0));   // the first player character (default(TId)) is no fighter of an idle town
        var into = new List<int>();
        l.CopyFighters(into);
        Assert.Empty(into);
    }

    [Fact]
    public void AbandonWorldStateResetsAndRestoresTheProtocolOnlyWhileBusy()
    {
        var (l, f) = RunningEvent();
        f.Medical = Protocol.RingsideAid;
        l.State.BookingPending = true;
        l.State.PlannedNpc.Valid = true;
        l.State.SchedulePaused = true;
        l.AbandonWorldState(f);
        Assert.Equal(["matchmaking shutdown", "announcer abandon", "corpse abandon", "limbshop abandon", "aftercare abandon", "spectators abandon",
            "bookie abandon", "diagnostic cancel", "protocol Bloodsport"], f.Calls);
        Assert.False(l.IsBusy());
        Assert.False(l.HasBooking());
        Assert.False(l.State.PlannedNpc.Valid);
        Assert.True(l.IsScheduleEnabled());
        Assert.Null(l.State.TownRegistry);
        Assert.Equal(TownArena.Text.Idle, l.GetStatus());
        f.Calls.Clear();
        l.AbandonWorldState(f);
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("protocol", StringComparison.Ordinal));   // idle: no protocol write
    }
}
