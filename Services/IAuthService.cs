using ExpenseManagerAPI.DTOs;

namespace ExpenseManagerAPI.Services;

public interface IAuthService
{
    Task<LoginResultDto> LoginAsync(LoginRequestDto dto);
    Task<RegisterResultDto> RegisterAsync(RegisterRequestDto dto);
    Task<ForgotPasswordResultDto> ForgotPasswordAsync(ForgotPasswordDto dto);
    Task<ResetPasswordResultDto> ResetPasswordAsync(ResetPasswordDto dto);
    Task<ChangePasswordResultDto> ChangePasswordAsync(long userId, ChangePasswordDto dto);
    Task<UserProfileDto?> GetProfileAsync(long userId);
}
