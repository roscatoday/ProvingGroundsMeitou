# Proving Grounds for Meitou

A C# port of [Proving Grounds](https://steamcommunity.com/sharedfiles/filedetails/?id=3774745195) 1.3.4 by Shutori for
[Meitou](https://github.com/VlcekM/MeitouClient), the open-source rebuild of Kenshi: the arena, the Registry, the leaderboards, the reward
shop, prisoner recruiting and the town arena of Scratch, with the same rules, windows and texts.
On the Steam Workshop: [Proving Grounds for Meitou](https://steamcommunity.com/sharedfiles/filedetails/?id=3841575072). It needs the
original Proving Grounds Workshop item enabled above it (this port ships none of its art or data).

## Layout

- `ProvingGrounds/` the mod. `Core/` holds the game-free logic (the original's C++ ported file by file; `Core/Glue/` the decisions of
  the engine-facing parts over small host interfaces), `src/` the Meitou glue (Harmony patches, the tick system, the windows), `Tests/`
  the unit tests (no game needed) with golden data made by compiling the original C++ (`golden/`), `upstream/` that C++ (Shutori's,
  GPLv3, commit `4a20d48`), `fidelity/` the original's player-visible strings, `docs/` the differences from the original
  (`divergences.md`), the file-by-file map (`port-map.md`), the Meitou versions it runs on (`versions.md`) and the API for other mods
  (`api.md`).
- `_dual/` two shared MSBuild files the build imports from the folder beside `ProvingGrounds/`. Keep both folders side by side.

## Build

Needs the .NET SDK 10 and Meitou 0.17.3 installed (the oldest game the mod supports; the build references that game's
`MeitouClient.dll`, `Meitou.*.dll` and `0Harmony.dll` and does not copy them). `Directory.Build.props` looks for it in
`%LOCALAPPDATA%\Meitou\versions\0.17.3`; another folder: `dotnet build -p:GameDir=<folder holding MeitouClient.dll>`.

```
cd ProvingGrounds
dotnet build -c Release ProvingGroundsMeitou.csproj
dotnet test Tests/ProvingGrounds.Tests.csproj      # the unit tests need no game
```

Output: `bin/modern/Release/net10.0/ProvingGroundsMeitou.dll` and `Core/bin/modern/Release/net10.0/ProvingGrounds.Core.dll`. Install:
copy those two DLLs with `meitou.json`, `ProvingGroundsMeitou.mod`, `README.txt`, `LICENSE` and `NOTICE.md` into
`Kenshi/mods/ProvingGroundsMeitou/`, and enable the original Proving Grounds above it in the launcher.
`tools/package.py` is the release script (it also runs a reference-check tool that is not part of this repository).

## For other mods

Other Meitou code mods can ask the arena who is in a bout and who is one of Scratch's town fighters, by reflection, with no dependency:
see [`ProvingGrounds/docs/api.md`](ProvingGrounds/docs/api.md).

## Licence and credit

GPLv3 (`LICENSE`, `ProvingGrounds/NOTICE.md`). Original Proving Grounds by Shutori, source:
https://github.com/shutori/Shutori_Re_Kenshi_Source. This port is a translation of that code and uses none of its art.
