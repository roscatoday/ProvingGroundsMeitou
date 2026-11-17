using ProvingGrounds.Core;
using static ProvingGrounds.Core.RewardTransaction;
using Snapshot = ProvingGrounds.Core.ArenaPersistence.Snapshot;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/RewardTransaction.cs (RewardTransaction.h:1-77). Every Deliver / CompleteAction / Cancel input is in the golden
// (Tests/Golden/LedgerGoldenTests.cs); these pin ReservedAccounting's binding rules and the glue shape (a class that is both Binding
// and ReservedAccounting, RewardDelivery.cpp:439-446).
public class RewardTransactionTests
{
    sealed class Ids : IBinding
    {
        public readonly List<string> Allowed = new();
        public bool Matches(string id) => Allowed.Contains(id);
    }

    /// <summary>The glue shape: the C++ passes static_cast&lt;Binding&amp;&gt;(*this) to the base.</summary>
    sealed class SelfBound : SelfBoundReservedAccounting
    {
        public string Live = "a";
        public override bool Matches(string id) => id == Live;
    }

    static ArenaLedger.Ledger Active()
    {
        var s = new Snapshot();
        s.Fighters.Progression.Add(new LeaderboardData.Progression { Id = "a", Marks = 10 });
        s.Fighters.Progression.Add(new LeaderboardData.Progression { Id = "b", Marks = 10 });
        var l = new ArenaLedger.Ledger(); l.Activate(s); return l;
    }

    [Fact]
    public void Result_values_match_the_cpp_enum()
    {
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, new[] { Result.Success, Result.RemovalFailed, Result.DeliveryFailed, Result.CompletionFailed,
            Result.InventoryRollbackFailed, Result.AccountingRollbackFailed, Result.RollbackFailed }.Select(r => (int)r));
    }

    [Fact]
    public void Unreserved_accounting_fails_every_call()
    {
        var acc = new ReservedAccounting(new Ids());
        Assert.False(acc.Complete()); Assert.False(acc.Rollback()); Assert.False(acc.Accept());
    }

    [Fact]
    public void Reserve_checks_the_binding_then_the_ledger()
    {
        var ids = new Ids(); var acc = new ReservedAccounting(ids); ArenaLedger.Ledger l = Active();
        Assert.False(acc.Reserve(l, "a", "", new ArenaLedger.Purchase(1)), "binding does not match");
        Assert.Equal(10, l.Marks("a"));
        ids.Allowed.Add("a");
        Assert.False(acc.Reserve(l, "a", "", new ArenaLedger.Purchase(11)), "ledger refuses");
        Assert.True(acc.Reserve(l, "a", "", new ArenaLedger.Purchase(1)));
        Assert.False(acc.Reserve(l, "a", "", new ArenaLedger.Purchase(1)), "refused while its own reservation is pending");
        Assert.True(acc.Complete());
        ids.Allowed.Clear();
        Assert.False(acc.Complete(), "the live binding moved away");
        Assert.True(acc.Rollback(), "cancellation owns the reserved change even after the binding disappeared");
        Assert.Equal(10, l.Marks("a"));
        ids.Allowed.Add("a");
        Assert.True(acc.Reserve(l, "a", "", new ArenaLedger.Purchase(2)), "a new reservation once the old one is gone");
        Assert.True(acc.Accept()); Assert.False(acc.Accept()); Assert.Equal(8, l.Marks("a"));
    }

    [Fact]
    public void Complete_checks_the_identity_reserved_not_a_later_one()
    {
        var ids = new Ids { Allowed = { "a", "b" } }; var acc = new ReservedAccounting(ids); ArenaLedger.Ledger l = Active();
        Assert.True(acc.Reserve(l, "a", "", new ArenaLedger.Purchase(1)));
        ids.Allowed.Remove("a");
        Assert.False(acc.Complete());
        Assert.True(acc.Rollback());
        Assert.True(acc.Reserve(l, "b", "", new ArenaLedger.Purchase(1)));
        Assert.True(acc.Complete());
    }

    [Fact]
    public void A_glue_class_that_is_its_own_binding()
    {
        var acc = new SelfBound(); ArenaLedger.Ledger l = Active();
        Assert.True(acc.Reserve(l, "a", "", new ArenaLedger.Purchase(3)));
        acc.Live = "b";
        Assert.False(acc.Complete());
        Assert.True(acc.Rollback());
        Assert.Throws<ArgumentNullException>(() => new ReservedAccounting(null!));
    }

    [Fact]
    public void Deliver_with_a_reserved_accounting_refunds_on_failure_and_keeps_it_on_success()
    {
        var ids = new Ids { Allowed = { "a" } }; ArenaLedger.Ledger l = Active();
        var acc = new ReservedAccounting(ids);
        Assert.True(acc.Reserve(l, "a", "", new ArenaLedger.Purchase(4)));
        Assert.Equal(Result.DeliveryFailed, Deliver(new Inv(deliver: false), acc));
        Assert.Equal(10, l.Marks("a"));
        acc = new ReservedAccounting(ids);
        Assert.True(acc.Reserve(l, "a", "", new ArenaLedger.Purchase(4)));
        Assert.Equal(Result.Success, Deliver(new Inv(deliver: true), acc));
        Assert.Equal(6, l.Marks("a")); Assert.False(l.ReservedForTest);
    }

    sealed class Inv : IInventory
    {
        readonly bool deliver;
        public Inv(bool deliver) { this.deliver = deliver; }
        public bool RemovePrevious() => true;
        public bool DeliverNew() => deliver;
        public bool RemoveNew() => true;
        public bool RestorePrevious() => true;
        public void DestroyNew() { }
        public void DestroyPrevious() { }
    }
}
