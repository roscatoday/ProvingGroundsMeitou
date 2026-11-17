using System.Numerics;
using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>Task 8.5: TownLimbShop.cpp's decisions (Core/Glue/TownLimbShopLogic.cs) over a fake world: Begin's queue (stumps, players, duplicates, the
/// grade and variant from the seed, the identity fallback), the 5 s readiness poll and the aftercare wait, the trader search (record, town, radius,
/// nearest, free), the shared external slots, facing and the RUN walk, the 0.5 s re-orders, the arrival, the two lines 2.5 s apart, the fit (quality,
/// one refresh, the purchase line), the silent fallbacks (no trader, a refused reservation, a 20 s stall, an interrupted trader), the fighter's wait,
/// a failed install retried after 5 s and logged once, a throwing order dropped, SuspendVisit, Release and AbandonWorldState.</summary>
public class TownLimbShopLogicTests
{
    const long Town = 7, OtherTown = 8;
    static readonly ArenaBuildingRef RegistryRef = new(ArenaBuildingSource.Town, "registry", PgRecords.TownRegistry);
    static readonly Vector3 RegistryPos = new(0, 0, 0);

    sealed class Npc
    {
        public string Record = PgRecords.ScratchCivilian;
        public string Name = "";
        public long Town = TownLimbShopLogicTests.Town;
        public Vector3 Pos;
        public bool Player, Dead, Ko, Combat, Carried, Carrying, Gone, Managed;
        public uint Stumps;
        public uint Replaced;
        public string Identity = "";
        public int Matches;
        public bool Hold = true, Passive = false;
        public byte Speed = 1;
        public int Fits, Refreshes;
        public Vector3? Faced;
    }

    sealed class FakeHost : ITownLimbShopHost<int>
    {
        public readonly Dictionary<int, Npc> Npcs = [];
        public readonly List<int> Nearby = [];
        public readonly List<string> Calls = [];
        public readonly List<string> Debugs = [];
        public readonly List<string> Errors = [];
        public readonly List<(int Who, string Text)> Said = [];
        public readonly HashSet<int> Reserved = [];
        public int ExternalCapacity = 4;
        public bool RegistryGone, Fit = true, RecordMissing, CreateFails, FitRefused, FitLimbRefuses, RefreshFails;
        public int RefuseSlot = -1, ThrowOnPositionOf = -1;
        public int ThrowOnStumpsOf = -1;
        public uint Now = 1000;
        public int Failures, Created, Destroyed;

        public bool IsPaused => false;
        public uint NowMs => Now;
        public void Debug(string line) => Debugs.Add(line);
        public void Error(string line) => Errors.Add(line);
        public int None => -1;
        public bool IsNull(int c) => c < 0;
        public bool Valid(int c) => Npcs.TryGetValue(c, out var n) && !n.Gone;
        public bool IsPlayer(int c) => Npcs[c].Player;
        public bool IsDead(int c) => Npcs[c].Dead;
        public bool IsUnconscious(int c) => Npcs[c].Ko;
        public bool InCombatMode(int c) => Npcs[c].Combat;
        public bool IsCarried(int c) => Npcs[c].Carried;
        public bool IsCarrying(int c) => Npcs[c].Carrying;
        public string RecordId(int c) => Valid(c) ? Npcs[c].Record : "";
        public string Name(int c) => Npcs[c].Name;
        public string ActorText(int c) => "a" + c;
        public Vector3 Position(int c) => c == ThrowOnPositionOf ? throw new InvalidOperationException("bad position") : Npcs[c].Pos;
        public long CurrentTown(int c) => Npcs[c].Town;
        public IReadOnlyList<int> CharactersInSphere(Vector3 origin, float radius, int max)
        {
            var list = new List<int>();
            foreach (int c in Nearby)
                if (Npcs.TryGetValue(c, out var n) && Vector3.Distance(n.Pos, origin) <= radius && list.Count < max) list.Add(c);
            return list;
        }
        public uint StumpMask(int c)
        {
            if (c == ThrowOnStumpsOf) throw new InvalidOperationException("bad fighter");
            return Npcs[c].Stumps;
        }
        public bool LimbReplaced(int c, int slot) => (Npcs[c].Replaced & (1u << slot)) != 0;
        public bool AftercareManages(int c) => Npcs[c].Managed;
        public string Identity(int c) => Npcs[c].Identity;
        public int TownMatchCount(int c) => Npcs[c].Matches;
        public bool ReserveExternal(int c)
        {
            Calls.Add("reserve " + c);
            if (Reserved.Count >= ExternalCapacity && !Reserved.Contains(c)) return false;
            return Reserved.Add(c);
        }
        public void ReleaseExternal(int c) { Calls.Add("release " + c); Reserved.Remove(c); }
        public PlacedBuilding? Resolve(ArenaBuildingRef registry) => RegistryGone ? null
            : new PlacedBuilding(new ArenaBuilding(ArenaBuildingSource.Town, "registry", PgRecords.TownRegistry, null, true, RegistryPos, 0f), Town);
        public (bool Hold, bool Passive) StandingOrders(int c) => (Npcs[c].Hold, Npcs[c].Passive);
        public void SetStandingOrders(int c, bool hold, bool passive) { Calls.Add($"orders {c} {hold} {passive}"); Npcs[c].Hold = hold; Npcs[c].Passive = passive; }
        public byte SpeedOf(int c) => Npcs[c].Speed;
        public void SetSpeed(int c, byte speed) { Calls.Add($"speed {c} {speed}"); Npcs[c].Speed = speed; }
        public void SetRun(int c) => Npcs[c].Speed = 2;
        public void ClearOrders(int c) => Calls.Add("clear " + c);
        public void EndWalk(int c) => Calls.Add("endwalk " + c);
        public void Face(int c, Vector3 target) => Npcs[c].Faced = target;
        public void WalkTo(int c, Vector3 target) => Calls.Add($"walk {c} {target.X:0.##} {target.Z:0.##}");
        public void Say(int c, string text) => Said.Add((c, text));
        public bool FitAvailable => Fit;
        public bool LimbRecordReady(int slot) => !RecordMissing;
        public object? CreateLimb(int slot, int quality)
        {
            if (CreateFails) return null;
            Created++;
            Calls.Add($"create {slot} {quality}");
            return "limb" + slot;
        }
        public void DestroyItem(object item) { Destroyed++; Calls.Add("destroy " + item); }
        public bool CanFit(int fighter, int slot) => !FitRefused && slot != RefuseSlot && (Npcs[fighter].Stumps & (1u << slot)) != 0;
        public bool FitLimb(int fighter, int slot, object item)
        {
            if (FitLimbRefuses) return false;
            Npcs[fighter].Fits++;
            Npcs[fighter].Stumps &= ~(1u << slot);
            Npcs[fighter].Replaced |= 1u << slot;
            return true;
        }
        public bool RefreshEquipment(int fighter) { Npcs[fighter].Refreshes++; return !RefreshFails; }
        public void OrderFailed(Exception e) => Failures++;

        public int Add(int id, Npc n) { Npcs[id] = n; Nearby.Add(id); return id; }
    }

    const int Trader = 50;

    static (FakeHost h, TownLimbShopLogic<int> l) Setup(bool trader = true, uint stumps = 1u, bool managed = false)
    {
        var h = new FakeHost();
        h.Add(1, new Npc { Record = "1-fighter", Name = "Alpha", Pos = new Vector3(100, 0, 0), Stumps = stumps, Identity = "id-alpha", Matches = 3, Managed = managed });
        h.Add(2, new Npc { Record = "2-fighter", Name = "Beta", Pos = new Vector3(-100, 0, 0) });
        if (trader) h.Add(Trader, new Npc { Record = PgRecords.RoboticsTrader, Name = "Trader", Pos = new Vector3(0, 0, 300) });
        var l = new TownLimbShopLogic<int>(-1);
        l.Begin(h, RegistryRef, [1, 2], 2);
        return (h, l);
    }

    /// <summary>Advances the clock by <paramref name="ms"/> in steps of at most 250 ms, one Tick each.</summary>
    static void Run(FakeHost h, TownLimbShopLogic<int> l, uint ms, Action? each = null)
    {
        while (ms > 0)
        {
            uint step = Math.Min(ms, 250u);
            h.Now += step;
            ms -= step;
            each?.Invoke();
            l.Tick(h);
        }
    }

    static void Arrive(FakeHost h, TownLimbShopLogic<int> l) => h.Npcs[1].Pos = l.Target + new Vector3(1, 0, 0);

    [Fact]
    public void Begin_queues_npc_stumps_only_with_the_seed_grade_and_lines()
    {
        var h = new FakeHost();
        h.Add(1, new Npc { Record = "1-fighter", Name = "Alpha", Stumps = 1u | 4u, Identity = "id-alpha", Matches = 3 });
        h.Add(2, new Npc { Record = "2-fighter", Name = "Beta" });   // no stump
        h.Add(3, new Npc { Record = "3-fighter", Name = "Player", Stumps = 2, Player = true });
        h.Add(4, new Npc { Record = "4-fighter", Name = "Dead", Stumps = 2, Dead = true });
        h.Add(5, new Npc { Record = "169-Proving Grounds.mod", Name = "Senn", Stumps = 8 });   // a named legendary, no identity
        var l = new TownLimbShopLogic<int>(-1);
        l.Begin(h, RegistryRef, [1, 2, 3, 4, 5, -1], 6);
        Assert.Equal(2, l.OrderCount);
        var a = l.OrderAt(0);
        Assert.Equal(1, a.Fighter);
        Assert.Equal(5u, a.LimbMask);
        uint seed = TownLimbShopPolicy.Seed("id-alpha", 3, 5u);
        Assert.Equal(TownLimbShopPolicy.GradeFor(0, false, seed), a.Grade);
        Assert.Equal((seed >> 8) % 3u, a.Variant);
        var b = l.OrderAt(1);
        Assert.Equal("a5", b.Identity);   // the hand's text when there is no identity
        uint seedB = TownLimbShopPolicy.Seed("a5", 0, 8u);
        Assert.Equal(TownLimbShopPolicy.GradeFor(2, true, seedB), b.Grade);
        Assert.Contains("Proving Grounds: limb shop match-end check participants=6 newlyQueued=2 pending=2", h.Debugs);
        Assert.Contains("Proving Grounds: limb shop queue started after match-end limb loss check", h.Debugs);
        Assert.Contains("Proving Grounds: limb shop added 2 fighter visits; 2 pending", h.Debugs);
        Assert.Equal("Fighters queued for Scratch Robotics", l.GetStatus());
        Assert.True(l.IsActive());
        Assert.True(l.IsManaged(h, 1));
        Assert.False(l.IsManaged(h, 2));

        // a second Begin with the same fighter adds nothing and does not restart the queue
        l.Begin(h, RegistryRef, [1], 1);
        Assert.Equal(2, l.OrderCount);
        Assert.Contains("Proving Grounds: limb shop match-end check participants=1 newlyQueued=0 pending=2", h.Debugs);
    }

    [Fact]
    public void Begin_skips_a_throwing_fighter_and_queues_the_rest()
    {
        var h = new FakeHost();
        h.Add(1, new Npc { Record = "1-fighter", Stumps = 1 });
        h.Add(2, new Npc { Record = "2-fighter", Stumps = 2 });
        h.ThrowOnStumpsOf = 1;
        var l = new TownLimbShopLogic<int>(-1);
        l.Begin(h, RegistryRef, [1, 2], 2);
        Assert.Equal(1, l.OrderCount);
        Assert.Equal(2, l.OrderAt(0).Fighter);
        Assert.Equal(1, h.Failures);
        Assert.Equal(1, l.OrderFailures);
        Assert.Contains("Proving Grounds: limb shop match-end check participants=2 newlyQueued=1 pending=1", h.Debugs);
    }

    [Fact]
    public void Begin_does_nothing_without_a_registry_or_the_fit()
    {
        var h = new FakeHost();
        h.Add(1, new Npc { Stumps = 1 });
        var l = new TownLimbShopLogic<int>(-1);
        l.Begin(h, null, [1], 1);
        h.RegistryGone = true;
        l.Begin(h, RegistryRef, [1], 1);
        h.RegistryGone = false;
        h.Fit = false;
        l.Begin(h, RegistryRef, [1], 1);
        Assert.Equal(0, l.OrderCount);
        Assert.Empty(h.Debugs);
        Assert.False(l.Tick(h));
    }

    [Fact]
    public void A_fighter_in_aftercare_waits_for_the_5_s_poll()
    {
        var (h, l) = Setup(managed: true);
        Run(h, l, 250);
        Assert.Equal(TownLimbShopPhase.Acquire, l.Phase);
        Assert.Equal("Robotics visits pending until queued fighters finish medical care and can visit", l.GetStatus());
        h.Npcs[1].Managed = false;
        Run(h, l, 4500);   // the next readiness check is 5 s after the first
        Assert.Equal(TownLimbShopPhase.Acquire, l.Phase);
        Run(h, l, 500);
        Assert.Equal(TownLimbShopPhase.Approach, l.Phase);
    }

    [Fact]
    public void A_visit_reserves_both_faces_the_trader_and_runs_the_fighter_to_12_units()
    {
        var (h, l) = Setup();
        h.Add(51, new Npc { Record = PgRecords.RoboticsTrader, Pos = new Vector3(0, 0, 900) });                     // farther
        h.Add(52, new Npc { Record = PgRecords.RoboticsTrader, Pos = new Vector3(0, 0, 10), Town = OtherTown });   // another town
        h.Add(53, new Npc { Record = PgRecords.RoboticsTrader, Pos = new Vector3(0, 0, 20), Combat = true });      // busy
        h.Add(54, new Npc { Record = PgRecords.RoboticsTrader, Pos = new Vector3(0, 0, 2600) });                   // out of the radius
        Run(h, l, 250);
        Assert.Equal(TownLimbShopPhase.Approach, l.Phase);
        Assert.Equal(Trader, l.TraderActor);
        Assert.Equal(1, l.FighterActor);
        Assert.True(l.Visible);
        Assert.Equal(new HashSet<int> { 1, Trader }, h.Reserved);
        Assert.Equal(new Vector3(100, 0, 0), h.Npcs[Trader].Faced);
        Assert.Contains($"endwalk {Trader}", h.Calls);
        Assert.Contains($"orders {Trader} True True", h.Calls);
        Assert.Contains("orders 1 False False", h.Calls);
        Assert.Equal(2, h.Npcs[1].Speed);   // RUN
        Vector3 dir = Vector3.Normalize(new Vector3(100, 0, -300));
        Assert.True(Vector3.Distance(new Vector3(0, 0, 300) + dir * 12f, l.Target) < 1e-3f);
        Assert.Equal("Alpha is heading to Scratch Robotics", l.GetStatus());
        Assert.True(l.IsVisitActive());
    }

    [Fact]
    public void The_walk_is_reordered_every_half_second()
    {
        var (h, l) = Setup();
        Run(h, l, 250);
        int walks() => h.Calls.Count(c => c.StartsWith("walk 1 ", StringComparison.Ordinal));
        Assert.Equal(1, walks());
        float x = 100;
        Run(h, l, 2000, () => { x -= 5; h.Npcs[1].Pos = new Vector3(x, 0, 0); });   // progress: no stall
        Assert.InRange(walks(), 4, 6);
        Assert.Equal(TownLimbShopPhase.Approach, l.Phase);
    }

    [Fact]
    public void A_full_visible_visit_speaks_two_lines_and_fits_at_the_grade_quality()
    {
        var (h, l) = Setup(stumps: 1u | 8u);
        Run(h, l, 250);
        var order = l.OrderAt(l.Current);
        int kind = (int)TownLimbShopPolicy.KindFor(order.LimbMask);
        Arrive(h, l);
        Run(h, l, 250);
        Assert.Equal(TownLimbShopPhase.TraderSpeech, l.Phase);
        Assert.Equal((Trader, TownLimbShopPolicy.TraderLine(order.Grade, order.Variant, kind)), h.Said.Single());
        Assert.Equal(h.Npcs[Trader].Pos, h.Npcs[1].Faced);
        Assert.Equal("Scratch Robotics is quoting a replacement order", l.GetStatus());
        Run(h, l, 2250);
        Assert.Single(h.Said);   // 2.25 s: not yet
        Run(h, l, 250);
        Assert.Equal(2, h.Said.Count);
        Assert.Equal((1, TownLimbShopPolicy.FighterLine(order.Grade, order.Variant, kind)), h.Said[1]);
        Assert.Equal("Alpha is buying replacement limbs", l.GetStatus());
        Run(h, l, 2500);
        Assert.Equal(2, h.Npcs[1].Fits);
        Assert.Equal(1, h.Npcs[1].Refreshes);
        int q = TownLimbShopPolicy.QualityLevel(order.Grade);
        Assert.Contains($"create 0 {q}", h.Calls);
        Assert.Contains($"create 3 {q}", h.Calls);
        Assert.Contains($"Proving Grounds: limb shop purchase fighter=Alpha identity=id-alpha grade={TownLimbShopPolicy.GradeName(order.Grade)} quality={q} limbs=9 count=2 scene=visible installed=1", h.Debugs);
        Assert.Contains("Proving Grounds: limb shop queue stopped; pending=0; awaiting next match-end limb check", h.Debugs);
        Assert.Equal("Robotics visits complete", l.GetStatus());
        Assert.Equal(TownLimbShopPhase.Idle, l.Phase);
        Assert.Empty(h.Reserved);
        Assert.Contains("clear 1", h.Calls);
        Assert.Contains($"clear {Trader}", h.Calls);
        Assert.Contains("orders 1 True False", h.Calls);   // the fighter's own orders back
        Assert.Equal(1, h.Npcs[1].Speed);
        Assert.False(l.IsVisitActive());
        Assert.Equal(1, l.Installed);
        Assert.Empty(h.Errors);
    }

    [Fact]
    public void No_trader_fits_silently()
    {
        var (h, l) = Setup(trader: false);
        Run(h, l, 250);
        Assert.Contains("Proving Grounds: limb shop silent fallback - Scratch Robotics Trader unavailable", h.Debugs);
        Assert.Equal(TownLimbShopPhase.Install, l.Phase);
        Assert.Equal("Scratch mechanic fitting replacement limbs", l.GetStatus());
        Run(h, l, 250);
        Assert.Empty(h.Said);
        Assert.Equal(1, h.Npcs[1].Fits);
        Assert.Contains(h.Debugs, d => d.Contains("scene=silent installed=1", StringComparison.Ordinal));
        Assert.Empty(h.Reserved);
    }

    [Fact]
    public void A_refused_reservation_fits_silently()
    {
        var (h, l) = Setup();
        h.ExternalCapacity = 1;
        h.Reserved.Add(99);   // the slots are full (an announcer pair, a corpse handler)
        Run(h, l, 250);
        Assert.Contains("Proving Grounds: limb shop silent fallback - fighter AI ownership unavailable", h.Debugs);
        Assert.Equal(1, l.ReserveRefused);
        h.ExternalCapacity = 4;
        h.Reserved.Clear();
        var (h2, l2) = Setup();
        h2.ExternalCapacity = 1;
        Run(h2, l2, 250);
        Assert.Contains("Proving Grounds: limb shop silent fallback - trader AI ownership unavailable", h2.Debugs);
        Assert.Empty(h2.Reserved);   // the fighter's reservation given back
    }

    [Fact]
    public void A_stall_of_20_s_fits_silently()
    {
        var (h, l) = Setup();
        Run(h, l, 250);
        Run(h, l, 20500);
        Assert.Contains("Proving Grounds: limb shop silent fallback - shop approach stalled for 20 seconds", h.Debugs);
        Assert.Equal(1, h.Npcs[1].Fits);
        Assert.Empty(h.Said);
    }

    [Fact]
    public void An_interrupted_trader_fits_silently_and_a_busy_fighter_waits()
    {
        var (h, l) = Setup();
        Run(h, l, 250);
        h.Npcs[Trader].Combat = true;
        Run(h, l, 250);
        Assert.Contains("Proving Grounds: limb shop silent fallback - trader interrupted during visit", h.Debugs);

        var (h2, l2) = Setup();
        Run(h2, l2, 250);
        h2.Npcs[1].Combat = true;
        Run(h2, l2, 250);
        Assert.Equal(TownLimbShopPhase.Acquire, l2.Phase);
        Assert.Equal("Robotics visit pending until the fighter is available after medical care", l2.GetStatus());
        Assert.Empty(h2.Reserved);
        h2.Npcs[1].Combat = false;
        Run(h2, l2, 4750);
        Assert.Equal(TownLimbShopPhase.Acquire, l2.Phase);   // the wait restarted the 5 s poll
        Run(h2, l2, 250);
        Assert.Equal(TownLimbShopPhase.Approach, l2.Phase);
    }

    [Fact]
    public void A_failed_install_is_retried_after_5_s_and_logged_once()
    {
        var (h, l) = Setup(trader: false);
        h.RecordMissing = true;
        Run(h, l, 500);
        Assert.Equal(new[]
        {
            "Proving Grounds: limb shop missing replacement data limb=0 id=" + PgRecords.SkeletonArmLeft,
            "Proving Grounds: limb shop installation incomplete; order retained; retry in 5 unpaused seconds",
        }, h.Errors);
        Assert.Equal("Replacement limb installation failed; queued order will retry", l.GetStatus());
        Assert.Equal(1, l.InstallFailures);
        int silentLines = h.Debugs.Count(d => d.Contains("silent fallback", StringComparison.Ordinal));
        Run(h, l, 6000);
        Assert.Equal(2, l.InstallFailures);
        Assert.Equal(2, h.Errors.Count);   // the repeat is counted, not logged
        Assert.Equal(silentLines, h.Debugs.Count(d => d.Contains("silent fallback", StringComparison.Ordinal)));
        h.RecordMissing = false;
        Run(h, l, 6000);
        Assert.Equal(1, h.Npcs[1].Fits);
        Assert.Equal(TownLimbShopPhase.Idle, l.Phase);
    }

    [Fact]
    public void A_failed_creation_rolls_back_and_a_refused_fit_logs_installed_0()
    {
        var (h, l) = Setup(trader: false, stumps: 3u);
        h.CreateFails = true;
        Run(h, l, 500);
        Assert.Contains("Proving Grounds: limb shop item creation failed limb=0 id=" + PgRecords.SkeletonArmLeft, h.Errors);

        // the pre-check refuses both stumps: no item is made for the attempt, installed=0, retried (as the C++ retries an incomplete install)
        var (h2, l2) = Setup(trader: false, stumps: 3u);
        h2.FitRefused = true;
        Run(h2, l2, 500);
        Assert.Equal(0, h2.Created);
        Assert.Equal(2, l2.FitsRefused);
        Assert.Contains(h2.Errors, e => e.Contains("scene=silent installed=0", StringComparison.Ordinal));
        Assert.Equal(0, h2.Npcs[1].Refreshes);
        Run(h2, l2, 6000);
        Assert.Equal(2, l2.InstallFailures);
        Assert.Equal(0, h2.Created);   // each retry stays cheap

        // a fit refused after the pre-check passed: the made limbs let go
        var (h3, l3) = Setup(trader: false, stumps: 3u);
        h3.FitLimbRefuses = true;
        Run(h3, l3, 500);
        Assert.Equal(2, h3.Destroyed);
    }

    [Fact]
    public void A_two_stump_order_with_one_refused_fits_one_refreshes_once_and_retries_only_the_other()
    {
        var (h, l) = Setup(trader: false, stumps: 1u | 2u);
        h.RefuseSlot = 1;
        Run(h, l, 500);
        Assert.Equal(1, h.Npcs[1].Fits);
        Assert.Equal(1, h.Npcs[1].Refreshes);
        Assert.Equal(2u, h.Npcs[1].Stumps);
        Assert.Equal(1, l.InstallFailures);
        Assert.Equal(1, h.Created);
        h.RefuseSlot = -1;
        Run(h, l, 6000);
        Assert.Equal(2, h.Npcs[1].Fits);
        Assert.Equal(2, h.Npcs[1].Refreshes);
        Assert.Equal(1, h.Calls.Count(c => c.StartsWith("create 1 ", StringComparison.Ordinal)));   // the refused stump made only on the retry
        Assert.Equal(1, h.Calls.Count(c => c.StartsWith("create 0 ", StringComparison.Ordinal)));   // the fitted stump is not made again
        Assert.Equal(TownLimbShopPhase.Idle, l.Phase);
        Assert.Contains(h.Debugs, d => d.Contains("limbs=2 count=1 scene=silent installed=1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_refresh_is_counted_and_the_limb_stays_fitted()
    {
        var (h, l) = Setup(trader: false);
        h.RefreshFails = true;
        Run(h, l, 500);
        Assert.Equal(1, l.RefreshFailures);
        Assert.Equal(1, l.Installed);
    }

    [Fact]
    public void A_throw_mid_visit_gives_both_back_and_keeps_neither()
    {
        var (h, l) = Setup();
        Run(h, l, 250);
        Assert.True(l.Keeps(1) && l.Keeps(Trader));
        h.ThrowOnPositionOf = 1;   // the approach reads the fighter's position
        Run(h, l, 250);
        Assert.Equal(1, l.OrderFailures);
        Assert.False(l.Keeps(1));
        Assert.False(l.Keeps(Trader));
        Assert.Empty(h.Reserved);   // every reservation the visit took was given back: none left for the glue's all:false sweep to keep
        Assert.Equal(TownLimbShopPhase.Idle, l.Phase);
    }

    [Fact]
    public void A_throwing_fighter_is_dropped_and_the_next_one_visits()
    {
        var h = new FakeHost();
        h.Add(1, new Npc { Record = "1-fighter", Name = "Alpha", Pos = new Vector3(100, 0, 0), Stumps = 1 });
        h.Add(2, new Npc { Record = "2-fighter", Name = "Beta", Pos = new Vector3(-100, 0, 0), Stumps = 2 });
        h.Add(Trader, new Npc { Record = PgRecords.RoboticsTrader, Pos = new Vector3(0, 0, 300) });
        var l = new TownLimbShopLogic<int>(-1);
        l.Begin(h, RegistryRef, [1, 2], 2);
        h.ThrowOnStumpsOf = 1;
        Run(h, l, 250);
        Assert.Equal(1, h.Failures);
        Assert.Equal(1, l.OrderFailures);
        Assert.Equal(1, l.OrderCount);
        Assert.Equal(2, l.FighterActor);
        Assert.Equal(TownLimbShopPhase.Approach, l.Phase);
    }

    [Fact]
    public void SuspendVisit_gives_both_back_and_waits_for_the_next_poll()
    {
        var (h, l) = Setup();
        Run(h, l, 250);
        Assert.True(l.IsVisitActive());
        Assert.True(l.PhaseActive());
        l.SuspendVisit(h);
        Assert.Empty(h.Reserved);
        Assert.False(l.IsVisitActive());
        Assert.Equal(TownLimbShopPhase.Acquire, l.Phase);
        Assert.Equal("Robotics visits resume after saving", l.GetStatus());
        Assert.Equal(1, l.Suspended);
        Assert.True(l.IsManaged(h, 1));
        Run(h, l, 250);   // lastReadinessCheck 0: the next Tick acquires again
        Assert.Equal(TownLimbShopPhase.Approach, l.Phase);
    }

    [Fact]
    public void Install_phase_is_a_visit_for_the_save_safety()
    {
        var (h, l) = Setup(trader: false);
        h.RecordMissing = true;
        Run(h, l, 250);
        Assert.Equal(TownLimbShopPhase.Install, l.Phase);
        Assert.True(l.IsVisitActive());
    }

    [Fact]
    public void A_dead_fighter_ends_the_visit_and_release_and_abandon_clear_everything()
    {
        var (h, l) = Setup();
        Run(h, l, 250);
        h.Npcs[1].Dead = true;
        Run(h, l, 250);
        Assert.Equal(TownLimbShopPhase.Idle, l.Phase);
        Assert.Empty(h.Reserved);

        var (h2, l2) = Setup();
        Run(h2, l2, 250);
        l2.Release(h2);
        Assert.Empty(h2.Reserved);
        Assert.Equal(0, l2.OrderCount);
        Assert.Equal("Robotics visits idle", l2.GetStatus());

        var (h3, l3) = Setup();
        Run(h3, l3, 250);
        l3.AbandonWorldState();
        Assert.Equal(0, l3.OrderCount);
        Assert.Equal(TownLimbShopPhase.Idle, l3.Phase);
        Assert.False(l3.IsVisitActive());
        Assert.Equal("Robotics visits idle", l3.GetStatus());
        Assert.Equal(-1, l3.FighterActor);
    }

    [Fact]
    public void Keeps_names_only_the_visit_actors()
    {
        var (h, l) = Setup();
        Assert.False(l.Keeps(1));
        Run(h, l, 250);
        Assert.True(l.Keeps(1));
        Assert.True(l.Keeps(Trader));
        Assert.False(l.Keeps(2));
        Assert.False(l.Keeps(-1));
    }
}
