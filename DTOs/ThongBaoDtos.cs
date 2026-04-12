namespace ExpenseManagerAPI.DTOs;

// Response theo spec FE (field tên tiếng Anh)
public class NotificationDto
{
    public long NotificationId { get; set; }
    public long? DebtId { get; set; }
    /// <summary>NO | CHO_VAY — để FE deep-link đúng màn hình, không cần parse text</summary>
    public string? DebtType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string NotificationType { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Legacy — giữ lại cho endpoint cũ
public class ThongBaoResponseDto
{
    public long IdThongBao { get; set; }
    public long IdNguoiDung { get; set; }
    public long? IdCongNo { get; set; }
    public string TieuDe { get; set; } = string.Empty;
    public string NoiDung { get; set; } = string.Empty;
    public string LoaiThongBao { get; set; } = string.Empty;
    public bool DaDoc { get; set; }
    public DateTime NgayTao { get; set; }
}

// Cài đặt thông báo
public class CaiDatThongBaoDto
{
    public bool AutoRemindAfter7Days { get; set; }
}

public class CapNhatCaiDatThongBaoDto
{
    public bool AutoRemindAfter7Days { get; set; }
}
