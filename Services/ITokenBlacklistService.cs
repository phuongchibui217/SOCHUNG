namespace ExpenseManagerAPI.Services;

public interface ITokenBlacklistService
{
    /// <summary>Thêm jti vào blacklist, tự xóa khi token hết hạn.</summary>
    void Revoke(string jti, DateTime tokenExpiry);

    /// <summary>Kiểm tra jti có bị revoke không.</summary>
    bool IsRevoked(string jti);
}
