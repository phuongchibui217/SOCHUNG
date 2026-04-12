using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManagerAPI.Services;

public class CategoryService : ICategoryService
{
    private const string DefaultCategoryName = "Khác";
    private readonly SoChungDbContext _db;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(SoChungDbContext db, ILogger<CategoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DanhMucChiTieu?> GetValidCategoryAsync(long categoryId, long userId)
    {
        return await _db.DanhMucChiTieus
            .FirstOrDefaultAsync(d =>
                d.IdDanhMuc == categoryId &&
                !d.DaXoa &&
                (d.IdNguoiDung == userId || d.IdNguoiDung == null));
    }

    public async Task<DanhMucChiTieu> GetOrCreateDefaultCategoryAsync(long userId)
    {
        // Ưu tiên 1: category hệ thống "Khác" (IdNguoiDung IS NULL)
        var systemDefault = await _db.DanhMucChiTieus
            .FirstOrDefaultAsync(d =>
                d.IdNguoiDung == null &&
                d.TenDanhMuc == DefaultCategoryName &&
                !d.DaXoa);

        if (systemDefault != null) return systemDefault;

        // Ưu tiên 2: category "Khác" riêng của user
        var userDefault = await _db.DanhMucChiTieus
            .FirstOrDefaultAsync(d =>
                d.IdNguoiDung == userId &&
                d.TenDanhMuc == DefaultCategoryName &&
                !d.DaXoa);

        if (userDefault != null) return userDefault;

        // Tự tạo nếu chưa có
        var newCategory = new DanhMucChiTieu
        {
            IdNguoiDung = userId,
            TenDanhMuc = DefaultCategoryName,
            Icon = "more_horiz",
            MauSac = "#9E9E9E",
            NgayTao = DateTime.UtcNow,
            DaXoa = false
        };

        _db.DanhMucChiTieus.Add(newCategory);
        await _db.SaveChangesAsync();

        return newCategory;
    }

    public async Task<List<CategoryPickerDto>> GetCategoriesForPickerAsync(long userId)
    {
        // Đảm bảo "Khác" luôn tồn tại trước khi query
        await GetOrCreateDefaultCategoryAsync(userId);

        // Lấy tất cả category hệ thống chưa xóa
        var systemCategories = await _db.DanhMucChiTieus
            .Where(d => d.IdNguoiDung == null && !d.DaXoa)
            .ToListAsync();

        // Lấy tất cả category của user (bao gồm cả override) chưa xóa
        var userCategories = await _db.DanhMucChiTieus
            .Where(d => d.IdNguoiDung == userId && !d.DaXoa)
            .ToListAsync();

        // Lấy override bị ẩn (DaXoa = true) để biết system category nào cần ẩn
        var hiddenSystemIds = await _db.DanhMucChiTieus
            .Where(d => d.IdNguoiDung == userId && d.IdDanhMucGoc != null && d.DaXoa)
            .Select(d => d.IdDanhMucGoc!.Value)
            .ToListAsync();

        _logger.LogInformation(
            "GetCategories userId={UserId} systemCount={SystemCount} userCount={UserCount} hiddenCount={HiddenCount}",
            userId, systemCategories.Count, userCategories.Count, hiddenSystemIds.Count);

        var result = new List<CategoryPickerDto>();

        // Merge: với mỗi system category, ưu tiên override của user nếu có
        foreach (var sys in systemCategories)
        {
            // Bỏ qua nếu user đã ẩn (override DaXoa=true)
            if (hiddenSystemIds.Contains(sys.IdDanhMuc))
                continue;

            var overrideItem = userCategories
                .FirstOrDefault(u => u.IdDanhMucGoc == sys.IdDanhMuc);

            if (overrideItem != null)
            {
                _logger.LogInformation(
                    "GetCategories system={SystemId} overridden by {OverrideId} name='{Name}'",
                    sys.IdDanhMuc, overrideItem.IdDanhMuc, overrideItem.TenDanhMuc);

                result.Add(new CategoryPickerDto
                {
                    IdDanhMuc        = overrideItem.IdDanhMuc,
                    TenDanhMuc       = overrideItem.TenDanhMuc,
                    Icon             = overrideItem.Icon,
                    MauSac           = overrideItem.MauSac,
                    TransactionCount = await _db.ChiTieus
                        .CountAsync(c => c.IdDanhMuc == overrideItem.IdDanhMuc && c.IdNguoiDung == userId && !c.DaXoa)
                });
            }
            else
            {
                result.Add(new CategoryPickerDto
                {
                    IdDanhMuc        = sys.IdDanhMuc,
                    TenDanhMuc       = sys.TenDanhMuc,
                    Icon             = sys.Icon,
                    MauSac           = sys.MauSac,
                    TransactionCount = await _db.ChiTieus
                        .CountAsync(c => c.IdDanhMuc == sys.IdDanhMuc && c.IdNguoiDung == userId && !c.DaXoa)
                });
            }
        }

        // Thêm category riêng của user (không phải override hệ thống)
        foreach (var u in userCategories.Where(u => u.IdDanhMucGoc == null))
        {
            result.Add(new CategoryPickerDto
            {
                IdDanhMuc        = u.IdDanhMuc,
                TenDanhMuc       = u.TenDanhMuc,
                Icon             = u.Icon,
                MauSac           = u.MauSac,
                TransactionCount = await _db.ChiTieus
                    .CountAsync(c => c.IdDanhMuc == u.IdDanhMuc && c.IdNguoiDung == userId && !c.DaXoa)
            });
        }

        return result
            .OrderBy(d => d.TenDanhMuc == DefaultCategoryName ? 1 : 0)
            .ThenBy(d => d.TenDanhMuc)
            .ToList();
    }

    public async Task<(DanhMucChiTieu? Entity, string? Error)> CreateCategoryAsync(
        long userId, string name, string? icon, string? color)
    {
        var trimmedName = name.Trim();
        var trimmedNameLower = trimmedName.ToLower();

        // Không cho tạo trùng "Khác" — category mặc định do hệ thống quản lý
        if (trimmedName.Equals(DefaultCategoryName, StringComparison.OrdinalIgnoreCase))
            return (null, "Danh mục đã tồn tại");

        // Chỉ check trùng trong danh mục riêng của user — shared categories (IdNguoiDung == null)
        // không tính là duplicate, user được phép tạo category cùng tên với shared category
        var duplicate = await _db.DanhMucChiTieus.AnyAsync(d =>
            d.IdNguoiDung == userId &&
            !d.DaXoa &&
            d.TenDanhMuc.ToLower() == trimmedNameLower);
        if (duplicate)
            return (null, "Danh mục đã tồn tại");

        var entity = new DanhMucChiTieu
        {
            IdNguoiDung = userId,
            TenDanhMuc = trimmedName,
            Icon = icon,
            MauSac = color,
            NgayTao = DateTime.UtcNow,
            DaXoa = false
        };

        _db.DanhMucChiTieus.Add(entity);

        try
        {
            await _db.SaveChangesAsync();
            return (entity, null);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException?.Message.Contains("UQ_DanhMucChiTieu") == true)
        {
            // Race condition: unique constraint DB bắt được
            return (null, "Danh mục đã tồn tại");
        }
    }
}
