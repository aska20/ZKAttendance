# Running ZKAttendance — full guide

---

# PART 1 — Getting it running

## Step 0: Delete your old folder first

This matters more than it sounds. Your earlier screenshot showed Arabic text
that no longer exists in this code — you were editing an old copy. The zip
extracts to `ZKAttendance/`; your original was `ZKAttendanceWeb/`. Two
different folders, and Visual Studio reopens whatever you last had open.

Extract the zip somewhere **fresh**, e.g. `C:\Projects\ZKAttendance`, and open
`ZKAttendance.sln` from there. Do not extract on top of the old folder —
Windows merges instead of replacing, and stale files survive.

## Step 1: Install what you need

| Tool | Version | Verify |
|---|---|---|
| .NET SDK | 8.0+ | `dotnet --version` |
| SQL Server | Express is fine | open SSMS |
| EF Core CLI | 9.x | `dotnet ef --version` |

```bash
dotnet tool install --global dotnet-ef
```

## Step 2: Build

```bash
cd C:\Projects\ZKAttendance
dotnet restore
dotnet build
```

**Expect errors.** This code has never been compiled — there was no .NET SDK
on the machine it was written on. What you will see is almost entirely missing
`using` lines from the restructure into four projects:

| Error | Meaning | Fix |
|---|---|---|
| `CS0246` type not found | missing `using` | Ctrl+. and accept the suggestion |
| `CS0105` duplicate using | harmless | delete the line or ignore |
| `CS0234` namespace missing | wrong `@using` in a `.cshtml` | fix that view |

Fix them top-down. One `using` often clears a dozen errors. Paste the list if
you get stuck.

## Step 3: Point at your database

Open `ZKAttendance.Web/appsettings.json` and change **one thing**:

```
"Server=DESKTOP-NAAT855"   →   "Server=YOUR-PC-NAME"
```

Find your name: run `hostname`, or copy the Server name from SSMS's connect
dialog. Full details in Part 2.

## Step 4: Create the database

```
dotnet ef migrations add MasterDeviceAndTemplates ^
  --project ZKAttendance.Infrastructure ^
  --startup-project ZKAttendance.Web

dotnet ef database update ^
  --project ZKAttendance.Infrastructure ^
  --startup-project ZKAttendance.Web
```

Both flags are **required**. The DbContext lives in Infrastructure but the app
starts from Web. Omit them and you get *"Unable to create an object of type
AttendanceDbContext"* — the single most common error here.

