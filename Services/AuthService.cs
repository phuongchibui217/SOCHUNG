using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Models;
using ExpenseManagerAPI.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace ExpenseManagerAPI.Services;

public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 1;
    private const int ResetTokenExpiryMinutes = 3;

    private readonly IUserRepository _userRepo;
    private readonly IJwtProvider _jwt;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly SoChungDbContext _db;

    public AuthService(IUserRepository userRepo, IJwtProvider jwt, IEmailService emailService, IConfiguration config, SoChungDbContext db)
    {
        _userRepo = userRepo;
        _jwt = jwt;
        _emailService = emailService;
        _config = config;
        _db = db;
    }

    // -------------------------------------------------------------------------
    // Register
    // -------------------------------------------------------------------------
    public async Task<RegisterResultDto> RegisterAsync(RegisterRequestDto dto)
    {
        var errors = new Dictionary<string, string[]>();

        // Validate password strength — tách 2 rule riêng biệt
        if (!string.IsNullOrEmpty(dto.Password))
        {
            var pwdErrors = new List<string>();

            if (dto.Password.Length < 8)
                pwdErrors.Add("Mật khẩu phải có ít nhất 8 ký tự");

            // Phải có chữ, số VÀ ký tự đặc biệt
            bool hasLetter  = dto.Password.Any(char.IsLetter);
            bool hasDigit   = dto.Password.Any(char.IsDigit);
            bool hasSpecial = dto.Password.Any(c => !char.IsLetterOrDigit(c));

            if (!hasLetter || !hasDigit || !hasSpecial)
                pwdErrors.Add("Mật khẩu phải gồm chữ, số và ký tự đặc biệt");

            if (pwdErrors.Count > 0)
                errors["password"] = pwdErrors.ToArray();
        }

        // Validate confirmPassword
        if (!string.IsNullOrEmpty(dto.Password) &&
            !string.IsNullOrEmpty(dto.ConfirmPassword) &&
            dto.Password != dto.ConfirmPassword)
        {
            errors["confirmPassword"] = new[] { "Mật khẩu xác nhận không khớp" };
        }

        if (errors.Count > 0)
            return new RegisterResultDto
            {
                Success = false,
                Message = "Dữ liệu không hợp lệ",
                Errors = errors
            };

        // Kiểm tra email trùng
        var emailNormalized = dto.Email.Trim().ToLower();
        if (await _userRepo.EmailExistsAsync(emailNormalized))
            return new RegisterResultDto
            {
                Success = false,
                IsEmailDuplicate = true,
                Message = "Email đã được sử dụng"
            };

        var user = new NguoiDung
        {
            Email          = emailNormalized,
            MatKhauHash    = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            NgayTao        = DateTime.UtcNow,
            DaXacMinhEmail = true,
            FailedAttempts = 0
        };

        await _userRepo.AddUserAsync(user);
        await _userRepo.SaveChangesAsync();

        return new RegisterResultDto { Success = true, Message = "Đăng ký thành công" };
    }

    // -------------------------------------------------------------------------
    // Login
    // -------------------------------------------------------------------------
    public async Task<LoginResultDto> LoginAsync(LoginRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return Fail("Vui lòng nhập email");

        if (string.IsNullOrWhiteSpace(dto.Password))
            return Fail("Vui lòng nhập mật khẩu");

        var user = await _userRepo.FindByEmailAsync(dto.Email.Trim().ToLower());
        if (user == null)
            return Fail("Email hoặc mật khẩu không đúng, xin vui lòng thử lại.");

        var verify = BCrypt.Net.BCrypt.Verify(dto.Password, user.MatKhauHash);

        if (user.LockUntil.HasValue && user.LockUntil.Value > DateTime.UtcNow)
        {
            var remaining = (int)Math.Ceiling((user.LockUntil.Value - DateTime.UtcNow).TotalSeconds);
            return new LoginResultDto
            {
                Success = false,
                IsLocked = true,
                LockedSeconds = remaining,
                Message = "Bạn đã nhập sai quá nhiều lần. Vui lòng thử lại sau."
            };
        }

        if (!verify)
        {
            user.FailedAttempts++;
            if (user.FailedAttempts >= MaxFailedAttempts)
                user.LockUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);

            await _userRepo.SaveChangesAsync();
            return Fail("Email hoặc mật khẩu không đúng, xin vui lòng thử lại.");
        }

        user.FailedAttempts = 0;
        user.LockUntil = null;
        await _userRepo.SaveChangesAsync();

        return new LoginResultDto
        {
            Success = true,
            Message = "Đăng nhập thành công",
            AccessToken = _jwt.Generate(user),
            UserId = user.IdNguoiDung,
            UserEmail = user.Email
        };
    }
    private static LoginResultDto Fail(string message) =>
        new() { Success = false, Message = message };

    // -------------------------------------------------------------------------
    // Forgot Password
    // -------------------------------------------------------------------------
    public async Task<ForgotPasswordResultDto> ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        const string genericMessage = "Vui lòng kiểm tra email để đặt lại mật khẩu";

        var user = await _userRepo.FindByEmailAsync(dto.Email.Trim().ToLower());
        if (user == null)
            return new ForgotPasswordResultDto { Success = false, Message = "Email không tồn tại trong hệ thống" };

        // Hủy token cũ chưa dùng của user này
        var oldTokens = await _db.TokenNguoiDungs
            .Where(t => t.IdNguoiDung == user.IdNguoiDung
                     && t.LoaiToken == "DAT_LAI_MAT_KHAU"
                     && !t.DaDung)
            .ToListAsync();
        _db.TokenNguoiDungs.RemoveRange(oldTokens);

        // Tạo token mới — 32 bytes hex = 64 ký tự, cryptographically secure
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToHexString(tokenBytes).ToLower();

        _db.TokenNguoiDungs.Add(new TokenNguoiDung
        {
            IdNguoiDung = user.IdNguoiDung,
            MaToken     = token,
            LoaiToken   = "DAT_LAI_MAT_KHAU",
            HetHan      = DateTime.UtcNow.AddMinutes(ResetTokenExpiryMinutes),
            DaDung      = false,
            NgayTao     = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var frontendUrl = _config["App:FrontendUrl"]?.TrimEnd('/') ?? "https://yourapp";
        var resetLink = $"{frontendUrl}/reset-password?token={token}";
        await _emailService.SendPasswordResetEmailAsync(user.Email, resetLink);

        return new ForgotPasswordResultDto { Success = true, Message = genericMessage };
    }

    // -------------------------------------------------------------------------
    // Reset Password
    // -------------------------------------------------------------------------
    public async Task<ResetPasswordResultDto> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var pwdErrors = ValidatePassword(dto.NewPassword);
        if (pwdErrors.Count > 0)
            return new ResetPasswordResultDto { Success = false, Message = pwdErrors[0] };

        if (dto.NewPassword != dto.ConfirmPassword)
            return new ResetPasswordResultDto { Success = false, Message = "Mật khẩu xác nhận không khớp" };

        // Tìm token hợp lệ: đúng loại, chưa dùng, chưa hết hạn
        var tokenRecord = await _db.TokenNguoiDungs
            .Include(t => t.NguoiDung)
            .FirstOrDefaultAsync(t => t.MaToken == dto.Token
                                   && t.LoaiToken == "DAT_LAI_MAT_KHAU"
                                   && !t.DaDung
                                   && t.HetHan > DateTime.UtcNow);

        if (tokenRecord?.NguoiDung == null)
            return new ResetPasswordResultDto { Success = false, Message = "Liên kết không hợp lệ hoặc đã hết hạn" };

        // Hash password mới, đánh dấu token đã dùng (1 lần)
        tokenRecord.NguoiDung.MatKhauHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        tokenRecord.DaDung = true;
        await _db.SaveChangesAsync();

        return new ResetPasswordResultDto { Success = true, Message = "Đặt lại mật khẩu thành công" };
    }

    // -------------------------------------------------------------------------
    // Change Password (đã đăng nhập)
    // -------------------------------------------------------------------------
    public async Task<ChangePasswordResultDto> ChangePasswordAsync(long userId, ChangePasswordDto dto)
    {
        var errors = new Dictionary<string, string[]>();

        // Validate newPassword strength
        if (!string.IsNullOrEmpty(dto.NewPassword))
        {
            var pwdErrors = ValidatePassword(dto.NewPassword);
            if (pwdErrors.Count > 0)
                errors["newPassword"] = pwdErrors.ToArray();
        }

        // Validate confirmNewPassword
        if (!string.IsNullOrEmpty(dto.NewPassword) &&
            !string.IsNullOrEmpty(dto.ConfirmNewPassword) &&
            dto.NewPassword != dto.ConfirmNewPassword)
        {
            errors["confirmNewPassword"] = new[] { "Mật khẩu xác nhận không khớp" };
        }

        if (errors.Count > 0)
            return new ChangePasswordResultDto
            {
                Success = false,
                Message = "Dữ liệu không hợp lệ",
                Errors = errors
            };

        var user = await _userRepo.FindByIdAsync(userId);
        if (user == null)
            return new ChangePasswordResultDto { Success = false, Message = "Người dùng không tồn tại" };

        // Verify mật khẩu hiện tại
        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.MatKhauHash))
            return new ChangePasswordResultDto { Success = false, Message = "Mật khẩu hiện tại không đúng" };

        // Không được trùng mật khẩu cũ
        if (BCrypt.Net.BCrypt.Verify(dto.NewPassword, user.MatKhauHash))
            return new ChangePasswordResultDto { Success = false, Message = "Mật khẩu mới không được trùng với mật khẩu cũ" };

        user.MatKhauHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await _userRepo.SaveChangesAsync();

        return new ChangePasswordResultDto { Success = true, Message = "Thay đổi mật khẩu thành công" };
    }

    public async Task<UserProfileDto?> GetProfileAsync(long userId)
    {
        var user = await _userRepo.FindByIdAsync(userId);
        if (user == null) return null;

        return new UserProfileDto
        {
            Id      = user.IdNguoiDung,
            Email   = user.Email,
            NgayTao = user.NgayTao.ToString("yyyy-MM-dd")
        };
    }

    private static List<string> ValidatePassword(string password)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password))       { errors.Add("Vui lòng nhập mật khẩu mới"); return errors; }
        if (password.Length < 8)                   errors.Add("Mật khẩu phải có ít nhất 8 ký tự");
        if (!password.Any(char.IsLetter) ||
            !password.Any(char.IsDigit)  ||
            !password.Any(c => !char.IsLetterOrDigit(c)))
            errors.Add("Mật khẩu phải gồm chữ, số và ký tự đặc biệt");
        return errors;
    }
}
