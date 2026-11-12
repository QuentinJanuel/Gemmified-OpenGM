using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// The date/time family. A GM datetime is a Delphi <c>TDateTime</c>: whole part = days since
/// 1899-12-30, fractional part = time of day.
///
/// The family used to be a handful of functions over a hand-rolled JS-Date lookalike that its own
/// header asked to have deleted, and the rest of the ~37 were bare <c>// name</c> comments. Since an
/// unresolved call aborts the entire calling event, every one of those gaps was fatal to whatever
/// event touched it -- and the ones that *were* implemented were mostly wrong anyway
/// (<c>date_datetime_string</c> returned a C# type name; <c>date_get_week</c> returned
/// day-of-year/7).
///
/// The interesting assertions here are the ones about the negative side of the epoch, where the
/// time-of-day still counts forward while the value as a whole runs backwards.
/// </summary>
[TestClass]
public class DateFunctionsTests
{
    private static double Num(object? o) => o.Conv<double>();

    // 2020-03-15 is a Sunday, which is what pins the weekday numbering.
    private static double Sample() => Num(DateFunctions.date_create_datetime([2020, 3, 15, 13, 45, 30]));

    [TestMethod]
    public void EpochIsDelphiNot1970()
    {
        // 25569 is the number of days from 1899-12-30 to 1970-01-01, and it is exactly the constant
        // YoYo's own HTML5 Function_Date.js adds to a Unix day count -- which is what pins day zero
        // to 1899-12-30 even though Studio itself refuses to construct any date that early.
        Assert.AreEqual(25569.0, Num(DateFunctions.date_create_datetime([1970, 1, 1, 0, 0, 0])));
        Assert.AreEqual(25570.0, Num(DateFunctions.date_create_datetime([1970, 1, 2, 0, 0, 0])));

        Assert.AreEqual(0.0, Num(DateFunctions.date_create_date([1899, 12, 30])));
        Assert.AreEqual(2.0, Num(DateFunctions.date_create_date([1900, 1, 1])));
    }

    [TestMethod]
    public void TimeOfDayIsTheFractionalPart()
    {
        Assert.AreEqual(0.5, Num(DateFunctions.date_create_time([12, 0, 0])), 1e-12);
        Assert.AreEqual(0.25, Num(DateFunctions.date_create_time([6, 0, 0])), 1e-12);
        Assert.AreEqual(0.0, Num(DateFunctions.date_create_time([0, 0, 0])), 1e-12);

        var noonOnDayTwo = Num(DateFunctions.date_create_datetime([1900, 1, 1, 12, 0, 0]));
        Assert.AreEqual(2.5, noonOnDayTwo, 1e-12);
    }

    [TestMethod]
    public void RoundTripsThroughTheRealExactly()
    {
        var d = Sample();

        Assert.AreEqual(2020, DateFunctions.date_get_year([d]).Conv<int>());
        Assert.AreEqual(3, DateFunctions.date_get_month([d]).Conv<int>());
        Assert.AreEqual(15, DateFunctions.date_get_day([d]).Conv<int>());
        Assert.AreEqual(13, DateFunctions.date_get_hour([d]).Conv<int>());
        Assert.AreEqual(45, DateFunctions.date_get_minute([d]).Conv<int>());

        // The one most at risk from precision loss: the seconds live in the last bits of a value
        // whose whole part is already ~44000. Flooring instead of rounding drops this to 29.
        Assert.AreEqual(30, DateFunctions.date_get_second([d]).Conv<int>());
    }

    [TestMethod]
    public void EverySecondOfADayRoundTrips()
    {
        // Brute force over a whole day, at a date far enough from the epoch that the fraction is
        // down to its last few bits. This is the assertion that would catch a floor/round mistake
        // in FromGM, which only bites for a minority of times of day.
        for (var s = 0; s < 86400; s += 1)
        {
            var d = Num(DateFunctions.date_create_datetime([2020, 3, 15, s / 3600, s / 60 % 60, s % 60]));
            var got = DateFunctions.date_get_hour([d]).Conv<int>() * 3600
                      + DateFunctions.date_get_minute([d]).Conv<int>() * 60
                      + DateFunctions.date_get_second([d]).Conv<int>();
            Assert.AreEqual(s, got, $"second {s} of the day did not round trip");
        }
    }