(On Mac/Linux use `\` instead of `^` for line continuation.)

Refresh SSMS — `ZKAttendanceWebDB` appears with all the tables.

## Step 5: Run

```bash
dotnet run --project ZKAttendance.Web
```

Open the URL it prints, usually `https://localhost:7264`. If the browser
blocks the certificate:

```bash
dotnet dev-certs https --trust
```

## Step 6: Watch it work with no hardware

About 20 seconds after startup the console shows:

```
Device monitor started - checking every 1 minute(s)
Attendance sync service started - every 5 minute(s)
FakeDeviceReader produced 214 punches for 192.168.1.201
Sync round finished: 3/3 devices, 214 new punches
```

**The attendance screen will still look empty. That is correct.**

Punches arrive with `EmployeeId = null` because no biometric ID is mapped to
an employee yet. They are *stored*, not discarded — throwing away a real punch
because HR has not done the paperwork would lose attendance permanently.

To see them resolve:

1. **Employees → Unregistered Biometric IDs**
2. Add one as an employee
3. Restart, or wait for the next sync round

---

# PART 2 — appsettings.json, explained line by line

This file is how you change behaviour **without touching code**. It is read at
startup and pushed into `IConfiguration`.

## First, the honest part: what is actually wired up

I added several config blocks. Not all of them are read by code yet. Here is
the truth, verified by grepping the source:

| Setting | Read by code? | Where |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | **YES** | `Program.cs:34` |
| `SyncConfiguration:UseFakeDeviceReader` | **YES** | `Program.cs:118` |
| `SyncConfiguration:EnableAutoSync` | **YES** | `AttendanceSyncBackgroundService` |
| `SyncConfiguration:SyncIntervalMinutes` | **YES** | `AttendanceSyncBackgroundService` |
| `NepaliCalendarSettings:WeeklyOffDay` | **YES** | `Program.cs:100` |
| `Logging` | **YES** | ASP.NET Core built-in |
| `OrganizationSettings` | no | for report headers later |
| `BranchConfiguration` | no | seed data |
| `DeviceConfiguration` | no | seed data — real devices live in the DB |
| `DeviceMonitorConfiguration` | no | values still hard-coded in the service |
| `MinimumPunchIntervalSeconds` | no | hard-coded as 60 in the service |
| `ClockDriftToleranceSeconds` | no | hard-coded as 60 |
| `OverlapWindowDays` | no | hard-coded as 2 |

The "no" rows are documented intent, not live switches. Changing them today
does nothing. I am telling you because discovering it during your viva would
be worse. Wiring one up is a two-line change — ask and I will show you.

---

## Block 1: ConnectionStrings — the only one you MUST edit

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=DESKTOP-NAAT855;Database=ZKAttendanceWebDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
}
```

Five parts separated by semicolons:

### `Server=DESKTOP-NAAT855`

Which SQL Server to talk to. **Change this.**

| Your setup | Value |
|---|---|
| Default instance on this PC | `Server=DESKTOP-ABC;` |
| SQL Express | `Server=DESKTOP-ABC\\SQLEXPRESS;` |
| LocalDB | `Server=(localdb)\\MSSQLLocalDB;` |
| Shorthand for local | `Server=.;` |

**Double backslash.** JSON treats `\` as an escape character. `\SQLEXPRESS`
with one backslash is a JSON syntax error and the app will not start.

### `Database=ZKAttendanceWebDB`

The database name. It does not exist yet — `dotnet ef database update` creates
it. Change only if you want a different name.

### `Trusted_Connection=True`

Log in as your Windows account. No password stored in the file. If you use SQL
authentication instead:

```
Server=...;Database=...;User Id=sa;Password=YourPass;TrustServerCertificate=True
```

Remove `Trusted_Connection=True` when you do — the two conflict.

### `TrustServerCertificate=True`

SQL Server encrypts connections using a self-signed certificate locally. EF
Core 9 rejects untrusted certificates by default, so without this you get
*"certificate chain was issued by an authority that is not trusted"*. Fine
locally; on a real server, install a proper certificate instead.

### `MultipleActiveResultSets=true`

Lets one connection run a second query while the first is still streaming. EF
Core needs it when you iterate a query and lazy-load inside the loop. Without
it: *"There is already an open DataReader associated with this Command"*.

---

## Block 2: NepaliCalendarSettings

```json
"NepaliCalendarSettings": {
  "UseNepaliDates": true,
  "DateFormat": "yyyy-MM-dd",
  "ShowGregorianAlongside": true,
  "WeeklyOffDay": "Saturday",
  "FiscalYearStartMonth": 4
}
```

### `WeeklyOffDay` — the live one

`Program.cs` reads it:

```csharp
var weeklyOff = Enum.Parse<DayOfWeek>(
    builder.Configuration["NepaliCalendarSettings:WeeklyOffDay"] ?? "Saturday");
builder.Services.AddSingleton<INepaliDateService>(_ => new NepaliDateService(weeklyOff));
```

Saturday, because Nepal's week off is Saturday and Sunday is a working day.
Must be a valid `DayOfWeek` name — a typo throws at startup.

### `FiscalYearStartMonth: 4`

4 is Shrawan. Nepal's fiscal year runs Shrawan 1 to the last day of Ashadh,
not January to December. `FiscalYearRange(2083)` uses this for annual leave and
yearly reports.

### `DateFormat`

Displays as `2083-05-11`, the government format.

### Important: dates are stored in AD, displayed in BS

Every `DateTime` column holds the Gregorian value exactly as the device sent
it. Conversion happens only when a date reaches a screen or a report. Sorting
and date ranges are simple in AD and painful in BS; converting on input would
force every query to convert back.

### If the calendar table is wrong, the app will not start

`NepaliDateConverter.SelfTest()` runs inside the `NepaliDateService`
constructor and checks three published conversions in both directions. That is
deliberate — a startup crash beats months of quietly wrong monthly reports.

Supported range is **2080–2086 BS**. Adding a year is one line in
`NepaliCalendarData.MonthDays`.

---

## Block 3: SyncConfiguration — the one you will actually toggle

```json
"SyncConfiguration": {
  "EnableAutoSync": true,
  "SyncIntervalMinutes": 5,
  "UseFakeDeviceReader": true
}
```

### `UseFakeDeviceReader` — the most important switch in the file

`true` → `FakeDeviceReader` invents realistic punches: skips Saturdays, varies
arrival between 08:50 and 09:25, adds lunch breaks, occasionally misses a
check-out. The entire pipeline runs with no hardware, which is how you demo
this project.

`false` → the real SDK. Requires `zkemkeeper.dll` registered and an x86 build.
Until you write `ZkemkeeperDeviceClient`, setting false throws
`NotImplementedException` **with a message telling you exactly that** — the
`throw` in `Program.cs` is intentional, not a bug.

### `EnableAutoSync: false`

Stops background syncing entirely. Useful while debugging so log noise does
not drown your breakpoints.

### `SyncIntervalMinutes: 5`

How often to poll every device. Lower means fresher data and more load. Five
minutes is a reasonable default; one minute is fine for a demo.

---

## Block 4: DeviceConfiguration — read this carefully

```json
"DeviceConfiguration": {
  "_comment": "Seed data only. Devices are managed in the portal...",
  "Devices": [
    { "DeviceName": "Head Office - Main Gate", "DeviceIP": "192.168.1.201", "DevicePort": 4370 }
  ]
}
```

**Editing this does not add a device to the system.** Devices live in the
`Devices` **table**, and `AttendanceSyncService` loops over that table, not
this list. This block is documentation and seed reference.

To add a real device: **Devices → Add** in the portal. That is what the sync
job reads.

Ports are all 4370, the ZKTeco default.

---

## Block 5: Logging

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "Microsoft.EntityFrameworkCore": "Warning"
  }
}
```

