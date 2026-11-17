using ProvingGrounds.Core.Glue;

namespace ProvingGrounds.Tests.Glue;

/// <summary>The recruitment window's request numbering (Core/Glue/RecruitmentRequestGate.cs, task 6.4b).</summary>
public class RecruitmentRequestGateTests
{
    [Fact]   // the sim drops a RecruitPrisoner at or below its LastSeq (6.4a review): the window numbers above it and waits for each answer
    public void The_request_gate_numbers_above_last_seq_and_waits_for_the_answer()
    {
        var g = new RecruitmentRequestGate();
        var world = new object();
        g.FollowWorld(world);
        Assert.Equal(1, g.Send(0));
        Assert.True(g.Waiting(0));
        Assert.Equal(0, g.Send(0));          // unanswered: nothing sent
        Assert.Equal(2, g.Send(1));          // answered (LastSeq 1)
        Assert.Equal(1_000_006, g.Send(1_000_005));   // a test verb's request recorded later: numbered above it
        Assert.False(g.Waiting(1_000_007));  // a later request recorded: not waiting
        g.FollowWorld(world);
        Assert.True(g.Waiting(1_000_005));
        g.FollowWorld(new object());         // a new world: starts over
        Assert.False(g.Waiting(0));
        Assert.Equal(1, g.Send(0));
    }

    [Fact]   // task 7.6: the town window's row releases are always numbered, above everything sent and the published seq
    public void Row_releases_are_always_numbered_above_everything_sent()
    {
        var g = new RecruitmentRequestGate();
        g.FollowWorld(new object());
        Assert.Equal(1, g.Next(0));
        Assert.Equal(2, g.Next(0));    // a second release before the first answer: still sent
        Assert.Equal(11, g.Next(10));  // the published LastSeq moved past the gate
    }

    [Fact]   // task 7.6: a booking waits for its own answer, not for row releases, and keeps the order
    public void A_booking_waits_for_its_answer_but_not_for_row_releases()
    {
        var g = new RecruitmentRequestGate();
        g.FollowWorld(new object());
        Assert.Equal(1, g.Next(0));
        Assert.Equal(2, g.Send(0));
        Assert.Equal(0, g.Send(1));    // the first booking unanswered (LastSeq 1 < 2)
        Assert.Equal(3, g.Next(1));    // a row release still goes, above it
        Assert.Equal(4, g.Send(2));    // answered: the next booking goes
    }

    [Fact]   // task 7.6 quality review: an answer lost (the window closed before it came) is recovered by reopening: a new RequestSerial starts over
    public void A_lost_answer_is_recovered_by_a_new_request_serial()
    {
        var g = new RecruitmentRequestGate();
        g.FollowWorld(new object());
        g.FollowSerial(3);
        Assert.Equal(1, g.Send(0));
        Assert.Equal(0, g.Send(0));    // never answered: blocked
        g.FollowSerial(3);
        Assert.Equal(0, g.Send(0));    // the same opening: still blocked
        g.FollowSerial(5);             // closed and shown again
        Assert.False(g.Waiting(0));
        Assert.Equal(1, g.Send(0));
    }

    [Fact]   // task 7.6 quality review (pinned in 7.7): a restart on a new RequestSerial while the earlier booking N is still queued numbers the next
             // booking at or below N, so once the sim records N (LastSeq = N) that second booking is dropped by the sim's seq rule: the known cost of
             // starting over, accepted because the reopened window shows the first booking's answer
    public void A_booking_after_a_serial_restart_while_the_earlier_one_is_queued_is_numbered_at_or_below_it()
    {
        var g = new RecruitmentRequestGate();
        g.FollowWorld(new object());
        g.FollowSerial(3);
        Assert.Equal(1, g.Next(0));    // a row release
        long n = g.Send(0);            // booking N, queued, not yet recorded (published LastSeq still 0)
        Assert.Equal(2, n);
        g.FollowSerial(5);             // the window closed and shown again before N's answer
        long second = g.Send(0);
        Assert.True(second <= n);      // at or below N: the sim (LastSeq = N once it ran N) drops it
        Assert.Equal(1, second);
    }
}
