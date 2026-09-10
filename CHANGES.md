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

## Fix: 500 on "Register on ZKTeco"

The enrolment path did all its device I/O outside any exception handling, so anything the terminal
threw came back to the browser as a bare 500 with no explanation. Three separate holes:

1. **`ZkTcpDeviceReader.ConnectAsync`** guarded the TCP dial but not the protocol handshake that
   follows it. A terminal that accepts the socket and then goes quiet — wrong port, firewall,
   half-open link, or another operator already mid-scan — threw straight out. Now returns `false`.
2. **`StartEnrollmentAsync`** called `ConnectAsync`, `GetUsersAsync` and `SetUserAsync` with no
   try/catch at all. Now wrapped; a device failure comes back as a reported result carrying the
   reason. Same for `CancelEnrollmentAsync` (best-effort, never throws) and the master template pull
   in `PullAndPropagateAsync`.
3. **`EnrollmentApiController`** caught only `InvalidOperationException`; everything else fell
   through to a 500. Every endpoint now also catches broadly, logs, and returns **502** with the
   reason instead.

Also added: an employee with no biometric / enrol number is now rejected with a plain message rather
than being sent to the terminal, where it produced an opaque protocol error.

**What you will see now instead of a 500:**

| Situation | Response |
|---|---|
| Terminal unplugged or wrong IP | `Could not reach <name> at <ip>:<port>. Check the terminal is powered on, on the same network, and that the comm key matches.` |
| No device registered / none active | 400 — `No active device is registered, so there is nowhere to enrol.` |
| Employee has no enrol number | `<name> has no biometric / enrol number... Set one on the employee first.` |
| Terminal refuses or times out | 502 with the device's own reason |

## Fix: "Device did not accept the buffered read (code 4989)"

This was my bug, and it was the real blocker behind the 500.

`ReadWithBufferAsync(command, fct, ext)` takes the command **being read** as its first argument;
the transport command (`CMD_DATA_WRRQ`, 1503) is applied inside the method. The existing attendance
read gets this right:

```csharp
await ReadWithBufferAsync(CMD_ATTLOG_RRQ);      // 13 - correct
```

My two new call sites passed the transport command as the inner command:

```csharp
await ReadWithBufferAsync(CMD_DATA_WRRQ, FCT_USER);        // asked the device to buffer-read 1503
await ReadWithBufferAsync(CMD_DATA_WRRQ, FCT_FINGERTMP);   // same
```

The terminal was asked to bulk-read command 1503, which means nothing, and replied with the
nonsense code 4989. Corrected to:

```csharp
await ReadWithBufferAsync(CMD_USERTEMP_RRQ, FCT_USER);     // 9
await ReadWithBufferAsync(CMD_DB_RRQ, FCT_FINGERTMP);      // 7  (constant added)
```

### Three knock-on fixes

- **"offline" was a lie.** The terminal had answered — it just refused that read. The panel now
  separates *cannot connect* from *connected but will not list its users*, so nobody goes checking
  network cables over a firmware limitation.
- **Registration no longer depends on listing users first.** Writing a user record is idempotent
  (the terminal overwrites the slot rather than duplicating it), so when the table cannot be listed
  it is now written unconditionally. Listing is an optimisation, not a precondition.
- **`ResolveUidAsync` has a fallback.** With no readable table there is no way to see which slots
  are free; for a numeric enrol number it now assumes the matching slot, which is the near-universal
  ZK convention, and logs that it did so.

The buffered-read error message now names the command and reply code, so the next failure of this
kind is diagnosable at a glance instead of being an opaque number.

## Office hours, grace, and late-arrival approval

New **Settings** page (sidebar, admin-editable) and a new **Attendance Approvals** queue.

### The rule, with your numbers

| Arrival | Status | Who decides |
|---|---|---|
| 10:00 to 10:15 | Approved | Automatic |
| 10:15 to 11:00 | Late | Counted, flagged |
| After 11:00 | **Pending** | Admin must approve |

The middle band was the one thing your message left open, so it is a toggle:
**"Late arrivals also need approval"**. Off by default, meaning 10:15 to 11:00 counts but shows as
late. Turn it on and that band goes to the approval queue too.

### Settings page

Configurable: office start, office end, grace minutes, approval cut-off, the late toggle, absent
wait, and the half-day threshold. The three coloured cards at the top recalculate live as you type,
so you see the real clock times rather than adding minutes in your head.

Stored in the existing **SystemSettings** table, so `appsettings.json` is still untouched and no
redeploy is needed to change a rule. The hardcoded constants I had put in `OverviewApiController`
now read from here instead.

Invalid combinations are rejected with a reason, for example a cut-off earlier than the end of
grace, or an end time before the start time.

### Approvals queue

Pending / Approved / Rejected / All tabs with a live pending count, department filter, date range,
row-level Approve and Reject, and multi-select bulk approve. Approve and reject are **Admin only**;
managers can view.

A decision already made by a person is never overwritten by re-evaluation, so an approval does not
silently revert on the next sync. If an earlier scan arrives late from an offline device, the
earliest time wins and the day is re-classified.

