# ZKAttendance — Nepal Edition

Attendance system that collects fingerprint punches from **several ZKTeco
devices**, merges them per employee, and presents everything in the **Bikram
Sambat** calendar.

The backend is a headless **ASP.NET Core 8 Web API** (`ZKAttendance.Api`).
The frontend is a separate **React SPA** (`client/`, Vite + JavaScript +
Tailwind). They communicate over JSON with JWT bearer auth — there are no
server-rendered pages.

---

## Running it

### Backend — `ZKAttendance.Api`

```bash
dotnet restore
dotnet build
```

Edit the connection string in `ZKAttendance.Api/appsettings.json`, then:

```bash
dotnet ef database update \
  --project ZKAttendance.Infrastructure \
  --startup-project ZKAttendance.Api

cd ZKAttendance.Api
dotnet run
```

The API listens on `http://localhost:5107` (and `https://localhost:7264`).
Swagger UI is at `/swagger` in Development.
The Hangfire dashboard is at `/hangfire`, Admin only.

### Frontend — `client`

```bash
cd client
npm install
cp .env.example .env
npm run dev            # http://localhost:5173
```

See [`client/README.md`](client/README.md) for details. The Vite dev server
proxies `/api` to the backend, so CORS is only a concern once the SPA is
deployed to its own origin (configure `Cors:AllowedOrigins` in the API).

### No biometric device? It still runs.

`appsettings.json` ships with:

```json
"SyncConfiguration": { "DeviceProtocol": "Fake" }
```

`FakeDeviceReader` generates believable punches for eight users across all
configured devices — skipping Saturdays, varying arrival times, adding lunch
breaks, and occasionally missing a check-out. It never invents a punch in the
future, so today stops at the current time rather than pretending everyone has
already gone home.

It also simulates a **user table and fingerprint templates**, so the whole
enrolment flow — create the user on the terminal, start a scan, pull the
template, copy it to the other terminals — can be exercised with no hardware.
A simulated scan completes about six seconds after enrolment starts.

### Pulling from real ZKTeco devices

`ZkTcpDeviceReader` speaks the ZKTeco "standalone" protocol over **TCP port
4370** directly — no `zkemkeeper.dll`, no COM, no x86 build.

```json
"SyncConfiguration": {
  "DeviceProtocol": "Tcp",          // "Fake" (default) or "Tcp"
  "EnableAutoSync": false,          // true = pull every 5 minutes
  "DeviceCommPassword": 0,          // the terminal's Comm Key, if set (0 = none)
  "DeviceTimeoutMs": 5000
}
```

Register each device on the **Devices** screen (IP + port 4370), then hit
**Test** and **Sync** on that row — or `POST /api/Devices/{id}/sync`. Once one
device works, turn on `EnableAutoSync` and the background job picks up all
active devices.

> The reader follows the proven `pyzk` implementation. Reading punches, reading
> users, writing users and starting a remote enrolment all follow layouts that
> are stable across the range; **writing a fingerprint template to a device has
> not been tested against physical hardware**, because that one operation's
> byte framing varies across ZKTeco firmware families. It is marked in the
> source. Propagation degrades to "user created, finger still to be enrolled
> here" rather than failing silently, so the honest partial state is visible.
>
> The host must be on the same LAN as the devices — a cloud host cannot reach
> `192.168.x.x`.

`DeviceProtocol` can differ per environment: keep `"Fake"` in the cloud,
`"Tcp"` on the on-site box.

---

## Upgrading an existing database

Run these **before** `dotnet ef database update`:

1. **`Migrations/BackFill_PerDeviceBiometricId.sql`**
   The migration adds a unique index on `(DeviceId, BiometricUserId)`. If
   existing `EmployeeDevices` rows have an empty biometric ID, every row
   collides and the migration fails halfway.

2. **`Migrations/CheckDuplicateEmails.sql`**
   A unique index now covers `Employees.Email`. If two employees already share
   an address the index will not create, and SQL Server's error names the table
   but not the people. This script names them, and auto-fixes the safe cases
   (trim whitespace, blank to NULL).

Then create the approvals table:

```
Add-Migration AddAttendanceApprovals
Update-Database
```

---

## How it works

### Collecting from several devices

`AttendanceSyncBackgroundService` wakes every 5 minutes (when `EnableAutoSync`
is on) and calls `AttendanceSyncService.SyncAllDevicesAsync()`. For each active
device, in turn:

1. Open a session.
2. Compare the device clock to the server; push server time if drift exceeds
   60 seconds. Done **before** reading, because a slow branch clock makes one
   arrival look like two different times.
3. Read the attendance log.
4. Translate each `BiometricUserId` into an `EmployeeId` using the
   `EmployeeDevices` rows **for that device**.
5. Skip anything already stored, or within 60 seconds of an existing punch by
   the same person.
6. Save, then write the outcome to `SyncLogs`.

Devices are processed one at a time on purpose — the protocol is not
thread-safe and they usually share a LAN. Each device is wrapped in its own
try/catch, so one machine being offline never stops collection from the others.

### Why re-running a sync is safe

Three guards, in increasing order of authority:

