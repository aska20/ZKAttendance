using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    /// <summary>
    /// One local agent: a small application on a machine inside a branch LAN
    /// that can actually reach the ZKTeco terminals.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// ZKTeco terminals speak a binary protocol over raw TCP on port 4370 and
    /// have no outbound capability at all. They cannot dial a VPS. So when the
    /// central server moves off the LAN, something on the LAN has to bridge:
    ///
    ///     ZKTeco --TCP 4370--> Local Agent --WSS--> Central Server
    ///
    /// The agent holds the outbound WebSocket, which is what makes this work
    /// through NAT and a firewall with no port forwarding and no static IP at
    /// the branch.
    ///
    /// The central server stays the source of truth. The agent never decides
    /// anything about attendance; it reads punches and forwards them.
    /// </summary>
    [Table("LocalServers")]
    public class LocalServer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int LocalServerId { get; set; }

        [Required, StringLength(100)]
        public string ServerName { get; set; } = string.Empty;

        /// <summary>
        /// The agent's identity on the wire. Issued once when the agent is
        /// registered and sent on every connection.
        /// </summary>
        [Required, StringLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string AgentKey { get; set; } = string.Empty;

        /// <summary>
        /// Hash of the agent secret. Never store the secret itself: a leaked
        /// database would otherwise let anyone impersonate a branch and inject
        /// attendance.
        /// </summary>
        [Required, StringLength(200)]
        public string SecretHash { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string SecretSalt { get; set; } = string.Empty;

        /// <summary>Which branch this agent serves. Its devices come from here.</summary>
        [Required]
        public int BranchId { get; set; }

        public bool IsActive { get; set; } = true;

        // ── live connection state ────────────────────────────────────

        public bool IsConnected { get; set; }
        public DateTime? LastConnectedAt { get; set; }
        public DateTime? LastHeartbeatAt { get; set; }
        public DateTime? LastImportAt { get; set; }

        /// <summary>Public IP the agent last connected from. Diagnostics only.</summary>
        [StringLength(45)]
        public string? LastRemoteIp { get; set; }

        [StringLength(50)]
        public string? AgentVersion { get; set; }

        /// <summary>Total punches this agent has forwarded. Cheap health signal.</summary>
        public long RecordsImported { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? ModifiedDate { get; set; }

        public virtual Branch? Branch { get; set; }
        public virtual ICollection<Device> Devices { get; set; } = new List<Device>();
    }
}
