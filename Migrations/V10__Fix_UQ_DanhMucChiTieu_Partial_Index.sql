-- Drop constraint cũ (nếu tồn tại) và tạo lại dưới dạng partial unique index
-- chỉ enforce unique trên (IdNguoiDung, TenDanhMuc) khi IdNguoiDung IS NOT NULL
-- → shared categories (IdNguoiDung = null) không bị ràng buộc unique với nhau
-- → user-specific categories vẫn unique per user
-- → user được phép có category cùng tên với shared category

DROP INDEX IF EXISTS "UQ_DanhMucChiTieu_IdNguoiDung_TenDanhMuc";

CREATE UNIQUE INDEX "UQ_DanhMucChiTieu_IdNguoiDung_TenDanhMuc"
    ON "DanhMucChiTieu" ("IdNguoiDung", "TenDanhMuc")
    WHERE "IdNguoiDung" IS NOT NULL;
