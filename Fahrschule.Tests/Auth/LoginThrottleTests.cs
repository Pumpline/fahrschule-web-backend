using Fahrschule.Application.Auth;

namespace Fahrschule.Tests.Auth;

/// <summary>
/// Unit tests for the per-IP login throttle (brute-force protection).
///
/// The throttle replaces the per-account lockout: the first attempts from an IP
/// are free, then a cooldown begins from the 3rd failed attempt and grows
/// strongly with every further failure. These tests drive a fake clock so we
/// can "fast-forward" time without actually waiting.
/// </summary>
public class LoginThrottleTests
{
    /// <summary>A TimeProvider whose "now" we can move forward by hand.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private const string Ip = "203.0.113.7";

    [Fact]
    public void First_two_failures_are_free()
    {
        var throttle = new LoginThrottle(new FakeClock());

        Assert.True(throttle.Check(Ip).Allowed);
        throttle.RegisterFailure(Ip);
        Assert.True(throttle.Check(Ip).Allowed);
        throttle.RegisterFailure(Ip);

        // After two failures the client is still allowed to try.
        Assert.True(throttle.Check(Ip).Allowed);
    }

    [Fact]
    public void Third_failure_starts_a_short_cooldown()
    {
        var throttle = new LoginThrottle(new FakeClock());

        throttle.RegisterFailure(Ip);
        throttle.RegisterFailure(Ip);
        throttle.RegisterFailure(Ip); // 3rd → cooldown starts

        var status = throttle.Check(Ip);
        Assert.False(status.Allowed);
        // Base cooldown is 5 seconds.
        Assert.Equal(5, status.RetryAfterSeconds);
    }

    [Fact]
    public void Cooldown_grows_strongly_with_every_further_failure()
    {
        var clock = new FakeClock();
        var throttle = new LoginThrottle(clock);

        throttle.RegisterFailure(Ip);
        throttle.RegisterFailure(Ip);

        // 3rd failure → 5s; wait it out, 4th → 15s, 5th → 45s.
        throttle.RegisterFailure(Ip);
        Assert.Equal(5, throttle.Check(Ip).RetryAfterSeconds);

        clock.Advance(TimeSpan.FromSeconds(6));
        throttle.RegisterFailure(Ip);
        Assert.Equal(15, throttle.Check(Ip).RetryAfterSeconds);

        clock.Advance(TimeSpan.FromSeconds(16));
        throttle.RegisterFailure(Ip);
        Assert.Equal(45, throttle.Check(Ip).RetryAfterSeconds);
    }

    [Fact]
    public void Cooldown_is_capped_at_one_hour()
    {
        var throttle = new LoginThrottle(new FakeClock());

        // Hammer the IP far past the point where the formula would exceed 1h.
        // RegisterFailure never blocks, so we can register back-to-back; the
        // huge exponent must still be clamped to 3600 seconds.
        for (var i = 0; i < 20; i++)
        {
            throttle.RegisterFailure(Ip);
        }

        var status = throttle.Check(Ip);
        Assert.False(status.Allowed);
        Assert.Equal(3600, status.RetryAfterSeconds);
    }

    [Fact]
    public void Allowed_again_once_the_cooldown_has_elapsed()
    {
        var clock = new FakeClock();
        var throttle = new LoginThrottle(clock);

        throttle.RegisterFailure(Ip);
        throttle.RegisterFailure(Ip);
        throttle.RegisterFailure(Ip);
        Assert.False(throttle.Check(Ip).Allowed);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(throttle.Check(Ip).Allowed);
    }

    [Fact]
    public void Success_forgives_the_ip_immediately()
    {
        var throttle = new LoginThrottle(new FakeClock());

        throttle.RegisterFailure(Ip);
        throttle.RegisterFailure(Ip);
        throttle.RegisterFailure(Ip);
        Assert.False(throttle.Check(Ip).Allowed);

        throttle.RegisterSuccess(Ip);

        // After a successful sign-in the counter is gone - free again.
        Assert.True(throttle.Check(Ip).Allowed);
    }

    [Fact]
    public void Long_idle_period_forgets_past_failures()
    {
        var clock = new FakeClock();
        var throttle = new LoginThrottle(clock);

        throttle.RegisterFailure(Ip);
        throttle.RegisterFailure(Ip);
        throttle.RegisterFailure(Ip);

        // After more than the "forget" window (2h) the IP starts fresh.
        clock.Advance(TimeSpan.FromHours(3));
        Assert.True(throttle.Check(Ip).Allowed);

        // And the next failure is treated as a first one (still free).
        throttle.RegisterFailure(Ip);
        Assert.True(throttle.Check(Ip).Allowed);
    }

    [Fact]
    public void Different_ips_are_throttled_independently()
    {
        var throttle = new LoginThrottle(new FakeClock());

        throttle.RegisterFailure("198.51.100.1");
        throttle.RegisterFailure("198.51.100.1");
        throttle.RegisterFailure("198.51.100.1"); // this IP is now blocked

        Assert.False(throttle.Check("198.51.100.1").Allowed);
        // A different IP is unaffected.
        Assert.True(throttle.Check("198.51.100.2").Allowed);
    }
}
