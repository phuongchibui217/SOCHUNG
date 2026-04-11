namespace ExpenseManagerAPI.Models;

public class NguoiDung
{
    public long IdNguoiDung { get; set; }
    public string Email { get; set; } = string.Empty;
    public string MatKhauHash { get; set; } = string.Empty;
    public DateTime NgayTao { get; set; }
    public bool DaXacMinhEmail { get; set; }
    public int FailedAttempts { get; set; } = 0;
    public DateTime? LockUntil { get; set; }
    public bool NhacNo7Ngay { get; set; } = true;

    public ICollection<DanhMucChiTieu> DanhMucChiTieus { get; set; } = new List<DanhMucChiTieu>();
    public ICollection<ChiTieu> ChiTieus { get; set; } = new List<ChiTieu>();
    public ICollection<CongNo> CongNos { get; set; } = new List<CongNo>();
    public ICollection<ThongBao> ThongBaos { get; set; } = new List<ThongBao>();
    public ICollection<TokenNguoiDung> TokenNguoiDungs { get; set; } = new List<TokenNguoiDung>();
}
