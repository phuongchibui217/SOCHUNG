using System.ComponentModel.DataAnnotations;

namespace ExpenseManagerAPI.DTOs;

public class ThanhToanResponseDto
{
    public long IdThanhToan { get; set; }
    public long IdCongNo { get; set; }
    public decimal SoTienThanhToan { get; set; }
    public DateTime NgayThanhToan { get; set; }
    public string? GhiChu { get; set; }
}

public class CreateThanhToanDto
{
    [Required] [Range(0.01, double.MaxValue)] public decimal SoTienThanhToan { get; set; }
    [Required] public DateTime NgayThanhToan { get; set; }
    [MaxLength(255)] public string? GhiChu { get; set; }
}

// -------------------------------------------------------------------------
// POST /api/debts/{id}/payments — request theo spec FE
// -------------------------------------------------------------------------

/// <summary>
/// Request ghi nhận thanh toán.
/// paymentMethod: nhận từ FE nhưng không lưu DB (schema hiện tại chưa có cột này).
/// Nếu cần persist sau này, thêm cột PaymentMethod vào ThanhToanCongNo và migration.
/// </summary>
public class AddPaymentRequest
{
    [Required(ErrorMessage = "Vui lòng nhập Số tiền")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Số tiền phải lớn hơn 0")]
    public decimal Amount { get; set; }

    // Nullable — default hôm nay nếu không truyền
    public DateTime? PaymentDate { get; set; }

    /// <summary>
    /// Nhận từ FE (BANK_TRANSFER, CASH, ...) nhưng không persist vào DB hiện tại
    /// vì schema ThanhToanCongNo chưa có cột PaymentMethod.
    /// Để thêm: ALTER TABLE ThanhToanCongNo ADD PaymentMethod NVARCHAR(50) NULL
    /// </summary>
    public string? PaymentMethod { get; set; }

    [MaxLength(255, ErrorMessage = "Ghi chú không được vượt quá 255 ký tự")]
    public string? Note { get; set; }
}

/// <summary>
/// Response sau khi ghi nhận thanh toán thành công.
/// </summary>
public class AddPaymentResponseDto
{
    public long PaymentId { get; set; }
    public long DebtId { get; set; }
    public decimal PaidAmount { get; set; }        // số tiền vừa thanh toán
    public decimal TotalPaidAmount { get; set; }   // tổng đã trả sau giao dịch này
    public decimal RemainingAmount { get; set; }   // còn lại sau giao dịch này
    public string Status { get; set; } = string.Empty;
}

// -------------------------------------------------------------------------
// Timeline payment history — GET /api/debts/{id}/payments
// -------------------------------------------------------------------------

/// <summary>
/// Một item trong timeline: có thể là PAYMENT hoặc ORIGINAL_DEBT.
/// </summary>
public class DebtTimelineItemDto
{
    /// <summary>PAYMENT | ORIGINAL_DEBT</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// PAYMENT + NO       => "Trả tiền"
    /// PAYMENT + CHO_VAY  => "Thu tiền"
    /// ORIGINAL_DEBT + NO      => "Tạo khoản nợ"
    /// ORIGINAL_DEBT + CHO_VAY => "Tạo khoản vay"
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>null với ORIGINAL_DEBT</summary>
    public long? PaymentId { get; set; }

    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Response đầy đủ cho GET /api/debts/{id}/payments — summary + timeline.
/// </summary>
public class DebtPaymentTimelineDto
{
    public long DebtId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<DebtTimelineItemDto> Items { get; set; } = new();
}
