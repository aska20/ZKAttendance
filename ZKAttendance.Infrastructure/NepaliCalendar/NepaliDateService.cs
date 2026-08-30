using System;
using System.Collections.Generic;

namespace ZKAttendance.Infrastructure.NepaliCalendar
{
    public interface INepaliDateService
    {
        NepaliDate Today { get; }

        NepaliDate ToBs(DateTime ad);
        DateTime ToAd(NepaliDate bs);
        DateTime ToAd(string bsText);

        string Format(DateTime ad);
        string FormatLong(DateTime ad);
        string FormatWithTime(DateTime ad);
        string? FormatNullable(DateTime? ad);

        (DateTime FromAd, DateTime ToAd) MonthRange(int bsYear, int bsMonth);
        (DateTime FromAd, DateTime ToAd) FiscalYearRange(int startBsYear);

        bool IsWeeklyOff(DateTime ad);

        IReadOnlyList<(int Value, string Text)> MonthOptions();
        IReadOnlyList<int> YearOptions();
    }

    /// <summary>
    /// The single place in the application that knows about Bikram Sambat.
    /// Controllers and views call this; nothing else touches the converter.
    ///
    /// Register in Program.cs:
    ///     builder.Services.AddSingleton&lt;INepaliDateService, NepaliDateService&gt;();
    /// </summary>
    public class NepaliDateService : INepaliDateService
    {
        private readonly DayOfWeek _weeklyOff;

        /// <param name="weeklyOff">
        /// Saturday in Nepal. Passed in so an organisation with a different
        /// off day does not need a code change.
        /// </param>
        public NepaliDateService(DayOfWeek weeklyOff = DayOfWeek.Saturday)
        {
            _weeklyOff = weeklyOff;
            NepaliDateConverter.SelfTest();   // fail fast if the table is wrong
        }

        public NepaliDate Today => ToBs(DateTime.Today);

        public NepaliDate ToBs(DateTime ad) => NepaliDateConverter.ToBs(ad);

        public DateTime ToAd(NepaliDate bs) => NepaliDateConverter.ToAd(bs);

        public DateTime ToAd(string bsText) => NepaliDateConverter.ToAd(NepaliDate.Parse(bsText));

        /// <summary>"2083-01-15"</summary>
        public string Format(DateTime ad) => ToBs(ad).ToString();

        /// <summary>"15 Baisakh 2083"</summary>
        public string FormatLong(DateTime ad) => ToBs(ad).ToLongString();

        /// <summary>"2083-01-15 09:12" - the date is BS, the time is untouched.</summary>
        public string FormatWithTime(DateTime ad) => $"{ToBs(ad)} {ad:HH:mm}";

        public string? FormatNullable(DateTime? ad) => ad.HasValue ? Format(ad.Value) : null;

        public (DateTime FromAd, DateTime ToAd) MonthRange(int bsYear, int bsMonth) =>
            NepaliDateConverter.BsMonthRange(bsYear, bsMonth);

        public (DateTime FromAd, DateTime ToAd) FiscalYearRange(int startBsYear) =>
            NepaliDateConverter.FiscalYearRange(startBsYear);

        public bool IsWeeklyOff(DateTime ad) => ad.DayOfWeek == _weeklyOff;

        public IReadOnlyList<(int Value, string Text)> MonthOptions()
        {
            var list = new List<(int, string)>(12);
            for (var m = 1; m <= 12; m++)
                list.Add((m, $"{m:00} - {NepaliCalendarData.MonthNamesEn[m - 1]}"));
            return list;
        }

        public IReadOnlyList<int> YearOptions()
        {
            var list = new List<int>();
            for (var y = NepaliCalendarData.MinBsYear; y <= NepaliCalendarData.MaxBsYear; y++)
                list.Add(y);
            return list;
        }
    }
}
