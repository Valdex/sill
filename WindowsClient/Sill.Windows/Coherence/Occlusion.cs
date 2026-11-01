namespace Sill.Windows.Coherence;

/// <summary>Whether a Coherence window is wholly covered by the windows above it, so its stream can
/// be paused as a minimized one's is (kind 29's `hidden`): the Mac then encodes nothing for it. Pure:
/// rectangles in screen pixels, the windows above as Windows stacks them (CoherenceHost reads them).
/// A window is covered only when every pixel of it lies under some opaque window above; any sliver
/// left showing keeps its stream, since a sliver can be what the user is watching.</summary>
public static class Occlusion
{
    public readonly record struct Box(int Left, int Top, int Right, int Bottom)
    {
        public bool Empty => Right <= Left || Bottom <= Top;
    }

    /// <summary>True when the union of <paramref name="above"/> covers all of <paramref name="target"/>.</summary>
    public static bool Covered(Box target, IEnumerable<Box> above)
    {
        if (target.Empty) return false;
        var left = new List<Box> { target };
        foreach (var a in above)
        {
            if (a.Empty) continue;
            var next = new List<Box>(left.Count + 4);
            foreach (var r in left) Subtract(r, a, next);
            left = next;
            if (left.Count == 0) return true;
            // A pathological stack (hundreds of tiny windows) is never worth the work: still showing.
            if (left.Count > 256) return false;
        }
        return false;
    }

    /// <summary>r minus a, as up to four boxes (top band, bottom band, left and right of the middle).</summary>
    static void Subtract(Box r, Box a, List<Box> into)
    {
        if (a.Right <= r.Left || a.Left >= r.Right || a.Bottom <= r.Top || a.Top >= r.Bottom) { into.Add(r); return; }
        if (a.Top > r.Top) into.Add(new Box(r.Left, r.Top, r.Right, a.Top));
        if (a.Bottom < r.Bottom) into.Add(new Box(r.Left, a.Bottom, r.Right, r.Bottom));
        int top = Math.Max(r.Top, a.Top), bottom = Math.Min(r.Bottom, a.Bottom);
        if (a.Left > r.Left) into.Add(new Box(r.Left, top, a.Left, bottom));
        if (a.Right < r.Right) into.Add(new Box(a.Right, top, r.Right, bottom));
    }

    /// <summary>The pause waits for two looks in a row (a window dragged across another must not stop
    /// and start its stream); showing again is at once.</summary>
    public static bool Hide(bool coveredNow, int coveredLooks) => coveredNow && coveredLooks >= 2;
}
