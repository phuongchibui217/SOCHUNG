using ExpenseManagerAPI.Data;
using ExpenseManagerAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManagerAPI.Repositories;

public class UserRepository : IUserRepository
{
    private readonly SoChungDbContext _db;

    public UserRepository(SoChungDbContext db) => _db = db;

    public Task<NguoiDung?> FindByEmailAsync(string email)
        => _db.NguoiDungs.FirstOrDefaultAsync(u => u.Email == email);

    public Task<NguoiDung?> FindByIdAsync(long id)
        => _db.NguoiDungs.FirstOrDefaultAsync(u => u.IdNguoiDung == id);

    public Task<bool> EmailExistsAsync(string email)
        => _db.NguoiDungs.AnyAsync(u => u.Email == email);

    public async Task AddUserAsync(NguoiDung user)
        => await _db.NguoiDungs.AddAsync(user);

    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
