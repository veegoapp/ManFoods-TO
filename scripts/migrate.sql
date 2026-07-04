-- =============================================
-- Manfoods McDonald's — DB Migration Script
-- شغّل: psql "$NEON_DATABASE_URL" -f scripts/migrate.sql
-- =============================================

-- ── users ─────────────────────────────────────
-- password_hash is nullable: bulk-created accounts start "pending" (no
-- password) until the OTP self-activation flow sets one.
CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    phone TEXT NOT NULL DEFAULT '',
    password_hash TEXT,
    role TEXT NOT NULL DEFAULT '',
    assigned_name TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
-- CREATE TABLE IF NOT EXISTS is a no-op on a pre-existing table, so backfill
-- explicitly for databases that already had the old shape.
ALTER TABLE users ADD COLUMN IF NOT EXISTS phone TEXT NOT NULL DEFAULT '';
ALTER TABLE users ALTER COLUMN password_hash DROP NOT NULL;

-- Role system simplified from Admin_Full/Admin_Read/Operation_Manager/
-- Operation_Consultant/Viewer down to just Admin/User.
UPDATE users SET role = 'Admin' WHERE role IN ('Admin_Full', 'Admin_Read');
UPDATE users SET role = 'User' WHERE role IN ('Operation_Manager', 'Operation_Consultant', 'Viewer');

-- ── password_reset_otps ────────────────────────
-- OTPs for the self-service "forgot password" flow (User accounts only —
-- Admin accounts use the separate master-key recovery flow). 4h expiry,
-- single use, invalidated after 5 failed attempts.
CREATE TABLE IF NOT EXISTS password_reset_otps (
    id SERIAL PRIMARY KEY,
    user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    otp_code TEXT NOT NULL DEFAULT '',
    expires_at TIMESTAMPTZ NOT NULL,
    is_used BOOLEAN NOT NULL DEFAULT FALSE,
    failed_attempts INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ── app_settings ───────────────────────────────
-- Small key/value store for config that isn't tied to any entity — right
-- now just the admin recovery key hash (bcrypt, same as passwords). Not an
-- env var/Secret: this way there is nothing extra to configure outside the
-- database, and it can be rotated later from within the app if needed.
CREATE TABLE IF NOT EXISTS app_settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL DEFAULT ''
);
-- Seeds the recovery key hash for the key already generated and handed to
-- the admin — ON CONFLICT DO NOTHING so re-running this script never
-- silently resets a key that's since been rotated.
INSERT INTO app_settings (key, value)
VALUES ('admin_recovery_key_hash', '$2b$11$24/KLaFMtFEfWIHLPFgbsudQs/B1SN/EVztSlE7u4ff0QAMiMS.sC')
ON CONFLICT (key) DO NOTHING;

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

-- ── exit_interviews ────────────────────────────
-- One row per Microsoft Forms exit-interview submission. No name / national
-- ID is stored — employee_id is kept only to resolve store/leader/OC/OM at
-- upload time and must never be surfaced in any view or API response.
CREATE TABLE IF NOT EXISTS exit_interviews (
    id SERIAL PRIMARY KEY,
    forms_response_id TEXT NOT NULL DEFAULT '',
    employee_id TEXT NOT NULL DEFAULT '',
    store TEXT NOT NULL DEFAULT '',
    store_leader TEXT NOT NULL DEFAULT '',
    operation_consultant TEXT NOT NULL DEFAULT '',
    operation_manager TEXT NOT NULL DEFAULT '',
    job_title TEXT NOT NULL DEFAULT '',
    month INTEGER NOT NULL DEFAULT 0,
    year INTEGER NOT NULL DEFAULT 0,
    submitted_at TIMESTAMPTZ,

    reason_for_leaving TEXT NOT NULL DEFAULT '',
    would_return TEXT NOT NULL DEFAULT '',
    overall_experience TEXT NOT NULL DEFAULT '',
    workload_condition TEXT NOT NULL DEFAULT '',
    fair_treatment TEXT NOT NULL DEFAULT '',
    encourage_opinions TEXT NOT NULL DEFAULT '',
    complaints_handling TEXT NOT NULL DEFAULT '',
    benefits_match TEXT NOT NULL DEFAULT '',
    teamwork TEXT NOT NULL DEFAULT '',
    communication TEXT NOT NULL DEFAULT '',
    task_fit TEXT NOT NULL DEFAULT '',
    training TEXT NOT NULL DEFAULT '',
    feedback TEXT NOT NULL DEFAULT '',
    use_personal_abilities TEXT NOT NULL DEFAULT '',

    reason_other_text TEXT,
    work_pressure_reason_text TEXT,
    what_would_change_text TEXT,
    what_learned_text TEXT,
    final_comments_text TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_exit_interviews_forms_response_id
    ON exit_interviews (forms_response_id) WHERE forms_response_id <> '';

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

-- CREATE TABLE IF NOT EXISTS is a no-op on a table that already exists with
-- an older shape, so columns added after the table's first deploy (like
-- these two) never land on existing databases. Backfill them explicitly.
ALTER TABLE upload_logs ADD COLUMN IF NOT EXISTS file_content BYTEA;
ALTER TABLE upload_logs ADD COLUMN IF NOT EXISTS content_type TEXT;

-- ── ai_usage_daily ────────────────────────────
CREATE TABLE IF NOT EXISTS ai_usage_daily (
    user_id INTEGER NOT NULL,
    usage_date DATE NOT NULL,
    question_count INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, usage_date)
);

-- ── seed users ────────────────────────────────
-- admin@mcd.com / 123123654  →  Admin portal
-- user@mcd.com  / 123123654  →  Home portal
INSERT INTO users (email, phone, password_hash, role, created_at)
VALUES
    ('admin@mcd.com', '+201000000000', '$2a$11$4dMAuH6DiUfgnniQT39r1uof2UmVIJQ2vslu8qs8OwOJ7EUM1i/n6', 'Admin', NOW()),
    ('user@mcd.com',  '+201000000001', '$2a$11$4dMAuH6DiUfgnniQT39r1uof2UmVIJQ2vslu8qs8OwOJ7EUM1i/n6', 'User',  NOW())
ON CONFLICT (email) DO NOTHING;
