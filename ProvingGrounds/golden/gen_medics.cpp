// sources:
// (header-only: TownMedicCoordinator.h, TownMedicAIPolicy.h and TownAftercarePolicy.h have no .cpp, so nothing is linked)
// The town medic planner, its AI-policy helpers and the aftercare policy, as JSON lines in one array; each line has "k".
//   "seq"  {i, n, steps}  a Coordinator driven through a random sequence. n = [medicCount, patientCount, bedCount] it starts with.
//          Each step {op, ...args, s}: s = the coordinator's whole state after the step (see State() below; private members
//          are read by compiling TownMedicCoordinator.h with `#define class struct` + `#define private public`, no layout or behaviour change).
//          Ops: tick {fo, e, hs, o}   Tick(observation o, fightOver fo, elapsed e, holdSpares hs). o = the observation (Obs() below).
//               fail {m, p, kd}       Fail(m, p, kind) (p may be out of range: the C++ returns early)
//               reopen, reset         Reopen(), Reset()
//               hand {c}              HandoffPending(c)
//          Between ticks a small world simulator answers the intents (treatment lowers the need, a pick-up carries, a delivery
//          beds the patient, ...) and adds random noise (flags flip, beds fill, counts change); only the printed observations
//          matter to the test, the simulator itself is not ported. Shapes: capacity edges (exactly 10 medics / 16 patients /
//          64 beds, empty sets), random worlds, "ties" (twin patients, beds and medics, so the first-wins comparisons decide)
//          quiet bedless worlds where the 45 s bed wait runs out, and five hand-built "script" sequences (no random
//          numbers) for branches the random worlds can miss: distance exactly 12 to a patient and to the hospital, equal
//          flesh and repair needs, a helper's sticky bandaging, a carry of another patient than the one a bed is held for.
//   "ap"   {f, a, r}       a TownAftercarePolicy free function f with arguments a and result r (BedPollDue / MedicPollDue take
//                          unsigned long, 32 bits on MSVC: their arguments and results go through uint32_t, so the golden shows the
//                          shipped wrap-around).
//   "cp"   {steps}         a CommandProgress driven by Update(next, elapsed, distance, aid) / Reset; each step prints r and
//                          the struct after it [action, issued, bestDistance, bestAid, lastProgress, lastObserved, stalledRetries].
//   "res"  {cap, steps}    a ReceiverReservations<cap> (10, 16, 4, and 1) driven by Add / Remove / Contains / Clear on fake
//                          pointers 0 (null) .. 21; each step prints r and the slots after it.
//   "dlv"  {steps}         a Deliveries driven by Watch / Entered / Completed / Clear on fake patients 0..20 and beds 0..4;
//                          each step prints r and the 16 receipts after it [patient, bed, complete].
//   "nat"  {f, log}        PrepareOwnedOrder / ResumeNative over a recording Native: the call order.
//   "oh"   {a, r}          OrdersHandled(2 args), OrdersHandled(4 args) and CanReserveExternal over every input.
// Out-of-range inputs that are undefined behaviour in the C++ (counts above the array sizes, a carried index >= MaxPatients,
// HandoffPending(count > MaxPatients), Fail with m outside 0..9) stay out of the golden.
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <cstdio>
#include <string>
#include <vector>
#include <cstring>
#include "TownAftercarePolicy.h"
// TownMedicAIPolicy.h says template<class Native>, which `#define class struct` would break: it is included normally and its
// private arrays are read by memcpy into a mirror of the same layout (both classes hold one array of plain data, nothing else).
#include "TownMedicAIPolicy.h"
#define class struct
#define private public
#include "TownMedicCoordinator.h"
#undef private
#undef class

using namespace TownMedicCoordinator;

static unsigned s_seed = 1110001u;
static unsigned R() { s_seed = s_seed * 1664525u + 1013904223u; return s_seed >> 8; }
static unsigned Rn(unsigned n) { return R() % n; }
static bool Rp(unsigned pct) { return Rn(100) < pct; }

static bool g_first = true;
static void Line(const std::string& s) { std::printf("%s%s\n", g_first ? "[" : ",", s.c_str()); g_first = false; }
static std::string I(long long v) { char b[32]; std::snprintf(b, sizeof b, "%lld", v); return b; }
static std::string U(unsigned long long v) { char b[32]; std::snprintf(b, sizeof b, "%llu", v); return b; }
static std::string F(float v) { char b[48]; std::snprintf(b, sizeof b, "%.9g", static_cast<double>(v)); return b; }
static std::string B(bool v) { return v ? "1" : "0"; }

// positions on a quarter grid (exact in float): distances tie, and land on both sides of 12
static float Coord() { return (static_cast<int>(Rn(161)) - 80) * .25f; }
static Point RandomPoint() { Point p; p.x = Coord(); p.y = Rp(70) ? 0.0f : Coord() * .1f; p.z = Coord(); return p; }
static Point Near(const Point& a) {
    Point p = a; p.x += (static_cast<int>(Rn(33)) - 16) * .5f; p.z += (static_cast<int>(Rn(33)) - 16) * .5f; return p;
}
static float Need() {
    static const float kNeeds[] = { 0.0f, 0.0f, .01f, .0100001f, .02f, .2f, .5f, 1.0f, .009f, .75f };
    return Rp(30) ? kNeeds[Rn(10)] : Rn(101) * .01f;
}
static float Bleed() {
    static const float kBleed[] = { 0.0f, .0001f, .00011f, .5f, 2.0f };
    return Rp(60) ? 0.0f : kBleed[Rn(5)];
}

