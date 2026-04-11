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

        // Query một lần: join DanhMucChiTieu với ChiTieu để đếm transactionCount
        // TransactionCount chỉ đếm giao dịch của user hiện tại, chưa bị xóa
        var result = await _db.DanhMucChiTieus
            .Where(d => (d.IdNguoiDung == userId || d.IdNguoiDung == null) && !d.DaXoa)
            .Select(d => new CategoryPickerDto
            {
                IdDanhMuc = d.IdDanhMuc,
                TenDanhMuc = d.TenDanhMuc,
                Icon = d.Icon,
                MauSac = d.MauSac,
                TransactionCount = d.ChiTieus
                    .Count(c => c.IdNguoiDung == userId && !c.DaXoa)
            })
            .OrderBy(d => d.TenDanhMuc == DefaultCategoryName ? 1 : 0) // "Khác" xuống cuối
            .ThenBy(d => d.TenDanhMuc)
            .ToListAsync();

        return result;
    }

    public async Task<(DanhMucChiTieu? Entity, string? Error)> CreateCategoryAsync(
        long userId, string name, string? icon, string? color)
    {
        var trimmedName = name.Trim();

        // Không cho tạo trùng "Khác" — category mặc định do hệ thống quản lý
        if (trimmedName.Equals(DefaultCategoryName, StringComparison.OrdinalIgnoreCase))
            return (null, "Danh mục đã tồn tại");

        // Kiểm tra trùng với category của chính user (chưa xóa)
        var duplicateUser = await _db.DanhMucChiTieus.AnyAsync(d =>
            d.IdNguoiDung == userId &&
            d.TenDanhMuc == trimmedName &&
            !d.DaXoa);
        if (duplicateUser)
            return (null, "Danh mục đã tồn tại");

        // Kiểm tra trùng với category hệ thống (IdNguoiDung IS NULL)
        var duplicateSystem = await _db.DanhMucChiTieus.AnyAsync(d =>
            d.IdNguoiDung == null &&
            d.TenDanhMuc == trimmedName &&
            !d.DaXoa);
        if (duplicateSystem)
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
