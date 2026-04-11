namespace ExpenseManagerAPI.Models;

public class ThanhToanCongNo
{
    public long IdThanhToan { get; set; }
    public long IdCongNo { get; set; }
    public decimal SoTienThanhToan { get; set; }
    public DateTime NgayThanhToan { get; set; }
    public string? GhiChu { get; set; }

    public CongNo? CongNo { get; set; }
}
