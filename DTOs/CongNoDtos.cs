using System.ComponentModel.DataAnnotations;

namespace ExpenseManagerAPI.DTOs;

public class CongNoResponseDto
{
    public long IdCongNo { get; set; }
    public long IdNguoiDung { get; set; }
    public string TenNguoi { get; set; } = string.Empty;
    public decimal SoTien { get; set; }
    public decimal DaThanhToan { get; set; }
    public decimal ConLai { get; set; }
    public string LoaiCongNo { get; set; } = string.Empty;
    public string? NoiDung { get; set; }
    public DateTime? HanTra { get; set; }
    public string TrangThai { get; set; } = string.Empty;
    public bool DaXoa { get; set; }
    public DateTime NgayPhatSinh { get; set; }
}

public class CongNoDetailDto : CongNoResponseDto
{
    public List<ThanhToanResponseDto> ThanhToanCongNos { get; set; } = new();
}

// Request: POST /api/debts — field tên tiếng Anh theo spec FE
public class CreateDebtRequest
{
    // "NO" | "CHO_VAY" — flow này mặc định NO, vẫn nhận để tương thích CHO_VAY sau này
    [RegularExpression("^(NO|CHO_VAY)$", ErrorMessage = "Loại công nợ không hợp lệ. Chấp nhận: NO, CHO_VAY")]
    public string TransactionType { get; set; } = "NO";

    [Required(ErrorMessage = "Vui lòng nhập Số tiền")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Số tiền phải lớn hơn 0")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập Người giao dịch")]
    [MaxLength(100, ErrorMessage = "Tên người không được vượt quá 100 ký tự")]
    public string PersonName { get; set; } = string.Empty;

    // Ngày phát sinh — null thì dùng hôm nay
    public DateTime? OccurredDate { get; set; }

    // Hạn trả — null thì tự tính = occurredDate + 7 ngày
    public DateTime? DueDate { get; set; }

    [MaxLength(255, ErrorMessage = "Ghi chú không được vượt quá 255 ký tự")]
    public string? Note { get; set; }
}

// Response: POST /api/debts
public class CreateDebtResponse
{
    public long DebtId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string OccurredDate { get; set; } = string.Empty;
    public string? DueDate { get; set; }
    public string? Note { get; set; }
    // Tổng số tiền còn nợ của người này (tất cả khoản CHUA_TRA + TRA_MOT_PHAN)
    public decimal CurrentOutstandingOfPerson { get; set; }
}

// Response: GET /api/debts/people/suggestions
public class PersonSuggestionDto
{
    public string PersonName { get; set; } = string.Empty;
    // Tổng ConLai của các khoản chưa thanh toán xong
    public decimal CurrentOutstanding { get; set; }
    // Số khoản CHUA_TRA hoặc TRA_MOT_PHAN
    public int OpenDebtCount { get; set; }
}

