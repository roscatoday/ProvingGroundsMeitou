Proving Grounds for Meitou 0.2.0

A C# port of Proving Grounds 1.3.4 by Shutori (Steam Workshop item 3774745195) for Meitou. Same rules, windows and texts: the
arena, the Registry, Leaderboards, the reward shop, prisoner recruiting and the town arena of Scratch.
Requires Meitou 0.17.3 or later. Tested on Meitou 0.17.3 and 0.20.1.

REQUIRES the original mod
  This port brings no art or data of its own. It needs Proving Grounds (Workshop 3774745195) installed and enabled.
  If it is missing, our mod switches itself off and writes one error line to Meitou's log.txt.

HOW TO INSTALL
  1. Subscribe to Proving Grounds (3774745195) on the Steam Workshop and install this mod.
  2. In the launcher's mod list enable "Proving Grounds", then "ProvingGroundsMeitou" BELOW it (so the original is above ours).
  3. Close and reopen the launcher before you press Play.

SETTINGS
  Esc > Settings > Mods > ProvingGroundsMeitou > "Settings", then the Save button.
  Most changes need a restart of the game. The debug menu key and crowd performance apply at once.
  The remaining options (gear, weapons, supplies, licences) are in pg_config.json in this mod's folder; it is written on first start.
  Debug menu key: Off by default; pick O, N or V. (F8 is Meitou's screenshot key. The setting's hint still says F8: that is the
  original's text, kept.)

YOUR PROGRESS
  Ratings, Marks and rewards are stored inside each Kenshi save. There is no separate sidecar file, and no import of old files.

WORKS WITH OTHER MODS
  - Other code mods can ask Proving Grounds who is in a bout (including the walk to the ring and the aftercare) and who is one of
    Scratch's town fighters: docs/api.md in the source (ProvingGrounds.ProvingGroundsApi, read by reflection).
  - With The Wasteland Remembers installed (its versions with a public API): Scratch's fighters are never made its rivals or
    renamed, arena upsets and your squad's big wins become town news, History lines and Recap moments, and it can tell a spar or a
    town bout from an assault.

KNOWN DIFFERENCES FROM THE ORIGINAL
  - Settings are on Meitou's Settings page, not in a window of our own.
  - Fighter identity comes from the character's portrait and record, not an id written into the save.
  - Time counts the game's simulation time and stops while paused.
  - Limb replacements go only to the town's own fighters, for free.
  - The announcer is for the player's bouts.
  - Meitou 0.20 is supported with the same files. On 0.20.2 a Proving Grounds release of a prisoner posts no "has been released from
    prison" line, and a few Rewards window labels can sit 1 px off after a purchase (neither was seen live; 0.20.2 is not live-tested).
  - Full list: docs/divergences.md in the source.

LICENCE AND CREDIT
  GPLv3 (see LICENSE and NOTICE.md). Original Proving Grounds by Shutori, source:
  https://github.com/shutori/Shutori_Re_Kenshi_Source. This port is a translation of that code and uses none of its art.
  Source of this port: source.zip in this mod's folder (GPLv3; BUILD.txt inside says how to build it).
