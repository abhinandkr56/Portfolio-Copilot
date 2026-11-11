namespace PortfolioCopilot.Core;

/// <summary>A clock that always reads the same instant. Used to evaluate a mock scenario "as of" its own date.</summary>
public sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;

    /// <summary>12:00 IST on <paramref name="istDate"/>, the reference time for scenarios (same as the golden tests).</summary>
    public static FixedClock AtIstNoon(DateOnly istDate) =>
        new(new DateTimeOffset(istDate.ToDateTime(new TimeOnly(12, 0)), Ist.Offset));
}
