using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManagerAPI.Services;

public class NotificationService : INotificationService
{
    private static readonly TimeZoneInfo VnTz =
        TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

    private readonly IServiceScopeFactory _scopeFactory;

    public NotificationService(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    private static DateTime VnNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VnTz);

    // -------------------------------------------------------------------------
    // RunDailyReminderAsync — chạy mỗi ngày 09:00 AM (UTC+7)
    // -------------------------------------------------------------------------
    public async Task RunDailyReminderAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SoChungDbContext>();

        var today = VnNow().Date;

        var khoans = await db.CongNos
            .Include(c => c.NguoiDung)
            .Where(c => !c.DaXoa &&
                        (c.TrangThai == "CHUA_TRA" || c.TrangThai == "TRA_MOT_PHAN"))
            .ToListAsync();

        var newNotifications = new List<ThongBao>();

        foreach (var khoan in khoans)
        {
            // --- Quá hạn ---
            if (khoan.HanTra.HasValue && khoan.HanTra.Value.Date < today)
            {
                var soNgayQuaHan = (today - khoan.HanTra.Value.Date).Days;
                if (!await DuplicateExistsAsync(db, khoan.IdCongNo, "QUA_HAN", today))
                {
                    newNotifications.Add(new ThongBao
                    {
                        IdNguoiDung  = khoan.IdNguoiDung,
                        IdCongNo     = khoan.IdCongNo,
                        TieuDe       = "Khoản nợ QUÁ HẠN!",
                        NoiDung      = $"Khoản nợ từ {khoan.TenNguoi} ({khoan.SoTien:N0}đ) đã quá hạn {soNgayQuaHan} ngày.",
                        LoaiThongBao = "QUA_HAN",
                        DaDoc        = false,
                        NgayTao      = VnNow()
                    });
                }
            }
            // --- Sắp đến hạn (ngày mai) ---
            else if (khoan.HanTra.HasValue && khoan.HanTra.Value.Date == today.AddDays(1))
            {
                if (!await DuplicateExistsAsync(db, khoan.IdCongNo, "SAP_DEN_HAN", today))
                {
                    newNotifications.Add(new ThongBao
                    {
                        IdNguoiDung  = khoan.IdNguoiDung,
                        IdCongNo     = khoan.IdCongNo,
                        TieuDe       = "Sắp đến hạn trả nợ",
                        NoiDung      = $"Khoản nợ {khoan.TenNguoi} ({khoan.SoTien:N0}đ) sẽ đến hạn vào ngày mai.",
                        LoaiThongBao = "SAP_DEN_HAN",
                        DaDoc        = false,
                        NgayTao      = VnNow()
                    });
                }
            }

            // --- Nhắc 7 ngày định kỳ (chỉ khi user bật) ---
            if (!khoan.HanTra.HasValue && khoan.NguoiDung?.NhacNo7Ngay == true)
            {
                var soNgay = (today - khoan.NgayPhatSinh.Date).Days;
                if (soNgay > 0 && soNgay % 7 == 0)
                {
                    if (!await DuplicateExistsAsync(db, khoan.IdCongNo, "NHAC_7_NGAY", today))
                    {
                        newNotifications.Add(new ThongBao
                        {
                            IdNguoiDung  = khoan.IdNguoiDung,
                            IdCongNo     = khoan.IdCongNo,
                            TieuDe       = "Nhắc nhở khoản nợ chưa có hạn trả",
                            NoiDung      = $"Khoản nợ {khoan.TenNguoi} đã phát sinh {soNgay} ngày nhưng chưa có hạn trả.",
                            LoaiThongBao = "NHAC_7_NGAY",
                            DaDoc        = false,
                            NgayTao      = VnNow()
                        });
                    }
                }
            }
        }

        if (newNotifications.Count > 0)
        {
            db.ThongBaos.AddRange(newNotifications);
            await db.SaveChangesAsync();
        }
    }

    // -------------------------------------------------------------------------
    // GetCaiDatAsync — đọc setting nhắc nợ từ NguoiDung
    // -------------------------------------------------------------------------
    public async Task<CaiDatThongBaoDto> GetCaiDatAsync(long userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SoChungDbContext>();

        var user = await db.NguoiDungs.FindAsync(userId);
        return new CaiDatThongBaoDto { AutoRemindAfter7Days = user?.NhacNo7Ngay ?? false };
    }

    // -------------------------------------------------------------------------
    // UpsertCaiDatAsync — cập nhật setting nhắc nợ trên NguoiDung
    // -------------------------------------------------------------------------
    public async Task<CaiDatThongBaoDto> UpsertCaiDatAsync(long userId, CapNhatCaiDatThongBaoDto dto)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SoChungDbContext>();

        var user = await db.NguoiDungs.FindAsync(userId);
        if (user == null) return new CaiDatThongBaoDto();

        user.NhacNo7Ngay = dto.AutoRemindAfter7Days;
        await db.SaveChangesAsync();

        return new CaiDatThongBaoDto { AutoRemindAfter7Days = user.NhacNo7Ngay };
    }

    private static async Task<bool> DuplicateExistsAsync(
        SoChungDbContext db, long idCongNo, string loai, DateTime today)
    {
        return await db.ThongBaos.AnyAsync(t =>
            t.IdCongNo == idCongNo &&
            t.LoaiThongBao == loai &&
            t.NgayTao.Date == today);
    }
}
