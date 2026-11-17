// sources: ArenaSnapshotJson.cpp ArenaSnapshotRepository.cpp ArenaRewards.cpp ArenaPerformance.cpp pgconfig_stub.cpp
// needs: rapidjson
// ArenaPersistence::SaveCoordinator (header-only ArenaSaveCoordinator.h) over a scripted in-memory Storage, with the REAL codec and
// repository linked (as in gen_snapshot.cpp). Shipped config only (bookie maximum 10000). One JSON array, one case per line:
//   "sc" {i, steps}  a random call sequence on one coordinator. Each step {op, ...args, r, err, pend, tgt, key, gen, log, files}:
//                    r = FinishSave's result (null for other ops), err = Error(), pend / tgt / key / gen = the private pending flag,
//                    target and captured saveKey / generation after the step (read with #define class struct + private public), log = the
//                    storage calls of this step, files = every stored file [path, text id] after it (only on steps that
//                    can change them: put, del, or a Preserve / Replace call; otherwise the key is absent and nothing changed).
//   "txt" {id, t}    every distinct file text, referenced by id from "put" (t) and "files".
//   Ops: begin {d, g, b} (BeginSave(destination d, generation g, base snapshot b: 0 default (empty saveKey: not encodable until a key is set), 1 one row with a Katana tier, 2 a planned bout with
//        startHours NaN = not encodable, 3 credits + the weapon licence unlock); the base object stays the caller's and "mut" later scribbles on it),
//        mut (scribble on the last begin's snapshot: must not reach the capture), key {v} (SetSaveKey), fin {n} (FinishSave(n)),
//        ab (AbandonWorld), sto {rf, pf, wf, se} (storage switches: Read fails, Preserve fails, Replace fails, failures set an
//        error text), put {p, t} (store file p with text id t: base 1 encoded, or "not json"), del {p} (remove a file).
// Texts are byte strings: bytes 0x20..0x7e other than '"' and '\\' as themselves, every other byte as \u00XX.
#include "ArenaSnapshotJson.h"
#include "ArenaSnapshotRepository.h"
#include "ArenaRewards.h"
#include <cmath>
#include <cstdio>
#include <map>
#include <string>
#include <vector>
// Every header must be included BEFORE the two #defines below (the patched headers' own includes too): a std or project header
// first included under them would compile with `class` -> `struct` and `private` -> `public` and could break or change.
#define class struct
#define private public
#include "ArenaSaveCoordinator.h"
#undef private
#undef class

using namespace ArenaPersistence;

static unsigned s_seed = 4242017u;
static unsigned R() { s_seed = s_seed * 1664525u + 1013904223u; return s_seed >> 8; }
static unsigned Rn(unsigned n) { return R() % n; }
static bool Rp(unsigned pct) { return Rn(100) < pct; }

static bool g_first = true;
static void Line(const std::string& s) { std::printf("%s%s\n", g_first ? "[" : ",", s.c_str()); g_first = false; }
static std::string Q(const std::string& v) {
    std::string out = "\"";
    for (size_t i = 0; i < v.size(); ++i) {
        const unsigned char c = static_cast<unsigned char>(v[i]);
        if (c >= 0x20 && c <= 0x7e && c != '"' && c != '\\') out += static_cast<char>(c);
        else { char b[8]; std::snprintf(b, sizeof b, "\\u%04x", c); out += b; }
    }
    return out + "\"";
}
static std::string I(long long v) { char b[32]; std::snprintf(b, sizeof b, "%lld", v); return b; }
static std::string B(bool v) { return v ? "true" : "false"; }

struct Fake : Storage {
    std::map<std::string, std::string> files;
    bool readFail, preserveFail, replaceFail, setError;
    std::string log;
    Fake() : readFail(false), preserveFail(false), replaceFail(false), setError(false) {}
    ReadCode Read(const std::string& path, std::string& text, std::string& error) {
        log += "R:" + path + ";";
        if (readFail) { if (setError) error = "read broke"; return ReadFailed; }
        std::map<std::string, std::string>::const_iterator f = files.find(path);
        if (f == files.end()) return NotFound;
        text = f->second; return ReadOk;
    }
    bool ListBackups(const std::string& path, std::vector<std::string>&, std::string&) { log += "L:" + path + ";"; return true; }
    bool Preserve(const std::string& path, std::string& backup, std::string& error) {
        log += "P:" + path + ";";
        if (preserveFail) { if (setError) error = "keep broke"; return false; }
        backup = path + ".bak"; files[backup] = files[path]; return true;
    }
    bool Replace(const std::string& path, const std::string& text, std::string& error) {
        log += "W:" + path + ";";
        if (replaceFail) { if (setError) error = "write broke"; return false; }
        files[path] = text; return true;
    }
};

static Snapshot Base(int b) {
    Snapshot s;
    if (b == 1) {
        s.saveKey = "s1"; s.generation = "old";
        LeaderboardData::Progression p; p.id = "a"; p.name = "Al"; p.marks = 5;
        p.rewardTiers.push_back(RewardProgression::TierEntry(ArenaRewards::StableId(ArenaRewards::PieceKatana), 1));
        s.fighters.progression.push_back(p);
    } else if (b == 2) {
        s.plannedNpc.active = true; s.plannedNpc.registryId = "r"; s.plannedNpc.startHours = NAN;
        s.plannedNpc.teamA.push_back("x"); s.plannedNpc.teamB.push_back("y");
    } else if (b == 3) {
        s.bookieCredit = 7; s.challengeCredit = 3; s.fighters.factionUnlocks.push_back(ArenaRewards::GetWeaponLicence().stableId); s.saveKey = "s3";
    }
    return s;
}

