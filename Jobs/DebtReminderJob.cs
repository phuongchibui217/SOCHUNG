using ExpenseManagerAPI.Helpers;
using ExpenseManagerAPI.Services;

namespace ExpenseManagerAPI.Jobs;

/// <summary>
/// Background job chạy mỗi ngày lúc 09:00 AM (UTC+7).
/// Quét công nợ và tạo thông báo nhắc nợ.
/// </summary>
public class DebtReminderJob : BackgroundService
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<DebtReminderJob> _logger;

    public DebtReminderJob(
        INotificationService notificationService,
        ILogger<DebtReminderJob> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DebtReminderJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = CalculateDelayUntilNextRun();
            _logger.LogInformation("DebtReminderJob: next run in {Delay}", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                _logger.LogInformation("DebtReminderJob: running at {Time}", TimeZoneHelper.NowVn());

                await _notificationService.RunDailyReminderAsync();

                _logger.LogInformation("DebtReminderJob: completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DebtReminderJob: error during execution.");
            }
        }

        _logger.LogInformation("DebtReminderJob stopped.");
    }

    /// <summary>
    /// Tính thời gian chờ đến lần chạy tiếp theo lúc 09:00 AM (UTC+7).
    /// </summary>
    private static TimeSpan CalculateDelayUntilNextRun()
    {
        var vnTz   = TimeZoneHelper.VnTimeZone;
        var nowVn  = TimeZoneHelper.NowVn();
        var nextRun = nowVn.Date.AddHours(9);

        if (nowVn >= nextRun)
            nextRun = nextRun.AddDays(1);

        var nextRunUtc = TimeZoneInfo.ConvertTimeToUtc(nextRun, vnTz);
        return nextRunUtc - DateTime.UtcNow;
    }
}