`Microsoft.EntityFrameworkCore` is set to `Warning` on purpose. At
`Information`, EF prints **every SQL statement it generates** — with a sync job
running every 5 minutes, your own log messages vanish in the flood.

Set it to `Information` temporarily when you want to see the actual SQL. Very
useful for debugging a query; unbearable otherwise.

---

## Block 6: OrganizationSettings and BranchConfiguration

Not read by code yet. `OrganizationSettings` is intended for report headers,
`BranchConfiguration` for seeding the first branch. Harmless to leave.

---

## The `_comment_` keys

```json
"OverlapWindowDays": 2,
"_comment_OverlapWindowDays": "Deliberately re-read this many days..."
```

JSON has no comment syntax. `_comment_` keys are a convention: .NET's
configuration binder ignores keys it cannot map, so they are safe. They sit
next to the setting they explain, which beats a separate document nobody opens.

---

## appsettings.Development.json

Also in the project. When `ASPNETCORE_ENVIRONMENT=Development` — the default in
Visual Studio — it **overrides** matching keys in `appsettings.json`. Same key
in both, Development wins.

Practical use: put your local machine's connection string there and leave
`appsettings.json` generic. Handy for team projects, since your teammates do
not inherit your PC name.

---

# PART 3 — Common errors

| Error | Cause | Fix |
|---|---|---|
| `Unable to create an object of type 'AttendanceDbContext'` | missing `--startup-project` | use both flags, Step 4 |
| `A network-related or instance-specific error` | wrong `Server=` | check `hostname`, confirm in SSMS |
| `certificate chain ... not trusted` | missing setting | add `TrustServerCertificate=True` |
| `Login failed for user` | SQL auth vs Windows auth mismatch | see Block 1 |
| JSON parse error at startup | single `\` in `\SQLEXPRESS` | use `\\` |
| Attendance screen empty | no filter chosen | pick a date range — it will not load everything by default |
| Empty even with a filter | punches unmapped | Step 6 |
| `NotImplementedException` about zkemkeeper | `UseFakeDeviceReader: false` with no adapter | set it back to `true` |
| `Nepali calendar table is wrong` | bad month lengths | fix `NepaliCalendarData.cs` |
| `BadImageFormatException` | 64-bit build, 32-bit COM | set `PlatformTarget` to x86 |

---

# PART 4 — Project layout

```
ZKAttendance.sln
├── ZKAttendance.Domain/          entities, enums — ZERO packages
├── ZKAttendance.Application/     interfaces + logic — no EF, no SQL
├── ZKAttendance.Infrastructure/  DbContext, repositories, device clients,
│                                 Nepali calendar, background jobs, migrations
└── ZKAttendance.Web/             controllers, views, Program.cs, appsettings
```

Dependencies point inward only. Domain having no NuGet packages is the test:
if it needed one, something belongs in an outer layer.

`Program.cs` is the composition root — the one place interfaces are wired to
implementations. Add a service, register it there.
