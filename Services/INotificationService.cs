using ExpenseManagerAPI.DTOs;

namespace ExpenseManagerAPI.Services;

public interface INotificationService
{
    Task RunDailyReminderAsync();
    Task<CaiDatThongBaoDto> GetCaiDatAsync(long userId);
    Task<CaiDatThongBaoDto> UpsertCaiDatAsync(long userId, CapNhatCaiDatThongBaoDto dto);
}
