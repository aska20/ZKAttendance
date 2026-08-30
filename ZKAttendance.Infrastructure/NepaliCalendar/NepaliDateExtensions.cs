using System;

namespace ZKAttendance.Infrastructure.NepaliCalendar
{
    /// <summary>
    /// Shortcuts for Razor views so a date can be shown in BS without
    /// injecting the service into every view.
    ///
    /// In _ViewImports.cshtml add:
    ///     @using ZKAttendance.Infrastructure.NepaliCalendar
    ///
    /// Then in any view:
    ///     @log.AttendanceTime.ToBsString()          -> 2083-01-15
    ///     @log.AttendanceTime.ToBsWithTime()        -> 2083-01-15 09:12
    ///     @emp.HireDate.ToBsStringOrDash()          -> 2079-08-03  or  "-"
    /// </summary>
    public static class NepaliDateExtensions
    {
        public static NepaliDate ToNepali(this DateTime ad) => NepaliDateConverter.ToBs(ad);

        public static string ToBsString(this DateTime ad) => NepaliDateConverter.ToBs(ad).ToString();

        public static string ToBsLongString(this DateTime ad) =>
            NepaliDateConverter.ToBs(ad).ToLongString();

        public static string ToBsWithTime(this DateTime ad) =>
            $"{NepaliDateConverter.ToBs(ad)} {ad:HH:mm}";

        public static string ToBsDevanagari(this DateTime ad) =>
            NepaliDateConverter.ToBs(ad).ToNepaliString();

        public static string ToBsStringOrDash(this DateTime? ad) =>
            ad.HasValue ? NepaliDateConverter.ToBs(ad.Value).ToString() : "-";

        public static string ToBsWithTimeOrDash(this DateTime? ad) =>
            ad.HasValue ? ad.Value.ToBsWithTime() : "-";

        public static DateTime ToGregorian(this NepaliDate bs) => NepaliDateConverter.ToAd(bs);
    }
}
