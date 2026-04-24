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
[Route("api/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly SoChungDbContext _db;
    private readonly ICategoryService _categoryService;
    private readonly ILogger<CategoriesController> _logger;

    public CategoriesController(SoChungDbContext db, ICategoryService categoryService, ILogger<CategoriesController> logger)
    {
        _db = db;
        _categoryService = categoryService;
        _logger = logger;
    }

    private long GetCurrentUserId() =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException());

    // GET /api/categories          — màn hình "Thêm Giao Dịch"
    // GET /api/categories?type=expense  — tương đương
    [HttpGet]
    public async Task<IActionResult> GetMyCategories([FromQuery] string? type)
    {
        var userId = GetCurrentUserId();

        // Đảm bảo "Khác" tồn tại + tính transactionCount trực tiếp từ DB
        var data = await _categoryService.GetCategoriesForPickerAsync(userId);

        return Ok(data);
    }

    // GET /api/categories/{userId}  — giữ lại để tương thích
    [HttpGet("{userId}")]
    public async Task<IActionResult> GetByUser(long userId)
    {
        var list = await _db.DanhMucChiTieus
            .Where(d => (d.IdNguoiDung == userId || d.IdNguoiDung == null) && !d.DaXoa)
            .Select(d => new DanhMucResponseDto
            {
                IdDanhMuc = d.IdDanhMuc,
                IdNguoiDung = d.IdNguoiDung,
                TenDanhMuc = d.TenDanhMuc,
                Icon = d.Icon,
                MauSac = d.MauSac,
                TransactionCount = d.ChiTieus.Count(c => c.IdNguoiDung == userId && !c.DaXoa)
            })
            .ToListAsync();

        return Ok(list);
    }

    // POST /api/categories
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        // 1. Validate model (name bắt buộc, maxlength)
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

        // 2. Lấy userId từ token
        long userId;
        try { userId = GetCurrentUserId(); }
        catch { return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." }); }

        // 3. Delegate toàn bộ business logic xuống service
        try
        {
            var (entity, error) = await _categoryService.CreateCategoryAsync(
                userId, request.Name, request.Icon, request.Color);

            if (error != null)
                return Conflict(new { message = error });

            return StatusCode(201, new
            {
                message = "Thêm danh mục thành công",
                data = new CreateCategoryResponse
                {
                    IdDanhMuc = entity!.IdDanhMuc,
                    TenDanhMuc = entity.TenDanhMuc,
                    Icon = entity.Icon,
                    MauSac = entity.MauSac
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CreateCategory] ERROR userId={UserId} name='{Name}'", userId, request.Name);
            return StatusCode(500, new { message = "Thêm mới danh mục thất bại" });
        }
    }

    // PUT /api/categories/{id} — cập nhật danh mục tự tạo của user
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateCategoryRequest request)
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
        Console.WriteLine($"[Category][Update] id={id} userId={userId} name='{request.Name}'");

        // 1. Tìm category theo id — không filter user/DaXoa trước để phân biệt rõ lỗi
        var entity = await _db.DanhMucChiTieus
            .FirstOrDefaultAsync(d => d.IdDanhMuc == id);

        if (entity == null)
        {
            Console.WriteLine($"[Category][Update] NOT FOUND id={id}");
            return NotFound(new { message = "Danh mục không tồn tại" });
        }

        if (entity.DaXoa)
        {
            Console.WriteLine($"[Category][Update] DELETED id={id}");
            return NotFound(new { message = "Danh mục đã bị xoá" });
        }

        // Cho phép: danh mục chung (IdNguoiDung == null) hoặc danh mục của chính user
        if (entity.IdNguoiDung != null && entity.IdNguoiDung != userId)
        {
            Console.WriteLine($"[Category][Update] FORBIDDEN id={id} owner={entity.IdNguoiDung} caller={userId}");
            return StatusCode(403, new { message = "Không có quyền chỉnh sửa danh mục này" });
        }

        // 2. Chặn sửa danh mục mặc định "Khác"
        if (entity.TenDanhMuc.Equals("Khác", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Không thể chỉnh sửa danh mục mặc định" });

        var trimmedName = request.Name.Trim();
        var trimmedNameLower = trimmedName.ToLower();
        bool isSystemCategory = entity.IdNguoiDung == null;

        Console.WriteLine($"[Category][Update] isSystemCategory={isSystemCategory} owner={entity.IdNguoiDung} caller={userId}");

        // Với category hệ thống: tìm override hiện có của user (nếu có)
        DanhMucChiTieu? existingOverride = null;
        if (isSystemCategory)
        {
            existingOverride = await _db.DanhMucChiTieus
                .FirstOrDefaultAsync(d => d.IdNguoiDung == userId && d.IdDanhMucGoc == id && !d.DaXoa);
            Console.WriteLine($"[Category][Update] existingOverride={existingOverride?.IdDanhMuc.ToString() ?? "null"}");
        }

        // targetId = record thực sự sẽ được update (override nếu có, hoặc chính entity)
        long targetId = existingOverride?.IdDanhMuc ?? id;
        // originalSystemId = id category hệ thống gốc (dùng để exclude override cùng gốc khỏi duplicate check)
        long? originalSystemId = isSystemCategory ? id : null;

        // Duplicate check: chỉ trong scope user, exclude chính record sẽ update và override cùng gốc
        var isDuplicate = await _db.DanhMucChiTieus.AnyAsync(d =>
            d.IdNguoiDung == userId &&
            !d.DaXoa &&
            d.TenDanhMuc.ToLower() == trimmedNameLower &&
            d.IdDanhMuc != targetId &&
            (originalSystemId == null || d.IdDanhMucGoc != originalSystemId));

        Console.WriteLine($"[Category][Update] duplicateCheck={isDuplicate} targetId={targetId} name='{trimmedName}'");

        if (isDuplicate)
            return Conflict(new { message = "Danh mục đã tồn tại" });

        try
        {
            DanhMucChiTieu resultEntity;

            if (isSystemCategory)
            {
                if (existingOverride != null)
                {
                    // Đã có override → update trực tiếp override đó
                    Console.WriteLine($"[Category][Update] branch=update-existing-override id={existingOverride.IdDanhMuc} userId={userId}");
                    existingOverride.TenDanhMuc = trimmedName;
                    existingOverride.Icon       = request.Icon;
                    existingOverride.MauSac     = request.Color;
                    await _db.SaveChangesAsync();
                    resultEntity = existingOverride;
                }
                else
                {
                    // Chưa có override → tạo mới, migrate giao dịch sang
                    Console.WriteLine($"[Category][Update] branch=create-override id={id} → new override for userId={userId}");
                    var newOverride = new DanhMucChiTieu
                    {
                        IdNguoiDung  = userId,
                        IdDanhMucGoc = id,
                        TenDanhMuc   = trimmedName,
                        Icon         = request.Icon,
                        MauSac       = request.Color,
                        NgayTao      = DateTime.UtcNow,
                        DaXoa        = false
                    };
                    _db.DanhMucChiTieus.Add(newOverride);
                    await _db.SaveChangesAsync();

                    await _db.ChiTieus
                        .Where(c => c.IdNguoiDung == userId && c.IdDanhMuc == id && !c.DaXoa)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.IdDanhMuc, newOverride.IdDanhMuc));

                    resultEntity = newOverride;
                }
            }
            else
            {
                // Category của user → update trực tiếp
                Console.WriteLine($"[Category][Update] branch=update-own-category id={id} userId={userId}");
                entity.TenDanhMuc = trimmedName;
                entity.Icon       = request.Icon;
                entity.MauSac     = request.Color;
                await _db.SaveChangesAsync();
                resultEntity = entity;
            }

            var transactionCount = await _db.ChiTieus
                .CountAsync(c => c.IdDanhMuc == resultEntity.IdDanhMuc && c.IdNguoiDung == userId && !c.DaXoa);

            return Ok(new
            {
                message = "Cập nhật danh mục thành công",
                data = new UpdateCategoryResponseDto
                {
                    IdDanhMuc        = resultEntity.IdDanhMuc,
                    TenDanhMuc       = resultEntity.TenDanhMuc,
                    Icon             = resultEntity.Icon,
                    MauSac           = resultEntity.MauSac,
                    TransactionCount = transactionCount
                }
            });
        }
        catch (DbUpdateException ex)
            when (ex.InnerException?.Message.Contains("UQ_DanhMucChiTieu") == true)
        {
            return Conflict(new { message = "Danh mục đã tồn tại" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Category][Update] ERROR id={id}: {ex.Message}");
            return StatusCode(500, new { message = "Không thể cập nhật danh mục. Vui lòng thử lại." });
        }
    }

    // DELETE /api/categories/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        var userId = GetCurrentUserId();
        Console.WriteLine($"[Category][Delete] id={id} userId={userId}");

        // 1. Tìm category theo id — không filter user/DaXoa trước để phân biệt rõ lỗi
        var entity = await _db.DanhMucChiTieus
            .FirstOrDefaultAsync(d => d.IdDanhMuc == id);

        if (entity == null)
        {
            Console.WriteLine($"[Category][Delete] NOT FOUND id={id}");
            return NotFound(new { message = "Danh mục không tồn tại" });
        }

        if (entity.DaXoa)
        {
            Console.WriteLine($"[Category][Delete] DELETED id={id}");
            return NotFound(new { message = "Danh mục đã bị xoá" });
        }

        // Cho phép: danh mục chung (IdNguoiDung == null) hoặc danh mục của chính user
        if (entity.IdNguoiDung != null && entity.IdNguoiDung != userId)
        {
            Console.WriteLine($"[Category][Delete] FORBIDDEN id={id} owner={entity.IdNguoiDung} caller={userId}");
            return StatusCode(403, new { message = "Không có quyền xoá danh mục này" });
        }

        // 2. Chặn xóa danh mục mặc định "Khác"
        if (entity.TenDanhMuc.Equals("Khác", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Không thể xóa danh mục mặc định" });

        try
        {
            bool isSystemCategory = entity.IdNguoiDung == null;

            if (isSystemCategory)
            {
                // Danh mục chung: không xóa record gốc
                // Kiểm tra đã có override DaXoa cho user này chưa
                var existingOverride = await _db.DanhMucChiTieus
                    .FirstOrDefaultAsync(d => d.IdDanhMucGoc == id && d.IdNguoiDung == userId);

                if (existingOverride != null)
                {
                    // Đã có override → chỉ đánh dấu DaXoa = true
                    existingOverride.DaXoa = true;
                }
                else
                {
                    // Tạo override mới với DaXoa = true để ẩn category chung với user này
                    // Dùng tên unique tạm để tránh vi phạm unique constraint (IdNguoiDung, TenDanhMuc)
                    var overrideName = $"__deleted_{id}_{userId}";
                    _db.DanhMucChiTieus.Add(new DanhMucChiTieu
                    {
                        IdNguoiDung  = userId,
                        IdDanhMucGoc = id,
                        TenDanhMuc   = overrideName,
                        NgayTao      = DateTime.UtcNow,
                        DaXoa        = true
                    });
                }

                // Migrate giao dịch của user đang dùng category chung này → "Khác"
                var khac = await _categoryService.GetOrCreateDefaultCategoryAsync(userId);
                await _db.ChiTieus
                    .Where(c => c.IdNguoiDung == userId && c.IdDanhMuc == id && !c.DaXoa)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IdDanhMuc, khac.IdDanhMuc));

                await _db.SaveChangesAsync();
                Console.WriteLine($"[Category][Delete] SYSTEM category id={id} → override DaXoa for userId={userId}");
            }
            else
            {
                // Danh mục của user: migrate giao dịch → "Khác" rồi soft delete
                var khac = await _categoryService.GetOrCreateDefaultCategoryAsync(userId);
                await _db.ChiTieus
                    .Where(c => c.IdNguoiDung == userId && c.IdDanhMuc == id && !c.DaXoa)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IdDanhMuc, khac.IdDanhMuc));

                entity.DaXoa = true;
                await _db.SaveChangesAsync();
                Console.WriteLine($"[Category][Delete] USER category id={id} soft deleted");
            }

            return Ok(new { message = "Xóa danh mục thành công" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Category][Delete] ERROR id={id}: {ex.Message}");
            return StatusCode(500, new { message = "Xóa danh mục thất bại" });
        }
    }
}
