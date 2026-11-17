"""Writes ProvingGroundsMeitou.mod: an FCS mod with no records whose header names 'Proving Grounds.mod' as a dependency."""
import pathlib, struct
def s(t: str) -> bytes:
    b = t.encode("utf-8"); return struct.pack("<i", len(b)) + b
ROOT = pathlib.Path(__file__).resolve().parent.parent
data = struct.pack("<ii", 16, 1) + s("") + s("Proving Grounds for Meitou: the Proving Grounds arena rebuilt as a Meitou code mod. Needs Proving Grounds (Workshop 3774745195) above it.") \
     + s("Proving Grounds.mod") + s("") + struct.pack("<ii", 0, 0)
(ROOT / "ProvingGroundsMeitou.mod").write_bytes(data)
print("wrote", ROOT / "ProvingGroundsMeitou.mod", len(data), "bytes")
