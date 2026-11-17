// sources:
// (header-only: TownArenaPolicy.h, TownArenaRuntimePolicy.h, TownChallengePolicy.h, TownRosterSelection.h, TownFurniturePolicy.h and
// TownFighterCatalog.h are inline functions with no .cpp, so nothing is linked)
// Every ported function of the six headers, as JSON lines in one array. Each line has "k" plus its inputs and outputs.
// Doubles are printed as their IEEE bits (decimal uint64, keys ending in "b") so NaN, -0 and infinities round-trip exactly; floats as %.9g.
// Strings are UTF-8 (JSON-escaped: '"', '\\' and control bytes as \u00XX); a JSON null stands for a NULL const char*.
// An "offer" object (O) and a "card" object (C) carry EVERY field of TownChallengePolicy::Offer / Card (see PrintOffer / PrintCard).
// Kinds:
//   ep     per refresh value r: hb[] hours (bits) -> ce[] CardEpoch(h), cce[] ChallengeCardEpoch(h, r), hu[] HoursUntilChallengeRefresh bits
//   cost   per (base, mult bits): o[i] = PaidRefreshCost(base, mult, n0 + i), counts -2..40
//   sel    SelectNext(busy, pending, npc); amb AmbientNext(paused, ready, busy, pending, now, due); disc ShouldDiscoverRegistry
//   team   SelectNpcTeamSize(recovered, roll)
//   ost    BookOffer / BeginOffer / CancelBooking on a state 0..3 (each from a copy of the input state)
//   dflt   Offer() and Card() as constructed
//   refr   RefreshGenerated(card, epoch, pinned): card in, card out
//   sdiv   SetDivision(card, division, locked): card in, return, card out
//   rar    OfferRarity over draw 0..9999 for a generated offer (r = array) / orn: non-generated encounter x division
//   l7     LegendaryEncounter 7-arg: fixed division, repeat, regular, skarn flags; r = array over uniqueWins 0..31 x namedRoll 0..15
//   l7r    LegendaryEncounter 7-arg on random full-width rolls
//   l3     LegendaryEncounter(division, roll, skarnUnlocked)
//   efd    EncounterForDivision(offer, slot, division, uniqueWins, skarnU, skarnW)
//   prev   PreviewSource(card, slot)
//   book   CanBookDivision(card, slot) and Visible(card, slot)
//   why    UnavailableReason(offer, playersReady, anchorReady, fitsWithoutMedics)
//   hex    Hex(s) / unhex: Unhex(s) -> ok, inv, result (result as hex of the bytes, since Unhex can produce any byte)
//   enc    Encode(card), EncodeLegacy(card), and Decode(Encode(card)) -> ok, card
//   dec    Decode(text) -> ok, inv, card (null when not ok: the C++ leaves the default-initialised target untouched)
//   decl   DecodeLegacy(text) -> ok, inv, card. inv = 1: the C++ accepted it but a decoded name is not valid UTF-8, which the
//          port refuses (docs/divergences.md); the card is then null and the port's expected result is "not ok"
//   hist   WriteHistory(history) text / rhist: ReadHistory(text) -> ok, inv, history, and the text the stream had not read
//   hlp    per random offer: Players, Opponents, SecondRole, OpponentRole(0..4), Resolved, ClearPreview copy
//   same   SameEncounter(a, b)
//   res    ReserveOffer(source, preview) -> ok, source, reservation (reservation starts as a random offer)
//   names  Role, Rarity, Title, ModeName, RarityName, DivisionName, NamedDivision, UniqueVictoryForEncounter, UniqueEncounter,
//          NamedForDivision(e, d) for d -1..3, for encounter/int -3..11
//   nrole  NamedRoleDivision(role)
//   okey   OpponentKey(uid, role, name)
//   rroll  RarityForRoll(roll)
//   rse    ChallengeCooldownActive(card fields, offer encounter, now, cooldown) -> o, then RecordSkarnChallengeEnd(card, offer, now)
//          -> skarnAttempted, skarnLastEnd after
//   rvic   RecordVictory(card, challenge, won, defeated) -> uniqueWins, challengeWins, SkarnUnlocked
//   draw   Draw(card) repeated from a seed
//   uio    UniqueEncountersInOffers(offers, division, exceptSlot)
//   key    TownArenaRuntimePolicy::PlayerSelectionKey
//   mso    MedicStandbyOffset(dx, dz) -> x, z
//   rip    RetainedIncidentPair; exa ExemptArenaIncident; iso IsolatePair
//   aft    AdvanceAftercare(elapsed, dt, paused), AftercareExpired(elapsed and result)
//   rdy    CanParticipate(r), CanProvideAid(r, medicallyAble)
//   ids    Equals(id, expected), IsNewArena, IsTownRegistry, IsRegularFighter, IsMedic over an id list (null included)
//   didx   DiscoveryIndex<Actor>::Add sequences: actor index (-1 = NULL) and key per step -> return, actors, byIdentity
//   rost   TownRosterSelection Add / Remove / Toggle / Prune sequences -> return, team after each step
//   furn   TownFurniturePolicy::Occupied
//   cat    TownFighterCatalog::Find(id) -> the entry's fields, or null
#include "TownChallengePolicy.h"
#include "TownArenaRuntimePolicy.h"
#include "TownRosterSelection.h"
#include "TownFurniturePolicy.h"
#include "TownFighterCatalog.h"
#include <cstdio>
#include <cstring>
#include <cmath>
#include <cfloat>
#include <climits>
#include <cstdint>
#include <limits>
#include <string>
#include <vector>
using namespace TownChallengePolicy;
namespace TA = TownArenaPolicy;
namespace TR = TownArenaRuntimePolicy;

static unsigned s = 919191u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }   // 24 bits
static unsigned R32() { unsigned a = R(); unsigned b = R(); return (a << 8) ^ b; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }
static unsigned long long Bits(double d) { unsigned long long u; std::memcpy(&u, &d, 8); return u; }

static void Str(const std::string& v)
{
  std::putchar('"');
  for (size_t i = 0; i < v.size(); ++i)
  {
    const unsigned char c = static_cast<unsigned char>(v[i]);
    if (c == '"' || c == '\\') { std::putchar('\\'); std::putchar(c); }
    else if (c < 0x20) std::printf("\\u%04x", c);
    else std::putchar(c);
  }
  std::putchar('"');
}
static void CStr(const char* v) { if (!v) std::printf("null"); else Str(v); }
// Strict UTF-8 (what .NET's throwing UTF8Encoding accepts): no overlong forms, no surrogates, nothing above U+10FFFF.
static bool ValidUtf8(const std::string& v)
{
  size_t i = 0;
  while (i < v.size())
  {
    const unsigned char c = static_cast<unsigned char>(v[i]);
    int n; unsigned cp;
    if (c < 0x80) { ++i; continue; }
    else if (c >= 0xc2 && c <= 0xdf) { n = 1; cp = c & 0x1f; }
    else if (c >= 0xe0 && c <= 0xef) { n = 2; cp = c & 0x0f; }
    else if (c >= 0xf0 && c <= 0xf4) { n = 3; cp = c & 0x07; }
    else return false;
    if (i + static_cast<size_t>(n) >= v.size()) return false;   // the sequence runs past the end
    for (int j = 1; j <= n; ++j)
    {
      const unsigned char d = static_cast<unsigned char>(v[i + j]);
      if ((d & 0xc0) != 0x80) return false;
      cp = (cp << 6) | (d & 0x3f);
    }
    if ((n == 2 && cp < 0x800) || (n == 3 && (cp < 0x10000 || cp > 0x10ffff)) || (cp >= 0xd800 && cp <= 0xdfff)) return false;
    i += n + 1;
  }
  return true;
}
static bool ValidOffer(const Offer& o)
{
  const std::string* f[] = { &o.opponent, &o.name, &o.secondOpponent, &o.secondName, &o.thirdOpponent, &o.thirdName, &o.fourthOpponent,
    &o.fourthName, &o.context, &o.roles[0], &o.roles[1], &o.roles[2], &o.roles[3] };
  for (const std::string* x : f) if (!ValidUtf8(*x)) return false;
  return true;
}
static bool ValidHistory(const std::vector<std::vector<std::string> >& h)
{
  for (size_t i = 0; i < h.size(); ++i) for (size_t j = 0; j < h[i].size(); ++j) if (!ValidUtf8(h[i][j])) return false;
  return true;
}
static bool ValidCardText(const Card& c)
{
  for (int i = 0; i < 5; ++i) if (!ValidOffer(c.offers[i])) return false;
  return ValidOffer(c.skarnOffer) && ValidHistory(c.playerHistory) && ValidHistory(c.ambientHistory);
}
static std::string HexBytes(const std::string& v)
{
  std::string r;
  for (size_t i = 0; i < v.size(); ++i) { char b[3]; std::snprintf(b, 3, "%02x", static_cast<unsigned char>(v[i])); r += b; }
  return r;
}

