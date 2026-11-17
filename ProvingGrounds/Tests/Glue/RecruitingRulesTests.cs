using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The recruit adapter's order and outcomes (Core/Glue/RecruitingRules.cs, task 6.4a review fix): the cap, the release, the join, a throw
/// read back (a player = joined; else put back in its cage, that checked).</summary>
public class RecruitingRulesTests
{
    sealed class Fake : IRecruitingHost
    {
        public bool Part = true, Dialogue = true, Talker = true, Resolves_ = true, Imprisoned = true, Cage = true, ReleaseOk = true, JoinOk = true;
        public bool IsPlayerThrows;
        public bool ReleaseThrows, JoinThrows, PutBackOk = true, PutBackThrows, BecamePlayer, ReleasedBeforeThrow;
        public int Heads = 3, Max = 30;
        public bool Player;
        public readonly List<string> Calls = [], Logs = [];
        public bool PartAvailable() => Part;
        public bool HasDialogue() => Dialogue;
        public bool HasTalker() => Talker;
        public bool Resolves() => Resolves_;
        public int PlayerHeads() => Heads;
        public int MaxFactionSize() => Max;
        public bool IsImprisoned() => Imprisoned;
        public bool RememberCage() { Calls.Add("cage"); return Cage; }
        public bool Release()
        {
            Calls.Add("release");
            if (ReleaseThrows) { if (ReleasedBeforeThrow) Imprisoned = false; throw new InvalidOperationException("release"); }
            if (ReleaseOk) Imprisoned = false;
            return ReleaseOk;
        }
        public bool JoinPlayer()
        {
            Calls.Add("join");
            if (JoinThrows) { Player = BecamePlayer; throw new InvalidOperationException("join"); }
            Player = JoinOk;
            return JoinOk;
        }
        public bool IsPlayer() => IsPlayerThrows ? throw new InvalidOperationException("is player") : Player;
        public bool PutBack()
        {
            Calls.Add("putback");
            if (PutBackThrows) throw new InvalidOperationException("put back");
            if (PutBackOk) Imprisoned = true;
            return PutBackOk;
        }
        public void Log(string line) => Logs.Add(line);
    }

    static (RecruitOutcome O, Fake F) Run(Action<Fake> set)
    {
        var f = new Fake();
        set(f);
        return (RecruitingRules.Recruit(f), f);
    }

    [Fact]
    public void Joined()
    {
        var (o, f) = Run(_ => { });
        Assert.Equal(RecruitOutcome.Joined, o);
        Assert.Equal(["cage", "release", "join"], f.Calls);
        Assert.True(RecruitingRules.IsJoined(o));
        Assert.Empty(f.Logs);
    }

    [Fact]   // not caged: no release
    public void Not_imprisoned_joins_directly()
    {
        var (o, f) = Run(x => x.Imprisoned = false);
        Assert.Equal(RecruitOutcome.Joined, o);
        Assert.Equal(["join"], f.Calls);
    }

    [Fact]   // the checks before anything moves
    public void Refusals_before_anything_moves()
    {
        Assert.Equal(RecruitOutcome.PartOff, Run(x => x.Part = false).O);
        Assert.Equal(RecruitOutcome.NoDialogue, Run(x => x.Dialogue = false).O);
        Assert.Equal(RecruitOutcome.NoTalker, Run(x => x.Talker = false).O);
        Assert.Equal(RecruitOutcome.StaleId, Run(x => x.Resolves_ = false).O);
        var (o, f) = Run(x => x.Heads = 30);   // at the cap (>=)
        Assert.Equal(RecruitOutcome.FactionFull, o);
        Assert.Empty(f.Calls);
        Assert.Equal(RecruitOutcome.Joined, Run(x => x.Heads = 29).O);
    }