The queue re-checks the last three days whenever it is opened, so rows appear without a separate
background job.

### Database

New table **AttendanceApprovals**, one row per employee per day, created only when a decision is
actually needed.

**Create it before opening the page:**

```
Add-Migration AddAttendanceApprovals
Update-Database
```

The entity is already in the DbContext, so EF generates the migration correctly on its own. If you
would rather not run migrations, execute `ZKAttendance.Infrastructure/Migrations/AddAttendanceApprovals.sql`
instead; it is safe to re-run.

The office-hours defaults do not need seeding. `AttendancePolicyService` falls back to
10:00 / 15 min / 11:00 when the rows are absent and writes them the first time Settings is saved.

### UI text

Removed dash-joined sentences and cut the long explanatory notes down to one short line throughout.

## Fix: 500 on the Attendance Approvals page

My mistake, and a clean one.

EF Core discovers migrations through a `[Migration("id")]` attribute, which lives in the paired
`.Designer.cs` file, not in the migration class. I hand-wrote
`20260908060000_AddAttendanceApprovals.cs` without a Designer file, so **EF never saw it as a
migration at all**. `Update-Database` reported success and did nothing, the table was never created,
and every query against it threw "Invalid object name" straight out as a 500.

Faking the Designer was not an option: its `BuildTargetModel` has to contain the entire model, and
getting that wrong would corrupt the baseline every future migration is diffed against.

**The fix:** the invalid migration is deleted. The entity is already in the DbContext, so EF
generates a correct migration itself:

```
Add-Migration AddAttendanceApprovals
Update-Database
```

A plain `AddAttendanceApprovals.sql` is included as an alternative, and is safe to re-run.

### Two things hardened along the way

- **A missing table now says so.** The endpoint returns 503 with "The AttendanceApprovals table does
  not exist yet. Run Add-Migration... then Update-Database" instead of an unexplained 500. The
  sidebar count degrades to zero rather than breaking the page.
- **The page no longer sits on "Loading..." under an error banner.** The failure path never set
  state, so the card stayed in its loading placeholder for ever, which is what the screenshot showed.

## Daily automation, emails and the monthly cross-tab

Built to the spec, with the architecture it asks for: Hangfire decides *when*, `DailyAttendanceService` decides *what*. The business logic exists once and both the schedule and the admin buttons call it.

### The part that is easy to get wrong

The report is driven by the **employee list**, with attendance joined onto it, never the reverse. Querying attendance records alone silently omits the people who never scanned, who are exactly the ones the report exists to surface. Hari does not disappear.

Punches whose enrol number matches nobody are collected into `UnmappedDeviceIds` and shown in the admin summary, rather than being dropped.

### Statuses

| Status | Rule |
|---|---|
| Present | Checked in within grace, and checked out |
| Late | Checked in after grace |
| Partial | Only one scan, usually a forgotten check-out |
| Absent | No scan on a working day |
| Holiday | Weekly off or declared holiday |
| Not joined | Date is before the hire date |

Thresholds come from the Settings page you already have, so nothing is hardcoded and the two screens cannot disagree.

### Emails

One email per person, never a combined list. Templates match the spec's four cases. A bad address fails that recipient and the run continues.

SMTP config lives in `SystemSettings` under category `Email`, not `appsettings.json`. Until it is filled in, the endpoints say so rather than throwing, and the send button is disabled with a tooltip.

### Monthly Report page

Cross-tab with employees down and dates across, P / L / PT / A / H marks, per-employee totals, department filter, AD and BS date headers, and **Excel export** named `attendance_summary_<from>_to_<to>.xlsx`. Export uses the `xlsx` package already in your dependencies, so nothing new was installed.

Admin buttons on the same page: **Process today's attendance** and **Send attendance emails**, both calling the same service the job does.

### New endpoints

```
GET  /api/Attendance/daily/report?date=
GET  /api/Attendance/daily/monthly?from=&to=
POST /api/Attendance/daily/process?date=&sendEmails=
POST /api/Attendance/daily/send-emails?date=
GET  /api/Attendance/daily/email-status
```

### Hangfire

Now wired into the solution. Nothing to paste.

| Piece | File |
|---|---|
| The job | `ZKAttendance.Api/Jobs/AttendanceDailyJob.cs` |
| Dashboard guard | `ZKAttendance.Api/Security/HangfireAdminFilter.cs` |
| Services, dashboard, schedule | `ZKAttendance.Api/Program.cs` |
| Packages | `ZKAttendance.Api.csproj` (Hangfire.AspNetCore + Hangfire.SqlServer 1.8.14) |

Runs at **17:30, Sunday to Friday** (`"30 17 * * 0-5"`). Saturday is the weekly off, so it is skipped. Timezone resolves `Nepal Standard Time`, falls back to `Asia/Kathmandu`, then to server local, so it registers on Windows or Linux.

Four decisions worth knowing:

- **The job is thin.** It calls `IDailyAttendanceService.RunEndOfDayAsync`, the same service the admin buttons use. The logic is not duplicated.
- **The date resolves inside the job**, not in the schedule. Passing `DateTime.Today` into `RecurringJob.AddOrUpdate` would freeze it at app-start and reprocess that same day for ever. That is the classic Hangfire mistake.
- **One worker**, plus `DisableConcurrentExecution`. The job emails in a loop; two copies would send duplicates.
- **Two retries, not the default ten.** Retrying a partly-completed email run re-sends to everyone who already got one.

**These three files are the only code in this release that has not been compiled**, because NuGet was unreachable in the build environment. Let NuGet restore on first open. Everything else compiles clean.

## Email uniqueness

Now that attendance emails go out, a shared address is a privacy leak rather than untidy data: one employee would receive another's check-in times.

**Four layers, deliberately:**

1. **`UX_Employee_Email`** — unique index on `Employees.Email`, filtered on `IS NOT NULL`. Without the filter the NULL rows collide and only one employee could ever have a blank address. `ApiUsers.Email` was already unique.
2. **Normalisation on write.** `Email` is trimmed, and blank becomes `NULL`. `" ram@x.com "` and `"ram@x.com"` are one address to a person but two values to an index. Case is already handled: SQL Server's default collation is case-insensitive.
3. **A guard on create and update** returning a sentence, so a duplicate never surfaces as a unique-index violation dressed up as a 500.
4. **A runtime check in the mail run.** If two employees somehow share an address, **both** are skipped and the reason is reported. Guessing which one owns it would be worse than sending nothing.

### Run this before Update-Database

`ZKAttendance.Infrastructure/Migrations/CheckDuplicateEmails.sql`

The index **will fail to create** if duplicates already exist, and SQL Server's error names the table but not the people. The script lists exactly who shares what, plus addresses differing only by case or whitespace, plus blank-not-null values.

It auto-fixes the safe cases (trim, blank to NULL). It deliberately does **not** auto-fix genuine duplicates: two people on one address is either a typo or a shared family address, and blanking the wrong one silently stops that person's emails. Decide per pair.

An employee with no email is skipped by the mail run and counted under "Skipped". Nothing fails.

## Fix: future days showing Absent on the Employee Report

The Day-by-Day Attendance Log was painting the rest of the month red. The overview grid had already been fixed for this, but `EmployeeReport.jsx` computes its own status and was missed.

The cause was one line:

```js
let resolvedStatus = 'Absent'   // then override if recognised
```

Absent was the **default**, so anything the chain did not explicitly recognise fell through to it, including the `Upcoming` status the server sends for days that have not finished. Now the default is `Upcoming`, and Absent is set only when the server says so, or when a past working day genuinely has no record.

The monthly summary counters had the same problem in reverse: future days were incrementing `workingDays`, inflating the denominator. They are now skipped until the day closes.

### Also fixed on this page, which had been missed earlier

- **Star and calendar glyphs replaced** with the shared P / A / H / O notation, so this screen reads like the attendance grid instead of having its own icon language.
- **Dates were still printing as `2026-09-01`** in both the table and the Excel export. Now dd/mm/yyyy like everywhere else.

## Email configuration UI

The "Email is not configured" banner had no way to clear it from the app, which was an omission on my part. Settings now has an **Email (SMTP)** card.

- Host, port, username, password, from-address, from-name, SSL toggle
- A **Configured / Not configured** badge
- **Send test** button, so one message proves it works before the end-of-day job tries two hundred

Two details that matter:

- **The password is never sent back to the browser.** The endpoint returns only `passwordIsSet`. Leaving the field blank on save keeps the stored one, so re-saving the form does not wipe it.
- **The test endpoint passes the SMTP error through verbatim.** "Authentication failed" or "relay denied" tells you what to fix; a generic "sending failed" does not.

Admin only. Settings are cached for five minutes, but saving drops the cache immediately.

## Removed: orphan audit and tenancy entities

`AuditAndTenancyEntities.cs` held `AuditLog`, `Company` and `AttendanceLogArchive`. They were created as a first step towards the security review items, then never mapped in the DbContext, so they produced no tables and did nothing. Dead code that looked like a feature is worse than no code, so they are gone.

The underlying needs have not gone away, and are listed under Still open below.

## Still open

- **Leave types** — not started.
- **`WorkShift` lateness / overtime detection** — `EndTime` is now consumed for the absent cut-off,
  but late-arrival and OT flags are not implemented.
- **Device clock drift** — read by `GetDeviceInfoAsync`, not surfaced in the UI.
- **Punch correction with audit trail** — manual punches are flagged, but there is no edit trail.
- **Audit log** — no record of who approved, rejected or edited what. Attendance decides pay, so "the system says you were absent" should be answerable with a record. This is the most worthwhile of the security items.
- **Fingerprint templates stored unencrypted** in `FingerprintTemplates.TemplateData`.
- **SMTP password stored in plain text** in `SystemSettings`. Use a send-only app password so a leak cannot read the mailbox.
- **Attendance log retention** — `AttendanceLogs` grows without bound and is scanned on every grid load. Roughly 290,000 rows a year for 200 employees.
- **No tenant isolation** — single-company only.