// An offer as one array (field order of the C++ struct):
// [epoch, encounter, format, mode, state, opponent, name, secondOpponent, secondName, thirdOpponent, thirdName, fourthOpponent,
//  fourthName, generated, draw, division, playerCount, enemyCount, context, roles[0], roles[1], roles[2], roles[3]]
static void PrintOffer(const Offer& o)
{
  std::printf("[%d,%d,%d,%d,%d,", o.epoch, o.encounter, o.format, o.mode, (int)o.state);
  Str(o.opponent); std::putchar(','); Str(o.name); std::putchar(','); Str(o.secondOpponent); std::putchar(','); Str(o.secondName);
  std::putchar(','); Str(o.thirdOpponent); std::putchar(','); Str(o.thirdName); std::putchar(','); Str(o.fourthOpponent);
  std::putchar(','); Str(o.fourthName);
  std::printf(",%d,%u,%d,%d,%d,", (int)o.generated, o.draw, o.division, o.playerCount, o.enemyCount);
  Str(o.context);
  for (int i = 0; i < 4; ++i) { std::putchar(','); Str(o.roles[i]); }
  std::printf("]");
}
static void PrintHistory(const std::vector<std::vector<std::string> >& h)
{
  std::putchar('[');
  for (size_t i = 0; i < h.size(); ++i)
  {
    if (i) std::putchar(',');
    std::putchar('[');
    for (size_t j = 0; j < h[i].size(); ++j) { if (j) std::putchar(','); Str(h[i][j]); }
    std::putchar(']');
  }
  std::putchar(']');
}
static void PrintCard(const Card& c)
{
  std::printf("{\"seed\":%u,\"as\":%u,\"div\":%d,\"ph\":", c.seed, c.ambientSeed, c.division);
  PrintHistory(c.playerHistory); std::printf(",\"ah\":"); PrintHistory(c.ambientHistory);
  std::printf(",\"mi\":%u,\"uw\":%u,\"cw\":%u,\"of\":[", c.milestones, c.uniqueWins, c.challengeWins);
  for (int i = 0; i < 5; ++i) { if (i) std::putchar(','); PrintOffer(c.offers[i]); }
  std::printf("],\"sk\":"); PrintOffer(c.skarnOffer);
  std::printf(",\"sg\":%d,\"sw\":%d,\"sa\":%d,\"sp\":%d,\"sleb\":%llu,\"lceb\":%llu,\"hce\":%d,\"prc\":%d,\"prn\":%u}",
              (int)c.skarnGenerated, (int)c.skarnWon, (int)c.skarnAttempted, (int)c.skarnInProgress, Bits(c.skarnLastEnd),
              Bits(c.lastChallengeEndHours), (int)c.hasChallengeEnd, c.paidRefreshCount, c.paidRefreshNonce);
}

// ---- random inputs ----
static const char* kPool[] = { "", "a", "b", "skarn", "lineup", "x y", "Ressa Vane", "Bum-Per", "\xc3\x9cn\xc3\xaf" "c\xc3\xb8" "d\xc3\xa9",
  "\xe5\x90\x8d\xe5\x89\x8d", "96-Proving Grounds.mod", "616-Proving Grounds.mod", "619-Proving Grounds.mod", "169-Proving Grounds.mod",
  "168-Proving Grounds.mod", "167-Proving Grounds.mod", "623-Proving Grounds.mod", "97-Proving Grounds.mod", "98-Proving Grounds.mod" };
static const int kPoolN = sizeof(kPool) / sizeof(kPool[0]);
static std::string P() { return R() % 3 == 0 ? std::string() : std::string(kPool[R() % kPoolN]); }
static const char* kRoles[] = { "", "96-Proving Grounds.mod", "97-Proving Grounds.mod", "98-Proving Grounds.mod", "169-Proving Grounds.mod",
  "168-Proving Grounds.mod", "167-Proving Grounds.mod", "616-Proving Grounds.mod", "619-Proving Grounds.mod", "623-Proving Grounds.mod",
  "100-Proving Grounds.mod", "616-Proving Grounds.mo", "616-proving grounds.mod" };
