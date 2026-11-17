// sources: 
// ArenaRingGuard.h pure arithmetic (header only; the engine half, ArenaRingGuard.cpp, is glue), as JSON lines in one array.
// Case kinds: "zone" (distance, near, out -> ClassifyRing), "ret" (dx, dz, target radius -> RingReturnPoint x, z),
// "due" (now, last, interval, unsigned -> RingCorrectionDue, including wrap-around).
#include "ArenaRingGuard.h"
#include <cstdio>
using namespace ArenaRingGuard;
static unsigned s = 141414u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static unsigned R32() { unsigned a = R(); unsigned b = R(); return (a << 8) ^ b; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }
int main()
{
  std::printf("[\n");
  static const float kRadii[][2] = {{145.0f, 147.5f}, {87.0f, 88.5f}, {150.0f, 160.0f}, {100.0f, 100.0f}, {160.0f, 150.0f}, {0.0f, 0.0f}};
  for (int c = 0; c < 2000; ++c)
  {
    const float* r = kRadii[R() % 6];
    float d;
    unsigned k = R() % 6;
    if (k == 0) d = r[0];
    else if (k == 1) d = r[1];
    else if (k == 2) d = -(float)(R() % 1000) / 10.0f;
    else d = (float)(R() % 400000) / 1000.0f;
    std::printf("%s{\"k\":\"zone\",\"d\":%.9g,\"near\":%.9g,\"out\":%.9g,\"z\":%d}\n", Sep(), d, r[0], r[1], (int)ClassifyRing(d, r[0], r[1]));
  }
  static const float kTargets[] = {142.5f, 85.5f, 140.0f, 0.0f, -10.0f};
  for (int c = 0; c < 2500; ++c)
  {
    float dx, dz;
    unsigned k = R() % 8;
    if (k == 0) { dx = 0.0f; dz = 0.0f; }
    else if (k == 1) { dx = (float)((int)(R() % 2001) - 1000) / 1000.0f; dz = (float)((int)(R() % 2001) - 1000) / 1000.0f; }   // near 1
    else if (k == 2) { dx = 1.0f; dz = 0.0f; }
    else if (k == 3) { dx = 0.6f; dz = 0.8f; }
    else { dx = (float)((int)(R() % 800001) - 400000) / 1000.0f; dz = (float)((int)(R() % 800001) - 400000) / 1000.0f; }
    float t = kTargets[R() % 5];
    float x = -1.0f, z = -1.0f;
    RingReturnPoint(dx, dz, t, x, z);
    std::printf("%s{\"k\":\"ret\",\"dx\":%.9g,\"dz\":%.9g,\"t\":%.9g,\"x\":%.9g,\"z\":%.9g}\n", Sep(), dx, dz, t, x, z);
  }
  static const unsigned kIntervals[] = {1000u, 0u, 1u, 250u, 4294967295u};
  for (int c = 0; c < 1500; ++c)
  {
    unsigned now, last, iv = kIntervals[R() % 5];
    unsigned k = R() % 5;
    if (k == 0) { now = R32(); last = 0u; }
    else if (k == 1) { last = R32(); now = last + (R() % 2001); }               // around the interval
    else if (k == 2) { last = 4294967295u - (R() % 1500); now = R() % 1500; }   // wrap-around
    else if (k == 3) { now = R32(); last = now + 1u + (R() % 50); }            // clock behind last
    else { now = R32(); last = R32(); }
    std::printf("%s{\"k\":\"due\",\"now\":%u,\"last\":%u,\"iv\":%u,\"due\":%d}\n", Sep(), now, last, iv, (int)RingCorrectionDue(now, last, iv));
  }
  std::printf("]\n");
}
