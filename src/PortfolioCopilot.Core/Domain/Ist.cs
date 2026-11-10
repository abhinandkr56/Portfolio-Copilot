using System.Globalization;

namespace PortfolioCopilot.Core;

/// <summary>India Standard Time helpers. IST is a fixed UTC+05:30 with no daylight saving.</summary>
public static class Ist
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(5.5);

    public static DateTimeOffset Now(TimeProvider time) => time.GetUtcNow().ToOffset(Offset);

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(Now(time).DateTime);

    /// <summary>Parses a Kite IST timestamp ("yyyy-MM-dd HH:mm:ss") and returns it in UTC.</summary>
    public static DateTimeOffset ParseKiteTimestamp(string text)
    {
        var local = DateTime.ParseExact(text.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return new DateTimeOffset(local, Offset).ToUniversalTime();
    }

    public static DateOnly ToIstDate(DateTimeOffset value) => DateOnly.FromDateTime(value.ToOffset(Offset).DateTime);

    /// <summary>Kite access tokens expire around 06:00 IST: the first 06:00 IST strictly after <paramref name="createdUtc"/>, in UTC.</summary>
    public static DateTimeOffset NextKiteExpiry(DateTimeOffset createdUtc)
    {
        var local = createdUtc.ToOffset(Offset);
        var sixAm = new DateTimeOffset(local.Year, local.Month, local.Day, 6, 0, 0, Offset);
        if (sixAm <= local)
            sixAm = sixAm.AddDays(1);
        return sixAm.ToUniversalTime();
    }
}
