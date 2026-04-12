-- Migration V6: Cleanup dữ liệu ThongBao có LoaiThongBao không hợp lệ
-- Các record cũ có thể có type = 'SYSTEM' hoặc giá trị không nằm trong constraint
-- Map lại dựa trên CongNo liên quan (LoaiCongNo, HanTra)

-- Bước 1: Update record SYSTEM có IdCongNo → map theo loại công nợ và trạng thái
UPDATE "ThongBao" t
SET "LoaiThongBao" = CASE
    WHEN c."LoaiCongNo" = 'CHO_VAY' AND c."HanTra" IS NOT NULL AND c."HanTra"::date < CURRENT_DATE
        THEN 'QUA_HAN_THU'
    WHEN c."LoaiCongNo" = 'NO'      AND c."HanTra" IS NOT NULL AND c."HanTra"::date < CURRENT_DATE
        THEN 'QUA_HAN_TRA'
    WHEN c."LoaiCongNo" = 'CHO_VAY' AND c."HanTra" IS NOT NULL
        THEN 'SAP_DEN_HAN_THU'
    WHEN c."LoaiCongNo" = 'NO'      AND c."HanTra" IS NOT NULL
        THEN 'SAP_DEN_HAN_TRA'
    WHEN c."LoaiCongNo" = 'CHO_VAY' AND c."HanTra" IS NULL
        THEN 'NHAC_THU_NO_7_NGAY'
    ELSE 'NHAC_TRA_NO_7_NGAY'
END
FROM "CongNo" c
WHERE t."IdCongNo" = c."IdCongNo"
  AND t."LoaiThongBao" NOT IN (
      'SAP_DEN_HAN', 'QUA_HAN', 'NHAC_7_NGAY',
      'SAP_DEN_HAN_TRA', 'SAP_DEN_HAN_THU',
      'QUA_HAN_TRA', 'QUA_HAN_THU',
      'NHAC_TRA_NO_7_NGAY', 'NHAC_THU_NO_7_NGAY',
      'GIAO_DICH_THANH_CONG'
  );

-- Bước 2: Record không có IdCongNo và type không hợp lệ → dùng generic fallback
UPDATE "ThongBao"
SET "LoaiThongBao" = 'NHAC_TRA_NO_7_NGAY'
WHERE "IdCongNo" IS NULL
  AND "LoaiThongBao" NOT IN (
      'SAP_DEN_HAN', 'QUA_HAN', 'NHAC_7_NGAY',
      'SAP_DEN_HAN_TRA', 'SAP_DEN_HAN_THU',
      'QUA_HAN_TRA', 'QUA_HAN_THU',
      'NHAC_TRA_NO_7_NGAY', 'NHAC_THU_NO_7_NGAY',
      'GIAO_DICH_THANH_CONG'
  );
