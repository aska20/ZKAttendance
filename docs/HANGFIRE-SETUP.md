# Hangfire

## It is now wired in

The job, the dashboard filter, the package references and the schedule are all in the solution. You do **not** need to paste anything.

| Piece | File |
|---|---|
| The job | `ZKAttendance.Api/Jobs/AttendanceDailyJob.cs` |
| Dashboard guard | `ZKAttendance.Api/Security/HangfireAdminFilter.cs` |
| Services, dashboard, schedule | `ZKAttendance.Api/Program.cs` |
| Packages | `ZKAttendance.Api.csproj` |

**On first open, let NuGet restore.** `Hangfire.AspNetCore` and `Hangfire.SqlServer` 1.8.14 are referenced but were not downloadable in the environment this was built in, so those three files are the only code in this release that has not been compiled. Everything else has.

If restore fails, right-click the solution and choose **Restore NuGet Packages**, or run `dotnet restore`.

## What runs, and when

`"30 17 * * 0-5"` is **17:30, Sunday to Friday**. Saturday is skipped since it is the weekly off.

Change it in `Program.cs` to match your `OfficeEndTime` in Settings. Run it **after** the office closes, or people who have not checked out yet get a Partial email.

The timezone resolves `Nepal Standard Time` on Windows and falls back to `Asia/Kathmandu` on Linux, then to server local time, so it registers on any host.

## Design notes

**The job is deliberately thin.** It calls `IDailyAttendanceService.RunEndOfDayAsync`, which is the same service the admin buttons call. The attendance logic is not duplicated.

**The date is resolved inside the job, not in the schedule.** Passing `DateTime.Today` into `RecurringJob.AddOrUpdate` would freeze it at whatever it was when the app last started, and the same day would be reprocessed for ever. This is the classic Hangfire mistake.

**One worker, not the default.** The job sends email in a loop; two copies at once would send everyone duplicates. `DisableConcurrentExecution` guards it as well.

**Two retries, not Hangfire's default ten.** A partial failure retried ten times would re-send to everyone who already got one. Per-recipient failures are already swallowed inside the service, so a retry only happens if something larger breaks.

**The dashboard is Admin only.** Hangfire allows everyone when no filter is supplied, meaning any visitor could trigger the email job. Note it authenticates from the browser session, not the SPA's JWT, so if your admins only ever sign in through the API the dashboard will refuse them. Restrict it at the reverse proxy instead if that is your setup.

## Check it works

1. Start the app, open `/hangfire`, signed in as Admin.
2. **Recurring jobs** should list `daily-attendance` with its next run time.
3. Press **Trigger now**.
4. Watch **Succeeded**, then look for `Scheduled end-of-day job starting` in your Serilog output.

Failures show the exception and stack trace on the job's detail page.

## Testing without waiting for 17:30

Everything the job does is on the API already, so you do not need Hangfire to test the logic:

| Action | Endpoint |
|---|---|
| Recalculate a day, no email | `POST /api/Attendance/daily/process?date=2026-09-08` |
| Recalculate and email | `POST /api/Attendance/daily/process?date=2026-09-08&sendEmails=true` |
| Emails only | `POST /api/Attendance/daily/send-emails?date=2026-09-08` |
| Is SMTP set up | `GET /api/Attendance/daily/email-status` |
| One-day report | `GET /api/Attendance/daily/report?date=2026-09-08` |
| Cross-tab | `GET /api/Attendance/daily/monthly?from=2026-08-17&to=2026-09-16` |

The same buttons exist on the Daily Report screen.

---

## Email configuration

SMTP settings live in the `SystemSettings` table under category `Email`, not in `appsettings.json`, so the mail server can be changed without a redeploy.

| SettingKey | Example |
|---|---|
| SmtpHost | smtp.gmail.com |
| SmtpPort | 587 |
| UseSsl | true |
| Username | attendance@company.com |
| Password | app password |
| FromAddress | attendance@company.com |
| FromName | Attendance System |

```sql
INSERT INTO SystemSettings (SettingKey, SettingValue, Category, IsActive, CreatedDate) VALUES
 ('SmtpHost',    'smtp.gmail.com',          'Email', 1, GETDATE()),
 ('SmtpPort',    '587',                     'Email', 1, GETDATE()),
 ('UseSsl',      'true',                    'Email', 1, GETDATE()),
 ('Username',    'attendance@company.com',  'Email', 1, GETDATE()),
 ('Password',    'your-app-password',       'Email', 1, GETDATE()),
 ('FromAddress', 'attendance@company.com',  'Email', 1, GETDATE()),
 ('FromName',    'Attendance System',       'Email', 1, GETDATE());
```

Use a Gmail **app password**, not the account password. Settings are cached for five minutes, so a change takes up to that long to take effect.

Until this is filled in, `IsConfiguredAsync` returns false and the email endpoints say so rather than throwing.

---

## Unused columns

You asked about removing dead columns. I have **not dropped any**, because dropping is irreversible and several of these are referenced in places I could not exhaustively check without running the app.

Here is what looks unused, for you to confirm before acting:

| Table | Column | Notes |
|---|---|---|
| Employees | `SSN` | Never read anywhere in the codebase. Also personal data you may not want stored. |
| Employees | `CheckHoliday`, `CheckOvertime`, `CheckEarly` | Written on create, never read. Overtime and early-leave are not implemented. |
| WorkShifts | `Color` | Not used by the frontend. |
| WorkShifts | `CheckInWindowStart/End`, `CheckOutWindowStart/End` | Window logic is not implemented; the policy in Settings covers this instead. |
| DeviceStatuses | `FaceCount`, `FreeSpace`, `TotalSpace` | Populated only if the device reports them; nothing displays them. |
| SyncLogs | `ServerName` | Written, never displayed. |

`Employees.CheckAttendance` and `CheckLate` **are** used, so leave those.

Before dropping anything, run this to confirm a column is genuinely empty:

```sql
SELECT COUNT(*) AS NonNullRows FROM Employees WHERE SSN IS NOT NULL;
```

If a column has data, someone somewhere is filling it, and that is worth understanding before it disappears.
