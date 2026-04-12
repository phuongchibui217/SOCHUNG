using System.ComponentModel.DataAnnotations;

namespace ExpenseManagerAPI.DTOs;

public class DanhMucResponseDto
{
    public long IdDanhMuc { get; set; }
    public long? IdNguoiDung { get; set; }
    public string TenDanhMuc { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? MauSac { get; set; }
    // Alias chuẩn hóa — cùng giá trị với MauSac, FE dùng field này
    public string? Color => MauSac;
    public int TransactionCount { get; set; }
}

// DTO gọn cho màn hình chọn danh mục (Thêm giao dịch)
public class CategoryPickerDto
{
    public long IdDanhMuc { get; set; }
    public string TenDanhMuc { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? MauSac { get; set; }
    // Alias chuẩn hóa — cùng giá trị với MauSac, FE dùng field này
    public string? Color => MauSac;
    public int TransactionCount { get; set; }
}

// Request: POST /api/categories — field name theo spec FE
public class CreateCategoryRequest
{
    [Required(ErrorMessage = "Vui lòng nhập Tên danh mục")]
    [MaxLength(100, ErrorMessage = "Tên danh mục không được vượt quá 100 ký tự")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50, ErrorMessage = "Icon không được vượt quá 50 ký tự")]
    public string? Icon { get; set; }

    [MaxLength(7, ErrorMessage = "Mã màu không hợp lệ")]
    public string? Color { get; set; }

    // "expense" | "income" — hiện tại chỉ support expense, giữ để tương thích FE
    public string? Type { get; set; }
}

// Response data cho POST /api/categories
public class CreateCategoryResponse
{
    public long IdDanhMuc { get; set; }
    public string TenDanhMuc { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? MauSac { get; set; }
}

// Request: PUT /api/categories/{id} — field name theo spec FE
public class UpdateCategoryRequest
{
    [Required(ErrorMessage = "Vui lòng nhập Tên danh mục")]
    [MaxLength(100, ErrorMessage = "Tên danh mục không được vượt quá 100 ký tự")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50, ErrorMessage = "Icon không được vượt quá 50 ký tự")]
    public string? Icon { get; set; }

    [MaxLength(7, ErrorMessage = "Mã màu không hợp lệ")]
    public string? Color { get; set; }

    // Nhận từ FE để tương thích, không ảnh hưởng logic
    public string? Type { get; set; }
}

// Response data cho PUT /api/categories/{id}
public class UpdateCategoryResponseDto
{
    public long IdDanhMuc { get; set; }
    public string TenDanhMuc { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? MauSac { get; set; }
    public int TransactionCount { get; set; }
}

// --- Legacy DTOs giữ lại cho các endpoint khác ---

public class CreateDanhMucDto
{
    [Required] public long IdNguoiDung { get; set; }
    [Required] [MaxLength(100)] public string TenDanhMuc { get; set; } = string.Empty;
    [MaxLength(50)] public string? Icon { get; set; }
    [MaxLength(7)] public string? MauSac { get; set; }
}

public class UpdateDanhMucDto
{
    [Required] [MaxLength(100)] public string TenDanhMuc { get; set; } = string.Empty;
    [MaxLength(50)] public string? Icon { get; set; }
    [MaxLength(7)] public string? MauSac { get; set; }
}
