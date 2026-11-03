using Microsoft.Extensions.Time.Testing;

namespace FinanceDashboard.Tests.Support;

public static class TestClock
{
    /// <summary>
    /// A fake clock starting at the real now (whole seconds, so values round-trip through Postgres unchanged).
    /// It must follow the real now: the session cookie expires 7 days after the server's clock, and a fixed date
    /// would make HttpClient's cookie container drop that cookie (401) once the real clock passes it.
    /// </summary>
    public static FakeTimeProvider StartingNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new FakeTimeProvider(new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero));
    }
}
