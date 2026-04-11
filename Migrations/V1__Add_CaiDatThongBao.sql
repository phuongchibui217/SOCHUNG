-- ============================================================
-- Migration: Thêm bảng CaiDatThongBao
-- Mô tả: Lưu cấu hình thông báo của từng người dùng,
--        bao gồm tính năng tự động nhắc nợ sau 7 ngày.
-- Timezone hệ thống: UTC+7 (SE Asia Standard Time)
-- ============================================================

CREATE TABLE [dbo].[CaiDatThongBao] (
    [IdCaiDat]           INT           NOT NULL IDENTITY(1,1),
    [IdNguoiDung]        INT           NOT NULL,
    [TuDongNhacSau7Ngay] BIT           NOT NULL CONSTRAINT [DF_CaiDatThongBao_TuDongNhacSau7Ngay] DEFAULT (0),
    [NgayTao]            DATETIME      NOT NULL CONSTRAINT [DF_CaiDatThongBao_NgayTao]            DEFAULT (GETDATE()),
    [NgayCapNhat]        DATETIME      NOT NULL CONSTRAINT [DF_CaiDatThongBao_NgayCapNhat]        DEFAULT (GETDATE()),

    CONSTRAINT [PK_CaiDatThongBao]         PRIMARY KEY CLUSTERED ([IdCaiDat]),
    CONSTRAINT [FK_CaiDatThongBao_NguoiDung] FOREIGN KEY ([IdNguoiDung])
        REFERENCES [dbo].[NguoiDung] ([IdNguoiDung]) ON DELETE CASCADE,
    -- Mỗi user chỉ có 1 bản ghi cài đặt
    CONSTRAINT [UQ_CaiDatThongBao_IdNguoiDung] UNIQUE ([IdNguoiDung])
);
GO

-- Index hỗ trợ job quét: lọc nhanh user có bật tính năng nhắc 7 ngày
CREATE INDEX [IX_CaiDatThongBao_TuDongNhacSau7Ngay]
    ON [dbo].[CaiDatThongBao] ([TuDongNhacSau7Ngay])
    WHERE [TuDongNhacSau7Ngay] = 1;
GO