static std::string PointJ(const Point& p) { return F(p.x) + "," + F(p.y) + "," + F(p.z); }
static bool Zero(const Point& p) { return p.x == 0 && p.y == 0 && p.z == 0; }
// Only the entries below the counts are printed: every entry at or above a count must be default-constructed (the planner still
// reads some by index: a carried patient index, an assignment kept from a larger earlier count), so the test rebuilds them.
static void CheckDefaultTail(const Observation& o) {
    bool ok = true;
    for (int i = o.medicCount; i < MaxMedics; ++i) { const MedicObservation& m = o.medics[i];
        ok = ok && !m.ready && !m.bandages && !m.repairs && m.carrying == -1 && Zero(m.position); }
    for (int i = o.patientCount; i < MaxPatients; ++i) { const PatientObservation& p = o.patients[i];
        ok = ok && !p.valid && !p.hospital && !p.robot && !p.inBed && !p.delivered && !p.externalCarry && !p.combat && !p.canWalk &&
            p.eligible && p.flesh == 0 && p.repair == 0 && p.bleed == 0 && Zero(p.position); }
    for (int i = o.bedCount; i < MaxBeds; ++i) { const BedObservation& b = o.beds[i];
        ok = ok && !b.available && !b.robot && b.occupant == -1 && Zero(b.position); }
    if (!ok) { std::fprintf(stderr, "gen_medics: an observation entry above its count is not the default\n"); std::exit(3); }
}
static std::string Obs(const Observation& o) {
    CheckDefaultTail(o);
    std::string s = "{\"m\":[";
    for (int i = 0; i < o.medicCount; ++i) {
        const MedicObservation& m = o.medics[i];
        s += std::string(i ? "," : "") + "[" + B(m.ready) + "," + B(m.bandages) + "," + B(m.repairs) + "," + I(m.carrying) + "," +
            PointJ(m.position) + "]";
    }
    s += "],\"p\":[";
    for (int i = 0; i < o.patientCount; ++i) {
        const PatientObservation& p = o.patients[i];
        s += std::string(i ? "," : "") + "[" + B(p.valid) + "," + B(p.hospital) + "," + B(p.robot) + "," + B(p.inBed) + "," +
            B(p.delivered) + "," + B(p.externalCarry) + "," + B(p.combat) + "," + B(p.canWalk) + "," + B(p.eligible) + "," +
            F(p.flesh) + "," + F(p.repair) + "," + F(p.bleed) + "," + PointJ(p.position) + "]";
    }
    s += "],\"b\":[";
    for (int i = 0; i < o.bedCount; ++i) {
        const BedObservation& b = o.beds[i];
        s += std::string(i ? "," : "") + "[" + B(b.available) + "," + B(b.robot) + "," + I(b.occupant) + "," + PointJ(b.position) + "]";
    }
    return s + "],\"ha\":" + B(o.hospitalAvailable) + ",\"h\":[" + PointJ(o.hospital) + "]}";
}
static std::string Arr(const int* v, int n) { std::string s = "["; for (int i = 0; i < n; ++i) s += std::string(i ? "," : "") + I(v[i]); return s + "]"; }

// The whole coordinator after a step: every member, then the public queries (out-of-range arguments included).
static std::string State(const Coordinator& c) {
    std::string s = "{\"ph\":" + I(c.phase) + ",\"mb\":" + B(c.missingBed) + ",\"ew\":" + I(c.expiredWaits) + ",\"lt\":" + F(c.lastTick);
    s += ",\"cmd\":[";
    for (int m = 0; m < MaxMedics; ++m)
        s += std::string(m ? "," : "") + "[" + I(c.commands[m].kind) + "," + I(c.commands[m].patient) + "," + I(c.commands[m].bed) + "]";
    s += "],\"as\":" + Arr(c.assigned, MaxMedics) + ",\"rb\":" + Arr(c.reservedBed, MaxMedics) + ",\"am\":" + Arr(c.activeMode, MaxMedics) +
        ",\"ht\":" + Arr(c.helperTarget, MaxMedics) + ",\"hm\":" + Arr(c.helperMode, MaxMedics);
    s += ",\"fl\":[";   // nonzero failed[m][p] only: [m, p, bits]
    bool any = false;
    for (int m = 0; m < MaxMedics; ++m) for (int p = 0; p < MaxPatients; ++p) if (c.failed[m][p]) {
        s += std::string(any ? "," : "") + "[" + I(m) + "," + I(p) + "," + U(c.failed[m][p]) + "]"; any = true;
    }
    int wf = 0, bx = 0, done = 0, work = 0, expired = 0;
    for (int p = 0; p < MaxPatients; ++p) {
        if (c.walkFailed[p]) wf |= 1 << p;
        if (c.bedWaitExpired[p]) bx |= 1 << p;
        if (c.PatientDone(p)) done |= 1 << p;
        if (c.HasPatientWork(p)) work |= 1 << p;
        if (c.WaitExpired(p)) expired |= 1 << p;
    }
    s += "],\"wf\":" + I(wf) + ",\"oc\":[";
    for (int p = 0; p < MaxPatients; ++p) s += std::string(p ? "," : "") + I(c.outcomes[p]);
    s += "],\"bw\":[";
    for (int p = 0; p < MaxPatients; ++p) s += std::string(p ? "," : "") + F(c.bedWait[p]);
    s += "],\"bx\":" + I(bx);
    int helping = 0, medicDone = 0;
    for (int m = 0; m < MaxMedics; ++m) { if (c.IsHelping(m)) helping |= 1 << m; if (c.MedicDone(m)) medicDone |= 1 << m; }
    // q: GetPhase, IsHelping mask, MedicDone mask, PatientDone mask, HasPatientWork mask, WaitExpired mask, HasUnresolved,
    //    MissingBed, ExpiredWaits, HasPatientWork(-1), ReservedBed(-1), ReservedBed(10), BedWait(-1), BedWait(16),
    //    WaitExpired(-1), WaitExpired(16)
    s += ",\"q\":[" + I(c.GetPhase()) + "," + I(helping) + "," + I(medicDone) + "," + I(done) + "," + I(work) + "," + I(expired) + "," +
        B(c.HasUnresolved()) + "," + B(c.MissingBed()) + "," + I(c.ExpiredWaits()) + "," + B(c.HasPatientWork(-1)) + "," +
        I(c.ReservedBed(-1)) + "," + I(c.ReservedBed(MaxMedics)) + "," + F(c.BedWait(-1)) + "," + F(c.BedWait(MaxPatients)) + "," +
        B(c.WaitExpired(-1)) + "," + B(c.WaitExpired(MaxPatients)) + "]";
    // PatientOutcome(p), ReservedBed(m) and BedWait(p) in range return the members printed above (checked against oc / rb / bw)
    for (int p = 0; p < MaxPatients; ++p) if (c.PatientOutcome(p) != c.outcomes[p] || c.BedWait(p) != c.bedWait[p]) std::exit(2);
    for (int m = 0; m < MaxMedics; ++m) if (c.ReservedBed(m) != c.reservedBed[m]) std::exit(2);
    return s + "}";
}

