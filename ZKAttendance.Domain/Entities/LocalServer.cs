using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    /// <summary>
    /// A registered App2 (local agent) that is allowed to submit punches and
    /// query devices from App1.
    ///
    /// SECURITY NOTE
    /// -------------
    /// AgentKey is public - it identifies the agent in logs and URLs.
    /// SecretHash is a SHA-256 hex digest of the secret. The plaintext secret
    /// is shown exactly once (on registration) and is never stored. Rotation
    /// issues a fresh secret and invalidates the old hash immediately.
    /// </summary>
    [Table("LocalServers")]
    public class LocalServer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int LocalServerId { get; set; }

        /// <summary>Human-readable label, e.g. "Head Office Agent".</summary>
        [Required]
        [MaxLength(100)]
        public string ServerName { get; set; } = string.Empty;

        /// <summary>
        /// The branch whose devices this agent is responsible for reading.
        /// The agent will only see devices belonging to this branch.
        /// </summary>
        [Required]
        public int BranchId { get; set; }

        /// <summary>
        /// Public identifier sent with every request. Starts with "agt_"
        /// so it is recognisable in logs. Safe to log. Unique.
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string AgentKey { get; set; } = string.Empty;

        /// <summary>
        /// SHA-256 hex digest of the secret. The agent sends the plaintext
        /// secret; App1 hashes it and compares. Never expose this column.
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string SecretHash { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        /// <summary>Updated on every heartbeat so the UI can show "last seen".</summary>
        public DateTime? LastHeartbeatAt { get; set; }

        /// <summary>Agent version string, updated on login.</summary>
        [MaxLength(20)]
        public string? AgentVersion { get; set; }

        [Required]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey("BranchId")]
        public virtual Branch? Branch { get; set; }
    }
}
