namespace ZKAttendance.Domain.Enums;

public enum DeviceRole
{
    /// <summary>
    /// Verifies people and records punches. Never enrolled on directly -
    /// its users arrive by being pushed from the application.
    /// </summary>
    Slave = 0,

    /// <summary>
    /// The single enrolment point. Fingerprints are captured here and pulled
    /// into the database, then propagated outward. Also records attendance
    /// like any other device.
    /// </summary>
    Master = 1
}

public enum EnrollmentStatus
{
    /// <summary>Found on the master, waiting for HR to fill in the details.</summary>
    AwaitingReview = 0,

    /// <summary>Turned into an Employee and queued for propagation.</summary>
    Approved = 1,

    /// <summary>A test enrolment or a mistake. Left on record so it is not re-discovered every pull.</summary>
    Rejected = 2
}

/// <summary>
/// The InOutMode value a ZKTeco device reports on each punch.
/// Devices are inconsistent about this - many models report 0 for everything
/// regardless of which key was pressed, which is why the attendance
/// calculation uses first-punch / last-punch rather than trusting these.
/// </summary>
public enum PunchDirection
{
    CheckIn = 0,
    CheckOut = 1,
    BreakOut = 2,
    BreakIn = 3,
    OvertimeIn = 4,
    OvertimeOut = 5,
    Unknown = 99
}

public enum VerifyMode
{
    Password = 0,
    Fingerprint = 1,
    Card = 2,
    Face = 15,
    Other = 99
}
