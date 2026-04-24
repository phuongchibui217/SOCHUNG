using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Helpers;
using ExpenseManagerAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManagerAPI.Services;

public class NotificationService : INotificationService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public NotificationService(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    private static DateTime VnNow() => TimeZoneHelper.NowVn();

    // -------------------------------------------------------------------------
    // RunDailyReminderAsync — chạy mỗi ngày 09:00 AM (UTC+7)
    // Áp dụng cho cả 2 loại: NO (nợ phải trả) và CHO_VAY (nợ phải thu)
    // LoaiThongBao tách riêng theo loại công nợ để FE render đúng icon/nội dung
    // -------------------------------------------------------------------------
    public async Task RunDailyReminderAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SoChungDbContext>();

        var today = VnNow().Date;

        Console.WriteLine($"[Reminder] ===== RunDailyReminderAsync START — today={today:yyyy-MM-dd} =====");

        var khoans = await db.CongNos
            .Include(c => c.NguoiDung)
            .Where(c => !c.DaXoa &&
                        (c.TrangThai == "CHUA_TRA" || c.TrangThai == "TRA_MOT_PHAN"))
            .ToListAsync();

        Console.WriteLine($"[Reminder] Loaded {khoans.Count} khoản chưa hoàn tất");

        var newNotifications = new List<ThongBao>();

        foreach (var khoan in khoans)
        {
            var laChoVay  = khoan.LoaiCongNo == "CHO_VAY";
            var tenDoiTac = khoan.TenNguoi;
            var soTienFmt = $"{khoan.SoTien:N0}đ";
            var hanTraStr = khoan.HanTra.HasValue ? khoan.HanTra.Value.Date.ToString("yyyy-MM-dd") : "null";

            Console.WriteLine($"[Reminder] Xét IdCongNo={khoan.IdCongNo} loai={khoan.LoaiCongNo} " +
                              $"tenNguoi={tenDoiTac} hanTra={hanTraStr} trangThai={khoan.TrangThai}");

            if (khoan.HanTra.HasValue)
            {
                var hanTra = khoan.HanTra.Value.Date;

                // --- Quá hạn ---
                if (hanTra < today)
                {
                    var soNgayQuaHan = (today - hanTra).Days;
                    var loai = laChoVay ? "QUA_HAN_THU" : "QUA_HAN_TRA";

                    Console.WriteLine($"[Reminder]   → QUA_HAN soNgayQuaHan={soNgayQuaHan}");

                    if (!await DuplicateQuaHanExistsAsync(db, khoan.IdCongNo, loai, soNgayQuaHan))
                    {
                        Console.WriteLine($"[Reminder]   → TẠO {loai} mốc {soNgayQuaHan} ngày");
                        newNotifications.Add(new ThongBao
                        {
                            IdNguoiDung  = khoan.IdNguoiDung,
                            IdCongNo     = khoan.IdCongNo,
                            TieuDe       = laChoVay ? "Khoản cho vay QUÁ HẠN thu hồi!" : "Khoản nợ QUÁ HẠN!",
                            NoiDung      = laChoVay
                                ? $"Khoản cho vay {tenDoiTac} ({soTienFmt}) đã quá hạn thu hồi {soNgayQuaHan} ngày."
                                : $"Khoản nợ {tenDoiTac} ({soTienFmt}) đã quá hạn trả {soNgayQuaHan} ngày.",
                            LoaiThongBao = loai,
                            DaDoc        = false,
                            NgayTao      = VnNow()
                        });
                    }
                    else
                    {
                        Console.WriteLine($"[Reminder]   → SKIP duplicate {loai} mốc {soNgayQuaHan} ngày");
                    }
                }
                // --- Sắp đến hạn: trước 3 ngày ---
                else if (hanTra == today.AddDays(3))
                {
                    var loai = laChoVay ? "SAP_DEN_HAN_THU" : "SAP_DEN_HAN_TRA";
                    Console.WriteLine($"[Reminder]   → SAP_DEN_HAN trước 3 ngày");

                    if (!await DuplicateExistsAsync(db, khoan.IdCongNo, loai, today))
                    {
                        Console.WriteLine($"[Reminder]   → TẠO {loai}");
                        newNotifications.Add(new ThongBao
                        {
                            IdNguoiDung  = khoan.IdNguoiDung,
                            IdCongNo     = khoan.IdCongNo,
                            TieuDe       = laChoVay ? "Sắp đến hạn thu hồi khoản cho vay" : "Sắp đến hạn trả nợ",
                            NoiDung      = laChoVay
                                ? $"Khoản cho vay {tenDoiTac} ({soTienFmt}) sẽ đến hạn thu hồi sau 3 ngày."
                                : $"Khoản nợ {tenDoiTac} ({soTienFmt}) sẽ đến hạn trả sau 3 ngày.",
                            LoaiThongBao = loai,
                            DaDoc        = false,
                            NgayTao      = VnNow()
                        });
                    }
                    else
                    {
                        Console.WriteLine($"[Reminder]   → SKIP duplicate {loai}");
                    }
                }
                // --- Sắp đến hạn: trước 1 ngày ---
                else if (hanTra == today.AddDays(1))
                {
                    var loai = laChoVay ? "SAP_DEN_HAN_THU" : "SAP_DEN_HAN_TRA";
                    Console.WriteLine($"[Reminder]   → SAP_DEN_HAN trước 1 ngày");

                    if (!await DuplicateExistsAsync(db, khoan.IdCongNo, loai, today))
                    {
                        Console.WriteLine($"[Reminder]   → TẠO {loai}");
                        newNotifications.Add(new ThongBao
                        {
                            IdNguoiDung  = khoan.IdNguoiDung,
                            IdCongNo     = khoan.IdCongNo,
                            TieuDe       = laChoVay ? "Sắp đến hạn thu hồi khoản cho vay" : "Sắp đến hạn trả nợ",
                            NoiDung      = laChoVay
                                ? $"Khoản cho vay {tenDoiTac} ({soTienFmt}) sẽ đến hạn thu hồi vào ngày mai."
                                : $"Khoản nợ {tenDoiTac} ({soTienFmt}) sẽ đến hạn trả vào ngày mai.",
                            LoaiThongBao = loai,
                            DaDoc        = false,
                            NgayTao      = VnNow()
                        });
                    }
                    else
                    {
                        Console.WriteLine($"[Reminder]   → SKIP duplicate {loai}");
                    }
                }
                else
                {
                    Console.WriteLine($"[Reminder]   → Không match điều kiện nào (hanTra={hanTraStr})");
                }
            }
            else
            {
                // --- Nhắc 7 ngày định kỳ — không có HanTra ---
                if (khoan.NguoiDung?.NhacNo7Ngay != true)
                {
                    Console.WriteLine($"[Reminder]   → Không có HanTra, NhacNo7Ngay=false → skip");
                    continue;
                }

                var soNgay = (today - khoan.NgayPhatSinh.Date).Days;
                Console.WriteLine($"[Reminder]   → Không có HanTra, soNgay={soNgay}");

                if (soNgay <= 0 || soNgay % 7 != 0)
                {
                    Console.WriteLine($"[Reminder]   → soNgay={soNgay} không phải mốc 7 ngày → skip");
                    continue;
                }

                var loai = laChoVay ? "NHAC_THU_NO_7_NGAY" : "NHAC_TRA_NO_7_NGAY";

                if (!await DuplicateNhacExistsAsync(db, khoan.IdCongNo, loai, soNgay))
                {
                    Console.WriteLine($"[Reminder]   → TẠO {loai} mốc {soNgay} ngày");
                    newNotifications.Add(new ThongBao
                    {
                        IdNguoiDung  = khoan.IdNguoiDung,
                        IdCongNo     = khoan.IdCongNo,
                        TieuDe       = laChoVay ? "Nhắc thu hồi khoản cho vay" : "Nhắc trả khoản nợ",
                        NoiDung      = laChoVay
                            ? $"Khoản cho vay {tenDoiTac} ({soTienFmt}) đã phát sinh {soNgay} ngày nhưng chưa có hạn thu hồi."
                            : $"Khoản nợ {tenDoiTac} ({soTienFmt}) đã phát sinh {soNgay} ngày nhưng chưa có hạn trả.",
                        LoaiThongBao = loai,
                        DaDoc        = false,
                        NgayTao      = VnNow()
                    });
                }
                else
                {
                    Console.WriteLine($"[Reminder]   → SKIP duplicate {loai} mốc {soNgay} ngày");
                }
            }
        }

        Console.WriteLine($"[Reminder] Chuẩn bị insert {newNotifications.Count} notifications");

        if (newNotifications.Count > 0)
        {
            // Insert từng cái riêng lẻ để 1 cái lỗi không rollback toàn bộ batch
            var successCount = 0;
            foreach (var n in newNotifications)
            {
                try
                {
                    db.ThongBaos.Add(n);
                    await db.SaveChangesAsync();
                    successCount++;
                }
                catch (Exception ex)
                {
                    db.Entry(n).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                    Console.WriteLine($"[Reminder] FAILED insert IdCongNo={n.IdCongNo} loai={n.LoaiThongBao}: {ex.Message}");
                }
            }
            Console.WriteLine($"[Reminder] ===== DONE: {successCount}/{newNotifications.Count} inserted =====");
        }
        else
        {
            Console.WriteLine($"[Reminder] ===== DONE: No new notifications =====");
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

    // Dedupe cho NHAC_7_NGAY: check theo mốc soNgay, không phải ngày tạo.
    // Tránh tạo lại nếu đã nhắc đúng mốc này rồi (dù test nhiều lần trong ngày).
    private static async Task<bool> DuplicateNhacExistsAsync(
        SoChungDbContext db, long idCongNo, string loai, int soNgay)
    {
        // NoiDung chứa "{soNgay} ngày" — dùng để phân biệt mốc
        var marker = $"{soNgay} ngày";
        return await db.ThongBaos.AnyAsync(t =>
            t.IdCongNo == idCongNo &&
            t.LoaiThongBao == loai &&
            t.NoiDung.Contains(marker));
    }

    // Dedupe cho QUA_HAN: chỉ tạo 1 lần mỗi ngày quá hạn (không spam mỗi ngày).
    // Dùng same-day check — job chạy lại trong ngày không tạo thêm.
    // Job ngày hôm sau sẽ tạo thông báo mới với soNgayQuaHan tăng lên.
    // Để tránh spam, chỉ tạo thông báo quá hạn mỗi 7 ngày một lần.
    private static async Task<bool> DuplicateQuaHanExistsAsync(
        SoChungDbContext db, long idCongNo, string loai, int soNgayQuaHan)
    {
        // Tạo thông báo quá hạn tại mốc: 1, 7, 14, 21... ngày quá hạn
        // soNgayQuaHan == 1: lần đầu quá hạn
        // soNgayQuaHan % 7 == 0: mỗi tuần nhắc lại
        var isMilestone = soNgayQuaHan == 1 || soNgayQuaHan % 7 == 0;
        if (!isMilestone) return true; // không phải mốc → coi như duplicate để skip

        var marker = $"{soNgayQuaHan} ngày";
        return await db.ThongBaos.AnyAsync(t =>
            t.IdCongNo == idCongNo &&
            t.LoaiThongBao == loai &&
            t.NoiDung.Contains(marker));
    }
}
