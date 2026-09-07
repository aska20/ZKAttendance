# ZKAttendance — changes

29 files: 23 modified, 6 new. No dependency changes (`package.json`, `.csproj` untouched).

**`appsettings.json` is untouched.** No new settings sections, no new keys. The only change outside
the feature files is a single DI registration line in `Program.cs` — without it the enrolment
endpoints cannot resolve their dependency and every call returns a 500.

## How to apply this

`appsettings.json` in this archive is **byte-identical to yours** — it is included only so the
folder is complete.

**Replace the folders, don't merge them.** Windows zip-merge leaves stale files behind, which is
what caused the long run of phantom build errors last time. Delete `ZKAttendance.Api`,
`ZKAttendance.Application`, `ZKAttendance.Infrastructure`, `ZKAttendance.Domain` and `client/src`,
then copy the ones from this archive in.

`client/node_modules` is not in this archive. Keep your existing one — nothing was added to it.

---

## 1. Every date is dd/mm/yyyy

`lib/dates.js` was rewritten. ISO (`yyyy-mm-dd`) now exists only on the wire to the API; anything a
person reads goes through `dmy()`, `bsDmy()` or `dateTime()`.

- `hoursText(7.75)` → `"7h 45m"` instead of `7.75`
- `isFutureDate()` — used to block future hire dates
- `CalendarContext` gained `formatDateShort` (no BS/AD suffix, for tight cells) and
  `formatDateBoth` (`"22/05/2083 BS · 07/09/2026 AD"`, for headers)

Swept through: Attendance, MyAttendance, PendingApprovals, Profile, Employees, Holidays, and the
PDF/Excel export headers. Export **filenames** deliberately stay ISO so they sort correctly in a
folder.

## 2. Calendar rebuilt to look like a patro

`lib/nepaliCalendar.js`:

- BS month table extended **2095 → 2100**
- New: `bsToAdDate`, `buildMonthGrid`, `shiftMonth`, `toNepaliDigits`

**Verified:** the AD↔BS round-trip was tested over ~9,000 consecutive days — zero failures.
07/09/2026 AD resolves to 22/05/2083 BS, which is correct.

> The 2096–2100 rows come from the community dataset, not an official publication. Check them
> against Nepal Patro before running payroll that far out. 2070–2095 are settled.

`components/NepaliMonthCalendar.jsx` (new) is the shared grid: dark month header, Nepali month name
and year in Devanagari, the *other* calendar's day small in the corner, Saturday in red, today as a
filled circle, festival name printed under the number. Used by the holiday editor, the employee
attendance calendar and the date picker.

`NepaliDatePicker` was rebuilt on top of it, so AD mode no longer falls back to the browser's native
date input — both systems look identical now. New `disableFuture` prop (defaults to `true`).

## 3. Attendance notation — star and emoji removed

`components/attendance/StatusMark.jsx` (new). Muster-roll letters instead of decoration:

| Mark | Meaning | Was |
|---|---|---|
| **P** | Present | green circle icon |
| **½** | Half day | — |
| **A** | Absent | red circle icon |
| **H** | Holiday | ⭐ amber star |
| **O** | Weekly off | 📅 calendar emoji |
| **·** | Not due yet | *(previously shown as Absent)* |
| **—** | Before joining | *(previously shown as Absent)* |

A single letter per day means a column can be scanned down and counted by eye, which is the whole
point of a muster roll.

## 4. Absent is no longer assigned to days that haven't happened

This was the substantive bug, and the fix is server-side in `OverviewApiController` — the UI is not
guessing.

A day is marked absent **only once it is closed**:

- it is in the past, **or**
- it is today *and* the shift end plus a grace period has passed

Before that it returns `Upcoming`. Days before the employee's hire date return `Not Joined`, so a
new joiner no longer starts with a wall of red.

`totalDays` now counts only **closed** working days, so present + absent always reconciles.
Added `upcomingDays`, `scheduledDays`, `totalHours`, `avgHours`.

An employee with a `WorkShift` assigned uses **that shift's `EndTime`**, which is where this belongs
and is already per-employee data. Employees with no shift fall back to constants at the top of
`OverviewApiController`:

```csharp
private static readonly TimeSpan DefaultCloseTime = new(18, 0, 0);
private const int CloseGraceMinutes = 30;
private const double HalfDayUnderHours = 4.0;
```

These are in code rather than configuration so nothing new goes into `appsettings.json`. Edit them
there if 18:00 is wrong for your office — or better, assign shifts.

`/summary` got the same treatment — asking for "this month" halfway through the month no longer
reports everyone absent for the remaining days.

## 5. Daily log shows every scan; the summary shows first-in / last-out

`DayDetailModal` was rebuilt as a summary report:

1. Verdict band — present / half day / absent
2. Figures — first check-in, last check-out, time inside, first-to-last span
3. **Sessions** — punches paired into in/out spells with per-session duration and a total
4. **All scans** — every punch, however many, with type, method, device, manual-vs-device

Someone who scans four times (in, lunch out, lunch in, out) still reads as one clean row in the
grid. "Time inside" excludes the lunch gap; "first to last" includes it, and is flagged when the two
differ.

Session pairing prefers the device's `attendanceType`, but falls back to strict alternation when the
types are incomplete — terminals report in/out inconsistently across models, and trusting the field
blindly gives wrong totals.

