-- Migration V2: Thêm cột lockout vào bảng NguoiDung
ALTER TABLE NguoiDung
    ADD FailedAttempts INT NOT NULL DEFAULT 0,
        LockUntil DATETIME NULL;
