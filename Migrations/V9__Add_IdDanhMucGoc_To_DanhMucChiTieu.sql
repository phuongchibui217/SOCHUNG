-- Thêm cột IdDanhMucGoc để lưu liên kết override từ category chung
-- null  = category bình thường (chung hoặc của user)
-- có giá trị = record override của user cho 1 category chung (dùng để ẩn/tùy chỉnh)
ALTER TABLE "DanhMucChiTieu"
    ADD COLUMN IF NOT EXISTS "IdDanhMucGoc" BIGINT NULL
        REFERENCES "DanhMucChiTieu"("IdDanhMuc") ON DELETE SET NULL;
