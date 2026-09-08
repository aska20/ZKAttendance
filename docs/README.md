# ZKAttendance — Database Documentation

| File | Use |
|---|---|
| `schema.sql` | Full SQL Server DDL. Documentation, and for standing up a database by hand. |
| `ERD.mermaid` / `ERD.svg` / `ERD.png` | Full ER diagram, all 20 tables. |
| `ERD-core.mermaid` / `ERD-core.svg` / `ERD-core.png` | The 10 business tables. Use this one in the report. |

EF Core migrations remain the source of truth. `schema.sql` is generated from the same model, so the two agree, but `Update-Database` is what actually builds the database.

To re-render after editing a `.mermaid` file:

```bash
npx @mermaid-js/mermaid-cli -i ERD-core.mermaid -o ERD-core.png -b white -w 2400
```

---

## Table groups

| Group | Tables |
|---|---|
| Organisation | Branches, Departments, Employees, EmployeeBranches, WorkShifts, EmployeeShiftAssignments |
| Devices | Devices, EmployeeDevices, DeviceStatuses, DeviceErrors, SyncLogs |
| Enrolment | FingerprintTemplates, PendingEnrollments |
| Attendance | AttendanceLogs, AttendanceApprovals, Holidays |
| Security | ApiUsers, RefreshTokens |
| System | SystemSettings, Notifications |

---

## Data dictionary

### Employees

| Column | Type | Notes |
|---|---|---|
| EmployeeId | int PK | |
| BiometricUserId | varchar(12) | The enrol number a terminal reports in a punch. Device data, not free text. |
| EmployeeName | nvarchar(100) | |
| HireDate | datetime2 | Cannot be in the future. Days before it read as "Not Joined", not absent. |
| DepartmentId | int FK | `ON DELETE SET NULL` so removing a department does not delete people. |
| DefaultShiftId | int FK | The shift whose `EndTime` decides when a day closes. |
| ApprovalStatus | nvarchar(20) | Approved / Pending / Rejected. New-hire sign-off, separate from daily attendance approval. |

### Devices

| Column | Type | Notes |
|---|---|---|
| DeviceId | int PK | |
| DeviceIP + DevicePort | | Unique together. One terminal, one address. |
| Role | int | 0 Slave, 1 Master. |
| CommPassword | int | The terminal's Comm Key. 0 means none. |
| IsProvisioned | bit | Whether cached templates have been pushed to this terminal. |

`UX_Device_SingleMaster` is a filtered unique index allowing **at most one active Master**. Enforced in the database because two masters would mean two competing sources of truth for fingerprints.

### EmployeeDevices

The enrol-number mapping. Terminals allocate their own numbers, so employee 1017 at head office may be 88 at a branch. Two unique indexes:

- `(EmployeeId, DeviceId)` — a person appears once per device
- `(DeviceId, BiometricUserId)` — one number on a device belongs to one person

Without the second, two people could share a number and their punches would be indistinguishable.

### AttendanceLogs

One row per scan. Somebody who steps out for lunch produces four rows in a day.

`EmployeeId` is **nullable on purpose**. A punch can arrive for an enrol number nobody has claimed yet; discarding it would lose attendance, so `BiometricUserId` is kept and the row is attributed later.

`IX_AttendanceLog_Unique` on `(BiometricUserId, AttendanceTime, DeviceId)` is the idempotency guard. Most ZKTeco models cannot filter by date and hand back their entire log on every pull, so the same punch is re-offered constantly. This index makes a repeated sync harmless.

### AttendanceApprovals

One decision per employee per day.

| Status | Meaning |
|---|---|
| 0 | AutoApproved — arrived within grace |
| 1 | Pending — arrived after the cut-off, waiting on an admin |
| 2 | Approved — an admin accepted the late arrival |
| 3 | Rejected — an admin refused it |

Separate from `AttendanceLogs` because a person scans many times a day but there is only ever one decision about whether that day counts. Hanging it off a punch would mean re-choosing the owning punch every time an earlier scan arrives late from an offline device.

`CK_Approval_Decided` enforces that a row in status 2 or 3 records who decided it and when.

### FingerprintTemplates

Cached templates pulled from the Master. This cache is what makes adding a third or fourth terminal a background job instead of calling every employee back to the sensor.

`TemplateFormatVersion` (9 or 10) travels with the bytes because ZKTeco firmware families use incompatible encodings. A v10 template written to a v9 terminal is accepted and then never matches, so propagation checks the version and refuses rather than failing silently at the door.

### SystemSettings

Key/value configuration. Category `Attendance` holds the office-hours rules edited from the Settings screen:

| Key | Default | Meaning |
|---|---|---|
| OfficeStartTime | 10:00 | Working day start |
| OfficeEndTime | 18:00 | Working day end |
| GraceMinutes | 15 | On time until 10:15 |
| ApprovalRequiredAfterMinutes | 60 | Pending after 11:00 |
| RequireApprovalForLate | false | Whether 10:15 to 11:00 also needs approval |
| CloseGraceMinutes | 30 | Absent recorded from 18:30 |
| HalfDayUnderHours | 4 | Below this, the day is a half day |

Stored in the database rather than `appsettings.json` so a rule change needs no redeploy.

---

## Referential integrity

Delete behaviour is chosen per relationship rather than applied uniformly:

| Parent | Child | Behaviour | Why |
|---|---|---|---|
| Employees | EmployeeBranches, EmployeeDevices, FingerprintTemplates, AttendanceApprovals | CASCADE | Meaningless without the person. |
| Employees | AttendanceLogs | SET NULL | The punch happened. Keep it as an unattributed record. |
| Departments | Employees | SET NULL | Removing a department must not delete staff. |
| Branches | Devices | CASCADE | A terminal belongs to one office. |
| Departments | Departments | NO ACTION | SQL Server rejects a self-referencing cascade. |
| Branches | AttendanceLogs, DeviceStatuses, SyncLogs | NO ACTION | Avoids multiple cascade paths, which SQL Server rejects. |

The `NO ACTION` choices on Branches are not laziness. `AttendanceLogs` already cascades from `Employees`; a second cascade path from `Branches` would make SQL Server refuse to create the constraint.

---

## Notes for the report

Three things worth calling out in a defense:

1. **Nullable `EmployeeId` on `AttendanceLogs`** is a deliberate integrity trade-off — data preservation over strict referential completeness, because an unattributed punch is more useful than a lost one.
2. **`IX_AttendanceLog_Unique`** solves idempotency at the database layer rather than in application code, which is what makes the sync safe to retry.
3. **`UX_Device_SingleMaster`** shows a filtered unique index used to enforce a business rule ("only one master") that would otherwise need application-level locking.