// ---------------------------------------------------------------- the world (generator only)
static Observation g_o;
static Coordinator g_c;
static float g_elapsed;
static int g_fightIn;          // ticks of fightOver = false before the fight ends
static bool g_hold;
static unsigned g_noise;       // percent

static MedicObservation RandomMedic() {
    MedicObservation m;
    m.ready = Rp(88); m.bandages = Rp(75); m.repairs = Rp(40); m.carrying = -1; m.position = RandomPoint();
    return m;
}
static PatientObservation RandomPatient() {
    PatientObservation p;
    p.valid = Rp(92); p.hospital = Rp(65); p.robot = Rp(25); p.inBed = Rp(5); p.delivered = Rp(4);
    p.externalCarry = Rp(4); p.combat = Rp(6); p.canWalk = Rp(35); p.eligible = Rp(92);
    const bool needs = Rp(65); p.flesh = needs ? Need() : 0.0f; p.repair = needs && (p.robot || Rp(20)) ? Need() : 0.0f; p.bleed = Bleed(); p.position = RandomPoint();
    return p;
}
static BedObservation RandomBed() {
    BedObservation b;
    b.available = Rp(70); b.robot = Rp(30); b.occupant = Rp(80) ? -1 : static_cast<int>(Rn(17)) - 1; b.position = RandomPoint();
    return b;
}
static void Populate(int nm, int np, int nb) {
    g_o = Observation();
    g_o.medicCount = nm; g_o.patientCount = np; g_o.bedCount = nb;
    for (int i = 0; i < nm; ++i) g_o.medics[i] = RandomMedic();
    for (int i = 0; i < np; ++i) g_o.patients[i] = RandomPatient();
    for (int i = 0; i < nb; ++i) g_o.beds[i] = RandomBed();
    g_o.hospitalAvailable = Rp(80);
    g_o.hospital = nb > 0 && Rp(70) ? g_o.beds[0].position : RandomPoint();
}

static bool Carried(int p) { for (int m = 0; m < g_o.medicCount; ++m) if (g_o.medics[m].carrying == p) return true; return false; }

// The world's answer to the commands of the last tick.
static void Respond() {
    for (int m = 0; m < g_o.medicCount; ++m) {
        MedicObservation& med = g_o.medics[m];
        const Intent& in = g_c.commands[m];
        if (Rp(10)) continue;   // the native order did not progress this second
        const int p = in.patient;
        PatientObservation* pat = p >= 0 && p < g_o.patientCount ? &g_o.patients[p] : 0;
        switch (in.kind) {
        case Bandage: if (pat) { med.position = Near(pat->position); pat->flesh = pat->flesh > .15f ? pat->flesh - .15f : (Rp(80) ? 0.0f : .005f);
            if (Rp(50)) pat->bleed = 0; } break;
        case Repair: if (pat) { med.position = Near(pat->position); pat->repair = pat->repair > .2f ? pat->repair - .2f : 0.0f; } break;
        case ApproachCarry: if (pat) med.position = Rp(70) ? pat->position : Near(pat->position); break;
        case PickUp: if (pat && !Carried(p) && Rp(75)) { med.carrying = p; pat->canWalk = false; } break;
        case Deliver: if (pat && in.bed >= 0 && in.bed < g_o.bedCount && Rp(70)) {
            med.carrying = -1; pat->inBed = Rp(85); pat->delivered = Rp(60); pat->position = g_o.beds[in.bed].position;
            g_o.beds[in.bed].available = false; g_o.beds[in.bed].occupant = p; med.position = Near(pat->position); } break;
        case WalkBed: if (pat && in.bed >= 0 && in.bed < g_o.bedCount) {
            if (Rp(60)) { pat->inBed = true; pat->position = g_o.beds[in.bed].position; g_o.beds[in.bed].available = false;
                g_o.beds[in.bed].occupant = p; } else pat->position = Near(pat->position); } break;
        case GoHospital: med.position = Rp(80) ? g_o.hospital : Near(g_o.hospital); break;
        case PutDown: med.carrying = -1; break;
        default: break;
        }
        if (med.carrying >= 0 && med.carrying < g_o.patientCount) {
            g_o.patients[med.carrying].position = med.position; if (Rp(12)) g_o.patients[med.carrying].valid = false; }
        else if (Rp(8)) med.position = RandomPoint();
    }
}

