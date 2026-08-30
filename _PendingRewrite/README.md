# Files awaiting rewrite — NOT part of the build

These seven files implement the **master-device enrolment feature** (pull
enrolments from the master, propagate fingerprint templates to slaves).

## Why they are here

They were written against a parallel entity model that no longer exists.
When the duplicate entities were deleted during the Clean Architecture
restructure, these files were orphaned. They reference:

* `EmployeeDeviceMapping` — the real entity is `EmployeeDevice`
* `DeviceSyncLog` — the real entity is `SyncLog`

plus 21 property-name mismatches, for example:

| Used here | Real property |
|---|---|
| `Device.DeviceIp` | `Device.DeviceIP` |
| `Device.LastConnectionUtc` | `Device.LastConnectionTime` |
| `Employee.FullName` | `Employee.EmployeeName` |
| `AttendanceLog.PunchTimeAd` | `AttendanceLog.AttendanceTime` |
| `AttendanceLog.DeviceUserId` | `AttendanceLog.BiometricUserId` |
| `EmployeeDevice.MappingId` | `EmployeeDevice.EmployeeDeviceId` |

Left in the build, they produce several hundred compile errors.

## What still works without them

The multi-device **attendance** path is complete and compiles:

* `Application/Abstractions/IZkDeviceReader.cs` — the port
* `Infrastructure/Devices/FakeDeviceReader.cs` — runs with no hardware
* `Infrastructure/Services/Devices/AttendanceSyncService.cs` — polls every
  device, de-duplicates, resolves biometric IDs through `EmployeeDevices`
* `Infrastructure/Devices/AttendanceSyncBackgroundService.cs` — the timer

What is missing is only the *enrolment* half: pulling new users off the master
and pushing templates out to the slaves.

## To bring them back

1. Rename `EmployeeDeviceMapping` → `EmployeeDevice`, `DeviceSyncLog` → `SyncLog`.
2. Fix the property names per the table above.
3. Add to `EmployeeDevice`: `TemplateSyncedDate`, `LastSyncError`.
   Add to `Device`: `LastAttendancePullDate`.
   Add to `AttendanceLog`: `PunchDateBs`, `SourceType`.
4. Implement the repository ports in `IRepositories.cs` against EF Core.
5. Move the files back and register the handlers in `Program.cs`.
