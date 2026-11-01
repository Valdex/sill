namespace Sill.Windows.Video;

/// <summary>What <see cref="KeyframeRecovery.Consider"/> says: nothing to ask; ask now (counted);
/// owed but held back by the spacing until <see cref="KeyframeDecision.At"/>; or the ask is out and
/// its keyframe is due by <see cref="KeyframeDecision.At"/>.</summary>
public enum KeyframeAsk { Nothing, AskNow, AskAt, KeyframeDue }

public readonly record struct KeyframeDecision(KeyframeAsk Ask, double At = 0)
{
    public static readonly KeyframeDecision Nothing = new(KeyframeAsk.Nothing);
    public static readonly KeyframeDecision AskNow = new(KeyframeAsk.AskNow);
}

/// <summary>When the PC asks the Mac for a keyframe after its picture was interrupted (kind 44,
/// issue #101): the decoder was made again or failed, the Direct3D device was lost, or frames came
/// while the decoder waited for a keyframe, which a still window on the Mac never sends by itself.
/// The Mac answers by encoding its last frame again; never a pick of the source again, which would
/// restart every device's stream. The rule is the iOS client's (iOSClient/KeyframeRecovery.swift,
/// checked by Tests/checks/keyframe-recovery), line for line:
/// <list type="bullet">
/// <item>One ask per interruption while its keyframe is due; an interruption runs from the picture
///   lost to the next picture drawn.</item>
/// <item>An ask whose keyframe has not come within its wait (<see cref="AnswerWait"/>: 4 s, or four
///   of the worst round trips) is owed again, as a repeat: a Mac from before kind 44 skips the ask,
///   and a keyframe can be lost. So a Mac that never answers is asked after 4, 8 and 16 s, then every
///   32 s, which costs it nothing.</item>
/// <item>At least <see cref="Spacing"/> (2 s) after the last ask, whatever interrupted the picture;
///   an ask the spacing holds back is owed until then.</item>
/// <item>Repeats wait longer, so a decoder that cannot keep up never costs every device a keyframe
///   every 2 s: a picture that fails again within <see cref="Calm"/> (10 s) of its keyframe, or an
///   ask that went unanswered, doubles the spacing (4, 8, 16, then 32 s at most). A picture back for
///   10 s starts it at 2 s again.</item>
/// <item>Only while something streams, and while the app is active (iOS's background; this PC decodes
///   whatever its window's state, so it always is, and <see cref="LeftForeground"/> is kept for the
///   rule's sake).</item>
/// </list>
/// Pure: the session feeds it the pipeline's events and a clock in seconds (a Stopwatch's), under its
/// own lock. WindowsClient/Sill.Checks checks it with the iOS check's cases.</summary>
public sealed class KeyframeRecovery
{
    /// <summary>The least time between two asks.</summary>
    public const double Spacing = 2;
    /// <summary>The most, for a picture that fails again after each keyframe, or a Mac that never answers.</summary>
    public const double LongestSpacing = 32;
    /// <summary>A picture back this long has recovered: the next interruption is not a repeat.</summary>
    public const double Calm = 10;
    /// <summary>The least time an ask waits for its keyframe before it is owed again.</summary>
    public const double ShortestWait = 4;

    /// <summary>How long an ask waits for its keyframe: 4 s, or four of the worst round trips of the
    /// last second that measured one (none measured counts as 4 s).</summary>
    public static double AnswerWait(double? worstRttMs) => Math.Max(ShortestWait, 4 * (worstRttMs ?? 0) / 1000);

    /// <summary>The picture was lost and none has been drawn since.</summary>
    public bool Open { get; private set; }
    /// <summary>An ask is out for this interruption, waiting for its keyframe.</summary>
    public bool Asked { get; private set; }
    /// <summary>Asks in a row that did not bring the picture back for good; each doubles the spacing.</summary>
    public int Repeats { get; private set; }
    /// <summary>When the last ask went out.</summary>
    public double? LastAsk { get; private set; }
    double? backAt;
    bool wentAway;

    /// <summary>The spacing the next ask keeps from the last one.</summary>
    public double SpacingNow => Math.Min(Spacing * Math.Pow(2, Repeats), LongestSpacing);

    /// <summary>The picture waits for a keyframe. Opens an interruption, or is part of the open one.</summary>
    public void Interrupted(double now)
    {
        if (Open) return;
        Open = true;
        Asked = false;
        if (!wentAway && backAt is { } back && now >= back && now - back < Calm) Repeats++;
        else Repeats = 0;
    }

    /// <summary>A picture was drawn after the wait: the interruption is over.</summary>
    public void PictureBack(double now, bool active)
    {
        if (!Open) return;
        Open = false;
        backAt = now;
        if (active) wentAway = false;
    }

    /// <summary>The app is no longer active: an ask that was out is owed again, and the spacing starts at 2 s again.</summary>
    public void LeftForeground()
    {
        wentAway = true;
        Asked = false;
        Repeats = 0;
    }

    /// <summary>Whether to ask now; counts the ask when it says so.</summary>
    public KeyframeDecision Consider(double now, bool active, bool streaming, double wait)
    {
        if (!Open || !active || !streaming) return KeyframeDecision.Nothing;
        if (Asked)
        {
            if (LastAsk is { } out1 && now >= out1 && now < out1 + wait) return new(KeyframeAsk.KeyframeDue, out1 + wait);
            // None came: owed again, as a repeat, so a Mac that never answers is asked ever less often.
            Asked = false;
            Repeats++;
        }
        // Against the time it is due, not now - last < spacing: (last + s) - last can round below s.
        if (LastAsk is { } last && now >= last && now < last + SpacingNow) return new(KeyframeAsk.AskAt, last + SpacingNow);
        Asked = true;
        LastAsk = now;
        return KeyframeDecision.AskNow;
    }
}
