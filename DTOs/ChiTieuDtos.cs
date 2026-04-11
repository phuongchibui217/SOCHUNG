using System.ComponentModel.DataAnnotations;

namespace ExpenseManagerAPI.DTOs;

// --- Request ---

public class CreateExpenseRequest
{
    [Required(ErrorMessage = "Số tiền là bắt buộc")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Số tiền phải lớn hơn 0")]
    public decimal Amount { get; set; }

    // Nullable — nếu null thì fallback sang "Khác"
    public long? CategoryId { get; set; }

    // Nullable — nếu null thì dùng ngày hiện tại
    public DateTime? TransactionDate { get; set; }

    [MaxLength(255, ErrorMessage = "Ghi chú không được vượt quá 255 ký tự")]
    public string? Note { get; set; }
}

public class UpdateChiTieuDto
{
    [Required] public long IdDanhMuc { get; set; }
    [Required] [Range(0.01, double.MaxValue)] public decimal SoTien { get; set; }
    [MaxLength(255)] public string? NoiDung { get; set; }
    [Required] public DateTime NgayChi { get; set; }
}

/// <summary>
/// Request PUT /api/expenses/{id} — field tên tiếng Anh theo spec FE.
/// </summary>
public class UpdateExpenseRequest
{
    [Required(ErrorMessage = "Vui lòng nhập Số tiền")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Số tiền phải lớn hơn 0")]
    public decimal Amount { get; set; }

    // Nullable — nếu null thì fallback sang "Khác"
    public long? CategoryId { get; set; }

    // Nullable — nếu null thì dùng ngày hiện tại
    public DateTime? TransactionDate { get; set; }

    [MaxLength(255, ErrorMessage = "Ghi chú không được vượt quá 255 ký tự")]
    public string? Note { get; set; }
}

/// <summary>
/// Response PUT /api/expenses/{id} và GET /api/expenses/{id}.
/// </summary>
public class ExpenseDetailDto
{
    public long ExpenseId { get; set; }
    public decimal Amount { get; set; }
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? CategoryIcon { get; set; }
    public string? CategoryColor { get; set; }
    public string TransactionDate { get; set; } = string.Empty;
    public string? Note { get; set; }
}

// --- Response ---

public class CreateExpenseResponse
{
    public long ExpenseId { get; set; }
    public decimal Amount { get; set; }
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string TransactionDate { get; set; } = string.Empty;
    public string? Note { get; set; }
}

// --- History query params ---

public class ExpenseHistoryQuery
{
    // Phân trang
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    // Lấy N item gần nhất — nếu có thì bỏ qua Page/PageSize
    public int? Limit { get; set; }

    // Tìm theo nội dung giao dịch
    public string? Keyword { get; set; }

    // Lọc theo khoảng thời gian
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    // Lọc theo danh mục
    public long? CategoryId { get; set; }
}

// --- History response item ---

public class ExpenseHistoryDto
{
    public long ExpenseId { get; set; }
    public string? Note { get; set; }
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? CategoryIcon { get; set; }
    public string? CategoryColor { get; set; }
}

// --- Search ---

/// <summary>
/// Query params cho GET /api/expenses/search
/// </summary>
public class ExpenseSearchQuery
{
    // Từ khóa tìm kiếm — bắt buộc, 1–100 ký tự
    [Required(ErrorMessage = "Keyword là bắt buộc")]
    [MinLength(1, ErrorMessage = "Keyword phải có ít nhất 1 ký tự")]
    [MaxLength(100, ErrorMessage = "Keyword không được vượt quá 100 ký tự")]
    public string Keyword { get; set; } = string.Empty;

    public int Page { get; set; } = 1;

    // Max 15 theo spec
    public int PageSize { get; set; } = 15;
}

/// <summary>
/// Item trong kết quả search — cùng shape với ExpenseHistoryDto để FE tái dụng
/// </summary>
public class ExpenseSearchItemDto
{
    public long ExpenseId { get; set; }
    public string? Note { get; set; }
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? CategoryIcon { get; set; }
    public string? CategoryColor { get; set; }
}

/// <summary>
/// Response cho GET /api/expenses/search
/// </summary>
public class ExpenseSearchResultDto
{
    public List<ExpenseSearchItemDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// Item trong gợi ý nhanh — GET /api/expenses/search/suggestions
/// </summary>
public class ExpenseSuggestionDto
{
    public long ExpenseId { get; set; }
    public string? Note { get; set; }
    public decimal Amount { get; set; }
    public string CategoryName { get; set; } = string.Empty;
}

public class ChiTieuResponseDto
{
    public long IdChiTieu { get; set; }
    public long IdNguoiDung { get; set; }
    public long IdDanhMuc { get; set; }
    public string TenDanhMuc { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? MauSac { get; set; }
    public decimal SoTien { get; set; }
    public string? NoiDung { get; set; }
    public DateTime NgayChi { get; set; }
}
