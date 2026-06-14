using System.Collections.Concurrent;

namespace Fahrschule.Application.Auth;

/// <summary>The throttle's verdict for one client IP before a login attempt.</summary>
public record ThrottleStatus(bool Allowed, int RetryAfterSeconds);

/// <summary>
/// Per-IP login throttle against brute force (KONZEPT "Sichere Anmeldung").
///
/// Instead of locking the ACCOUNT, we slow down the CLIENT IP: the first
/// attempts are free; from the 3rd failed attempt a short cooldown begins that
/// grows strongly with every further failure (5s → 15s → 45s → … up to 1h).
/// A successful login clears the IP. State is in-memory (a single instance,
/// registered as a singleton); good enough for one server and survives no
/// restart - which is fine, a restart simply forgives past attempts.
/// </summary>
public interface ILoginThrottle
{
    /// <summary>Is a login attempt from this IP allowed right now?</summary>
    ThrottleStatus Check(string ip);

    /// <summary>Record a failed attempt (wrong user/password) for this IP.</summary>
    void RegisterFailure(string ip);

    /// <summary>Record a success → the IP is forgiven.</summary>
    void RegisterSuccess(string ip);
}

public sealed class LoginThrottle(TimeProvider clock) : ILoginThrottle
{
    // First 2 failures are free; the 3rd starts the cooldown.
    private const int FreeFailures = 2;
    // Cooldown after the (FreeFailures+1)-th failure, then ×Factor each time.
    private static readonly TimeSpan BaseCooldown = TimeSpan.FromSeconds(5);
    private const double Factor = 3.0;
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromHours(1);
    // Forget an IP that has been quiet for a while (also bounds memory use).
    private static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(2);

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset? BlockedUntil;
        public DateTimeOffset LastSeen;
    }

    private readonly ConcurrentDictionary<string, Entry> _byIp = new();

    public ThrottleStatus Check(string ip)
    {
        var now = clock.GetUtcNow();
        if (_byIp.TryGetValue(ip, out var e))
        {
            lock (e)
            {
                if (now - e.LastSeen > ForgetAfter)
                {
                    e.Failures = 0;
                    e.BlockedUntil = null;
                }
                if (e.BlockedUntil is { } until && until > now)
                {
                    return new ThrottleStatus(false, (int)Math.Ceiling((until - now).TotalSeconds));
                }
            }
        }
        PrunePeriodically(now);
        return new ThrottleStatus(true, 0);
    }

    public void RegisterFailure(string ip)
    {
        var now = clock.GetUtcNow();
        var e = _byIp.GetOrAdd(ip, _ => new Entry());
        lock (e)
        {
            if (now - e.LastSeen > ForgetAfter)
            {
                e.Failures = 0;
            }
            e.LastSeen = now;
            e.Failures++;

            if (e.Failures > FreeFailures)
            {
                // 3rd failure → BaseCooldown; then ×Factor each further failure.
                var steps = e.Failures - FreeFailures - 1; // 0 for the 3rd failure
                var seconds = BaseCooldown.TotalSeconds * Math.Pow(Factor, steps);
                var cooldown = TimeSpan.FromSeconds(Math.Min(seconds, MaxCooldown.TotalSeconds));
                e.BlockedUntil = now + cooldown;
            }
        }
    }

    public void RegisterSuccess(string ip) => _byIp.TryRemove(ip, out _);

    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    /// <summary>Occasionally drop long-idle entries so the map can't grow without
    /// bound under a distributed attack.</summary>
    private void PrunePeriodically(DateTimeOffset now)
    {
        if (now - _lastPrune < TimeSpan.FromMinutes(10)) return;
        _lastPrune = now;
        foreach (var (key, e) in _byIp)
        {
            if (now - e.LastSeen > ForgetAfter)
            {
                _byIp.TryRemove(key, out _);
            }
        }
    }
}
