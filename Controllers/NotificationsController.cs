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
        var userId = GetCurrentUserId();

        var data = await _db.ThongBaos
            .Where(t => t.IdNguoiDung == userId)
            .OrderByDescending(t => t.NgayTao)
            .Select(t => new NotificationDto
            {
                NotificationId = t.IdThongBao,
                DebtId = t.IdCongNo,
                Title = t.TieuDe,
                Content = t.NoiDung,
                NotificationType = t.LoaiThongBao,
                IsRead = t.DaDoc,
                CreatedAt = t.NgayTao
            })
            .ToListAsync();

        return Ok(new { message = "Lấy danh sách thông báo thành công", data });
    }

    // POST /api/notifications/mark-all-read — đánh dấu tất cả đã đọc
    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = GetCurrentUserId();

        await _db.ThongBaos
            .Where(t => t.IdNguoiDung == userId && !t.DaDoc)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.DaDoc, true));

        return Ok(new { message = "Đã đánh dấu tất cả thông báo là đã đọc" });
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
