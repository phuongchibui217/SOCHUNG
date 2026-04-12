using ExpenseManagerAPI.DTOs;

namespace ExpenseManagerAPI.Services;

public interface IDashboardService
{
    /// <summary>
    /// Resolve khoảng thời gian hiện tại và kỳ trước từ query params.
    /// Trả null nếu input không hợp lệ kèm error message.
    /// </summary>
    (DateTime From, DateTime To, DateTime PrevFrom, DateTime PrevTo, string? Error)
        ResolveDateRange(OverviewQuery query);

    /// <summary>
    /// Tính tổng chi tiêu trong khoảng [from, to].
    /// </summary>
    Task<decimal> GetTotalExpenseAsync(long userId, DateTime from, DateTime to);

    /// <summary>
    /// Tính tổng công nợ phát sinh trong khoảng [from, to].
    /// Bao gồm cả NO và CHO_VAY chưa xóa.
    /// </summary>
    Task<decimal> GetTotalDebtAsync(long userId, DateTime from, DateTime to);

    /// <summary>
    /// Tính tổng công nợ theo loại (NO hoặc CHO_VAY) trong khoảng [from, to].
    /// </summary>
    Task<decimal> GetTotalDebtByTypeAsync(long userId, DateTime from, DateTime to, string loaiCongNo);

    /// <summary>
    /// Lấy top 3 danh mục chi tiêu nhiều nhất trong khoảng [from, to].
    /// </summary>
    Task<List<TopCategoryDto>> GetTopCategoriesAsync(long userId, DateTime from, DateTime to);

    /// <summary>
    /// Tính % thay đổi so với kỳ trước. Trả null nếu kỳ trước = 0.
    /// </summary>
    double? CalculateTrend(decimal current, decimal previous);

    /// <summary>
    /// Tạo chart data phù hợp theo filterType.
    /// </summary>
    Task<List<ChartPointDto>> BuildChartDataAsync(
        long userId, string filterType, DateTime from, DateTime to);
}
