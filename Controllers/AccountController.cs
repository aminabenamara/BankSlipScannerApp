using BankSlipScannerApp.DTOs;
using BankSlipScannerApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankSlipScannerApp.Controllers
{
    [ApiController]
    [Route("api/account")]
    public class AccountController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AccountController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, errors = ModelState });

            var result = await _authService.LoginAsync(request);

            if (!result.Success)
                return Unauthorized(new { success = false, message = result.Message });

            return Ok(new
            {
                success = true,
                message = result.Message,
                token = result.Token,
                expiresIn = result.ExpiresIn,
                user = result.User
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, errors = ModelState });

            var result = await _authService.RegisterAsync(request);

            if (!result.Success)
                return Conflict(new { success = false, message = result.Message });

            return StatusCode(201, new { success = true, message = result.Message });
        }

        [HttpGet("me")]
        [Authorize]
        public IActionResult GetMe()
        {
            var userId = User.FindFirst("userId")?.Value;
            var nomComplet = User.FindFirst("nomComplet")?.Value;
            var email = User.FindFirst("email")?.Value;
            var role = User.FindFirst("role")?.Value;

            return Ok(new { success = true, user = new { userId, nomComplet, email, role } });
        }

        [HttpPost("logout")]
        [Authorize]
        public IActionResult Logout()
        {
            return Ok(new { success = true, message = "Déconnecté avec succès." });
        }
    }
}