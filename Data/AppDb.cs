namespace ExpenseManagerAPI.Data;

/// <summary>
/// Static methods được map sang PostgreSQL DB functions qua HasDbFunction.
/// Chỉ dùng trong LINQ-to-EF queries — không gọi trực tiếp ở app layer.
/// </summary>
public static class AppDb
{
    /// <summary>
    /// Map sang PostgreSQL unaccent() — loại bỏ dấu tiếng Việt/Latin.
    /// "nhà" → "nha", "cà phê" → "ca phe"
    /// Kết hợp với ILIKE để tìm kiếm accent-insensitive + case-insensitive.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "EF1001")]
    public static string Unaccent(string value)
        => throw new InvalidOperationException("AppDb.Unaccent chỉ dùng trong EF LINQ query.");
}