## 6. Employees — table and calendar views, detail with average hours

- View toggle **Table ⇄ Calendar** (remembered in `localStorage`). Calendar view is a card grid
  sized for tapping.
- Clicking an employee opens a detail modal with a **30-day attendance summary**: present, absent,
  working days, **average hours per day**, and the most recent day attended with its check-in,
  check-out and hours.
- Future hire dates are blocked in the picker and stripped in `toPayload`.
- `EmployeeCalendarModal` rebuilt on the patro grid, with a month summary strip and each day showing
  its check-in–check-out range.

## 7. Attendance page

- Legend replaced with the new key
- Employee names are clickable → their attendance calendar
- New **Avg hrs** column
- Weekly off renders as muted grey, named holidays in violet

## 8. ZKTeco registration from this system

This is the "add here → register on the terminal" gap. Adding a person used to write a database row
only, so their first scan arrived unattributed.

**`IZkDeviceReader` gained a write half:** `SetUserAsync`, `DeleteUserAsync`,
`StartRemoteEnrollAsync`, `CancelCaptureAsync`, `GetTemplatesAsync`, `SetTemplateAsync`,
`RefreshDataAsync`. `GetUsersAsync` was a stub returning an empty list — it is now implemented,
handling both the 28-byte and 72-byte user record layouts.

**`DeviceEnrollmentOrchestrator`** (new) runs the sequence:

```
add employee → PushUserToDevicesAsync   user record on every terminal
             → StartEnrollmentAsync     master switches to "place finger" / opens the face camera
             → PullAndPropagateAsync    template cached, then copied to the rest
```

**UI:** `DeviceEnrollPanel` gives a **Register on ZKTeco** button. Pressing it opens the
registration screen on the terminal itself; a live banner then polls every 3s and auto-propagates
once a template appears, giving up after 2 minutes. It appears in the employee edit form, as a row
action, and automatically right after creating someone.

## 9. Multiple devices staying in sync

One active device is the **Master** — the only place a finger is physically captured, because
somebody has to stand there. Everything else is a **Slave** and receives its users by being written
to.

Templates are cached in `FingerprintTemplates`, which is what makes adding a third or fourth
terminal a background job rather than a day of calling everyone back to the sensor. It is also the
only protection against the master being cleared.

There is **no background service and no new settings.** Propagation runs only when asked for:

- the **Register on ZKTeco** flow copies automatically once a scan is captured
- **Copy to other terminals** on the employee panel
- **Push users** per terminal on the Devices page, which fills a machine added later or one that was
  offline
- `POST /api/Devices/{id}/reconcile-all` repairs everything in one call

The Devices page also gained a Filled / Not filled column and a banner when no device is marked
Master.

## 10. Holidays — split screen

Calendar on the left, entry form on the right, each scrolling independently so a long festival list
never pushes Save off screen. Click a day to load it, or pick the date in the form. **Future dates
are allowed here** — that is the point of a holiday calendar. Saturday is rejected with an
explanation since it is already the fixed weekly off. Both AD and BS throughout.

## 11. Not touched, deliberately

Branches, Devices and Departments have **no date controls** added, as you asked.

---

## Verification actually performed

- **React client builds clean** (`vite build`, 333 modules) after every change.
- **Linter: 0 errors.** The 4 unused-variable warnings are pre-existing, in files not touched here
  (Dashboard, ErrorLog, SummaryReport, DailyReport).
- **Domain + Application compile clean** against real .NET 8 reference assemblies.
- **Both device readers compile clean.** This caught three genuine bugs — `Span<byte>` locals are
  illegal in `async` methods (CS4012), which my first draft of `GetUsersAsync` and
  `GetTemplatesAsync` both did. Fixed by extracting synchronous `ParseUsers` / `ParseTemplates`.
- BS↔AD conversion round-tripped over ~9,000 days with zero failures.

**Not verified:** a full `dotnet build`. NuGet was unreachable from the build environment, so the
Api and Infrastructure projects could not be restored. Controllers and the orchestrator are
syntax-checked but not type-checked against EF Core and ASP.NET. Build in Visual Studio first.

## One thing to test on hardware before trusting it

`ZkTcpDeviceReader.SetTemplateAsync` — writing a fingerprint template **to** a device — is the one
operation whose byte framing genuinely varies across ZKTeco firmware families, and there was no
hardware here to check it against. It is marked with a comment in the source.

Everything else in that class either follows a layout stable across the ZKTeco range or is a
fixed-size command. Reading templates, writing users and starting enrolment are all low-risk.

The orchestrator is written so a failure here degrades to *"user created, fingerprint still to be
enrolled on this terminal"* rather than a silent gap — you will see the honest partial state in the
panel instead of a false green tick.

To exercise the whole flow without hardware, your existing `SyncConfiguration.DeviceProtocol` is
already set to `"Fake"` — no change needed. `FakeDeviceReader` now keeps a per-IP simulated user and
template table, and completes a "scan" about 6 seconds after enrolment starts.

---

## Still open

- **Leave types** — not started.
- **`WorkShift` lateness / overtime detection** — `EndTime` is now consumed for the absent cut-off,
  but late-arrival and OT flags are not implemented.
- **Device clock drift** — read by `GetDeviceInfoAsync`, not surfaced in the UI.
- **Punch correction with audit trail** — manual punches are flagged, but there is no edit trail.
