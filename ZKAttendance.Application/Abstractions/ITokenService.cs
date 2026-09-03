using ZKAttendance.Application.Dtos.Api;

namespace ZKAttendance.Application.Abstractions
{
    /// <summary>
    /// Issues and validates API tokens.
    ///
    /// The port lives here; the JWT implementation lives in Infrastructure,
    /// because signing keys and the JWT library are infrastructure concerns.
    /// </summary>
    public interface ITokenService
    {
        Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
        Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);

        /// <summary>Revoke a refresh token so it can no longer be exchanged.</summary>
        Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default);
    }
}
