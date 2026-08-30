using System;

namespace ZKAttendance.Infrastructure.NepaliCalendar
{
    /// <summary>
    /// A Bikram Sambat date. Immutable value type - it holds a BS year, month
    /// and day, nothing else. It never stores a time.
    /// </summary>
    public readonly struct NepaliDate : IEquatable<NepaliDate>, IComparable<NepaliDate>
    {
        public int Year { get; }
        public int Month { get; }
        public int Day { get; }

        public NepaliDate(int year, int month, int day)
        {
            var maxDay = NepaliCalendarData.DaysInMonth(year, month);
            if (day < 1 || day > maxDay)
                throw new ArgumentOutOfRangeException(
                    nameof(day),
                    $"{NepaliCalendarData.MonthNamesEn[month - 1]} {year} has {maxDay} days; got {day}.");

            Year = year;
            Month = month;
            Day = day;
        }

        public string MonthNameEn => NepaliCalendarData.MonthNamesEn[Month - 1];
        public string MonthNameNp => NepaliCalendarData.MonthNamesNp[Month - 1];

        /// <summary>Government format, e.g. "2083-01-15".</summary>
        public override string ToString() => $"{Year:0000}-{Month:00}-{Day:00}";

        /// <summary>Readable format, e.g. "15 Baisakh 2083".</summary>
        public string ToLongString() => $"{Day} {MonthNameEn} {Year}";

        /// <summary>Devanagari format, e.g. "१५ बैशाख २०८३".</summary>
        public string ToNepaliString() =>
            $"{ToDevanagari(Day)} {MonthNameNp} {ToDevanagari(Year)}";

        public static string ToDevanagari(int value)
        {
            const string digits = "०१२३४५६७८९";
            var s = value.ToString();
            var chars = new char[s.Length];
            for (var i = 0; i < s.Length; i++)
                chars[i] = digits[s[i] - '0'];
            return new string(chars);
        }

        public static NepaliDate Parse(string bs)
        {
            if (string.IsNullOrWhiteSpace(bs))
                throw new FormatException("Empty BS date.");

            var parts = bs.Trim().Split('-', '/', '.');
            if (parts.Length != 3)
                throw new FormatException($"'{bs}' is not in YYYY-MM-DD format.");

            return new NepaliDate(
                int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        }

        public static bool TryParse(string bs, out NepaliDate result)
        {
            try { result = Parse(bs); return true; }
            catch { result = default; return false; }
        }

        public bool Equals(NepaliDate other) =>
            Year == other.Year && Month == other.Month && Day == other.Day;

        public override bool Equals(object? obj) => obj is NepaliDate d && Equals(d);
        public override int GetHashCode() => (Year * 100 + Month) * 100 + Day;

        public int CompareTo(NepaliDate other)
        {
            if (Year != other.Year) return Year.CompareTo(other.Year);
            if (Month != other.Month) return Month.CompareTo(other.Month);
            return Day.CompareTo(other.Day);
        }

        public static bool operator ==(NepaliDate a, NepaliDate b) => a.Equals(b);
        public static bool operator !=(NepaliDate a, NepaliDate b) => !a.Equals(b);
        public static bool operator <(NepaliDate a, NepaliDate b) => a.CompareTo(b) < 0;
        public static bool operator >(NepaliDate a, NepaliDate b) => a.CompareTo(b) > 0;
        public static bool operator <=(NepaliDate a, NepaliDate b) => a.CompareTo(b) <= 0;
        public static bool operator >=(NepaliDate a, NepaliDate b) => a.CompareTo(b) >= 0;
    }

    /// <summary>
    /// Converts between Gregorian (AD) and Bikram Sambat (BS).
    ///
    /// The whole method is: count how many days the AD date is away from the
    /// anchor (Baisakh 1, 2080 = 14 April 2023), then walk that many days
    /// through the BS month table.
    /// </summary>
    public static class NepaliDateConverter
    {
        public static NepaliDate ToBs(DateTime ad)
        {
            var offset = (int)(ad.Date - NepaliCalendarData.ReferenceAdDate).TotalDays;

            if (offset < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(ad),
                    $"Dates before {NepaliCalendarData.ReferenceAdDate:yyyy-MM-dd} are not supported.");

            var year = NepaliCalendarData.StartBsYear;
            var month = 1;
            var day = 1;

            while (offset > 0)
            {
                var daysThisMonth = NepaliCalendarData.DaysInMonth(year, month);
                var remainingInMonth = daysThisMonth - day;

                if (offset <= remainingInMonth)
                {
                    day += offset;
                    offset = 0;
                }
                else
                {
                    offset -= remainingInMonth + 1;   // +1 steps onto day 1 of next month
                    day = 1;
                    month++;
                    if (month > 12) { month = 1; year++; }
                }
            }

            return new NepaliDate(year, month, day);
        }

        public static DateTime ToAd(NepaliDate bs)
        {
            var days = 0;

            for (var y = NepaliCalendarData.StartBsYear; y < bs.Year; y++)
                days += NepaliCalendarData.DaysInYear(y);

            for (var m = 1; m < bs.Month; m++)
                days += NepaliCalendarData.DaysInMonth(bs.Year, m);

            days += bs.Day - 1;

            return NepaliCalendarData.ReferenceAdDate.AddDays(days);
        }

        public static DateTime ToAd(int year, int month, int day) =>
            ToAd(new NepaliDate(year, month, day));

        /// <summary>First and last AD date of a BS month - used for monthly reports.</summary>
        public static (DateTime FromAd, DateTime ToAd) BsMonthRange(int bsYear, int bsMonth)
        {
            var first = ToAd(new NepaliDate(bsYear, bsMonth, 1));
            var last = ToAd(new NepaliDate(
                bsYear, bsMonth, NepaliCalendarData.DaysInMonth(bsYear, bsMonth)));
            return (first, last);
        }

        /// <summary>
        /// Nepali fiscal year: Shrawan 1 to the last day of Ashadh.
        /// Pass 2083 to get the range for FY 2083/84.
        /// </summary>
        public static (DateTime FromAd, DateTime ToAd) FiscalYearRange(int startBsYear)
        {
            var from = ToAd(new NepaliDate(startBsYear, 4, 1));               // Shrawan 1
            var endYear = startBsYear + 1;
            var to = ToAd(new NepaliDate(
                endYear, 3, NepaliCalendarData.DaysInMonth(endYear, 3)));     // last of Ashadh
            return (from, to);
        }

        /// <summary>
        /// Checks the table against three independently published conversions.
        /// Call this once at startup. If it throws, the month-length table is wrong.
        /// </summary>
        public static void SelfTest()
        {
            Check(new DateTime(2023, 4, 28), new NepaliDate(2080, 1, 15));
            Check(new DateTime(2024, 7, 24), new NepaliDate(2081, 4, 9));
            Check(new DateTime(2025, 4, 14), new NepaliDate(2082, 1, 1));

            static void Check(DateTime ad, NepaliDate expected)
            {
                var actual = ToBs(ad);
                if (actual != expected)
                    throw new InvalidOperationException(
                        $"Nepali calendar table is wrong: {ad:yyyy-MM-dd} converted to " +
                        $"{actual} but should be {expected}.");

                var back = ToAd(expected);
                if (back != ad.Date)
                    throw new InvalidOperationException(
                        $"Round trip failed: {expected} converted back to {back:yyyy-MM-dd}, " +
                        $"expected {ad:yyyy-MM-dd}.");
            }
        }
    }
}