    [TestMethod]
    public void BeforeTheEpochTheValueRunsBackwards()
    {
        // Beyond what real Studio supports -- it aborts the game on any date before 1 Jan 1970
        // ("invalid date conversion", verified on 1.4.9999) because its own routines go through a
        // Unix timestamp. This asserts the Delphi rule anyway, because handling the range
        // gracefully is strictly better than crashing on input no working game can produce, and
        // because a silently wrong sign here would also corrupt the >= 1970 arithmetic that shares
        // these two conversions.
        //
        // Midday on the day before the epoch is -1.5, not -0.5: the whole part steps a full extra
        // day back and the time-of-day is then subtracted from it.
        var d = Num(DateFunctions.date_create_datetime([1899, 12, 29, 12, 0, 0]));
        Assert.AreEqual(-1.5, d, 1e-12);

        Assert.AreEqual(1899, DateFunctions.date_get_year([d]).Conv<int>());
        Assert.AreEqual(12, DateFunctions.date_get_month([d]).Conv<int>());
        Assert.AreEqual(29, DateFunctions.date_get_day([d]).Conv<int>());
        Assert.AreEqual(12, DateFunctions.date_get_hour([d]).Conv<int>());

        var earlier = Num(DateFunctions.date_create_datetime([1899, 12, 28, 6, 0, 0]));
        Assert.AreEqual(-2.25, earlier, 1e-12);
    }

    [TestMethod]
    public void SubDayIncrementsFollowTheSignConvention()
    {
        var d = Sample();
        Assert.AreEqual(d + 1.0 / 24, Num(DateFunctions.date_inc_hour([d, 1])), 1e-12);
        Assert.AreEqual(d + 1.0 / 1440, Num(DateFunctions.date_inc_minute([d, 1])), 1e-12);
        Assert.AreEqual(d + 1.0 / 86400, Num(DateFunctions.date_inc_second([d, 1])), 1e-12);
        Assert.AreEqual(d + 1, Num(DateFunctions.date_inc_day([d, 1])), 1e-12);
        Assert.AreEqual(d + 7, Num(DateFunctions.date_inc_week([d, 1])), 1e-12);

        // On the negative side of the epoch, moving an hour *later* makes the real smaller.
        var neg = Num(DateFunctions.date_create_datetime([1899, 12, 29, 12, 0, 0]));
        var plusHour = Num(DateFunctions.date_inc_hour([neg, 1]));
        Assert.AreEqual(13, DateFunctions.date_get_hour([plusHour]).Conv<int>());
        Assert.IsTrue(plusHour < neg, "an hour later must be a smaller real before the epoch");
    }

    [TestMethod]
    public void CalendarIncrementsClampTheDayOfMonth()
    {
        // Delphi's IncMonth clamps rather than spilling into the next month.
        var jan31 = Num(DateFunctions.date_create_date([2020, 1, 31]));
        var feb = Num(DateFunctions.date_inc_month([jan31, 1]));
        Assert.AreEqual(2, DateFunctions.date_get_month([feb]).Conv<int>());
        Assert.AreEqual(29, DateFunctions.date_get_day([feb]).Conv<int>());

        var leapDay = Num(DateFunctions.date_create_date([2020, 2, 29]));
        var nextYear = Num(DateFunctions.date_inc_year([leapDay, 1]));
        Assert.AreEqual(2021, DateFunctions.date_get_year([nextYear]).Conv<int>());
        Assert.AreEqual(28, DateFunctions.date_get_day([nextYear]).Conv<int>());
    }

    [TestMethod]
    public void WeekdayIsZeroBasedFromSunday()
    {
        // 2020-03-15 was a Sunday.
        Assert.AreEqual(0, DateFunctions.date_get_weekday([Sample()]).Conv<int>());
        Assert.AreEqual(1, DateFunctions.date_get_weekday([Num(DateFunctions.date_create_date([2020, 3, 16]))]).Conv<int>());
        Assert.AreEqual(6, DateFunctions.date_get_weekday([Num(DateFunctions.date_create_date([2020, 3, 21]))]).Conv<int>());
    }

    [TestMethod]
    public void OfYearAccumulatorsCompound()
    {
        var d = Sample();
        // 2020 is a leap year: Jan 31 + Feb 29 + 14 = 74 days before the 15th of March.
        Assert.AreEqual(75, DateFunctions.date_get_day_of_year([d]).Conv<int>());
        Assert.AreEqual(74 * 24 + 13, DateFunctions.date_get_hour_of_year([d]).Conv<int>());
        Assert.AreEqual((74 * 24 + 13) * 60 + 45, DateFunctions.date_get_minute_of_year([d]).Conv<int>());
        Assert.AreEqual(((74 * 24 + 13) * 60 + 45) * 60 + 30, DateFunctions.date_get_second_of_year([d]).Conv<int>());
    }

    [TestMethod]
    public void DateAndTimeCanBeSplitOut()
    {
        var d = Sample();
        Assert.AreEqual(Num(DateFunctions.date_create_date([2020, 3, 15])), Num(DateFunctions.date_date_of([d])), 1e-9);
        Assert.AreEqual(Num(DateFunctions.date_create_time([13, 45, 30])), Num(DateFunctions.date_time_of([d])), 1e-9);
    }

