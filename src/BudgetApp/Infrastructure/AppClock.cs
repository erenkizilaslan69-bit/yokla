using System.Globalization;

namespace BudgetApp.Infrastructure;

/// <summary>
/// The app's one clock: controllers, views, filters and validation read "now" and "today" here, and the services
/// that take a <see cref="TimeProvider"/> get the same one. Today it is the server's local time (see README,
/// "If you deploy it", for the time zone).
/// </summary>
public sealed class AppClock(TimeProvider time)
{
    /// <summary>Development only: a date (yyyy-MM-dd) the app treats as today, e.g. to take screenshots on the 15th.</summary>
    public const string TodaySetting = "Clock:Today";

    public DateTime Now => time.GetLocalNow().DateTime;

    public DateTime Today => Now.Date;

    /// <summary>
    /// The system clock, or in Development with <see cref="TodaySetting"/> set, one that stays on that date while the
    /// time of day runs on. Anywhere else the setting is refused, so a production server cannot live on a fake date.
    /// </summary>
    public static TimeProvider For(IConfiguration configuration, IHostEnvironment environment)
    {
        var setting = configuration[TodaySetting];
        if (string.IsNullOrWhiteSpace(setting))
            return TimeProvider.System;
        if (!environment.IsDevelopment())
            throw new InvalidOperationException($"{TodaySetting} may only be set in Development.");
        if (!DateTime.TryParseExact(setting, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidOperationException($"{TodaySetting} must be a date like 2026-10-15, not '{setting}'.");
        return new FixedDateTimeProvider(date);
    }
}

/// <summary>A clock that is always on one date; the time of day comes from the real clock.</summary>
public sealed class FixedDateTimeProvider(DateTime date, TimeProvider? real = null) : TimeProvider
{
    private readonly TimeProvider _real = real ?? System;

    public override TimeZoneInfo LocalTimeZone => _real.LocalTimeZone;

    public override DateTimeOffset GetUtcNow()
    {
        var now = _real.GetLocalNow();
        return new DateTimeOffset(date.Date + now.TimeOfDay, now.Offset).ToUniversalTime();
    }
}
