namespace ExpenseManagerAPI.Models;

public class ThongBao
{
    public long IdThongBao { get; set; }
    public long IdNguoiDung { get; set; }
    public long? IdCongNo { get; set; }
    public string TieuDe { get; set; } = string.Empty;
    public string NoiDung { get; set; } = string.Empty;
    public string LoaiThongBao { get; set; } = string.Empty;
    public bool DaDoc { get; set; }
    public DateTime NgayTao { get; set; }

    public NguoiDung? NguoiDung { get; set; }
    public CongNo? CongNo { get; set; }
}
