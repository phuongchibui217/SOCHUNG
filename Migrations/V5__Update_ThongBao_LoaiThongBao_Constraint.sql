-- Migration V5: Mở rộng CHECK constraint LoaiThongBao
-- Tách loại thông báo theo chiều công nợ (NO / CHO_VAY) để FE render đúng
--
-- Type mới:
--   NHAC_TRA_NO_7_NGAY   — nhắc 7 ngày, khoản nợ phải trả (NO)
--   NHAC_THU_NO_7_NGAY   — nhắc 7 ngày, khoản cho vay (CHO_VAY)
--   SAP_DEN_HAN_TRA      — sắp đến hạn, khoản nợ phải trả
--   SAP_DEN_HAN_THU      — sắp đến hạn, khoản cho vay
--   QUA_HAN_TRA          — quá hạn, khoản nợ phải trả
--   QUA_HAN_THU          — quá hạn, khoản cho vay
--
-- Type cũ giữ lại để không break dữ liệu lịch sử:
--   SAP_DEN_HAN, QUA_HAN, NHAC_7_NGAY, GIAO_DICH_THANH_CONG

-- PostgreSQL: drop constraint cũ và tạo lại
ALTER TABLE "ThongBao" DROP CONSTRAINT IF EXISTS "CK_ThongBao_LoaiThongBao";

ALTER TABLE "ThongBao"
    ADD CONSTRAINT "CK_ThongBao_LoaiThongBao"
    CHECK ("LoaiThongBao" IN (
        -- Legacy (giữ lại cho dữ liệu cũ)
        'SAP_DEN_HAN',
        'QUA_HAN',
        'NHAC_7_NGAY',
        'GIAO_DICH_THANH_CONG',
        -- Mới: tách theo chiều công nợ
        'SAP_DEN_HAN_TRA',
        'SAP_DEN_HAN_THU',
        'QUA_HAN_TRA',
        'QUA_HAN_THU',
        'NHAC_TRA_NO_7_NGAY',
        'NHAC_THU_NO_7_NGAY'
    ));
