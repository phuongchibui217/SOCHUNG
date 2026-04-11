namespace ExpenseManagerAPI.Models;

public class ChiTieu
{
    public long IdChiTieu { get; set; }
    public long IdNguoiDung { get; set; }
    public long IdDanhMuc { get; set; }
    public decimal SoTien { get; set; }
    public string? NoiDung { get; set; }
    public DateTime NgayChi { get; set; }
    public bool DaXoa { get; set; }

    public NguoiDung? NguoiDung { get; set; }
    public DanhMucChiTieu? DanhMucChiTieu { get; set; }
}
