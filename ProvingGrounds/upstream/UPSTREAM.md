# Upstream: Proving Grounds (RE_Kenshi plugin) source

- Source: https://github.com/shutori/Shutori_Re_Kenshi_Source (folder `ProvingGrounds`)
- Commit: `4a20d48` (2026-09-26, "Update Proving Grounds source to latest"); matches Workshop item 3774745195, version 1.3.4 of 2026-09-26
- Licence: GPLv3, Copyright (C) 2026 shutori (see `LICENSE` here and in the repo root)
- This is a read-only copy for reference. It is never compiled or run from here. Line numbers in the port plan refer to this copy.
- Read-only: every file here carries the Windows read-only attribute (`attrib +R /S /D upstream\*`, set from a .bat; `chmod` has no
  effect on the C: mount). To copy it for a build use `cp -r --no-preserve=mode` (the copy is writable). To edit or delete it, first
  `attrib -R /S /D upstream\*`.
