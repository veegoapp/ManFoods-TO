-- =============================================
-- Manfoods McDonald's — DB Migration Script
-- شغّل: psql "$NEON_DATABASE_URL" -f scripts/migrate.sql
-- =============================================

-- ── users ─────────────────────────────────────
CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL DEFAULT '',
    role TEXT NOT NULL DEFAULT '',
    assigned_name TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ── active_employees ──────────────────────────
CREATE TABLE IF NOT EXISTS active_employees (
    id SERIAL PRIMARY KEY,
    employee_id TEXT NOT NULL DEFAULT '',
    name TEXT NOT NULL DEFAULT '',
    store TEXT NOT NULL DEFAULT '',
    job_title TEXT NOT NULL DEFAULT '',
    gender TEXT NOT NULL DEFAULT '',
    hire_date DATE,
    month INTEGER NOT NULL DEFAULT 0,
    year INTEGER NOT NULL DEFAULT 0
);

-- ── resignations ──────────────────────────────
CREATE TABLE IF NOT EXISTS resignations (
    id SERIAL PRIMARY KEY,
    employee_id TEXT NOT NULL DEFAULT '',
    name TEXT NOT NULL DEFAULT '',
    store TEXT NOT NULL DEFAULT '',
    job_title TEXT NOT NULL DEFAULT '',
    gender TEXT NOT NULL DEFAULT '',
    hire_date DATE,
    resignation_date DATE,
    tenure_months INTEGER NOT NULL DEFAULT 0,
    month INTEGER NOT NULL DEFAULT 0,
    year INTEGER NOT NULL DEFAULT 0
);

-- ── store_references ──────────────────────────
CREATE TABLE IF NOT EXISTS store_references (
    id SERIAL PRIMARY KEY,
    store_name TEXT NOT NULL DEFAULT '',
    region TEXT NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

-- ── upload_logs ───────────────────────────────
CREATE TABLE IF NOT EXISTS upload_logs (
    id SERIAL PRIMARY KEY,
    file_type TEXT NOT NULL DEFAULT '',
    file_name TEXT NOT NULL DEFAULT '',
    month INTEGER NOT NULL DEFAULT 0,
    year INTEGER NOT NULL DEFAULT 0,
    upload_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    uploaded_by TEXT NOT NULL DEFAULT '',
    file_content BYTEA,
    content_type TEXT
);

-- ── seed users ────────────────────────────────
-- admin@mcd.com / 123123654  →  Admin portal
-- user@mcd.com  / 123123654  →  Home portal
INSERT INTO users (email, password_hash, role, assigned_name, created_at)
VALUES
    ('admin@mcd.com', '$2a$11$9P3qHK/vfR4g8c99FKA.regSIy2D6QIAQhgVx4JWnn6BqFTxvA.mC', 'Admin_Full', 'Admin', NOW()),
    ('user@mcd.com',  '$2a$11$9P3qHK/vfR4g8c99FKA.regSIy2D6QIAQhgVx4JWnn6BqFTxvA.mC', 'Viewer',     'User',  NOW())
ON CONFLICT (email) DO NOTHING;
