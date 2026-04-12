using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.DTOs;
using ExpenseManagerAPI.Models;
using ExpenseManagerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExpenseManagerAPI.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly SoChungDbContext _db;
    private readonly INotificationService _notificationService;

    public NotificationsController(SoChungDbContext db, INotificationService notificationService)
    {
        _db = db;
        _notificationService = notificationService;
    }

    private long GetCurrentUserId() =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException());

    // GET /api/notifications — danh sách thông báo của user hiện tại
    [HttpGet]
    public async Task<IActionResult> GetMyNotifications()
    {
        long userId;
        try { userId = GetCurrentUserId(); }
        catch { return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." }); }

        try
        {
            // LEFT JOIN CongNo để lấy LoaiCongNo cho data cũ chưa có DebtType
            // Dùng t.CongNo (nullable navigation) thay vì t.CongNo! để tránh crash khi IdCongNo null
            var raw = await _db.ThongBaos
                .Where(t => t.IdNguoiDung == userId)
                .OrderByDescending(t => t.NgayTao)
                .Select(t => new
                {
                    t.IdThongBao,
                    t.IdCongNo,
                    CongNoLoai   = t.CongNo != null ? t.CongNo.LoaiCongNo : null,
                    t.TieuDe,
                    t.NoiDung,
                    t.LoaiThongBao,
                    t.DaDoc,
                    t.NgayTao
                })
                .ToListAsync();

            // Map sang anonymous object với field names FE đang dùng
            var data = raw.Select(t => new
            {
                id               = t.IdThongBao,
                notificationId   = t.IdThongBao,   // alias cho FE dùng cả 2 tên
                debtId           = t.IdCongNo,
                relatedDebtId    = t.IdCongNo,      // alias
                debtType         = t.CongNoLoai,
                transactionType  = t.CongNoLoai,    // alias
                loaiThongBao     = t.LoaiThongBao,
                notificationType = t.LoaiThongBao,  // alias
                title            = t.TieuDe,
                tieuDe           = t.TieuDe,        // alias
                content          = t.NoiDung,
                noiDung          = t.NoiDung,        // alias
                isRead           = t.DaDoc,
                daDoc            = t.DaDoc,          // alias
                createdAt        = t.NgayTao,
                ngayTao          = t.NgayTao         // alias
            }).ToList();

            Console.WriteLine($"[Notifications] GET userId={userId} count={data.Count}");
            foreach (var n in data)
            {
                Console.WriteLine(
                    $"[Notification] id={n.id} loaiThongBao={n.loaiThongBao} " +
                    $"debtId={n.debtId?.ToString() ?? "null"} debtType={n.debtType ?? "null"}");
            }

            return Ok(new { message = "Lấy danh sách thông báo thành công", data });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Notifications] GET ERROR userId={userId}: {ex.Message}\n{ex.StackTrace}");
            return StatusCode(500, new { message = "Không thể tải thông báo. Vui lòng thử lại.", error = ex.Message });
        }
    }

    // POST /api/notifications/mark-all-read — đánh dấu tất cả đã đọc
    // POST /api/notifications/read-all       — alias cho FE
    // Idempotent: gọi nhiều lần vẫn success, không lỗi nếu không có gì cần update
    [HttpPost("mark-all-read")]
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = GetCurrentUserId();

        try
        {
            var updatedCount = await _db.ThongBaos
                .Where(t => t.IdNguoiDung == userId && !t.DaDoc)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DaDoc, true));

            // Đếm lại unreadCount sau khi update — phải là 0 nếu update thành công
            var unreadCount = await _db.ThongBaos
                .CountAsync(t => t.IdNguoiDung == userId && !t.DaDoc);

            Console.WriteLine($"[Notifications] MarkAllRead userId={userId} updatedCount={updatedCount} unreadCount={unreadCount}");

            return Ok(new
            {
                success     = true,
                updatedCount,
                unreadCount
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Notifications] MarkAllRead ERROR userId={userId}: {ex.Message}");
            return StatusCode(500, new { success = false, message = "Không thể cập nhật thông báo. Vui lòng thử lại." });
        }
    }

    // GET /api/notifications/unread-count — đếm thông báo chưa đọc của user hiện tại
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        long userId;
        try { userId = GetCurrentUserId(); }
        catch { return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." }); }

        try
        {
            var unreadCount = await _db.ThongBaos
                .CountAsync(t => t.IdNguoiDung == userId && !t.DaDoc);

            Console.WriteLine($"[Notifications] unread-count userId={userId} count={unreadCount}");
            return Ok(new { unreadCount });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Notifications] unread-count ERROR: {ex.Message}");
            return Ok(new { unreadCount = 0 }); // fallback an toàn, không crash FE
        }
    }

    // POST /api/notifications/{id}/read — đánh dấu từng thông báo đã đọc
    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkOneRead(long id)
    {
        var userId = GetCurrentUserId();

        var entity = await _db.ThongBaos
            .FirstOrDefaultAsync(t => t.IdThongBao == id && t.IdNguoiDung == userId);

        if (entity == null)
            return NotFound(new { message = "Thông báo không tồn tại." });

        entity.DaDoc = true;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Đã đánh dấu đã đọc." });
    }

    // PUT /api/notifications/{id}/read — giữ lại để tương thích
    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(long id)
        => await MarkOneRead(id);

    // GET /api/notifications/{userId} — giữ lại để tương thích
    [HttpGet("{userId}")]
    public async Task<IActionResult> GetByUser(long userId)
    {
        var list = await _db.ThongBaos
            .Where(t => t.IdNguoiDung == userId)
            .OrderByDescending(t => t.NgayTao)
            .Select(t => new ThongBaoResponseDto
            {
                IdThongBao = t.IdThongBao,
                IdNguoiDung = t.IdNguoiDung,
                IdCongNo = t.IdCongNo,
                TieuDe = t.TieuDe,
                NoiDung = t.NoiDung,
                LoaiThongBao = t.LoaiThongBao,
                DaDoc = t.DaDoc,
                NgayTao = t.NgayTao
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET /api/notification-settings — lấy cài đặt thông báo của user hiện tại
    [HttpGet("/api/notification-settings")]
    public async Task<IActionResult> GetNotificationSettings()
    {
        var userId = GetCurrentUserId();
        var data = await _notificationService.GetCaiDatAsync(userId);
        return Ok(new { message = "Lấy cài đặt thông báo thành công", data });
    }

    // PUT /api/notification-settings — lưu cài đặt thông báo
    [HttpPut("/api/notification-settings")]
    public async Task<IActionResult> UpdateNotificationSettings([FromBody] CapNhatCaiDatThongBaoDto dto)
    {
        var userId = GetCurrentUserId();
        var data = await _notificationService.UpsertCaiDatAsync(userId, dto);
        return Ok(new { message = "Lưu cài đặt thành công", data });
    }
}
