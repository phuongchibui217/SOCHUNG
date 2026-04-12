using ExpenseManagerAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManagerAPI.Data;

public class SoChungDbContext : DbContext
{
    public SoChungDbContext(DbContextOptions<SoChungDbContext> options) : base(options) { }

    public DbSet<NguoiDung> NguoiDungs { get; set; }
    public DbSet<DanhMucChiTieu> DanhMucChiTieus { get; set; }
    public DbSet<ChiTieu> ChiTieus { get; set; }
    public DbSet<CongNo> CongNos { get; set; }
    public DbSet<ThanhToanCongNo> ThanhToanCongNos { get; set; }
    public DbSet<ThongBao> ThongBaos { get; set; }
    public DbSet<TokenNguoiDung> TokenNguoiDungs { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Giữ nguyên tên PascalCase — không để Npgsql tự convert sang snake_case
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Tắt convention tự động lowercase của Npgsql
        modelBuilder.HasDefaultSchema(null);

        // Map AppDb.Unaccent() → PostgreSQL unaccent() function
        // Yêu cầu extension unaccent được cài trên DB (có sẵn trên Supabase):
        //   CREATE EXTENSION IF NOT EXISTS unaccent;
        modelBuilder.HasDbFunction(
            typeof(AppDb).GetMethod(nameof(AppDb.Unaccent), new[] { typeof(string) })!
        ).HasName("unaccent").IsBuiltIn(false);

        // NguoiDung
        modelBuilder.Entity<NguoiDung>(e =>
        {
            e.ToTable("NguoiDung");
            e.HasKey(x => x.IdNguoiDung);
            e.Property(x => x.IdNguoiDung).HasColumnName("IdNguoiDung");
            e.Property(x => x.Email).HasColumnName("Email").IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.MatKhauHash).HasColumnName("MatKhauHash").IsRequired();
            e.Property(x => x.NgayTao).HasColumnName("NgayTao").HasDefaultValueSql("now()");
            e.Property(x => x.DaXacMinhEmail).HasColumnName("DaXacMinhEmail").HasDefaultValue(false);
            e.Property(x => x.FailedAttempts).HasColumnName("FailedAttempts").HasDefaultValue(0);
            e.Property(x => x.LockUntil).HasColumnName("LockUntil").IsRequired(false);
            e.Property(x => x.NhacNo7Ngay).HasColumnName("NhacNo7Ngay").HasDefaultValue(true);
        });

