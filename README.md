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
"SyncConfiguration": { "UseFakeDeviceReader": true }
```

`FakeDeviceReader` generates believable punches for eight users across all
three configured devices — skipping Saturdays, varying arrival times, adding
lunch breaks, and occasionally missing a check-out. The whole pipeline runs
end to end so you can demonstrate the system with no hardware present.

### Pulling from real ZKTeco devices

`ZkTcpDeviceReader` speaks the ZKTeco "standalone" protocol over **TCP port
4370** directly — no `zkemkeeper.dll`, no COM, no x86 build. Point it at your
devices:

```json
"SyncConfiguration": {
  "DeviceProtocol": "Tcp",          // "Fake" (default) or "Tcp"
  "DeviceCommPassword": 0,          // the terminal's Comm Key, if set (0 = none)
  "DeviceTimeoutMs": 5000
}
```

Register each device on the **Devices** screen (IP + port 4370), then hit
**Test** and **Sync** on that row — or `POST /api/Devices/{id}/sync`. Once one
device works the 5-minute background job picks up all active devices.

> The reader follows the proven `pyzk` implementation but has **not** been
> tested against physical hardware in this repo. It parses the common 40-byte
> attendance-record layout; if your model returns another size the sync fails
> with the size in the message so it can be added. The host must be on the same
> LAN as the devices (a cloud host cannot reach `192.168.x.x`).

The machine that runs the sync must therefore be on-prem. `DeviceProtocol`
can differ per environment — keep `"Fake"` in the cloud, `"Tcp"` on the
on-site box.

---

## Upgrading an existing database

**Run `Migrations/BackFill_PerDeviceBiometricId.sql` BEFORE `dotnet ef database update`.**

The migration adds a unique index on `(DeviceId, BiometricUserId)`. If the
existing `EmployeeDevices` rows have an empty biometric ID, every row collides
and the migration fails halfway. The script fills the column first, then gives
you a query that must return zero rows before you proceed.

---

## How it works

### Collecting from several devices

`AttendanceSyncBackgroundService` wakes every 5 minutes and calls
`AttendanceSyncService.SyncAllDevicesAsync()`. For each active device, in turn:

1. Open an SDK session.
2. Compare the device clock to the server; push server time if drift exceeds
   60 seconds. Done **before** reading, because a slow branch clock makes one
   arrival look like two different times.
3. Read the attendance log.
4. Translate each `BiometricUserId` into an `EmployeeId` using the
   `EmployeeDevices` rows **for that device**.
5. Skip anything already stored, or within 60 seconds of an existing punch by
   the same person.
6. Save, then write the outcome to `SyncLogs`.

Devices are processed one at a time on purpose — the SDK is not thread-safe and
they usually share a LAN. Each device is wrapped in its own try/catch, so one
machine being offline never stops collection from the others.

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

### Nepali dates

**Stored in AD, displayed in BS.** Every `DateTime` column keeps the Gregorian
value exactly as the device reported it. Conversion happens only at display
time, because sorting and date arithmetic are simple in AD and awkward in BS.

BS month lengths vary year to year with no formula, so `NepaliCalendarData`
holds a published lookup table anchored to **Baisakh 1, 2080 = 14 April 2023**.
`NepaliDateConverter.SelfTest()` runs in the `NepaliDateService` constructor
and checks three independently published conversions in both directions — a
wrong table crashes the app at startup instead of producing wrong reports.

In views:

```razor
@log.AttendanceTime.ToBsString()      // 2083-01-15
@log.AttendanceTime.ToBsWithTime()    // 2083-01-15 09:12
@log.AttendanceTime.ToBsLongString()  // 15 Baisakh 2083
@log.AttendanceTime.ToBsDevanagari()  // १५ बैशाख २०८३
@emp.HireDate.ToBsStringOrDash()      // 2079-08-03  or  "-"
```

In a controller:

```csharp
var (from, to) = _nepali.MonthRange(2083, 5);      // Bhadra 2083
var (fyFrom, fyTo) = _nepali.FiscalYearRange(2083); // Shrawan 2083 → Ashadh 2084
```

Supported range is **2080–2086 BS**. Adding a year is one line in
`NepaliCalendarData.MonthDays` — copy the 12 month lengths from the official
patro and re-run the self-test.

---

## Project layout

```
ZKAttendance.Domain/          entities + enums, zero dependencies
ZKAttendance.Application/     ports (interfaces), DTOs, pure services
ZKAttendance.Infrastructure/  EF Core, repositories, device SDK, Nepali calendar,
                             JWT token service, background sync + monitor
ZKAttendance.Api/            composition root — controllers, DI, Swagger, CORS
client/                      React SPA (Vite + JS + Tailwind)
```

See [`ARCHITECTURE.md`](ARCHITECTURE.md) for the layer rules.

### API surface

`Auth` (login / refresh / revoke / register), `Account` (profile / change
password), `Dashboard`, `Employees`, `Departments`, `Branches`, `WorkShifts`,
`Devices`, `Attendance` (raw punches, computed log, my-attendance, manual
entry, daily summary), `Reports` (daily / range / daily-summary), `System`
(status / sync / device checks / Nepali-date), `Configuration` (branch +
device config). Swagger lists them all.

Not yet exposed: Excel / PDF report export (was unimplemented in the old code
too).

---

## Known gaps

- `Services/Report/ExcelReportService.cs` is an empty class. ClosedXML is
  referenced but unused.
- No authentication scheme is registered. `UseAuthorization()` is called but no
  controller carries `[Authorize]`.
- Shift rules (`LateMinutes`, `BreakMinutes`, `OvertimeStartMinutes`) are loaded
  but not used in the calculation — thresholds are still hard-coded.
- No leave management entity.
- Holidays store Gregorian dates; Nepali festivals move each year, so
  `IsRecurring` cannot be used for them.
