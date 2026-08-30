using System.Collections.Generic;

namespace ZKAttendance.Infrastructure.NepaliCalendar
{
    /// <summary>
    /// Month-length table for the Bikram Sambat calendar.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// BS month lengths are not fixed. Baisakh can be 30, 31 or 32 days depending
    /// on the year, so a BS date cannot be calculated with a formula the way a
    /// Gregorian date can. Every converter in the world works from a lookup table
    /// like this one.
    ///
    /// HOW IT WORKS
    /// ------------
    /// We know one anchor: Baisakh 1, 2080 BS = 14 April 2023 AD.
    /// From there, every other date is reached by counting days forward or
    /// backward through this table.
    ///
    /// VERIFIED
    /// --------
    /// Running NepaliDateConverter.SelfTest() checks three independently
    /// published conversions. If you add years below and the self-test still
    /// passes, the earlier years are still correct.
    ///
    /// ADDING MORE YEARS
    /// -----------------
    /// Copy the 12 month lengths for the new year from the official
    /// Nepal Patro published by the Ministry of Information, add a row, then
    /// run SelfTest() plus a spot check of a known date in that year.
    /// </summary>
    public static class NepaliCalendarData
    {
        /// <summary>The anchor: Baisakh 1 of <see cref="StartBsYear"/>.</summary>
        public static readonly System.DateTime ReferenceAdDate = new System.DateTime(2023, 4, 14);

        public const int StartBsYear = 2080;

        /// <summary>
        /// Days in each of the 12 BS months, indexed by BS year.
        /// Order: Baisakh, Jestha, Ashadh, Shrawan, Bhadra, Ashwin,
        ///        Kartik, Mangsir, Poush, Magh, Falgun, Chaitra.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, int[]> MonthDays =
            new Dictionary<int, int[]>
            {
                // BS year        Bai Jes Ash Shr Bha Ash Kar Man Pou Mag Fal Cha    total
                [2080] = new[] { 31, 32, 31, 32, 31, 30, 30, 30, 29, 29, 30, 30 }, // 365
                [2081] = new[] { 31, 31, 32, 32, 31, 30, 30, 30, 29, 30, 30, 30 }, // 366
                [2082] = new[] { 30, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30 }, // 365
                [2083] = new[] { 31, 31, 32, 31, 31, 30, 30, 30, 29, 30, 30, 30 }, // 365
                [2084] = new[] { 31, 31, 32, 32, 30, 31, 30, 30, 29, 30, 30, 30 }, // 366
                [2085] = new[] { 31, 32, 31, 32, 31, 30, 30, 30, 29, 30, 30, 30 }, // 366
                [2086] = new[] { 31, 31, 32, 31, 31, 30, 30, 30, 29, 30, 30, 30 }, // 365
            };

        public static readonly string[] MonthNamesEn =
        {
            "Baisakh", "Jestha", "Ashadh", "Shrawan", "Bhadra", "Ashwin",
            "Kartik", "Mangsir", "Poush", "Magh", "Falgun", "Chaitra"
        };

        public static readonly string[] MonthNamesNp =
        {
            "बैशाख", "जेठ", "असार", "साउन", "भदौ", "असोज",
            "कार्तिक", "मंसिर", "पुष", "माघ", "फागुन", "चैत"
        };

        public static readonly string[] DayNamesEn =
        {
            "Aaitabar", "Sombar", "Mangalbar", "Budhabar",
            "Bihibar", "Sukrabar", "Sanibar"
        };

        public static int MinBsYear => StartBsYear;

        public static int MaxBsYear
        {
            get
            {
                var max = StartBsYear;
                foreach (var y in MonthDays.Keys)
                    if (y > max) max = y;
                return max;
            }
        }

        public static int DaysInMonth(int bsYear, int bsMonth)
        {
            if (!MonthDays.TryGetValue(bsYear, out var months))
                throw new System.ArgumentOutOfRangeException(
                    nameof(bsYear),
                    $"BS year {bsYear} is not in the calendar table. " +
                    $"Supported range is {MinBsYear}-{MaxBsYear}. " +
                    "Add the year to NepaliCalendarData.MonthDays.");

            if (bsMonth < 1 || bsMonth > 12)
                throw new System.ArgumentOutOfRangeException(nameof(bsMonth));

            return months[bsMonth - 1];
        }

        public static int DaysInYear(int bsYear)
        {
            var total = 0;
            for (var m = 1; m <= 12; m++) total += DaysInMonth(bsYear, m);
            return total;
        }
    }
}
