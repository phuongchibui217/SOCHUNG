using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Helpers;
using ExpenseManagerAPI.Models;
using ExpenseManagerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExpenseManagerAPI.Controllers;

[ApiController]
[Route("api/expenses")]
[Authorize]
public class ExpensesController : ControllerBase
{
    private readonly SoChungDbContext _db;
    private readonly ICategoryService _categoryService;
    private readonly ILogger<ExpensesController> _logger;

    public ExpensesController(SoChungDbContext db, ICategoryService categoryService, ILogger<ExpensesController> logger)
    {
        _db = db;
        _categoryService = categoryService;
        _logger = logger;
    }

    // Lấy userId từ JWT claim "sub"
    private long GetCurrentUserId() =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException("Không xác định được người dùng."));

    // GET /api/expenses  — lấy danh sách chi tiêu của user hiện tại
    [HttpGet]
    public async Task<IActionResult> GetMyExpenses()
    {
        var userId = GetCurrentUserId();

        var list = await _db.ChiTieus
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa)
            .OrderByDescending(c => c.NgayChi)
            .Select(c => new ChiTieuResponseDto
            {
                IdChiTieu   = c.IdChiTieu,
                IdNguoiDung = c.IdNguoiDung,
                IdDanhMuc   = c.IdDanhMuc,
                TenDanhMuc  = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                                  ? c.DanhMucChiTieu.TenDanhMuc : "Khác",
                Icon        = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                                  ? c.DanhMucChiTieu.Icon : "more_horiz",
                MauSac      = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                                  ? c.DanhMucChiTieu.MauSac : "#9E9E9E",
                SoTien      = c.SoTien,
                NoiDung     = c.NoiDung,
                NgayChi     = c.NgayChi
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET /api/expenses/history — lịch sử chi tiêu có filter + paging
    // Hỗ trợ 2 cách filter thời gian:
    //   Cách 1: fromDate + toDate (truyền thẳng range)
    //   Cách 2: mode + date (BE tự tính range)
    //     mode=day   → đúng ngày date
    //     mode=week  → thứ Hai đến Chủ Nhật của tuần chứa date
    //     mode=month → ngày 1 đến cuối tháng chứa date
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] ExpenseHistoryQuery query)
    {
        var userId = GetCurrentUserId();
        const string fallbackCategoryName = "Khác";

        var q = _db.ChiTieus
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa)
            .AsQueryable();

        // Filter keyword (tìm trong NoiDung)
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(c => c.NoiDung != null && c.NoiDung.Contains(kw));
        }

        // Tính fromDate/toDate từ mode+date nếu có, ưu tiên hơn fromDate/toDate trực tiếp
        DateTime? fromDate = query.FromDate;
        DateTime? toDate   = query.ToDate;

        if (!string.IsNullOrEmpty(query.Mode) && query.Date.HasValue)
        {
            var anchor = query.Date.Value.Date;
            switch (query.Mode.ToLower())
            {
                case "day":
                    fromDate = anchor;
                    toDate   = anchor;
                    break;
                case "week":
                    // Tuần bắt đầu từ thứ Hai (ISO week)
                    var dow      = (int)anchor.DayOfWeek; // 0=Sun,1=Mon,...,6=Sat
                    var daysToMon = dow == 0 ? 6 : dow - 1;
                    fromDate = anchor.AddDays(-daysToMon);
                    toDate   = fromDate.Value.AddDays(6);
                    break;
                case "month":
                    fromDate = new DateTime(anchor.Year, anchor.Month, 1);
                    toDate   = fromDate.Value.AddMonths(1).AddDays(-1);
                    break;
            }
            Console.WriteLine($"[History] mode={query.Mode} anchor={anchor:yyyy-MM-dd} from={fromDate:yyyy-MM-dd} to={toDate:yyyy-MM-dd}");
        }

        if (fromDate.HasValue)
            q = q.Where(c => c.NgayChi >= fromDate.Value.Date);

        if (toDate.HasValue)
            q = q.Where(c => c.NgayChi <= toDate.Value.Date);

        // Filter theo danh mục
        if (query.CategoryId.HasValue)
            q = q.Where(c => c.IdDanhMuc == query.CategoryId.Value);

        // Sắp xếp mới nhất trước
        q = q.OrderByDescending(c => c.NgayChi).ThenByDescending(c => c.IdChiTieu);

        // Đếm tổng (chỉ khi không dùng limit)
        int? totalCount = null;
        if (!query.Limit.HasValue)
            totalCount = await q.CountAsync();

        // Lấy dữ liệu: limit hoặc paging
        IQueryable<ChiTieu> paged;
        if (query.Limit.HasValue)
        {
            paged = q.Take(query.Limit.Value);
        }
        else
        {
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var page     = Math.Max(query.Page, 1);
            paged = q.Skip((page - 1) * pageSize).Take(pageSize);
        }

        // Project — TransactionDate trả string yyyy-MM-dd, không có time component
        var raw = await paged
            .Select(c => new
            {
                c.IdChiTieu,
                c.NoiDung,
                c.SoTien,
                c.NgayChi,
                c.IdDanhMuc,
                CatName  = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa ? c.DanhMucChiTieu.TenDanhMuc : fallbackCategoryName,
                CatIcon  = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa ? c.DanhMucChiTieu.Icon : "more_horiz",
                CatColor = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa ? c.DanhMucChiTieu.MauSac : "#9E9E9E"
            })
            .ToListAsync();

        var data = raw.Select(c => new ExpenseHistoryDto
        {
            ExpenseId       = c.IdChiTieu,
            Note            = c.NoiDung,
            Amount          = c.SoTien,
            TransactionDate = c.NgayChi.ToString("yyyy-MM-dd"),
            CategoryId      = c.IdDanhMuc,
            CategoryName    = c.CatName ?? fallbackCategoryName,
            CategoryIcon    = c.CatIcon,
            CategoryColor   = c.CatColor
        }).ToList();

        // Response
        if (query.Limit.HasValue)
            return Ok(new { message = "Lấy lịch sử chi tiêu thành công", data });

        return Ok(new
        {
            message = "Lấy lịch sử chi tiêu thành công",
            data,
            pagination = new
            {
                page      = query.Page,
                pageSize  = Math.Clamp(query.PageSize, 1, 100),
                totalCount,
                totalPages = (int)Math.Ceiling((double)totalCount! / Math.Clamp(query.PageSize, 1, 100))
            }
        });
    }

    // -------------------------------------------------------------------------
    // GET /api/expenses/search
    // Tìm kiếm realtime theo keyword, có phân trang (max 15/page).
    //
    // Accent-insensitive + case-insensitive strategy (PostgreSQL):
    //   Dùng unaccent() extension (có sẵn trên Supabase) kết hợp ILIKE.
    //   unaccent("nhà") = "nha" → "nha" ILIKE "%nha%" sẽ match.
    //   Áp dụng cả cho keyword lẫn column để đảm bảo 2 chiều.
    //
    // Phạm vi tìm kiếm: NoiDung (title), TenDanhMuc (category), SoTien (amount)
    // Keyword rỗng → trả toàn bộ (FE xử lý recent transactions)
    // -------------------------------------------------------------------------
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] ExpenseSearchQuery query)
    {
        var userId = GetCurrentUserId();

        // Trim trái, giữ khoảng trắng giữa các từ
        var keyword = (query.Keyword ?? string.Empty).TrimStart();

        Console.WriteLine($"[ExpenseSearch] userId={userId} keyword='{keyword}' page={query.Page} pageSize={query.PageSize}");

        const string fallbackCategoryName = "Khác";

        var pageSize = Math.Clamp(query.PageSize, 1, 15);
        var page     = Math.Max(query.Page, 1);

        var q = _db.ChiTieus
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa)
            .AsQueryable();

        var totalBefore = await q.CountAsync();
        Console.WriteLine($"[ExpenseSearch] totalBefore filter={totalBefore}");

        int totalItems;
        List<ExpenseSearchItemDto> items;

        if (!string.IsNullOrEmpty(keyword))
        {
            var pattern = "%" + keyword + "%";

            // Thử unaccent() trước — accent-insensitive + case-insensitive
            // Fallback sang ILike thường nếu extension chưa enable (không crash 500)
            IQueryable<ChiTieu> filtered;
            try
            {
                filtered = q.Where(c =>
                    (c.NoiDung != null &&
                        EF.Functions.ILike(AppDb.Unaccent(c.NoiDung), AppDb.Unaccent(pattern)))
                    ||
                    (c.DanhMucChiTieu != null &&
                        EF.Functions.ILike(AppDb.Unaccent(c.DanhMucChiTieu.TenDanhMuc), AppDb.Unaccent(pattern)))
                    ||
                    EF.Functions.ILike(c.SoTien.ToString(), pattern)
                );
                // Probe COUNT để phát hiện lỗi unaccent trước khi fetch data
                totalItems = await filtered.CountAsync();
                Console.WriteLine($"[ExpenseSearch] mode=unaccent totalAfter={totalItems}");
            }
            catch (Exception ex) when (
                ex.Message.Contains("unaccent") ||
                ex.InnerException?.Message.Contains("unaccent") == true)
            {
                Console.WriteLine($"[ExpenseSearch] unaccent unavailable, fallback ILike: {ex.Message}");
                filtered = q.Where(c =>
                    (c.NoiDung != null && EF.Functions.ILike(c.NoiDung, pattern))
                    ||
                    (c.DanhMucChiTieu != null && EF.Functions.ILike(c.DanhMucChiTieu.TenDanhMuc, pattern))
                    ||
                    EF.Functions.ILike(c.SoTien.ToString(), pattern)
                );
                totalItems = await filtered.CountAsync();
                Console.WriteLine($"[ExpenseSearch] mode=ilike totalAfter={totalItems}");
            }

            items = await filtered
                .OrderByDescending(c => c.NgayChi).ThenByDescending(c => c.IdChiTieu)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new
                {
                    c.IdChiTieu, c.NoiDung, c.SoTien, c.NgayChi, c.IdDanhMuc,
                    CatName  = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa ? c.DanhMucChiTieu.TenDanhMuc : fallbackCategoryName,
                    CatIcon  = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa ? c.DanhMucChiTieu.Icon : "more_horiz",
                    CatColor = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa ? c.DanhMucChiTieu.MauSac : "#9E9E9E"
                })
                .ToListAsync()
                .ContinueWith(t => t.Result.Select(c => new ExpenseSearchItemDto
                {
                    ExpenseId       = c.IdChiTieu,
                    Note            = c.NoiDung,
                    Amount          = c.SoTien,
                    TransactionDate = c.NgayChi.ToString("yyyy-MM-dd"),
                    CategoryId      = c.IdDanhMuc,
                    CategoryName    = c.CatName ?? fallbackCategoryName,
                    CategoryIcon    = c.CatIcon,
                    CategoryColor   = c.CatColor
                }).ToList());
        }
        else
        {
            // Keyword rỗng → trả toàn bộ, không filter
            var sorted = q.OrderByDescending(c => c.NgayChi).ThenByDescending(c => c.IdChiTieu);
            totalItems = await sorted.CountAsync();
            items = await sorted
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new
                {
                    c.IdChiTieu, c.NoiDung, c.SoTien, c.NgayChi, c.IdDanhMuc,
                    CatName  = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa ? c.DanhMucChiTieu.TenDanhMuc : fallbackCategoryName,
                    CatIcon  = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa ? c.DanhMucChiTieu.Icon : "more_horiz",
                    CatColor = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa ? c.DanhMucChiTieu.MauSac : "#9E9E9E"
                })
                .ToListAsync()
                .ContinueWith(t => t.Result.Select(c => new ExpenseSearchItemDto
                {
                    ExpenseId       = c.IdChiTieu,
                    Note            = c.NoiDung,
                    Amount          = c.SoTien,
                    TransactionDate = c.NgayChi.ToString("yyyy-MM-dd"),
                    CategoryId      = c.IdDanhMuc,
                    CategoryName    = c.CatName ?? fallbackCategoryName,
                    CategoryIcon    = c.CatIcon,
                    CategoryColor   = c.CatColor
                }).ToList());
        }

        var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
        Console.WriteLine($"[ExpenseSearch] returned={items.Count} totalPages={totalPages}");

        return Ok(new
        {
            message = "Tìm kiếm thành công",
            data = new ExpenseSearchResultDto
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
    // GET /api/expenses/search/suggestions
    // Gợi ý nhanh top 5 khi user nhập keyword.
    // Accent-insensitive qua unaccent(), case-insensitive qua ILIKE.
    // Keyword rỗng → trả [] ngay, không query DB.
    // -------------------------------------------------------------------------
    [HttpGet("search/suggestions")]
    public async Task<IActionResult> SearchSuggestions([FromQuery] string? keyword)
    {
        var kw = keyword?.TrimStart() ?? string.Empty;

        if (kw.Length == 0)
            return Ok(new { message = "Lấy gợi ý thành công", data = Array.Empty<object>() });

        var userId = GetCurrentUserId();
        Console.WriteLine($"[ExpenseSuggestions] userId={userId} keyword='{kw}'");

        const string fallbackCategoryName = "Khác";

        List<ExpenseSuggestionDto> suggestions;
        try
        {
            suggestions = await _db.ChiTieus
                .Where(c => c.IdNguoiDung == userId && !c.DaXoa &&
                    (
                        (c.NoiDung != null &&
                            EF.Functions.ILike(AppDb.Unaccent(c.NoiDung), AppDb.Unaccent("%" + kw + "%")))
                        ||
                        (c.DanhMucChiTieu != null &&
                            EF.Functions.ILike(AppDb.Unaccent(c.DanhMucChiTieu.TenDanhMuc), AppDb.Unaccent("%" + kw + "%")))
                    ))
                .OrderByDescending(c => c.NgayChi)
                .Take(5)
                .Select(c => new ExpenseSuggestionDto
                {
                    ExpenseId    = c.IdChiTieu,
                    Note         = c.NoiDung,
                    Amount       = c.SoTien,
                    CategoryName = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                        ? c.DanhMucChiTieu.TenDanhMuc : fallbackCategoryName
                })
                .ToListAsync();
        }
        catch
        {
            // Fallback nếu unaccent không khả dụng
            suggestions = await _db.ChiTieus
                .Where(c => c.IdNguoiDung == userId && !c.DaXoa &&
                    (
                        (c.NoiDung != null && EF.Functions.ILike(c.NoiDung, "%" + kw + "%"))
                        ||
                        (c.DanhMucChiTieu != null && EF.Functions.ILike(c.DanhMucChiTieu.TenDanhMuc, "%" + kw + "%"))
                    ))
                .OrderByDescending(c => c.NgayChi)
                .Take(5)
                .Select(c => new ExpenseSuggestionDto
                {
                    ExpenseId    = c.IdChiTieu,
                    Note         = c.NoiDung,
                    Amount       = c.SoTien,
                    CategoryName = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                        ? c.DanhMucChiTieu.TenDanhMuc : fallbackCategoryName
                })
                .ToListAsync();
        }

        Console.WriteLine($"[ExpenseSuggestions] found={suggestions.Count}");
        return Ok(new { message = "Lấy gợi ý thành công", data = suggestions });
    }

    // POST /api/expenses  — tạo khoản chi mới
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExpenseRequest request)
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
            Console.WriteLine($"[CreateExpense] ModelState invalid: {System.Text.Json.JsonSerializer.Serialize(errors)}");
            return BadRequest(new { message = "Dữ liệu không hợp lệ", errors });
        }

        // 2. Lấy userId từ token — không nhận từ body
        long userId;
        try { userId = GetCurrentUserId(); }
        catch { return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." }); }

        // 3. Validate transactionDate
        var todayVn = TimeZoneHelper.TodayVn();
        var transactionDate = request.TransactionDate?.Date ?? todayVn;
        if (transactionDate > todayVn)
        {
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { transactionDate = new[] { "Không được chọn ngày trong tương lai" } }
            });
        }

        // 4. Xử lý category
        DanhMucChiTieu category;

        if (request.CategoryId.HasValue)
        {
            // categoryId được cung cấp — kiểm tra hợp lệ
            DanhMucChiTieu? found;
            try
            {
                found = await _categoryService.GetValidCategoryAsync(request.CategoryId.Value, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CreateExpense] GetValidCategoryAsync failed userId={UserId} categoryId={CategoryId}", userId, request.CategoryId.Value);
                return StatusCode(500, new { message = "Lỗi khi kiểm tra danh mục", debug = ex.Message });
            }

            if (found == null)
                return BadRequest(new { message = "Danh mục không tồn tại hoặc không thuộc người dùng" });

            category = found;
        }
        else
        {
            // categoryId null — fallback sang "Khác"
            try
            {
                category = await _categoryService.GetOrCreateDefaultCategoryAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CreateExpense] GetOrCreateDefaultCategoryAsync failed userId={UserId}", userId);
                return StatusCode(500, new { message = "Lỗi khi lấy danh mục mặc định", debug = ex.Message });
            }
        }

        // 5. Insert ChiTieu
        try
        {
            var entity = new ChiTieu
            {
                IdNguoiDung = userId,
                IdDanhMuc = category.IdDanhMuc,
                SoTien = request.Amount,
                NoiDung = request.Note,
                NgayChi = transactionDate,
                DaXoa = false
            };

            _db.ChiTieus.Add(entity);
            await _db.SaveChangesAsync();

            return StatusCode(201, new
            {
                message = "Thêm mới giao dịch thành công",
                data = new CreateExpenseResponse
                {
                    ExpenseId = entity.IdChiTieu,
                    Amount = entity.SoTien,
                    CategoryId = category.IdDanhMuc,
                    CategoryName = category.TenDanhMuc,
                    TransactionDate = transactionDate.ToString("yyyy-MM-dd"),
                    Note = entity.NoiDung
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CreateExpense] ERROR userId={userId} categoryId={category.IdDanhMuc} amount={request.Amount}: {ex}");
            return StatusCode(500, new { message = "Lưu giao dịch thất bại", debug = ex.Message });
        }
    }

    // GET /api/expenses/{id} — load detail 1 khoản chi (dùng trước khi edit)
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var userId = GetCurrentUserId();

        var entity = await _db.ChiTieus
            .Include(c => c.DanhMucChiTieu)
            .FirstOrDefaultAsync(c => c.IdChiTieu == id && c.IdNguoiDung == userId && !c.DaXoa);

        if (entity == null)
            return NotFound(new { message = "Khoản chi không tồn tại" });

        const string fallbackName  = "Khác";
        const string fallbackIcon  = "more_horiz";
        const string fallbackColor = "#9E9E9E";

        var cat = entity.DanhMucChiTieu;
        var validCat = cat != null && !cat.DaXoa;

        return Ok(new
        {
            message = "Lấy chi tiết khoản chi thành công",
            data = new ExpenseDetailDto
            {
                ExpenseId       = entity.IdChiTieu,
                Amount          = entity.SoTien,
                CategoryId      = entity.IdDanhMuc,
                CategoryName    = validCat ? cat!.TenDanhMuc  : fallbackName,
                CategoryIcon    = validCat ? cat!.Icon        : fallbackIcon,
                CategoryColor   = validCat ? cat!.MauSac      : fallbackColor,
                TransactionDate = entity.NgayChi.ToString("yyyy-MM-dd"),
                Note            = entity.NoiDung
            }
        });
    }

    // PUT /api/expenses/{id} — cập nhật khoản chi
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateExpenseRequest request)
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

        // 1. Tìm khoản chi — chỉ của user hiện tại, chưa xóa
        var entity = await _db.ChiTieus
            .FirstOrDefaultAsync(c => c.IdChiTieu == id && c.IdNguoiDung == userId && !c.DaXoa);

        if (entity == null)
            return NotFound(new { message = "Khoản chi không tồn tại" });

        // 2. Validate transactionDate
        var todayVn = TimeZoneHelper.TodayVn();
        var transactionDate = request.TransactionDate?.Date ?? todayVn;
        if (transactionDate > todayVn)
            return BadRequest(new
            {
                message = "Dữ liệu không hợp lệ",
                errors = new { transactionDate = new[] { "Không được chọn ngày trong tương lai" } }
            });

        // 3. Resolve category
        DanhMucChiTieu category;
        if (request.CategoryId.HasValue)
        {
            var found = await _categoryService.GetValidCategoryAsync(request.CategoryId.Value, userId);
            if (found == null)
                return BadRequest(new { message = "Danh mục không tồn tại hoặc không thuộc người dùng" });
            category = found;
        }
        else
        {
            // categoryId null → fallback "Khác"
            category = await _categoryService.GetOrCreateDefaultCategoryAsync(userId);
        }

        // 4. Cập nhật
        try
        {
            entity.SoTien   = request.Amount;
            entity.IdDanhMuc = category.IdDanhMuc;
            entity.NgayChi  = transactionDate;
            entity.NoiDung  = request.Note;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Cập nhật khoản chi thành công",
                data = new ExpenseDetailDto
                {
                    ExpenseId       = entity.IdChiTieu,
                    Amount          = entity.SoTien,
                    CategoryId      = category.IdDanhMuc,
                    CategoryName    = category.TenDanhMuc,
                    CategoryIcon    = category.Icon,
                    CategoryColor   = category.MauSac,
                    TransactionDate = transactionDate.ToString("yyyy-MM-dd"),
                    Note            = entity.NoiDung
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpdateExpense] ERROR id={Id} userId={UserId}", id, userId);
            return StatusCode(500, new { message = "Không thể cập nhật giao dịch. Vui lòng kiểm tra lại thông tin." });
        }
    }

    // DELETE /api/expenses/{id} — xóa mềm khoản chi (DaXoa = 1)
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        var userId = GetCurrentUserId();

        var entity = await _db.ChiTieus
            .FirstOrDefaultAsync(c => c.IdChiTieu == id && c.IdNguoiDung == userId && !c.DaXoa);

        if (entity == null)
            return NotFound(new { message = "Khoản chi không tồn tại" });

        try
        {
            entity.DaXoa = true;
            await _db.SaveChangesAsync();
            return Ok(new { message = "Xóa khoản chi thành công" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeleteExpense] ERROR id={Id} userId={UserId}", id, userId);
            return StatusCode(500, new { message = "Xóa khoản chi thất bại" });
        }
    }
}
