// sources: BalanceTuning.cpp pgconfig_stub.cpp
// The original's RNG and hash helpers -> Core/Hashing.cs. Each block calls the ORIGINAL inline function from its header, except
// "mix" (no named function in the C++: the expression is written inline twice), which is copied below with its file:line.
// Strings are printed as hex bytes so the JSON stays ASCII; the C# test decodes them (the "utf8" cases are valid UTF-8 and also go
// through the string overload, the "bytes" cases are arbitrary bytes and go through the span overload).
#include "TownMatchmakingPolicy.h"
#include "TownChallengePolicy.h"
#include "TownLimbShopPolicy.h"
#include <cstdio>
#include <string>
static unsigned s = 4242u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static unsigned R32() { unsigned hi = R() & 0xffffu; unsigned lo = R() & 0xffffu; return (hi << 16) | lo; }
// Copied from TownChallengePolicy.h:344-346 (PreviewOffer) == TownChallengePolicy.h:403 (EncounterForDivision), identical text.
static uint32_t ChallengeMix(uint32_t seed) { seed ^= seed >> 16; seed *= 0x85ebca6bu; seed ^= seed >> 13; return seed; }
static void Hex(const std::string& v) {
  std::printf("\"");
  for (size_t i = 0; i < v.size(); ++i) std::printf("%02x", (unsigned)(unsigned char)v[i]);
  std::printf("\"");
}
int main() {
  bool first = true;
  std::printf("[\n");
#define SEP() do { std::printf("%s", first ? "" : ","); first = false; } while (0)
  // xorshift32 (TownMatchmakingPolicy::Detail::Next): chains of 8 draws from a seed, seed 0 included (the zero fix)
  for (int c = 0; c < 1000; ++c) {
    unsigned seed = c == 0 ? 0u : c == 1 ? 0x9e3779b9u : c == 2 ? 0xffffffffu : c == 3 ? 1u : R32();
    SEP(); std::printf("{\"k\":\"next\",\"seed\":%u,\"out\":[", seed);
    unsigned st = seed;
    for (int i = 0; i < 8; ++i) { unsigned v = TownMatchmakingPolicy::Detail::Next(st); std::printf("%s%u", i ? "," : "", v); }
    std::printf("],\"state\":%u}\n", st);
  }
  // LCG (TownChallengePolicy::Draw on a Card): chains of 8
  for (int c = 0; c < 1000; ++c) {
    TownChallengePolicy::Card card;
    unsigned seed = c == 0 ? 0u : c == 1 ? 1u : c == 2 ? 0xffffffffu : R32();
    card.seed = seed;
    SEP(); std::printf("{\"k\":\"draw\",\"seed\":%u,\"out\":[", seed);
    for (int i = 0; i < 8; ++i) { uint32_t v = TownChallengePolicy::Draw(card); std::printf("%s%u", i ? "," : "", v); }
    std::printf("],\"state\":%u}\n", card.seed);
  }
  // challenge mix (copied expression)
  for (int c = 0; c < 1000; ++c) {
    unsigned seed = c == 0 ? 0u : c == 1 ? 0xffffffffu : R32();
    SEP(); std::printf("{\"k\":\"mix\",\"seed\":%u,\"out\":%u}\n", seed, ChallengeMix(seed));
  }
  // limb shop Mix
  for (int c = 0; c < 1000; ++c) {
    unsigned h = c == 0 ? 0u : R32(), v = c == 1 ? 0xffffffffu : c == 2 ? 0u : R32();
    SEP(); std::printf("{\"k\":\"lmix\",\"hash\":%u,\"value\":%u,\"out\":%u}\n", h, v, TownLimbShopPolicy::Mix(h, v));
  }
  // FNV-1a over strings (TownMatchmakingPolicy::Detail::Hash with a seed; TownLimbShopPolicy::Seed's loop = seed 0)
  const char* fixed[] = { "", "a", "616-Proving Grounds.mod", "next NPC start", "M\xc3\xb6nch", "\xe6\x97\xa5\xe6\x9c\xac",
                          "\xf0\x9f\x98\x80 x", "Caf\xc3\xa9 \xe2\x82\xac 100" };
  const int nFixed = sizeof fixed / sizeof fixed[0];
  for (int c = 0; c < 1000; ++c) {
    std::string v; bool utf8;
    if (c < nFixed) { v = fixed[c]; utf8 = true; }
    else if (c % 2) {                                    // random ASCII
      int n = R() % 40; for (int i = 0; i < n; ++i) v.push_back((char)(32 + R() % 95)); utf8 = true;
    } else {                                             // random bytes (high bytes: unsigned char handling)
      int n = R() % 40; for (int i = 0; i < n; ++i) v.push_back((char)(R() % 256)); utf8 = false;
    }
    unsigned seed = c % 3 == 0 ? 0u : R32();
    SEP(); std::printf("{\"k\":\"%s\",\"seed\":%u,\"hex\":", utf8 ? "utf8" : "bytes", seed); Hex(v);
    std::printf(",\"out\":%u}\n", TownMatchmakingPolicy::Detail::Hash(seed, v));
  }
  // limb shop Seed (FNV-1a of the identity, Mix(matches clamped >= 0), Mix(limbMask & 15), zero -> 0x9e3779b9)
  for (int c = 0; c < 1000; ++c) {
    std::string id; int n = R() % 30; for (int i = 0; i < n; ++i) id.push_back((char)(32 + R() % 95));
    if (c == 0) id = "";
    if (c == 4) id = "q@RMu\\ ";                      // found by brute force: its hash is 0, so Seed takes the zero fix
    int matches = c == 1 ? -5 : c == 2 ? 0 : c == 4 ? 7 : (int)(R() % 2000) - 100;
    unsigned mask = c == 3 ? 0xffffffffu : c == 4 ? 3u : R32();
    SEP(); std::printf("{\"k\":\"lseed\",\"hex\":"); Hex(id);
    std::printf(",\"matches\":%d,\"mask\":%u,\"out\":%u}\n", matches, mask, TownLimbShopPolicy::Seed(id, matches, mask));
  }
  std::printf("]\n");
}
