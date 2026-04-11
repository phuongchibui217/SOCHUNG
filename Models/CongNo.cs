namespace ExpenseManagerAPI.Models;

public class CongNo
{
    public long IdCongNo { get; set; }
    public long IdNguoiDung { get; set; }
    public string TenNguoi { get; set; } = string.Empty;
    public decimal SoTien { get; set; }
    public string LoaiCongNo { get; set; } = string.Empty; // 'NO' | 'CHO_VAY'
    public string? NoiDung { get; set; }
    public DateTime? HanTra { get; set; }
    public string TrangThai { get; set; } = "CHUA_TRA"; // 'CHUA_TRA' | 'TRA_MOT_PHAN' | 'DA_TRA'
    public bool DaXoa { get; set; }
    public DateTime NgayPhatSinh { get; set; }

    public NguoiDung? NguoiDung { get; set; }
    public ICollection<ThanhToanCongNo> ThanhToanCongNos { get; set; } = new List<ThanhToanCongNo>();
    public ICollection<ThongBao> ThongBaos { get; set; } = new List<ThongBao>();
}
