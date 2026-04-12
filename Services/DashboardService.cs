using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManagerAPI.Services;

public class DashboardService : IDashboardService
{
    private readonly SoChungDbContext _db;

    public DashboardService(SoChungDbContext db) => _db = db;

    // -------------------------------------------------------------------------
    // ResolveDateRange
    // -------------------------------------------------------------------------
    public (DateTime From, DateTime To, DateTime PrevFrom, DateTime PrevTo, string? Error)
        ResolveDateRange(OverviewQuery query)
    {
        var today = DateTime.Today;

        switch (query.FilterType.ToLower())
        {
            case "day":
            {
                var d = query.Date?.Date ?? today;
                var prev = d.AddDays(-1);
                return (d, d, prev, prev, null);
            }

            case "week":
            {
                var anchor = query.Date?.Date ?? today;
                // Tuần bắt đầu Thứ 2
                int diff = ((int)anchor.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var from = anchor.AddDays(-diff);
                var to = from.AddDays(6);
                var prevFrom = from.AddDays(-7);
                var prevTo = to.AddDays(-7);
                return (from, to, prevFrom, prevTo, null);
            }

            case "month":
            {
                DateTime from, to;
                if (!string.IsNullOrWhiteSpace(query.Month))
                {
                    if (!DateTime.TryParseExact(query.Month + "-01", "yyyy-MM-dd",
                            null, System.Globalization.DateTimeStyles.None, out var parsed))
                        return (default, default, default, default,
                            "Định dạng tháng không hợp lệ. Vui lòng dùng YYYY-MM.");
                    from = parsed;
                }
                else
                {
                    from = new DateTime(today.Year, today.Month, 1);
                }
                to = from.AddMonths(1).AddDays(-1);
                var prevFrom = from.AddMonths(-1);
                var prevTo = from.AddDays(-1);
                return (from, to, prevFrom, prevTo, null);
            }

            case "custom":
            {
                if (!query.FromDate.HasValue || !query.ToDate.HasValue)
                    return (default, default, default, default,
                        "Vui lòng chọn đầy đủ khoảng thời gian.");

                var from = query.FromDate.Value.Date;
                var to = query.ToDate.Value.Date;

                if (from > to)
                    return (default, default, default, default,
                        "Khoảng thời gian không hợp lệ. Vui lòng kiểm tra lại.");

                var span = (to - from).Days + 1;
                var prevTo = from.AddDays(-1);
                var prevFrom = prevTo.AddDays(-(span - 1));
                return (from, to, prevFrom, prevTo, null);
            }

            default:
                return (default, default, default, default,
                    $"filterType '{query.FilterType}' không hợp lệ. Chấp nhận: day, week, month, custom.");
        }
    }

    // -------------------------------------------------------------------------
    // GetTotalExpenseAsync
    // Dùng range [from, toExclusive) để tránh lệch timezone khi so sánh date.
    // -------------------------------------------------------------------------
    public async Task<decimal> GetTotalExpenseAsync(long userId, DateTime from, DateTime to)
    {
        var toExclusive = to.Date.AddDays(1);
        var result = await _db.ChiTieus
            .Where(c => c.IdNguoiDung == userId
                     && !c.DaXoa
                     && c.NgayChi >= from.Date
                     && c.NgayChi < toExclusive)
            .SumAsync(c => (decimal?)c.SoTien) ?? 0m;

        Console.WriteLine($"[Dashboard] GetTotalExpense userId={userId} from={from:yyyy-MM-dd} to={to:yyyy-MM-dd} => {result}");
        return result;
    }

    // -------------------------------------------------------------------------
    // GetTotalDebtAsync
    // NgayPhatSinh là timestamp — cast về date bằng EF.Property để tránh
    // .Date không translate được sang PostgreSQL.
    // -------------------------------------------------------------------------
    public async Task<decimal> GetTotalDebtAsync(long userId, DateTime from, DateTime to)
    {
        var toExclusive = to.Date.AddDays(1);
        var result = await _db.CongNos
            .Where(c => c.IdNguoiDung == userId
                     && !c.DaXoa
                     && c.NgayPhatSinh >= from.Date
                     && c.NgayPhatSinh < toExclusive)
            .SumAsync(c => (decimal?)c.SoTien) ?? 0m;

        Console.WriteLine($"[Dashboard] GetTotalDebt userId={userId} from={from:yyyy-MM-dd} to={to:yyyy-MM-dd} => {result}");
        return result;
    }

    // -------------------------------------------------------------------------
    // GetTotalDebtByTypeAsync — tổng số tiền CÒN LẠI chưa thanh toán theo loại
    // Chỉ tính khoản CHUA_TRA hoặc TRA_MOT_PHAN, phát sinh trong kỳ.
    // Sum (SoTien - đã thanh toán) để phản ánh đúng số tiền thực tế còn nợ.
    // -------------------------------------------------------------------------
    public async Task<decimal> GetTotalDebtByTypeAsync(long userId, DateTime from, DateTime to, string loaiCongNo)
    {
        var toExclusive = to.Date.AddDays(1);

        var khoans = await _db.CongNos
            .Where(c => c.IdNguoiDung == userId
                     && !c.DaXoa
                     && c.LoaiCongNo == loaiCongNo
                     && (c.TrangThai == "CHUA_TRA" || c.TrangThai == "TRA_MOT_PHAN")
                     && c.NgayPhatSinh >= from.Date
                     && c.NgayPhatSinh < toExclusive)
            .Select(c => new
            {
                c.SoTien,
                DaThanhToan = c.ThanhToanCongNos.Sum(t => (decimal?)t.SoTienThanhToan) ?? 0m
            })
            .ToListAsync();

        var result = khoans.Sum(c => Math.Max(0, c.SoTien - c.DaThanhToan));

        Console.WriteLine($"[Dashboard] GetTotalDebt({loaiCongNo}) userId={userId} from={from:yyyy-MM-dd} to={to:yyyy-MM-dd} count={khoans.Count} => {result}");
        return result;
    }

    // -------------------------------------------------------------------------
    // GetTopCategoriesAsync
    // Join trực tiếp sang DanhMucChiTieu với điều kiện !DaXoa để đảm bảo
    // metadata (icon/color) đồng nhất với GET /api/categories.
    // Nếu category đã soft-delete → fallback về "Khác" / "more_horiz" / "#9E9E9E".
    // -------------------------------------------------------------------------
    public async Task<List<TopCategoryDto>> GetTopCategoriesAsync(
        long userId, DateTime from, DateTime to)
    {
        var toExclusive = to.Date.AddDays(1);

        // Lấy raw data trong kỳ, kèm thông tin category (có thể null nếu đã xóa)
        var raw = await _db.ChiTieus
            .Where(c => c.IdNguoiDung == userId
                     && !c.DaXoa
                     && c.NgayChi >= from.Date
                     && c.NgayChi < toExclusive)
            .Select(c => new
            {
                c.IdDanhMuc,
                c.SoTien,
                // Chỉ lấy metadata nếu category còn active (!DaXoa)
                CategoryName  = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                                    ? c.DanhMucChiTieu.TenDanhMuc : (string?)null,
                CategoryIcon  = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                                    ? c.DanhMucChiTieu.Icon : (string?)null,
                CategoryColor = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                                    ? c.DanhMucChiTieu.MauSac : (string?)null,
            })
            .ToListAsync();

        // Group trong memory — tránh EF translate phức tạp với nullable string
        return raw
            .GroupBy(c => c.IdDanhMuc)
            .Select(g =>
            {
                var first = g.First();
                // Nếu category đã bị soft-delete thì first.CategoryName == null → fallback
                var isActive = first.CategoryName != null;
                return new TopCategoryDto
                {
                    CategoryId   = g.Key,
                    CategoryName = isActive ? first.CategoryName! : "Khác",
                    Icon         = isActive ? first.CategoryIcon  : "more_horiz",
                    Color        = isActive ? first.CategoryColor : "#9E9E9E",
                    Amount       = g.Sum(c => c.SoTien)
                };
            })
            .OrderByDescending(x => x.Amount)
            .Take(3)
            .ToList();
    }

    // -------------------------------------------------------------------------
    // CalculateTrend
    // -------------------------------------------------------------------------
    public double? CalculateTrend(decimal current, decimal previous)
    {
        if (previous == 0) return null; // tránh chia 0
        return Math.Round((double)((current - previous) / previous * 100), 1);
    }

    // -------------------------------------------------------------------------
    // BuildChartDataAsync
    // -------------------------------------------------------------------------
    public async Task<List<ChartPointDto>> BuildChartDataAsync(
        long userId, string filterType, DateTime from, DateTime to)
    {
        return filterType.ToLower() switch
        {
            "day"    => await BuildDayChartAsync(userId, from),
            "week"   => await BuildWeekChartAsync(userId, from, to),
            "month"  => await BuildMonthChartAsync(userId, from, to),
            "custom" => await BuildCustomChartAsync(userId, from, to),
            _        => new List<ChartPointDto>()
        };
    }

    // day: 1 cột tổng duy nhất
    private async Task<List<ChartPointDto>> BuildDayChartAsync(long userId, DateTime day)
    {
        var expense = await GetTotalExpenseAsync(userId, day, day);
        var debt = await GetTotalDebtAsync(userId, day, day);
        return new List<ChartPointDto>
        {
            new() { Label = day.ToString("dd/MM"), Expense = expense, Debt = debt }
        };
    }

    // week: 7 cột theo từng ngày (T2 → CN)
    private async Task<List<ChartPointDto>> BuildWeekChartAsync(
        long userId, DateTime from, DateTime to)
    {
        var toExclusive = to.Date.AddDays(1);
        var expenses = await _db.ChiTieus
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa
                     && c.NgayChi >= from.Date && c.NgayChi < toExclusive)
            .Select(c => new { c.NgayChi, c.SoTien })
            .ToListAsync();

        var debts = await _db.CongNos
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa
                     && c.NgayPhatSinh >= from.Date && c.NgayPhatSinh < toExclusive)
            .Select(c => new { Date = c.NgayPhatSinh.Date, c.SoTien })
            .ToListAsync();

