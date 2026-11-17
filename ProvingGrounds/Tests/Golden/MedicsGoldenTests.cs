using System.Text.Json;
using ProvingGrounds.Core;
using static ProvingGrounds.Core.TownMedicCoordinator;
using AP = ProvingGrounds.Core.TownAftercarePolicy;
using AI = ProvingGrounds.Core.TownMedicAIPolicy;

namespace ProvingGrounds.Tests.Golden;

/// <summary>Core/TownMedicCoordinator.cs, Core/TownAftercarePolicy.cs and Core/TownMedicAIPolicy.cs against Tests/Golden/medics.json
/// (golden/gen_medics.cpp; its header lists the case kinds). The coordinator's whole state (its private members, internal here)
/// is compared after every step of every sequence; ints, enums and bits exactly, floats with GoldenFile.Near. The fake C++
/// pointers 1..21 are distinct objects here, 0 is null.</summary>
public class MedicsGoldenTests
{
    static readonly Lazy<JsonElement> All = new(() => GoldenFile.Load("medics"));

    static List<JsonElement> Cases(string kind)
    {
        var all = All.Value.EnumerateArray().Where(c => c.GetProperty("k").GetString() == kind).ToList();
        if (all.Count == 0) Assert.Fail($"no '{kind}' cases");
        return all;
    }

    static int Int(JsonElement c, string n) => c.GetProperty(n).GetInt32();
    static bool Bool(JsonElement e) => e.GetInt32() != 0;
    static bool Bool(JsonElement c, string n) => Bool(c.GetProperty(n));
    static float F(JsonElement e) => e.GetSingle();
    static float F(JsonElement c, string n) => F(c.GetProperty(n));

    // A regenerated medics.json with fewer (or other) cases must be a deliberate change: update this table with it.
    static readonly Dictionary<string, int> ExpectedCounts = new()
    {
        ["ap"] = 614, ["cp"] = 300, ["dlv"] = 120, ["nat"] = 2, ["oh"] = 28, ["res"] = 200, ["seq"] = 260,
    };

