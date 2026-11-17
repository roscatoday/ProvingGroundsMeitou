using ProvingGrounds.Core;
using ProvingGrounds.Core.Glue;
using static ProvingGrounds.Core.ArenaRewards;

namespace ProvingGrounds.Tests.Glue;

/// <summary>A reward item of <see cref="RewardFake"/>: a record at a level, held by a character or loose.</summary>
sealed class FakeItem(string record, int level)
{
    public string Record = record;
    public int Level = level;
    public int Charges;
    public bool Destroyed;
    public bool Blueprint;
    public override string ToString() => $"{Record}@{Level}{(Destroyed ? " destroyed" : "")}";
}

/// <summary>A fake world for RewardDeliveryLogic: characters are ints (0 = null), each with a list of held items; the store is the real
/// LeaderboardStoreLogic over a ready ArenaWorldState (its own StoreFake for identities), so every reservation is the real ledger's.</summary>
sealed class RewardFake : IRewardDeliveryHost<int, FakeItem>
{
    public readonly StoreFake Store = StoreFake.TwoPlayers();
    public readonly ArenaWorldState World;
    public readonly LeaderboardStoreLogic<int> S;
    public readonly Dictionary<int, List<FakeItem>> Held = new() { [1] = [], [2] = [] };
    public readonly HashSet<string> Records = [];
    public readonly HashSet<int> NoInventory = [];
    public readonly List<string> Diags = [], Calls = [], Errors = [];
    public readonly List<FakeItem> Made = [];
    public bool CanStart = true, Room = true, CreateFails, BrokenIdentity, ReserveFails, IdentityThrows, CopiesThrow;
    public Func<FakeItem, bool>? TakeOutFails, GiveFails, GiveThrows;
    public Action<FakeItem>? AfterGive;
    public int CanStartCalls;

    public RewardFake()
    {
        World = new ArenaWorldState(_ => { }, _ => null);
        World.Resolve(loadedFromSave: false, tickCount: 1);
        S = new LeaderboardStoreLogic<int>(World, Store);
        foreach (var r in new[] { PgRecords.RetainerLeatherHelmet, PgRecords.WarlordCloak, PgRecords.WarlordCloakNeck, PgRecords.WarlordSabre,
                     PgRecords.PitSurgeonRoll, PgRecords.BlueprintArenaBuildingsI, PgRecords.ResearchProvingGrounds })
            Records.Add(r);
        foreach (Piece p in Enum.GetValues<Piece>())
            if (p != Piece.Count) Records.Add(RewardGrades.ItemRecord(p));
    }

    public bool IsNull(int c) => c == 0;
    public bool Valid(int c) => Store.Valid(c);
    public bool IsPlayerSquadFighter(int c) => Store.Squad.Contains(c);
    public bool ItemsCanStart() { ++CanStartCalls; return CanStart; }
    public bool HasInventory(int c) => !NoInventory.Contains(c);
    public string Name(int c) => Store.Name(c);
    public int GetMarks(int c) => S.GetMarks(c);
    public int GetRewardTier(int c, string rewardId) => S.GetRewardTier(c, rewardId);
    public bool HasFactionUnlock(string unlockId) => S.HasFactionUnlock(unlockId);
    public bool ReserveProgression(int c, ArenaLedger.Purchase purchase, RewardTransaction.ReservedAccounting accounting)
    {
        Calls.Add("reserve");
        return !ReserveFails && S.ReserveProgression(c, purchase, accounting);
    }
    public bool IdentityMatches(int c, string identity) => IdentityThrows ? throw new InvalidOperationException("identity") : !BrokenIdentity && Store.Valid(c) && Store.TryIdentity(c, false, out var t) && t == identity;
    public bool RecordExists(string recordId) => Records.Contains(recordId);
    public bool BlueprintRecordsExist(string researchId) => Records.Contains(researchId);
    public IReadOnlyList<FakeItem> Copies(int c, string recordId) => CopiesThrow ? throw new InvalidOperationException("copies") : Held.TryGetValue(c, out var l) ? l.Where(i => i.Record == recordId).ToList() : [];
    public int Level(FakeItem item) => item.Level;
    public bool HasRoomForRecord(int c, string recordId) { Calls.Add("room " + recordId); return Room; }
    public bool HasRoomForBlueprint(int c, string researchId) { Calls.Add("room blueprint " + researchId); return Room; }
    public FakeItem? CreateRewardItem(string recordId, int qualityLevel, string? model, string? maker)
    {
        Calls.Add($"create {recordId} {qualityLevel}");
        if (CreateFails) return null;
        var i = new FakeItem(recordId, qualityLevel < 0 ? 0 : qualityLevel);
        Made.Add(i);
        return i;
    }
    public FakeItem? CreateBlueprintReward(string researchId)
    {
        Calls.Add("create blueprint " + researchId);
        if (CreateFails) return null;
        var i = new FakeItem(researchId, 0) { Blueprint = true };
        Made.Add(i);
        return i;
    }
    public void SetCharges(FakeItem item, int charges) => item.Charges = charges;
    public FakeItem? TakeOut(int c, FakeItem held)
    {
        Calls.Add("takeout " + held);
        if (TakeOutFails?.Invoke(held) == true || !Held[c].Remove(held)) return null;
        return held;
    }
    public bool Give(int c, FakeItem item, bool destroyOnFail)
    {
        Calls.Add($"give {item} {destroyOnFail}");
        if (GiveThrows?.Invoke(item) == true) throw new InvalidOperationException("give");
        if (item.Destroyed || GiveFails?.Invoke(item) == true)
        {
            if (destroyOnFail) item.Destroyed = true;
            return false;
        }
        Held[c].Add(item);
        AfterGive?.Invoke(item);
        return true;
    }
    public void Destroy(int c, FakeItem item)
    {
        Calls.Add("destroy " + item);
        Held[c].Remove(item);
        item.Destroyed = true;
    }
    public void Diag(string line) => Diags.Add(line);
    public void Error(string line) => Errors.Add(line);

