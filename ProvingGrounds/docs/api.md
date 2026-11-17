# Proving Grounds for Meitou: API for other Meitou mods

Proving Grounds for Meitou (from 0.2.0) has a small public class other code mods can read, so mods can work together without depending
on each other. Find it by reflection; if the mod isn't installed you get null and carry on.

```csharp
using System.Reflection;
using Meitou.Simulation;

static readonly Type? Pg = AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetName().Name == "ProvingGroundsMeitou")
    ?.GetType("ProvingGrounds.ProvingGroundsApi");

static readonly Func<CharacterId, bool>? InBout =
    Pg?.GetMethod("InBout", [typeof(CharacterId)])?.CreateDelegate<Func<CharacterId, bool>>();
static readonly Func<string, bool>? IsTownFighter =
    Pg?.GetMethod("IsTownFighter", [typeof(string)])?.CreateDelegate<Func<string, bool>>();

// a blow between two characters that are both in a bout is sport, not an assault
bool sport = InBout?.Invoke(striker) == true && InBout(victim);
// one of Scratch's arena fighters: don't make it a rival or rename it (the leaderboard shows it by name)
bool leaveAlone = IsTownFighter?.Invoke(name) == true;
```

Mods load in any order, so look the type up on first use (in a tick), not in your mod's `Initialize`. Every member answers from one
immutable view the arena publishes at the end of its tick: it is safe from any thread, allocates nothing and never throws. Before a game
is loaded, and when the original Proving Grounds data is missing (which switches our mod off), every answer is false.

`Version` goes up when something is added. Nothing is removed and nothing changes meaning, so check `Version >= n` for what you need.

## Version 1

| Member | What it does |
|---|---|
| `int Version` (const) | 1 |
| `string Build` | The mod's version, e.g. `0.2.0` |
| `bool Ready` | A game is loaded and the arena runs in it |
| `bool InBout(CharacterId id)` | The character is in one of the arena's bouts or being walked to or from one: walking to the ring, fighting, benched between Teams 1v1 bouts, knocked out in it, carried and treated by Scratch's medics in the aftercare, or a match prisoner on its way from its cage and back. In the squad's own arena and in Scratch's town arena alike. Stays true for 30 s of game time after the arena lets the character go, so a bout's last blow (the knock-out) that your mod counts a moment later is still sport. False for `CharacterId.None`, a stale id (the slot reused by someone else) and before a game is loaded |
| `bool IsTownFighter(string name)` | One of Scratch's town fighters by name, as the game shows it (letter case and spaces around it ignored): the Bloodstamp characters of the arena's fighter templates and its unique fighters (Senn, Torka, Skarn, Ressa Vane, Bum-Per, Veyr). The leaderboard shows them by name. Read every few seconds |
| `bool IsTownFighterId(CharacterId id)` | The same test by character (exact) |

`CharacterId` is the game's own type (`Meitou.Simulation`), the same in every mod, so it crosses between mods as is.

## What Proving Grounds tells other mods by itself

When **The Wasteland Remembers** (from its build with its API version 3) is installed, Proving Grounds calls its public API
(`LivingWasteland.MeitouMod.WastelandApi`, by reflection: neither mod needs the other):

- `LeaveAlone(name, "Bloodstamp", "ProvingGroundsMeitou")` for every town fighter, once per game: it never makes one of them a rival or
  renames it.
- After a bout in Scratch's arena, news when it is news (`Report(kind, "Scratch", "Bloodstamp", text, weight, "ProvingGroundsMeitou")`):
  - `arena-upset` (weight 30): two sides of town fighters, and the underdog won (its chance by the ratings before the bout was one in
    four or less). "Ruka beat the favourite Hagar in the arena at Scratch."
  - `arena-squad-upset` (weight 50): the player's side beat the favourite.
  - `arena-unique-beaten` (weight 65): the player's side beat one of the arena's unique fighters.
  - `arena-skarn-beaten` (weight 80): the player's side beat Skarn.
  The player's own wins also go to the squad's History tab (`Note`) and to Recap (`Moment`). An ordinary win, a loss, a stop or a spar in
  the squad's own arena is not news.

Questions or requests: the Proving Grounds for Meitou Workshop page, or the Meitou Discord.