static void Noise() {
    if (!Rp(g_noise)) return;
    const unsigned what = Rn(16);
    if (what == 0 && g_o.medicCount) g_o.medics[Rn(g_o.medicCount)].ready ^= true;
    else if (what == 1 && g_o.medicCount) { MedicObservation& m = g_o.medics[Rn(g_o.medicCount)]; m.bandages = Rp(60); m.repairs = Rp(50); }
    else if (what == 2 && g_o.medicCount) { MedicObservation& m = g_o.medics[Rn(g_o.medicCount)];
        m.carrying = Rp(40) ? -2 : Rp(50) ? -1 : static_cast<int>(Rn(MaxPatients)); if (m.carrying == -2) m.ready = false; }
    else if (what == 3 && g_o.patientCount) g_o.patients[Rn(g_o.patientCount)].combat ^= true;
    else if (what == 4 && g_o.patientCount) g_o.patients[Rn(g_o.patientCount)].externalCarry ^= true;
    else if (what == 5 && g_o.patientCount) g_o.patients[Rn(g_o.patientCount)].valid ^= true;
    else if (what == 6 && g_o.patientCount) g_o.patients[Rn(g_o.patientCount)].canWalk ^= true;
    else if (what == 7 && g_o.patientCount) { PatientObservation& p = g_o.patients[Rn(g_o.patientCount)]; p.flesh = Need(); p.bleed = Bleed(); }
    else if (what == 8 && g_o.patientCount) g_o.patients[Rn(g_o.patientCount)].hospital ^= true;
    else if (what == 9 && g_o.patientCount) g_o.patients[Rn(g_o.patientCount)].eligible ^= true;
    else if (what == 10 && g_o.bedCount) { BedObservation& b = g_o.beds[Rn(g_o.bedCount)]; b.available ^= true;
        if (Rp(30)) b.occupant = static_cast<int>(Rn(17)) - 1; }
    else if (what == 11) g_o.hospitalAvailable ^= true;
    else if (what == 12 && g_o.patientCount) { PatientObservation& p = g_o.patients[Rn(g_o.patientCount)]; p.inBed = false; p.delivered = false; }
    else if (what == 13) {   // a count changes; entries at or above a count are default-constructed (see Obs)
        const int which = static_cast<int>(Rn(3));
        if (which == 0) { g_o.medicCount = static_cast<int>(Rn(MaxMedics + 1)); for (int i = g_o.medicCount; i < MaxMedics; ++i) g_o.medics[i] = MedicObservation(); }
        else if (which == 1) { g_o.patientCount = static_cast<int>(Rn(MaxPatients + 1)); for (int i = g_o.patientCount; i < MaxPatients; ++i) g_o.patients[i] = PatientObservation(); }
        else { g_o.bedCount = static_cast<int>(Rn(9)); for (int i = g_o.bedCount; i < MaxBeds; ++i) g_o.beds[i] = BedObservation(); }
    }
    else if (what == 14 && g_o.patientCount) g_o.patients[Rn(g_o.patientCount)].repair = Need();
    else if (what == 15 && g_o.medicCount) g_o.medics[Rn(g_o.medicCount)].position = RandomPoint();
}

static std::string TickStep() {
    // elapsed mostly steps 1 s; sometimes a short or long gap (dt clamps to 1), a clock that went back (dt 0) or a restart at 0
    const unsigned e = Rn(100);
    if (e < 80) g_elapsed += 1.0f; else if (e < 87) g_elapsed += .25f * (1 + Rn(3)); else if (e < 92) g_elapsed += 1.5f + Rn(30);
    else if (e < 96) g_elapsed -= 1.0f + Rn(4); else if (e < 98) g_elapsed = 0.0f; else g_elapsed += 1.0001f;
    bool fightOver = g_fightIn <= 0;
    if (g_fightIn > 0) --g_fightIn;
    if (fightOver && Rp(3)) fightOver = false;
    const bool hold = Rp(85) ? g_hold : !g_hold;
    const std::string in = "\"op\":\"tick\",\"fo\":" + B(fightOver) + ",\"e\":" + F(g_elapsed) + ",\"hs\":" + B(hold) + ",\"o\":" + Obs(g_o);
    g_c.Tick(g_o, fightOver, g_elapsed, hold);
    const std::string out = "{" + in + ",\"s\":" + State(g_c) + "}";
    Respond();
    Noise();
    return out;
}

static std::string Step() {
    const unsigned r = Rn(100);
    if (r < 88) return TickStep();
    std::string in;
    if (r < 94) {
        const int m = static_cast<int>(Rn(MaxMedics)), p = Rp(85) ? static_cast<int>(Rn(MaxPatients)) : (Rp(50) ? -1 : MaxPatients + static_cast<int>(Rn(3)));
        const Kind k = static_cast<Kind>(Rn(12));
        g_c.Fail(m, p, k);
        in = "\"op\":\"fail\",\"m\":" + I(m) + ",\"p\":" + I(p) + ",\"kd\":" + I(k);
    } else if (r < 97) { g_c.Reopen(); in = "\"op\":\"reopen\""; }
    else if (r < 99) { const int count = static_cast<int>(Rn(MaxPatients + 1)); g_c.HandoffPending(count); in = "\"op\":\"hand\",\"c\":" + I(count); }
    else { g_c.Reset(); in = "\"op\":\"reset\""; }
    return "{" + in + ",\"s\":" + State(g_c) + "}";
}

