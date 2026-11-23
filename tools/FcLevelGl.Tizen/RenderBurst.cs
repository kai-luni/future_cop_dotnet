using System.Threading;

namespace FcLevelGlTizen;

// The current preview is static between camera turns; stop requesting frames when it is idle.
internal sealed class RenderBurst
{
    private const int FrameBudget = 120;
    private int _remaining = FrameBudget;

    public void Request() => Interlocked.Exchange(ref _remaining, FrameBudget);

    public bool TryConsume()
    {
        while (true)
        {
            int remaining = Volatile.Read(ref _remaining);
            if (remaining <= 0) return false;
            if (Interlocked.CompareExchange(ref _remaining, remaining - 1, remaining) == remaining)
                return true;
        }
    }
}
