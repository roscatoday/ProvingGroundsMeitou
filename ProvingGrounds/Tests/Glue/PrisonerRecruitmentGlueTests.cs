using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>A fake world for PrisonerRecruitmentGlue: characters are ints (0 = null), 1 / 2 the player squad, 3 a caged prisoner (Dust Bandit, key
/// k3); the store is the real LeaderboardStoreLogic over a ready ArenaWorldState, so every reservation is the real ledger's.</summary>
sealed class RecruitFake : IPrisonerRecruitmentHost<int>
{
    public const int Bandit = 3;
    public readonly StoreFake Store = StoreFake.TwoPlayers();
    public readonly ArenaWorldState World;
    public readonly LeaderboardStoreLogic<int> S;
    public readonly Dictionary<int, float> Skill = new() { [Bandit] = 40f };   // 40 x 2.5 = 100 Marks at the shipped values
    public readonly HashSet<int> NoStats = [], Unconscious = [], Joined = [], Forgotten = [];
    public readonly List<string> Calls = [], Debugs = [], Errors = [];
    public bool Ingress, Spar, Runtime, Player = true, Available = true, RecruitRefused, ReserveFails, BrokenIdentity;
    public Action? DuringRecruit, AfterJoin, OnForget;
    public bool NameThrows;

    public RecruitFake()
    {
        Store.Keys[Bandit] = "k3"; Store.Names[Bandit] = "Dust Bandit";
        Store.Roster.Add(Bandit); Store.RosterList.Add(Bandit);
        World = new ArenaWorldState(_ => { }, _ => null);
        World.Resolve(loadedFromSave: false, tickCount: 1);
        S = new LeaderboardStoreLogic<int>(World, Store);
    }

    public bool IsNull(int c) => c == 0;
    public bool Valid(int c) => Store.Valid(c);
    public bool IsDead(int c) => Store.Dead.Contains(c);
    public bool IsUnconscious(int c) => Unconscious.Contains(c);
    public bool TryOverallSkill(int c, out float skill)
    {
        skill = 0f;
        if (NoStats.Contains(c)) return false;
        skill = Skill.TryGetValue(c, out var s) ? s : 30f;
        return true;
    }
    public bool IsRosterPrisoner(int c) => Store.Roster.Contains(c);
    public bool IngressPending() => Ingress;
    public bool SparActive() => Spar;
    public bool PrisonerRuntimeActivity() => Runtime;
    public bool PlayerReady() => Player;
    public bool RecruitAvailable() => Available;
    public int GetMarks(int c) => S.GetMarks(c);
    public bool ReserveProgression(int c, ArenaLedger.Purchase purchase, RewardTransaction.ReservedAccounting accounting)
    {
        Calls.Add("reserve " + purchase.Cost);
        return !ReserveFails && S.ReserveProgression(c, purchase, accounting);
    }
    public bool IdentityMatches(int c, string identity)
    {
        Calls.Add("matches");
        return !BrokenIdentity && Store.Valid(c) && Store.TryIdentity(c, false, out var t) && t == identity;
    }
    public bool Recruit(int c)
    {
        Calls.Add("recruit " + c);
        DuringRecruit?.Invoke();
        if (RecruitRefused) return false;
        Joined.Add(c);
        Store.Players.Add(c);
        AfterJoin?.Invoke();
        return true;
    }
    public bool JoinedPlayer(int c) => Store.Players.Contains(c);
    public void ForgetRosterPrisoner(int c)
    {
        OnForget?.Invoke();
        Calls.Add("forget " + c);
        Forgotten.Add(c);
        Store.Roster.Remove(c);
        Store.RosterList.Remove(c);
    }
    public string Name(int c) => NameThrows ? throw new InvalidOperationException("name") : Store.Name(c);
    public void Debug(string line) => Debugs.Add(line);
    public void Error(string line) => Errors.Add(line);

    /// <summary>k3's Marks (none: no row) and whether no reservation is open (a snapshot can be captured).</summary>
    public string Ledger() =>
        $"k3={(World.Ledger.Find("k3") is { } p ? p.Marks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")} free={S.CaptureSnapshot(new ArenaPersistence.Snapshot())}";

    public PrisonerRecruitmentGlue<int> Logic() => new(this);
}