// ---------------------------------------------------------------- hand-built sequences (no random numbers, no simulator)
// Each pins one branch the random worlds can miss; positions are exact in float (whole numbers on one axis).
static Point P(float x, float y, float z) { Point p; p.x = x; p.y = y; p.z = z; return p; }
static std::string ScriptTick(bool fightOver, float elapsed, bool hold) {
    const std::string in = "\"op\":\"tick\",\"fo\":" + B(fightOver) + ",\"e\":" + F(elapsed) + ",\"hs\":" + B(hold) + ",\"o\":" + Obs(g_o);
    g_c.Tick(g_o, fightOver, elapsed, hold);
    return "{" + in + ",\"s\":" + State(g_c) + "}";
}
static MedicObservation ScriptMedic(const Point& at, bool bandages, bool repairs) {
    MedicObservation m; m.ready = true; m.bandages = bandages; m.repairs = repairs; m.carrying = -1; m.position = at; return m;
}
static PatientObservation ScriptPatient(const Point& at, float flesh, float repair) {
    PatientObservation p; p.valid = true; p.hospital = true; p.flesh = flesh; p.repair = repair; p.position = at; return p;
}
static BedObservation ScriptBed(const Point& at) { BedObservation b; b.available = true; b.position = at; return b; }
static void Emit(int& id, const char* name, const std::string& steps) {
    Line("{\"k\":\"seq\",\"i\":" + I(id++) + ",\"script\":\"" + name + "\",\"n\":[" + I(g_o.medicCount) + "," + I(g_o.patientCount) + "," +
        I(g_o.bedCount) + "],\"steps\":[" + steps + "]}");
}
static void Scripted(int& id) {
    // 1. a transfer at exactly 12 from its medic is a PickUp (> 12.0f), at 12.5 an ApproachCarry
    g_c = Coordinator(); g_o = Observation(); g_o.medicCount = 2; g_o.patientCount = 2; g_o.bedCount = 2;
    g_o.medics[0] = ScriptMedic(P(0, 0, 0), true, false); g_o.medics[1] = ScriptMedic(P(0, 0, 0), true, false);
    g_o.patients[0] = ScriptPatient(P(12, 0, 0), 0, 0); g_o.patients[1] = ScriptPatient(P(0, 0, -12.5f), 0, 0);
    g_o.beds[0] = ScriptBed(P(20, 0, 0)); g_o.beds[1] = ScriptBed(P(0, 0, -20));
    Emit(id, "pickup_at_12", ScriptTick(true, 1, false));

    // 2. spare medics at exactly 12 from the hospital are Released (> 12.0f), at 12.5 sent there: first while a patient is
    //    still pending (combat: a candidate nobody may treat, holdSpares false), then once care completes (the patient is gone)
    g_c = Coordinator(); g_o = Observation(); g_o.medicCount = 3; g_o.patientCount = 1; g_o.bedCount = 0;
    g_o.medics[0] = ScriptMedic(P(12, 0, 0), true, false); g_o.medics[1] = ScriptMedic(P(0, 0, 12.5f), true, false);
    g_o.medics[2] = ScriptMedic(P(0, 0, 0), true, false);
    g_o.patients[0] = ScriptPatient(P(-30, 0, 0), .5f, 0); g_o.patients[0].combat = true;
    g_o.hospitalAvailable = true; g_o.hospital = P(0, 0, 0);
    {
        std::string steps = ScriptTick(true, 1, false);
        g_o.patients[0].valid = false;
        steps += "," + ScriptTick(true, 2, false);
        Emit(id, "hospital_at_12", steps);
    }

    // 3. a medic with both kits and equal flesh and repair needs bandages (repair > flesh is strict)
    g_c = Coordinator(); g_o = Observation(); g_o.medicCount = 1; g_o.patientCount = 1; g_o.bedCount = 1;
    g_o.medics[0] = ScriptMedic(P(0, 0, 0), true, true);
    g_o.patients[0] = ScriptPatient(P(4, 0, 0), .5f, .5f); g_o.patients[0].robot = true;
    g_o.beds[0] = ScriptBed(P(20, 0, 0)); g_o.beds[0].robot = true;
    Emit(id, "mode_tie", ScriptTick(true, 1, false));

    // 4. a helper bandaging keeps bandaging when the repair need overtakes flesh (the sticky helperMode == 0), and switches to
    //    repair once flesh clears; the primary does the same through activeMode
    g_c = Coordinator(); g_o = Observation(); g_o.medicCount = 2; g_o.patientCount = 1; g_o.bedCount = 1;
    g_o.medics[0] = ScriptMedic(P(0, 0, 0), true, true); g_o.medics[1] = ScriptMedic(P(0, 0, 8), true, true);
    g_o.patients[0] = ScriptPatient(P(4, 0, 0), .5f, .3f); g_o.patients[0].robot = true;
    g_o.beds[0] = ScriptBed(P(20, 0, 0)); g_o.beds[0].robot = true;
    {
        std::string steps = ScriptTick(true, 1, false);
        g_o.patients[0].flesh = .4f; g_o.patients[0].repair = .6f;
        steps += "," + ScriptTick(true, 2, false);
        g_o.patients[0].flesh = 0;
        steps += "," + ScriptTick(true, 3, false);
        Emit(id, "helper_sticky", steps);
    }

    // 5. a medic holding a bed for its patient picks up another one: the old reservation is dropped and the new patient gets
    //    its own nearest bed (the old one would still be usable)
    g_c = Coordinator(); g_o = Observation(); g_o.medicCount = 1; g_o.patientCount = 2; g_o.bedCount = 2;
    g_o.medics[0] = ScriptMedic(P(0, 0, 0), true, false);
    g_o.patients[0] = ScriptPatient(P(15, 0, 0), 0, 0); g_o.patients[1] = ScriptPatient(P(-15, 0, 0), 0, 0);
    g_o.beds[0] = ScriptBed(P(20, 0, 0)); g_o.beds[1] = ScriptBed(P(-20, 0, 0));
    {
        std::string steps = ScriptTick(true, 1, false);
        g_o.medics[0].carrying = 1; g_o.medics[0].position = P(-15, 0, 0);
        steps += "," + ScriptTick(true, 2, false);
        Emit(id, "carry_other", steps);
    }
}

