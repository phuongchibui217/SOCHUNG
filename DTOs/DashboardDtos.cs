namespace ExpenseManagerAPI.DTOs;

// --- Query params ---

public class OverviewQuery
{
    // "day" | "week" | "month" | "custom"
    public string FilterType { get; set; } = "month";

    // Dùng cho day và week: bất kỳ ngày nào trong kỳ
    public DateTime? Date { get; set; }

    // Dùng cho month: "YYYY-MM", mặc định tháng hiện tại
    public string? Month { get; set; }

    // Dùng cho custom
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

// --- Response models ---

public class DateRangeDto
{
    public string FromDate { get; set; } = string.Empty;
    public string ToDate { get; set; } = string.Empty;
    public string PreviousFromDate { get; set; } = string.Empty;
    public string PreviousToDate { get; set; } = string.Empty;
}

public class TopCategoryDto
{
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public decimal Amount { get; set; }
}

public class ChartPointDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Expense { get; set; }
    public decimal Debt { get; set; }
}

public class OverviewData
{
    public string FilterType { get; set; } = string.Empty;
    public DateRangeDto Range { get; set; } = new();
    public decimal TotalExpense { get; set; }
    public decimal TotalDebt { get; set; }   // LoaiCongNo = "NO"
    public decimal TotalLoan { get; set; }   // LoaiCongNo = "CHO_VAY"
    public double? ExpenseTrendPercent { get; set; }
    public double? DebtTrendPercent { get; set; }
    public double? LoanTrendPercent { get; set; }
    public List<TopCategoryDto> TopExpenseCategories { get; set; } = new();
    public List<ChartPointDto> ChartData { get; set; } = new();
}