    [Fact]
    public void Release_refused()
    {
        var (o, f) = Run(x => x.ReleaseOk = false);
        Assert.Equal(RecruitOutcome.ReleaseRefused, o);
        Assert.Equal(["cage", "release"], f.Calls);
        Assert.False(RecruitingRules.IsJoined(o));
    }

    [Fact]   // the release throws: still caged -> nothing to undo; out of the cage -> put back (checked)
    public void Release_throws()
    {
        var (o, f) = Run(x => x.ReleaseThrows = true);
        Assert.Equal(RecruitOutcome.ReleaseThrew, o);
        Assert.DoesNotContain("putback", f.Calls);
        (o, f) = Run(x => { x.ReleaseThrows = true; x.ReleasedBeforeThrow = true; });
        Assert.Equal(RecruitOutcome.ReleaseThrew, o);
        Assert.Contains("putback", f.Calls);
        Assert.True(f.Imprisoned);
        (o, f) = Run(x => { x.ReleaseThrows = true; x.ReleasedBeforeThrow = true; x.PutBackOk = false; });
        Assert.Equal(RecruitOutcome.ReleaseThrewPutBackFailed, o);
        Assert.Single(f.Logs);
    }

    [Fact]   // the join refuses: back in the cage, and the put-back is checked
    public void Join_refused()
    {
        var (o, f) = Run(x => x.JoinOk = false);
        Assert.Equal(RecruitOutcome.JoinRefused, o);
        Assert.Equal(["cage", "release", "join", "putback"], f.Calls);
        Assert.True(f.Imprisoned);
        Assert.Empty(f.Logs);
        (o, f) = Run(x => { x.JoinOk = false; x.PutBackOk = false; });
        Assert.Equal(RecruitOutcome.JoinRefusedPutBackFailed, o);
        Assert.Single(f.Logs);
        (o, _) = Run(x => { x.JoinOk = false; x.PutBackThrows = true; });
        Assert.Equal(RecruitOutcome.JoinRefusedPutBackFailed, o);
        (o, f) = Run(x => { x.JoinOk = false; x.Cage = false; });   // no cage registered: nothing to put it back in
        Assert.Equal(RecruitOutcome.JoinRefusedPutBackFailed, o);
        Assert.DoesNotContain("putback", f.Calls);
        (o, f) = Run(x => { x.JoinOk = false; x.Imprisoned = false; });   // never caged: nothing to put back
        Assert.Equal(RecruitOutcome.JoinRefused, o);
        Assert.DoesNotContain("putback", f.Calls);
    }

    [Fact]   // the join throws: read back: a player character = joined (no put-back); not a player = put back, checked
    public void Join_throws()
    {
        var (o, f) = Run(x => { x.JoinThrows = true; x.BecamePlayer = true; });
        Assert.Equal(RecruitOutcome.JoinThrewJoined, o);
        Assert.True(RecruitingRules.IsJoined(o));
        Assert.DoesNotContain("putback", f.Calls);
        Assert.Single(f.Logs);
        (o, f) = Run(x => x.JoinThrows = true);
        Assert.Equal(RecruitOutcome.JoinThrew, o);
        Assert.False(RecruitingRules.IsJoined(o));
        Assert.Contains("putback", f.Calls);
        Assert.True(f.Imprisoned);
        (o, _) = Run(x => { x.JoinThrows = true; x.PutBackOk = false; });
        Assert.Equal(RecruitOutcome.JoinThrewPutBackFailed, o);
    }

    [Fact]   // re-review fix: the join throws and the player read throws too: not known, never put back in a cage (it may be a player now)
    public void Join_throws_and_the_read_throws()
    {
        var (o, f) = Run(x => { x.JoinThrows = true; x.BecamePlayer = true; x.IsPlayerThrows = true; });
        Assert.Equal(RecruitOutcome.JoinThrewUnknown, o);
        Assert.False(RecruitingRules.IsJoined(o));
        Assert.DoesNotContain("putback", f.Calls);
        Assert.False(f.Imprisoned);
        Assert.Single(f.Logs);
    }
}
