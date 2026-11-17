// sources: 
// RosterStatusPolicy.h (header only): health recovery and bands from limb/blood ratios, as JSON lines in one array.
// Case kinds: "rec" (lowest limb, blood -> Clamp of each, Recovery, BandFor(recovery), BandFor(raw lowest limb)),
// "fold" (limb ratios folded with LowestLimb from a start value -> lowest, Recovery with a blood ratio, band),
// "band" (BandFor at and around the 0.5 / 0.9 thresholds), "str" (reason -> IsRecoveryReason, InlineStatus(ready, reason)).
#include "RosterStatusPolicy.h"
#include <cmath>
#include <cstdio>
#include <cstring>
using namespace RosterStatusPolicy;
static unsigned s = 202020u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }
static float Ratio()
{
  unsigned k = R() % 10;
  if (k == 0) return (float)((int)(R() % 2001) - 1000) / 400.0f;   // -2.5 .. 2.5
  if (k == 1) return (float)(R() % 11) / 10.0f;                    // exact tenths, ties
  return (float)(R() % 1000001) / 1000000.0f;                      // 0 .. 1
}
int main()
{
  std::printf("[\n");
  for (int c = 0; c < 2500; ++c)
  {
    float limb = Ratio();
    float blood = (R() % 8 == 0) ? limb : Ratio();
    float rec = Recovery(limb, blood);
    std::printf("%s{\"k\":\"rec\",\"limb\":%.9g,\"blood\":%.9g,\"cl\":%.9g,\"cb\":%.9g,\"rec\":%.9g,\"band\":%d,\"rawBand\":%d}\n",
                Sep(), limb, blood, Clamp(limb), Clamp(blood), rec, (int)BandFor(rec), (int)BandFor(limb));
  }
  for (int c = 0; c < 600; ++c)
  {
    float start = (R() % 2) ? 1.0f : Ratio();
    int n = (int)(R() % 8);
    float limbs[8];
    float lowest = start;
    for (int i = 0; i < n; ++i) { limbs[i] = Ratio(); lowest = LowestLimb(lowest, limbs[i]); }
    float blood = Ratio();
    float rec = Recovery(lowest, blood);
    std::printf("%s{\"k\":\"fold\",\"start\":%.9g,\"limbs\":[", Sep(), start);
    for (int i = 0; i < n; ++i) std::printf("%s%.9g", i ? "," : "", limbs[i]);
    std::printf("],\"blood\":%.9g,\"lowest\":%.9g,\"rec\":%.9g,\"band\":%d}\n", blood, lowest, rec, (int)BandFor(rec));
  }
  static const float kEdges[] = {0.9f, 0.5f, 0.0f, 1.0f, -0.0f, 0.89999998f, 0.90000004f, 0.49999997f, 0.50000006f, 0.9000001f};
  for (float e : kEdges)
  {
    std::printf("%s{\"k\":\"band\",\"v\":%.9g,\"band\":%d}\n", Sep(), e, (int)BandFor(e));
    float lo = std::nextafter(e, -10.0f), hi = std::nextafter(e, 10.0f);
    std::printf("%s{\"k\":\"band\",\"v\":%.9g,\"band\":%d}\n", Sep(), lo, (int)BandFor(lo));
    std::printf("%s{\"k\":\"band\",\"v\":%.9g,\"band\":%d}\n", Sep(), hi, (int)BandFor(hi));
  }
  static const char* kReasons[] = {"", "recovering", "recovering (62%)", "recover", "Recovering", "recoverinG", " recovering",
                                   "recovering\t", "recoveringrecovering", "busy", "dead", "in a match", "r", "recoverin"};
  for (const char* r : kReasons)
    for (int ready = 0; ready < 2; ++ready)
      std::printf("%s{\"k\":\"str\",\"reason\":\"%s\",\"ready\":%d,\"isRec\":%d,\"inline\":\"%s\"}\n", Sep(),
                  std::strcmp(r, "recovering\t") == 0 ? "recovering\\t" : r, ready, (int)IsRecoveryReason(r), InlineStatus(ready, r).c_str());
  std::printf("]\n");
}
