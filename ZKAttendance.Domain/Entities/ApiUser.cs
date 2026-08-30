using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZKAttendance.Domain.Entities
{
    /// <summary>
    /// An account that can call the API.
    ///
    /// Deliberately minimal and separate from Employee: not every employee has
    /// a login, and not every login belongs to an employee (an integration
    /// account, for example). EmployeeId links them when both exist.
    /// </summary>
    [Table("ApiUsers")]
    public class ApiUser
    {
        [Key]
        public int ApiUserId { get; set; }

        [Required, StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required, StringLength(150)]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// PBKDF2 hash. The plain password is never stored, and never logged.
        /// </summary>
        [Required, StringLength(200)]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>
        /// Per-user random salt. Without it, two people choosing the same
        /// password would produce the same hash, and one leaked hash would
        /// expose every account sharing that password.
        /// </summary>
        [Required, StringLength(100)]
        public string PasswordSalt { get; set; } = string.Empty;

        /// <summary>"Admin", "HR" or "Employee".</summary>
        [Required, StringLength(20)]
        public string Role { get; set; } = "Employee";

        /// <summary>Optional link to the employee this account belongs to.</summary>
        public int? EmployeeId { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? LastLoginDate { get; set; }

        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    }

    /// <summary>
    /// A long-lived token used to obtain a new access token without asking for
    /// the password again.
    ///
    /// Stored server-side on purpose. A JWT cannot be revoked once issued -
    /// it is valid until it expires. Keeping refresh tokens in a table means a
    /// stolen session can actually be cut off, which is what /revoke does.
    /// </summary>
    [Table("RefreshTokens")]
    public class RefreshToken
    {
        [Key]
        public int RefreshTokenId { get; set; }

        [Required]
        public int ApiUserId { get; set; }

        [Required, StringLength(200)]
        public string Token { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        /// <summary>Set when revoked, either by /revoke or by being rotated.</summary>
        public DateTime? RevokedAt { get; set; }

        [NotMapped]
        public bool IsActive => RevokedAt is null && DateTime.Now < ExpiresAt;

        [ForeignKey(nameof(ApiUserId))]
        public ApiUser? User { get; set; }
    }
}
