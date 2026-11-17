// sources: PrisonerMatchLogic.cpp PrisonerRecruitmentLogic.cpp PrisonerRecruitmentPresentation.cpp pgconfig_stub.cpp
// PrisonerMatchLogic, PrisonerRecruitmentLogic and PrisonerRecruitmentPresentation, as JSON lines in one array.
// Case kinds: "price" (stat, marks multiplier, recruitment {min,max,step,scale}, current marks -> RequiredMarks, CanRecruit),
// "lock" (all 1024 CageLockObservation combinations -> ChooseCageLockAction), "hand" (prisoners, handlers -> HasEnoughHandlers,
// FormatHandlerShortage), "cage" (original index, per-cage flags and distances -> ChooseReturnCage), "supp" (every
// ShouldSuppressNaturalEnemy input), "ret" (every ChooseReturnAction input), "pres" (Input -> State of Evaluate).
#include "PrisonerMatchLogic.h"
#include "PrisonerRecruitmentLogic.h"
#include "PrisonerRecruitmentPresentation.h"
#include "pgconfig_stub.h"
#include <cstdio>
using namespace PrisonerMatchLogic;
static unsigned s = 515151u;
static unsigned R() { s = s * 1664525u + 1013904223u; return s >> 8; }
static bool first = true;
static const char* Sep() { const char* r = first ? "" : ","; first = false; return r; }

static void Price(float stat, int current)
{
  const PGConfig::Recruitment& r = PGConfig::RecruitmentValues();
  int marks = PrisonerRecruitmentLogic::RequiredMarks(stat);
  bool can = PrisonerRecruitmentLogic::CanRecruit(stat, current);
  std::printf("%s{\"k\":\"price\",\"stat\":%.9g,\"rec\":[%d,%d,%d,%.9g],\"cur\":%d,\"marks\":%d,\"can\":%d}\n",
              Sep(), stat, r.minimum, r.maximum, r.step, r.combatScale, current, marks, (int)can);
}

int main()
{
  std::printf("[\n");
  static const double kMults[] = {1.0, 0.5, 2.5, 0.0, 1.37, 100.0};
  static const PGConfig::Recruitment kRecs[] = {
    {25, 250, 5, 2.5}, {10, 400, 10, 3.0}, {0, 100, 1, 1.7}, {50, 60, 7, 0.33}, {30, 200, 3, 2.25}};
  static const float kStats[] = {-10.0f, -0.0f, 0.0f, 0.1f, 9.99f, 10.0f, 10.0001f, 37.3f, 50.0f, 99.9f, 100.0f, 100.5f, 150.0f, 1e9f};
  for (int m = 0; m < 6; ++m)
    for (int r = 0; r < 5; ++r)
    {
      PGConfigStub_Reset();
      PGConfigStub_SetMarksMultiplier(kMults[m]);
      PGConfigStub_Recruitment() = kRecs[r];
      std::printf("%s{\"k\":\"mult\",\"mult\":%.9g}\n", Sep(), kMults[m]);
      for (float st : kStats) { int cur = (int)(R() % 600); Price(st, cur); }
      for (int c = 0; c < 60; ++c)
      {
        float st = (float)((int)(R() % 140001) - 20000) / 1000.0f;   // -20 .. 120
        int cur = (int)(R() % 700) - 50;
        if (R() % 4 == 0) cur = PrisonerRecruitmentLogic::RequiredMarks(st) - 1 + (int)(R() % 3);   // ties at the boundary
        Price(st, cur);
      }
    }
  PGConfigStub_Reset();

  for (int bits = 0; bits < 1024; ++bits)
  {
    CageLockObservation o;
    o.destinationValid = bits & 1; o.correctOccupant = bits & 2; o.hasLock = bits & 4; o.broken = bits & 8;
    o.verifierAvailable = bits & 16; o.locked = bits & 32; o.handlerAvailable = bits & 64; o.orderIssued = bits & 128;
    o.orderRejected = bits & 256; o.timedOut = bits & 512;
    std::printf("%s{\"k\":\"lock\",\"bits\":%d,\"act\":%d}\n", Sep(), bits, (int)ChooseCageLockAction(o));
  }

  static const int kHand[][2] = {{0, 0}, {2, 2}, {2, 1}, {1, 0}, {0, 3}, {5, 7}, {7, 5}, {-1, 0}, {0, -1}, {12, 11}, {100, 3}};
  for (auto& h : kHand)
    std::printf("%s{\"k\":\"hand\",\"p\":%d,\"a\":%d,\"ok\":%d,\"msg\":\"%s\"}\n", Sep(), h[0], h[1],
                (int)HasEnoughHandlers(h[0], h[1]), FormatHandlerShortage(h[0], h[1]).c_str());

  for (int c = 0; c < 1200; ++c)
  {
    int count = (int)(R() % 7);   // 0..6
    bool fr[6], inr[6], res[6]; float dist[6];
    for (int i = 0; i < 6; ++i)
    {
      fr[i] = R() % 4 != 0; inr[i] = R() % 5 != 0; res[i] = R() % 5 == 0;
      dist[i] = (R() % 3 == 0) ? (float)(R() % 5) * 10.0f : (float)(R() % 1000000) / 37.0f;   // many ties
    }
    int orig = (int)(R() % 9) - 2;   // -2 .. 6, includes out of range
    CagePick p = ChooseReturnCage(orig, fr, dist, inr, res, count);
    std::printf("%s{\"k\":\"cage\",\"orig\":%d,\"n\":%d,\"c\":[", Sep(), orig, count);
    for (int i = 0; i < count; ++i)
      std::printf("%s[%d,%d,%d,%.9g]", i ? "," : "", (int)fr[i], (int)inr[i], (int)res[i], dist[i]);
    std::printf("],\"pick\":%d}\n", p.index);
  }

  for (int bits = 0; bits < 8; ++bits)
    std::printf("%s{\"k\":\"supp\",\"bits\":%d,\"r\":%d}\n", Sep(), bits,
                (int)ShouldSuppressNaturalEnemy(bits & 1, bits & 2, bits & 4));
  for (int bits = 0; bits < 4; ++bits)
    std::printf("%s{\"k\":\"ret\",\"bits\":%d,\"r\":%d}\n", Sep(), bits, (int)ChooseReturnAction(bits & 1, bits & 2));

  static const int kMarks[][2] = {{0, 0}, {0, 1}, {1, 2}, {5, 25}, {24, 25}, {25, 25}, {300, 250}, {-5, 0}, {0, -5}, {0, 100}, {99, 100}};
  for (int bits = 0; bits < 32; ++bits)
    for (auto& mk : kMarks)
    {
      PrisonerRecruitmentPresentation::Input in;
      in.hasSelection = bits & 1; in.livingRosterPrisoner = bits & 2; in.busy = bits & 4; in.unconscious = bits & 8;
      in.playerReady = bits & 16; in.currentMarks = mk[0]; in.requiredMarks = mk[1];
      PrisonerRecruitmentPresentation::State st = PrisonerRecruitmentPresentation::Evaluate(in);
      std::printf("%s{\"k\":\"pres\",\"bits\":%d,\"cur\":%d,\"req\":%d,\"on\":%d,\"cap\":\"%s\",\"exp\":\"%s\"}\n", Sep(), bits,
                  mk[0], mk[1], (int)st.enabled, st.actionCaption.c_str(), st.explanation.c_str());
    }
  std::printf("]\n");
}
