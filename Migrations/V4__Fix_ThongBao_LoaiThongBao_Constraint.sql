-- Migration V4: Mở rộng CHECK constraint LoaiThongBao
-- Thêm 'GIAO_DICH_THANH_CONG' vào danh sách hợp lệ
-- Code NotificationService.CreatePaymentSuccessNotificationAsync insert loại này

-- Bước 1: Drop constraint cũ (tên auto-generated bởi SQL Server)
DECLARE @constraintName NVARCHAR(200) = (
    SELECT name FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('ThongBao')
      AND CHARINDEX('LoaiThongBao', definition) > 0
);
IF @constraintName IS NOT NULL
    EXEC('ALTER TABLE ThongBao DROP CONSTRAINT [' + @constraintName + ']');

-- Bước 2: Tạo lại constraint với đầy đủ các loại
ALTER TABLE ThongBao
    ADD CONSTRAINT CK_ThongBao_LoaiThongBao
    CHECK (LoaiThongBao IN ('SAP_DEN_HAN', 'QUA_HAN', 'NHAC_7_NGAY', 'GIAO_DICH_THANH_CONG'));
