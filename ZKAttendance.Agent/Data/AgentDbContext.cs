using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Agent.Data
{
    /// <summary>Where an outbox row is in its life.</summary>
    public enum OutboxStatus
    {
        Pending = 0,
        Sent = 1,
        /// <summary>Central rejected it in a way that will never succeed.</summary>
        Dead = 2
    }

    /// <summary>
    /// One punch waiting to go to App1.
    ///
    /// THE WHOLE POINT
    /// ---------------
    /// The agent never posts straight from memory. It reads the device, commits
    /// rows here, and a separate worker drains them. If the internet is down
    /// mid-send the punches are already on disk, so nothing is lost. The link
    /// can be out for days and they flush when it returns.
    ///
    /// Without this, a dropped connection means nobody knows Ram came to work
    /// on Tuesday.
    /// </summary>
    [Table("OutboxPunches")]
    public class OutboxPunch
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long OutboxId { get; set; }

        [Required, StringLength(12)]
        public string BiometricUserId { get; set; } = string.Empty;

        /// <summary>App1's device id, not a local one. The agent is not the source of truth.</summary>
        public int DeviceId { get; set; }

        public DateTime PunchTime { get; set; }

        public int VerifyMode { get; set; }
        public int InOutMode { get; set; }
        public int WorkCode { get; set; }

        public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
        public int Attempts { get; set; }

        /// <summary>When to try again. Null means straight away.</summary>
        public DateTime? NextAttemptAt { get; set; }

        [StringLength(500)]
        public string? LastError { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? SentAt { get; set; }

        /// <summary>Which sync run produced this row, for the UI.</summary>
        public long? SyncRunId { get; set; }
    }

    /// <summary>One press of the Sync button, and what came of it.</summary>
    [Table("SyncRuns")]
    public class SyncRun
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long SyncRunId { get; set; }

        public int DeviceId { get; set; }

        [StringLength(100)]
        public string DeviceName { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; } = DateTime.Now;
        public DateTime? FinishedAt { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Running";   // Running / Success / Failed

        public int RecordsRead { get; set; }
        public int RecordsQueued { get; set; }

        [StringLength(1000)]
        public string? Message { get; set; }

        [StringLength(100)]
        public string? TriggeredBy { get; set; }
    }

    /// <summary>
    /// The agent's own small database. Completely separate from App1's.
    ///
    /// It holds only what the agent needs to survive a dropped connection: the
    /// outbox and a history of sync runs. No employees, no departments, no
    /// attendance rules. App1 stays the source of truth for all of that, which
    /// is what stops the two from ever disagreeing.
    /// </summary>
    public class AgentDbContext : DbContext
    {
        public AgentDbContext(DbContextOptions<AgentDbContext> options) : base(options) { }

        public DbSet<OutboxPunch> OutboxPunches => Set<OutboxPunch>();
        public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<OutboxPunch>(e =>
            {
                // The drain worker asks "what is pending and due" every few
                // seconds. Without a filtered index that query walks every row
                // ever sent, which after a month is most of the table.
                e.HasIndex(x => new { x.Status, x.NextAttemptAt })
                 .HasDatabaseName("IX_Outbox_Pending");

                // Stops the same punch being queued twice if a sync is run
                // twice over the same window. App1 would reject the duplicate
                // anyway, but there is no reason to carry it over the wire.
                e.HasIndex(x => new { x.BiometricUserId, x.PunchTime, x.DeviceId })
                 .IsUnique()
                 .HasDatabaseName("UX_Outbox_Punch");

                e.Property(x => x.Status).HasConversion<int>();
            });

            b.Entity<SyncRun>(e =>
            {
                e.HasIndex(x => x.StartedAt).HasDatabaseName("IX_SyncRun_Started");
            });
        }
    }
}