static const int kRolesN = sizeof(kRoles) / sizeof(kRoles[0]);
static int Epoch()
{
  static const int e[] = { -3, -2, -1, 0, 1, 5, 12, 13, 40, 1999999, 2000000 };
  return R() % 3 == 0 ? (int)(R() % 100) : e[R() % 11];
}
static Offer RandOffer()
{
  Offer o;
  if (R() % 10 == 0) return o;   // default-constructed
  o.epoch = Epoch();
  o.encounter = R() % 8 == 0 ? (int)(R() % 13) - 2 : (int)(R() % 9);
  o.format = (int)(R() % 9) - 1;
  o.mode = (int)(R() % 3);
  o.state = (TA::OfferState)(R() % 3);
  o.opponent = P(); o.name = P(); o.secondOpponent = P(); o.secondName = P();
  o.thirdOpponent = P(); o.thirdName = P(); o.fourthOpponent = P(); o.fourthName = P();
  o.generated = R() % 2 != 0;
  o.draw = R() % 5 == 0 ? R32() : R() % 12000;
  o.division = (int)(R() % 5) - 1;
  o.playerCount = (int)(R() % 6) - 1;
  o.enemyCount = (int)(R() % 7) - 1;
  o.context = P();
  for (int i = 0; i < 4; ++i) o.roles[i] = kRoles[R() % kRolesN];
  return o;
}
static std::vector<std::vector<std::string> > RandHistory(bool valid)
{
  std::vector<std::vector<std::string> > h;
  const int n = valid ? (int)(R() % 6) : (int)(R() % 7);
  for (int i = 0; i < n; ++i)
  {
    std::vector<std::string> bout;
    const int m = valid ? 1 + (int)(R() % 8) : (int)(R() % 10);
    for (int j = 0; j < m; ++j)
    {
      std::string id = valid ? std::string(kPool[1 + R() % (kPoolN - 1)]) + std::to_string(j) : P();
      bout.push_back(id);
    }
    h.push_back(bout);
  }
  return h;
}
static Card RandCard()
{
  Card c;
  if (R() % 12 == 0) return c;
  c.seed = R32();
  c.ambientSeed = R32();
  c.division = (int)(R() % 5) - 1;
  c.playerHistory = RandHistory(R() % 2 != 0);
  c.ambientHistory = RandHistory(R() % 2 != 0);
  c.milestones = R() % 10;
  c.uniqueWins = R() % 4 == 0 ? 31 : R() % 40;
  c.challengeWins = R() % 5 == 0 ? R32() : R() % 50;
  for (int i = 0; i < 5; ++i) c.offers[i] = RandOffer();
  c.skarnOffer = RandOffer();
  c.skarnGenerated = R() % 2; c.skarnWon = R() % 2; c.skarnAttempted = R() % 2; c.skarnInProgress = R() % 2;
  c.skarnLastEnd = R() % 4 == 0 ? -1.5 : (double)(R() % 2000000) / 7.0;
  c.lastChallengeEndHours = (double)(R() % 100000) / 3.0;
  c.hasChallengeEnd = R() % 2;
  c.paidRefreshCount = (int)(R() % 10);
  c.paidRefreshNonce = R32();
  return c;
}
// A card the compact codec accepts in most cases: generated offers with consistent previews.
static Card ValidCard()
{
  Card c;
  c.seed = R32(); c.ambientSeed = R32(); c.division = (int)(R() % 3);
  c.uniqueWins = R() % 3 == 0 ? 31 : R() % 32;
  c.milestones = R() % 8; c.challengeWins = R() % 5 == 0 ? 1000000000u : R() % 100;
  c.skarnWon = R() % 2;
  RefreshGenerated(c, (int)(R() % 3000), R() % 3 == 0 ? (int)(R() % 7) - 1 : -1);
  for (int i = 0; i < 5; ++i)
  {
    Offer& o = c.offers[i];
    if (R() % 4 == 0) o.generated = false;
    if (o.generated && R() % 3 != 0)
    {
      o.playerCount = 1 + (int)(R() % 3);
      o.enemyCount = 1 + (int)(R() % 4);
      o.context = R() % 2 ? "lineup" : "ctx \xc3\xa9";
      for (int j = 0; j < o.enemyCount; ++j)
      {
        OpponentId(o, j) = std::string("id") + std::to_string(j) + (R() % 2 ? "\xe5\x90\x8d" : "");
        OpponentName(o, j) = R() % 3 ? std::string(kPool[R() % kPoolN]) : std::string();
        o.roles[j] = kRoles[1 + R() % (kRolesN - 1)];
      }
      o.state = (TA::OfferState)(R() % 3);
    }
    else if (!o.generated)
    {
      o.state = (TA::OfferState)(R() % 3);
      o.format = (int)(R() % 6);
      if (R() % 2) { o.opponent = "legacy1"; o.name = "Leg One"; }
      if (Opponents(o) >= 2 && R() % 2) { o.secondOpponent = "legacy2"; o.secondName = "Leg Two"; }
      if (Opponents(o) >= 3 && R() % 2) { o.thirdOpponent = "legacy3"; o.thirdName = "Leg Three"; }
    }
  }
  if (R() % 3 == 0)
  {
    c.skarnGenerated = R() % 2;
    c.skarnOffer.encounter = c.skarnGenerated ? 5 : (int)(R() % 6);
    c.skarnOffer.format = (int)(R() % 7);
    c.skarnOffer.epoch = Epoch();
    if (c.skarnOffer.epoch < -2 || c.skarnOffer.epoch > 1999999) c.skarnOffer.epoch = 3;
    c.skarnOffer.state = (TA::OfferState)(R() % 3);
    if (R() % 2) { c.skarnOffer.opponent = "sk1"; c.skarnOffer.name = "Skarn"; }
  }
  c.skarnAttempted = R() % 2; c.skarnInProgress = R() % 2;
  c.skarnLastEnd = R() % 3 == 0 ? 0.0 : (double)R32() / 179.0 + 0.1;
  if (c.skarnLastEnd > 24000000.0) c.skarnLastEnd = 24000000.0;
  c.playerHistory = RandHistory(true); c.ambientHistory = RandHistory(true);
  for (int i = 0; i < 5; ++i) c.offers[i].mode = (int)(R() % 2);
  return c;
}

static std::vector<std::string> Tokens(const std::string& t)
{
  std::vector<std::string> r; std::string cur;
  for (size_t i = 0; i < t.size(); ++i)
  {
    if (t[i] == ' ') { r.push_back(cur); cur.clear(); } else cur += t[i];
  }
  r.push_back(cur);
  return r;
}
static std::string Join(const std::vector<std::string>& v)
{
  std::string r;
  for (size_t i = 0; i < v.size(); ++i) { if (i) r += ' '; r += v[i]; }
  return r;
}
static const char* kBad[] = { "-1", "0", "1", "2", "3", "4", "5", "6", "7", "9", "10000", "-3", "-2", "1999999", "2000000",
  "x", "-", "zz", "41", "7a7a", "c3a9", "00", "1.5", "04294967296", "04294967295", "+4294967295", "2147483648", "-2147483649",
  "1e3", "+1", "1000000001", "32", "31", "8", "24000000", "24000001", "-0", "abc", "A1", "09999", "-4294967295" };
// Every even-length all-hex token above decodes to valid UTF-8 (4294967295 or 9999 as hex would be 0x94/0x99 bytes, which the port
// refuses as text: they are written with a leading 0 or + instead, so they stay odd-length or non-hex and keep their numeric value).
static const int kBadN = sizeof(kBad) / sizeof(kBad[0]);

static void Dec(const char* kind, const std::string& text)
{
  Card out;
  const bool ok = std::strcmp(kind, "decl") == 0 ? DecodeLegacy(text, out) : Decode(text, out);
  std::printf("%s{\"k\":\"%s\",\"t\":", Sep(), kind); Str(text);
  // inv: the C++ accepted bytes that are not valid UTF-8; the port refuses those (docs/divergences.md), so its expected ok is 0
  const bool inv = ok && !ValidCardText(out);
  std::printf(",\"ok\":%d,\"inv\":%d,\"c\":", (int)ok, (int)inv);
  if (ok && !inv) PrintCard(out); else std::printf("null");   // not ok: the target is untouched (still Card())
  std::printf("}\n");
}

// Hand-written legacy text of a version 1..4 card (the shapes DecodeLegacy reads).
static std::string LegacyText(int version)
{
  std::string t = std::to_string(version) + " " + std::to_string(R32());
  if (version >= 2) t += " " + std::to_string(R() % 9) + " " + std::to_string(R() % 20);
  if (version >= 4)
    t += std::string(" ") + (R() % 2 ? "1" : "0") + " " + (R() % 2 ? "1" : "0") + " " + (R() % 2 ? "1" : "0") + " " +
         (R() % 2 ? "1" : "0") + " " + std::to_string(R() % 3000) + "." + std::to_string(R() % 100);
  const int offers = version >= 4 ? 6 : 5;
  for (int i = 0; i < offers; ++i)
  {
    int enc = (int)(R() % (i == 5 ? 6 : 5));
    t += " " + std::to_string(Epoch() % 200) + " " + std::to_string(enc) + " " + std::to_string(R() % 3);
    t += " " + Hex(R() % 2 ? "id" + std::to_string(i) : "") + " " + Hex(R() % 2 ? "Name" : "");
    if (version >= 3)
    {
      int fmt = (int)(R() % (i == 5 ? 7 : 6));
      t += " " + std::to_string(fmt);
      Offer probe; probe.format = fmt;
      const bool two = Opponents(probe) >= 2;
      t += " " + Hex(two && R() % 2 ? "second" + std::to_string(i) : "") + " " + Hex(two && R() % 2 ? "Second" : "");
      if (version >= 4)
      {
        const bool three = Opponents(probe) >= 3;
        t += " " + Hex(three && R() % 2 ? "third" + std::to_string(i) : "") + " " + Hex(three && R() % 2 ? "Third" : "");
      }
    }
  }
  return t;
}

struct FakePrep { bool ready; int cancels; bool Ready() const { return ready; } void Cancel() { ++cancels; } };
struct Dummy { int index; };

