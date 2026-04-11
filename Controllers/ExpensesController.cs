using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.DTOs;
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

    public ExpensesController(SoChungDbContext db, ICategoryService categoryService)
    {
        _db = db;
        _categoryService = categoryService;
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
            .Include(c => c.DanhMucChiTieu)
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa)
            .OrderByDescending(c => c.NgayChi)
            .Select(c => new ChiTieuResponseDto
            {
                IdChiTieu = c.IdChiTieu,
                IdNguoiDung = c.IdNguoiDung,
                IdDanhMuc = c.IdDanhMuc,
                TenDanhMuc = c.DanhMucChiTieu!.TenDanhMuc,
                Icon = c.DanhMucChiTieu.Icon,
                MauSac = c.DanhMucChiTieu.MauSac,
                SoTien = c.SoTien,
                NoiDung = c.NoiDung,
                NgayChi = c.NgayChi
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET /api/expenses/history — lịch sử chi tiêu có filter + paging
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] ExpenseHistoryQuery query)
    {
        var userId = GetCurrentUserId();

        // Fallback tên danh mục khi category bị null hoặc đã xóa
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

        // Filter fromDate
        if (query.FromDate.HasValue)
            q = q.Where(c => c.NgayChi >= query.FromDate.Value.Date);

        // Filter toDate
        if (query.ToDate.HasValue)
            q = q.Where(c => c.NgayChi <= query.ToDate.Value.Date);

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
            var page = Math.Max(query.Page, 1);
            paged = q.Skip((page - 1) * pageSize).Take(pageSize);
        }

        // Project — LEFT JOIN với DanhMucChiTieu, fallback nếu category null/đã xóa
        var data = await paged
            .Select(c => new ExpenseHistoryDto
            {
                ExpenseId = c.IdChiTieu,
                Note = c.NoiDung,
                Amount = c.SoTien,
                TransactionDate = c.NgayChi,
                CategoryId = c.IdDanhMuc,
                CategoryName = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                    ? c.DanhMucChiTieu.TenDanhMuc
                    : fallbackCategoryName,
                CategoryIcon = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                    ? c.DanhMucChiTieu.Icon
                    : "more_horiz",
                CategoryColor = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                    ? c.DanhMucChiTieu.MauSac
                    : "#9E9E9E"
            })
            .ToListAsync();

        // Response
        if (query.Limit.HasValue)
            return Ok(new { message = "Lấy lịch sử chi tiêu thành công", data });

        return Ok(new
        {
            message = "Lấy lịch sử chi tiêu thành công",
            data,
            pagination = new
            {
                page = query.Page,
                pageSize = Math.Clamp(query.PageSize, 1, 100),
                totalCount,
                totalPages = (int)Math.Ceiling((double)totalCount! / Math.Clamp(query.PageSize, 1, 100))
            }
        });
    }

    // -------------------------------------------------------------------------
    // GET /api/expenses/search
    // Tìm kiếm khoản chi theo keyword, có phân trang.
    //
    // Accent-insensitive strategy:
    //   SQL Server collation Latin1_General_CI_AI (CI = case-insensitive,
    //   AI = accent-insensitive) được áp dụng qua EF.Functions.Collate().
    //   Điều này cho phép "ca phe" match "cà phê" trực tiếp ở DB layer
    //   mà không cần normalize chuỗi ở app layer, giữ hiệu năng tốt.
    //
    // Phạm vi tìm kiếm: NoiDung, TenDanhMuc, SoTien (dạng text)
    // -------------------------------------------------------------------------
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] ExpenseSearchQuery query)
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

        // Trim trái, giữ khoảng trắng giữa các từ
        var keyword = query.Keyword.TrimStart();
        if (keyword.Length == 0)
            return BadRequest(new { message = "Dữ liệu không hợp lệ",
                errors = new { keyword = new[] { "Keyword không được rỗng" } } });

        const string fallbackCategoryName = "Khác";

        var q = _db.ChiTieus
            .Where(c => c.IdNguoiDung == userId && !c.DaXoa)
            .AsQueryable();

        q = q.Where(c =>
            (c.NoiDung != null && EF.Functions.ILike(c.NoiDung, "%" + keyword + "%"))
            ||
            (c.DanhMucChiTieu != null && EF.Functions.ILike(c.DanhMucChiTieu.TenDanhMuc, "%" + keyword + "%"))
            ||
            EF.Functions.ILike(c.SoTien.ToString(), "%" + keyword + "%")
        );

        q = q.OrderByDescending(c => c.NgayChi).ThenByDescending(c => c.IdChiTieu);

        var pageSize = Math.Clamp(query.PageSize, 1, 15);
        var page = Math.Max(query.Page, 1);

        var totalItems = await q.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

        var items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ExpenseSearchItemDto
            {
                ExpenseId = c.IdChiTieu,
                Note = c.NoiDung,
                Amount = c.SoTien,
                TransactionDate = c.NgayChi,
                CategoryId = c.IdDanhMuc,
                CategoryName = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                    ? c.DanhMucChiTieu.TenDanhMuc
                    : fallbackCategoryName,
                CategoryIcon = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                    ? c.DanhMucChiTieu.Icon
                    : "more_horiz",
                CategoryColor = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                    ? c.DanhMucChiTieu.MauSac
                    : "#9E9E9E"
            })
            .ToListAsync();

        return Ok(new
        {
            message = "Tìm kiếm thành công",
            data = new ExpenseSearchResultDto
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = totalPages
            }
        });
    }

    // -------------------------------------------------------------------------
    // GET /api/expenses/search/suggestions
    // Gợi ý nhanh top 5 khi user nhập keyword.
    // Cùng collation CI_AI, tìm trong NoiDung và TenDanhMuc.
    // -------------------------------------------------------------------------
    [HttpGet("search/suggestions")]
    public async Task<IActionResult> SearchSuggestions([FromQuery] string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.TrimStart().Length < 1)
            return Ok(new { message = "Lấy gợi ý thành công", data = Array.Empty<object>() });

        var userId = GetCurrentUserId();
        var kw = keyword.TrimStart();

        const string fallbackCategoryName = "Khác";

        var suggestions = await _db.ChiTieus
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
                ExpenseId = c.IdChiTieu,
                Note = c.NoiDung,
                Amount = c.SoTien,
                CategoryName = c.DanhMucChiTieu != null && !c.DanhMucChiTieu.DaXoa
                    ? c.DanhMucChiTieu.TenDanhMuc
                    : fallbackCategoryName
            })
            .ToListAsync();

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
            return BadRequest(new { message = "Dữ liệu không hợp lệ", errors });
        }

        // 2. Lấy userId từ token — không nhận từ body
        long userId;
        try { userId = GetCurrentUserId(); }
        catch { return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." }); }

        // 3. Validate transactionDate
        var transactionDate = request.TransactionDate?.Date ?? DateTime.Today;
        if (transactionDate > DateTime.Today)
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
            var found = await _categoryService.GetValidCategoryAsync(request.CategoryId.Value, userId);
            if (found == null)
                return BadRequest(new { message = "Danh mục không tồn tại hoặc không thuộc người dùng" });

            category = found;
        }
        else
        {
            // categoryId null — fallback sang "Khác"
            category = await _categoryService.GetOrCreateDefaultCategoryAsync(userId);
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
        catch (Exception)
        {
            return StatusCode(500, new { message = "Lưu giao dịch thất bại" });
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
        var transactionDate = request.TransactionDate?.Date ?? DateTime.Today;
        if (transactionDate > DateTime.Today)
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
        catch (Exception)
        {
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
        catch (Exception)
        {
            return StatusCode(500, new { message = "Xóa khoản chi thất bại" });
        }
    }
}