    /// <summary>The ledger as text: marks, reward tiers and the faction unlocks of k1 / k2, and whether a reservation is open.</summary>
    public string Ledger()
    {
        string Row(string id) => World.Ledger.Find(id) is { } p ? $"{p.Marks}:{RewardProgression.EncodeTiers(p.RewardTiers)}" : "none";
        return $"k1={Row("k1")} k2={Row("k2")} unlocks={string.Join(',', World.Ledger.State().Fighters.FactionUnlocks)} free={S.CaptureSnapshot(new ArenaPersistence.Snapshot())}";
    }
}

/// <summary>RewardDelivery (Core/Glue/RewardDeliveryLogic.cs, task 5.2b): offers, claims, upgrades, replacements, licences and general items,
/// every refusal and every failure branch of the two-phase transaction (the ledger ends exactly where it began).</summary>
public class RewardDeliveryLogicTests
{
    static (RewardFake F, RewardDeliveryLogic<int, FakeItem> L) New(int marks = 500)
    {
        var f = new RewardFake();
        f.S.SetMarks(1, marks);
        return (f, new RewardDeliveryLogic<int, FakeItem>(f));
    }

    static void Unlock(RewardFake f, params string[] ids) { foreach (var id in ids) Assert.True(f.S.AddFactionUnlock(id)); }

    const string Helmet = PgRecords.RetainerLeatherHelmet;

    // ---------- ResolveRewardItemData / ResolveOffer ----------

    [Fact]
    public void ResolveRewardItemData_TheCloakIsAlways89_NullWhenTheRecordIsMissing()
    {
        var (f, l) = New();
        Assert.Equal(PgRecords.WarlordCloak, l.ResolveRewardItemData(Piece.Cloak));
        Assert.Equal(Helmet, l.ResolveRewardItemData(Piece.RetainerHelmet));
        f.Records.Remove(Helmet);
        Assert.Null(l.ResolveRewardItemData(Piece.RetainerHelmet));
    }

    [Fact]
    public void ResolveOffer_InvalidCharacterIsComplete()
    {
        var (f, l) = New();
        f.Store.Invalid.Add(1);
        var o = l.ResolveOffer(1, Piece.RetainerHelmet);
        Assert.Equal((OfferKind.Complete, Tier.MaxCount, 0), (o.Kind, o.Tier, o.Cost));
        o = l.ResolveOffer(0, Piece.RetainerHelmet);
        Assert.Equal(OfferKind.Complete, o.Kind);
    }

    [Fact]
    public void ResolveOffer_ClaimThenUpgradeThenReplaceWhenThePreviousGradeIsGone()
    {
        var (f, l) = New();
        var o = l.ResolveOffer(1, Piece.RetainerHelmet);
        Assert.Equal((OfferKind.Claim, Tier.Standard, 20), (o.Kind, o.Tier, o.Cost));
        Assert.True(f.S.BeginRewardTierPurchase(1, 0, "armour.retainer.helmet", -1, 0));   // Standard claimed
        f.Held[1].Add(new FakeItem(Helmet, 40));
        o = l.ResolveOffer(1, Piece.RetainerHelmet);
        Assert.Equal((OfferKind.Claim, Tier.HighGrade, 40), (o.Kind, o.Tier, o.Cost));
        f.Held[1].Clear();
        o = l.ResolveOffer(1, Piece.RetainerHelmet);
        Assert.Equal((OfferKind.Replace, Tier.Standard, 10), (o.Kind, o.Tier, o.Cost));
        f.Held[1].Add(new FakeItem(Helmet, 60));   // a copy of another grade: still not the previous one
        o = l.ResolveOffer(1, Piece.RetainerHelmet);
        Assert.Equal(OfferKind.Replace, o.Kind);
    }

    [Fact]
    public void ResolveOffer_TheCloakAcceptsTheNeckRecordAsThePreviousGrade()
    {
        var (f, l) = New();
        Assert.True(f.S.BeginRewardTierPurchase(1, 0, "armour.warlord.cloak", -1, 0));
        f.Held[1].Add(new FakeItem(PgRecords.WarlordCloakNeck, 40));
        var o = l.ResolveOffer(1, Piece.Cloak);
        Assert.Equal((OfferKind.Claim, Tier.HighGrade), (o.Kind, o.Tier));
    }

    [Fact]
    public void ResolveOffer_NoInventoryOrNoRecordMeansNoCopy()
    {
        var (f, l) = New();
        Assert.True(f.S.BeginRewardTierPurchase(1, 0, "armour.retainer.helmet", -1, 0));
        f.Held[1].Add(new FakeItem(Helmet, 40));
        f.NoInventory.Add(1);
        Assert.Equal(OfferKind.Replace, l.ResolveOffer(1, Piece.RetainerHelmet).Kind);
        f.NoInventory.Clear();
        f.Records.Remove(Helmet);
        Assert.Equal(OfferKind.Replace, l.ResolveOffer(1, Piece.RetainerHelmet).Kind);
    }

    // ---------- ClaimNext: the refusals, in the C++ order ----------