- Ask for less — only records newer than the last connection, minus a two-day
  deliberate overlap.
- Check in memory — existing punches loaded into a `HashSet`.
- **Let SQL Server refuse** — the unique index on
  `(BiometricUserId, AttendanceTime, DeviceId)`. This is the real guarantee;
  the first two are optimisations that could have bugs.

### One person, different ID on each device

The device only knows a number. `EmployeeDevices` maps
`(DeviceId, BiometricUserId) → EmployeeId`:

| EmployeeId | DeviceId | BiometricUserId |
|---|---|---|
| 42 | 1 (Head Office) | 1017 |
| 42 | 2 (Branch) | 88 |
| 43 | 1 (Head Office) | 1018 |

Unique index is on `(DeviceId, BiometricUserId)` — on one device an ID means
one person; across devices the same number may belong to different people.

Attendance is then grouped by **`EmployeeId` + date**, deliberately *not* by
device. That single decision is what makes "check in at head office, check out
at the branch" produce one row with no special-case code.

### Enrolling a person on the terminals

Adding an employee used to write only a database row, so their first scan
arrived unattributed. Now one device is the **Master** — the only place a
finger is physically captured — and the rest are **Slaves**:

```
add employee  →  create the user record on every terminal
              →  Register on ZKTeco: the Master opens its scan screen
              →  pull the template, cache it, copy it to the others
```

Templates are cached in `FingerprintTemplates`, which is what turns adding a
third terminal into a background job instead of calling every employee back to
the sensor. `UX_Device_SingleMaster` is a filtered unique index allowing at
most one active Master, because two would mean two competing sources of truth.

Propagation runs on demand — automatically after a scan, or from **Copy to
other terminals** and **Push users** on the Devices page. There is no
background sweep.

### Office hours, lateness and approval

Rules live in `SystemSettings` (category `Attendance`) and are edited on the
**Settings** screen, so changing one needs no redeploy:

| Arrival | Status | Who decides |
|---|---|---|
| within grace (default, to 10:15) | Present | automatic |
| grace to cut-off (10:15–11:00) | Late | counted, flagged |
| after the cut-off (11:00) | **Pending** | admin must approve |

An employee with a `WorkShift` uses that shift's `EndTime`; everyone else falls
back to `OfficeEndTime`.

**A day is only marked Absent once it has closed** — it is in the past, or it
is today and the shift end plus grace has passed. Before that it reads
`Upcoming`. Days before an employee's hire date read `Not Joined`. This is
decided server-side so no screen has to guess.

Decisions live in `AttendanceApprovals`, one row per employee per day, created
only when a decision is actually needed. A decision made by a person is never
overwritten by re-evaluation.

### End-of-day processing

`DailyAttendanceService` is called by **both** the scheduled job and the admin
buttons, so automatic and manual runs cannot disagree.

The report is driven by the **employee list**, with attendance joined onto it,
never the reverse — querying attendance records alone silently omits the people
who never scanned, who are exactly the ones the report exists to surface.
Punches whose enrol number matches nobody are collected into
`UnmappedDeviceIds` and shown, not dropped.

Statuses: **Present**, **Late**, **Partial** (one scan, usually a forgotten
check-out), **Absent**, **Holiday**, **Not joined**.

Hangfire runs it at **17:30, Sunday to Friday** (`"30 17 * * 0-5"` — Saturday is
the weekly off). The job is a thin wrapper that only decides which date; the
date is resolved inside the job, not baked into the schedule.
See [`docs/HANGFIRE-SETUP.md`](docs/HANGFIRE-SETUP.md).

### Email

Each employee receives their own attendance by email — one message per person,
never a combined list. SMTP settings live in `SystemSettings` (category
`Email`) and are edited on the **Settings** screen, so the mail server can
change without a redeploy.

Until it is configured, `IsConfiguredAsync` returns false and the endpoints say
so rather than throwing. Employees with no address are skipped and counted.
Two employees sharing an address are **both** skipped, because sending one
person's times to another is a privacy leak.

### Nepali dates

**Stored in AD, displayed in BS.** Every `DateTime` column keeps the Gregorian
value exactly as the device reported it. Conversion happens only at display
time, because sorting and date arithmetic are simple in AD and awkward in BS.

BS month lengths vary year to year with no formula, so a published lookup table
is used, anchored to **Baisakh 1, 2080 = 14 April 2023**.

> **Range mismatch, worth knowing.** The frontend table
> (`client/src/lib/nepaliCalendar.js`) covers **2070–2100**. The backend
> (`NepaliCalendarData.cs`) covers only **2080–2086**. Anything the API converts
> outside that window is unreliable. Widen the backend table before running
> reports outside it. The frontend's 2096–2100 rows come from the community
> dataset and should be checked against the official patro before payroll
> depends on them.

`NepaliDateConverter.SelfTest()` runs in the `NepaliDateService` constructor and
checks published conversions in both directions — a wrong table crashes the app
at startup instead of producing wrong reports. The frontend conversion has been
round-tripped over ~9,000 consecutive days with zero failures.

