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

-- One-time historical cleanup (already applied): Admin_Full/Admin_Read were
-- folded into Admin, and Viewer into User. Operation_Manager/Operation_Consultant
-- are valid role values again (per-store access restriction) — do NOT add a
-- rewrite-to-User statement here, since this script is re-run on every deploy
-- and would silently wipe out live OM/OC role assignments.
UPDATE users SET role = 'Admin' WHERE role IN ('Admin_Full', 'Admin_Read');
UPDATE users SET role = 'User' WHERE role = 'Viewer';

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

-- The actual table backing Models/StoreReference.cs is "store_reference"
-- (singular, EnsureCreated()-managed) — unrelated to "store_references" above.
-- Backfill the OM/OC email columns used for per-store access restriction.
ALTER TABLE store_reference ADD COLUMN IF NOT EXISTS operation_manager_email TEXT NOT NULL DEFAULT '';
ALTER TABLE store_reference ADD COLUMN IF NOT EXISTS operation_consultant_email TEXT NOT NULL DEFAULT '';
ALTER TABLE store_reference ADD COLUMN IF NOT EXISTS head_manager TEXT NOT NULL DEFAULT '';
ALTER TABLE store_reference ADD COLUMN IF NOT EXISTS head_manager_email TEXT NOT NULL DEFAULT '';

-- One row per (store_name, month, year) is a hard authorization invariant —
-- StoreAccessService grants store access to anyone whose email matches ANY
-- row for that store/period, so a duplicate row (e.g. a copy-paste mistake in
-- an upload) could hand the same store to two different people. New uploads
-- are rejected in-app before they can create one (UploadService), but this
-- script re-runs on every deploy and must never delete/merge existing rows to
-- "fix" a duplicate on someone's behalf — that is a business decision, not
-- something a migration should guess. So: only create the unique index when
-- the data is already clean; otherwise report the offending groups via
-- NOTICE (visible in deploy logs) and leave the existing data untouched. This
-- migration script has no way to know today whether the current production
-- store_reference table already contains duplicates — this block is exactly
-- how that gets discovered, safely, the next time it's run against the real
-- database.
DO $$
DECLARE
    dup_count INTEGER;
    dup_list  TEXT;
BEGIN
    SELECT COUNT(*), STRING_AGG(store_name || ' (' || month || '/' || year || ')', ', ')
      INTO dup_count, dup_list
      FROM (
          SELECT store_name, month, year
          FROM store_reference
          GROUP BY store_name, month, year
          HAVING COUNT(*) > 1
      ) d;

    IF dup_count > 0 THEN
        RAISE NOTICE 'store_reference has % duplicate (store_name, month, year) group(s): %. The ux_store_reference_store_month_year unique index was NOT created — resolve the duplicates, then re-run this script.', dup_count, dup_list;
    ELSE
        CREATE UNIQUE INDEX IF NOT EXISTS ux_store_reference_store_month_year
            ON store_reference (store_name, month, year);
    END IF;
END $$;

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
ALTER TABLE ai_usage_daily ADD COLUMN IF NOT EXISTS prompt_tokens BIGINT NOT NULL DEFAULT 0;
ALTER TABLE ai_usage_daily ADD COLUMN IF NOT EXISTS completion_tokens BIGINT NOT NULL DEFAULT 0;

-- ── store_action_plans / recommendations / notes ──────────────────────────
-- Store Action Plan feature. No EF Migrations in this app (Program.cs uses
-- Database.EnsureCreated(), which only builds schema for a brand-new
-- database) — this script is the real schema change for the existing DB.
-- Store is the permission/ownership unit: StoreReference remains the single
-- source of truth for who's responsible for a store, so these tables never
-- store a user-store assignment of their own.
CREATE TABLE IF NOT EXISTS store_action_plans (
    id SERIAL PRIMARY KEY,
    store_name TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Active',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_month INTEGER NOT NULL DEFAULT 0,
    created_year INTEGER NOT NULL DEFAULT 0,
    resolved_at TIMESTAMPTZ,
    resolved_reason TEXT,
    baseline_turnover_rate DOUBLE PRECISION,
    baseline_early_leaver_rate DOUBLE PRECISION,
    baseline_retention_rate DOUBLE PRECISION,
    detected_issues_summary TEXT NOT NULL DEFAULT '',
    healthy_streak_count INTEGER NOT NULL DEFAULT 0,
    -- Not part of the originally-specified column list — needed so detection
    -- can be re-run safely for a period that was already evaluated (e.g. after
    -- a single-file re-upload correction) without double-counting a monthly
    -- cycle toward the 2-consecutive-healthy-cycle auto-resolve rule.
    last_evaluated_month INTEGER,
    last_evaluated_year INTEGER
);
ALTER TABLE store_action_plans ADD COLUMN IF NOT EXISTS last_evaluated_month INTEGER;
ALTER TABLE store_action_plans ADD COLUMN IF NOT EXISTS last_evaluated_year INTEGER;

-- Only one Active plan per store — a partial unique index rather than a
-- plain one, since Resolved plans for the same store must coexist historically.
CREATE UNIQUE INDEX IF NOT EXISTS ux_store_action_plans_active_store
    ON store_action_plans (store_name)
    WHERE status = 'Active';
CREATE INDEX IF NOT EXISTS ix_store_action_plans_store_name ON store_action_plans (store_name);

CREATE TABLE IF NOT EXISTS action_plan_recommendations (
    id SERIAL PRIMARY KEY,
    store_action_plan_id INTEGER NOT NULL REFERENCES store_action_plans (id) ON DELETE CASCADE,
    signal_code TEXT NOT NULL DEFAULT '',
    category TEXT NOT NULL DEFAULT '',
    recommendation_text TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_action_plan_recommendations_plan_id ON action_plan_recommendations (store_action_plan_id);

-- Manager notes are append-only in V1 — no update/delete path in the app,
-- and author_name/author_role are snapshotted per row at write time so a
-- historical note keeps its original author even if that user account's
-- role or assigned name changes later.
CREATE TABLE IF NOT EXISTS action_plan_notes (
    id SERIAL PRIMARY KEY,
    store_action_plan_id INTEGER NOT NULL REFERENCES store_action_plans (id) ON DELETE CASCADE,
    author_user_id INTEGER NOT NULL,
    author_name TEXT NOT NULL DEFAULT '',
    author_role TEXT NOT NULL DEFAULT '',
    note_text TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_action_plan_notes_plan_id ON action_plan_notes (store_action_plan_id);

-- ── seed users ────────────────────────────────
-- admin@mcd.com / 123123654  →  Admin portal
-- user@mcd.com  / 123123654  →  Home portal
INSERT INTO users (email, phone, password_hash, role, created_at)
VALUES
    ('admin@mcd.com', '+201000000000', '$2a$11$4dMAuH6DiUfgnniQT39r1uof2UmVIJQ2vslu8qs8OwOJ7EUM1i/n6', 'Admin', NOW()),
    ('user@mcd.com',  '+201000000001', '$2a$11$4dMAuH6DiUfgnniQT39r1uof2UmVIJQ2vslu8qs8OwOJ7EUM1i/n6', 'User',  NOW())
ON CONFLICT (email) DO NOTHING;