    [Fact]
    public void ClaimNext_Refusals()
    {
        var (f, l) = New();
        string s;
        Assert.False(l.ClaimNext(0, Piece.RetainerHelmet, out s)); Assert.Equal("Select a valid squad fighter.", s);
        f.Store.Invalid.Add(2);
        Assert.False(l.ClaimNext(2, Piece.RetainerHelmet, out s)); Assert.Equal("Select a valid squad fighter.", s);
        f.Store.Invalid.Clear();
        f.Store.Squad.Remove(2);   // a prisoner (M6): not in the player squad
        Assert.False(l.ClaimNext(2, Piece.RetainerHelmet, out s)); Assert.Equal("Prisoners retain Marks but cannot claim rewards.", s);
        f.CanStart = false;
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out s)); Assert.Equal("The world item factory is not ready.", s);
        f.CanStart = true;
        f.NoInventory.Add(1);
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out s)); Assert.Equal("The selected fighter has no accessible inventory.", s);
        f.NoInventory.Clear();
        Assert.False(l.ClaimNext(1, Piece.Sabre, out s)); Assert.Equal("Unlock the weapons licence before purchasing weapons.", s);
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out s)); Assert.Equal("Unlock this armour licence before purchasing pieces.", s);
        Unlock(f, "armour.retainer");
        f.S.SetMarks(1, 19);
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out s)); Assert.Equal("This fighter does not have enough Arena Marks.", s);
        f.S.SetMarks(1, 500);
        f.Records.Remove(Helmet);
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out s)); Assert.Equal("The Warlord reward record could not be found.", s);
        Assert.Contains("Proving Grounds: missing reward FCS record " + Helmet, f.Diags);
        f.Records.Add(Helmet);
        f.Room = false;
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out s)); Assert.Equal("This fighter needs more inventory space for the reward.", s);
        Assert.Empty(f.Made);   // the room check comes before the item is made (:851)
        f.Room = true;
        f.CreateFails = true;
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out s)); Assert.Equal("The reward item could not be created.", s);
        Assert.Contains("Proving Grounds: createItem failed for " + Helmet + " level 40", f.Diags);
        Assert.Equal("k1=500: k2=none unlocks=armour.retainer free=True", f.Ledger());
    }

    [Fact]
    public void ClaimNext_CompleteWhenEveryGradeIsClaimedAndHeld()
    {
        var (f, l) = New();
        Unlock(f, "armour.retainer");
        Assert.True(f.S.BeginRewardTierPurchase(1, 0, "armour.retainer.helmet", -1, 3));
        f.Held[1].Add(new FakeItem(Helmet, 95));
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("All grades of this reward are already claimed.", s);
    }

    [Fact]
    public void ClaimNext_AsksItemsCanStartOnce()
    {
        var (f, l) = New();
        Unlock(f, "armour.retainer");
        Assert.True(l.ClaimNext(1, Piece.RetainerHelmet, out _));
        Assert.Equal(1, f.CanStartCalls);
    }

    // ---------- ClaimNext: success ----------

    [Fact]
    public void ClaimNext_FirstClaim_DeliversAndPays()
    {
        var (f, l) = New();
        Unlock(f, "armour.retainer");
        Assert.True(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("Retainer Leather Helmet - Standard claimed for Ann.", s);
        var held = Assert.Single(f.Held[1]);
        Assert.Equal((Helmet, 40, false), (held.Record, held.Level, held.Destroyed));
        Assert.Equal(480, f.S.GetMarks(1));
        Assert.Equal(0, f.S.GetRewardTier(1, "armour.retainer.helmet"));
        Assert.Equal(["room " + Helmet, "create " + Helmet + " 40", "reserve", $"give {Helmet}@40 True"], f.Calls);
        Assert.Contains("Proving Grounds: claim prev=-1 -> created=40 (want 40) for " + Helmet + " cost=20", f.Diags);
        Assert.Contains("Proving Grounds: reward delivered: " + Helmet + " / Standard", f.Diags);
        Assert.EndsWith("free=True", f.Ledger());
    }

    [Fact]
    public void ClaimNext_Upgrade_TakesTheExactPreviousGradeAndDestroysIt()
    {
        var (f, l) = New();
        Unlock(f, "armour.retainer");
        Assert.True(l.ClaimNext(1, Piece.RetainerHelmet, out _));
        var standard = f.Held[1][0];
        var other = new FakeItem(Helmet, 80);
        f.Held[1].Insert(0, other);   // another grade first in the list: the exact previous one is taken
        f.Calls.Clear();
        Assert.True(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("Retainer Leather Helmet - High Grade claimed for Ann.", s);
        Assert.True(standard.Destroyed);
        Assert.False(other.Destroyed);
        Assert.Equal([80, 60], f.Held[1].Select(i => i.Level));
        Assert.Equal(440, f.S.GetMarks(1));
        Assert.Equal(1, f.S.GetRewardTier(1, "armour.retainer.helmet"));
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("room"));   // an upgrade skips the room check (:834-855)
        Assert.Contains("Proving Grounds: claim prev=40 -> created=60 (want 60) for " + Helmet + " cost=40", f.Diags);
    }

    [Fact]
    public void ClaimNext_Upgrade_RecognisesAnOldLevel()
    {
        var (f, l) = New();
        Unlock(f, "weapon.all");
        Assert.True(f.S.BeginRewardTierPurchase(1, 0, "weapon.sabre", -1, 1));   // High Grade (55) claimed
        var old = new FakeItem(PgRecords.WarlordSabre, 50);   // the earlier uniform 50 (LevelsMatch 50 -> 55)
        f.Held[1].Add(old);
        Assert.Equal(OfferKind.Claim, l.ResolveOffer(1, Piece.Sabre).Kind);
        Assert.True(l.ClaimNext(1, Piece.Sabre, out string s));
        Assert.Equal("Warlord Sabre - Arena Talon claimed for Ann.", s);
        Assert.True(old.Destroyed);
    }

    [Fact]
    public void ClaimNext_Replace_HalfCostNoTierChange()
    {
        var (f, l) = New();
        Unlock(f, "armour.retainer");
        Assert.True(l.ClaimNext(1, Piece.RetainerHelmet, out _));
        Assert.True(l.ClaimNext(1, Piece.RetainerHelmet, out _));   // High Grade
        f.Held[1].Clear();   // lost
        Assert.Equal((OfferKind.Replace, Tier.HighGrade, 20), (l.ResolveOffer(1, Piece.RetainerHelmet).Kind, l.ResolveOffer(1, Piece.RetainerHelmet).Tier, l.ResolveOffer(1, Piece.RetainerHelmet).Cost));
        Assert.True(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("Retainer Leather Helmet - High Grade replaced for Ann.", s);
        Assert.Equal(60, Assert.Single(f.Held[1]).Level);
        Assert.Equal(420, f.S.GetMarks(1));
        Assert.Equal(1, f.S.GetRewardTier(1, "armour.retainer.helmet"));
        Assert.Contains("Proving Grounds: reward replaced: " + Helmet + " / High Grade", f.Diags);
        Assert.Contains("Proving Grounds: replace prev=-1 -> created=60 (want 60) for " + Helmet + " cost=20", f.Diags);
    }

    // ---------- ClaimNext: every failure branch of the transaction; the ledger ends where it began ----------

    /// <summary>An upgrade set up: Standard claimed and held, 480 Marks.</summary>
    static (RewardFake F, RewardDeliveryLogic<int, FakeItem> L, FakeItem Standard, string Before) Upgradable()
    {
        var (f, l) = New();
        Unlock(f, "armour.retainer");
        Assert.True(l.ClaimNext(1, Piece.RetainerHelmet, out _));
        f.Calls.Clear();
        f.Diags.Clear();
        return (f, l, f.Held[1][0], f.Ledger());
    }

    [Fact]
    public void ClaimNext_ReserveFails_NewItemDestroyed()
    {
        var (f, l, standard, before) = Upgradable();
        f.ReserveFails = true;
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("The reward purchase could not be reserved.", s);
        Assert.True(f.Made[^1].Destroyed);
        Assert.False(standard.Destroyed);
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void ClaimNext_ReplaceReserveFails_NewItemDestroyed()
    {
        var (f, l, _, _) = Upgradable();
        f.Held[1].Clear();
        string before = f.Ledger();
        f.ReserveFails = true;
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("The replacement purchase could not be reserved.", s);
        Assert.True(f.Made[^1].Destroyed);
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void ClaimNext_RemovalFails_PreviousKept()
    {
        var (f, l, standard, before) = Upgradable();
        f.TakeOutFails = i => i == standard;
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("The previous grade could not be removed; the purchase was cancelled.", s);
        Assert.Contains(s, f.Diags);
        Assert.Same(standard, Assert.Single(f.Held[1]));
        Assert.False(standard.Destroyed);
        Assert.True(f.Made[^1].Destroyed);
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void ClaimNext_DeliveryFails_PreviousRestored()
    {
        var (f, l, standard, before) = Upgradable();
        f.GiveFails = i => i.Level == 60;
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("The reward would not fit; the purchase was rolled back.", s);
        Assert.Same(standard, Assert.Single(f.Held[1]));
        Assert.False(standard.Destroyed);
        Assert.True(f.Made[^1].Destroyed);
        Assert.Contains($"give {Helmet}@40 False", f.Calls);   // given back without destroyOnFail (:492)
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void ClaimNext_DeliveryFailsAndRestoreFails_InventoryRollbackFailed()
    {
        var (f, l, standard, before) = Upgradable();
        f.GiveFails = _ => true;
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("The purchase failed and inventory restoration failed. Check the fighter's equipment before saving.", s);
        Assert.Contains("Proving Grounds: inventory rollback could not restore retained previous reward item", f.Diags);
        Assert.False(standard.Destroyed);   // kept alive (not destroyed) when it could not be given back
        Assert.DoesNotContain(standard, f.Held[1]);
        Assert.True(f.Made[^1].Destroyed);
        Assert.Equal(before, f.Ledger());   // the accounting was refunded
        Assert.Contains(f.Errors, e => e.Contains(Helmet) && e.Contains("level 60"));
    }

    [Fact]
    public void ClaimNext_CompletionFails_NewRemovedPreviousRestored()
    {
        var (f, l, standard, before) = Upgradable();
        f.AfterGive = i => { if (i.Level == 60) f.BrokenIdentity = true; };   // the binding no longer matches at Complete
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("Arena progression became unavailable; the purchase was cancelled.", s);
        Assert.Same(standard, Assert.Single(f.Held[1]));
        Assert.False(standard.Destroyed);
        Assert.True(f.Made[^1].Destroyed);
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void ClaimNext_CompletionFailsAndTheNewItemCannotBeRemoved_InventoryRollbackFailed()
    {
        var (f, l, standard, before) = Upgradable();
        f.AfterGive = i => { if (i.Level == 60) { f.BrokenIdentity = true; f.TakeOutFails = x => x.Level == 60; } };
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("The purchase failed and inventory restoration failed. Check the fighter's equipment before saving.", s);
        Assert.False(f.Made[^1].Destroyed);   // still held: never destroyed while in the inventory
        Assert.Contains(standard, f.Held[1]);
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void ClaimNext_CompletionFailsAndTheLedgerIsGone_AccountingRollbackFailed()
    {
        var (f, l, standard, _) = Upgradable();
        f.AfterGive = i => { if (i.Level == 60) f.World.BlockPersistence("unloaded"); };
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("The purchase failed and accounting could not be restored. Check Arena Marks and equipment before saving.", s);
        Assert.Same(standard, Assert.Single(f.Held[1]));
        Assert.False(standard.Destroyed);
        Assert.True(f.Made[^1].Destroyed);
        Assert.Contains(f.Errors, e => e.Contains(Helmet) && e.Contains("level 60"));
    }

    [Fact]
    public void ClaimNext_EverythingFails_RollbackFailed()
    {
        var (f, l, standard, _) = Upgradable();
        f.GiveFails = i => { if (i.Level == 60) f.World.BlockPersistence("unloaded"); return true; };
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("The purchase failed and accounting could not be restored. Check Arena Marks and equipment before saving.", s);
        Assert.DoesNotContain(standard, f.Held[1]);   // the give-back failed too: out of the inventory, kept alive
        Assert.False(standard.Destroyed);
        Assert.True(f.Made[^1].Destroyed);
        Assert.Empty(f.Held[1]);
        Assert.Contains(f.Errors, e => e.Contains(Helmet) && e.Contains("level 60"));
    }

    // ---------- licences ----------

    [Fact]
    public void PurchaseLicence_RefusalsAndSuccess()
    {
        var (f, l) = New(120);
        string s;
        Assert.False(l.PurchaseLicence(0, ArmourSet.T1, out s)); Assert.Equal("Select an eligible squad fighter.", s);
        f.Store.Squad.Remove(2);
        Assert.False(l.PurchaseLicence(2, ArmourSet.T1, out s)); Assert.Equal("Select an eligible squad fighter.", s);
        f.CanStart = false;   // the licences make no item: the C++ never checks the factory for them (:921-1017)
        Assert.False(l.PurchaseLicence(1, ArmourSet.T2, out s)); Assert.Equal("The previous armour licence is required.", s);
        Assert.True(l.PurchaseLicence(1, ArmourSet.T1, out s)); Assert.Equal("RETAINER LEATHER licence unlocked for the faction.", s);
        Assert.Equal(0, f.CanStartCalls);
        Assert.Equal(70, f.S.GetMarks(1));
        Assert.True(f.S.HasFactionUnlock("armour.retainer"));
        Assert.False(l.PurchaseLicence(1, ArmourSet.T1, out s)); Assert.Equal("This armour licence is already unlocked.", s);
        Assert.False(l.PurchaseLicence(1, ArmourSet.T2, out s)); Assert.Equal("This fighter does not have enough Arena Marks.", s);
        Assert.Equal(70, f.S.GetMarks(1));
        Assert.EndsWith("free=True", f.Ledger());
    }

    [Fact]
    public void PurchaseLicence_ReserveFails()
    {
        var (f, l) = New(120);
        f.ReserveFails = true;
        string before = f.Ledger();
        Assert.False(l.PurchaseLicence(1, ArmourSet.T1, out string s));
        Assert.Equal("The armour licence purchase could not be reserved.", s);
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void PurchaseLicence_CompleteFails_RolledBack()
    {
        var (f, _) = New(120);
        string before = f.Ledger();
        var l = new RewardDeliveryLogic<int, FakeItem>(new BreakAfterReserve(f));   // the binding breaks after the reservation
        Assert.False(l.PurchaseLicence(1, ArmourSet.T1, out string s));
        Assert.Equal("Arena progression became unavailable; the armour licence purchase was cancelled.", s);
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void PurchaseLicence_CompleteFailsAndTheLedgerIsGone()
    {
        var (f, _) = New(120);
        var lw = new RewardDeliveryLogic<int, FakeItem>(new BreakAfterReserve(f, block: true));
        Assert.False(lw.PurchaseLicence(1, ArmourSet.T1, out string s));
        Assert.Equal("The armour licence purchase failed and accounting could not be restored.", s);
    }

    [Fact]
    public void PurchaseWeaponLicence_RefusalsSuccessAndFailures()
    {
        var (f, l) = New(60);
        string s;
        Assert.False(l.PurchaseWeaponLicence(0, out s)); Assert.Equal("Select an eligible squad fighter.", s);
        f.CanStart = false;   // never asked for a licence
        f.S.SetMarks(1, 49);
        Assert.False(l.PurchaseWeaponLicence(1, out s)); Assert.Equal("This fighter does not have enough Arena Marks.", s);
        f.S.SetMarks(1, 60);
        string before = f.Ledger();
        Assert.False(new RewardDeliveryLogic<int, FakeItem>(new BreakAfterReserve(f)).PurchaseWeaponLicence(1, out s));
        Assert.Equal("Arena progression became unavailable; the weapons licence purchase was cancelled.", s);
        Assert.Equal(before, f.Ledger());
        f.BrokenIdentity = false;
        f.ReserveFails = true;
        Assert.False(l.PurchaseWeaponLicence(1, out s)); Assert.Equal("The weapons licence purchase could not be reserved.", s);
        f.ReserveFails = false;
        Assert.True(l.PurchaseWeaponLicence(1, out s)); Assert.Equal("Weapons licence unlocked for the faction.", s);
        Assert.Equal(0, f.CanStartCalls);
        Assert.Equal(10, f.S.GetMarks(1));
        Assert.False(l.PurchaseWeaponLicence(1, out s)); Assert.Equal("The weapons licence is already unlocked.", s);
        var (g, _) = New(60);
        Assert.False(new RewardDeliveryLogic<int, FakeItem>(new BreakAfterReserve(g, block: true)).PurchaseWeaponLicence(1, out s));
        Assert.Equal("The weapons licence purchase failed and accounting could not be restored.", s);
    }

    /// <summary>A host that breaks the accounting binding (or blocks the ledger) right after the reservation, so Complete fails.</summary>
    sealed class BreakAfterReserve(RewardFake f, bool block = false) : IRewardDeliveryHost<int, FakeItem>
    {
        public bool Throw;
        public bool IsNull(int c) => f.IsNull(c);
        public bool Valid(int c) => f.Valid(c);
        public bool IsPlayerSquadFighter(int c) => f.IsPlayerSquadFighter(c);
        public bool ItemsCanStart() => f.ItemsCanStart();
        public bool HasInventory(int c) => f.HasInventory(c);
        public string Name(int c) => f.Name(c);
        public int GetMarks(int c) => f.GetMarks(c);
        public int GetRewardTier(int c, string rewardId) => f.GetRewardTier(c, rewardId);
        public bool HasFactionUnlock(string unlockId) => f.HasFactionUnlock(unlockId);
        public bool ReserveProgression(int c, ArenaLedger.Purchase purchase, RewardTransaction.ReservedAccounting accounting)
        {
            bool ok = f.ReserveProgression(c, purchase, accounting);
            if (ok) { if (block) f.World.BlockPersistence("unloaded"); else if (Throw) f.IdentityThrows = true; else f.BrokenIdentity = true; }
            return ok;
        }
        public bool IdentityMatches(int c, string identity) => f.IdentityMatches(c, identity);
        public bool RecordExists(string recordId) => f.RecordExists(recordId);
        public bool BlueprintRecordsExist(string researchId) => f.BlueprintRecordsExist(researchId);
        public IReadOnlyList<FakeItem> Copies(int c, string recordId) => f.Copies(c, recordId);
        public int Level(FakeItem item) => f.Level(item);
        public bool HasRoomForRecord(int c, string recordId) => f.HasRoomForRecord(c, recordId);
        public bool HasRoomForBlueprint(int c, string researchId) => f.HasRoomForBlueprint(c, researchId);
        public FakeItem? CreateRewardItem(string recordId, int q, string? model, string? maker) => f.CreateRewardItem(recordId, q, model, maker);
        public FakeItem? CreateBlueprintReward(string researchId) => f.CreateBlueprintReward(researchId);
        public void SetCharges(FakeItem item, int charges) => f.SetCharges(item, charges);
        public FakeItem? TakeOut(int c, FakeItem held) => f.TakeOut(c, held);
        public bool Give(int c, FakeItem item, bool destroyOnFail) => f.Give(c, item, destroyOnFail);
        public void Destroy(int c, FakeItem item) => f.Destroy(c, item);
        public void Diag(string line) => f.Diag(line);
        public void Error(string line) => f.Error(line);
    }

    // ---------- review I1: an exception after the reservation never leaves the ledger reserved ----------

    [Fact]
    public void ClaimNext_GiveThrows_RolledBack()
    {
        var (f, l, standard, before) = Upgradable();
        f.GiveThrows = i => i.Level == 60;
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("The reward would not fit; the purchase was rolled back.", s);
        Assert.Same(standard, Assert.Single(f.Held[1]));
        Assert.False(standard.Destroyed);
        Assert.True(f.Made[^1].Destroyed);
        Assert.Contains(f.Diags, d => d.Contains("threw InvalidOperationException"));
        Assert.Equal(before, f.Ledger());
        Assert.EndsWith("free=True", f.Ledger());
    }

    [Fact]
    public void ClaimNext_IdentityThrowsAtComplete_RolledBack()
    {
        var (f, l, standard, before) = Upgradable();
        f.AfterGive = i => { if (i.Level == 60) f.IdentityThrows = true; };
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        f.IdentityThrows = false;
        Assert.Equal("Arena progression became unavailable; the purchase was cancelled.", s);
        Assert.Same(standard, Assert.Single(f.Held[1]));
        Assert.True(f.Made[^1].Destroyed);
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void ClaimNext_ThrowsAndTheLedgerIsGone_AccountingCouldNotBeRestored()
    {
        var (f, l, standard, _) = Upgradable();
        f.GiveThrows = i => { if (i.Level != 60) return false; f.World.BlockPersistence("unloaded"); return true; };
        Assert.False(l.ClaimNext(1, Piece.RetainerHelmet, out string s));
        Assert.Equal("The purchase failed and accounting could not be restored. Check Arena Marks and equipment before saving.", s);
        Assert.Same(standard, Assert.Single(f.Held[1]));
        Assert.Contains(f.Errors, e => e.Contains(Helmet) && e.Contains("level 60"));
    }

    [Fact]
    public void PurchaseGeneral_GiveThrows_RolledBack()
    {
        var (f, l) = New(100);
        string before = f.Ledger();
        f.GiveThrows = _ => true;
        Assert.False(l.PurchaseGeneral(1, GeneralItem.ArenaBuildingsI, out string s));
        Assert.Equal("The reward would not fit; the purchase was rolled back.", s);
        Assert.True(f.Made[^1].Destroyed);
        Assert.Equal(before, f.Ledger());
        Assert.EndsWith("free=True", f.Ledger());
    }

    [Fact]
    public void Licences_IdentityThrowsAfterReserve_RolledBack()
    {
        var (f, _) = New(120);
        string before = f.Ledger();
        var l = new RewardDeliveryLogic<int, FakeItem>(new BreakAfterReserve(f) { Throw = true });
        Assert.False(l.PurchaseLicence(1, ArmourSet.T1, out string s));
        Assert.Equal("Arena progression became unavailable; the armour licence purchase was cancelled.", s);
        Assert.Equal(before, f.Ledger());
        f.IdentityThrows = false;
        Assert.False(l.PurchaseWeaponLicence(1, out s));
        Assert.Equal("Arena progression became unavailable; the weapons licence purchase was cancelled.", s);
        f.IdentityThrows = false;
        Assert.Equal(before, f.Ledger());
    }

    // ---------- review: a ledger that is not Ready ----------

    [Fact]
    public void LedgerNotReady_MarksRefusalNothingMade()
    {
        var (f, l) = New(100);
        f.World.BlockPersistence("blocked");
        string before = f.Ledger();
        Assert.False(l.PurchaseGeneral(1, GeneralItem.PitSurgeonRoll, out string s));
        Assert.Equal("This fighter does not have enough Arena Marks.", s);
        Assert.False(l.PurchaseLicence(1, ArmourSet.T1, out s));
        Assert.Equal("This fighter does not have enough Arena Marks.", s);
        Assert.Empty(f.Made);
        Assert.Equal(before, f.Ledger());
    }

    // ---------- review I2: the command's entry point (Purchase) ----------

    [Fact]
    public void Purchase_RefusesAClaimWhoseOfferChangedSinceShown()
    {
        var (f, l) = New();
        Unlock(f, "armour.retainer");
        Offer shown = l.ResolveOffer(1, Piece.RetainerHelmet);
        var first = l.Purchase(1, RewardPurchaseKind.Claim, (int)Piece.RetainerHelmet, shown);
        Assert.Equal((true, "Retainer Leather Helmet - Standard claimed for Ann.", false), (first.Ok, first.Status, first.OfferChanged));
        int made = f.Made.Count;
        string before = f.Ledger();
        var second = l.Purchase(1, RewardPurchaseKind.Claim, (int)Piece.RetainerHelmet, shown);   // a second click on the same shown offer
        Assert.Equal((false, "", true), (second.Ok, second.Status, second.OfferChanged));
        Assert.Equal(made, f.Made.Count);
        Assert.Equal(before, f.Ledger());
        Assert.Contains(f.Diags, d => d.StartsWith("Proving Grounds: reward purchase skipped"));
        var third = l.Purchase(1, RewardPurchaseKind.Claim, (int)Piece.RetainerHelmet, l.ResolveOffer(1, Piece.RetainerHelmet));
        Assert.True(third.Ok);
        Assert.True(l.Purchase(1, RewardPurchaseKind.Claim, (int)Piece.RetainerHelmet, null).Ok);   // no offer shown: no check (the test verb)
    }

    [Fact]
    public void Purchase_AnExceptionIsAFailureWithTheCxxText()
    {
        var (f, l) = New();
        Unlock(f, "armour.retainer");
        string before = f.Ledger();
        f.CopiesThrow = true;   // thrown before any reservation (ResolveOffer's finders)
        var r = l.Purchase(1, RewardPurchaseKind.Claim, (int)Piece.RetainerHelmet, null);
        Assert.Equal((false, "The purchase failed and accounting could not be restored. Check Arena Marks and equipment before saving.", false),
            (r.Ok, r.Status, r.OfferChanged));
        Assert.Contains(f.Errors, e => e.Contains("threw InvalidOperationException") && e.Contains("RetainerHelmet"));
        Assert.Empty(f.Made);
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void Purchase_EachKind()
    {
        var (f, l) = New();
        Assert.Equal("RETAINER LEATHER licence unlocked for the faction.", l.Purchase(1, RewardPurchaseKind.Licence, (int)ArmourSet.T1, null).Status);
        Assert.Equal("Weapons licence unlocked for the faction.", l.Purchase(1, RewardPurchaseKind.WeaponLicence, 0, null).Status);
        Assert.Equal("Pit Surgeon Roll purchased for Ann.", l.Purchase(1, RewardPurchaseKind.General, (int)GeneralItem.PitSurgeonRoll, null).Status);
    }

    [Theory]
    [InlineData(RewardPurchaseKind.Claim, (int)Piece.Count)] [InlineData(RewardPurchaseKind.Claim, -1)] [InlineData(RewardPurchaseKind.Claim, 99)]
    [InlineData(RewardPurchaseKind.Licence, (int)ArmourSet.None)] [InlineData(RewardPurchaseKind.Licence, 7)]
    [InlineData(RewardPurchaseKind.General, (int)GeneralItem.Count)] [InlineData((RewardPurchaseKind)9, 0)]
    public void Purchase_InvalidItemRefused(RewardPurchaseKind kind, int item)
    {
        var (f, l) = New();
        string before = f.Ledger();
        var r = l.Purchase(1, kind, item, null);
        Assert.Equal((false, "The reward item record could not be found.", false), (r.Ok, r.Status, r.OfferChanged));
        Assert.Empty(f.Made);
        Assert.Equal(before, f.Ledger());
    }

    // ---------- general items ----------

    [Fact]
    public void PurchaseGeneral_Blueprint_SuppliesAndTheUnlockItGrants()
    {
        var (f, l) = New(100);
        Assert.False(l.PurchaseGeneral(1, GeneralItem.ArenaBuildingsII, out string s));
        Assert.Equal("Purchase Arena Buildings I first.", s);
        Assert.True(l.PurchaseGeneral(1, GeneralItem.ArenaBuildingsI, out s));
        Assert.Equal("Arena Buildings I purchased for Ann.", s);
        var bp = Assert.Single(f.Held[1]);
        Assert.True(bp.Blueprint);
        Assert.Equal(PgRecords.ResearchProvingGrounds, bp.Record);
        Assert.Contains("room blueprint " + PgRecords.ResearchProvingGrounds, f.Calls);
        Assert.Equal(75, f.S.GetMarks(1));
        Assert.True(f.S.HasFactionUnlock("blueprint.arena1"));
        Assert.True(l.PurchaseGeneral(1, GeneralItem.ArenaBuildingsI, out _));   // repeatable: the unlock is already there
        Assert.Equal(50, f.S.GetMarks(1));
        Assert.True(l.PurchaseGeneral(1, GeneralItem.PitSurgeonRoll, out s));
        Assert.Equal("Pit Surgeon Roll purchased for Ann.", s);
        var roll = f.Held[1][^1];
        Assert.Equal((PgRecords.PitSurgeonRoll, 1200), (roll.Record, roll.Charges));
        Assert.Contains($"create {PgRecords.PitSurgeonRoll} -1", f.Calls);
        Assert.Equal(43, f.S.GetMarks(1));
        Assert.EndsWith("free=True", f.Ledger());
    }

    [Fact]
    public void PurchaseGeneral_Refusals()
    {
        var (f, l) = New(100);
        string s;
        Assert.False(l.PurchaseGeneral(0, GeneralItem.PitSurgeonRoll, out s)); Assert.Equal("Select an eligible squad fighter.", s);
        f.CanStart = false;
        Assert.False(l.PurchaseGeneral(1, GeneralItem.PitSurgeonRoll, out s)); Assert.Equal("The world item factory is not ready.", s);
        f.CanStart = true;
        f.S.SetMarks(1, 6);
        Assert.False(l.PurchaseGeneral(1, GeneralItem.PitSurgeonRoll, out s)); Assert.Equal("This fighter does not have enough Arena Marks.", s);
        f.S.SetMarks(1, 100);
        f.NoInventory.Add(1);
        Assert.False(l.PurchaseGeneral(1, GeneralItem.PitSurgeonRoll, out s)); Assert.Equal("The selected fighter has no accessible inventory.", s);
        f.NoInventory.Clear();
        f.Records.Remove(PgRecords.PitSurgeonRoll);
        Assert.False(l.PurchaseGeneral(1, GeneralItem.PitSurgeonRoll, out s)); Assert.Equal("The reward item record could not be found.", s);
        f.Records.Remove(PgRecords.ResearchProvingGrounds);
        Assert.False(l.PurchaseGeneral(1, GeneralItem.ArenaBuildingsI, out s)); Assert.Equal("The blueprint research record could not be found.", s);
        f.Records.Add(PgRecords.ResearchProvingGrounds);
        f.Room = false;
        Assert.False(l.PurchaseGeneral(1, GeneralItem.ArenaBuildingsI, out s)); Assert.Equal("This fighter needs more inventory space for the reward.", s);
        Assert.Empty(f.Made);
        f.Room = true;
        f.CreateFails = true;
        Assert.False(l.PurchaseGeneral(1, GeneralItem.ArenaBuildingsI, out s)); Assert.Equal("The reward item could not be created.", s);
        f.CreateFails = false;
        string before = f.Ledger();
        f.ReserveFails = true;
        Assert.False(l.PurchaseGeneral(1, GeneralItem.ArenaBuildingsI, out s)); Assert.Equal("The reward purchase could not be reserved.", s);
        Assert.True(f.Made[^1].Destroyed);
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void PurchaseGeneral_DeliveryFails_LedgerAndUnlockRestored()
    {
        var (f, l) = New(100);
        string before = f.Ledger();
        f.GiveFails = _ => true;
        Assert.False(l.PurchaseGeneral(1, GeneralItem.ArenaBuildingsI, out string s));
        Assert.Equal("The reward would not fit; the purchase was rolled back.", s);
        Assert.True(f.Made[^1].Destroyed);
        Assert.False(f.S.HasFactionUnlock("blueprint.arena1"));   // the granted unlock removed with the refund
        Assert.Equal(before, f.Ledger());
    }

    [Fact]
    public void PurchaseGeneral_CompletionFails_LedgerRestored()
    {
        var (f, l) = New(100);
        string before = f.Ledger();
        f.AfterGive = _ => f.BrokenIdentity = true;
        Assert.False(l.PurchaseGeneral(1, GeneralItem.ArenaBuildingsI, out string s));
        Assert.Equal("Arena progression became unavailable; the purchase was cancelled.", s);
        Assert.Empty(f.Held[1]);
        Assert.Equal(before, f.Ledger());
    }
}

/// <summary>The words of the test-only pg reward verb (Core/Glue/RewardPurchaseArgs.cs, task 5.2b).</summary>
public class RewardPurchaseArgsTests
{
    [Theory]
    [InlineData("claim RetainerHelmet", RewardPurchaseKind.Claim, (int)Piece.RetainerHelmet)]
    [InlineData("CLAIM sabre", RewardPurchaseKind.Claim, (int)Piece.Sabre)]
    [InlineData("licence t1", RewardPurchaseKind.Licence, (int)ArmourSet.T1)]
    [InlineData("weapon-licence", RewardPurchaseKind.WeaponLicence, 0)]
    [InlineData("general PitSurgeonRoll", RewardPurchaseKind.General, (int)GeneralItem.PitSurgeonRoll)]
    public void Parses(string line, RewardPurchaseKind kind, int item)
    {
        Assert.True(RewardPurchaseArgs.TryParse(line.Split(' '), out var k, out int i, out string e));
        Assert.Equal((kind, item, ""), (k, i, e));
    }

    [Theory]
    [InlineData("")] [InlineData("claim")] [InlineData("claim Count")] [InlineData("claim 3")] [InlineData("claim Nope")]
    [InlineData("licence None")] [InlineData("licence 0")] [InlineData("weapon-licence x")] [InlineData("general Count")]
    [InlineData("claim Helmet extra")] [InlineData("buy Helmet")]
    public void Refuses(string line)
    {
        Assert.False(RewardPurchaseArgs.TryParse(line.Length == 0 ? [] : line.Split(' '), out _, out _, out string e));
        Assert.Equal(RewardPurchaseArgs.Usage, e);
    }
}
