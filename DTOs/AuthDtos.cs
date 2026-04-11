using System.ComponentModel.DataAnnotations;

namespace ExpenseManagerAPI.DTOs;

// -------------------------------------------------------------------------
// POST /api/auth/register — spec FE mới (email + password + confirmPassword)
// -------------------------------------------------------------------------
public class RegisterRequestDto
{
    [Required(ErrorMessage = "Vui lòng nhập email")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận lại mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

// -------------------------------------------------------------------------
// Result từ RegisterAsync
// -------------------------------------------------------------------------
public class RegisterResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    /// <summary>Field-level validation errors — null khi Success = true</summary>
    public Dictionary<string, string[]>? Errors { get; set; }
    public bool IsEmailDuplicate { get; set; }
}

public class LoginDto
{
    [Required] public string Email { get; set; } = string.Empty;
    [Required] public string MatKhau { get; set; } = string.Empty;
}

public class ForgotPasswordDto
{
    [Required(ErrorMessage = "Vui lòng nhập email")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordDto
{
    [Required(ErrorMessage = "Token không được để trống")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ForgotPasswordResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class ResetPasswordResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class ChangePasswordDto
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận lại mật khẩu")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public class ChangePasswordResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}

public class UserProfileDto
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NgayTao { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public long IdNguoiDung { get; set; }
    public string Email { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
}

public class LoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResultDto
{
    public bool Success { get; set; }
    public bool IsLocked { get; set; }
    public int? LockedSeconds { get; set; }
    public string? Message { get; set; }
    public string? AccessToken { get; set; }
    public long? UserId { get; set; }
    public string? UserEmail { get; set; }
}