static const char* kDest[] = { "save/a.json", "save/b.json", "" };
static const char* kGen[] = { "g1", "g2", "" };
static const char* kKeys[] = { "k1", "k2", "" };

static std::map<std::string, int> g_texts;   // every distinct file text, printed once as a "txt" line
static std::vector<std::string> g_textOrder;
static int TextId(const std::string& t) {
    std::map<std::string, int>::const_iterator i = g_texts.find(t);
    if (i != g_texts.end()) return i->second;
    const int id = static_cast<int>(g_textOrder.size()); g_texts[t] = id; g_textOrder.push_back(t); return id;
}
static std::string Files(const Fake& f) {
    std::string o = "[";
    bool first = true;
    for (std::map<std::string, std::string>::const_iterator i = f.files.begin(); i != f.files.end(); ++i) {
        o += std::string(first ? "" : ",") + "[" + Q(i->first) + "," + I(TextId(i->second)) + "]"; first = false;
    }
    return o + "]";
}

int main() {
    int cases = 0, steps = 0;
    for (int i = 0; i < 300; ++i) {
        Fake files;
        SaveCoordinator c(files);
        Snapshot held;           // the caller's snapshot of the last begin
        std::string out;
        if (Rp(40)) {            // an existing sidecar before the first save
            std::string text, error; EncodeSnapshot(Base(Rp(50) ? 1 : 3), text, error); files.files[kDest[Rn(2)]] = text;
            out = "{\"op\":\"put\",\"p\":" + Q(files.files.begin()->first) + ",\"t\":" + I(TextId(text)) + ",\"r\":null,\"err\":\"\",\"pend\":false,\"tgt\":\"\",\"key\":\"\",\"gen\":\"\",\"log\":\"\",\"files\":" + Files(files) + "}";
            ++steps;
        }
        const int n = 3 + static_cast<int>(Rn(10));
        for (int k = 0; k < n; ++k) {
            files.log.clear();
            std::string o, r = "null";
            const unsigned op = k > 0 && Rp(30) ? 40 : Rn(100);   // 30%: SetSaveKey (usually right after a begin)
            if (op < 32) {
                const std::string d = Rp(85) ? kDest[Rn(2)] : kDest[2];
                const std::string g = Rp(85) ? kGen[Rn(2)] : kGen[2];
                const int b = static_cast<int>(Rn(4));
                held = Base(b);
                c.BeginSave(d, g, held);
                o = "\"op\":\"begin\",\"d\":" + Q(d) + ",\"g\":" + Q(g) + ",\"b\":" + I(b);
            } else if (op < 37) {
                held.saveKey = "scribble"; held.generation = "scribble"; held.fighters.factionUnlocks.push_back("scribble");
                o = "\"op\":\"mut\"";
            } else if (op < 50) {
                const std::string v = kKeys[Rn(3)]; c.SetSaveKey(v); o = "\"op\":\"key\",\"v\":" + Q(v);
            } else if (op < 78) {
                const bool nat = Rp(75); r = B(c.FinishSave(nat)); o = "\"op\":\"fin\",\"n\":" + B(nat);
            } else if (op < 84) {
                c.AbandonWorld(); o = "\"op\":\"ab\"";
            } else if (op < 92) {
                files.readFail = Rp(20); files.preserveFail = Rp(20); files.replaceFail = Rp(20); files.setError = Rp(50);
                o = "\"op\":\"sto\",\"rf\":" + B(files.readFail) + ",\"pf\":" + B(files.preserveFail) + ",\"wf\":" + B(files.replaceFail) + ",\"se\":" + B(files.setError);
            } else if (op < 97) {
                const std::string p = kDest[Rn(2)]; std::string text;
                const int t = static_cast<int>(Rn(2));
                if (t == 0) { std::string error; EncodeSnapshot(Base(1), text, error); } else text = "not json";
                files.files[p] = text;
                o = "\"op\":\"put\",\"p\":" + Q(p) + ",\"t\":" + I(TextId(text));
            } else {
                const std::string p = kDest[Rn(2)]; files.files.erase(p); o = "\"op\":\"del\",\"p\":" + Q(p);
            }
            o += ",\"r\":" + r + ",\"err\":" + Q(c.Error()) + ",\"pend\":" + B(c.pending) + ",\"tgt\":" + Q(c.target) +
                ",\"key\":" + Q(c.captured.saveKey) + ",\"gen\":" + Q(c.captured.generation) + ",\"log\":" + Q(files.log) +
                (files.log.find('P') != std::string::npos || files.log.find('W') != std::string::npos || o.find("\"put\"") != std::string::npos ||
                 o.find("\"del\"") != std::string::npos ? ",\"files\":" + Files(files) : std::string());
            out += std::string(out.empty() ? "" : ",") + "{" + o + "}";
            ++steps;
        }
        Line("{\"k\":\"sc\",\"i\":" + I(i) + ",\"steps\":[" + out + "]}");
        ++cases;
    }
    for (size_t t = 0; t < g_textOrder.size(); ++t) Line("{\"k\":\"txt\",\"id\":" + I(static_cast<long long>(t)) + ",\"t\":" + Q(g_textOrder[t]) + "}");
    std::printf("]\n");
    std::fprintf(stderr, "savecoord: %d cases, %d steps, %d texts\n", cases, steps, static_cast<int>(g_textOrder.size()));
    return 0;
}
