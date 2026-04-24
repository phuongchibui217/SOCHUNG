using ExpenseManagerAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManagerAPI.Controllers;

/// <summary>
/// Dev-only endpoints — chỉ hoạt động ở môi trường Development.
/// Không expose ở production.
/// </summary>
[ApiController]
[Route("api/dev")]
[Authorize]  // bảo vệ thêm lớp 2 phòng khi env bị set sai
public class DevController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly IWebHostEnvironment _env;

    public DevController(INotificationService notificationService, IWebHostEnvironment env)
    {
        _notificationService = notificationService;
        _env = env;
    }

    // POST /api/dev/test-reminder
    // Trigger reminder thủ công để test, không cần chờ 09:00 AM.
    [HttpPost("test-reminder")]
    public async Task<IActionResult> TestReminder()
    {
        if (!_env.IsDevelopment())
            return StatusCode(403, new { message = "Endpoint này chỉ khả dụng ở môi trường Development." });

        await _notificationService.RunDailyReminderAsync();

        return Ok(new { message = "Reminder triggered" });
    }
}
