using Microsoft.Extensions.Caching.Memory;

namespace ExpenseManagerAPI.Services;

/// <summary>
/// JWT blacklist dùng IMemoryCache.
/// Mỗi jti được cache cho đến khi token hết hạn — sau đó tự xóa vì token đã vô hiệu.
/// Giới hạn: in-process only, không share giữa nhiều instance (scale-out cần Redis).
/// </summary>
public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly IMemoryCache _cache;

    public TokenBlacklistService(IMemoryCache cache) => _cache = cache;

    public void Revoke(string jti, DateTime tokenExpiry)
    {
        var ttl = tokenExpiry - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero) return; // token đã hết hạn, không cần blacklist

        _cache.Set(CacheKey(jti), true, ttl);
    }

    public bool IsRevoked(string jti) => _cache.TryGetValue(CacheKey(jti), out _);

    private static string CacheKey(string jti) => $"blacklist:{jti}";
}
