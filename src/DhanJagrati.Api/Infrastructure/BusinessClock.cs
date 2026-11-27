namespace DhanJagrati.Api.Infrastructure;

/// <summary>"Today" in the organisation's time zone (default India Standard Time), not the server's.</summary>
public sealed class BusinessClock(IConfiguration config, TimeProvider time)
{
    private readonly TimeZoneInfo _zone = ResolveZone(config["App:TimeZoneId"] ?? "Asia/Kolkata");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), _zone).DateTime);

    private static TimeZoneInfo ResolveZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }
}
