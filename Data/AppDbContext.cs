using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MvcApp.Models;

namespace MvcApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<ActiveEmployee> ActiveEmployees { get; set; }
    public DbSet<Resignation> Resignations { get; set; }
    public DbSet<StoreReference> StoreReferences { get; set; }
    public DbSet<UploadLog> UploadLogs { get; set; }
    public DbSet<ExitInterview> ExitInterviews { get; set; }
    public DbSet<JobHeadcountProjection> JobHeadcountProjections { get; set; }
    public DbSet<CrewTrainerEmployee> CrewTrainerEmployees { get; set; }
    public DbSet<JobPayrollGroup> JobPayrollGroups { get; set; }
    public DbSet<PageVisibility> PageVisibilities { get; set; }
    public DbSet<PasswordResetOtp> PasswordResetOtps { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<StoreActionPlan> StoreActionPlans { get; set; }
    public DbSet<ActionPlanRecommendation> ActionPlanRecommendations { get; set; }
    public DbSet<ActionPlanNote> ActionPlanNotes { get; set; }
    public DbSet<ActionPlanMetricSnapshot> ActionPlanMetricSnapshots { get; set; }
    public DbSet<StoreActionPlanRoleAssignment> StoreActionPlanRoleAssignments { get; set; }
    public DbSet<ActionPlanSeverityBandConfig> ActionPlanSeverityBandConfigs { get; set; }
    public DbSet<ActionPlanSeverityBandHistory> ActionPlanSeverityBandHistories { get; set; }
    public DbSet<SignalOccurrence> SignalOccurrences { get; set; }
    public DbSet<LoginHistory> LoginHistories { get; set; }
    public DbSet<ActivityLog> ActivityLogs { get; set; }
    public DbSet<PageAccessConfig> PageAccessConfigs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        // Only one Active plan per store. This config only takes effect for a
        // fresh EnsureCreated() database (e.g. local/test) — the real schema
        // change for the existing production database is scripts/migrate.sql,
        // since this app doesn't use EF Migrations.
        // activity_logs: same column sizes and index names as scripts/migrate.sql, so a database created by EnsureCreated()
        // matches the migrated one (and the migration's "index exists?" checks find these by name).
        modelBuilder.Entity<ActivityLog>(e =>
        {
            e.Property(x => x.Category).HasMaxLength(20);
            e.Property(x => x.Action).HasMaxLength(40);
            e.Property(x => x.UserEmail).HasMaxLength(256);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.Property(x => x.Portal).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(100);
            e.Property(x => x.Details).HasMaxLength(1000);
            e.HasIndex(x => new { x.OccurredAt, x.Id }).IsDescending(true, true).IncludeProperties(x => new { x.Success, x.Category, x.Action }).HasDatabaseName("ix_activity_logs_occurred_at");
            e.HasIndex(x => new { x.Category, x.OccurredAt }).IsDescending(false, true).IncludeProperties(x => new { x.Success }).HasDatabaseName("ix_activity_logs_category_occurred_at");
            e.HasIndex(x => new { x.Action, x.OccurredAt }).IsDescending(false, true).IncludeProperties(x => new { x.Success, x.Category }).HasDatabaseName("ix_activity_logs_action_occurred_at");
        });

        modelBuilder.Entity<StoreActionPlan>()
            .HasIndex(p => p.StoreName)
            .IsUnique()
            .HasFilter("status = 'Active'")
            .HasDatabaseName("ux_store_action_plans_active_store");

        // One Action Plan role override per store — same "config only takes
        // effect for a fresh EnsureCreated() database" caveat as above.
        modelBuilder.Entity<StoreActionPlanRoleAssignment>()
            .HasIndex(a => a.StoreName)
            .IsUnique()
            .HasDatabaseName("ux_store_action_plan_role_assignments_store");

        // One occurrence row per store/signal/period — re-running detection or
        // the backfill script for an already-logged period must not duplicate it.
        modelBuilder.Entity<SignalOccurrence>()
            .HasIndex(s => new { s.StoreName, s.SignalCode, s.Year, s.Month })
            .IsUnique()
            .HasDatabaseName("ux_signal_occurrences_store_signal_period");

        // Per-user login history is always queried "most recent logins for
        // this user" — same "config only takes effect for a fresh
        // EnsureCreated() database" caveat as above.
        modelBuilder.Entity<LoginHistory>()
            .HasIndex(l => new { l.UserId, l.LoggedInAt })
            .HasDatabaseName("ix_login_history_user_logged_in_at");

        // One projection row per year/month/store/job — a re-upload replaces the
        // whole year (same fresh-DB caveat as above).
        modelBuilder.Entity<JobHeadcountProjection>()
            .HasIndex(j => new { j.Year, j.Month, j.StoreName, j.JobTitle })
            .IsUnique()
            .HasDatabaseName("ux_job_headcount_projections_period_store_job");
        modelBuilder.Entity<JobHeadcountProjection>().Property(j => j.StoreName).HasMaxLength(450);
        modelBuilder.Entity<JobHeadcountProjection>().Property(j => j.JobTitle).HasMaxLength(200);

        // One row per employee per month — a re-upload replaces the whole month.
        modelBuilder.Entity<CrewTrainerEmployee>()
            .HasIndex(c => new { c.Year, c.Month, c.EmployeeId })
            .IsUnique()
            .HasDatabaseName("ux_crew_trainer_employees_period_employee");
        modelBuilder.Entity<CrewTrainerEmployee>().Property(c => c.EmployeeId).HasMaxLength(100);
        modelBuilder.Entity<CrewTrainerEmployee>().Property(c => c.StoreName).HasMaxLength(450);
        modelBuilder.Entity<CrewTrainerEmployee>().Property(c => c.PayrollGroup).HasMaxLength(200);

        // One payroll group per job title (matched case-insensitively by SQL Server's collation).
        modelBuilder.Entity<JobPayrollGroup>()
            .HasIndex(j => j.JobTitle)
            .IsUnique()
            .HasDatabaseName("ux_job_payroll_groups_job");
        modelBuilder.Entity<JobPayrollGroup>().Property(j => j.JobTitle).HasMaxLength(200);
        modelBuilder.Entity<JobPayrollGroup>().Property(j => j.PayrollGroup).HasMaxLength(200);

        // Per-area access configuration — one row per area, keyed by the area
        // string (same fresh-DB caveat as above).
        modelBuilder.Entity<PageAccessConfig>().HasKey(p => p.AreaKey);
        modelBuilder.Entity<PageVisibility>().HasKey(p => new { p.PageKey, p.Role });
        modelBuilder.Entity<PageVisibility>().Property(p => p.Role).HasMaxLength(100);
        modelBuilder.Entity<PageVisibility>().Property(p => p.PageKey).HasMaxLength(100);

        // SQL Server's DATETIME2 (unlike Npgsql's TIMESTAMPTZ) has no concept of
        // DateTimeKind — every DateTime read back from it comes back as Kind=
        // Unspecified. Every DateTime column in this app is always written as
        // DateTime.UtcNow, so tag every value read back as Kind=Utc explicitly;
        // otherwise JSON responses (e.g. ExitInterview.SubmittedAt, UploadLog.
        // UploadDate) would serialize without the "Z"/UTC suffix, which
        // JavaScript's Date parsing on the frontend would misread as local time
        // instead of UTC — a real behavior change the Npgsql provider never had.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(utcConverter);
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(nullableUtcConverter);
            }
        }
    }
}
