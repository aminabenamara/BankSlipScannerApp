using BankSlipScannerApp.DTOs;
using BankSlipScannerApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace BankSlipScannerApp.Controllers
{
    [ApiController]
    [Route("api/password")]
    public class PasswordController : ControllerBase
    {
        private readonly IPasswordService _passwordService;

        public PasswordController(IPasswordService passwordService)
        {
            _passwordService = passwordService;
        }

        // POST /api/password/forgot
        [HttpPost("forgot")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, errors = ModelState });

            var result = await _passwordService.ForgotPasswordAsync(request);
            return Ok(new { success = result.Success, message = result.Message });
        }

        // POST /api/password/reset
        [HttpPost("reset")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { success = false, errors = ModelState });

            var result = await _passwordService.ResetPasswordAsync(request);

            if (!result.Success)
                return BadRequest(new { success = false, message = result.Message });

            return Ok(new { success = true, message = result.Message });
        }

        //  voir le token
        [HttpGet("test-token/{email}")]
        public IActionResult GetTestToken(string email)
        {
            if (PasswordService._resetTokens.TryGetValue(email.ToLower(), out var info))
                return Ok(new { token = info.Token });

            return NotFound(new { message = "Faites d'abord forgot password !" });
        }
    }
}