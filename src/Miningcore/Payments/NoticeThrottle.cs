namespace Miningcore.Payments;

/// <summary>
/// Lets a repeating, self-clearing condition notify an operator once per interval instead of on every payout run.
/// The caller supplies the time so the behaviour is testable.
/// </summary>
public sealed class NoticeThrottle
{
    public NoticeThrottle(TimeSpan interval)
    {
        this.interval = interval;
    }

    private readonly TimeSpan interval;
    private readonly object gate = new();
    private DateTime? lastNotice;

    /// <summary>True (and starts a new interval) when the last notice is older than the interval or there was none.</summary>
    public bool ShouldNotify(DateTime nowUtc)
    {
        lock(gate)
        {
            if(lastNotice != null && nowUtc - lastNotice.Value < interval)
                return false;

            lastNotice = nowUtc;
            return true;
        }
    }

    /// <summary>Forget the last notice, so the next occurrence notifies immediately (call when the condition has cleared).</summary>
    public void Reset()
    {
        lock(gate)
            lastNotice = null;
    }
}
