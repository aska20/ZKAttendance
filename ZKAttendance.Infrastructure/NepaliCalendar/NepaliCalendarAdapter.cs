using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.NepaliCalendar;

/// <summary>
/// Adapter: implements the Application's INepaliCalendar port using the
/// converter in this layer.
///
/// WHY THE PORT AND THE ADAPTER ARE SEPARATE
/// -----------------------------------------
/// Application owns the interface because Application decides what it needs
/// from a calendar. Infrastructure owns this class because converting AD to BS
/// requires a published month-length table, which is external data and can
/// change independently of any business rule - exactly the kind of detail the
/// inner layers should not know about.
///
/// The practical payoff: a unit test of AttendanceCalculationService can pass a
/// stub that returns a fixed string, with no calendar table and no dependency
/// on this project at all.
/// </summary>
public class NepaliCalendarAdapter : INepaliCalendar
{
    private readonly INepaliDateService _service;

    public NepaliCalendarAdapter(INepaliDateService service) => _service = service;

    public string ToBsString(DateTime ad) => _service.Format(ad);

    public (DateTime FromAd, DateTime ToAd) BsMonthRange(int bsYear, int bsMonth)
        => _service.MonthRange(bsYear, bsMonth);

    public (DateTime FromAd, DateTime ToAd) FiscalYearRange(int startBsYear)
        => _service.FiscalYearRange(startBsYear);

    public bool IsWeeklyOff(DateTime ad) => _service.IsWeeklyOff(ad);
}

/// <summary>System clock. Injected so tests can freeze time.</summary>
public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime LocalNow => DateTime.Now;
    public DateTime Today => DateTime.Today;
}
