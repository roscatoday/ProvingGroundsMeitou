using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.ArenaLedger;
using static ProvingGrounds.Core.RewardTransaction;
using Snapshot = ProvingGrounds.Core.ArenaPersistence.Snapshot;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/ArenaLedger.cs and Core/RewardTransaction.cs against Tests/Golden/ledger.json (golden/gen_ledger.cpp, the real
/// headers compiled with g++): every step of 800 call sequences on two ledgers (result and the ledger after: ready, reserved, both
/// counters, every row, tier and unlock), and every Deliver / CompleteAction / Cancel input with its result and call order.</summary>
public class LedgerGoldenTests
{
    static List<JsonElement> Cases(string kind) =>
        GoldenFile.Load("ledger").EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();

    [Fact]
    public void Case_counts_are_pinned()
    {
        Assert.Equal(800, Cases("seq").Count);
        Assert.Equal(256, Cases("dlv").Count);
        Assert.Equal(32, Cases("cact").Count);
        Assert.Equal(28, Cases("cncl").Count);
        Assert.Equal(9234, Cases("seq").Sum(c => c.GetProperty("steps").GetArrayLength()));
    }

    // ---- sequences ----

    sealed class ScriptBinding : IBinding
    {
        public List<string> Ids = new();
        public bool Matches(string id) => Ids.Contains(id);
    }

    static string S(JsonElement e, string name) => e.GetProperty(name).GetString()!;
    static int N(JsonElement e, string name) => e.GetProperty(name).GetInt32();

    static Snapshot SnapshotOf(JsonElement e)
    {
        var s = new Snapshot { SaveKey = S(e, "key") };
        foreach (JsonElement r in e.GetProperty("rows").EnumerateArray())
        {
            var p = new LeaderboardData.Progression { Id = r[0].GetString()!, Name = r[1].GetString()!, Marks = r[2].GetInt32() };
            foreach (JsonElement t in r[3].EnumerateArray()) p.RewardTiers.Add(new RewardProgression.TierEntry(t[0].GetString()!, t[1].GetInt32()));
            s.Fighters.Progression.Add(p);
        }
        foreach (JsonElement u in e.GetProperty("un").EnumerateArray()) s.Fighters.FactionUnlocks.Add(u.GetString()!);
        return s;
    }

    static Purchase PurchaseOf(JsonElement p) => new Purchase(p[0].GetInt32())
    {
        PreviousTier = p[1].GetInt32(), NextTier = p[2].GetInt32(), Reward = p[3].GetString()!, Unlock = p[4].GetString()!,
        RequireNewUnlock = p[5].GetBoolean(),
    };

    static void SameRows(JsonElement rows, List<LeaderboardData.Progression> actual, string w)
    {
        Assert.True(rows.GetArrayLength() == actual.Count, $"{w}: rows expected {rows.GetArrayLength()}, got {actual.Count}");
        int i = 0;
        foreach (JsonElement r in rows.EnumerateArray())
        {
            LeaderboardData.Progression p = actual[i];
            Assert.True(r[0].GetString() == p.Id && r[1].GetString() == p.Name && r[2].GetInt32() == p.Marks,
                $"{w}: row {i} expected {r}, got [{p.Id},{p.Name},{p.Marks}]");
            Assert.True(r[3].GetArrayLength() == p.RewardTiers.Count, $"{w}: row {i} tier count");
            int t = 0;
            foreach (JsonElement te in r[3].EnumerateArray())
            {
                Assert.True(te[0].GetString() == p.RewardTiers[t].Id && te[1].GetInt32() == p.RewardTiers[t].Tier, $"{w}: row {i} tier {t}");
                ++t;
            }
            ++i;
        }
    }

    static void SameStrings(JsonElement a, List<string> actual, string w) =>
        Assert.True(a.EnumerateArray().Select(x => x.GetString()!).SequenceEqual(actual), $"{w}: expected {a}, got [{string.Join(",", actual)}]");

    static void SameSnapshot(JsonElement e, Snapshot s, string w)
    {
        SameRows(e.GetProperty("rows"), s.Fighters.Progression, w + " rows");
        SameStrings(e.GetProperty("un"), s.Fighters.FactionUnlocks, w + " unlocks");
        Assert.True(S(e, "key") == s.SaveKey, $"{w}: saveKey");
    }

    static void SameState(JsonElement st, Ledger l, string w)
    {
        Assert.True(st[0].GetBoolean() == l.Ready(), $"{w}: ready");
        Assert.True(st[1].GetBoolean() == l.ReservedForTest, $"{w}: reserved");
        Assert.True(st[2].GetUInt64() == l.EpochForTest, $"{w}: epoch expected {st[2]}, got {l.EpochForTest}");
        Assert.True(st[3].GetUInt64() == l.SequenceForTest, $"{w}: sequence expected {st[3]}, got {l.SequenceForTest}");
        SameRows(st[4], l.State().Fighters.Progression, w + " state");
        SameStrings(st[5], l.State().Fighters.FactionUnlocks, w + " state unlocks");
        Assert.True(st[6].GetString() == l.State().SaveKey, $"{w}: state saveKey");
    }

    [Fact]
    public void Sequences_match()
    {
        foreach (JsonElement c in Cases("seq"))
        {
            var ledgers = new[] { new Ledger(), new Ledger() };
            var src = new[] { new Snapshot(), new Snapshot() };
            var tok = new Reservation[4];
            var bind = new[] { new ScriptBinding { Ids = { "a", "b" } }, new ScriptBinding { Ids = { "b" } } };
            var ra = new[] { new ReservedAccounting(bind[0]), new ReservedAccounting(bind[1]) };
            int k = 0;
            foreach (JsonElement st in c.GetProperty("steps").EnumerateArray())
            {
                string op = S(st, "op");
                int li = N(st, "l");
                Ledger l = ledgers[li];
                string w = $"seq {N(c, "i")} step {k} {op} {st}";
                JsonElement r = st.GetProperty("r");
                switch (op)
                {
                    case "act": src[li] = SnapshotOf(st.GetProperty("src")); l.Activate(src[li]); break;
                    case "blk": l.Block(); break;
                    case "cnt": l.SetCountersForTest(st.GetProperty("e").GetUInt64(), st.GetProperty("q").GetUInt64()); break;
                    case "marks": Assert.True(r.GetInt32() == l.Marks(S(st, "id")), w); break;
                    case "tier": Assert.True(r.GetInt32() == l.Tier(S(st, "id"), S(st, "rw")), w); break;
                    case "find":
                    {
                        LeaderboardData.Progression? row = l.Find(S(st, "id"));
                        if (row != null) row.Marks = N(st, "w");
                        Assert.True(r.GetBoolean() == (row != null), w);
                        break;
                    }
                    case "spend": Assert.True(r.GetBoolean() == l.Spend(S(st, "id"), S(st, "nm"), N(st, "c")), w); break;
                    case "refund": Assert.True(r.GetBoolean() == l.Refund(S(st, "id"), S(st, "nm"), N(st, "c")), w); break;
                    case "btier":
                        Assert.True(r.GetBoolean() == l.BeginTier(S(st, "id"), S(st, "nm"), N(st, "c"), S(st, "rw"), N(st, "pv"), N(st, "nx")), w);
                        break;
                    case "rtier": Assert.True(r.GetBoolean() == l.RollbackTier(S(st, "id"), N(st, "c"), S(st, "rw"), N(st, "pv")), w); break;
                    case "bunl": Assert.True(r.GetBoolean() == l.BeginUnlock(S(st, "id"), S(st, "nm"), N(st, "c"), S(st, "un")), w); break;
                    case "runl": Assert.True(r.GetBoolean() == l.RollbackUnlock(S(st, "id"), S(st, "nm"), N(st, "c"), S(st, "un")), w); break;
                    case "res":
                    {
                        int slot = N(st, "slot");
                        bool ok = l.Reserve(S(st, "id"), S(st, "nm"), PurchaseOf(st.GetProperty("p")), ref tok[slot]);
                        Assert.True(r.GetBoolean() == ok, w);
                        JsonElement t = st.GetProperty("tok");
                        int owner = ReferenceEquals(tok[slot].Owner, ledgers[0]) ? 0 : ReferenceEquals(tok[slot].Owner, ledgers[1]) ? 1 : -1;
                        Assert.True(t[0].GetInt32() == owner && t[1].GetUInt64() == tok[slot].Epoch && t[2].GetUInt64() == tok[slot].Sequence,
                            $"{w}: token [{owner},{tok[slot].Epoch},{tok[slot].Sequence}]");
                        break;
                    }
                    case "pend": Assert.True(r.GetBoolean() == l.Pending(tok[N(st, "slot")]), w); break;
                    case "can": Assert.True(r.GetBoolean() == l.Cancel(tok[N(st, "slot")]), w); break;
                    case "acc": Assert.True(r.GetBoolean() == l.Accept(tok[N(st, "slot")]), w); break;
                    case "comp": Assert.True(r.GetBoolean() == l.Complete(), w); break;
                    case "ready": Assert.True(r.GetBoolean() == l.Ready(), w); break;
                    case "cap":
                    {
                        var output = new Snapshot { SaveKey = "pre" };
                        Snapshot before = output;
                        bool ok = l.Capture(output);
                        Assert.True(r.GetBoolean() == ok, w);
                        Assert.True(ReferenceEquals(before, output), w + ": Capture fills the caller's object");
                        SameSnapshot(st.GetProperty("out"), output, w + " out");
                        if (output.Fighters.Progression.Count != 0) output.Fighters.Progression[0].Marks = -77;
                        output.Fighters.FactionUnlocks.Add("scribble");
                        break;
                    }
                    case "mutsrc":
                        if (src[li].Fighters.Progression.Count != 0) src[li].Fighters.Progression[0].Marks = -55;
                        src[li].Fighters.FactionUnlocks.Add("scribble");
                        break;
                    case "edit":
                    {
                        Snapshot s = l.State();
                        int kind = N(st, "kind"); string id = S(st, "id"), rw = S(st, "rw"), un = S(st, "un"); int v = N(st, "v");
                        int at = s.Fighters.Progression.FindIndex(p => p.Id == id);
                        if (kind == 0) { if (at >= 0) s.Fighters.Progression[at].Marks = v; }
                        else if (kind == 1) { if (at >= 0) RewardProgression.RestoreHighestTier(s.Fighters.Progression[at].RewardTiers, rw, N(st, "tv")); }
                        else if (kind == 2) RewardProgression.AddUnlock(s.Fighters.FactionUnlocks, un);
                        else if (kind == 3) RewardProgression.RemoveUnlock(s.Fighters.FactionUnlocks, un);
                        else if (kind == 4) s.Fighters.Progression.Add(new LeaderboardData.Progression { Id = id, Marks = v });
                        else if (at >= 0) s.Fighters.Progression.RemoveAt(at);
                        break;
                    }
                    case "bind": bind[N(st, "ra")].Ids = st.GetProperty("ids").EnumerateArray().Select(x => x.GetString()!).ToList(); break;
                    case "rares":
                        Assert.True(r.GetBoolean() == ra[N(st, "ra")].Reserve(l, S(st, "id"), S(st, "nm"), PurchaseOf(st.GetProperty("p"))), w);
                        break;
                    case "racomp": Assert.True(r.GetBoolean() == ra[N(st, "ra")].Complete(), w); break;
                    case "raroll": Assert.True(r.GetBoolean() == ra[N(st, "ra")].Rollback(), w); break;
                    case "raacc": Assert.True(r.GetBoolean() == ra[N(st, "ra")].Accept(), w); break;
                    default: throw new InvalidOperationException(w + ": unknown op");
                }
                SameState(st.GetProperty("s"), l, w);
                ++k;
            }
        }
    }

    // ---- Deliver / CompleteAction / Cancel ----

    sealed class Log { public string Text = ""; }

    class ScriptAccounting : Accounting
    {
        protected readonly Log log; readonly bool complete, rollback;
        public ScriptAccounting(Log l, bool c, bool r) { log = l; complete = c; rollback = r; }
        public override bool Complete() { log.Text += "C"; return complete; }
        public override bool Rollback() { log.Text += "R"; return rollback; }
    }

    sealed class ScriptAccountingAccept : ScriptAccounting
    {
        readonly bool accept;
        public ScriptAccountingAccept(Log l, bool c, bool a, bool r) : base(l, c, r) { accept = a; }
        public override bool Accept() { log.Text += "A"; return accept; }
    }

    sealed class ScriptInventory : IInventory
    {
        readonly Log log; readonly bool rp, dn, rn, rs;
        public ScriptInventory(Log l, bool a, bool b, bool c, bool d) { log = l; rp = a; dn = b; rn = c; rs = d; }
        public bool RemovePrevious() { log.Text += "p"; return rp; }
        public bool DeliverNew() { log.Text += "d"; return dn; }
        public bool RemoveNew() { log.Text += "n"; return rn; }
        public bool RestorePrevious() { log.Text += "s"; return rs; }
        public void DestroyNew() { log.Text += "x"; }
        public void DestroyPrevious() { log.Text += "y"; }
    }

    sealed class ScriptAction : IAction
    {
        readonly Log log; readonly bool apply;
        public ScriptAction(Log l, bool a) { log = l; apply = a; }
        public bool Apply() { log.Text += "a"; return apply; }
    }

    static bool Bit(int b, int mask) => (b & mask) != 0;

    [Fact]
    public void Deliver_matches()
    {
        foreach (JsonElement c in Cases("dlv"))
        {
            int b = N(c, "b"); bool def = c.GetProperty("def").GetBoolean();
            var log = new Log();
            var inv = new ScriptInventory(log, Bit(b, 1), Bit(b, 2), Bit(b, 4), Bit(b, 8));
            Accounting acc = def ? new ScriptAccounting(log, Bit(b, 16), Bit(b, 64)) : new ScriptAccountingAccept(log, Bit(b, 16), Bit(b, 32), Bit(b, 64));
            Result r = Deliver(inv, acc);
            Assert.True(N(c, "r") == (int)r && S(c, "log") == log.Text, $"dlv def={def} b={b}: got {(int)r} {log.Text}, expected {c}");
        }
    }

    [Fact]
    public void CompleteAction_matches()
    {
        foreach (JsonElement c in Cases("cact"))
        {
            int b = N(c, "b"); bool def = c.GetProperty("def").GetBoolean();
            var log = new Log();
            var act = new ScriptAction(log, Bit(b, 2));
            Accounting acc = def ? new ScriptAccounting(log, Bit(b, 1), Bit(b, 8)) : new ScriptAccountingAccept(log, Bit(b, 1), Bit(b, 4), Bit(b, 8));
            Result r = CompleteAction(act, acc);
            Assert.True(N(c, "r") == (int)r && S(c, "log") == log.Text, $"cact def={def} b={b}: got {(int)r} {log.Text}, expected {c}");
        }
    }

    [Fact]
    public void Cancel_matches()
    {
        foreach (JsonElement c in Cases("cncl"))
        {
            var log = new Log();
            var acc = new ScriptAccounting(log, true, c.GetProperty("rb").GetBoolean());
            Result r = RewardTransaction.Cancel(acc, c.GetProperty("ir").GetBoolean(), (Result)N(c, "f"));
            Assert.True(N(c, "r") == (int)r && S(c, "log") == log.Text, $"cncl {c}: got {(int)r} {log.Text}");
        }
    }
}
