-- Migration V3: Thêm cột NhacNo7Ngay vào NguoiDung
-- Thay thế bảng CaiDatThongBao — setting đơn giản không cần bảng riêng
ALTER TABLE NguoiDung
    ADD NhacNo7Ngay BIT NOT NULL DEFAULT 0;
