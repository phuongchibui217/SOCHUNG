using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ExpenseManagerAPI.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IAuthService _authService;
    private readonly ITokenBlacklistService _blacklist;

    public AuthController(IConfiguration config, IAuthService authService, ITokenBlacklistService blacklist)
    {
        _config = config;
        _authService = authService;
        _blacklist = blacklist;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
    {
        // 1. ModelState: Required + EmailAddress format
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    k => char.ToLower(k.Key[0]) + k.Key[1..],
                    v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            return BadRequest(new { message = "Dữ liệu không hợp lệ", errors });
        }

        // 2. Business logic (password strength, confirmPassword, email duplicate)
        var result = await _authService.RegisterAsync(dto);

        if (!result.Success)
        {
            if (result.IsEmailDuplicate)
                return Conflict(new { message = result.Message });

            return BadRequest(new { message = result.Message, errors = result.Errors });
        }

        return Ok(new { message = result.Message });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        var result = await _authService.LoginAsync(dto);

        if (result.IsLocked)
            return StatusCode(429, new
            {
                message = result.Message,
                data = new { lockedSeconds = result.LockedSeconds }
            });

        if (!result.Success)
            return Unauthorized(new { message = result.Message });

        return Ok(new
        {
            message = result.Message,
            data = new
            {
                accessToken = result.AccessToken,
                user = new { id = result.UserId, email = result.UserEmail }
            }
        });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    k => char.ToLower(k.Key[0]) + k.Key[1..],
                    v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            return BadRequest(new { message = "Dữ liệu không hợp lệ", errors });
        }

        try
        {
            var result = await _authService.ForgotPasswordAsync(dto);
            // Luôn trả 200 dù email có tồn tại hay không — tránh leak thông tin
            return Ok(new { message = "Đã gửi liên kết nếu email tồn tại" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ForgotPassword] ERROR email={dto.Email}: {ex.Message}\n{ex.StackTrace}");
            return StatusCode(500, new { message = "Không thể gửi email. Vui lòng thử lại sau." });
        }
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    k => char.ToLower(k.Key[0]) + k.Key[1..],
                    v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            return BadRequest(new { message = "Dữ liệu không hợp lệ", errors });
        }

        var result = await _authService.ResetPasswordAsync(dto);
        if (!result.Success)
            return BadRequest(new { message = result.Message });

        return Ok(new { message = result.Message });
    }

    [Authorize]
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { message = "Token không hợp lệ" });

        var result = await _authService.GetProfileAsync(userId);
        if (result == null)
            return NotFound(new { message = "Người dùng không tồn tại" });

        return Ok(new { message = "Lấy thông tin thành công", data = result });
    }

    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var expClaim = User.FindFirstValue(JwtRegisteredClaimNames.Exp);

        if (jti != null && long.TryParse(expClaim, out var expUnix))
        {
            var expiry = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
            _blacklist.Revoke(jti, expiry);
        }

        return Ok(new { message = "Đăng xuất thành công" });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    k => char.ToLower(k.Key[0]) + k.Key[1..],
                    v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            return BadRequest(new { message = "Dữ liệu không hợp lệ", errors });
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { message = "Token không hợp lệ" });

        var result = await _authService.ChangePasswordAsync(userId, dto);

        if (!result.Success)
        {
            if (result.Errors != null)
                return BadRequest(new { message = result.Message, errors = result.Errors });
            return BadRequest(new { message = result.Message });
        }

        return Ok(new { message = result.Message });
    }
}