static void Sequences() {
    int id = 0;
    struct Shape { int nm, np, nb, len, n; unsigned noise; };
    // capacity edges first (exactly 10 / 16 / 64, and empty sets), then random worlds, then long bed waits
    const Shape kEdges[] = {
        { 10, 16, 64, 24, 3, 10 }, { 10, 16, 0, 60, 2, 5 }, { 0, 0, 0, 6, 2, 10 }, { 0, 5, 3, 10, 2, 10 }, { 4, 0, 3, 10, 2, 10 },
        { 3, 4, 0, 60, 4, 5 }, { 10, 1, 1, 20, 2, 10 }, { 1, 16, 64, 20, 2, 10 }, { 10, 16, 1, 30, 2, 10 },
    };
    for (const Shape& sh : kEdges) for (int k = 0; k < sh.n; ++k) {
        g_c = Coordinator(); Populate(sh.nm, sh.np, sh.nb); g_elapsed = Rp(50) ? 0.0f : static_cast<float>(Rn(200));
        g_fightIn = static_cast<int>(Rn(4)); g_hold = Rp(25); g_noise = sh.noise;
        if (sh.nb == 0) for (int p = 0; p < sh.np; ++p) if (Rp(60)) { g_o.patients[p].flesh = 0; g_o.patients[p].repair = 0; g_o.patients[p].hospital = true; }
        std::string steps;
        for (int s = 0; s < sh.len; ++s) steps += std::string(s ? "," : "") + Step();
        Line("{\"k\":\"seq\",\"i\":" + I(id++) + ",\"n\":[" + I(sh.nm) + "," + I(sh.np) + "," + I(sh.nb) + "],\"steps\":[" + steps + "]}");
    }
    for (int k = 0; k < 200; ++k) {
        const int nm = Rp(10) ? 0 : 1 + static_cast<int>(Rn(Rp(80) ? 4 : MaxMedics));
        const int np = Rp(8) ? 0 : 1 + static_cast<int>(Rn(Rp(80) ? 5 : MaxPatients));
        const int nb = Rp(15) ? 0 : static_cast<int>(Rn(Rp(92) ? 6 : MaxBeds + 1));
        const int len = 8 + static_cast<int>(Rn(Rp(85) ? 22 : 50));
        g_c = Coordinator(); if (Rp(20)) g_c.Reset();
        Populate(nm, np, nb); g_elapsed = Rp(60) ? 0.0f : Coord() + 40.0f;
        g_fightIn = Rp(30) ? 0 : static_cast<int>(Rn(5)); g_hold = Rp(25); g_noise = Rp(30) ? 2 : 12;
        std::string steps;
        for (int s = 0; s < len; ++s) steps += std::string(s ? "," : "") + Step();
        Line("{\"k\":\"seq\",\"i\":" + I(id++) + ",\"n\":[" + I(nm) + "," + I(np) + "," + I(nb) + "],\"steps\":[" + steps + "]}");
    }
    // ties: twin patients (same needs and place), twin beds, twin medics, so the first-wins rules (score > priority,
    // distance < nearest, helpers < fewest) decide
    for (int k = 0; k < 24; ++k) {
        const int nm = 1 + static_cast<int>(Rn(4)), np = 2 + static_cast<int>(Rn(5)), nb = 2 + static_cast<int>(Rn(5));
        g_c = Coordinator(); Populate(nm, np, nb); g_elapsed = 0.0f; g_fightIn = static_cast<int>(Rn(2)); g_hold = Rp(25); g_noise = 4;
        for (int p = 1; p < np; ++p) if (Rp(70)) { g_o.patients[p] = g_o.patients[p - 1]; }
        for (int b = 1; b < nb; ++b) if (Rp(70)) { g_o.beds[b] = g_o.beds[b - 1]; }
        for (int m = 1; m < nm; ++m) if (Rp(50)) { g_o.medics[m] = g_o.medics[m - 1]; }
        std::string steps;
        const int len = 6 + static_cast<int>(Rn(20));
        for (int s = 0; s < len; ++s) steps += std::string(s ? "," : "") + Step();
        Line("{\"k\":\"seq\",\"i\":" + I(id++) + ",\"n\":[" + I(nm) + "," + I(np) + "," + I(nb) + "],\"steps\":[" + steps + "]}");
    }
    // bed waits that run out: patients owe a transfer, no compatible bed, a quiet world
    for (int k = 0; k < 10; ++k) {
        const int nm = 1 + static_cast<int>(Rn(3)), np = 1 + static_cast<int>(Rn(3)), nb = Rp(50) ? 0 : 1 + static_cast<int>(Rn(2));
        g_c = Coordinator(); Populate(nm, np, nb); g_elapsed = 0.0f; g_fightIn = 0; g_hold = Rp(50); g_noise = 0;
        for (int p = 0; p < np; ++p) { PatientObservation& q = g_o.patients[p];
            q.valid = true; q.eligible = true; q.hospital = true; q.flesh = q.repair = 0; q.inBed = q.delivered = q.combat = q.externalCarry = false; q.robot = false; }
        for (int b = 0; b < nb; ++b) { g_o.beds[b].available = Rp(30); g_o.beds[b].robot = true; }
        for (int m = 0; m < nm; ++m) { g_o.medics[m].ready = true; g_o.medics[m].carrying = -1; }
        std::string steps;
        for (int s = 0; s < 52; ++s) steps += std::string(s ? "," : "") + TickStep();
        Line("{\"k\":\"seq\",\"i\":" + I(id++) + ",\"n\":[" + I(nm) + "," + I(np) + "," + I(nb) + "],\"steps\":[" + steps + "]}");
    }
    Scripted(id);
}

