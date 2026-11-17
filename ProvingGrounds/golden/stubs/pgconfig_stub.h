// Test hooks of golden/stubs/pgconfig_stub.cpp. They have no counterpart in the original; only generators and the stub check use them.
// Include AFTER the real PGConfig.h types are visible (this header includes it).
#pragma once
#include "PGConfig.h"

// Back to the shipped values (what the original holds when pg_config.json is absent).
void PGConfigStub_Reset();
// PGConfig.cpp g_multiplier ("marks_multiplier"). Default 1.0; the file allows 0.0 to 100.0 (kMaxScale, PGConfig.cpp:25).
// NOTE: ArenaRewards::ApplyPriceOverrides() stamps prices IN PLACE (it passes the current cost in as the default), so it must run
// once per process after this is set; generate each multiplier in its own process (or reset before re-stamping).
void PGConfigStub_SetMarksMultiplier(double multiplier);
// Mutable views of the original's globals, for generators that vary one setting (the real accessors return const references).
PGConfig::Recruitment& PGConfigStub_Recruitment();
PGConfig::Bookie& PGConfigStub_Bookie();
PGConfig::Challenges& PGConfigStub_Challenges();
void PGConfigStub_SetChallengeBase(int division, int cats); // division 0 Easy, 1 Medium, 2 Hard
