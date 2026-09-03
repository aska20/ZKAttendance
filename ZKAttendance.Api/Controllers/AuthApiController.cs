using Microsoft.AspNetCore.Mvc;
using ZKAttendance.Application.Abstractions;
using ZKAttendance.Application.Dtos.Api;

namespace ZKAttendance.Api.Controllers
{
    /// <summary>Login and token lifecycle. Accounts are created only from the
    /// Employees screen (POST /api/Employees/{id}/create-login).</summary>
    [Route("api/Auth")]
    [ApiController]
    [Produces("application/json")]
    [Tags("Auth")]
    public class AuthApiController : ControllerBase
    {
        private readonly ITokenService _tokens;
        private readonly ILogger<AuthApiController> _logger;

        public AuthApiController(ITokenService tokens, ILogger<AuthApiController> logger)
        {
            _tokens = tokens;
            _logger = logger;
        }

        /// <summary>Exchange username and password for tokens.</summary>
        /// <remarks>
        /// Copy the accessToken from the response, click Authorize at the top
        /// of this page, and paste it to call the protected endpoints.
        /// </remarks>
        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponse), 200)]
        [ProducesResponseType(typeof(ApiError), 401)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var result = await _tokens.LoginAsync(request);

            if (result is null)
            {
                // One message for both wrong-username and wrong-password.
                // Telling them apart would confirm which usernames exist.
                _logger.LogWarning("Failed login attempt for {Username}", request.Username);
                return Unauthorized(ApiError.From("Invalid username or password"));
            }

            return Ok(result);
        }

        /// <summary>Swap a refresh token for a new access token.</summary>
        /// <remarks>
        /// Access tokens are short-lived. This avoids asking for the password
        /// again. The refresh token is rotated on use: the one you send is
        /// revoked and a new one returned, so a replayed copy fails.
        /// </remarks>
        [HttpPost("refresh")]
        [ProducesResponseType(typeof(AuthResponse), 200)]
        [ProducesResponseType(typeof(ApiError), 401)]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
        {
            var result = await _tokens.RefreshAsync(request.RefreshToken);

            return result is null
                ? Unauthorized(ApiError.From("Refresh token is invalid, expired or already used"))
                : Ok(result);
        }

        /// <summary>Revoke a refresh token — effectively a logout.</summary>
        /// <remarks>
        /// A JWT cannot be un-issued; it stays valid until it expires. Revoking
        /// the refresh token stops new access tokens being minted, which is why
        /// access tokens are kept short-lived.
        /// </remarks>
        [HttpPost("revoke")]
        [ProducesResponseType(200)]
        [ProducesResponseType(typeof(ApiError), 404)]
        public async Task<IActionResult> Revoke([FromBody] RefreshRequest request)
        {
            var ok = await _tokens.RevokeAsync(request.RefreshToken);

            return ok
                ? Ok(new { message = "Token revoked" })
                : NotFound(ApiError.From("Token not found or already revoked"));
        }
    }
}