    [TestMethod]
    public void SpansAreAbsoluteAndSubDayOnesAreLinear()
    {
        var a = Num(DateFunctions.date_create_datetime([2020, 3, 15, 0, 0, 0]));
        var b = Num(DateFunctions.date_create_datetime([2020, 3, 16, 12, 0, 0]));

        Assert.AreEqual(1.5, Num(DateFunctions.date_day_span([a, b])), 1e-9);
        Assert.AreEqual(36.0, Num(DateFunctions.date_hour_span([a, b])), 1e-6);
        Assert.AreEqual(2160.0, Num(DateFunctions.date_minute_span([a, b])), 1e-4);
        Assert.AreEqual(129600.0, Num(DateFunctions.date_second_span([a, b])), 1e-2);
        Assert.AreEqual(1.5 / 7, Num(DateFunctions.date_week_span([a, b])), 1e-9);

        // Order must not matter.
        Assert.AreEqual(1.5, Num(DateFunctions.date_day_span([b, a])), 1e-9);
    }

    [TestMethod]
    public void MonthAndYearSpansAreAverageLengths()
    {
        // Golden data from GMS 1.4.9999. The manual is actively wrong here: its own worked example
        // claims date_year_span(2011-01-01, 2012-07-01) is "1.5", which is what counting 18
        // calendar months would give -- and is what this used to implement. The runtime answers
        // 1.497604, i.e. 547 days / 365.25.
        var a = Num(DateFunctions.date_create_date([2011, 1, 1]));
        var b = Num(DateFunctions.date_create_date([2012, 7, 1]));
        Assert.AreEqual(1.497604, Num(DateFunctions.date_year_span([a, b])), 5e-7);

        // The same capture over an arbitrary pair, which is what pins the divisors to six digits:
        // 2020-03-15 13:45:30 to 2021-05-20 06:00:00 measured 430.676736 days.
        var d = Num(DateFunctions.date_create_datetime([2020, 3, 15, 13, 45, 30]));
        var e = Num(DateFunctions.date_create_datetime([2021, 5, 20, 6, 0, 0]));

        Assert.AreEqual(430.676736, Num(DateFunctions.date_day_span([d, e])), 5e-7);
        Assert.AreEqual(14.149544, Num(DateFunctions.date_month_span([d, e])), 5e-7);
        Assert.AreEqual(1.179129, Num(DateFunctions.date_year_span([d, e])), 5e-7);
        Assert.AreEqual(61.525248, Num(DateFunctions.date_week_span([d, e])), 5e-7);
        Assert.AreEqual(10336.241667, Num(DateFunctions.date_hour_span([d, e])), 5e-5);
        Assert.AreEqual(620174.5, Num(DateFunctions.date_minute_span([d, e])), 5e-4);
        Assert.AreEqual(37210470.0, Num(DateFunctions.date_second_span([d, e])), 5e-2);

        // A whole calendar month is NOT a whole month span, precisely because the divisor is an
        // average: Jan 10 to Mar 10 is 60 days, which is 60/30.4375 = 1.971 months.
        var jan = Num(DateFunctions.date_create_date([2020, 1, 10]));
        var mar = Num(DateFunctions.date_create_date([2020, 3, 10]));
        Assert.AreEqual(60.0 / 30.4375, Num(DateFunctions.date_month_span([jan, mar])), 1e-9);
    }

    [TestMethod]
    public void WeekIsDayOfYearOverSevenNotAnIsoWeek()
    {
        // Captured: 2020-03-15 is day 75 of the year and date_get_week reports 10. The ISO week is
        // 11, so implementing this as ISOWeek.GetWeekOfYear (which is what "fixing" it looked like)
        // is off by one for most of the year.
        var d = Sample();
        Assert.AreEqual(75, DateFunctions.date_get_day_of_year([d]).Conv<int>());
        Assert.AreEqual(10, DateFunctions.date_get_week([d]).Conv<int>());

        // It really is a floor of doy/7, so it starts at 0 and steps on multiples of 7.
        Assert.AreEqual(0, DateFunctions.date_get_week([Num(DateFunctions.date_create_date([2020, 1, 1]))]).Conv<int>());
        Assert.AreEqual(0, DateFunctions.date_get_week([Num(DateFunctions.date_create_date([2020, 1, 6]))]).Conv<int>());
        Assert.AreEqual(1, DateFunctions.date_get_week([Num(DateFunctions.date_create_date([2020, 1, 7]))]).Conv<int>());
    }

