using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManagerAPI.Services;

public class CategoryService : ICategoryService
{
    private const string DefaultCategoryName = "Khác";
    private readonly SoChungDbContext _db;

    public CategoryService(SoChungDbContext db) => _db = db;

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

        // Lấy danh sách IdDanhMucGoc mà user đã override DaXoa = true (ẩn category chung)
        var hiddenSystemIds = await _db.DanhMucChiTieus
            .Where(d => d.IdNguoiDung == userId && d.IdDanhMucGoc != null && d.DaXoa)
            .Select(d => d.IdDanhMucGoc!.Value)
            .ToListAsync();

        // Lấy danh mục của user (không phải override ẩn) + danh mục chung chưa bị user ẩn
        var result = await _db.DanhMucChiTieus
            .Where(d =>
                !d.DaXoa &&
                d.IdDanhMucGoc == null &&   // bỏ qua các record override
                (
                    d.IdNguoiDung == userId ||
                    (d.IdNguoiDung == null && !hiddenSystemIds.Contains(d.IdDanhMuc))
                ))
            .Select(d => new CategoryPickerDto
            {
                IdDanhMuc = d.IdDanhMuc,
                TenDanhMuc = d.TenDanhMuc,
                Icon = d.Icon,
                MauSac = d.MauSac,
                TransactionCount = d.ChiTieus
                    .Count(c => c.IdNguoiDung == userId && !c.DaXoa)
            })
            .OrderBy(d => d.TenDanhMuc == DefaultCategoryName ? 1 : 0)
            .ThenBy(d => d.TenDanhMuc)
            .ToListAsync();

        return result;
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
