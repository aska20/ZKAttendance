using System.Security.Cryptography;
using System.Text;

namespace ZKAttendance.Infrastructure.Security
{
    /// <summary>
    /// Cryptographic helper for password hashing using PBKDF2 with SHA-256 and unique per-user salts.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;
        private const int Iterations = 100_000;

        /// <summary>
        /// Hashes a plaintext password and generates a cryptographic random salt.
        /// </summary>
        public static (string Hash, string Salt) HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSizeBytes);

            return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
        }

        /// <summary>
        /// Verifies a plaintext password against a stored PBKDF2 hash and salt using fixed-time comparison.
        /// </summary>
        public static bool VerifyPassword(string password, string storedHash, string storedSalt)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
                return false;

            try
            {
                var salt = Convert.FromBase64String(storedSalt);
                var attempt = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    Iterations,
                    HashAlgorithmName.SHA256,
                    HashSizeBytes);

                return CryptographicOperations.FixedTimeEquals(
                    attempt,
                    Convert.FromBase64String(storedHash));
            }
            catch
            {
                return false;
            }
        }
    }
}
