-- Migration V4: Xóa cột TenDangNhap khỏi NguoiDung
-- Hệ thống chỉ dùng Email để định danh người dùng

-- Drop unique index trước khi drop cột
DROP INDEX IF EXISTS [UQ__NguoiDun__A9D10534XXXXXXXX] ON NguoiDung;

-- Tìm và drop constraint/index theo tên thực tế
DECLARE @indexName NVARCHAR(200) = (
    SELECT i.name FROM sys.indexes i
    JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    WHERE i.object_id = OBJECT_ID('NguoiDung')
      AND c.name = 'TenDangNhap'
      AND i.is_unique = 1
);
IF @indexName IS NOT NULL
    EXEC('DROP INDEX [' + @indexName + '] ON NguoiDung');

ALTER TABLE NguoiDung DROP COLUMN TenDangNhap;