// ---------------------------------------------------------------- TownAftercarePolicy
static void Aftercare() {
    using namespace TownAftercarePolicy;
    for (int w = 0; w < 2; ++w) for (int u = 0; u < 2; ++u) for (int n = 0; n < 2; ++n)
        Line("{\"k\":\"ap\",\"f\":\"NeedsHospital\",\"a\":[" + B(w) + "," + B(u) + "," + B(n) + "],\"r\":" + B(NeedsHospital(w, u, n)) + "}");
    const float kErr[] = { 0.0f, 323.0f, 324.0f, 324.00003f, 325.0f, 1e6f, -1.0f };
    for (int mv = 0; mv < 2; ++mv) for (float e : kErr)
        Line("{\"k\":\"ap\",\"f\":\"StandbyOrderLost\",\"a\":[" + B(mv) + "," + F(e) + "],\"r\":" + B(StandbyOrderLost(mv, e)) + "}");
    const float kAge[] = { -1.0f, 0.0f, 29.99f, 30.0f, 44.9999f, 45.0f, 45.0001f, 599.99f, 600.0f, 601.0f };
    for (float a : kAge) {
        Line("{\"k\":\"ap\",\"f\":\"TransferWaitExpired\",\"a\":[" + F(a) + "],\"r\":" + B(TransferWaitExpired(a)) + "}");
        Line("{\"k\":\"ap\",\"f\":\"BedStayExpired\",\"a\":[" + F(a) + "],\"r\":" + B(BedStayExpired(a)) + "}");
    }
    Line("{\"k\":\"ap\",\"f\":\"Budgets\",\"a\":[],\"r\":[" + F(TransferWaitBudget()) + "," + F(BedStayLimit()) + "," + F(BedReleaseRetry()) + "]}");
    // unsigned long is 32 bits on MSVC: feed and print 32-bit values only (on Linux the 64-bit type then behaves the same,
    // except where now - last wraps: there the shipped 32-bit result is computed with uint32_t, the same subtraction MSVC does)
    const uint32_t kTick[] = { 0u, 1u, 999u, 1000u, 1001u, 5000u, 0x7FFFFFFFu, 0x80000000u, 0xFFFFFC17u, 0xFFFFFC18u, 0xFFFFFFFEu, 0xFFFFFFFFu };
    for (uint32_t last : kTick) for (uint32_t now : kTick) for (int f = 0; f < 2; ++f) {
        // the C++ expression, evaluated in 32 bits as MSVC does
        const bool r32 = last == 0 || now < last || static_cast<uint32_t>(now - last) >= 1000u;
        const bool r = f == 0 ? BedPollDue(last, now) : MedicPollDue(last, now);
        if (r != r32) { std::fprintf(stderr, "gen_medics: 32-bit poll rule differs\n"); std::exit(1); }
        Line("{\"k\":\"ap\",\"f\":\"" + std::string(f == 0 ? "BedPollDue" : "MedicPollDue") + "\",\"a\":[" + U(last) + "," + U(now) + "],\"r\":" + B(r32) + "}");
    }
    for (int i = 0; i < 200; ++i) {   // random 32-bit pairs, many near each other and across the wrap
        const uint32_t last = Rp(30) ? 0xFFFFFFFFu - Rn(3000) : static_cast<uint32_t>(R()) * 257u;
        const uint32_t now = Rp(50) ? last + Rn(3000) : static_cast<uint32_t>(R()) * 131u;
        const bool r32 = last == 0 || now < last || static_cast<uint32_t>(now - last) >= 1000u;
        const bool bed = Rp(50);
        const bool r = bed ? BedPollDue(last, now) : MedicPollDue(last, now);
        if (r != r32) { std::fprintf(stderr, "gen_medics: 32-bit poll rule differs\n"); std::exit(1); }
        Line("{\"k\":\"ap\",\"f\":\"" + std::string(bed ? "BedPollDue" : "MedicPollDue") + "\",\"a\":[" + U(last) + "," + U(now) + "],\"r\":" + B(r32) + "}");
    }
    const int kFighters[] = { -5, -1, 0, 1, 2, 9, 10, 11, 100 };
    for (int f : kFighters) Line("{\"k\":\"ap\",\"f\":\"RequiredMedics\",\"a\":[" + I(f) + "],\"r\":" + I(RequiredMedics(f)) + "}");
    const float kH[] = { 0.0f, .5f, .98999f, .99f, .99001f, 1.0f };
    for (int u = 0; u < 2; ++u) for (float h : kH) for (float b : kH)
        Line("{\"k\":\"ap\",\"f\":\"NeedsRecovery\",\"a\":[" + B(u) + "," + F(h) + "," + F(b) + "],\"r\":" + B(NeedsRecovery(u, h, b)) + "}");
    for (int t = 0; t < 2; ++t)
        Line("{\"k\":\"ap\",\"f\":\"ClearExternalReservations\",\"a\":[" + B(t) + "],\"r\":" + B(ClearExternalReservations(t)) + "}");

    for (int k = 0; k < 300; ++k) {
        CommandProgress c;
        float elapsed = Rp(50) ? 0.0f : Rn(100) * .5f, distance = Rn(80) * .5f, aid = Rn(100) * .01f;
        Action a = static_cast<Action>(Rn(11));
        std::string steps;
        const int len = 3 + static_cast<int>(Rn(40));
        for (int s = 0; s < len; ++s) {
            std::string in;
            bool r = false;
            if (Rp(4)) { c.Reset(); in = "\"op\":\"reset\""; }
            else {
                if (Rp(15)) a = static_cast<Action>(Rn(11));
                const unsigned e = Rn(100);
                if (e < 70) elapsed += 1.0f; else if (e < 80) elapsed += 5.0f + Rn(30); else if (e < 88) elapsed -= Rn(5) * 1.0f;
                else if (e < 94) elapsed += .5f; else elapsed += 10.0f + (Rp(50) ? 0.0f : 20.0f);
                const unsigned d = Rn(100);
                if (d < 40) distance -= .5f + Rn(3) * .25f; else if (d < 60) distance -= 1.0f; else if (d < 75) distance += Rn(4) * .5f;
                const unsigned q = Rn(100);
                if (q < 30) aid -= .0001f; else if (q < 55) aid -= .05f; else if (q < 65) aid += .05f;
                r = c.Update(a, elapsed, distance, aid);
                in = "\"op\":\"up\",\"a\":" + I(a) + ",\"e\":" + F(elapsed) + ",\"d\":" + F(distance) + ",\"aid\":" + F(aid);
            }
            steps += std::string(s ? "," : "") + "{" + in + ",\"r\":" + B(r) + ",\"c\":[" + I(c.action) + "," + B(c.issued) + "," +
                F(c.bestDistance) + "," + F(c.bestAid) + "," + F(c.lastProgress) + "," + F(c.lastObserved) + "," + I(c.stalledRetries) + "]}";
        }
        Line("{\"k\":\"cp\",\"steps\":[" + steps + "]}");
    }
}

// ---------------------------------------------------------------- TownMedicAIPolicy
static const void* Ptr(unsigned h) { return reinterpret_cast<const void*>(static_cast<uintptr_t>(h) * 16u); }
static unsigned Handle(const void* p) { return static_cast<unsigned>(reinterpret_cast<uintptr_t>(p) / 16u); }