        var result = new List<ChartPointDto>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            result.Add(new ChartPointDto
            {
                Label = d.ToString("ddd dd/MM"),
                Expense = expenses.Where(e => e.NgayChi.Date == d).Sum(e => e.SoTien),
                Debt = debts.Where(x => x.Date == d).Sum(x => x.SoTien)
            });
        }
        return result;
    }

    // month: theo tuần (Tuần 1..4/5)
    private async Task<List<ChartPointDto>> BuildMonthChartAsync(
        long userId, DateTime from, DateTime to)
    {
        var toExclusive = to.Date.AddDays(1);
        var expenses = await _db.ChiTieus
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa
                     && c.NgayChi >= from.Date && c.NgayChi < toExclusive)
            .Select(c => new { c.NgayChi, c.SoTien })
            .ToListAsync();

        var debts = await _db.CongNos
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa
                     && c.NgayPhatSinh >= from.Date && c.NgayPhatSinh < toExclusive)
            .Select(c => new { Date = c.NgayPhatSinh.Date, c.SoTien })
            .ToListAsync();

        // Chia theo tuần trong tháng
        var result = new List<ChartPointDto>();
        var weekStart = from;
        int weekNum = 1;
        while (weekStart <= to)
        {
            var weekEnd = new DateTime(
                Math.Min(weekStart.AddDays(6).Ticks, to.Ticks));

            result.Add(new ChartPointDto
            {
                Label = $"Tuần {weekNum}",
                Expense = expenses
                    .Where(e => e.NgayChi.Date >= weekStart && e.NgayChi.Date <= weekEnd)
                    .Sum(e => e.SoTien),
                Debt = debts
                    .Where(x => x.Date >= weekStart && x.Date <= weekEnd)
                    .Sum(x => x.SoTien)
            });

            weekStart = weekStart.AddDays(7);
            weekNum++;
        }
        return result;
    }

    // custom: theo ngày nếu ≤ 31 ngày, theo tuần nếu > 31 ngày
    private async Task<List<ChartPointDto>> BuildCustomChartAsync(
        long userId, DateTime from, DateTime to)
    {
        var span = (to - from).Days + 1;
        if (span <= 31)
        {
            // Theo từng ngày
            var toExclusive = to.Date.AddDays(1);
            var expenses = await _db.ChiTieus
                .Where(c => c.IdNguoiDung == userId && !c.DaXoa
                         && c.NgayChi >= from.Date && c.NgayChi < toExclusive)
                .Select(c => new { c.NgayChi, c.SoTien })
                .ToListAsync();

            var debts = await _db.CongNos
                .Where(c => c.IdNguoiDung == userId && !c.DaXoa
                         && c.NgayPhatSinh >= from.Date && c.NgayPhatSinh < toExclusive)
                .Select(c => new { Date = c.NgayPhatSinh.Date, c.SoTien })
                .ToListAsync();

            var result = new List<ChartPointDto>();
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                result.Add(new ChartPointDto
                {
                    Label = d.ToString("dd/MM"),
                    Expense = expenses.Where(e => e.NgayChi.Date == d).Sum(e => e.SoTien),
                    Debt = debts.Where(x => x.Date == d).Sum(x => x.SoTien)
                });
            }
            return result;
        }
        else
        {
            // Theo tuần bucket
            return await BuildMonthChartAsync(userId, from, to);
        }
    }
}
