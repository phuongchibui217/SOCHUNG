namespace ExpenseManagerAPI.Helpers;

public static class TimeZoneHelper
{
    /// <summary>
    /// Trả về TimeZoneInfo cho giờ Việt Nam (UTC+7).
    /// Tương thích cả Windows ("SE Asia Standard Time") và Linux ("Asia/Ho_Chi_Minh").
    /// </summary>
    public static readonly TimeZoneInfo VnTimeZone = GetVnTimeZone();

    private static TimeZoneInfo GetVnTimeZone()
    {
        // Thử Windows ID trước, fallback sang IANA (Linux/macOS)
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Ho_Chi_Minh" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        // Fallback cuối: tạo thủ công UTC+7
        return TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    }

    /// <summary>Trả về ngày hiện tại theo giờ Việt Nam.</summary>
    public static DateTime TodayVn() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTimeZone).Date;

    /// <summary>Trả về DateTime hiện tại theo giờ Việt Nam.</summary>
    public static DateTime NowVn() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTimeZone);
}
