using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/FighterIdentityPolicy.cs (task 1.5); each expected value is read off the C++.

public class FighterIdentityPolicyTests
{
    sealed class Obj { }   // a class key: compared by reference, like the C++ object pointer

    [Fact]   // FighterIdentityPolicy.h:25-37, 76-81
    public void Lifetimes()
    {
        var r = new FighterIdentityPolicy.Registry<Obj>();
        var a = new Obj(); var b = new Obj();
        Assert.Equal(0UL, r.Begin(null));
        Assert.Equal(0UL, r.Current(a));
        Assert.Equal(1UL, r.Begin(a));
        Assert.Equal(2UL, r.Begin(b));
        Assert.Equal(3UL, r.Begin(a));   // a new lifetime replaces the entry
        Assert.Equal(3UL, r.Current(a));
        r.End(a, 1);                      // stale lifetime: kept
        Assert.Equal(3UL, r.Current(a));
        r.End(a, 3);
        Assert.Equal(0UL, r.Current(a));
        Assert.Equal(2UL, r.Current(b));
        r.Clear();
        Assert.Equal(0UL, r.Current(b));
        Assert.Equal(4UL, r.Begin(b));   // Clear keeps the sequence
    }

    [Fact]   // FighterIdentityPolicy.h:38-75
    public void Load_bind_find()
    {
        var r = new FighterIdentityPolicy.Registry<Obj>();
        var a = new Obj();
        r.Load(a, false, "id-a", out bool conflict);   // loadFromSerialise(NULL): no lifetime
        Assert.False(conflict);
        Assert.Equal(0UL, r.Current(a));
        r.Load(a, true, "", out conflict);              // legacy save: a lifetime, no id
        Assert.Equal(1UL, r.Current(a));
        Assert.False(r.Find(a, 1, out string id));
        Assert.Equal("", id);
        Assert.False(r.Bind(a, 2, "id-a", out conflict));   // wrong lifetime
        Assert.False(r.Bind(a, 1, "", out conflict));       // empty id
        Assert.True(r.Bind(a, 1, "id-a", out conflict));
        Assert.False(conflict);
        Assert.True(r.Bind(a, 1, "id-a", out conflict));    // same id again
        Assert.False(r.Bind(a, 1, "id-b", out conflict));   // another id over a bound one
        Assert.True(r.Find(a, 1, out id));
        Assert.Equal("id-a", id);
        Assert.False(r.Find(a, 2, out id));
        Assert.Equal("", id);
    }

    [Fact]   // FighterIdentityPolicy.h:47-65: a duplicate blocks both, stickily
    public void Duplicate_ids_conflict_and_stay_blocked()
    {
        var r = new FighterIdentityPolicy.Registry<Obj>();
        var a = new Obj(); var b = new Obj(); var c = new Obj();
        r.Load(a, true, "dup", out bool conflict);
        Assert.False(conflict);
        r.Load(b, true, "dup", out conflict);
        Assert.True(conflict);
        Assert.False(r.Find(a, 1, out string id)); Assert.Equal("", id);
        Assert.False(r.Find(b, 2, out id)); Assert.Equal("", id);
        Assert.True(r.SavedId(a, 1, out id)); Assert.Equal("dup", id);   // still serialised
        ulong lc = r.Begin(c);
        Assert.False(r.Bind(c, lc, "dup", out conflict));   // already conflicted: no new conflict reported
        Assert.False(conflict);
        r.End(b, 2);
        r.End(c, lc);
        Assert.False(r.Find(a, 1, out id));                 // sticky after the duplicate is gone
        Assert.False(r.Bind(a, 1, "dup", out conflict));
        r.Clear();
        ulong la = r.Begin(a);
        Assert.True(r.Bind(a, la, "dup", out conflict));    // Clear forgets the conflicts
    }

    [Fact]   // value key (the glue's character key): default equality
    public void Value_keys()
    {
        var r = new FighterIdentityPolicy.Registry<string>();
        ulong l = r.Begin("char-1");
        Assert.True(r.Bind("char-1", l, "x", out _));
        Assert.True(r.Find(new string("char-1".ToCharArray()), l, out string id));
        Assert.Equal("x", id);
    }

    [Fact]   // FighterIdentityPolicy.h:27, 35, 50, 71, 78: the no-key value plays the C++ null pointer
    public void Value_key_with_a_no_key_value()
    {
        var r = new FighterIdentityPolicy.Registry<ulong>(isNoKey: k => k == 0);
        Assert.Equal(0UL, r.Begin(0));                 // `if (!object ...) return 0;`
        Assert.Equal(0UL, r.Current(0));
        r.Load(0, true, "x", out bool conflict);       // Begin refuses, so Bind finds no entry
        Assert.False(conflict);
        Assert.False(r.Bind(0, 0, "x", out conflict));
        Assert.False(r.Find(0, 0, out string id)); Assert.Equal("", id);
        Assert.False(r.SavedId(0, 0, out id)); Assert.Equal("", id);
        r.End(0, 0);
        Assert.Equal(1UL, r.Begin(7));                 // the refused key used no lifetime
        Assert.True(r.Bind(7, 1, "x", out conflict));
        Assert.False(conflict);

        var plain = new FighterIdentityPolicy.Registry<ulong>();   // no isNoKey: 0 is a real key
        Assert.Equal(1UL, plain.Begin(0));
    }
}