**Every displayed date is `dd/mm/yyyy`**, in whichever calendar is selected. ISO
(`yyyy-mm-dd`) appears only on the wire and in export filenames, where it sorts
correctly.

C# helpers:

```csharp
log.AttendanceTime.ToBsString()      // 2083-01-15
log.AttendanceTime.ToBsWithTime()    // 2083-01-15 09:12
log.AttendanceTime.ToBsLongString()  // 15 Baisakh 2083
log.AttendanceTime.ToBsDevanagari()  // १५ बैशाख २०८३
emp.HireDate.ToBsStringOrDash()      // 2079-08-03  or  "-"

var (from, to) = _nepali.MonthRange(2083, 5);       // Bhadra 2083
var (fyFrom, fyTo) = _nepali.FiscalYearRange(2083); // Shrawan 2083 → Ashadh 2084
```

### Attendance notation

The grid uses muster-roll letters rather than icons, so a column can be counted
by eye:

| Mark | Meaning |
|---|---|
| **P** | Present |
| **½** | Half day |
| **L** | Late (monthly report) |
| **PT** | Partial (monthly report) |
| **A** | Absent |
| **H** | Holiday |
| **O** | Weekly off |
| **·** | Not due yet |
| **—** | Before joining |

---

## Project layout

```
ZKAttendance.Domain/          entities + enums, zero dependencies
ZKAttendance.Application/     ports (interfaces), DTOs, pure services
ZKAttendance.Infrastructure/  EF Core, repositories, device protocol, Nepali calendar,
                              JWT token service, background sync + monitor,
                              attendance policy, daily processing, SMTP
ZKAttendance.Api/             composition root — controllers, DI, Swagger, CORS,
                              Hangfire registration and job
client/                       React SPA (Vite + JS + Tailwind)
docs/                         schema.sql, ER diagrams, Hangfire setup
```

See [`ARCHITECTURE.md`](ARCHITECTURE.md) for the layer rules, and
[`docs/README.md`](docs/README.md) for the schema and ER diagrams.

### API surface

`Auth` (login / refresh / revoke / register), `Account` (profile / change
password), `Dashboard`, `Employees`, `Departments`, `Branches`, `WorkShifts`,
`Devices`, `Holidays`, `Overview` (attendance grid + summary), `Attendance`
(raw punches, computed log, my-attendance, manual entry, daily summary),
`Reports`, `System`, `Configuration`, `Notifications`.

Added since the previous revision:

```
GET  /api/Settings/attendance          office hours, grace, approval cut-off
PUT  /api/Settings/attendance
GET  /api/Settings/email               SMTP config (password never returned)
PUT  /api/Settings/email
POST /api/Settings/email/test

GET  /api/Attendance/approvals         late-arrival queue
POST /api/Attendance/approvals/{id}/approve
POST /api/Attendance/approvals/{id}/reject
POST /api/Attendance/approvals/approve-many

GET  /api/Attendance/daily/report      one day, every active employee
GET  /api/Attendance/daily/monthly     cross-tab
POST /api/Attendance/daily/process     manual end-of-day run
POST /api/Attendance/daily/send-emails
GET  /api/Attendance/daily/email-status

GET  /api/Employees/{id}/enrollment/status
POST /api/Employees/{id}/enrollment/push-user
POST /api/Employees/{id}/enrollment/start      opens the scan screen on a terminal
POST /api/Employees/{id}/enrollment/propagate
POST /api/Devices/{id}/provision
```

Swagger lists them all. JWT bearer auth is registered and 15 controllers carry
`[Authorize]`, most with `Roles = Admin` or `Roles = Management`.

---

## Known gaps

- **`Services/Report/ExcelReportService.cs` is still an empty class.** Excel and
  PDF export are done **client-side** instead (SheetJS and jsPDF, already in
  `package.json`) on the Attendance, Summary, Employee and Monthly Report
  screens. The empty class and the unused ClosedXML reference should be
  deleted.
- **Backend BS table covers only 2080–2086** while the frontend covers
  2070–2100. See the note above.
- **Writing fingerprint templates to a device is untested on hardware.**
  Reading them, and everything else in the protocol, follows stable layouts.
- **`SyncConfiguration` has both `UseFakeDeviceReader` and `DeviceProtocol`.**
  `DeviceProtocol` is the one that is read; the boolean is a leftover.
- **No audit log.** Nothing records who approved, rejected or edited what.
  Attendance decides pay, so this is the most worthwhile thing still missing.
- **Fingerprint templates are stored unencrypted** in
  `FingerprintTemplates.TemplateData`.
- **The SMTP password is stored in plain text** in `SystemSettings`. Use a
  send-only app password so a leak cannot read the mailbox.
- **No retention policy on `AttendanceLogs`.** Roughly 290,000 rows a year for
  200 employees, and the grid scans it on every load.
- **Shift rules `LateMinutes`, `BreakMinutes` and overtime are still unused.**
  `StartTime` and `EndTime` now drive lateness and the absent cut-off; the rest
  do not.
- **No leave management entity.**
- **Holidays store Gregorian dates.** Nepali festivals move each year, so
  `IsRecurring` cannot be used for them.
- **No tenant isolation.** Single company only.