struct ReceiptMirror { const void* patient; const void* bed; bool complete; };
struct DeliveriesMirror { ReceiptMirror receipts[16]; };
static_assert(sizeof(DeliveriesMirror) == sizeof(TownMedicAIPolicy::Deliveries), "Deliveries layout");
template<int Cap> struct SlotsMirror { const void* receivers[Cap]; };

template<int Cap> static void Reservations(int sequences) {
    static_assert(sizeof(SlotsMirror<Cap>) == sizeof(TownMedicAIPolicy::ReceiverReservations<Cap>), "ReceiverReservations layout");
    for (int k = 0; k < sequences; ++k) {
        TownMedicAIPolicy::ReceiverReservations<Cap> r;
        const unsigned pool = Rp(50) ? Cap + 4 : 21;
        std::string steps;
        const int len = 5 + static_cast<int>(Rn(45));
        for (int s = 0; s < len; ++s) {
            const unsigned op = Rn(100), h = Rp(8) ? 0 : Rn(pool + 1);
            std::string in, res = "null";
            if (op < 50) { res = B(r.Add(Ptr(h))); in = "\"op\":\"add\",\"h\":" + U(h); }
            else if (op < 72) { r.Remove(Ptr(h)); in = "\"op\":\"rem\",\"h\":" + U(h); }
            else if (op < 97) { res = B(r.Contains(Ptr(h))); in = "\"op\":\"has\",\"h\":" + U(h); }
            else { r.Clear(); in = "\"op\":\"clr\""; }
            SlotsMirror<Cap> mirror; std::memcpy(&mirror, &r, sizeof mirror);
            std::string slots = "[";
            for (int i = 0; i < Cap; ++i) slots += std::string(i ? "," : "") + U(Handle(mirror.receivers[i]));
            steps += std::string(s ? "," : "") + "{" + in + ",\"r\":" + res + ",\"v\":" + slots + "]}";
        }
        Line("{\"k\":\"res\",\"cap\":" + I(Cap) + ",\"steps\":[" + steps + "]}");
    }
}

static void Deliveries() {
    for (int k = 0; k < 120; ++k) {
        TownMedicAIPolicy::Deliveries d;
        std::string steps;
        const int len = 5 + static_cast<int>(Rn(50));
        const unsigned pool = Rp(50) ? 20 : 8;
        for (int s = 0; s < len; ++s) {
            const unsigned op = Rn(100), p = Rp(6) ? 0 : Rn(pool + 1), b = Rp(6) ? 0 : 1 + Rn(4);
            std::string in, res = "null";
            if (op < 45) { d.Watch(Ptr(p), Ptr(b)); in = "\"op\":\"watch\",\"p\":" + U(p) + ",\"b\":" + U(b); }
            else if (op < 70) { d.Entered(Ptr(p), Ptr(b)); in = "\"op\":\"enter\",\"p\":" + U(p) + ",\"b\":" + U(b); }
            else if (op < 97) { res = B(d.Completed(Ptr(p))); in = "\"op\":\"done\",\"p\":" + U(p); }
            else { d.Clear(); in = "\"op\":\"clr\""; }
            DeliveriesMirror mirror; std::memcpy(&mirror, &d, sizeof mirror);
            std::string v = "[";
            for (int i = 0; i < 16; ++i)
                v += std::string(i ? "," : "") + "[" + U(Handle(mirror.receipts[i].patient)) + "," + U(Handle(mirror.receipts[i].bed)) + "," +
                    B(mirror.receipts[i].complete) + "]";
            steps += std::string(s ? "," : "") + "{" + in + ",\"r\":" + res + ",\"v\":" + v + "]}";
        }
        Line("{\"k\":\"dlv\",\"steps\":[" + steps + "]}");
    }
}

struct Recorder {
    std::string log;
    void RestoreGoals() { log += "RestoreGoals,"; }
    void ClearOrders() { log += "ClearOrders,"; }
    void Rethink() { log += "Rethink,"; }
    void ChooseGoal() { log += "ChooseGoal,"; }
    void Schedule() { log += "Schedule,"; }
};

static void AiPolicy() {
    Recorder a; TownMedicAIPolicy::PrepareOwnedOrder(a);
    Line("{\"k\":\"nat\",\"f\":\"PrepareOwnedOrder\",\"log\":\"" + a.log + "\"}");
    Recorder b; TownMedicAIPolicy::ResumeNative(b);
    Line("{\"k\":\"nat\",\"f\":\"ResumeNative\",\"log\":\"" + b.log + "\"}");
    for (int i = 0; i < 4; ++i)
        Line("{\"k\":\"oh\",\"f\":\"OrdersHandled2\",\"a\":[" + B(i & 1) + "," + B(i & 2) + "],\"r\":" +
            B(TownMedicAIPolicy::OrdersHandled((i & 1) != 0, (i & 2) != 0)) + "}");
    for (int i = 0; i < 16; ++i)
        Line("{\"k\":\"oh\",\"f\":\"OrdersHandled4\",\"a\":[" + B(i & 1) + "," + B(i & 2) + "," + B(i & 4) + "," + B(i & 8) + "],\"r\":" +
            B(TownMedicAIPolicy::OrdersHandled((i & 1) != 0, (i & 2) != 0, (i & 4) != 0, (i & 8) != 0)) + "}");
    for (int i = 0; i < 8; ++i)
        Line("{\"k\":\"oh\",\"f\":\"CanReserveExternal\",\"a\":[" + B(i & 1) + "," + B(i & 2) + "," + B(i & 4) + "],\"r\":" +
            B(TownMedicAIPolicy::CanReserveExternal((i & 1) != 0, (i & 2) != 0, (i & 4) != 0)) + "}");
    Reservations<10>(60); Reservations<16>(60); Reservations<4>(60); Reservations<1>(20);
    Deliveries();
}

int main() {
    Sequences();
    Aftercare();
    AiPolicy();
    std::printf("]\n");
    return 0;
}
