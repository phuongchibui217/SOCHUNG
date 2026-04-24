using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Helpers;
using ExpenseManagerAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExpenseManagerAPI.Controllers;

[ApiController]
[Route("api/debts")]
[Authorize]
public class DebtsController : ControllerBase
{
    private readonly SoChungDbContext _db;
    private readonly ILogger<DebtsController> _logger;

    public DebtsController(SoChungDbContext db, ILogger<DebtsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private long GetCurrentUserId() =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException());

    // Helper: tính tổng ConLai của một người theo userId + tenNguoi
    // Chỉ tính khoản CHUA_TRA hoặc TRA_MOT_PHAN, chưa xóa
    private async Task<decimal> GetOutstandingOfPersonAsync(long userId, string tenNguoi)
    {
        var khoans = await _db.CongNos
            .Include(c => c.ThanhToanCongNos)
            .Where(c => c.IdNguoiDung == userId
                     && c.TenNguoi == tenNguoi
                     && !c.DaXoa
                     && (c.TrangThai == "CHUA_TRA" || c.TrangThai == "TRA_MOT_PHAN"))
            .ToListAsync();

        return khoans.Sum(c => c.SoTien - c.ThanhToanCongNos.Sum(t => t.SoTienThanhToan));
    }

    // -------------------------------------------------------------------------
    // GET /api/debts?transactionType=NO&statusFilter=ALL
    // Danh sách công nợ của user hiện tại, lọc theo loại và trạng thái.
    //
    // transactionType: bắt buộc — NO | CHO_VAY
    // statusFilter: optional — ALL (default) | OPEN | COMPLETED
    //
    // displayStatus logic:
    //   COMPLETED  — remainingAmount = 0 (DA_TRA)
    //   OVERDUE    — chưa hoàn tất, có HanTra, HanTra < today
    //   DUE_SOON   — chưa hoàn tất, có HanTra, HanTra >= today, HanTra - today <= DueSoonThresholdDays (3)
    //   NORMAL_OPEN — còn lại
    //
    // Sort: OVERDUE → DUE_SOON → NORMAL_OPEN → COMPLETED
    //   Trong OVERDUE/DUE_SOON: HanTra gần nhất trước
    //   Trong NORMAL_OPEN: có HanTra gần nhất trước, không có HanTra → NgayPhatSinh mới nhất
    //   Trong COMPLETED: NgayPhatSinh mới nhất trước
    // -------------------------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> GetMyDebts([FromQuery] DebtListQuery query)
    {
        // Validate transactionType
        var validTypes = new[] { "NO", "CHO_VAY" };
        if (string.IsNullOrEmpty(query.TransactionType) || !validTypes.Contains(query.TransactionType.ToUpper()))
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { transactionType = new[] { "transactionType là bắt buộc. Chấp nhận: NO, CHO_VAY" } }
            });

        var validFilters = new[] { "ALL", "OPEN", "COMPLETED" };
        var statusFilter = string.IsNullOrEmpty(query.StatusFilter) ? "ALL" : query.StatusFilter.ToUpper();
        if (!validFilters.Contains(statusFilter))
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { statusFilter = new[] { "statusFilter không hợp lệ. Chấp nhận: ALL, OPEN, COMPLETED" } }
            });

        var userId = GetCurrentUserId();
        const int DueSoonThresholdDays = 3;
        var today = TimeZoneHelper.TodayVn();

        Console.WriteLine($"[GetMyDebts] userId={userId} transactionType={query.TransactionType} statusFilter={statusFilter}");

        // 1. Load tất cả khoản theo transactionType, chưa xóa
        var rawList = await _db.CongNos
            .Include(c => c.ThanhToanCongNos)
            .Where(c => c.IdNguoiDung == userId
                     && !c.DaXoa
                     && c.LoaiCongNo == query.TransactionType.ToUpper())
            .ToListAsync();

        // 2. Tính personOutstandingTotal cho từng TenNguoi (chỉ khoản chưa hoàn tất)
        var outstandingByPerson = rawList
            .Where(c => c.SoTien - c.ThanhToanCongNos.Sum(t => t.SoTienThanhToan) > 0)
            .GroupBy(c => c.TenNguoi)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(c => c.SoTien - c.ThanhToanCongNos.Sum(t => t.SoTienThanhToan))
            );

        // 3. Map sang DTO với displayStatus
        var mapped = rawList.Select(c =>
        {
            var paid      = c.ThanhToanCongNos.Sum(t => t.SoTienThanhToan);
            var remaining = Math.Max(0, c.SoTien - paid);
            var status    = remaining == 0 ? "DA_TRA"
                          : paid > 0       ? "TRA_MOT_PHAN"
                                           : "CHUA_TRA";

            var isChoVay = c.LoaiCongNo == "CHO_VAY";
            string displayStatus;
            int? overdueDays = null;

            if (remaining == 0)
            {
                // Đã hoàn tất: tab Nợ → "DA_TRA", tab Cho vay → "DA_THU"
                displayStatus = isChoVay ? "DA_THU" : "DA_TRA";
            }
            else if (c.HanTra.HasValue && c.HanTra.Value.Date < today)
            {
                displayStatus = "QUA_HAN";
                overdueDays   = (int)(today - c.HanTra.Value.Date).TotalDays;
            }
            else if (c.HanTra.HasValue
                  && c.HanTra.Value.Date >= today
                  && (c.HanTra.Value.Date - today).TotalDays <= DueSoonThresholdDays)
            {
                displayStatus = "SAP_DEN_HAN";
            }
            else
            {
                // Đang mở: tab Nợ → "DANG_NO", tab Cho vay → "DANG_CHO_VAY"
                displayStatus = isChoVay ? "DANG_CHO_VAY" : "DANG_NO";
            }

            // completedStatus: giá trị "đã xong" tương ứng với từng loại
            var completedStatus = isChoVay ? "DA_THU" : "DA_TRA";

            return new DebtListItemDto
            {
                DebtId                 = c.IdCongNo,
                TransactionType        = c.LoaiCongNo,
                PersonName             = c.TenNguoi,
                OriginalAmount         = c.SoTien,
                PaidAmount             = paid,
                RemainingAmount        = remaining,
                PersonOutstandingTotal = displayStatus != completedStatus
                                         ? outstandingByPerson.GetValueOrDefault(c.TenNguoi)
                                         : null,
                OccurredDate  = c.NgayPhatSinh.ToString("yyyy-MM-dd"),
                DueDate       = c.HanTra?.ToString("yyyy-MM-dd"),
                Status        = status,
                DisplayStatus = displayStatus,
                OverdueDays   = overdueDays,
                Note          = c.NoiDung
            };
        }).ToList();

        // 4. Áp dụng statusFilter
        // completedStatus khác nhau theo transactionType — lấy từ item đầu tiên hoặc dùng query param
        var isChoVayQuery = query.TransactionType.ToUpper() == "CHO_VAY";
        var completedStatusFilter = isChoVayQuery ? "DA_THU" : "DA_TRA";

        var filtered = statusFilter switch
        {
            "OPEN"      => mapped.Where(x => x.DisplayStatus != completedStatusFilter).ToList(),
            "COMPLETED" => mapped.Where(x => x.DisplayStatus == completedStatusFilter).ToList(),
            _           => mapped // ALL
        };

        // 5. Sort theo priority
        static int DisplayPriority(string ds) => ds switch
        {
            "QUA_HAN"     => 0,
            "SAP_DEN_HAN" => 1,
            "DANG_NO"     => 2,
            "DANG_CHO_VAY" => 2,
            _             => 3  // DA_TRA / DA_THU
        };

        var sorted = filtered
            .OrderBy(x => DisplayPriority(x.DisplayStatus))
            .ThenBy(x =>
            {
                if (x.DisplayStatus is "QUA_HAN" or "SAP_DEN_HAN")
                    return x.DueDate ?? "9999-12-31";
                if (x.DisplayStatus is "DANG_NO" or "DANG_CHO_VAY")
                    return x.DueDate ?? "0000-00-00";
                return x.OccurredDate;
            })
            .ThenByDescending(x => x.OccurredDate)
            .ToList();

        return Ok(new { message = "Lấy danh sách công nợ thành công", data = sorted });
    }

    // -------------------------------------------------------------------------
    // GET /api/debts/{userId}  — giữ lại để tương thích
    // -------------------------------------------------------------------------
    // GET /api/debts/by-user/{userId}  — giữ lại để tương thích (legacy, đổi path tránh conflict)
    // Route cũ GET /api/debts/{userId} bị conflict với GET /api/debts/{id} của FE
    // -------------------------------------------------------------------------
    [HttpGet("by-user/{userId:long}")]
    public async Task<IActionResult> GetByUser(long userId)
    {
        var list = await _db.CongNos
            .Include(c => c.ThanhToanCongNos)
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa)
            .OrderByDescending(c => c.NgayPhatSinh)
            .Select(c => new CongNoResponseDto
            {
                IdCongNo = c.IdCongNo,
                IdNguoiDung = c.IdNguoiDung,
                TenNguoi = c.TenNguoi,
                SoTien = c.SoTien,
                DaThanhToan = c.ThanhToanCongNos.Sum(t => t.SoTienThanhToan),
                ConLai = c.SoTien - c.ThanhToanCongNos.Sum(t => t.SoTienThanhToan),
                LoaiCongNo = c.LoaiCongNo,
                NoiDung = c.NoiDung,
                HanTra = c.HanTra,
                TrangThai = c.TrangThai,
                DaXoa = c.DaXoa,
                NgayPhatSinh = c.NgayPhatSinh
            })
            .ToListAsync();

        return Ok(list);
    }

    // -------------------------------------------------------------------------
    // GET /api/debts/{id}  — chi tiết 1 khoản công nợ (spec FE)
    // Route constraint "detail" để tránh conflict với {userId} legacy
    // -------------------------------------------------------------------------
    [HttpGet("{id:long}")]         // GET /api/debts/5  ← FE đang gọi
    [HttpGet("{id:long}/detail")]  // GET /api/debts/5/detail ← alias
    [HttpGet("{id:long}/info")]    // GET /api/debts/5/info   ← alias
    public async Task<IActionResult> GetDebtDetail(long id)
    {
        var userId = GetCurrentUserId();

        Console.WriteLine($"[DebtDetail] GET id={id} userId={userId}");

        var entity = await _db.CongNos
            .Include(c => c.ThanhToanCongNos)
            .FirstOrDefaultAsync(c => c.IdCongNo == id && c.IdNguoiDung == userId && !c.DaXoa);

        if (entity == null)
        {
            Console.WriteLine($"[DebtDetail] NOT FOUND id={id} userId={userId}");
            return NotFound(new { message = "Công nợ không tồn tại." });
        }

        var paidAmount = entity.ThanhToanCongNos.Sum(t => t.SoTienThanhToan);
        var remainingAmount = Math.Max(0, entity.SoTien - paidAmount);

        // personOutstandingTotal: chỉ tính khi khoản còn mở
        decimal? personOutstandingTotal = null;
        if (entity.TrangThai == "CHUA_TRA" || entity.TrangThai == "TRA_MOT_PHAN")
            personOutstandingTotal = await GetOutstandingOfPersonAsync(userId, entity.TenNguoi);

        var paymentHistory = entity.ThanhToanCongNos
            .OrderByDescending(t => t.NgayThanhToan)
            .ThenByDescending(t => t.IdThanhToan)
            .Select(t => new PaymentHistoryItemDto
            {
                PaymentId = t.IdThanhToan,
                PaymentDate = t.NgayThanhToan.ToString("yyyy-MM-dd"),
                Amount = t.SoTienThanhToan,
                Note = t.GhiChu
            })
            .ToList();

        var data = new DebtDetailDto
        {
            DebtId = entity.IdCongNo,
            TransactionType = entity.LoaiCongNo,
            PersonName = entity.TenNguoi,
            OriginalAmount = entity.SoTien,
            PaidAmount = paidAmount,
            RemainingAmount = remainingAmount,
            PersonOutstandingTotal = personOutstandingTotal,
            Status = entity.TrangThai,
            OccurredDate = entity.NgayPhatSinh.ToString("yyyy-MM-dd"),
            DueDate = entity.HanTra?.ToString("yyyy-MM-dd"),
            Note = entity.NoiDung,
            PaymentHistory = paymentHistory
        };

        return Ok(new { message = "Lấy chi tiết công nợ thành công", data });
    }

    // -------------------------------------------------------------------------
    // GET /api/debts/detail/{id}  — giữ lại để tương thích (legacy)
    // -------------------------------------------------------------------------
    [HttpGet("detail/{id}")]
    public async Task<IActionResult> GetDetail(long id)
    {
        var userId = GetCurrentUserId();

        var entity = await _db.CongNos
            .Include(c => c.ThanhToanCongNos)
            .FirstOrDefaultAsync(c => c.IdCongNo == id && c.IdNguoiDung == userId && !c.DaXoa);

        if (entity == null) return NotFound(new { message = "Công nợ không tồn tại." });

        var daThanhToan = entity.ThanhToanCongNos.Sum(t => t.SoTienThanhToan);

        return Ok(new CongNoDetailDto
        {
            IdCongNo = entity.IdCongNo,
            IdNguoiDung = entity.IdNguoiDung,
            TenNguoi = entity.TenNguoi,
            SoTien = entity.SoTien,
            DaThanhToan = daThanhToan,
            ConLai = entity.SoTien - daThanhToan,
            LoaiCongNo = entity.LoaiCongNo,
            NoiDung = entity.NoiDung,
            HanTra = entity.HanTra,
            TrangThai = entity.TrangThai,
            DaXoa = entity.DaXoa,
            NgayPhatSinh = entity.NgayPhatSinh,
            ThanhToanCongNos = entity.ThanhToanCongNos.Select(t => new ThanhToanResponseDto
            {
                IdThanhToan = t.IdThanhToan,
                IdCongNo = t.IdCongNo,
                SoTienThanhToan = t.SoTienThanhToan,
                NgayThanhToan = t.NgayThanhToan,
                GhiChu = t.GhiChu
            }).ToList()
        });
    }

    // -------------------------------------------------------------------------
    // GET /api/debts/people/suggestions?keyword=...
    // Gợi ý người giao dịch từ lịch sử CongNo của user.
    // Dùng ILike để search case-insensitive (PostgreSQL native).
    // -------------------------------------------------------------------------
    [HttpGet("people/suggestions")]
    public async Task<IActionResult> GetPeopleSuggestions([FromQuery] string? keyword)
    {
        var userId = GetCurrentUserId();
        var kw = keyword?.Trim();

        Console.WriteLine($"[PeopleSuggestions] userId={userId} keyword_received='{keyword}' keyword_trimmed='{kw}'");

        // Lấy tất cả khoản chưa xóa của user, group theo TenNguoi
        var query = _db.CongNos
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa);

        if (!string.IsNullOrEmpty(kw))
            query = query.Where(c => EF.Functions.ILike(c.TenNguoi, "%" + kw + "%"));

        var raw = await query
            .Select(c => new
            {
                c.TenNguoi,
                c.SoTien,
                c.TrangThai,
                DaThanhToan = c.ThanhToanCongNos.Sum(t => t.SoTienThanhToan)
            })
            .ToListAsync();

        Console.WriteLine($"[PeopleSuggestions] raw_count={raw.Count}");

        // Group theo TenNguoi trong memory
        var suggestions = raw
            .GroupBy(c => c.TenNguoi)
            .Select(g => new PersonSuggestionDto
            {
                PersonName = g.Key,
                CurrentOutstanding = g
                    .Where(c => c.TrangThai == "CHUA_TRA" || c.TrangThai == "TRA_MOT_PHAN")
                    .Sum(c => c.SoTien - c.DaThanhToan),
                OpenDebtCount = g
                    .Count(c => c.TrangThai == "CHUA_TRA" || c.TrangThai == "TRA_MOT_PHAN")
            })
            .OrderByDescending(x => x.OpenDebtCount)
            .ThenBy(x => x.PersonName)
            .ToList();

        Console.WriteLine($"[PeopleSuggestions] suggestions_count={suggestions.Count}");

        return Ok(new { message = "Lấy danh sách gợi ý thành công", data = suggestions });
    }

    // -------------------------------------------------------------------------
    // GET /api/debts/search
    // Tìm kiếm công nợ theo keyword + statusFilter, có phân trang.
    // Tìm toàn bộ (cả NO lẫn CHO_VAY) — không filter theo transactionType.
    //
    // Keyword: tìm trong TenNguoi, NoiDung, SoTien — accent-insensitive (unaccent),
    //          case-insensitive (ILike), fallback ILike nếu unaccent chưa enable.
    // StatusFilter: DUE_SOON | OVERDUE | COMPLETED
    //   DUE_SOON  → HanTra - Today ∈ [1, 3] (tức HanTra trong [today+1, today+3])
    //   OVERDUE   → HanTra < today, chưa hoàn tất
    //   COMPLETED → TrangThai = DA_TRA
    // -------------------------------------------------------------------------
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] DebtSearchQuery query)
    {
        var validFilters = new[] { "SAP_DEN_HAN", "QUA_HAN", "DA_THU" };
        if (!string.IsNullOrEmpty(query.StatusFilter) &&
            !validFilters.Contains(query.StatusFilter.ToUpper()))
        {
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { statusFilter = new[] { "statusFilter không hợp lệ. Chấp nhận: SAP_DEN_HAN, QUA_HAN, DA_THU" } }
            });
        }

        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    k => char.ToLower(k.Key[0]) + k.Key[1..],
                    v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            return BadRequest(new { message = "Dữ liệu không hợp lệ", errors });
        }

        var userId  = GetCurrentUserId();
        var keyword = query.Keyword?.TrimStart();

        if (keyword != null)
        {
            if (keyword.Length == 0)
                return BadRequest(new
                {
                    message = "Dữ liệu không hợp lệ",
                    errors = new { keyword = new[] { "Vui lòng nhập thông tin" } }
                });

            if (System.Text.RegularExpressions.Regex.IsMatch(keyword, @"[<>""'%;()&+\-\*\/\\]"))
                return BadRequest(new
                {
                    message = "Dữ liệu không hợp lệ",
                    errors = new { keyword = new[] { "Dữ liệu chứa ký tự không hợp lệ" } }
                });
        }

        var today = TimeZoneHelper.TodayVn();

        Console.WriteLine($"[DebtSearch] userId={userId} keyword='{keyword}' statusFilter='{query.StatusFilter}' today={today:yyyy-MM-dd}");

        var q = _db.CongNos
            .Include(c => c.ThanhToanCongNos)
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa)
            .AsQueryable();

        // --- Keyword filter: unaccent (accent-insensitive) + ILike (case-insensitive) ---
        if (!string.IsNullOrEmpty(keyword))
        {
            var pattern = "%" + keyword + "%";
            try
            {
                q = q.Where(c =>
                    EF.Functions.ILike(AppDb.Unaccent(c.TenNguoi), AppDb.Unaccent(pattern))
                    ||
                    (c.NoiDung != null && EF.Functions.ILike(AppDb.Unaccent(c.NoiDung), AppDb.Unaccent(pattern)))
                    ||
                    EF.Functions.ILike(c.SoTien.ToString(), pattern)
                );
                // Probe để phát hiện lỗi unaccent sớm
                await q.CountAsync();
            }
            catch (Exception ex) when (
                ex.Message.Contains("unaccent") ||
                ex.InnerException?.Message.Contains("unaccent") == true)
            {
                Console.WriteLine($"[DebtSearch] unaccent unavailable, fallback ILike: {ex.Message}");
                q = _db.CongNos
                    .Include(c => c.ThanhToanCongNos)
                    .Where(c => c.IdNguoiDung == userId && !c.DaXoa);
                q = q.Where(c =>
                    EF.Functions.ILike(c.TenNguoi, pattern)
                    ||
                    (c.NoiDung != null && EF.Functions.ILike(c.NoiDung, pattern))
                    ||
                    EF.Functions.ILike(c.SoTien.ToString(), pattern)
                );
            }
        }

        // --- StatusFilter ---
        if (!string.IsNullOrEmpty(query.StatusFilter))
        {
            switch (query.StatusFilter.ToUpper())
            {
                case "SAP_DEN_HAN":
                    var dueSoonFrom = today.AddDays(1);
                    var dueSoonTo   = today.AddDays(3);
                    q = q.Where(c =>
                        c.HanTra.HasValue &&
                        c.HanTra.Value.Date >= dueSoonFrom &&
                        c.HanTra.Value.Date <= dueSoonTo &&
                        (c.TrangThai == "CHUA_TRA" || c.TrangThai == "TRA_MOT_PHAN"));
                    break;

                case "QUA_HAN":
                    q = q.Where(c =>
                        c.HanTra.HasValue &&
                        c.HanTra.Value.Date < today &&
                        (c.TrangThai == "CHUA_TRA" || c.TrangThai == "TRA_MOT_PHAN"));
                    break;

                case "DA_THU":
                    q = q.Where(c => c.TrangThai == "DA_TRA");
                    break;
            }
        }

        q = q.OrderByDescending(c => c.NgayPhatSinh).ThenByDescending(c => c.IdCongNo);

        var pageSize   = Math.Clamp(query.PageSize, 1, 15);
        var page       = Math.Max(query.Page, 1);
        var totalItems = await q.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

        Console.WriteLine($"[DebtSearch] totalItems={totalItems} page={page}/{totalPages}");

        // Fetch về memory để tính RemainingAmount, DisplayStatus và outstanding theo người
        var raw = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Tính tổng outstanding theo (TenNguoi, LoaiCongNo) từ TOÀN BỘ khoản chưa hoàn tất của user
        // — không giới hạn trong page hiện tại để số liệu luôn chính xác
        var allOpen = await _db.CongNos
            .Include(c => c.ThanhToanCongNos)
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa &&
                        (c.TrangThai == "CHUA_TRA" || c.TrangThai == "TRA_MOT_PHAN"))
            .ToListAsync();

        // Key: (tenNguoi, loaiCongNo) → tổng còn lại
        var outstandingMap = allOpen
            .GroupBy(c => (c.TenNguoi, c.LoaiCongNo))
            .ToDictionary(
                g => g.Key,
                g => g.Sum(c => Math.Max(0, c.SoTien - c.ThanhToanCongNos.Sum(t => t.SoTienThanhToan)))
            );

        var items = raw.Select(c =>
        {
            var paid      = c.ThanhToanCongNos.Sum(t => t.SoTienThanhToan);
            var remaining = Math.Max(0, c.SoTien - paid);
            var isChoVay = c.LoaiCongNo == "CHO_VAY";

            string displayStatus;
            if (remaining == 0)
                displayStatus = isChoVay ? "DA_THU" : "DA_TRA";
            else if (c.HanTra.HasValue && c.HanTra.Value.Date < today)
                displayStatus = "QUA_HAN";
            else if (c.HanTra.HasValue && c.HanTra.Value.Date >= today.AddDays(1) && c.HanTra.Value.Date <= today.AddDays(3))
                displayStatus = "SAP_DEN_HAN";
            else
                displayStatus = isChoVay ? "DANG_CHO_VAY" : "DANG_NO";

            var debtTotal    = outstandingMap.GetValueOrDefault((c.TenNguoi, "NO"),    0);
            var lendingTotal = outstandingMap.GetValueOrDefault((c.TenNguoi, "CHO_VAY"), 0);

            Console.WriteLine(
                $"[DebtSearch] item debtId={c.IdCongNo} person='{c.TenNguoi}' type={c.LoaiCongNo} " +
                $"remaining={remaining} debtTotal={debtTotal} lendingTotal={lendingTotal}");

            return new DebtSearchItemDto
            {
                DebtId                       = c.IdCongNo,
                TransactionType              = c.LoaiCongNo,
                PersonName                   = c.TenNguoi,
                Amount                       = c.SoTien,
                RemainingAmount              = remaining,
                OccurredDate                 = c.NgayPhatSinh.ToString("yyyy-MM-dd"),
                DueDate                      = c.HanTra?.ToString("yyyy-MM-dd"),
                Status                       = c.TrangThai,
                DisplayStatus                = displayStatus,
                Note                         = c.NoiDung,
                PersonOutstandingDebtTotal    = debtTotal,
                PersonOutstandingLendingTotal = lendingTotal
            };
        }).ToList();

        return Ok(new
        {
            message = "Tìm kiếm thành công",
            data = new DebtSearchResultDto
            {
                Items      = items,
                Page       = page,
                PageSize   = pageSize,
                TotalItems = totalItems,
                TotalPages = totalPages
            }
        });
    }

    // -------------------------------------------------------------------------
    // GET /api/debts/search/suggestions
    // Gợi ý nhanh top 5 theo keyword — tìm trong TenNguoi và NoiDung.
    // Tìm toàn bộ (cả NO lẫn CHO_VAY).
    // -------------------------------------------------------------------------
    [HttpGet("search/suggestions")]
    public async Task<IActionResult> SearchSuggestions([FromQuery] string? keyword)
    {
        var kw = keyword?.TrimStart();

        if (string.IsNullOrEmpty(kw))
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { keyword = new[] { "Vui lòng nhập thông tin" } }
            });

        if (kw.Length > 100)
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { keyword = new[] { "Không được vượt quá 100 ký tự" } }
            });

        var userId  = GetCurrentUserId();
        var pattern = "%" + kw + "%";

        Console.WriteLine($"[DebtSuggestions] userId={userId} keyword='{kw}'");

        List<DebtSuggestionDto> suggestions;
        try
        {
            suggestions = await _db.CongNos
                .Where(c => c.IdNguoiDung == userId && !c.DaXoa &&
                    (
                        EF.Functions.ILike(AppDb.Unaccent(c.TenNguoi), AppDb.Unaccent(pattern))
                        ||
                        (c.NoiDung != null && EF.Functions.ILike(AppDb.Unaccent(c.NoiDung), AppDb.Unaccent(pattern)))
                    ))
                .OrderByDescending(c => c.NgayPhatSinh)
                .Take(5)
                .Select(c => new DebtSuggestionDto
                {
                    DebtId          = c.IdCongNo,
                    TransactionType = c.LoaiCongNo,
                    PersonName      = c.TenNguoi,
                    Amount          = c.SoTien,
                    Status          = c.TrangThai,
                    Note            = c.NoiDung
                })
                .ToListAsync();
        }
        catch (Exception ex) when (
            ex.Message.Contains("unaccent") ||
            ex.InnerException?.Message.Contains("unaccent") == true)
        {
            Console.WriteLine($"[DebtSuggestions] unaccent unavailable, fallback ILike: {ex.Message}");
            suggestions = await _db.CongNos
                .Where(c => c.IdNguoiDung == userId && !c.DaXoa &&
                    (
                        EF.Functions.ILike(c.TenNguoi, pattern)
                        ||
                        (c.NoiDung != null && EF.Functions.ILike(c.NoiDung, pattern))
                    ))
                .OrderByDescending(c => c.NgayPhatSinh)
                .Take(5)
                .Select(c => new DebtSuggestionDto
                {
                    DebtId          = c.IdCongNo,
                    TransactionType = c.LoaiCongNo,
                    PersonName      = c.TenNguoi,
                    Amount          = c.SoTien,
                    Status          = c.TrangThai,
                    Note            = c.NoiDung
                })
                .ToListAsync();
        }

        Console.WriteLine($"[DebtSuggestions] found={suggestions.Count}");
        return Ok(new { message = "Lấy gợi ý thành công", data = suggestions });
    }

    // -------------------------------------------------------------------------
    // POST /api/debts  — tạo khoản nợ mới
    // -------------------------------------------------------------------------
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDebtRequest request)
    {
        // 1. Validate model
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    k => char.ToLower(k.Key[0]) + k.Key[1..],
                    v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            Console.WriteLine($"[CreateDebt] ModelState invalid: {System.Text.Json.JsonSerializer.Serialize(errors)}");
            return BadRequest(new { message = "Dữ liệu không hợp lệ", errors });
        }

        // 2. Lấy userId từ token
        long userId;
        try { userId = GetCurrentUserId(); }
        catch { return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." }); }

        // 3. Resolve ngày phát sinh
        var todayVn = TimeZoneHelper.TodayVn();
        var occurredDate = request.OccurredDate?.Date ?? todayVn;
        if (occurredDate > todayVn)
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { occurredDate = new[] { "Ngày phát sinh không được lớn hơn ngày hiện tại." } }
            });

        // 4. Resolve hạn trả — null nếu user không nhập, KHÔNG tự sinh +7 ngày
        // "Nhắc nợ sau 7 ngày" là logic notification riêng, không liên quan HanTra
        var dueDate = request.DueDate?.Date;

        // Validate dueDate >= occurredDate (chỉ khi user có nhập)
        if (dueDate.HasValue && dueDate.Value < occurredDate)
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { dueDate = new[] { "Hạn trả không được nhỏ hơn ngày phát sinh." } }
            });

        // 5. Insert
        try
        {
            var entity = new CongNo
            {
                IdNguoiDung  = userId,
                TenNguoi     = request.PersonName.Trim(),
                SoTien       = request.Amount,
                LoaiCongNo   = request.TransactionType.ToUpper(),
                NoiDung      = request.Note,
                HanTra       = dueDate,   // null nếu user không nhập
                NgayPhatSinh = occurredDate,
                TrangThai    = "CHUA_TRA",
                DaXoa        = false
            };

            _db.CongNos.Add(entity);
            await _db.SaveChangesAsync();

            // 6. Tính tổng còn nợ của người này (bao gồm khoản vừa tạo)
            var outstanding = await GetOutstandingOfPersonAsync(userId, entity.TenNguoi);

            return StatusCode(201, new
            {
                message = entity.LoaiCongNo == "CHO_VAY"
                    ? "Đã ghi nhận khoản cho vay thành công."
                    : "Đã ghi nhận khoản nợ thành công.",
                data = new
                {
                    debtId          = entity.IdCongNo,
                    transactionType = entity.LoaiCongNo,
                    personName      = entity.TenNguoi,
                    amount          = entity.SoTien,
                    occurredDate    = occurredDate.ToString("yyyy-MM-dd"),
                    dueDate         = dueDate?.ToString("yyyy-MM-dd"),   // null nếu không nhập
                    note            = entity.NoiDung,
                    currentOutstandingOfPerson = outstanding
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CreateDebt] ERROR userId={UserId}", userId);
            return StatusCode(500, new { message = "Có lỗi xảy ra, vui lòng thử lại." });
        }
    }

    // -------------------------------------------------------------------------
    // PUT /api/debts/{id}
    // Cập nhật khoản công nợ. NgayPhatSinh giữ nguyên, không nhận từ request.
    // Validate: amount mới >= tổng đã thanh toán.
    // Recalc TrangThai sau khi đổi SoTien.
    // -------------------------------------------------------------------------
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateDebtRequest request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    k => char.ToLower(k.Key[0]) + k.Key[1..],
                    v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            return BadRequest(new { message = "Dữ liệu không hợp lệ", errors });
        }

        var userId = GetCurrentUserId();

        // 1. Load khoản nợ kèm lịch sử thanh toán
        var entity = await _db.CongNos
            .Include(c => c.ThanhToanCongNos)
            .FirstOrDefaultAsync(c => c.IdCongNo == id && c.IdNguoiDung == userId && !c.DaXoa);

        if (entity == null)
            return NotFound(new { message = "Khoản công nợ không tồn tại" });

        // 2. Tính tổng đã thanh toán
        var paidAmount = entity.ThanhToanCongNos.Sum(t => t.SoTienThanhToan);

        // 3. Validate amount mới >= paidAmount
        if (request.Amount < paidAmount)
            return BadRequest(new
            {
                message = "Số tiền mới không được nhỏ hơn tổng đã thanh toán",
                errors = new { amount = new[] { $"Số tiền tối thiểu là {paidAmount:N0}đ (đã thanh toán)" } }
            });

        // 4. Validate DueDate >= NgayPhatSinh (giữ nguyên rule hiện tại)
        if (request.DueDate.HasValue && request.DueDate.Value.Date < entity.NgayPhatSinh.Date)
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { dueDate = new[] { "Hạn trả không được nhỏ hơn ngày phát sinh." } }
            });

        try
        {
            // 5. Cập nhật các field được phép — NgayPhatSinh giữ nguyên
            entity.TenNguoi   = request.PersonName.Trim();
            entity.SoTien     = request.Amount;
            entity.LoaiCongNo = request.TransactionType.ToUpper();
            entity.NoiDung    = request.Note;
            entity.HanTra     = request.DueDate?.Date;

            // 6. Recalc TrangThai dựa trên paidAmount và SoTien mới
            var remaining = entity.SoTien - paidAmount;
            entity.TrangThai = remaining == 0 ? "DA_TRA"
                             : paidAmount > 0 ? "TRA_MOT_PHAN"
                                              : "CHUA_TRA";

            await _db.SaveChangesAsync();

            var message = entity.LoaiCongNo == "CHO_VAY"
                ? "Cập nhật khoản cho vay thành công"
                : "Cập nhật khoản nợ thành công";

            return Ok(new
            {
                message,
                data = new UpdateDebtResponseDto
                {
                    DebtId          = entity.IdCongNo,
                    TransactionType = entity.LoaiCongNo,
                    PersonName      = entity.TenNguoi,
                    Amount          = entity.SoTien,
                    PaidAmount      = paidAmount,
                    RemainingAmount = Math.Max(0, remaining),
                    Status          = entity.TrangThai,
                    OccurredDate    = entity.NgayPhatSinh.ToString("yyyy-MM-dd"),
                    DueDate         = entity.HanTra?.ToString("yyyy-MM-dd"),
                    Note            = entity.NoiDung
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpdateDebt] ERROR id={Id} userId={UserId}", id, userId);
            return StatusCode(500, new { message = "Không thể lưu khoản nợ này. Vui lòng kiểm tra lại thông tin." });
        }
    }

    // -------------------------------------------------------------------------
    // DELETE /api/debts/{id}
    // Xóa mềm khoản công nợ: cập nhật DaXoa = true.
    // Chỉ cho phép xóa khoản thuộc user hiện tại và chưa bị xóa.
    // Sau khi xóa, mark các ThongBao liên quan là đã đọc để FE không còn
    // deep-link tới khoản không tồn tại.
    // -------------------------------------------------------------------------
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)    {
        var userId = GetCurrentUserId();

        var entity = await _db.CongNos
            .FirstOrDefaultAsync(c => c.IdCongNo == id && c.IdNguoiDung == userId && !c.DaXoa);

        if (entity == null)
            return NotFound(new { message = "Khoản công nợ không tồn tại" });

        try
        {
            // Soft delete khoản công nợ
            entity.DaXoa = true;

            // Mark các ThongBao liên quan là đã đọc để FE xử lý gracefully
            var relatedNotifications = await _db.ThongBaos
                .Where(t => t.IdCongNo == id && t.IdNguoiDung == userId && !t.DaDoc)
                .ToListAsync();

            foreach (var n in relatedNotifications)
                n.DaDoc = true;

            await _db.SaveChangesAsync();

            var message = entity.LoaiCongNo == "CHO_VAY"
                ? "Xóa khoản cho vay thành công"
                : "Xóa khoản nợ thành công";

            return Ok(new { message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeleteDebt] ERROR id={Id} userId={UserId}", id, userId);
            return StatusCode(500, new { message = "Có lỗi xảy ra, vui lòng thử lại." });
        }
    }

    // -------------------------------------------------------------------------
    // GET /api/debts/{debtId}/payments
    // Trả summary + toàn bộ timeline (PAYMENT + ORIGINAL_DEBT), mới nhất trước.
    // -------------------------------------------------------------------------
    [HttpGet("{debtId}/payments")]
    public async Task<IActionResult> GetPayments(long debtId)
    {
        var userId = GetCurrentUserId();

        var debt = await _db.CongNos
            .Include(c => c.ThanhToanCongNos)
            .FirstOrDefaultAsync(c => c.IdCongNo == debtId && c.IdNguoiDung == userId && !c.DaXoa);

        if (debt == null)
            return NotFound(new { message = "Công nợ không tồn tại." });

        var paidAmount = debt.ThanhToanCongNos.Sum(t => t.SoTienThanhToan);
        var remainingAmount = Math.Max(0, debt.SoTien - paidAmount);

        // Status phản ánh thực tế tính toán, không chỉ dựa vào cột TrangThai
        var status = remainingAmount == 0 ? "DA_TRA"
                   : paidAmount > 0       ? "TRA_MOT_PHAN"
                                          : "CHUA_TRA";

        // Label helper theo loại công nợ
        var paymentLabel  = debt.LoaiCongNo == "CHO_VAY" ? "Thu tiền"      : "Trả tiền";
        var originalLabel = debt.LoaiCongNo == "CHO_VAY" ? "Tạo khoản vay" : "Tạo khoản nợ";

        // Build timeline: các bản ghi thanh toán sắp xếp mới nhất trước
        var items = debt.ThanhToanCongNos
            .OrderByDescending(t => t.NgayThanhToan)
            .ThenByDescending(t => t.IdThanhToan)
            .Select(t => new DebtTimelineItemDto
            {
                Type        = "PAYMENT",
                Label       = paymentLabel,
                PaymentId   = t.IdThanhToan,
                PaymentDate = t.NgayThanhToan,
                Amount      = t.SoTienThanhToan,
                Note        = t.GhiChu
            })
            .ToList();

        // Append item gốc ở cuối timeline (cũ nhất)
        items.Add(new DebtTimelineItemDto
        {
            Type        = "ORIGINAL_DEBT",
            Label       = originalLabel,
            PaymentId   = null,
            PaymentDate = debt.NgayPhatSinh,
            Amount      = debt.SoTien,
            Note        = "Khoản gốc"
        });

        var data = new DebtPaymentTimelineDto
        {
            DebtId          = debt.IdCongNo,
            TransactionType = debt.LoaiCongNo,
            PersonName      = debt.TenNguoi,
            OriginalAmount  = debt.SoTien,
            PaidAmount      = paidAmount,
            RemainingAmount = remainingAmount,
            Status          = status,
            Items           = items
        };

        return Ok(new { message = "Lấy lịch sử thanh toán thành công", data });
    }

    // -------------------------------------------------------------------------
    // POST /api/debts/{debtId}/payments
    // Ghi nhận 1 lần thanh toán. Dùng DB transaction để đảm bảo toàn vẹn.
    // paymentMethod nhận từ request nhưng không persist (schema chưa có cột).
    // -------------------------------------------------------------------------
    [HttpPost("{debtId}/payments")]
    public async Task<IActionResult> AddPayment(long debtId, [FromBody] AddPaymentRequest request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    k => char.ToLower(k.Key[0]) + k.Key[1..],
                    v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            return BadRequest(new { message = "Dữ liệu không hợp lệ", errors });
        }

        var userId = GetCurrentUserId();

        // Load khoản nợ kèm lịch sử thanh toán hiện có
        var debt = await _db.CongNos
            .Include(c => c.ThanhToanCongNos)
            .FirstOrDefaultAsync(c => c.IdCongNo == debtId && c.IdNguoiDung == userId && !c.DaXoa);

        if (debt == null)
            return NotFound(new { message = "Công nợ không tồn tại." });

        // Tính dư nợ hiện tại trước giao dịch
        var currentPaid = debt.ThanhToanCongNos.Sum(t => t.SoTienThanhToan);
        var currentRemaining = Math.Max(0, debt.SoTien - currentPaid);

        // Validate: không được vượt quá dư nợ hiện tại
        if (request.Amount > currentRemaining)
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { amount = new[] { "Số tiền vượt quá dư nợ hiện tại" } }
            });

        // Resolve ngày thanh toán — default hôm nay (giờ VN)
        var paymentDate = request.PaymentDate?.Date ?? TimeZoneHelper.TodayVn();

        try
        {
            // 1. Insert bản ghi thanh toán mới
            var payment = new ThanhToanCongNo
            {
                IdCongNo        = debtId,
                SoTienThanhToan = request.Amount,
                NgayThanhToan   = paymentDate,
                GhiChu          = request.Note
            };
            _db.ThanhToanCongNos.Add(payment);

            // 2. Tính lại sau giao dịch
            var newTotalPaid      = currentPaid + request.Amount;
            var newRemainingAmount = Math.Max(0, debt.SoTien - newTotalPaid);

            // 3. Cập nhật TrangThai
            debt.TrangThai = newRemainingAmount == 0 ? "DA_TRA"
                           : newTotalPaid > 0        ? "TRA_MOT_PHAN"
                                                     : "CHUA_TRA";


            Console.WriteLine(
                $"[Payment] debtId={debtId} debtType={debt.LoaiCongNo} " +
                $"amount={request.Amount} newRemaining={newRemainingAmount} status={debt.TrangThai}");

            await _db.SaveChangesAsync();

            return StatusCode(201, new
            {
                message = "Ghi nhận thanh toán thành công",
                data = new AddPaymentResponseDto
                {
                    PaymentId       = payment.IdThanhToan,
                    DebtId          = debtId,
                    PaidAmount      = request.Amount,
                    TotalPaidAmount = newTotalPaid,
                    RemainingAmount = newRemainingAmount,
                    Status          = debt.TrangThai
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Payment] ERROR debtId={debtId}: {ex.Message}");
            return StatusCode(500, new { message = "Có lỗi xảy ra, vui lòng thử lại." });
        }
    }
}
