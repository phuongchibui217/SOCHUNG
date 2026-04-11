namespace ExpenseManagerAPI.Models;

public class DanhMucChiTieu
{
    public long IdDanhMuc { get; set; }
    public long? IdNguoiDung { get; set; }
    public string TenDanhMuc { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? MauSac { get; set; }
    public DateTime NgayTao { get; set; }
    public bool DaXoa { get; set; }

    public NguoiDung? NguoiDung { get; set; }
    public ICollection<ChiTieu> ChiTieus { get; set; } = new List<ChiTieu>();
}