        // DanhMucChiTieu
        modelBuilder.Entity<DanhMucChiTieu>(e =>
        {
            e.ToTable("DanhMucChiTieu");
            e.HasKey(x => x.IdDanhMuc);
            e.Property(x => x.IdDanhMuc).HasColumnName("IdDanhMuc");
            e.Property(x => x.IdNguoiDung).HasColumnName("IdNguoiDung");
            e.Property(x => x.IdDanhMucGoc).HasColumnName("IdDanhMucGoc").IsRequired(false);
            e.Property(x => x.TenDanhMuc).HasColumnName("TenDanhMuc").IsRequired();
            e.Property(x => x.Icon).HasColumnName("Icon");
            e.Property(x => x.MauSac).HasColumnName("MauSac").HasMaxLength(7);
            e.Property(x => x.NgayTao).HasColumnName("NgayTao").HasDefaultValueSql("now()");
            e.Property(x => x.DaXoa).HasColumnName("DaXoa").HasDefaultValue(false);
            e.HasIndex(x => new { x.IdNguoiDung, x.TenDanhMuc }).IsUnique()
                .HasDatabaseName("UQ_DanhMucChiTieu_IdNguoiDung_TenDanhMuc")
                .HasFilter("\"IdNguoiDung\" IS NOT NULL");  // chỉ enforce unique trong scope từng user, không ảnh hưởng shared (null)
            e.HasOne(x => x.NguoiDung)
                .WithMany(u => u.DanhMucChiTieus)
                .HasForeignKey(x => x.IdNguoiDung)
                .HasConstraintName("FK_DanhMuc_NguoiDung")
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ChiTieu
        modelBuilder.Entity<ChiTieu>(e =>
        {
            e.ToTable("ChiTieu");
            e.HasKey(x => x.IdChiTieu);
            e.Property(x => x.IdChiTieu).HasColumnName("IdChiTieu");
            e.Property(x => x.IdNguoiDung).HasColumnName("IdNguoiDung");
            e.Property(x => x.IdDanhMuc).HasColumnName("IdDanhMuc");
            e.Property(x => x.SoTien).HasColumnName("SoTien").HasColumnType("numeric(12,2)").IsRequired();
            e.Property(x => x.NoiDung).HasColumnName("NoiDung");
            e.Property(x => x.NgayChi).HasColumnName("NgayChi").HasColumnType("date").HasDefaultValueSql("current_date");
            e.Property(x => x.DaXoa).HasColumnName("DaXoa").HasDefaultValue(false);
            e.HasOne(x => x.NguoiDung)
                .WithMany(u => u.ChiTieus)
                .HasForeignKey(x => x.IdNguoiDung)
                .HasConstraintName("FK_ChiTieu_NguoiDung")
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DanhMucChiTieu)
                .WithMany(d => d.ChiTieus)
                .HasForeignKey(x => x.IdDanhMuc)
                .HasConstraintName("FK_ChiTieu_DanhMuc")
                .OnDelete(DeleteBehavior.Restrict);
        });

        // CongNo
        modelBuilder.Entity<CongNo>(e =>
        {
            e.ToTable("CongNo");
            e.HasKey(x => x.IdCongNo);
            e.Property(x => x.IdCongNo).HasColumnName("IdCongNo");
            e.Property(x => x.IdNguoiDung).HasColumnName("IdNguoiDung");
            e.Property(x => x.TenNguoi).HasColumnName("TenNguoi").IsRequired();
            e.Property(x => x.SoTien).HasColumnName("SoTien").HasColumnType("numeric(12,2)").IsRequired();
            e.Property(x => x.LoaiCongNo).HasColumnName("LoaiCongNo").IsRequired();
            e.Property(x => x.NoiDung).HasColumnName("NoiDung");
            e.Property(x => x.HanTra).HasColumnName("HanTra").HasColumnType("date");
            e.Property(x => x.TrangThai).HasColumnName("TrangThai").HasDefaultValue("CHUA_TRA").IsRequired();
            e.Property(x => x.DaXoa).HasColumnName("DaXoa").HasDefaultValue(false);
            e.Property(x => x.NgayPhatSinh).HasColumnName("NgayPhatSinh").HasDefaultValueSql("now()");
            e.HasOne(x => x.NguoiDung)
                .WithMany(u => u.CongNos)
                .HasForeignKey(x => x.IdNguoiDung)
                .HasConstraintName("FK_CongNo_NguoiDung")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ThanhToanCongNo
        modelBuilder.Entity<ThanhToanCongNo>(e =>
        {
            e.ToTable("ThanhToanCongNo");
            e.HasKey(x => x.IdThanhToan);
            e.Property(x => x.IdThanhToan).HasColumnName("IdThanhToan");
            e.Property(x => x.IdCongNo).HasColumnName("IdCongNo");
            e.Property(x => x.SoTienThanhToan).HasColumnName("SoTienThanhToan").HasColumnType("numeric(12,2)").IsRequired();
            e.Property(x => x.NgayThanhToan).HasColumnName("NgayThanhToan").HasColumnType("date").HasDefaultValueSql("current_date");
            e.Property(x => x.GhiChu).HasColumnName("GhiChu");
            e.HasOne(x => x.CongNo)
                .WithMany(c => c.ThanhToanCongNos)
                .HasForeignKey(x => x.IdCongNo)
                .HasConstraintName("FK_ThanhToan_CongNo")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ThongBao
        modelBuilder.Entity<ThongBao>(e =>
        {
            e.ToTable("ThongBao");
            e.HasKey(x => x.IdThongBao);
            e.Property(x => x.IdThongBao).HasColumnName("IdThongBao");
            e.Property(x => x.IdNguoiDung).HasColumnName("IdNguoiDung");
            e.Property(x => x.IdCongNo).HasColumnName("IdCongNo");
            e.Property(x => x.TieuDe).HasColumnName("TieuDe").IsRequired();
            e.Property(x => x.NoiDung).HasColumnName("NoiDung").IsRequired();
            e.Property(x => x.LoaiThongBao).HasColumnName("LoaiThongBao").IsRequired();
            e.Property(x => x.DaDoc).HasColumnName("DaDoc").HasDefaultValue(false);
            e.Property(x => x.NgayTao).HasColumnName("NgayTao").HasDefaultValueSql("now()");
            e.HasOne(x => x.NguoiDung)
                .WithMany(u => u.ThongBaos)
                .HasForeignKey(x => x.IdNguoiDung)
                .HasConstraintName("FK_ThongBao_NguoiDung")
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CongNo)
                .WithMany(c => c.ThongBaos)
                .HasForeignKey(x => x.IdCongNo)
                .HasConstraintName("FK_ThongBao_CongNo")
                .OnDelete(DeleteBehavior.NoAction);
        });

        // TokenNguoiDung
        modelBuilder.Entity<TokenNguoiDung>(e =>
        {
            e.ToTable("TokenNguoiDung");
            e.HasKey(x => x.IdToken);
            e.Property(x => x.IdToken).HasColumnName("IdToken");
            e.Property(x => x.IdNguoiDung).HasColumnName("IdNguoiDung");
            e.Property(x => x.MaToken).HasColumnName("MaToken").IsRequired();
            e.HasIndex(x => x.MaToken).IsUnique();
            e.Property(x => x.LoaiToken).HasColumnName("LoaiToken").IsRequired();
            e.Property(x => x.HetHan).HasColumnName("HetHan");
            e.Property(x => x.DaDung).HasColumnName("DaDung").HasDefaultValue(false);
            e.Property(x => x.NgayTao).HasColumnName("NgayTao").HasDefaultValueSql("now()");
            e.HasOne(x => x.NguoiDung)
                .WithMany(u => u.TokenNguoiDungs)
                .HasForeignKey(x => x.IdNguoiDung)
                .HasConstraintName("FK_TokenNguoiDung_NguoiDung")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