    [Fact]
    public void Case_counts_are_pinned()
    {
        Assert.Equal(1524, All.Value.GetArrayLength());
        var actual = All.Value.EnumerateArray().GroupBy(c => c.GetProperty("k").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(ExpectedCounts.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
        var ops = Cases("seq").SelectMany(c => c.GetProperty("steps").EnumerateArray()).GroupBy(s => s.GetProperty("op").GetString()!)
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int> { ["fail"] = 263, ["hand"] = 91, ["reopen"] = 155, ["reset"] = 63, ["tick"] = 4955 }
            .OrderBy(p => p.Key, StringComparer.Ordinal), ops.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(new[] { "carry_other", "helper_sticky", "hospital_at_12", "mode_tie", "pickup_at_12" },
            Cases("seq").Where(c => c.TryGetProperty("script", out _)).Select(c => c.GetProperty("script").GetString()!).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        var polls = Cases("ap").GroupBy(c => c.GetProperty("f").GetString()!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(245, polls["BedPollDue"]);
        Assert.Equal(243, polls["MedicPollDue"]);
        Assert.Equal(6901, Cases("cp").Sum(c => c.GetProperty("steps").GetArrayLength()));
        Assert.Equal(5281, Cases("res").Sum(c => c.GetProperty("steps").GetArrayLength()));
        Assert.Equal(3335, Cases("dlv").Sum(c => c.GetProperty("steps").GetArrayLength()));
    }

    static Point ReadPoint(JsonElement a, int at) => new(F(a[at]), F(a[at + 1]), F(a[at + 2]));

    static Observation ReadObservation(JsonElement j)
    {
        var o = new Observation();
        JsonElement ms = j.GetProperty("m"), ps = j.GetProperty("p"), bs = j.GetProperty("b");
        o.MedicCount = ms.GetArrayLength(); o.PatientCount = ps.GetArrayLength(); o.BedCount = bs.GetArrayLength();
        for (int i = 0; i < o.MedicCount; ++i)
        {
            JsonElement a = ms[i];
            var m = new MedicObservation();
            m.Ready = Bool(a[0]); m.Bandages = Bool(a[1]); m.Repairs = Bool(a[2]); m.Carrying = a[3].GetInt32(); m.Position = ReadPoint(a, 4);
            o.Medics[i] = m;
        }
        for (int i = 0; i < o.PatientCount; ++i)
        {
            JsonElement a = ps[i];
            var p = new PatientObservation();
            p.Valid = Bool(a[0]); p.Hospital = Bool(a[1]); p.Robot = Bool(a[2]); p.InBed = Bool(a[3]); p.Delivered = Bool(a[4]);
            p.ExternalCarry = Bool(a[5]); p.Combat = Bool(a[6]); p.CanWalk = Bool(a[7]); p.Eligible = Bool(a[8]);
            p.Flesh = F(a[9]); p.Repair = F(a[10]); p.Bleed = F(a[11]); p.Position = ReadPoint(a, 12);
            o.Patients[i] = p;
        }
        for (int i = 0; i < o.BedCount; ++i)
        {
            JsonElement a = bs[i];
            var b = new BedObservation();
            b.Available = Bool(a[0]); b.Robot = Bool(a[1]); b.Occupant = a[2].GetInt32(); b.Position = ReadPoint(a, 3);
            o.Beds[i] = b;
        }
        o.HospitalAvailable = Bool(j, "ha");
        o.Hospital = ReadPoint(j.GetProperty("h"), 0);
        return o;
    }

    static void Ints(JsonElement e, int[] actual, string what)
    {
        Assert.True(e.GetArrayLength() == actual.Length, $"{what}: {e.GetArrayLength()} values in the golden, expected {actual.Length}");
        for (int i = 0; i < actual.Length; ++i)
            Assert.True(e[i].GetInt32() == actual[i], $"{what}[{i}]: expected {e[i].GetInt32()}, actual {actual[i]}");
    }

    static void AssertState(JsonElement s, Coordinator c, string ctx)
    {
        Assert.True(Int(s, "ph") == (int)c.phase, ctx + " phase");
        Assert.True(Bool(s, "mb") == c.missingBed, ctx + " missingBed");
        Assert.True(Int(s, "ew") == c.expiredWaits, ctx + " expiredWaits");
        GoldenFile.Near(F(s, "lt"), c.lastTick, ctx + " lastTick");
        JsonElement cmd = s.GetProperty("cmd");
        for (int m = 0; m < MaxMedics; ++m)
        {
            ref readonly Intent i = ref c.Command(m);
            Assert.True(cmd[m][0].GetInt32() == (int)i.Kind && cmd[m][1].GetInt32() == i.Patient && cmd[m][2].GetInt32() == i.Bed,
                $"{ctx} command {m}: expected {cmd[m].GetRawText()}, actual [{(int)i.Kind},{i.Patient},{i.Bed}]");
        }
        Ints(s.GetProperty("as"), c.assigned, ctx + " assigned");
        Ints(s.GetProperty("rb"), c.reservedBed, ctx + " reservedBed");
        Ints(s.GetProperty("am"), c.activeMode, ctx + " activeMode");
        Ints(s.GetProperty("ht"), c.helperTarget, ctx + " helperTarget");
        Ints(s.GetProperty("hm"), c.helperMode, ctx + " helperMode");
        var failed = new uint[MaxMedics, MaxPatients];
        foreach (JsonElement f in s.GetProperty("fl").EnumerateArray()) failed[f[0].GetInt32(), f[1].GetInt32()] = f[2].GetUInt32();
        for (int m = 0; m < MaxMedics; ++m) for (int p = 0; p < MaxPatients; ++p)
            Assert.True(failed[m, p] == c.failed[m, p], $"{ctx} failed[{m},{p}]: expected {failed[m, p]}, actual {c.failed[m, p]}");
        int wf = Int(s, "wf"), bx = Int(s, "bx");
        JsonElement oc = s.GetProperty("oc"), bw = s.GetProperty("bw");
        for (int p = 0; p < MaxPatients; ++p)
        {
            Assert.True(((wf >> p) & 1) != 0 == c.walkFailed[p], $"{ctx} walkFailed[{p}]");
            Assert.True(((bx >> p) & 1) != 0 == c.bedWaitExpired[p], $"{ctx} bedWaitExpired[{p}]");
            Assert.True(oc[p].GetInt32() == (int)c.outcomes[p], $"{ctx} outcome[{p}]: expected {oc[p].GetInt32()}, actual {(int)c.outcomes[p]}");
            Assert.True(oc[p].GetInt32() == (int)c.PatientOutcome(p), $"{ctx} PatientOutcome({p})");
            GoldenFile.Near(F(bw[p]), c.bedWait[p], $"{ctx} bedWait[{p}]");
            GoldenFile.Near(F(bw[p]), c.BedWait(p), $"{ctx} BedWait({p})");
        }
        for (int m = 0; m < MaxMedics; ++m) Assert.True(s.GetProperty("rb")[m].GetInt32() == c.ReservedBed(m), $"{ctx} ReservedBed({m})");
        // q: GetPhase, IsHelping mask, MedicDone mask, PatientDone mask, HasPatientWork mask, WaitExpired mask, HasUnresolved,
        //    MissingBed, ExpiredWaits, HasPatientWork(-1), ReservedBed(-1), ReservedBed(10), BedWait(-1), BedWait(16),
        //    WaitExpired(-1), WaitExpired(16)
        JsonElement q = s.GetProperty("q");
        int helping = 0, medicDone = 0, done = 0, work = 0, expired = 0;
        for (int m = 0; m < MaxMedics; ++m) { if (c.IsHelping(m)) helping |= 1 << m; if (c.MedicDone(m)) medicDone |= 1 << m; }
        for (int p = 0; p < MaxPatients; ++p)
        {
            if (c.PatientDone(p)) done |= 1 << p;
            if (c.HasPatientWork(p)) work |= 1 << p;
            if (c.WaitExpired(p)) expired |= 1 << p;
        }
        int[] got =
        {
            (int)c.GetPhase(), helping, medicDone, done, work, expired, c.HasUnresolved() ? 1 : 0, c.MissingBed() ? 1 : 0,
            c.ExpiredWaits(), c.HasPatientWork(-1) ? 1 : 0, c.ReservedBed(-1), c.ReservedBed(MaxMedics),
        };
        for (int i = 0; i < got.Length; ++i) Assert.True(q[i].GetInt32() == got[i], $"{ctx} query {i}: expected {q[i].GetInt32()}, actual {got[i]}");
        GoldenFile.Near(F(q[12]), c.BedWait(-1), ctx + " BedWait(-1)");
        GoldenFile.Near(F(q[13]), c.BedWait(MaxPatients), ctx + " BedWait(16)");
        Assert.True(Bool(q[14]) == c.WaitExpired(-1), ctx + " WaitExpired(-1)");
        Assert.True(Bool(q[15]) == c.WaitExpired(MaxPatients), ctx + " WaitExpired(16)");
    }

    [Fact]
    public void Coordinator_sequences_match_step_by_step()
    {
        foreach (JsonElement seq in Cases("seq"))
        {
            var c = new Coordinator();
            int si = 0;
            foreach (JsonElement st in seq.GetProperty("steps").EnumerateArray())
            {
                string op = st.GetProperty("op").GetString()!;
                string ctx = $"seq {Int(seq, "i")}{(seq.TryGetProperty("script", out JsonElement sc) ? " " + sc.GetString() : "")} step {si} ({op})";
                switch (op)
                {
                    case "tick":
                        Observation o = ReadObservation(st.GetProperty("o"));
                        c.Tick(o, Bool(st, "fo"), F(st, "e"), Bool(st, "hs"));
                        break;
                    case "fail": c.Fail(Int(st, "m"), Int(st, "p"), (Kind)Int(st, "kd")); break;
                    case "reopen": c.Reopen(); break;
                    case "hand": c.HandoffPending(Int(st, "c")); break;
                    case "reset": c.Reset(); break;
                    default: Assert.Fail(ctx + ": unknown op"); break;
                }
                AssertState(st.GetProperty("s"), c, ctx);
                ++si;
            }
        }
    }

    [Fact]
    public void Aftercare_policy_functions_match()
    {
        foreach (JsonElement c in Cases("ap"))
        {
            string f = c.GetProperty("f").GetString()!, ctx = c.GetRawText();
            JsonElement a = c.GetProperty("a"), r = c.GetProperty("r");
            switch (f)
            {
                case "NeedsHospital": Assert.True(Bool(r) == AP.NeedsHospital(Bool(a[0]), Bool(a[1]), Bool(a[2])), ctx); break;
                case "StandbyOrderLost": Assert.True(Bool(r) == AP.StandbyOrderLost(Bool(a[0]), F(a[1])), ctx); break;
                case "TransferWaitExpired": Assert.True(Bool(r) == AP.TransferWaitExpired(F(a[0])), ctx); break;
                case "BedStayExpired": Assert.True(Bool(r) == AP.BedStayExpired(F(a[0])), ctx); break;
                case "Budgets":
                    Assert.True(F(r[0]) == AP.TransferWaitBudget() && F(r[1]) == AP.BedStayLimit() && F(r[2]) == AP.BedReleaseRetry(), ctx);
                    break;
                case "BedPollDue": Assert.True(Bool(r) == AP.BedPollDue(a[0].GetUInt32(), a[1].GetUInt32()), ctx); break;
                case "MedicPollDue": Assert.True(Bool(r) == AP.MedicPollDue(a[0].GetUInt32(), a[1].GetUInt32()), ctx); break;
                case "RequiredMedics": Assert.True(r.GetInt32() == AP.RequiredMedics(a[0].GetInt32()), ctx); break;
                case "NeedsRecovery": Assert.True(Bool(r) == AP.NeedsRecovery(Bool(a[0]), F(a[1]), F(a[2])), ctx); break;
                case "ClearExternalReservations": Assert.True(Bool(r) == AP.ClearExternalReservations(Bool(a[0])), ctx); break;
                default: Assert.Fail("unknown function " + ctx); break;
            }
        }
    }

    [Fact]
    public void Command_progress_sequences_match()
    {
        int ci = 0;
        foreach (JsonElement c in Cases("cp"))
        {
            var p = new AP.CommandProgress();
            int si = 0;
            foreach (JsonElement st in c.GetProperty("steps").EnumerateArray())
            {
                string ctx = $"cp {ci} step {si} {st.GetRawText()}";
                bool r = false;
                if (st.GetProperty("op").GetString() == "reset") p.Reset();
                else r = p.Update((AP.Action)Int(st, "a"), F(st, "e"), F(st, "d"), F(st, "aid"));
                Assert.True(Bool(st, "r") == r, ctx + " result");
                JsonElement e = st.GetProperty("c");
                Assert.True(e[0].GetInt32() == (int)p.Action, ctx + " action");
                Assert.True(Bool(e[1]) == p.Issued, ctx + " issued");
                GoldenFile.Near(F(e[2]), p.BestDistance, ctx + " bestDistance");
                GoldenFile.Near(F(e[3]), p.BestAid, ctx + " bestAid");
                GoldenFile.Near(F(e[4]), p.LastProgress, ctx + " lastProgress");
                GoldenFile.Near(F(e[5]), p.LastObserved, ctx + " lastObserved");
                Assert.True(e[6].GetInt32() == p.StalledRetries, ctx + " stalledRetries");
                ++si;
            }
            ++ci;
        }
    }

    // fake C++ pointers: handle 0 = null, 1..21 = distinct objects (the generator draws up to 21)
    static readonly object[] Handles = Enumerable.Range(0, 22).Select(i => new object()).ToArray();
    static object? H(JsonElement e) { uint h = e.GetUInt32(); return h == 0 ? null : Handles[h]; }
    static uint HandleOf(object? o) => o == null ? 0u : (uint)Array.IndexOf(Handles, o);

    [Fact]
    public void Receiver_reservations_match_at_every_capacity()
    {
        int ci = 0;
        var capacities = new SortedSet<int>();
        foreach (JsonElement c in Cases("res"))
        {
            int cap = Int(c, "cap");
            capacities.Add(cap);
            var r = cap switch
            {
                10 => AI.NewReservations(), 16 => AI.NewPatientReservations(), 4 => AI.NewExternalReservations(), _ => new AI.ReceiverReservations(cap),
            };
            Assert.Equal(cap, r.Capacity);
            int si = 0;
            foreach (JsonElement st in c.GetProperty("steps").EnumerateArray())
            {
                string op = st.GetProperty("op").GetString()!, ctx = $"res {ci} (cap {cap}) step {si} ({op})";
                bool? got = null;
                switch (op)
                {
                    case "add": got = r.Add(H(st.GetProperty("h"))); break;
                    case "rem": r.Remove(H(st.GetProperty("h"))); break;
                    case "has": got = r.Contains(H(st.GetProperty("h"))); break;
                    case "clr": r.Clear(); break;
                    default: Assert.Fail(ctx + ": unknown op"); break;
                }
                JsonElement res = st.GetProperty("r");
                if (res.ValueKind == JsonValueKind.Null) Assert.True(got == null, ctx + " result");
                else Assert.True(got == Bool(res), ctx + " result");
                JsonElement v = st.GetProperty("v");
                Assert.True(v.GetArrayLength() == cap, ctx + " slot count");
                for (int i = 0; i < cap; ++i)
                    Assert.True(v[i].GetUInt32() == HandleOf(r.receivers[i]), $"{ctx} slot {i}: expected {v[i].GetUInt32()}, actual {HandleOf(r.receivers[i])}");
                ++si;
            }
            ++ci;
        }
        Assert.Equal(new[] { 1, 4, 10, 16 }, capacities.ToArray());
    }

    [Fact]
    public void Deliveries_match()
    {
        int ci = 0;
        foreach (JsonElement c in Cases("dlv"))
        {
            var d = new AI.Deliveries();
            int si = 0;
            foreach (JsonElement st in c.GetProperty("steps").EnumerateArray())
            {
                string op = st.GetProperty("op").GetString()!, ctx = $"dlv {ci} step {si} ({op})";
                bool? got = null;
                switch (op)
                {
                    case "watch": d.Watch(H(st.GetProperty("p")), H(st.GetProperty("b"))); break;
                    case "enter": d.Entered(H(st.GetProperty("p")), H(st.GetProperty("b"))); break;
                    case "done": got = d.Completed(H(st.GetProperty("p"))); break;
                    case "clr": d.Clear(); break;
                    default: Assert.Fail(ctx + ": unknown op"); break;
                }
                JsonElement res = st.GetProperty("r");
                if (res.ValueKind == JsonValueKind.Null) Assert.True(got == null, ctx + " result");
                else Assert.True(got == Bool(res), ctx + " result");
                JsonElement v = st.GetProperty("v");
                Assert.True(v.GetArrayLength() == AI.Deliveries.Slots, ctx + " receipt count");
                for (int i = 0; i < AI.Deliveries.Slots; ++i)
                {
                    AI.Deliveries.Receipt x = d.receipts[i];
                    Assert.True(v[i][0].GetUInt32() == HandleOf(x.Patient) && v[i][1].GetUInt32() == HandleOf(x.Bed) && Bool(v[i][2]) == x.Complete,
                        $"{ctx} receipt {i}: expected {v[i].GetRawText()}, actual [{HandleOf(x.Patient)},{HandleOf(x.Bed)},{(x.Complete ? 1 : 0)}]");
                }
                ++si;
            }
            ++ci;
        }
    }

    struct Recorder : AI.INative
    {
        public string Log;
        public void RestoreGoals() { Log += "RestoreGoals,"; }
        public void ClearOrders() { Log += "ClearOrders,"; }
        public void Rethink() { Log += "Rethink,"; }
        public void ChooseGoal() { Log += "ChooseGoal,"; }
        public void Schedule() { Log += "Schedule,"; }
    }

    [Fact]
    public void Native_hand_over_calls_match_and_reach_a_struct_by_ref()
    {
        foreach (JsonElement c in Cases("nat"))
        {
            var r = new Recorder { Log = "" };
            string f = c.GetProperty("f").GetString()!;
            if (f == "PrepareOwnedOrder") AI.PrepareOwnedOrder(ref r);
            else if (f == "ResumeNative") AI.ResumeNative(ref r);
            else Assert.Fail("unknown " + f);
            Assert.Equal(c.GetProperty("log").GetString(), r.Log);
        }
    }

    [Fact]
    public void Orders_handled_tables_match()
    {
        foreach (JsonElement c in Cases("oh"))
        {
            string f = c.GetProperty("f").GetString()!, ctx = c.GetRawText();
            JsonElement a = c.GetProperty("a");
            bool r = Bool(c, "r");
            switch (f)
            {
                case "OrdersHandled2": Assert.True(r == AI.OrdersHandled(Bool(a[0]), Bool(a[1])), ctx); break;
                case "OrdersHandled4": Assert.True(r == AI.OrdersHandled(Bool(a[0]), Bool(a[1]), Bool(a[2]), Bool(a[3])), ctx); break;
                case "CanReserveExternal": Assert.True(r == AI.CanReserveExternal(Bool(a[0]), Bool(a[1]), Bool(a[2])), ctx); break;
                default: Assert.Fail("unknown " + ctx); break;
            }
        }
    }
}