int main()
{
  std::printf("[\n");
  // ---- epochs ----
  {
    static const int refreshes[] = { 1, 12, 24, 0, 169, -1, 168, 7 };
    std::vector<double> hours;
    for (int h = 0; h <= 500; ++h) hours.push_back((double)h);
    for (int i = 0; i < 300; ++i) hours.push_back((double)(R() % 50000) / 97.0);
    const double extra[] = { -1.0, -0.0, -1e-300, 5.999999999, 6.0, 6.000000001, 17.9999999, 18.0, 24000000.0, 24000000.000000004,
      24000001.0, 23999999.5, 1e300, std::numeric_limits<double>::infinity(), -std::numeric_limits<double>::infinity(),
      std::numeric_limits<double>::quiet_NaN(), std::numeric_limits<double>::denorm_min(), 0.5, 5.5 };
    for (double e : extra) hours.push_back(e);
    // one case per refresh value; hb[i] -> ce[i], cce[i], hu[i]
    for (int r : refreshes)
    {
      std::printf("%s{\"k\":\"ep\",\"r\":%d,\"hb\":[", Sep(), r);
      for (size_t i = 0; i < hours.size(); ++i) std::printf("%s%llu", i ? "," : "", Bits(hours[i]));
      std::printf("],\"ce\":[");
      for (size_t i = 0; i < hours.size(); ++i) std::printf("%s%d", i ? "," : "", TA::CardEpoch(hours[i]));
      std::printf("],\"cce\":[");
      for (size_t i = 0; i < hours.size(); ++i) std::printf("%s%d", i ? "," : "", TA::ChallengeCardEpoch(hours[i], r));
      std::printf("],\"hu\":[");
      for (size_t i = 0; i < hours.size(); ++i) std::printf("%s%llu", i ? "," : "", Bits(TA::HoursUntilChallengeRefresh(hours[i], r)));
      std::printf("]}\n");
    }
  }
  // ---- paid refresh cost ----
  {
    static const int bases[] = { -5, 0, 1, 2, 500, 1000, 12345, 1000000, 2000000000, INT_MAX };
    static const double mults[] = { 0.5, 0.999, 1.0, 1.0000001, 1.5, 2.0, 2.5, 3.3, 9.99, 10.0, 10.0001, 11.0, -2.0,
      std::numeric_limits<double>::quiet_NaN(), std::numeric_limits<double>::infinity() };
    // one case per (base, multiplier); o[i] is the cost for count n0 + i
    for (int b : bases) for (double m : mults)
    {
      std::printf("%s{\"k\":\"cost\",\"b\":%d,\"mb\":%llu,\"n0\":-2,\"o\":[", Sep(), b, Bits(m));
      for (int c = -2; c <= 40; ++c) std::printf("%s%d", c > -2 ? "," : "", TA::PaidRefreshCost(b, m, c));
      std::printf("]}\n");
    }
  }
  // ---- event selection ----
  for (int m = 0; m < 8; ++m)
  {
    std::printf("%s{\"k\":\"sel\",\"a\":%d,\"b\":%d,\"c\":%d,\"o\":%d}\n", Sep(), m & 1, (m >> 1) & 1, (m >> 2) & 1,
                (int)TA::SelectNext(m & 1, (m >> 1) & 1, (m >> 2) & 1));
    std::printf("%s{\"k\":\"disc\",\"a\":%d,\"b\":%d,\"c\":%d,\"o\":%d}\n", Sep(), m & 1, (m >> 1) & 1, (m >> 2) & 1,
                (int)TA::ShouldDiscoverRegistry(m & 1, (m >> 1) & 1, (m >> 2) & 1));
  }
  {
    const double times[] = { 0.0, 1.0, 5.0, 10.0, 10.0000001, -1.0, -0.0, DBL_MAX, std::numeric_limits<double>::infinity(),
      std::numeric_limits<double>::quiet_NaN(), 9.9999999 };
    for (int m = 0; m < 16; ++m) for (double now : times) for (double due : times)
      std::printf("%s{\"k\":\"amb\",\"m\":%d,\"nb\":%llu,\"db\":%llu,\"o\":%d}\n", Sep(), m, Bits(now), Bits(due),
                  (int)TA::AmbientNext(m & 1, (m >> 1) & 1, (m >> 2) & 1, (m >> 3) & 1, now, due));
  }
  for (int r = -3; r <= 12; ++r)
  {
    const unsigned rolls[] = { 0u, 1u, 2u, 3u, 4u, 5u, 7u, 11u, 4294967295u, 4294967294u, R32(), R32() };
    for (unsigned roll : rolls)
      std::printf("%s{\"k\":\"team\",\"n\":%d,\"roll\":%u,\"o\":%d}\n", Sep(), r, roll, TA::SelectNpcTeamSize(r, roll));
  }
  for (int st = 0; st < 4; ++st)
  {
    TA::OfferState a = (TA::OfferState)st, b = (TA::OfferState)st, c = (TA::OfferState)st;
    const bool book = TA::BookOffer(a), begin = TA::BeginOffer(b);
    TA::CancelBooking(c);
    std::printf("%s{\"k\":\"ost\",\"s\":%d,\"book\":%d,\"bs\":%d,\"begin\":%d,\"gs\":%d,\"cs\":%d}\n", Sep(), st, (int)book, (int)a,
                (int)begin, (int)b, (int)c);
  }
  // ---- defaults ----
  {
    std::printf("%s{\"k\":\"dflt\",\"o\":", Sep()); PrintOffer(Offer()); std::printf(",\"c\":"); PrintCard(Card());
    std::printf(",\"per\":%d}\n", TA::kOffersPerCard);
  }
  // ---- RefreshGenerated ----
  {
    const int epochs[] = { -3, -2, -1, 0, 1, 12, 13, 500, 1999999, 2000000 };
    for (int i = 0; i < 150; ++i)
    {
      Card c = R() % 3 == 0 ? Card() : RandCard();
      if (R() % 4 == 0) c.seed = R() % 20;
      if (R() % 2) c.division = (int)(R() % 3);
      if (R() % 3 == 0) c.uniqueWins = R() % 32;
      const int epoch = R() % 4 == 0 ? (int)(R() % 3000) : epochs[R() % 10];
      if (R() % 3 == 0) c.offers[R() % 5].epoch = epoch;
      if (R() % 4 == 0) c.offers[R() % 5].epoch = epoch;
      const int pinned = (int)(R() % 8) - 2;
      std::printf("%s{\"k\":\"refr\",\"in\":", Sep()); PrintCard(c);
      RefreshGenerated(c, epoch, pinned);
      std::printf(",\"e\":%d,\"p\":%d,\"out\":", epoch, pinned); PrintCard(c); std::printf("}\n");
    }
    // the upstream test's two cards, then a chain of refreshes on one card
    Card chain;
    for (int e = -2; e < 40; ++e)
    {
      std::printf("%s{\"k\":\"refr\",\"in\":", Sep()); PrintCard(chain);
      const int pinned = e % 7 == 0 ? (int)(e % 5) : -1;
      RefreshGenerated(chain, e, pinned);
      std::printf(",\"e\":%d,\"p\":%d,\"out\":", e, pinned); PrintCard(chain); std::printf("}\n");
      if (e % 9 == 0) chain.uniqueWins = (chain.uniqueWins * 3 + 7) & 31;
      if (e % 11 == 0) chain.skarnWon = !chain.skarnWon;
      chain.division = (e + 3) % 3;
    }
  }
  // ---- SetDivision ----
  for (int i = 0; i < 150; ++i)
  {
    Card c = R() % 3 == 0 ? ValidCard() : RandCard();
    const int division = (int)(R() % 6) - 2;
    const bool locked = R() % 5 == 0;
    std::printf("%s{\"k\":\"sdiv\",\"in\":", Sep()); PrintCard(c);
    const bool ok = SetDivision(c, division, locked);
    std::printf(",\"d\":%d,\"l\":%d,\"ok\":%d,\"out\":", division, (int)locked, (int)ok); PrintCard(c); std::printf("}\n");
  }
  // ---- OfferRarity ----
  {
    Offer o; o.generated = true;
    std::printf("%s{\"k\":\"rar\",\"r\":[", Sep());
    for (unsigned d = 0; d < 10000; ++d) { o.draw = d; std::printf("%s%d", d ? "," : "", OfferRarity(o, (int)(d % 3))); }
    std::printf("]}\n");
    const unsigned bigDraws[] = { 10000u, 12345u, 4294967295u, 3999u, 4000u, 6499u, 6500u, 8499u, 8500u };
    for (unsigned d : bigDraws) { o.draw = d; std::printf("%s{\"k\":\"rarg\",\"d\":%u,\"o\":%d}\n", Sep(), d, OfferRarity(o, 2)); }
    for (int e = -3; e <= 10; ++e) for (int div = -1; div <= 3; ++div) for (unsigned d = 0; d < 10000; d += 4999)
    {
      Offer n; n.encounter = e; n.draw = d;
      std::printf("%s{\"k\":\"orn\",\"e\":%d,\"div\":%d,\"d\":%u,\"o\":%d}\n", Sep(), e, div, d, OfferRarity(n, div));
    }
  }
  // ---- LegendaryEncounter ----
  {
    const unsigned regulars[] = { 0u, 1u, 2u, 5u };
    for (int div = -1; div <= 3; ++div) for (int su = 0; su < 2; ++su) for (int sw = 0; sw < 2; ++sw)
      for (unsigned rep = 0; rep < 4; ++rep) for (unsigned reg : regulars)
      {
        std::printf("%s{\"k\":\"l7\",\"div\":%d,\"su\":%d,\"sw\":%d,\"rep\":%u,\"reg\":%u,\"r\":[", Sep(), div, su, sw, rep, reg);
        int n = 0;
        for (unsigned uw = 0; uw < 32; ++uw) for (unsigned roll = 0; roll < 16; ++roll)
          std::printf("%s%d", n++ ? "," : "", LegendaryEncounter(div, roll, rep, reg, uw, su, sw));
        std::printf("]}\n");
      }
    for (int i = 0; i < 2000; ++i)
    {
      const int div = (int)(R() % 5) - 1;
      const unsigned a = R32(), b = R32(), c = R32(), uw = R() % 2 ? R32() : R() % 64;
      const bool su = R() % 2, sw = R() % 2;
      std::printf("%s{\"k\":\"l7r\",\"div\":%d,\"a\":%u,\"b\":%u,\"c\":%u,\"uw\":%u,\"su\":%d,\"sw\":%d,\"o\":%d}\n", Sep(), div, a, b, c,
                  uw, (int)su, (int)sw, LegendaryEncounter(div, a, b, c, uw, su, sw));
    }
    for (int div = -1; div <= 3; ++div) for (unsigned roll = 0; roll < 16; ++roll) for (int su = 0; su < 2; ++su)
      std::printf("%s{\"k\":\"l3\",\"div\":%d,\"roll\":%u,\"su\":%d,\"o\":%d}\n", Sep(), div, roll, su, LegendaryEncounter(div, roll, su));
    for (int i = 0; i < 50; ++i)
    {
      const unsigned roll = R32(); const int div = (int)(R() % 3); const bool su = R() % 2;
      std::printf("%s{\"k\":\"l3\",\"div\":%d,\"roll\":%u,\"su\":%d,\"o\":%d}\n", Sep(), div, roll, (int)su, LegendaryEncounter(div, roll, su));
    }
  }
  // ---- EncounterForDivision ----
  for (int i = 0; i < 800; ++i)
  {
    Offer o = RandOffer();
    if (R() % 2) o.generated = true;
    if (R() % 3 == 0) o.draw = 8500 + R() % 1500;
    if (R() % 10 == 0) o.epoch = (int)R32();
    const int slot = (int)(R() % 8) - 1;
    const int div = (int)(R() % 5) - 1;
    const unsigned uw = R() % 6 == 0 ? R32() : R() % 32;
    const bool su = R() % 2, sw = R() % 2;
    std::printf("%s{\"k\":\"efd\",\"o\":", Sep()); PrintOffer(o);
    std::printf(",\"slot\":%d,\"div\":%d,\"uw\":%u,\"su\":%d,\"sw\":%d,\"r\":%d}\n", slot, div, uw, (int)su, (int)sw,
                EncounterForDivision(o, slot, div, uw, su, sw));
  }
  // ---- PreviewSource, CanBookDivision ----
  for (int i = 0; i < 110; ++i)
  {
    Card c = R() % 3 == 0 ? ValidCard() : RandCard();
    if (R() % 2)
      for (int j = 0; j < 5; ++j) if (R() % 2) { c.offers[j].generated = R() % 2; c.offers[j].encounter = 3 + (int)(R() % 6); }
    if (R() % 3 == 0) c.division = (int)(R() % 3);
    std::printf("%s{\"k\":\"prev\",\"c\":", Sep()); PrintCard(c); std::printf(",\"r\":[");
    for (int slot = -1; slot <= 6; ++slot) { if (slot > -1) std::putchar(','); PrintOffer(PreviewSource(c, slot)); }
    std::printf("],\"bk\":[");
    for (int slot = -1; slot <= 6; ++slot) std::printf("%s%d", slot > -1 ? "," : "", (int)CanBookDivision(c, slot));
    std::printf("],\"vis\":[");
    for (int slot = -1; slot <= 6; ++slot) std::printf("%s%d", slot > -1 ? "," : "", (int)Visible(c, slot));
    std::printf("]}\n");
  }
  // ---- UnavailableReason ----
  for (int i = 0; i < 250; ++i)
  {
    Offer o = RandOffer();
    if (R() % 2) { o.generated = true; o.draw = 8500 + R() % 1500; }
    if (R() % 3 == 0) o.encounter = 3 + (int)(R() % 6);
    std::printf("%s{\"k\":\"why\",\"o\":", Sep()); PrintOffer(o); std::printf(",\"r\":[");
    for (int m = 0; m < 8; ++m) { if (m) std::putchar(','); Str(UnavailableReason(o, m & 1, (m >> 1) & 1, (m >> 2) & 1)); }
    std::printf("]}\n");
  }
  // ---- Hex / Unhex ----
  {
    for (int i = 0; i < kPoolN; ++i)
    {
      std::printf("%s{\"k\":\"hex\",\"s\":", Sep()); Str(kPool[i]); std::printf(",\"o\":"); Str(Hex(kPool[i])); std::printf("}\n");
    }
    std::string nul("a\0b", 3);
    std::printf("%s{\"k\":\"hex\",\"s\":", Sep()); Str(nul); std::printf(",\"o\":"); Str(Hex(nul)); std::printf("}\n");
    std::vector<std::string> inputs = { "-", "", "41", "4", "414", "4142", "zz", "4G", "4A", "c3a9", "e5908de5898d", "00", "ff", "c3",
      "--", "- ", "0a0d", "7f", std::string(2048, '6'), std::string(2050, '6'), std::string(2046, '7') + "41", "+1", " 41" };
    for (const std::string& in : inputs)
    {
      std::string out = "junk";
      const bool ok = Unhex(in, out);
      std::printf("%s{\"k\":\"unhex\",\"s\":", Sep()); Str(in); std::printf(",\"ok\":%d,\"inv\":%d,\"o\":", (int)ok, (int)(ok && !ValidUtf8(out)));
      Str(HexBytes(out));
      std::printf("}\n");
    }
  }
  // ---- Encode / Decode ----
  std::vector<std::string> encodings;
  {
    std::vector<Card> cards;
    cards.push_back(Card());
    for (int i = 0; i < 90; ++i) cards.push_back(ValidCard());
    for (int i = 0; i < 25; ++i) cards.push_back(RandCard());
    // The upstream test's lockedSkarn / unlockedSkarn cards
    Card locked; locked.division = 2; locked.offers[0].generated = true; locked.offers[0].division = 2; locked.offers[0].encounter = 5;
    locked.offers[0].epoch = 1; locked.offers[0].playerCount = 1; locked.offers[0].enemyCount = 1; locked.offers[0].context = "lineup";
    locked.offers[0].opponent = "skarn"; locked.offers[0].roles[0] = Role(5);
    cards.push_back(locked);
    Card unlocked = locked; unlocked.uniqueWins = AllUniqueVictories; cards.push_back(unlocked);
    // a name of 1024 bytes (2048 hex) is accepted, one byte longer is not
    Card longName; longName.offers[1].opponent = std::string(1024, 'n'); cards.push_back(longName);
    Card tooLong; tooLong.offers[1].opponent = std::string(1025, 'n'); cards.push_back(tooLong);
    for (const Card& c : cards)
    {
      const std::string e = Encode(c);
      encodings.push_back(e);
      Card out;
      const bool ok = Decode(e, out);
      std::printf("%s{\"k\":\"enc\",\"c\":", Sep()); PrintCard(c);
      std::printf(",\"e\":"); Str(e); std::printf(",\"el\":"); Str(EncodeLegacy(c));
      std::printf(",\"ok\":%d,\"d\":", (int)ok);
      if (ok) PrintCard(out); else std::printf("null");
      std::printf("}\n");
    }
  }
  // fuzz: one token replaced, removed, duplicated; the stream cut; extra token; other version prefixes
  for (int i = 0; i < 700; ++i)
  {
    std::vector<std::string> t = Tokens(encodings[R() % encodings.size()]);
    const unsigned op = R() % 10;
    const size_t at = R() % t.size();
    if (op < 5) t[at] = kBad[R() % kBadN];
    else if (op == 5) t.erase(t.begin() + at);
    else if (op == 6) t.insert(t.begin() + at, t[at]);
    else if (op == 7) t.resize(at);
    else if (op == 8) t.push_back(kBad[R() % kBadN]);
    else t[0] = std::to_string(R() % 10);
    std::string text = Join(t);
    if (R() % 15 == 0) text += R() % 2 ? "   " : "\t\n";
    if (R() % 25 == 0) { const size_t sp = text.find(' ', R() % text.size()); if (sp != std::string::npos) text.insert(sp, R() % 2 ? "\t" : "  "); }
    Dec("dec", text);
  }
  {
    const std::string targeted[] = { "", "8", "8 ", "8 5", " MM ", "8 MM ", "5 1 MM 0 0", "9 1", "0 1", "1", "1 2", "6 ", "7 x",
      "8  5 1 0 0 0 0 0 0 0 0 -2 0 0 - - 0 - - - - -2 0 0 - - 0 - - - - -2 0 0 - - 0 - - - - -2 0 0 - - 0 - - - - -2 0 0 - - 0 - - - - -2 0 0 - - 0 - - - - MM 0 1",
      std::string(100001, '8'), "8 " + std::string(100000, ' ') };
    for (const std::string& t : targeted) Dec("dec", t);
  }
  // DecodeLegacy directly and through Decode: hand-written versions 1..4, the current version 5, and their fuzz
  {
    std::vector<std::string> texts;
    for (int i = 0; i < 120; ++i) texts.push_back(LegacyText(1 + (int)(R() % 4)));
    for (int i = 0; i < 30; ++i) texts.push_back(EncodeLegacy(i % 3 == 0 ? RandCard() : ValidCard()));
    // the upstream test's version-4 migration text
    const std::string current = EncodeLegacy(Card());
    const size_t f1 = current.find(' '), f2 = current.find(' ', f1 + 1);
    const size_t f3 = current.find(' ', f2 + 1), f4 = current.find(' ', f3 + 1);
    texts.push_back("4" + current.substr(f1, f3 - f1) + current.substr(f4));
    for (const std::string& t : texts) { Dec("decl", t); Dec("dec", t); }
    for (int i = 0; i < 900; ++i)
    {
      std::vector<std::string> t = Tokens(texts[R() % texts.size()]);
      const unsigned op = R() % 8;
      const size_t at = R() % t.size();
      if (op < 5) t[at] = kBad[R() % kBadN];
      else if (op == 5) t.erase(t.begin() + at);
      else if (op == 6) t.resize(at);
      else t.push_back(kBad[R() % kBadN]);
      Dec("decl", Join(t));
    }
  }
  // ---- WriteHistory / ReadHistory ----
  for (int i = 0; i < 300; ++i)
  {
    std::vector<std::vector<std::string> > h = RandHistory(R() % 3 != 0);
    std::ostringstream out; WriteHistory(out, h);
    std::printf("%s{\"k\":\"hist\",\"h\":", Sep()); PrintHistory(h); std::printf(",\"t\":"); Str(out.str()); std::printf("}\n");
    std::string text = out.str();
    if (R() % 3 == 0)
    {
      std::vector<std::string> t = Tokens(text);
      t[R() % t.size()] = kBad[R() % kBadN];
      text = Join(t);
    }
    if (R() % 4 == 0) text += " 41 tail";
    std::istringstream in(text);
    std::vector<std::vector<std::string> > read;
    if (R() % 5 == 0) read.push_back(std::vector<std::string>(1, "pre"));   // ReadHistory appends to what is there
    std::printf("%s{\"k\":\"rhist\",\"t\":", Sep()); Str(text);
    std::printf(",\"pre\":%d", (int)read.size());
    const bool ok = ReadHistory(in, read);
    std::string rest; std::getline(in, rest, '\x01');
    const bool inv = ok && !ValidHistory(read);
    std::printf(",\"ok\":%d,\"inv\":%d,\"h\":", (int)ok, (int)inv); if (inv) std::printf("null"); else PrintHistory(read); std::printf(",\"rest\":"); Str(ok ? rest : std::string()); std::printf("}\n");
  }
  // ---- small helpers ----
  for (int i = 0; i < 600; ++i)
  {
    Offer o = RandOffer();
    Offer cleared = o; ClearPreview(cleared);
    std::printf("%s{\"k\":\"hlp\",\"o\":", Sep()); PrintOffer(o);
    std::printf(",\"pl\":%d,\"op\":%d,\"sr\":%d,\"res\":%d,\"or\":[%d,%d,%d,%d,%d],\"cl\":", Players(o), Opponents(o), SecondRole(o),
                (int)Resolved(o), OpponentRole(o, 0), OpponentRole(o, 1), OpponentRole(o, 2), OpponentRole(o, 3), OpponentRole(o, -1));
    PrintOffer(cleared); std::printf("}\n");
  }
  for (int i = 0; i < 500; ++i)
  {
    Offer a = RandOffer(), b = R() % 4 == 0 ? a : RandOffer();
    if (R() % 2) { b.format = a.format; b.generated = a.generated; b.playerCount = a.playerCount; b.enemyCount = a.enemyCount; }
    std::printf("%s{\"k\":\"same\",\"a\":", Sep()); PrintOffer(a); std::printf(",\"b\":"); PrintOffer(b);
    std::printf(",\"o\":%d}\n", (int)SameEncounter(a, b));
  }
  for (int i = 0; i < 300; ++i)
  {
    Offer source = RandOffer(), preview = RandOffer(), reservation = RandOffer();
    if (R() % 2) preview.epoch = source.epoch;
    if (R() % 2) source.state = TA::OfferAvailable;
    std::printf("%s{\"k\":\"res\",\"s\":", Sep()); PrintOffer(source); std::printf(",\"p\":"); PrintOffer(preview);
    std::printf(",\"r\":"); PrintOffer(reservation);
    const bool ok = ReserveOffer(source, preview, reservation);
    std::printf(",\"ok\":%d,\"so\":", (int)ok); PrintOffer(source); std::printf(",\"ro\":"); PrintOffer(reservation); std::printf("}\n");
  }
  for (int e = -3; e <= 11; ++e)
  {
    std::printf("%s{\"k\":\"names\",\"e\":%d,\"role\":", Sep(), e); Str(Role(e));
    std::printf(",\"rar\":"); Str(Rarity(e)); std::printf(",\"title\":"); Str(Title(e)); std::printf(",\"mode\":"); Str(ModeName(e));
    std::printf(",\"rn\":"); Str(RarityName(e)); std::printf(",\"dn\":"); Str(DivisionName(e));
    std::printf(",\"nd\":%d,\"uv\":%u,\"ue\":%d,\"nfd\":[%d,%d,%d,%d,%d]}\n", NamedDivision(e), UniqueVictoryForEncounter(e),
                (int)UniqueEncounter(e), (int)NamedForDivision(e, -1), (int)NamedForDivision(e, 0), (int)NamedForDivision(e, 1),
                (int)NamedForDivision(e, 2), (int)NamedForDivision(e, 3));
  }
  for (int i = 0; i < kRolesN; ++i)
  {
    std::printf("%s{\"k\":\"nrole\",\"s\":", Sep()); Str(kRoles[i]); std::printf(",\"o\":%d}\n", NamedRoleDivision(kRoles[i]));
  }
  {
    const char* parts[] = { "", "u1", "96-Proving Grounds.mod", "Na me" };
    for (const char* u : parts) for (const char* r : parts) for (const char* n : parts)
    {
      std::printf("%s{\"k\":\"okey\",\"u\":", Sep()); Str(u); std::printf(",\"r\":"); Str(r); std::printf(",\"n\":"); Str(n);
      std::printf(",\"o\":"); Str(OpponentKey(u, r, n)); std::printf("}\n");
    }
  }
  for (unsigned roll = 0; roll <= 120; ++roll) std::printf("%s{\"k\":\"rroll\",\"r\":%u,\"o\":%d}\n", Sep(), roll, RarityForRoll(roll));
  std::printf("%s{\"k\":\"rroll\",\"r\":%u,\"o\":%d}\n", Sep(), 4294967295u, RarityForRoll(4294967295u));
  for (int i = 0; i < 1200; ++i)
  {
    Card c;
    c.skarnAttempted = R() % 2;
    c.skarnLastEnd = R() % 5 == 0 ? -3.0 : (double)(R() % 1000) / 4.0;
    const bool sa = c.skarnAttempted; const double sle = c.skarnLastEnd;
    Offer o; o.encounter = R() % 3 == 0 ? (int)(R() % 9) : 5;
    const double now = R() % 9 == 0 ? (R() % 2 ? std::numeric_limits<double>::quiet_NaN() : -2.0) :
                       R() % 9 == 0 ? 24000000.5 : R() % 9 == 0 ? 24000000.0 : sle + (double)((int)(R() % 200) - 50) / 4.0;
    const int cooldown = R() % 6 == 0 ? (int)(R() % 3) - 1 : 24;
    const bool active = ChallengeCooldownActive(c, o, now, cooldown);
    RecordSkarnChallengeEnd(c, o, now);
    std::printf("%s{\"k\":\"rse\",\"sa\":%d,\"sleb\":%llu,\"e\":%d,\"nb\":%llu,\"cd\":%d,\"o\":%d,\"sa2\":%d,\"sle2b\":%llu}\n", Sep(),
                (int)sa, Bits(sle), o.encounter, Bits(now), cooldown, (int)active, (int)c.skarnAttempted, Bits(c.skarnLastEnd));
  }
  for (int i = 0; i < 800; ++i)
  {
    Card c;
    c.uniqueWins = R() % 3 == 0 ? R32() : R() % 32;
    c.challengeWins = R() % 4 == 0 ? 999999999u + R() % 3 : R() % 4 == 0 ? R32() : R() % 30;
    const unsigned uw = c.uniqueWins, cw = c.challengeWins;
    const bool challenge = R() % 2, won = R() % 4 != 0;
    const unsigned defeated = R() % 3 == 0 ? R32() : R() % 64;
    RecordVictory(c, challenge, won, defeated);
    std::printf("%s{\"k\":\"rvic\",\"uw\":%u,\"cw\":%u,\"ch\":%d,\"won\":%d,\"def\":%u,\"uw2\":%u,\"cw2\":%u,\"su\":%d,\"su0\":%d}\n", Sep(),
                uw, cw, (int)challenge, (int)won, defeated, c.uniqueWins, c.challengeWins, (int)SkarnUnlocked(c),
                (int)((uw & AllUniqueVictories) == AllUniqueVictories));
  }
  {
    const unsigned seeds[] = { 0u, 1u, 7u, 4294967295u, 2463534242u };
    for (unsigned seed : seeds)
    {
      Card c; c.seed = seed;
      std::printf("%s{\"k\":\"draw\",\"s\":%u,\"r\":[", Sep(), seed);
      for (int i = 0; i < 20; ++i) std::printf("%s%u", i ? "," : "", Draw(c));
      std::printf("],\"after\":%u}\n", c.seed);
    }
  }
  for (int i = 0; i < 250; ++i)
  {
    Card c;
    for (int j = 0; j < 5; ++j)
    {
      c.offers[j] = RandOffer();
      if (R() % 2) { c.offers[j].generated = true; c.offers[j].draw = 8500 + R() % 1500; c.offers[j].encounter = 3 + (int)(R() % 6); }
    }
    const int div = (int)(R() % 5) - 1, except = (int)(R() % 8) - 2;
    std::printf("%s{\"k\":\"uio\",\"of\":[", Sep());
    for (int j = 0; j < 5; ++j) { if (j) std::putchar(','); PrintOffer(c.offers[j]); }
    std::printf("],\"div\":%d,\"ex\":%d,\"o\":%d}\n", div, except, UniqueEncountersInOffers(c.offers, div, except));
  }
  // ---- TownArenaRuntimePolicy ----
  for (int i = 0; i < 300; ++i)
  {
    const unsigned a = R() % 3 ? R() % 100 : R32(), b = R() % 3 ? R() % 100 : R32(), c = R32(), d = R() % 5 ? R() % 10 : 4294967295u, e = R32();
    std::printf("%s{\"k\":\"key\",\"a\":[%u,%u,%u,%u,%u],\"o\":", Sep(), a, b, c, d, e); Str(TR::PlayerSelectionKey(a, b, c, d, e));
    std::printf("}\n");
  }
  {
    std::vector<std::pair<float, float> > d = { {0, 0}, {1, 0}, {0, 1}, {0.7f, 0.7f}, {0.70710677f, 0.70710677f}, {0.70710683f, 0.70710683f},
      {-1, 0}, {1.0000001f, 0}, {-3, 4}, {300, -400}, {1e-20f, 1e-20f}, {1e20f, 1e20f}, {1e30f, 1e30f}, {-0.0f, -5.0f} };
    for (int i = 0; i < 400; ++i)
      d.push_back({ (float)((int)(R() % 4001) - 2000) / 7.0f, (float)((int)(R() % 4001) - 2000) / 13.0f });
    for (auto& p : d)
    {
      float x = 99, z = 99;
      TR::MedicStandbyOffset(p.first, p.second, x, z);
      std::printf("%s{\"k\":\"mso\",\"dx\":%.9g,\"dz\":%.9g,\"x\":%.9g,\"z\":%.9g}\n", Sep(), p.first, p.second, x, z);
    }
  }
  for (int a = -2; a <= 2; ++a) for (int b = -2; b <= 2; ++b) for (int r = 0; r < 2; ++r)
    std::printf("%s{\"k\":\"rip\",\"r\":%d,\"a\":%d,\"b\":%d,\"o\":%d}\n", Sep(), r, a, b, (int)TR::RetainedIncidentPair(r, a, b));
  for (int m = 0; m < 8; ++m)
  {
    std::printf("%s{\"k\":\"exa\",\"m\":%d,\"o\":%d}\n", Sep(), m, (int)TR::ExemptArenaIncident(m & 1, (m >> 1) & 1, (m >> 2) & 1));
    std::printf("%s{\"k\":\"iso\",\"m\":%d,\"o\":%d}\n", Sep(), m, (int)TR::IsolatePair(m & 1, (m >> 1) & 1, (m >> 2) & 1));
    std::printf("%s{\"k\":\"furn\",\"m\":%d,\"o\":%d}\n", Sep(), m, (int)TownFurniturePolicy::Occupied(m & 1, (m >> 1) & 1, (m >> 2) & 1));
  }
  {
    std::vector<float> el = { 0, 299.99997f, 300, 300.00003f, -1, 1e9f, 299, 150.5f };
    for (int i = 0; i < 100; ++i) el.push_back((float)(R() % 70000) / 233.0f);
    std::vector<float> dts = { 0, -0.0f, 1e-7f, -1, 0.016f, 1, 0.5f, 300 };
    for (float e : el) for (float dt : dts) for (int p = 0; p < 2; ++p)
    {
      const float out = TR::AdvanceAftercare(e, dt, p);
      std::printf("%s{\"k\":\"aft\",\"e\":%.9g,\"dt\":%.9g,\"p\":%d,\"o\":%.9g,\"x\":%d,\"xo\":%d}\n", Sep(), e, dt, p, out,
                  (int)TR::AftercareExpired(e), (int)TR::AftercareExpired(out));
    }
  }
  {
    const float hs[] = { 0.9f, 0.89999998f, 0.90000004f, 1.0f, 0.0f, 0.5f, 2.0f, -1.0f };
    for (int i = 0; i < 1500; ++i)
    {
      TR::Readiness r;
      const unsigned m = R() % 4 == 0 ? R() % 64 : (R() % 2 ? 1u : 0u);
      r.valid = m & 1; r.player = (m >> 1) & 1; r.dead = (m >> 2) & 1; r.unconscious = (m >> 3) & 1; r.inCombat = (m >> 4) & 1;
      r.needsAid = (m >> 5) & 1;
      if (R() % 3 == 0) r.valid = true;
      r.minimumHealth = hs[R() % 8]; r.blood = hs[R() % 8];
      const bool able = R() % 2;
      std::printf("%s{\"k\":\"rdy\",\"m\":%u,\"v\":%d,\"h\":%.9g,\"b\":%.9g,\"able\":%d,\"cp\":%d,\"ca\":%d}\n", Sep(), m, (int)r.valid,
                  r.minimumHealth, r.blood, (int)able, (int)TR::CanParticipate(r), (int)TR::CanProvideAid(r, able));
    }
  }
  {
    std::vector<std::string> idText;
    for (int n = 0; n <= 700; ++n) idText.push_back(std::to_string(n) + "-Proving Grounds.mod");
    const char* odd[] = { "", "96", "96-Proving Grounds.mo", "96-Proving Grounds.modx", "096-Proving Grounds.mod", "96-proving grounds.mod",
      " 96-Proving Grounds.mod", "183-Proving Grounds.mod", "195-Proving Grounds.mod", "100-Proving Grounds.mod" };
    for (const char* o : odd) idText.push_back(o);
    std::printf("%s{\"k\":\"ids\",\"id\":null,\"eq\":%d,\"na\":%d,\"tr\":%d,\"rf\":%d,\"md\":%d,\"cat\":null}\n", Sep(),
                (int)TR::Equals(NULL, "x"), (int)TR::IsNewArena(NULL), (int)TR::IsTownRegistry(NULL), (int)TR::IsRegularFighter(NULL),
                (int)TR::IsMedic(NULL));
    for (const std::string& id : idText)
    {
      const TownFighterCatalog::Entry* e = TownFighterCatalog::Find(id.c_str());
      std::printf("%s{\"k\":\"ids\",\"id\":", Sep()); Str(id);
      std::printf(",\"eq\":%d,\"na\":%d,\"tr\":%d,\"rf\":%d,\"md\":%d,\"cat\":", (int)TR::Equals(id.c_str(), "96-Proving Grounds.mod"),
                  (int)TR::IsNewArena(id.c_str()), (int)TR::IsTownRegistry(id.c_str()), (int)TR::IsRegularFighter(id.c_str()),
                  (int)TR::IsMedic(id.c_str()));
      if (!e) std::printf("null");
      else
      {
        std::printf("{\"c\":"); Str(e->characterId); std::printf(",\"s\":"); Str(e->squadId); std::printf(",\"t\":"); Str(e->type);
        std::printf(",\"tier\":%d,\"n\":%d}", e->tier, e->count);
      }
      std::printf("}\n");
    }
  }
  {
    Dummy dummies[5] = { {0}, {1}, {2}, {3}, {4} };
    const char* keys[] = { "", "a", "b", "c" };
    for (int i = 0; i < 200; ++i)
    {
      TR::DiscoveryIndex<Dummy> index;
      const int steps = 1 + (int)(R() % 10);
      std::printf("%s{\"k\":\"didx\",\"s\":[", Sep());
      for (int st = 0; st < steps; ++st)
      {
        const int a = (int)(R() % 6) - 1;
        const char* key = keys[R() % 4];
        const bool ok = index.Add(a < 0 ? NULL : &dummies[a], key);
        std::printf("%s{\"a\":%d,\"key\":\"%s\",\"ok\":%d,\"actors\":[", st ? "," : "", a, key, (int)ok);
        for (size_t j = 0; j < index.actors.size(); ++j) std::printf("%s%d", j ? "," : "", index.actors[j]->index);
        std::printf("],\"by\":[");
        int n = 0;
        for (std::map<std::string, Dummy*>::const_iterator it = index.byIdentity.begin(); it != index.byIdentity.end(); ++it)
          std::printf("%s[\"%s\",%d]", n++ ? "," : "", it->first.c_str(), it->second ? it->second->index : -1);
        std::printf("]}");
      }
      std::printf("]}\n");
    }
  }
  {
    const char* ids[] = { "", "a", "b", "c", "d" };
    for (int i = 0; i < 300; ++i)
    {
      std::vector<std::string> team;
      const int steps = 1 + (int)(R() % 12);
      std::printf("%s{\"k\":\"rost\",\"s\":[", Sep());
      for (int st = 0; st < steps; ++st)
      {
        const int op = (int)(R() % 4);
        const char* id = ids[R() % 5];
        const bool ready = R() % 5 != 0, locked = R() % 6 == 0;
        int ok = -1;
        std::vector<std::string> roster;
        if (op == 0) ok = TownRosterSelection::Add(team, id, ready, locked);
        else if (op == 1) ok = TownRosterSelection::Remove(team, id, locked);
        else if (op == 2) ok = TownRosterSelection::Toggle(team, id, ready, locked);
        else
        {
          for (int j = 0; j < 5; ++j) if (R() % 2) roster.push_back(ids[j]);
          TownRosterSelection::Prune(team, roster);
        }
        std::printf("%s{\"op\":%d,\"id\":\"%s\",\"ready\":%d,\"locked\":%d,\"roster\":[", st ? "," : "", op, id, (int)ready, (int)locked);
        for (size_t j = 0; j < roster.size(); ++j) std::printf("%s\"%s\"", j ? "," : "", roster[j].c_str());
        std::printf("],\"ok\":%d,\"team\":[", ok);
        for (size_t j = 0; j < team.size(); ++j) std::printf("%s\"%s\"", j ? "," : "", team[j].c_str());
        std::printf("]}");
      }
      std::printf("]}\n");
    }
  }
  std::printf("]\n");
  return 0;
}
