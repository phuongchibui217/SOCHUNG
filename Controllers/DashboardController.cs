using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ExpenseManagerAPI.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
        => _dashboardService = dashboardService;

    private long GetCurrentUserId() =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException());

    // GET /api/dashboard/overview
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview([FromQuery] OverviewQuery query)
    {
        // 1. Resolve date range + validate
        var (from, to, prevFrom, prevTo, error) = _dashboardService.ResolveDateRange(query);
        if (error != null)
            return BadRequest(new { message = error });

        var userId = GetCurrentUserId();

        // Debug log — xác nhận filter đang dùng
        Console.WriteLine($"[Dashboard] userId={userId} filterType={query.FilterType} from={from:yyyy-MM-dd} to={to:yyyy-MM-dd}");

        // Chạy tuần tự — EF Core không hỗ trợ concurrent queries trên cùng DbContext
        var totalExpense     = await _dashboardService.GetTotalExpenseAsync(userId, from, to);
        var totalDebt        = await _dashboardService.GetTotalDebtByTypeAsync(userId, from, to, "NO");
        var totalLoan        = await _dashboardService.GetTotalDebtByTypeAsync(userId, from, to, "CHO_VAY");
        var prevExpense      = await _dashboardService.GetTotalExpenseAsync(userId, prevFrom, prevTo);
        var prevDebt         = await _dashboardService.GetTotalDebtByTypeAsync(userId, prevFrom, prevTo, "NO");
        var prevLoan         = await _dashboardService.GetTotalDebtByTypeAsync(userId, prevFrom, prevTo, "CHO_VAY");
        var topCats          = await _dashboardService.GetTopCategoriesAsync(userId, from, to);
        var chartData        = await _dashboardService.BuildChartDataAsync(userId, query.FilterType, from, to);

        Console.WriteLine($"[Dashboard] Result => expense={totalExpense} debt={totalDebt} loan={totalLoan}");

        // 3. Build response
        var data = new OverviewData
        {
            FilterType = query.FilterType.ToLower(),
            Range = new DateRangeDto
            {
                FromDate         = from.ToString("yyyy-MM-dd"),
                ToDate           = to.ToString("yyyy-MM-dd"),
                PreviousFromDate = prevFrom.ToString("yyyy-MM-dd"),
                PreviousToDate   = prevTo.ToString("yyyy-MM-dd")
            },
            TotalExpense          = totalExpense,
            TotalDebt             = totalDebt,
            TotalLoan             = totalLoan,
            ExpenseTrendPercent   = _dashboardService.CalculateTrend(totalExpense, prevExpense),
            DebtTrendPercent      = _dashboardService.CalculateTrend(totalDebt, prevDebt),
            LoanTrendPercent      = _dashboardService.CalculateTrend(totalLoan, prevLoan),
            TopExpenseCategories  = topCats,
            ChartData             = chartData
        };

        return Ok(new { message = "Lấy báo cáo thành công", data });
    }
}
