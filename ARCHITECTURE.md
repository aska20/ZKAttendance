# Clean Architecture layout

```
ZKAttendance.sln
├── ZKAttendance.Domain/           ← innermost, depends on NOTHING
├── ZKAttendance.Application/      ← depends on Domain only
├── ZKAttendance.Infrastructure/   ← depends on Application + Domain
└── ZKAttendance.Web/              ← composition root, startup project
```

Dependencies point strictly inward. Verified by inspection:

| Project | Project references | NuGet |
|---|---|---|
| Domain | *(none)* | *(none)* |
| Application | Domain | Logging.Abstractions only |
| Infrastructure | Application, Domain | EF Core, SQL Server, ClosedXML, QuestPDF |
| Web | Infrastructure, Application, Domain | ASP.NET Core, Swashbuckle |

Domain has **zero** package references. That is the test: if it needs a
NuGet package, something belongs in an outer layer.

---

## What went where, and why

### Domain — 16 files
All 14 entities plus the two new ones (`FingerprintTemplate`,
`PendingEnrollment`) and the `DeviceRole` / `EnrollmentStatus` enums.

Uses `System.ComponentModel.DataAnnotations` for `[Required]`, `[StringLength]`
and `[Table]`. These live in the base framework, **not** in EF Core, so the
project stays package-free. Anything genuinely EF-specific — indexes, filtered
indexes, delete behaviour, column types — is in `AttendanceDbContext`, where it
belongs.

### Application — 34 files
- `Abstractions/` — every interface: the service contracts (`IEmployeeService`,
  `IDeviceService`, …), repository ports, `IZkDeviceClient`, `IUnitOfWork`,
  `IClock`, `INepaliCalendar`.
- `Dtos/` — DTOs and view models that cross layers.
- `Services/` — logic with **no** infrastructure dependency:
  `AttendanceCalculationService` (grouping, hours, status).
- `Enrollment/`, `Provisioning/`, `Attendance/` — the master-device use cases.

### Infrastructure — 50 files
Everything that touches something external:
- `Persistence/` — `AttendanceDbContext`, repositories, migrations.
- `Services/` — the service implementations that query `DbContext` directly.
  These were called "services" in the original project but they are adapters.
- `Devices/` — `FakeZkDeviceClient`, and where `ZkemkeeperDeviceClient` goes.
- `NepaliCalendar/` — the converter and the `INepaliCalendar` adapter.

### Web — 35 files
Controllers, Razor views, `wwwroot`, `Program.cs`. Nothing but delivery.

---

## Two violations the restructure exposed

Both were real, and both are fixed.

**1. `AttendanceCalculationService` called `.ToBsString()`**

An extension method living in Infrastructure. Application would have needed a
`using ZKAttendance.Infrastructure.NepaliCalendar` — an inner layer importing
an outer one, the exact thing the rule forbids.

Now `INepaliCalendar` is injected:

```csharp
private readonly INepaliCalendar _nepali;   // port, defined in Application
...
NepaliDate = _nepali.ToBsString(group.Date),
```

The payoff is concrete: a unit test of the calculation can pass a stub that
returns a fixed string, with no calendar table and no reference to
Infrastructure at all.

**2. `AttendanceService` imported `EntityFrameworkCore`**

It was filed under Application but queried the DbContext. Moved to
Infrastructure, where a class that knows about SQL belongs.

---

## Migrations

Migrations moved to `ZKAttendance.Infrastructure/Migrations`. The context lives
in Infrastructure but the startup project is Web, so:

```bash
dotnet ef migrations add MasterDeviceAndTemplates \
  --project ZKAttendance.Infrastructure \
  --startup-project ZKAttendance.Web

dotnet ef database update \
  --project ZKAttendance.Infrastructure \
  --startup-project ZKAttendance.Web
```

Run `ZKAttendance.Infrastructure/Migrations/BackFill_PerDeviceBiometricId.sql`
**before** `database update` on an existing database — the new unique index
collides on empty values otherwise.

---

## Still to do

- `Program.cs` DI registration still uses the old namespaces; update the
  `using` lines to the new layer names.
- Repository implementations for the ports in `Abstractions/IRepositories.cs`.
- `ZkemkeeperDeviceClient` — the interface and all callers are ready.
- **Nothing here has been compiled.** No .NET SDK was available. Run
  `dotnet build` first and expect namespace fixes in `Program.cs` and views.
