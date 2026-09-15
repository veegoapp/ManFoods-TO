-- =============================================
-- Manfoods McDonald's — DB Migration Script (SQL Server / MonsterASP)
-- شغّل: bash scripts/db-update.sh (uses Tools/DbMigrator — no sqlcmd needed),
-- أو يدويًا: sqlcmd -S <server> -d <database> -U <user> -P <password> -i scripts/migrate.sql
--
-- Ported from the original PostgreSQL/Neon version of this script. T-SQL has
-- no "IF NOT EXISTS" shorthand for CREATE TABLE/ADD COLUMN/CREATE INDEX, so
-- every such statement below is wrapped in an explicit existence check
-- instead — this keeps the same "safe to re-run on every deploy" guarantee
-- the original script had. TEXT columns became NVARCHAR (never VARCHAR) to
-- keep storing Arabic text correctly; NVARCHAR(MAX) except where a column is
-- used as a PRIMARY KEY or as an indexed column (plain or unique/filtered
-- index), since SQL Server cannot place any index on a MAX-length column —
-- users.email, app_settings.key, exit_interviews.forms_response_id, and
-- store_action_plans.store_name are all bounded for this reason.
-- =============================================

-- ── users ─────────────────────────────────────
-- password_hash is nullable: bulk-created accounts start "pending" (no
-- password) until the OTP self-activation flow sets one.
IF OBJECT_ID('dbo.users', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.users (
        id INT IDENTITY(1,1) PRIMARY KEY,
        email NVARCHAR(450) NOT NULL UNIQUE,
        phone NVARCHAR(MAX) NOT NULL DEFAULT '',
        password_hash NVARCHAR(MAX) NULL,
        role NVARCHAR(MAX) NOT NULL DEFAULT '',
        assigned_name NVARCHAR(MAX) NOT NULL DEFAULT '',
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
-- CREATE TABLE ... IF NOT EXISTS above is a no-op on a pre-existing table, so
-- backfill explicitly for databases that already had the old shape.
IF COL_LENGTH('dbo.users', 'phone') IS NULL
    ALTER TABLE dbo.users ADD phone NVARCHAR(MAX) NOT NULL DEFAULT '';
ALTER TABLE dbo.users ALTER COLUMN password_hash NVARCHAR(MAX) NULL;

-- True while an account's current password_hash is a system-generated
-- temporary password (manual Add User, or bulk "Generate Default
-- Passwords") that hasn't been replaced yet — gates the forced
-- Change-Password redirect (see SessionAuthFilterAttribute).
IF COL_LENGTH('dbo.users', 'must_change_password') IS NULL
    ALTER TABLE dbo.users ADD must_change_password BIT NOT NULL DEFAULT 0;

-- One-time historical cleanup (already applied): Admin_Full/Admin_Read were
-- folded into Admin, and Viewer into User. Operation_Manager/Operation_Consultant
-- are valid role values again (per-store access restriction) — do NOT add a
-- rewrite-to-User statement here, since this script is re-run on every deploy
-- and would silently wipe out live OM/OC role assignments.
UPDATE dbo.users SET role = 'Admin' WHERE role IN ('Admin_Full', 'Admin_Read');
UPDATE dbo.users SET role = 'User' WHERE role = 'Viewer';

-- ── password_reset_otps ────────────────────────
-- OTPs for the self-service "forgot password" flow (User accounts only —
-- Admin accounts use the separate master-key recovery flow). 4h expiry,
-- single use, invalidated after 5 failed attempts.
IF OBJECT_ID('dbo.password_reset_otps', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.password_reset_otps (
        id INT IDENTITY(1,1) PRIMARY KEY,
        user_id INT NOT NULL REFERENCES dbo.users(id) ON DELETE CASCADE,
        otp_code NVARCHAR(MAX) NOT NULL DEFAULT '',
        expires_at DATETIME2 NOT NULL,
        is_used BIT NOT NULL DEFAULT 0,
        failed_attempts INT NOT NULL DEFAULT 0,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END

-- ── app_settings ───────────────────────────────
-- Small key/value store for config that isn't tied to any entity — right
-- now just the admin recovery key hash (bcrypt, same as passwords). Not an
-- env var/Secret: this way there is nothing extra to configure outside the
-- database, and it can be rotated later from within the app if needed.
IF OBJECT_ID('dbo.app_settings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.app_settings (
        [key] NVARCHAR(200) PRIMARY KEY,
        value NVARCHAR(MAX) NOT NULL DEFAULT ''
    );
END
-- Seeds the recovery key hash for the key already generated and handed to
-- the admin — guarded by an existence check so re-running this script never
-- silently resets a key that's since been rotated (same intent as the
-- original ON CONFLICT DO NOTHING).
IF NOT EXISTS (SELECT 1 FROM dbo.app_settings WHERE [key] = 'admin_recovery_key_hash')
    INSERT INTO dbo.app_settings ([key], value)
    VALUES ('admin_recovery_key_hash', '$2b$11$24/KLaFMtFEfWIHLPFgbsudQs/B1SN/EVztSlE7u4ff0QAMiMS.sC');

-- ── active_employees ──────────────────────────
IF OBJECT_ID('dbo.active_employees', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.active_employees (
        id INT IDENTITY(1,1) PRIMARY KEY,
        employee_id NVARCHAR(MAX) NOT NULL DEFAULT '',
        name NVARCHAR(MAX) NOT NULL DEFAULT '',
        store NVARCHAR(MAX) NOT NULL DEFAULT '',
        job_title NVARCHAR(MAX) NOT NULL DEFAULT '',
        grade NVARCHAR(MAX) NOT NULL DEFAULT '',
        payroll_group NVARCHAR(MAX) NOT NULL DEFAULT '',
        cost_center NVARCHAR(MAX) NOT NULL DEFAULT '',
        gender NVARCHAR(MAX) NOT NULL DEFAULT '',
        hire_date DATE NULL,
        month INT NOT NULL DEFAULT 0,
        year INT NOT NULL DEFAULT 0
    );
END
-- These three were missing from this script entirely (on both the original
-- Postgres/Neon version and this port) even though Models/ActiveEmployee.cs
-- has always declared them — schema drift where the live database picked
-- them up some other way but this script never did. Backfill explicitly so
-- CREATE TABLE ... IF NOT EXISTS above being a no-op on an existing table
-- doesn't leave them missing.
IF COL_LENGTH('dbo.active_employees', 'grade') IS NULL
    ALTER TABLE dbo.active_employees ADD grade NVARCHAR(MAX) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.active_employees', 'payroll_group') IS NULL
    ALTER TABLE dbo.active_employees ADD payroll_group NVARCHAR(MAX) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.active_employees', 'cost_center') IS NULL
    ALTER TABLE dbo.active_employees ADD cost_center NVARCHAR(MAX) NOT NULL DEFAULT '';

-- ── resignations ──────────────────────────────
IF OBJECT_ID('dbo.resignations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.resignations (
        id INT IDENTITY(1,1) PRIMARY KEY,
        employee_id NVARCHAR(MAX) NOT NULL DEFAULT '',
        name NVARCHAR(MAX) NOT NULL DEFAULT '',
        store NVARCHAR(MAX) NOT NULL DEFAULT '',
        job_title NVARCHAR(MAX) NOT NULL DEFAULT '',
        payroll_group NVARCHAR(MAX) NOT NULL DEFAULT '',
        cost_center NVARCHAR(MAX) NOT NULL DEFAULT '',
        gender NVARCHAR(MAX) NOT NULL DEFAULT '',
        hire_date DATE NULL,
        resignation_date DATE NULL,
        tenure_months INT NOT NULL DEFAULT 0,
        month INT NOT NULL DEFAULT 0,
        year INT NOT NULL DEFAULT 0
    );
END
-- Same schema-drift gap as active_employees above — Models/Resignation.cs
-- has always declared these two.
IF COL_LENGTH('dbo.resignations', 'payroll_group') IS NULL
    ALTER TABLE dbo.resignations ADD payroll_group NVARCHAR(MAX) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.resignations', 'cost_center') IS NULL
    ALTER TABLE dbo.resignations ADD cost_center NVARCHAR(MAX) NOT NULL DEFAULT '';

-- ── store_references ──────────────────────────
IF OBJECT_ID('dbo.store_references', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.store_references (
        id INT IDENTITY(1,1) PRIMARY KEY,
        store_name NVARCHAR(MAX) NOT NULL DEFAULT '',
        region NVARCHAR(MAX) NOT NULL DEFAULT '',
        is_active BIT NOT NULL DEFAULT 1
    );
END

-- The actual table backing Models/StoreReference.cs is "store_reference"
-- (singular) — unrelated to "store_references" above. EnsureCreated() only
-- builds schema for a database with zero pre-existing tables, so on any
-- database that already had other tables (e.g. this one), it silently
-- never created this table. Create it explicitly so this script is the
-- real source of truth for it, matching this app's other tables.
IF OBJECT_ID('dbo.store_reference', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.store_reference (
        id INT IDENTITY(1,1) PRIMARY KEY,
        month INT NOT NULL DEFAULT 0,
        year INT NOT NULL DEFAULT 0,
        store_name NVARCHAR(MAX) NOT NULL DEFAULT '',
        store_leader NVARCHAR(MAX) NOT NULL DEFAULT '',
        operation_consultant NVARCHAR(MAX) NOT NULL DEFAULT '',
        operation_manager NVARCHAR(MAX) NOT NULL DEFAULT ''
    );
END

-- Backfill the OM/OC email columns used for per-store access restriction.
IF COL_LENGTH('dbo.store_reference', 'operation_manager_email') IS NULL
    ALTER TABLE dbo.store_reference ADD operation_manager_email NVARCHAR(MAX) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.store_reference', 'operation_consultant_email') IS NULL
    ALTER TABLE dbo.store_reference ADD operation_consultant_email NVARCHAR(MAX) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.store_reference', 'head_manager') IS NULL
    ALTER TABLE dbo.store_reference ADD head_manager NVARCHAR(MAX) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.store_reference', 'head_manager_email') IS NULL
    ALTER TABLE dbo.store_reference ADD head_manager_email NVARCHAR(MAX) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.store_reference', 'senior_operation_consultant') IS NULL
    ALTER TABLE dbo.store_reference ADD senior_operation_consultant NVARCHAR(MAX) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.store_reference', 'senior_operation_consultant_email') IS NULL
    ALTER TABLE dbo.store_reference ADD senior_operation_consultant_email NVARCHAR(MAX) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.store_reference', 'operation_director') IS NULL
    ALTER TABLE dbo.store_reference ADD operation_director NVARCHAR(MAX) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.store_reference', 'operation_director_email') IS NULL
    ALTER TABLE dbo.store_reference ADD operation_director_email NVARCHAR(MAX) NOT NULL DEFAULT '';

-- ── exit_interviews ────────────────────────────
-- One row per Microsoft Forms exit-interview submission. No name / national
-- ID is stored — employee_id is kept only to resolve store/leader/OC/OM at
-- upload time and must never be surfaced in any view or API response.
IF OBJECT_ID('dbo.exit_interviews', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.exit_interviews (
        id INT IDENTITY(1,1) PRIMARY KEY,
        forms_response_id NVARCHAR(450) NOT NULL DEFAULT '',
        employee_id NVARCHAR(MAX) NOT NULL DEFAULT '',
        store NVARCHAR(MAX) NOT NULL DEFAULT '',
        store_leader NVARCHAR(MAX) NOT NULL DEFAULT '',
        operation_consultant NVARCHAR(MAX) NOT NULL DEFAULT '',
        operation_manager NVARCHAR(MAX) NOT NULL DEFAULT '',
        job_title NVARCHAR(MAX) NOT NULL DEFAULT '',
        month INT NOT NULL DEFAULT 0,
        year INT NOT NULL DEFAULT 0,
        submitted_at DATETIME2 NULL,

        reason_for_leaving NVARCHAR(MAX) NOT NULL DEFAULT '',
        would_return NVARCHAR(MAX) NOT NULL DEFAULT '',
        overall_experience NVARCHAR(MAX) NOT NULL DEFAULT '',
        workload_condition NVARCHAR(MAX) NOT NULL DEFAULT '',
        fair_treatment NVARCHAR(MAX) NOT NULL DEFAULT '',
        encourage_opinions NVARCHAR(MAX) NOT NULL DEFAULT '',
        complaints_handling NVARCHAR(MAX) NOT NULL DEFAULT '',
        benefits_match NVARCHAR(MAX) NOT NULL DEFAULT '',
        teamwork NVARCHAR(MAX) NOT NULL DEFAULT '',
        communication NVARCHAR(MAX) NOT NULL DEFAULT '',
        task_fit NVARCHAR(MAX) NOT NULL DEFAULT '',
        training NVARCHAR(MAX) NOT NULL DEFAULT '',
        feedback NVARCHAR(MAX) NOT NULL DEFAULT '',
        use_personal_abilities NVARCHAR(MAX) NOT NULL DEFAULT '',

        reason_other_text NVARCHAR(MAX) NULL,
        work_pressure_reason_text NVARCHAR(MAX) NULL,
        what_would_change_text NVARCHAR(MAX) NULL,
        what_learned_text NVARCHAR(MAX) NULL,
        final_comments_text NVARCHAR(MAX) NULL
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_exit_interviews_forms_response_id' AND object_id = OBJECT_ID('dbo.exit_interviews'))
    CREATE UNIQUE INDEX ux_exit_interviews_forms_response_id
        ON dbo.exit_interviews (forms_response_id) WHERE forms_response_id <> '';

-- ── upload_logs ───────────────────────────────
IF OBJECT_ID('dbo.upload_logs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.upload_logs (
        id INT IDENTITY(1,1) PRIMARY KEY,
        file_type NVARCHAR(MAX) NOT NULL DEFAULT '',
        file_name NVARCHAR(MAX) NOT NULL DEFAULT '',
        month INT NOT NULL DEFAULT 0,
        year INT NOT NULL DEFAULT 0,
        upload_date DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        uploaded_by NVARCHAR(MAX) NOT NULL DEFAULT '',
        file_content VARBINARY(MAX) NULL,
        content_type NVARCHAR(MAX) NULL
    );
END

-- CREATE TABLE ... IF NOT EXISTS above is a no-op on a table that already
-- exists with an older shape, so columns added after the table's first
-- deploy (like these two) never land on existing databases. Backfill them
-- explicitly.
IF COL_LENGTH('dbo.upload_logs', 'file_content') IS NULL
    ALTER TABLE dbo.upload_logs ADD file_content VARBINARY(MAX) NULL;
IF COL_LENGTH('dbo.upload_logs', 'content_type') IS NULL
    ALTER TABLE dbo.upload_logs ADD content_type NVARCHAR(MAX) NULL;

-- ── ai_usage_daily ────────────────────────────
IF OBJECT_ID('dbo.ai_usage_daily', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_usage_daily (
        user_id INT NOT NULL,
        usage_date DATE NOT NULL,
        question_count INT NOT NULL DEFAULT 0,
        PRIMARY KEY (user_id, usage_date)
    );
END
IF COL_LENGTH('dbo.ai_usage_daily', 'prompt_tokens') IS NULL
    ALTER TABLE dbo.ai_usage_daily ADD prompt_tokens BIGINT NOT NULL DEFAULT 0;
IF COL_LENGTH('dbo.ai_usage_daily', 'completion_tokens') IS NULL
    ALTER TABLE dbo.ai_usage_daily ADD completion_tokens BIGINT NOT NULL DEFAULT 0;

-- ── store_action_plans / recommendations / notes ──────────────────────────
-- Store Action Plan feature. No EF Migrations in this app (Program.cs uses
-- Database.EnsureCreated(), which only builds schema for a brand-new
-- database) — this script is the real schema change for the existing DB.
-- Store is the permission/ownership unit: StoreReference remains the single
-- source of truth for who's responsible for a store, so these tables never
-- store a user-store assignment of their own.
IF OBJECT_ID('dbo.store_action_plans', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.store_action_plans (
        id INT IDENTITY(1,1) PRIMARY KEY,
        store_name NVARCHAR(450) NOT NULL DEFAULT '',
        status NVARCHAR(MAX) NOT NULL DEFAULT 'Active',
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        created_month INT NOT NULL DEFAULT 0,
        created_year INT NOT NULL DEFAULT 0,
        resolved_at DATETIME2 NULL,
        resolved_reason NVARCHAR(MAX) NULL,
        baseline_turnover_rate FLOAT NULL,
        baseline_early_leaver_rate FLOAT NULL,
        baseline_retention_rate FLOAT NULL,
        detected_issues_summary NVARCHAR(MAX) NOT NULL DEFAULT '',
        healthy_streak_count INT NOT NULL DEFAULT 0,
        -- Not part of the originally-specified column list — needed so detection
        -- can be re-run safely for a period that was already evaluated (e.g. after
        -- a single-file re-upload correction) without double-counting a monthly
        -- cycle toward the 2-consecutive-healthy-cycle auto-resolve rule.
        last_evaluated_month INT NULL,
        last_evaluated_year INT NULL
    );
END
IF COL_LENGTH('dbo.store_action_plans', 'last_evaluated_month') IS NULL
    ALTER TABLE dbo.store_action_plans ADD last_evaluated_month INT NULL;
IF COL_LENGTH('dbo.store_action_plans', 'last_evaluated_year') IS NULL
    ALTER TABLE dbo.store_action_plans ADD last_evaluated_year INT NULL;
-- CREATE TABLE ... IF NOT EXISTS above is a no-op on a table that was already
-- created (e.g. by an earlier run of this script before store_name was
-- bounded to NVARCHAR(450) below) — fix it explicitly so the filtered index
-- further down can actually be created on it. Guarded on the column's actual
-- current width (max_length = -1 means NVARCHAR(MAX)) so this whole block is
-- a no-op once store_name is already NVARCHAR(450). ALTER COLUMN cannot run
-- while a DEFAULT constraint is attached to the column (SQL Server error:
-- "object ... is dependent on column"), so the inline DEFAULT '' from the
-- CREATE TABLE above — which got an auto-generated constraint name — has to
-- be located and dropped first, then re-added afterward with an explicit
-- name so it doesn't collide with itself if this ever needs to run again.
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.store_action_plans') AND name = 'store_name' AND max_length = -1
)
BEGIN
    DECLARE @storeNameDefaultConstraint NVARCHAR(200);
    SELECT @storeNameDefaultConstraint = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.store_action_plans') AND c.name = 'store_name';

    IF @storeNameDefaultConstraint IS NOT NULL
        EXEC('ALTER TABLE dbo.store_action_plans DROP CONSTRAINT [' + @storeNameDefaultConstraint + ']');

    ALTER TABLE dbo.store_action_plans ALTER COLUMN store_name NVARCHAR(450) NOT NULL;

    ALTER TABLE dbo.store_action_plans
        ADD CONSTRAINT DF_store_action_plans_store_name DEFAULT '' FOR store_name;
END

-- Only one Active plan per store — a filtered unique index rather than a
-- plain one, since Resolved plans for the same store must coexist historically.
-- SQL Server supports filtered indexes natively with the same WHERE syntax
-- Postgres partial indexes use, so this ports over unchanged.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_store_action_plans_active_store' AND object_id = OBJECT_ID('dbo.store_action_plans'))
    CREATE UNIQUE INDEX ux_store_action_plans_active_store
        ON dbo.store_action_plans (store_name)
        WHERE status = 'Active';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_store_action_plans_store_name' AND object_id = OBJECT_ID('dbo.store_action_plans'))
    CREATE INDEX ix_store_action_plans_store_name ON dbo.store_action_plans (store_name);

IF OBJECT_ID('dbo.action_plan_recommendations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.action_plan_recommendations (
        id INT IDENTITY(1,1) PRIMARY KEY,
        store_action_plan_id INT NOT NULL REFERENCES dbo.store_action_plans (id) ON DELETE CASCADE,
        signal_code NVARCHAR(MAX) NOT NULL DEFAULT '',
        category NVARCHAR(MAX) NOT NULL DEFAULT '',
        recommendation_text NVARCHAR(MAX) NOT NULL DEFAULT '',
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_action_plan_recommendations_plan_id' AND object_id = OBJECT_ID('dbo.action_plan_recommendations'))
    CREATE INDEX ix_action_plan_recommendations_plan_id ON dbo.action_plan_recommendations (store_action_plan_id);

-- Manager notes are append-only in V1 — no update/delete path in the app,
-- and author_name/author_role are snapshotted per row at write time so a
-- historical note keeps its original author even if that user account's
-- role or assigned name changes later.
IF OBJECT_ID('dbo.action_plan_notes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.action_plan_notes (
        id INT IDENTITY(1,1) PRIMARY KEY,
        store_action_plan_id INT NOT NULL REFERENCES dbo.store_action_plans (id) ON DELETE CASCADE,
        author_user_id INT NOT NULL,
        author_name NVARCHAR(MAX) NOT NULL DEFAULT '',
        author_role NVARCHAR(MAX) NOT NULL DEFAULT '',
        note_text NVARCHAR(MAX) NOT NULL DEFAULT '',
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_action_plan_notes_plan_id' AND object_id = OBJECT_ID('dbo.action_plan_notes'))
    CREATE INDEX ix_action_plan_notes_plan_id ON dbo.action_plan_notes (store_action_plan_id);

-- ── Action Center additions ──────────────────────────────────────────────
-- Purely additive columns/table on top of the existing Store Action Plan
-- schema above — the legacy detection engine and the old Store Action Plan
-- page never read or write any of these, so both pages can run side by side
-- against the same store_action_plans/action_plan_recommendations rows.

-- Turns each system-generated recommendation into a checkable task.
IF COL_LENGTH('dbo.action_plan_recommendations', 'is_completed') IS NULL
    ALTER TABLE dbo.action_plan_recommendations ADD is_completed BIT NOT NULL DEFAULT 0;
IF COL_LENGTH('dbo.action_plan_recommendations', 'completed_at') IS NULL
    ALTER TABLE dbo.action_plan_recommendations ADD completed_at DATETIME2 NULL;
IF COL_LENGTH('dbo.action_plan_recommendations', 'completed_by_name') IS NULL
    ALTER TABLE dbo.action_plan_recommendations ADD completed_by_name NVARCHAR(MAX) NULL;

-- Ownership, target date, and a manual-override close path (the legacy engine
-- only ever auto-resolves after 2 consecutive clean cycles; Action Center
-- lets an Admin close a plan early with a recorded reason).
IF COL_LENGTH('dbo.store_action_plans', 'assigned_to_name') IS NULL
    ALTER TABLE dbo.store_action_plans ADD assigned_to_name NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.store_action_plans', 'target_resolution_date') IS NULL
    ALTER TABLE dbo.store_action_plans ADD target_resolution_date DATE NULL;
IF COL_LENGTH('dbo.store_action_plans', 'closed_by_name') IS NULL
    ALTER TABLE dbo.store_action_plans ADD closed_by_name NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.store_action_plans', 'manual_close_reason') IS NULL
    ALTER TABLE dbo.store_action_plans ADD manual_close_reason NVARCHAR(MAX) NULL;

-- One row per detection cycle for a store with an active plan, regardless of
-- whether a signal fired that cycle — lets Action Center chart real
-- baseline-vs-now progress instead of a single frozen creation-time snapshot.
IF OBJECT_ID('dbo.action_plan_metric_snapshots', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.action_plan_metric_snapshots (
        id INT IDENTITY(1,1) PRIMARY KEY,
        store_action_plan_id INT NOT NULL REFERENCES dbo.store_action_plans (id) ON DELETE CASCADE,
        month INT NOT NULL,
        year INT NOT NULL,
        turnover_rate FLOAT NULL,
        early_leaver_rate FLOAT NULL,
        retention_rate FLOAT NULL,
        signal_count INT NOT NULL DEFAULT 0,
        recorded_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_action_plan_metric_snapshots_plan_id' AND object_id = OBJECT_ID('dbo.action_plan_metric_snapshots'))
    CREATE INDEX ix_action_plan_metric_snapshots_plan_id ON dbo.action_plan_metric_snapshots (store_action_plan_id);

-- ── store_action_plan_role_assignments ─────────────────────────────────────
-- Optional Admin override of which role is authorized to manage a store's
-- Action Plan (add notes / toggle recommendation checkboxes / shown as its
-- Responsible Party there) — lets an Admin delegate away from the computed
-- default (Operation_Consultant if assigned, else Head_Manager), e.g. while
-- the Operation Consultant is on leave. One row per store; no row means "use
-- the computed default." This table only affects Action Plan permissions —
-- StoreAccessService's general store-visibility rules are untouched by it.
IF OBJECT_ID('dbo.store_action_plan_role_assignments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.store_action_plan_role_assignments (
        id INT IDENTITY(1,1) PRIMARY KEY,
        store_name NVARCHAR(450) NOT NULL DEFAULT '',
        role NVARCHAR(MAX) NOT NULL DEFAULT '',
        set_by_name NVARCHAR(MAX) NULL,
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_store_action_plan_role_assignments_store' AND object_id = OBJECT_ID('dbo.store_action_plan_role_assignments'))
    CREATE UNIQUE INDEX ux_store_action_plan_role_assignments_store
        ON dbo.store_action_plan_role_assignments (store_name);

-- ── action_plan_severity_band_config / _history ────────────────────────────
-- Admin-configurable cutoffs for how many distinct fired signals make an
-- Active plan Medium/High/Critical (Settings page). Single row (id = 1);
-- StoreActionPlanService.ComputeSeverity reads it live and seeds it lazily
-- with the values that used to be hardcoded (1/2/3), so behavior is unchanged
-- until an Admin edits it. Every save is also appended to the history table
-- for audit purposes.
IF OBJECT_ID('dbo.action_plan_severity_band_config', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.action_plan_severity_band_config (
        id INT NOT NULL PRIMARY KEY,
        medium_min_signals INT NOT NULL DEFAULT 1,
        high_min_signals INT NOT NULL DEFAULT 2,
        critical_min_signals INT NOT NULL DEFAULT 3,
        updated_by_name NVARCHAR(MAX) NULL,
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END

IF OBJECT_ID('dbo.action_plan_severity_band_history', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.action_plan_severity_band_history (
        id INT IDENTITY(1,1) PRIMARY KEY,
        medium_min_signals INT NOT NULL,
        high_min_signals INT NOT NULL,
        critical_min_signals INT NOT NULL,
        changed_by_name NVARCHAR(MAX) NULL,
        changed_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END

-- ── signal_occurrences ──────────────────────────────────────────────────────
-- One row per store/signal/period where a detection signal fired — logged
-- live going forward by StoreActionPlanService.LogSignalOccurrencesAsync, and
-- reconstructed for 6 of the 7 signals from retained historical data by the
-- one-time Admin-triggered backfill (RunHistoricalSignalBackfillAsync;
-- is_backfilled = 1 for those rows). EARLY_WARNING_WATCHLIST is never
-- backfilled — it only reflects current active-employee risk state and
-- cannot be reconstructed for a past period. This is what the persistence
-- rule ("2 of the last 3 data-available evaluation periods") reads from,
-- independent of store_action_plans, so it survives plan creation/resolution.
IF OBJECT_ID('dbo.signal_occurrences', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.signal_occurrences (
        id INT IDENTITY(1,1) PRIMARY KEY,
        store_name NVARCHAR(450) NOT NULL DEFAULT '',
        signal_code NVARCHAR(100) NOT NULL DEFAULT '',
        month INT NOT NULL,
        year INT NOT NULL,
        occurred_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        is_backfilled BIT NOT NULL DEFAULT 0
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_signal_occurrences_store_signal_period' AND object_id = OBJECT_ID('dbo.signal_occurrences'))
    CREATE UNIQUE INDEX ux_signal_occurrences_store_signal_period
        ON dbo.signal_occurrences (store_name, signal_code, year, month);

-- ── login_history ────────────────────────────────────────────────────────
-- One row per login attempt against a known account (Home and Admin alike),
-- written by AuthService.ValidateAsync. Powers the per-user login log opened
-- from the Users page.
IF OBJECT_ID('dbo.login_history', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.login_history (
        id INT IDENTITY(1,1) PRIMARY KEY,
        user_id INT NOT NULL,
        email NVARCHAR(MAX) NOT NULL DEFAULT '',
        logged_in_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        ip_address NVARCHAR(MAX) NULL,
        user_agent NVARCHAR(MAX) NULL,
        success BIT NOT NULL DEFAULT 1,
        portal NVARCHAR(20) NOT NULL DEFAULT '',
        failure_reason NVARCHAR(200) NULL
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_login_history_user_logged_in_at' AND object_id = OBJECT_ID('dbo.login_history'))
    CREATE INDEX ix_login_history_user_logged_in_at
        ON dbo.login_history (user_id, logged_in_at);

-- Backfill for databases created before failed-attempt logging was added —
-- every row that already exists is a past successful login.
IF COL_LENGTH('dbo.login_history', 'success') IS NULL
    ALTER TABLE dbo.login_history ADD success BIT NOT NULL DEFAULT 1;
IF COL_LENGTH('dbo.login_history', 'portal') IS NULL
    ALTER TABLE dbo.login_history ADD portal NVARCHAR(20) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.login_history', 'failure_reason') IS NULL
    ALTER TABLE dbo.login_history ADD failure_reason NVARCHAR(200) NULL;

-- ── workforce_projections ────────────────────────────────────────────────
-- Forward-looking planned/required headcount per store per period, uploaded
-- independently of the three period files (like exit_interviews). Optional:
-- the Store Health engine reads the gap vs. current active headcount for its
-- Workforce Outlook pillar, and a store with no row simply contributes no
-- outlook signal instead of being penalised. One row per store/period — a
-- re-upload for the same period replaces rather than duplicates.
IF OBJECT_ID('dbo.workforce_projections', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.workforce_projections (
        id INT IDENTITY(1,1) PRIMARY KEY,
        month INT NOT NULL DEFAULT 0,
        year INT NOT NULL DEFAULT 0,
        store_name NVARCHAR(450) NOT NULL DEFAULT '',
        projected_headcount INT NOT NULL DEFAULT 0,
        planned_hires INT NOT NULL DEFAULT 0,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_workforce_projections_store_period' AND object_id = OBJECT_ID('dbo.workforce_projections'))
    CREATE UNIQUE INDEX ux_workforce_projections_store_period
        ON dbo.workforce_projections (store_name, year, month);

-- ── page_access_config ────────────────────────────────────────────────────
-- Admin-configurable per-area access. One row per access area (see
-- Services/AccessAreas). is_restricted = 1 means restricted roles (OC/OM/Head
-- Manager/…) see only their own stores in that area; 0 means they see
-- everything there. A missing row defaults to restricted, so behaviour is
-- unchanged until an Admin opens an area on the Settings page.
IF OBJECT_ID('dbo.page_access_config', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.page_access_config (
        area_key NVARCHAR(100) NOT NULL PRIMARY KEY,
        is_restricted BIT NOT NULL DEFAULT 1,
        updated_by_name NVARCHAR(MAX) NULL,
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END

-- ── seed users ────────────────────────────────
-- admin@mcd.com / 123123654  →  Admin portal
-- user@mcd.com  / 123123654  →  Home portal
INSERT INTO dbo.users (email, phone, password_hash, role, created_at)
SELECT v.email, v.phone, v.password_hash, v.role, SYSUTCDATETIME()
FROM (VALUES
    ('admin@mcd.com', '+201000000000', '$2a$11$4dMAuH6DiUfgnniQT39r1uof2UmVIJQ2vslu8qs8OwOJ7EUM1i/n6', 'Admin'),
    ('user@mcd.com',  '+201000000001', '$2a$11$4dMAuH6DiUfgnniQT39r1uof2UmVIJQ2vslu8qs8OwOJ7EUM1i/n6', 'User')
) AS v(email, phone, password_hash, role)
WHERE NOT EXISTS (SELECT 1 FROM dbo.users u WHERE u.email = v.email);
