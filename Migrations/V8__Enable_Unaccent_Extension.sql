-- V8: Enable unaccent extension để hỗ trợ tìm kiếm không dấu (accent-insensitive)
-- Supabase/PostgreSQL đã có sẵn extension này, chỉ cần enable.
CREATE EXTENSION IF NOT EXISTS unaccent;