/// <summary>PrisonerRecruitment (Core/Glue/PrisonerRecruitmentGlue.cs, task 6.4a): the price (skill edges, the clamps, the multiplier), every
/// refusal in the C++ order, the transaction's order and its rollback (the ledger ends where it began), and the state after a recruit.</summary>
public class PrisonerRecruitmentGlueTests
{
    const int B = RecruitFake.Bandit;

    static RecruitFake WithMarks(int marks)
    {
        var f = new RecruitFake();
        Assert.True(f.S.SetMarks(B, marks));
        return f;
    }

    // ---------- the price ----------

    [Theory]   // PrisonerRecruitmentLogic.cpp:8-27 at the shipped values (2.5 Marks per point, step 5, 25..250)
    [InlineData(0f, 25)]
    [InlineData(9f, 25)]      // 22.5 -> 25
    [InlineData(10f, 25)]     // 25
    [InlineData(10.5f, 30)]   // 26.25 -> 30
    [InlineData(11f, 30)]     // 27.5 -> 30
    [InlineData(40f, 100)]
    [InlineData(99f, 250)]    // 247.5 -> 250
    [InlineData(100f, 250)]
    [InlineData(130f, 250)]   // the stat clamps to 100
    [InlineData(-5f, 25)]
    public void RequiredMarks_from_the_overall_skill(float skill, int price)
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = new RecruitFake();
        f.Skill[B] = skill;
        Assert.Equal(skill, f.Logic().AverageCombatStat(B));   // the raw stat (RequiredMarks clamps it)
        Assert.Equal(price, f.Logic().RequiredMarks(B));
        // and the recruit charges exactly that price, for the bandit (not a default-skill character)
        Assert.True(f.S.SetMarks(B, 500));
        Assert.True(f.Logic().Recruit(B, out string _));
        Assert.Contains("reserve " + price, f.Calls);
        Assert.Contains("recruit 3", f.Calls);
        Assert.Equal(500 - price, f.S.GetMarks(B));
    }

    [Fact]   // AverageCombatStat :54-59: no character, an invalid one, no stats -> 0 -> the 25 floor
    public void No_stats_is_zero_skill()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = new RecruitFake();
        f.NoStats.Add(B);
        Assert.Equal(0f, f.Logic().AverageCombatStat(B));
        Assert.Equal(25, f.Logic().RequiredMarks(B));
        Assert.Equal(0f, f.Logic().AverageCombatStat(0));
        f.Store.Invalid.Add(2);
        Assert.Equal(0f, f.Logic().AverageCombatStat(2));
    }

    [Fact]   // the 250 clamp of a smaller configured maximum, and the 25 floor of a larger minimum
    public void Configured_clamps()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        using var r = PGConfig.UseRecruitmentForTest(new PGConfig.Recruitment { Minimum = 40, Maximum = 200, Step = 5, CombatScale = 2.5 });
        var f = new RecruitFake();
        f.Skill[B] = 100f;
        Assert.Equal(200, f.Logic().RequiredMarks(B));
        f.Skill[B] = 9f;
        Assert.Equal(40, f.Logic().RequiredMarks(B));
    }

    [Fact]   // the Marks multiplier scales the bounded price, the ceiling too (PrisonerRecruitmentLogic.cpp:22-25)
    public void Marks_multiplier()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        using var m = PGConfig.UseMarksMultiplierForTest(2.0);
        var f = new RecruitFake();
        f.Skill[B] = 0f;
        Assert.Equal(50, f.Logic().RequiredMarks(B));
        f.Skill[B] = 100f;
        Assert.Equal(500, f.Logic().RequiredMarks(B));
    }

    // ---------- the refusals, in the C++ order ----------

    static (bool Ok, string Status, RecruitFake F) Try(Action<RecruitFake> set, int marks = 500, int who = B)
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = WithMarks(marks);
        set(f);
        bool ok = f.Logic().Recruit(who, out string status);
        return (ok, status, f);
    }

    [Fact]
    public void Refusals()
    {
        Assert.Equal("Select a living prisoner.", Try(_ => { }, who: 0).Status);
        Assert.Equal("Select a living prisoner.", Try(f => f.Store.Invalid.Add(B)).Status);
        Assert.Equal("Select a living prisoner.", Try(f => f.Store.Dead.Add(B)).Status);
        Assert.Equal("This fighter is not an available arena prisoner.", Try(f => f.Store.Roster.Remove(B)).Status);
        Assert.Equal("This fighter is not an available arena prisoner.", Try(_ => { }, who: 1).Status);
        Assert.Equal("Finish the current arena operation before recruiting.", Try(f => f.Ingress = true).Status);
        Assert.Equal("Finish the current arena operation before recruiting.", Try(f => f.Spar = true).Status);
        Assert.Equal("Finish the current arena operation before recruiting.", Try(f => f.Runtime = true).Status);
        Assert.Equal("The prisoner must be conscious to accept freedom.", Try(f => f.Unconscious.Add(B)).Status);
        Assert.Equal("The player faction is not ready.", Try(f => f.Player = false).Status);
        Assert.Equal("This prisoner has not earned enough Arena Marks.", Try(_ => { }, marks: 99).Status);
        Assert.Equal(PrisonerRecruitmentGlue<int>.UnavailableText, Try(f => f.Available = false).Status);
        Assert.Equal("The recruitment payment could not be reserved.", Try(f => f.ReserveFails = true).Status);
    }

    [Fact]   // the checks run in the C++ order: an unconscious prisoner during a match is refused as busy; with too few Marks while busy, busy
    public void Refusal_order()
    {
        Assert.Equal("Finish the current arena operation before recruiting.", Try(f => { f.Spar = true; f.Unconscious.Add(B); f.Player = false; }, marks: 0).Status);
        Assert.Equal("The prisoner must be conscious to accept freedom.", Try(f => { f.Unconscious.Add(B); f.Player = false; }, marks: 0).Status);
        Assert.Equal("The player faction is not ready.", Try(f => f.Player = false, marks: 0).Status);
        Assert.Equal("This prisoner has not earned enough Arena Marks.", Try(f => f.Available = false, marks: 0).Status);
    }

    [Fact]   // a refusal before the reservation touches nothing: no reserve, no recruit, no forget, the Marks as they were
    public void A_refusal_changes_nothing()
    {
        foreach (Action<RecruitFake> set in new Action<RecruitFake>[] { f => f.Spar = true, f => f.Available = false, f => f.Unconscious.Add(B) })
        {
            var (ok, _, f) = Try(set);
            Assert.False(ok);
            Assert.Empty(f.Calls);
            Assert.Equal("k3=500 free=True", f.Ledger());
        }
    }

    // ---------- the transaction ----------

    [Fact]   // reserve -> the binding checked (Complete) -> the recruit -> Accept; then ForgetRosterPrisoner, the C++ status and debug line
    public void Recruit_pays_from_the_prisoners_own_marks()
    {
        var (ok, status, f) = Try(_ => { }, marks: 130);
        Assert.True(ok);
        Assert.Equal("Dust Bandit earned freedom and joined your squad for 100 Arena Marks.", status);
        Assert.Equal(["reserve 100", "matches", "matches", "recruit 3", "forget 3"], f.Calls);   // Matches at Reserve and at Complete
        Assert.Equal("k3=30 free=True", f.Ledger());
        Assert.Contains(B, f.Joined);
        Assert.DoesNotContain(B, f.Store.Roster);
        Assert.Equal(["Proving Grounds: arena prisoner recruited: Dust Bandit"], f.Debugs);
        Assert.Equal(30, f.S.GetMarks(B));   // its Marks stay its own (the same identity), less the price
        Assert.Equal(0, f.S.GetMarks(1));    // nobody else paid
    }

    [Fact]   // exactly the price is enough (C++ GetMarks < cost refuses only below it)
    public void Exactly_the_price()
    {
        var (ok, _, f) = Try(_ => { }, marks: 100);
        Assert.True(ok);
        Assert.Equal("k3=0 free=True", f.Ledger());
    }

    [Fact]   // CompleteAction: a recruit that fails cancels the reservation: the Marks back, nothing forgotten, the C++ text
    public void A_failed_recruit_is_rolled_back()
    {
        var (ok, status, f) = Try(f => f.RecruitRefused = true, marks: 130);
        Assert.False(ok);
        Assert.Equal("Kenshi could not add this prisoner to the current squad; the payment was cancelled.", status);
        Assert.Equal(["reserve 100", "matches", "matches", "recruit 3"], f.Calls);
        Assert.Equal("k3=130 free=True", f.Ledger());
        Assert.Empty(f.Forgotten);
        Assert.Contains(B, f.Store.Roster);
    }

    [Fact]   // CompleteAction: a binding that no longer matches (another identity) refuses before the recruit, rolled back
    public void A_changed_identity_refuses_before_the_recruit()
    {
        var (ok, status, f) = Try(f => f.BrokenIdentity = true, marks: 130);
        Assert.False(ok);
        Assert.Equal("The recruitment payment could not be reserved.", status);   // Reserve itself checks the binding first (RewardTransaction.h:26-31)
        Assert.Equal(["reserve 100", "matches"], f.Calls);
        Assert.Equal("k3=130 free=True", f.Ledger());
    }

    [Fact]   // the binding broken between the reservation and the completion: CompletionFailed, rolled back, no recruit
    public void Identity_lost_after_the_reservation()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = WithMarks(130);
        var h = new IdentityAfterReserve(f);
        bool ok = new PrisonerRecruitmentGlue<int>(h).Recruit(B, out string status);
        Assert.False(ok);
        Assert.Equal("Kenshi could not add this prisoner to the current squad; the payment was cancelled.", status);
        Assert.DoesNotContain("recruit 3", f.Calls);
        Assert.Equal("k3=130 free=True", f.Ledger());
    }

    [Fact]   // the ledger cancelled under the recruit (Accept fails after a native success): AccountingRollbackFailed, the C++ text, never undone
    public void Accept_failing_after_the_recruit()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = WithMarks(130);
        f.DuringRecruit = () => f.World.BlockPersistence("unloaded");   // the ledger blocked under the recruit: the reservation is no longer pending
        bool ok = f.Logic().Recruit(B, out string status);
        Assert.False(ok);
        Assert.Equal("Recruitment failed and its payment could not be restored. Check Arena Marks before saving.", status);
        Assert.Contains(B, f.Joined);        // the recruit is not undone
        Assert.Empty(f.Forgotten);           // as the C++: it returns before ForgetRosterPrisoner (kept: divergences.md "Kept bugs")
        Assert.Contains(B, f.Store.Roster);
        Assert.Equal("k3=none free=False", f.Ledger());   // the ledger blocked (unloaded): no row, no snapshot; nothing pending
        Assert.Single(f.Errors);
    }

    [Fact]   // port: an exception in the recruit's reads is a refusal with the C++ "could not be restored" text and an error line
    public void An_exception_never_escapes()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = WithMarks(130);
        var h = new Throwing(f);
        bool ok = new PrisonerRecruitmentGlue<int>(h).Recruit(B, out string status);
        Assert.False(ok);
        Assert.Equal(PrisonerRecruitmentGlue<int>.ExceptionText, status);
        Assert.Single(f.Errors);
    }

    [Fact]   // port: an exception inside the transaction before the recruit is recovered: the reservation cancelled
    public void An_exception_before_the_recruit_is_rolled_back()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = WithMarks(130);
        f.DuringRecruit = () => throw new InvalidOperationException("boom");
        bool ok = f.Logic().Recruit(B, out string status);
        Assert.False(ok);
        Assert.Equal("Kenshi could not add this prisoner to the current squad; the payment was cancelled.", status);
        Assert.Equal("k3=130 free=True", f.Ledger());
        Assert.Single(f.Errors);
    }

    [Fact]   // review fix: Accept throws (after the recruit): the payment is accepted in the recovery, the ledger free, the roster forgotten, a success
    public void Accept_throwing_after_the_recruit()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = WithMarks(130);
        var l = f.Logic();
        int accepts = 0;
        l.BeforeAcceptForTest = () => { if (++accepts == 1) throw new InvalidOperationException("accept"); };
        bool ok = l.Recruit(B, out string status);
        Assert.True(ok);
        Assert.Equal("Dust Bandit earned freedom and joined your squad for 100 Arena Marks.", status);
        Assert.Equal("k3=30 free=True", f.Ledger());
        Assert.Contains(B, f.Forgotten);
        Assert.Equal(2, accepts);
        Assert.Single(f.Errors);   // the exception line
    }

    [Fact]   // review fix: Accept keeps throwing: AccountingRollbackFailed, the recruit not undone and forgotten from the roster, the payment never refunded
    public void Accept_always_throwing()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = WithMarks(130);
        var l = f.Logic();
        l.BeforeAcceptForTest = () => throw new InvalidOperationException("accept");
        bool ok = l.Recruit(B, out string status);
        Assert.False(ok);
        Assert.Equal("Recruitment failed and its payment could not be restored. Check Arena Marks before saving.", status);
        Assert.Contains(B, f.Joined);
        Assert.Contains(B, f.Forgotten);
        Assert.Equal("k3=30 free=False", f.Ledger());   // paid and never refunded; the reservation Accept could not take stays (only a throwing Accept)
    }

    [Fact]   // re-review fix: the join's result not known (the glue's recruit throws): the recovery reads JoinedPlayer, accepts, forgets
    public void An_unknown_join_is_read_again()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = WithMarks(130);
        f.AfterJoin = () => throw new InvalidOperationException("the join threw and its result could not be read");
        Assert.True(f.Logic().Recruit(B, out string status));
        Assert.Equal("Dust Bandit earned freedom and joined your squad for 100 Arena Marks.", status);
        Assert.Equal("k3=30 free=True", f.Ledger());
        Assert.Contains(B, f.Forgotten);
    }

    [Fact]   // review fix: the recruit throws after the character joined: read back as recruited, the payment accepted, the ledger free, forgotten
    public void A_throw_after_the_join_is_a_recruit()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = WithMarks(130);
        f.AfterJoin = () => throw new InvalidOperationException("after join");
        bool ok = f.Logic().Recruit(B, out string status);
        Assert.True(ok);
        Assert.Equal("Dust Bandit earned freedom and joined your squad for 100 Arena Marks.", status);
        Assert.Equal("k3=30 free=True", f.Ledger());
        Assert.Contains(B, f.Forgotten);
        Assert.DoesNotContain(B, f.Store.Roster);
    }

    [Fact]   // review fix: a throw after the paid recruit (forget, name, debug) never turns the success into a failure
    public void Steps_after_the_recruit_are_guarded()
    {
        using var _ = PGConfig.UseForTest(PGConfig.State.Shipped());
        var f = WithMarks(130);
        f.OnForget = () => { f.NameThrows = true; throw new InvalidOperationException("forget"); };
        bool ok = f.Logic().Recruit(B, out string status);
        Assert.True(ok);
        Assert.Equal("Dust Bandit earned freedom and joined your squad for 100 Arena Marks.", status);   // the name read before the payment
        Assert.Equal("k3=30 free=True", f.Ledger());
        Assert.Single(f.Errors);
    }

    /// <summary>The binding matches at the reservation, then no longer (the identity changed between Reserve and Complete).</summary>
    sealed class IdentityAfterReserve(RecruitFake f) : Delegating(f)
    {
        int calls;
        public override bool IdentityMatches(int c, string identity) => ++calls == 1 && F.IdentityMatches(c, identity);
    }

    /// <summary>The overall skill read throws (a read outside the transaction).</summary>
    sealed class Throwing(RecruitFake f) : Delegating(f)
    {
        public override bool TryOverallSkill(int c, out float skill) => throw new InvalidOperationException("stats");
    }

    class Delegating(RecruitFake f) : IPrisonerRecruitmentHost<int>
    {
        protected RecruitFake F => f;
        public bool IsNull(int c) => f.IsNull(c);
        public bool Valid(int c) => f.Valid(c);
        public bool IsDead(int c) => f.IsDead(c);
        public bool IsUnconscious(int c) => f.IsUnconscious(c);
        public virtual bool TryOverallSkill(int c, out float skill) => f.TryOverallSkill(c, out skill);
        public bool IsRosterPrisoner(int c) => f.IsRosterPrisoner(c);
        public bool IngressPending() => f.IngressPending();
        public bool SparActive() => f.SparActive();
        public bool PrisonerRuntimeActivity() => f.PrisonerRuntimeActivity();
        public bool PlayerReady() => f.PlayerReady();
        public bool RecruitAvailable() => f.RecruitAvailable();
        public int GetMarks(int c) => f.GetMarks(c);
        public bool ReserveProgression(int c, ArenaLedger.Purchase purchase, RewardTransaction.ReservedAccounting accounting) =>
            f.S.ReserveProgression(c, purchase, accounting);
        public virtual bool IdentityMatches(int c, string identity) => f.IdentityMatches(c, identity);
        public bool Recruit(int c) => f.Recruit(c);
        public bool JoinedPlayer(int c) => f.JoinedPlayer(c);
        public void ForgetRosterPrisoner(int c) => f.ForgetRosterPrisoner(c);
        public string Name(int c) => f.Name(c);
        public void Debug(string line) => f.Debug(line);
        public void Error(string line) => f.Error(line);
    }
}
