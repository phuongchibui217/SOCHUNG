namespace ExpenseManagerAPI.Models;

public class TokenNguoiDung
{
    public long IdToken { get; set; }
    public long IdNguoiDung { get; set; }
    public string MaToken { get; set; } = string.Empty;
    public string LoaiToken { get; set; } = string.Empty;
    public DateTime HetHan { get; set; }
    public bool DaDung { get; set; }
    public DateTime NgayTao { get; set; }

    public NguoiDung? NguoiDung { get; set; }
}
