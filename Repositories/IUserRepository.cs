using ExpenseManagerAPI.Models;

namespace ExpenseManagerAPI.Repositories;

public interface IUserRepository
{
    Task<NguoiDung?> FindByEmailAsync(string email);
    Task<NguoiDung?> FindByIdAsync(long id);
    Task<bool> EmailExistsAsync(string email);
    Task AddUserAsync(NguoiDung user);
    Task SaveChangesAsync();
}
