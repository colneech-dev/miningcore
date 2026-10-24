using System;
using Miningcore.Payments;
using Xunit;

namespace Miningcore.Tests.Payments;

public class NoticeThrottleTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstOccurrence_Notifies()
    {
        var throttle = new NoticeThrottle(TimeSpan.FromMinutes(30));
        Assert.True(throttle.ShouldNotify(T0));
    }

    [Fact]
    public void RepeatsInsideTheInterval_AreSuppressed()
    {
        var throttle = new NoticeThrottle(TimeSpan.FromMinutes(30));
        Assert.True(throttle.ShouldNotify(T0));

        // a payout run every 10 minutes: the next two stay quiet
        Assert.False(throttle.ShouldNotify(T0.AddMinutes(10)));
        Assert.False(throttle.ShouldNotify(T0.AddMinutes(20)));
    }

    [Fact]
    public void AfterTheInterval_NotifiesAgain_AndRestartsTheClock()
    {
        var throttle = new NoticeThrottle(TimeSpan.FromMinutes(30));
        Assert.True(throttle.ShouldNotify(T0));
        Assert.True(throttle.ShouldNotify(T0.AddMinutes(30)));

        // the interval is measured from the last notice that was actually sent
        Assert.False(throttle.ShouldNotify(T0.AddMinutes(40)));
        Assert.True(throttle.ShouldNotify(T0.AddMinutes(60)));
    }

    [Fact]
    public void Reset_MakesTheNextOccurrenceNotifyImmediately()
    {
        var throttle = new NoticeThrottle(TimeSpan.FromMinutes(30));
        Assert.True(throttle.ShouldNotify(T0));
        Assert.False(throttle.ShouldNotify(T0.AddMinutes(5)));

        throttle.Reset();   // funds came back and a payout succeeded

        Assert.True(throttle.ShouldNotify(T0.AddMinutes(6)));
    }
}
