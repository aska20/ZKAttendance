namespace ZKAttendance.Application.Abstractions
{
    /// <summary>
    /// Wall clock, injected so tests can freeze time instead of depending on
    /// whatever DateTime.Now happens to return during a test run.
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
        DateTime LocalNow { get; }
        DateTime Today { get; }
    }

    /// <summary>
    /// Gregorian to Bikram Sambat conversion, as a PORT.
    ///
    /// AttendanceCalculationService lives in Application and needs to stamp a
    /// BS date on every row. The converter itself is an adapter over a
    /// published month-length table, so it lives in Infrastructure.
    ///
    /// Without this interface, Application would need a using for
    /// Infrastructure — an inner layer importing an outer one, which is the
    /// exact thing the dependency rule forbids and which would not compile,
    /// because Application has no project reference to Infrastructure.
    ///
    /// Implemented by Infrastructure/NepaliCalendar/NepaliCalendarAdapter.
    /// </summary>
    public interface INepaliCalendar
    {
        string ToBsString(DateTime ad);
        (DateTime FromAd, DateTime ToAd) BsMonthRange(int bsYear, int bsMonth);
        (DateTime FromAd, DateTime ToAd) FiscalYearRange(int startBsYear);
        bool IsWeeklyOff(DateTime ad);
    }
}
