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

    public CategoriesController(SoChungDbContext db, ICategoryService categoryService)
    {
        _db = db;
        _categoryService = categoryService;
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
        catch (Exception)
        {
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

        // 1. Tìm category — chỉ của user hiện tại (IdNguoiDung = userId), chưa xóa
        //    Category hệ thống (IdNguoiDung IS NULL) không match → không cho sửa
        var entity = await _db.DanhMucChiTieus
            .FirstOrDefaultAsync(d => d.IdDanhMuc == id && d.IdNguoiDung == userId && !d.DaXoa);

        if (entity == null)
            return NotFound(new { message = "Danh mục không tồn tại" });

        // 2. Chặn sửa danh mục mặc định "Khác"
        if (entity.TenDanhMuc.Equals("Khác", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Không thể chỉnh sửa danh mục mặc định" });

        var trimmedName = request.Name.Trim();

        // 3. Validate trùng tên — cho phép giữ nguyên tên cũ của chính nó
        var isDuplicate = await _db.DanhMucChiTieus.AnyAsync(d =>
            d.IdDanhMuc != id &&
            !d.DaXoa &&
            d.TenDanhMuc == trimmedName &&
            (d.IdNguoiDung == userId || d.IdNguoiDung == null));

        if (isDuplicate)
            return Conflict(new { message = "Danh mục đã tồn tại" });

        try
        {
            entity.TenDanhMuc = trimmedName;
            entity.Icon       = request.Icon;
            entity.MauSac     = request.Color;

            await _db.SaveChangesAsync();

            // Tính transactionCount sau update
            var transactionCount = await _db.ChiTieus
                .CountAsync(c => c.IdDanhMuc == id && c.IdNguoiDung == userId && !c.DaXoa);

            return Ok(new
            {
                message = "Cập nhật danh mục thành công",
                data = new UpdateCategoryResponseDto
                {
                    IdDanhMuc        = entity.IdDanhMuc,
                    TenDanhMuc       = entity.TenDanhMuc,
                    Icon             = entity.Icon,
                    MauSac           = entity.MauSac,
                    TransactionCount = transactionCount
                }
            });
        }
        catch (DbUpdateException ex)
            when (ex.InnerException?.Message.Contains("UQ_DanhMucChiTieu") == true)
        {
            // Race condition: unique constraint DB bắt được
            return Conflict(new { message = "Danh mục đã tồn tại" });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Không thể cập nhật danh mục. Vui lòng thử lại." });
        }
    }

    // DELETE /api/categories/{id}
    // Flow: chặn "Khác" → resolve category Khác → migrate ChiTieu → soft delete
    // Toàn bộ trong 1 DB transaction để đảm bảo toàn vẹn.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        var userId = GetCurrentUserId();

        // 1. Tìm category — chỉ của user hiện tại, chưa xóa
        var entity = await _db.DanhMucChiTieus
            .FirstOrDefaultAsync(d => d.IdDanhMuc == id && d.IdNguoiDung == userId && !d.DaXoa);

        if (entity == null)
            return NotFound(new { message = "Danh mục không tồn tại" });

        // 2. Chặn xóa danh mục mặc định "Khác"
        if (entity.TenDanhMuc.Equals("Khác", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Không thể xóa danh mục mặc định" });

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 3. Resolve category "Khác" — tìm hoặc tạo mới
            var khac = await _categoryService.GetOrCreateDefaultCategoryAsync(userId);

            // 4. Migrate toàn bộ ChiTieu của user đang dùng category bị xóa → sang "Khác"
            await _db.ChiTieus
                .Where(c => c.IdNguoiDung == userId && c.IdDanhMuc == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IdDanhMuc, khac.IdDanhMuc));

            // 5. Soft delete category
            entity.DaXoa = true;
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            return Ok(new { message = "Xóa danh mục thành công" });
        }
        catch (Exception)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { message = "Xóa danh mục thất bại" });
        }
    }
}
