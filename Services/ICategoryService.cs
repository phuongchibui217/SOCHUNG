using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Models;

namespace ExpenseManagerAPI.Services;

public interface ICategoryService
{
    /// <summary>
    /// Lấy category theo id, kiểm tra thuộc user hoặc là category hệ thống.
    /// Trả null nếu không hợp lệ.
    /// </summary>
    Task<DanhMucChiTieu?> GetValidCategoryAsync(long categoryId, long userId);

    /// <summary>
    /// Lấy category mặc định "Khác" của user hoặc hệ thống.
    /// Tự tạo nếu chưa tồn tại.
    /// </summary>
    Task<DanhMucChiTieu> GetOrCreateDefaultCategoryAsync(long userId);

    /// <summary>
    /// Lấy danh sách category hợp lệ của user (bao gồm category hệ thống).
    /// Đảm bảo "Khác" tồn tại. TransactionCount được tính trực tiếp từ DB.
    /// </summary>
    Task<List<CategoryPickerDto>> GetCategoriesForPickerAsync(long userId);

    /// <summary>
    /// Tạo mới danh mục cho user.
    /// Trả (entity, errorMessage) — errorMessage != null nếu có lỗi nghiệp vụ.
    /// </summary>
    Task<(DanhMucChiTieu? Entity, string? Error)> CreateCategoryAsync(
        long userId, string name, string? icon, string? color);
}
