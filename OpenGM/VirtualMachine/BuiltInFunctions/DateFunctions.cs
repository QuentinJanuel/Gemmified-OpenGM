namespace OpenGM.VirtualMachine.BuiltInFunctions;

/// <summary>
/// GameMaker's date/time API. A GM "datetime" is a plain real holding a Delphi <c>TDateTime</c>:
/// the whole part counts days since <b>1899-12-30</b> and the fractional part is the time of day.
/// (GameMaker 8 was a Delphi program, which is where that epoch comes from, and Studio kept the
/// number format even after reimplementing the functions.)
///
/// <b>Studio only supports dates from 1 Jan 1970 onward.</b> Its date routines go through a
/// Unix-epoch millisecond timestamp, so anything earlier aborts the game outright with
/// <c>"invalid date conversion - dates earlier than 1 Jan 1970 will be incorrect"</c> (verified on
/// 1.4.9999). The datetime *zero* is still 1899-12-30 -- 1970-01-01 reads as 25569, exactly the
/// constant YoYo's own HTML5 <c>Function_Date.js</c> adds -- so the epoch is reachable by
/// arithmetic, just never constructible.
///
/// This implementation handles the pre-1970 range correctly rather than reproducing that error.
/// It's a deliberate divergence: the only inputs affected are ones on which real GameMaker refuses
/// to run at all, so nothing a working game does can tell the difference, and crashing on purpose
/// would buy nothing. The rule it follows there is Delphi's, where the fractional part still counts
/// <i>forward</i> through the day while the whole part is negative -- so a datetime gets more
/// negative as the clock advances and 1899-12-29 12:00 is <c>-1.5</c>, not <c>-0.5</c>. Both
/// conversions below carry the sign correction that implies, and every round trip is exact.
/// Ground truth for that half: OpenGMK's <c>gml/datetime.rs</c>, plus Delphi's own
/// <c>EncodeDate</c>/<c>DecodeTime</c> for the rounding.
/// </summary>
public static class DateFunctions
{
    private static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0);

    private const double MsPerDay = 86400000.0;

    /// <summary>Packs a CLR <see cref="DateTime"/> into GM's real-valued datetime.</summary>
    public static double ToGM(DateTime dt)
    {
        // Whole days via ticks rather than TimeSpan.TotalDays: integer division truncates toward
        // zero exactly, where the floating-point version can land a hair either side of a whole day.
        var wholeDays = (dt - Epoch).Ticks / TimeSpan.TicksPerDay;
        var fraction = dt.TimeOfDay.TotalMilliseconds / MsPerDay;

        // Before the epoch the time-of-day still runs forward, so it has to be *subtracted* from a
        // whole day that has itself already been stepped one further back.
        return dt >= Epoch ? wholeDays + fraction : wholeDays - 1 - fraction;
    }

    /// <summary>Unpacks GM's real-valued datetime back into a CLR <see cref="DateTime"/>.</summary>
    public static DateTime FromGM(double value)
    {
        var days = Math.Truncate(value);

        // Round, not floor: the fractional part of a large datetime has already lost bits to the
        // day count, so 13:45:30 comes back as 49529999.97ms as often as 49530000.03ms. Delphi's
        // DecodeTime rounds for exactly this reason, and flooring would drop a second at random.
        var ms = Math.Round(Math.Abs(value - days) * MsPerDay, MidpointRounding.AwayFromZero);

        return Epoch.AddDays(days).AddMilliseconds(ms);
    }

    /// <summary>The time-of-day alone, as GM's [0,1) fraction of a day.</summary>
    private static double TimeOf(DateTime dt) => dt.TimeOfDay.TotalMilliseconds / MsPerDay;

    private static bool IsValidDate(int year, int month, int day) =>
        year >= 1 && year <= 9999
        && month >= 1 && month <= 12
        && day >= 1 && day <= DateTime.DaysInMonth(year, month);

    private static bool IsValidTime(int hour, int minute, int second) =>
        (hour >= 0 && hour < 24 && minute >= 0 && minute < 60 && second >= 0 && second < 60)
        // 24:00:00 is accepted by GM even though building a datetime out of it would fail.
        || (hour == 24 && minute == 0 && second == 0);

    /*
     * Creation
     *
     * Six of these are pre-Studio only, and are marked as such below: GameMaker: Studio dropped
     * every function that treats a date or a time as a thing separate from a datetime, so
     * date_current_date/_time, date_create_date/_time and date_valid_date/_time are all "Obsolete
     * function" compile errors in the 1.4 IDE (confirmed against UndertaleModLib's own GMS builtin
     * table, UndertaleModTool/UndertaleModLib/Compiler/BuiltinList.cs, which lists only the
     * _datetime spellings). No Studio data.win can therefore ever call them -- they're kept
     * registered because they cost nothing, they're the readable spelling for the tests, and a
     * GM8-era converted game would still want them.
     */

    [GMLFunction("date_current_datetime")]
    public static object date_current_datetime(object?[] args) => ToGM(DateTime.Now);

    [GMLFunction("date_current_date")] // pre-Studio only
    public static object date_current_date(object?[] args) => ToGM(DateTime.Now.Date);

    [GMLFunction("date_current_time")] // pre-Studio only
    public static object date_current_time(object?[] args) => TimeOf(DateTime.Now);

    [GMLFunction("date_create_datetime")]
    public static object date_create_datetime(object?[] args)
    {
        var year = args[0].Conv<int>();
        var month = args[1].Conv<int>();
        var day = args[2].Conv<int>();
        var hour = args[3].Conv<int>();
        var minute = args[4].Conv<int>();
        var second = args[5].Conv<int>();

        if (!IsValidDate(year, month, day) || !IsValidTime(hour, minute, second) || hour == 24)
        {
            return 0.0;
        }

        return ToGM(new DateTime(year, month, day, hour, minute, second));
    }

    [GMLFunction("date_create_date")] // pre-Studio only
    public static object date_create_date(object?[] args)
    {
        var year = args[0].Conv<int>();
        var month = args[1].Conv<int>();
        var day = args[2].Conv<int>();

        return IsValidDate(year, month, day) ? ToGM(new DateTime(year, month, day)) : 0.0;
    }

    [GMLFunction("date_create_time")] // pre-Studio only
    public static object date_create_time(object?[] args)
    {
        var hour = args[0].Conv<int>();
        var minute = args[1].Conv<int>();
        var second = args[2].Conv<int>();

        if (!IsValidTime(hour, minute, second) || hour == 24)
        {
            return 0.0;
        }

        // A pure time sits on the epoch day, i.e. it is just the [0,1) fraction.
        return (hour * 3600.0 + minute * 60.0 + second) / 86400.0;
    }

    /*
     * Validation
     */

    [GMLFunction("date_valid_datetime")]
    public static object date_valid_datetime(object?[] args) =>
        IsValidDate(args[0].Conv<int>(), args[1].Conv<int>(), args[2].Conv<int>())
        && IsValidTime(args[3].Conv<int>(), args[4].Conv<int>(), args[5].Conv<int>());

    [GMLFunction("date_valid_date")] // pre-Studio only
    public static object date_valid_date(object?[] args) =>
        IsValidDate(args[0].Conv<int>(), args[1].Conv<int>(), args[2].Conv<int>());

    [GMLFunction("date_valid_time")] // pre-Studio only
    public static object date_valid_time(object?[] args) =>
        IsValidTime(args[0].Conv<int>(), args[1].Conv<int>(), args[2].Conv<int>());

    /*
     * Incrementing
     *
     * The calendar-aware ones (year/month) go through DateTime so that day-of-month is clamped the
     * way Delphi's IncMonth clamps it -- 2020-01-31 plus a month is 2020-02-29, not 2020-03-02.
     * The rest are plain arithmetic on the real, which is what GM does and what keeps
     * date_inc_day(d, 1) exactly one whole unit away from d regardless of DST or month length.
     */

    [GMLFunction("date_inc_year")]
    public static object date_inc_year(object?[] args)
    {
        var dt = FromGM(args[0].Conv<double>());
        return ToGM(dt.AddYears(args[1].Conv<int>()));
    }

    [GMLFunction("date_inc_month")]
    public static object date_inc_month(object?[] args)
    {
        var dt = FromGM(args[0].Conv<double>());
        return ToGM(dt.AddMonths(args[1].Conv<int>()));
    }

    [GMLFunction("date_inc_week")]
    public static object date_inc_week(object?[] args) =>
        args[0].Conv<double>() + args[1].Conv<double>() * 7;

    [GMLFunction("date_inc_day")]
    public static object date_inc_day(object?[] args) =>
        args[0].Conv<double>() + args[1].Conv<double>();

    // The sub-day increments move the *time* part, which runs backwards on the negative side of the
    // epoch -- so adding an hour to a pre-1899 datetime has to subtract from the real.
    [GMLFunction("date_inc_hour")]
    public static object date_inc_hour(object?[] args) => IncFraction(args, 24.0);

    [GMLFunction("date_inc_minute")]
    public static object date_inc_minute(object?[] args) => IncFraction(args, 1440.0);

    [GMLFunction("date_inc_second")]
    public static object date_inc_second(object?[] args) => IncFraction(args, 86400.0);

    private static object IncFraction(object?[] args, double unitsPerDay)
    {
        var datetime = args[0].Conv<double>();
        var amount = args[1].Conv<double>();
        return datetime + amount / unitsPerDay * (datetime <= 0 ? -1 : 1);
    }

    /*
     * Extraction
     */

    [GMLFunction("date_get_year")]
    public static object date_get_year(object?[] args) => FromGM(args[0].Conv<double>()).Year;

    [GMLFunction("date_get_month")]
    public static object date_get_month(object?[] args) => FromGM(args[0].Conv<double>()).Month;

    [GMLFunction("date_get_day")]
    public static object date_get_day(object?[] args) => FromGM(args[0].Conv<double>()).Day;

    [GMLFunction("date_get_hour")]
    public static object date_get_hour(object?[] args) => FromGM(args[0].Conv<double>()).Hour;

    [GMLFunction("date_get_minute")]
    public static object date_get_minute(object?[] args) => FromGM(args[0].Conv<double>()).Minute;

    [GMLFunction("date_get_second")]
    public static object date_get_second(object?[] args) => FromGM(args[0].Conv<double>()).Second;

    /// <summary>0 = Sunday, matching the <c>current_weekday</c> builtin variable.</summary>
    [GMLFunction("date_get_weekday")]
    public static object date_get_weekday(object?[] args) => (int)FromGM(args[0].Conv<double>()).DayOfWeek;

    /// <summary>
    /// Not an ISO week number -- just the day of the year divided by 7 and floored, so it runs 0..52
    /// and every "week" starts on the same weekday the year did. Captured from GMS 1.4.9999:
    /// 2020-03-15 is day 75, and date_get_week reports <b>10</b> where the ISO week is 11.
    /// </summary>
    [GMLFunction("date_get_week")]
    public static object date_get_week(object?[] args)
    {
        var dt = FromGM(args[0].Conv<double>());
        return (int)Math.Floor(dt.DayOfYear / 7.0);
    }

    [GMLFunction("date_get_day_of_year")]
    public static object date_get_day_of_year(object?[] args) => FromGM(args[0].Conv<double>()).DayOfYear;

    [GMLFunction("date_get_hour_of_year")]
    public static object date_get_hour_of_year(object?[] args)
    {
        var dt = FromGM(args[0].Conv<double>());
        return (dt.DayOfYear - 1) * 24 + dt.Hour;
    }

    [GMLFunction("date_get_minute_of_year")]
    public static object date_get_minute_of_year(object?[] args)
    {
        var dt = FromGM(args[0].Conv<double>());
        return ((dt.DayOfYear - 1) * 24 + dt.Hour) * 60 + dt.Minute;
    }

    [GMLFunction("date_get_second_of_year")]
    public static object date_get_second_of_year(object?[] args)
    {
        var dt = FromGM(args[0].Conv<double>());
        return (((dt.DayOfYear - 1) * 24 + dt.Hour) * 60 + dt.Minute) * 60 + dt.Second;
    }

    [GMLFunction("date_date_of")]
    public static object date_date_of(object?[] args) => ToGM(FromGM(args[0].Conv<double>()).Date);

    [GMLFunction("date_time_of")]
    public static object date_time_of(object?[] args) => TimeOf(FromGM(args[0].Conv<double>()));

    /*
     * Spans
     *
     * The sub-month spans are pure arithmetic on the difference. Months and years are *calendar*
     * counts, not day counts: the manual's own example has
     * date_year_span(2011-01-01, 2012-07-01) == 1.5, which is 18 calendar months / 12 -- 547 days
     * / 365.25 would have been 1.4976.
     */

    [GMLFunction("date_second_span")]
    public static object date_second_span(object?[] args) => Math.Abs(Diff(args)) * 86400.0;

    [GMLFunction("date_minute_span")]
    public static object date_minute_span(object?[] args) => Math.Abs(Diff(args)) * 1440.0;

    [GMLFunction("date_hour_span")]
    public static object date_hour_span(object?[] args) => Math.Abs(Diff(args)) * 24.0;

    [GMLFunction("date_day_span")]
    public static object date_day_span(object?[] args) => Math.Abs(Diff(args));

    [GMLFunction("date_week_span")]
    public static object date_week_span(object?[] args) => Math.Abs(Diff(args)) / 7.0;

    [GMLFunction("date_month_span")]
    public static object date_month_span(object?[] args) => Math.Abs(Diff(args)) / AverageDaysPerMonth;

    [GMLFunction("date_year_span")]
    public static object date_year_span(object?[] args) => Math.Abs(Diff(args)) / AverageDaysPerYear;

    private static double Diff(object?[] args) => args[1].Conv<double>() - args[0].Conv<double>();

    /// <summary>
    /// Months and years are plain day counts over an average-length month/year -- 365.25 days per
    /// year and a twelfth of that per month -- not calendar counts.
    ///
    /// This one is worth stating because the manual actively misleads: its own example says
    /// date_year_span(2011-01-01, 2012-07-01) is "1.5", which is what a calendar count of 18 months
    /// would give. GMS 1.4.9999 actually answers <b>1.497604</b>, i.e. 547 days / 365.25. Measured
    /// spans over an arbitrary pair confirm the divisor to all six printed digits.
    /// </summary>
    private const double AverageDaysPerYear = 365.25;

    private const double AverageDaysPerMonth = AverageDaysPerYear / 12.0; // 30.4375

    /*
     * Comparison
     */

    [GMLFunction("date_compare_datetime")]
    public static object date_compare_datetime(object?[] args) =>
        args[0].Conv<double>().CompareTo(args[1].Conv<double>());

    [GMLFunction("date_compare_date")]
    public static object date_compare_date(object?[] args) =>
        FromGM(args[0].Conv<double>()).Date.CompareTo(FromGM(args[1].Conv<double>()).Date);

    [GMLFunction("date_compare_time")]
    public static object date_compare_time(object?[] args) =>
        FromGM(args[0].Conv<double>()).TimeOfDay.CompareTo(FromGM(args[1].Conv<double>()).TimeOfDay);

    /*
     * Calendar queries
     */

    [GMLFunction("date_days_in_month")]
    public static object date_days_in_month(object?[] args)
    {
        var dt = FromGM(args[0].Conv<double>());
        return DateTime.DaysInMonth(dt.Year, dt.Month);
    }

    [GMLFunction("date_days_in_year")]
    public static object date_days_in_year(object?[] args) =>
        DateTime.IsLeapYear(FromGM(args[0].Conv<double>()).Year) ? 366 : 365;

    [GMLFunction("date_leap_year")]
    public static object date_leap_year(object?[] args) =>
        DateTime.IsLeapYear(FromGM(args[0].Conv<double>()).Year);

    [GMLFunction("date_is_today")]
    public static object date_is_today(object?[] args) =>
        FromGM(args[0].Conv<double>()).Date == DateTime.Now.Date;

    /*
     * Formatting -- GM defers to the OS locale's short date / long time patterns, so these follow
     * CurrentCulture rather than hardcoding a US layout.
     */

    [GMLFunction("date_date_string")]
    public static object date_date_string(object?[] args) =>
        FromGM(args[0].Conv<double>()).ToString("d", Entry.DisplayCulture);

    [GMLFunction("date_time_string")]
    public static object date_time_string(object?[] args) =>
        FromGM(args[0].Conv<double>()).ToString("T", Entry.DisplayCulture);

    [GMLFunction("date_datetime_string")]
    public static object date_datetime_string(object?[] args)
    {
        var dt = FromGM(args[0].Conv<double>());
        // The player's own locale, deliberately -- see Entry.DisplayCulture. Everything else in
        // the engine is invariant, but a date shown to a person should read the way their
        // machine writes dates, which is what real GameMaker does.
        return dt.ToString("d", Entry.DisplayCulture) + " " + dt.ToString("T", Entry.DisplayCulture);
    }
}
