using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    /// <summary>
    /// An in-app notification shown in the bell menu. Targeted either at one
    /// user (<see cref="RecipientUserId"/>) or at everyone with a role
    /// (<see cref="RecipientRole"/>), e.g. "Admin" for approval requests.
    /// </summary>
    [Table("Notifications")]
    public class Notification
    {
        [Key]
        public long NotificationId { get; set; }

        public int? RecipientUserId { get; set; }

        [StringLength(20)]
        public string? RecipientRole { get; set; }

        [Required]
        [StringLength(300)]
        public string Message { get; set; } = string.Empty;

        /// <summary>Client route to open when the notification is clicked, e.g. "/employees/pending".</summary>
        [StringLength(200)]
        public string? LinkPath { get; set; }

        [StringLength(40)]
        public string? Type { get; set; }   // employee-approval-request | employee-approved | employee-rejected

        public bool IsRead { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [ForeignKey(nameof(RecipientUserId))]
        public virtual ApiUser? RecipientUser { get; set; }
    }
}
