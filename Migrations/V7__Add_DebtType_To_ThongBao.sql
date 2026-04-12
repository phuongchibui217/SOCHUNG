-- V7: Thêm cột DebtType vào ThongBao để hỗ trợ deep link (E9.5)
-- Lưu LoaiCongNo (NO | CHO_VAY) trực tiếp vào ThongBao,
-- tránh phụ thuộc JOIN khi CongNo bị xóa mềm.

ALTER TABLE "ThongBao"
    ADD COLUMN IF NOT EXISTS "DebtType" VARCHAR(20) NULL;

-- Backfill data cũ: lấy LoaiCongNo từ CongNo liên kết
UPDATE "ThongBao" t
SET "DebtType" = c."LoaiCongNo"
FROM "CongNo" c
WHERE t."IdCongNo" = c."IdCongNo"
  AND t."DebtType" IS NULL;