    [TestMethod]
    public void ComparisonsIgnoreThePartTheyreNotAbout()
    {
        var morning = Num(DateFunctions.date_create_datetime([2020, 3, 15, 6, 0, 0]));
        var evening = Num(DateFunctions.date_create_datetime([2020, 3, 15, 18, 0, 0]));
        var nextDayMorning = Num(DateFunctions.date_create_datetime([2020, 3, 16, 6, 0, 0]));

        Assert.AreEqual(-1, DateFunctions.date_compare_datetime([morning, evening]).Conv<int>());
        Assert.AreEqual(0, DateFunctions.date_compare_datetime([morning, morning]).Conv<int>());
        Assert.AreEqual(1, DateFunctions.date_compare_datetime([evening, morning]).Conv<int>());

        // Same day, different times.
        Assert.AreEqual(0, DateFunctions.date_compare_date([morning, evening]).Conv<int>());
        // Same time, different days.
        Assert.AreEqual(0, DateFunctions.date_compare_time([morning, nextDayMorning]).Conv<int>());
        Assert.AreEqual(-1, DateFunctions.date_compare_date([morning, nextDayMorning]).Conv<int>());
    }

    [TestMethod]
    public void ValidationRejectsImpossibleDates()
    {
        Assert.IsFalse(DateFunctions.date_valid_date([2019, 2, 29]).Conv<bool>());
        Assert.IsTrue(DateFunctions.date_valid_date([2020, 2, 29]).Conv<bool>());
        Assert.IsFalse(DateFunctions.date_valid_date([2020, 13, 1]).Conv<bool>());
        Assert.IsFalse(DateFunctions.date_valid_datetime([2020, 2, 30, 0, 0, 0]).Conv<bool>());
        Assert.IsTrue(DateFunctions.date_valid_datetime([2020, 2, 29, 23, 59, 59]).Conv<bool>());

        Assert.IsFalse(DateFunctions.date_valid_time([25, 0, 0]).Conv<bool>());
        Assert.IsFalse(DateFunctions.date_valid_time([12, 60, 0]).Conv<bool>());
        // GM accepts 24:00:00 specifically, even though a datetime can't be built from it.
        Assert.IsTrue(DateFunctions.date_valid_time([24, 0, 0]).Conv<bool>());

        // An invalid date is 0 rather than an error.
        Assert.AreEqual(0.0, Num(DateFunctions.date_create_date([2019, 2, 29])));
    }

    [TestMethod]
    public void CalendarQueries()
    {
        Assert.AreEqual(29, DateFunctions.date_days_in_month([Num(DateFunctions.date_create_date([2020, 2, 1]))]).Conv<int>());
        Assert.AreEqual(28, DateFunctions.date_days_in_month([Num(DateFunctions.date_create_date([2019, 2, 1]))]).Conv<int>());
        Assert.AreEqual(366, DateFunctions.date_days_in_year([Num(DateFunctions.date_create_date([2020, 6, 1]))]).Conv<int>());
        Assert.AreEqual(365, DateFunctions.date_days_in_year([Num(DateFunctions.date_create_date([2019, 6, 1]))]).Conv<int>());

        Assert.IsTrue(DateFunctions.date_leap_year([Num(DateFunctions.date_create_date([2020, 6, 1]))]).Conv<bool>());
        // The century rule, both ways.
        Assert.IsFalse(DateFunctions.date_leap_year([Num(DateFunctions.date_create_date([1900, 6, 1]))]).Conv<bool>());
        Assert.IsTrue(DateFunctions.date_leap_year([Num(DateFunctions.date_create_date([2000, 6, 1]))]).Conv<bool>());

        Assert.IsTrue(DateFunctions.date_is_today([Num(DateFunctions.date_current_date([]))]).Conv<bool>());
        Assert.IsFalse(DateFunctions.date_is_today([Sample()]).Conv<bool>());
    }

    [TestMethod]
    public void CurrentDateAndTimeAgreeWithEachOther()
    {
        var now = Num(DateFunctions.date_current_datetime([]));
        var date = Num(DateFunctions.date_current_date([]));
        var time = Num(DateFunctions.date_current_time([]));

        Assert.AreEqual(date, Math.Floor(now), 1e-9);
        Assert.IsTrue(time >= 0 && time < 1, $"time-of-day fraction out of range: {time}");
        Assert.AreEqual(now, date + time, 1e-4);
    }

    [TestMethod]
    public void StringsAreNotTypeNames()
    {
        // The old date_datetime_string returned "OpenGM.VirtualMachine.BuiltInFunctions.Date".
        var s = DateFunctions.date_datetime_string([Sample()]).Conv<string>();
        StringAssert.Contains(s, "2020");
        StringAssert.Contains(s, "15");
        Assert.IsFalse(s.Contains("OpenGM"), $"formatted date leaked a type name: {s}");

        var d = DateFunctions.date_date_string([Sample()]).Conv<string>();
        StringAssert.Contains(d, "2020");
        var t = DateFunctions.date_time_string([Sample()]).Conv<string>();
        StringAssert.Contains(t, "45");
    }
}
