using Sill.Windows.Video;

namespace Sill.Checks;

// KeyframeRecovery (Video/KeyframeRecovery.cs), the iOS client's rule in C#: the cases of upstream's
// Tests/checks/keyframe-recovery that hold on this PC, then random sessions against the rule's
// invariants (never stuck while open and streaming; never two asks closer than 2 s).
static class KeyframeChecks
{
    const double W = 4;   // the wait of an ask on a quick link

    static KeyframeDecision C(this KeyframeRecovery r, double now) => r.Consider(now, active: true, streaming: true, wait: W);
    static KeyframeDecision At(KeyframeAsk ask, double at) => new(ask, at);

    public static void Run()
    {
        Check.Equal(4.0, KeyframeRecovery.AnswerWait(null), "no round trip measured yet: an ask waits 4 s");
        Check.That(KeyframeRecovery.AnswerWait(0) == 4 && KeyframeRecovery.AnswerWait(300) == 4 && KeyframeRecovery.AnswerWait(1000) == 4, "quick round trips: 4 s");
        Check.Equal(6.0, KeyframeRecovery.AnswerWait(1500), "1.5 s round trips: four of them, 6 s");
        Check.Equal(12.0, KeyframeRecovery.AnswerWait(3000), "3 s round trips: 12 s");
        Check.Equal(4.0, KeyframeRecovery.AnswerWait(-20), "a negative round trip counts as none");

        var r = new KeyframeRecovery();
        Check.Equal(KeyframeDecision.Nothing, r.C(10), "no interruption: nothing to ask");
        r.Interrupted(10);
        Check.That(r.Open && !r.Asked, "an interruption opens");
        Check.Equal(KeyframeDecision.AskNow, r.C(10), "the first ask goes at once");
        Check.That(r.Asked && r.LastAsk == 10, "the ask is counted");
        Check.Equal(At(KeyframeAsk.KeyframeDue, 14), r.C(10.1), "one ask per interruption while its keyframe is due (4 s after the ask)");
        Check.Equal(At(KeyframeAsk.KeyframeDue, 14), r.C(13.999), "just under 4 s: still due");
        r.Interrupted(11);
        Check.That(r.C(11) == At(KeyframeAsk.KeyframeDue, 14) && r.Repeats == 0, "interrupted again while open: the same interruption, no second ask, no repeat");

        var answered = new KeyframeRecovery();
        answered.Interrupted(0); answered.C(0);
        answered.PictureBack(0.1, active: true);
        Check.That(!answered.Open && answered.C(4) == KeyframeDecision.Nothing && answered.C(100) == KeyframeDecision.Nothing, "a picture drawn closes the interruption: nothing more asked");

        // An ask whose keyframe never comes (a Mac from before kind 44 skips it).
        var unans = new KeyframeRecovery();
        unans.Interrupted(0); unans.C(0);
        Check.Equal(KeyframeDecision.AskNow, unans.C(4), "no keyframe 4 s after the ask: asked again");
        Check.That(unans.Repeats == 1 && unans.SpacingNow == 4, "an unanswered ask is a repeat: the spacing doubles");
        Check.Equal(At(KeyframeAsk.KeyframeDue, 8), unans.C(7.9), "the second ask's keyframe is due 4 s after it");
        Check.Equal(At(KeyframeAsk.AskAt, 12), unans.C(8), "none: owed again, held to 8 s after the second ask");
        Check.That(!unans.Asked && unans.Repeats == 2, "and not counted until then");
        Check.Equal(KeyframeDecision.AskNow, unans.C(12), "then asked");

        // A Mac that never answers: a bounded series of asks.
        var never = new KeyframeRecovery();
        never.Interrupted(0);
        var asks = new List<double>();
        for (double t = 0; t <= 300; t += 0.25) if (never.C(t) == KeyframeDecision.AskNow) asks.Add(t);
        Check.That(asks.SequenceEqual(new double[] { 0, 4, 12, 28, 60, 92, 124, 156, 188, 220, 252, 284 }),
                   $"a Mac that never answers is asked again after 4, 8 and 16 s, then every 32 s ({string.Join(", ", asks)})");
        Check.That(never.Open && never.SpacingNow == 32, "still open, the spacing at its cap");

        // On a slow link an ask waits four round trips.
        var slow = new KeyframeRecovery();
        slow.Interrupted(0); slow.Consider(0, true, true, 10);
        Check.Equal(At(KeyframeAsk.KeyframeDue, 10), slow.Consider(9.9, true, true, 10), "on a slow link the keyframe is due four round trips after the ask (10 s)");
        Check.Equal(KeyframeDecision.AskNow, slow.Consider(10, true, true, 10), "then asked again, the wait longer than the spacing");
        Check.Equal(At(KeyframeAsk.KeyframeDue, 14), slow.Consider(12, true, true, 4), "a quicker link: the next ask's keyframe due 4 s after it");

        // A picture lost again at once after it came back.
        var lost = new KeyframeRecovery();
        lost.Interrupted(0); lost.C(0);
        lost.PictureBack(0.1, true);
        lost.Interrupted(0.1);
        Check.That(lost.Open && !lost.Asked && lost.Repeats == 1, "back and lost again at once: a repeat");
        Check.Equal(At(KeyframeAsk.AskAt, 4), lost.C(0.1), "asked again at the repeat's spacing (4 s)");
        Check.Equal(KeyframeDecision.AskNow, lost.C(4), "and asked");
        lost.PictureBack(4.1, true); lost.Interrupted(4.1);
        Check.Equal(At(KeyframeAsk.AskAt, 12), lost.C(4.1), "lost again: 8 s");

        // Nothing streams.
        var idle = new KeyframeRecovery();
        idle.Interrupted(20);
        Check.That(idle.Consider(20, true, false, W) == KeyframeDecision.Nothing && idle.LastAsk == null, "nothing streams: no ask, none counted");
        Check.Equal(KeyframeDecision.AskNow, idle.C(20.5), "a stream again: asked");

        // The base spacing.
        var sp = new KeyframeRecovery();
        sp.Interrupted(100); sp.C(100);
        sp.PictureBack(100.5, true);
        sp.LeftForeground();
        sp.Interrupted(101);
        Check.That(sp.Repeats == 0 && sp.SpacingNow == 2, "a new interruption (not a repeat): the base spacing");
        Check.Equal(At(KeyframeAsk.AskAt, 102), sp.C(101), "1 s after the last ask: owed at 2 s");
        Check.Equal(At(KeyframeAsk.AskAt, 102), sp.C(101.999), "just under 2 s: still owed");
        Check.Equal(KeyframeDecision.AskNow, sp.C(102), "at 2 s: asked");

        // A picture that fails again right after each keyframe: 4, 8, 16, 32, 32, 32 s.
        var rep = new KeyframeRecovery();
        double now = 0, lastAsk = 0;
        rep.Interrupted(now); rep.C(now);
        var gaps = new List<double>();
        for (int i = 0; i < 6; i++)
        {
            rep.PictureBack(now + 0.1, true);
            rep.Interrupted(now + 0.2);
            var d = rep.C(now + 0.2);
            if (d.Ask != KeyframeAsk.AskAt) { Check.That(false, "a repeat is held back"); break; }
            now = d.At;
            Check.That(rep.C(now) == KeyframeDecision.AskNow, $"a repeat asked at {now - lastAsk} s");
            gaps.Add(now - lastAsk);
            lastAsk = now;
        }
        Check.That(gaps.SequenceEqual(new double[] { 4, 8, 16, 32, 32, 32 }), $"the spacing doubles for repeats, up to 32 s ({string.Join(", ", gaps)})");

        // Calm.
        var calm = new KeyframeRecovery();
        calm.Interrupted(0); calm.C(0); calm.PictureBack(1, true); calm.Interrupted(11);
        Check.That(calm.Repeats == 0 && calm.SpacingNow == 2, "back for 10 s: a new interruption, the base spacing");
        var nearly = new KeyframeRecovery();
        nearly.Interrupted(0); nearly.C(0); nearly.PictureBack(1, true); nearly.Interrupted(10.99);
        Check.That(nearly.Repeats == 1 && nearly.C(10.99) == KeyframeDecision.AskNow, "back for just under 10 s: a repeat, not held by its 4 s 11 s after the last ask");

        // A picture back with nothing open; the owed ask made moot.
        var stray = new KeyframeRecovery();
        stray.Interrupted(0); stray.C(0); stray.PictureBack(1, true); stray.PictureBack(9, true); stray.Interrupted(11.5);
        Check.Equal(0, stray.Repeats, "a picture with nothing open does not move when the picture came back");
        var moot = new KeyframeRecovery();
        moot.Interrupted(0); moot.C(0); moot.PictureBack(0.1, true); moot.Interrupted(0.2);
        Check.Equal(At(KeyframeAsk.AskAt, 4), moot.C(0.2), "owed at 4 s");
        moot.PictureBack(1, true);
        Check.Equal(KeyframeDecision.Nothing, moot.C(4), "the picture back before then: nothing to ask");

        // A clock gone back never leaves an ask stuck.
        var clock = new KeyframeRecovery();
        clock.Interrupted(200); clock.C(200);
        Check.Equal(KeyframeDecision.AskNow, clock.C(10), "a clock before an ask that is out: asked again, never waited for");

        var cap = new KeyframeRecovery();
        double ct = 0;
        cap.Interrupted(ct); cap.C(ct);
        for (int i = 0; i < 20; i++)
        {
            cap.PictureBack(ct + 0.1, true); cap.Interrupted(ct + 0.2);
            if (cap.C(ct + 0.2) is { Ask: KeyframeAsk.AskAt } held) { ct = held.At; cap.C(ct); }
        }
        Check.That(cap.SpacingNow == 32 && cap.Repeats == 20, $"twenty repeats: still 32 s (repeats {cap.Repeats})");

        // Random sessions as the session drives it: events at random, looked at every 250 ms.
        var random = new Random(44);
        int stuck = 0, tooClose = 0, sessions = 2000, totalAsks = 0;
        for (int s = 0; s < sessions; s++)
        {
            var k = new KeyframeRecovery();
            double? last = null;
            bool streaming = true;
            for (double t = 0; t < 120; t += 0.25)
            {
                double roll = random.NextDouble();
                if (roll < 0.02) k.Interrupted(t);
                else if (roll < 0.03) k.PictureBack(t, true);
                else if (roll < 0.035) streaming = !streaming;
                double wait = random.NextDouble() < 0.1 ? 10 : 4;
                var d = k.Consider(t, true, streaming, wait);
                if (d.Ask == KeyframeAsk.AskNow)
                {
                    totalAsks++;
                    if (last is { } l && t - l < KeyframeRecovery.Spacing) tooClose++;
                    last = t;
                }
                else if (d.Ask is KeyframeAsk.AskAt or KeyframeAsk.KeyframeDue)
                {
                    if (d.At < t) stuck++;   // a time to look again that has already passed would never come
                }
                else if (k.Open && streaming) stuck++;
            }
        }
        Check.Equal(0, stuck, $"{sessions} random sessions: open and streaming, the rule always names a time to look again, never one past");
        Check.Equal(0, tooClose, $"… and never two asks under 2 s apart ({totalAsks} asks)");
    }
}