// DTO cho GET /api/debts/{id} — response đúng spec FE
public class DebtDetailDto
{
    public long DebtId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    // null khi khoản đã DA_TRA
    public decimal? PersonOutstandingTotal { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OccurredDate { get; set; } = string.Empty;
    public string? DueDate { get; set; }
    public string? Note { get; set; }
    public List<PaymentHistoryItemDto> PaymentHistory { get; set; } = new();
}

// Item trong paymentHistory
public class PaymentHistoryItemDto
{
    public long PaymentId { get; set; }
    public string PaymentDate { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

// --- Search debt ---

/// <summary>
/// Query params cho GET /api/debts/search.
/// keyword optional, statusFilter optional.
/// Accent-insensitive: dùng SQL Server collation Latin1_General_CI_AI (xem controller).
/// </summary>
public class DebtSearchQuery
{
    // Optional — nếu truyền thì trim trái, max 100 ký tự
    [MaxLength(100, ErrorMessage = "Không được vượt quá 100 ký tự")]
    public string? Keyword { get; set; }

    // Optional — DUE_SOON | OVERDUE | COMPLETED
    public string? StatusFilter { get; set; }

    public int Page { get; set; } = 1;

    // Max 15 theo spec
    public int PageSize { get; set; } = 15;
}

/// <summary>
/// Một item trong kết quả search debt.
/// </summary>
public class DebtSearchItemDto
{
    public long DebtId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string OccurredDate { get; set; } = string.Empty;
    public string? DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>OVERDUE | DUE_SOON | NORMAL_OPEN | COMPLETED</summary>
    public string DisplayStatus { get; set; } = string.Empty;
    public string? Note { get; set; }
    /// <summary>Tổng còn lại của tất cả khoản NỢ (NO) chưa hoàn tất của người này</summary>
    public decimal PersonOutstandingDebtTotal { get; set; }
    /// <summary>Tổng còn lại của tất cả khoản CHO VAY chưa hoàn tất của người này</summary>
    public decimal PersonOutstandingLendingTotal { get; set; }
}

/// <summary>
/// Response cho GET /api/debts/search.
/// </summary>
public class DebtSearchResultDto
{
    public List<DebtSearchItemDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// Item gợi ý nhanh — GET /api/debts/search/suggestions.
/// </summary>
public class DebtSuggestionDto
{
    public long DebtId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
}

// --- Debt List with filter ---

/// <summary>
/// Query params cho GET /api/debts?transactionType=NO&statusFilter=ALL
/// </summary>
public class DebtListQuery
{
    // Bắt buộc: NO | CHO_VAY
    [Required(ErrorMessage = "transactionType là bắt buộc")]
    [RegularExpression("^(NO|CHO_VAY)$", ErrorMessage = "transactionType không hợp lệ. Chấp nhận: NO, CHO_VAY")]
    public string TransactionType { get; set; } = string.Empty;

    // Optional: ALL | OPEN | COMPLETED — mặc định ALL
    public string? StatusFilter { get; set; }
}

/// <summary>
/// Một item trong danh sách công nợ có filter.
/// displayStatus: OVERDUE | DUE_SOON | NORMAL_OPEN | COMPLETED
/// </summary>
public class DebtListItemDto
{
    public long DebtId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public decimal? PersonOutstandingTotal { get; set; }
    public string OccurredDate { get; set; } = string.Empty;
    public string? DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>OVERDUE | DUE_SOON | NORMAL_OPEN | COMPLETED</summary>
    public string DisplayStatus { get; set; } = string.Empty;
    /// <summary>Số ngày quá hạn — chỉ có giá trị khi DisplayStatus = OVERDUE</summary>
    public int? OverdueDays { get; set; }
    public string? Note { get; set; }
}

// --- Legacy DTOs giữ lại ---

public class CreateCongNoDto
{
    [Required] public long IdNguoiDung { get; set; }
    [Required] [MaxLength(100)] public string TenNguoi { get; set; } = string.Empty;
    [Required] [Range(0.01, double.MaxValue)] public decimal SoTien { get; set; }
    [Required] [RegularExpression("NO|CHO_VAY")] public string LoaiCongNo { get; set; } = string.Empty;
    [MaxLength(255)] public string? NoiDung { get; set; }
    public DateTime? HanTra { get; set; }
    [Required] public DateTime NgayPhatSinh { get; set; }
}

public class UpdateCongNoDto
{
    [Required] [MaxLength(100)] public string TenNguoi { get; set; } = string.Empty;
    [Required] [Range(0.01, double.MaxValue)] public decimal SoTien { get; set; }
    [Required] [RegularExpression("NO|CHO_VAY")] public string LoaiCongNo { get; set; } = string.Empty;
    [MaxLength(255)] public string? NoiDung { get; set; }
    public DateTime? HanTra { get; set; }
    [Required] public DateTime NgayPhatSinh { get; set; }
}

/// <summary>
/// Request PUT /api/debts/{id} — field tên tiếng Anh theo spec FE.
/// NgayPhatSinh không nhận từ request — giữ nguyên giá trị gốc.
/// </summary>
public class UpdateDebtRequest
{
    [Required(ErrorMessage = "Vui lòng nhập Số tiền")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Số tiền phải lớn hơn 0")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập Người giao dịch")]
    [MaxLength(100, ErrorMessage = "Tên người không được vượt quá 100 ký tự")]
    public string PersonName { get; set; } = string.Empty;

    [RegularExpression("^(NO|CHO_VAY)$", ErrorMessage = "Loại công nợ không hợp lệ. Chấp nhận: NO, CHO_VAY")]
    public string TransactionType { get; set; } = "NO";

    // Optional — null thì xóa hạn trả
    public DateTime? DueDate { get; set; }

    [MaxLength(255, ErrorMessage = "Ghi chú không được vượt quá 255 ký tự")]
    public string? Note { get; set; }

    // FE có thể truyền thêm các field này — bỏ qua, không dùng
    public object? PersonId { get; set; }
    public bool? IsExistingPerson { get; set; }
}

/// <summary>
/// Response data cho PUT /api/debts/{id}.
/// </summary>
public class UpdateDebtResponseDto
{
    public long DebtId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OccurredDate { get; set; } = string.Empty;
    public string? DueDate { get; set; }
    public string? Note { get; set; }
}
