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
    public DbSet<WorkforceProjection> WorkforceProjections { get; set; }
    public DbSet<PageAccessConfig> PageAccessConfigs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        // Only one Active plan per store. This config only takes effect for a
        // fresh EnsureCreated() database (e.g. local/test) — the real schema
        // change for the existing production database is scripts/migrate.sql,
        // since this app doesn't use EF Migrations.
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

        // One workforce projection row per store/period — a re-upload for the
        // same period replaces rather than duplicates (same fresh-DB caveat).
        modelBuilder.Entity<WorkforceProjection>()
            .HasIndex(w => new { w.StoreName, w.Year, w.Month })
            .IsUnique()
            .HasDatabaseName("ux_workforce_projections_store_period");

        // Per-area access configuration — one row per area, keyed by the area
        // string (same fresh-DB caveat as above).
        modelBuilder.Entity<PageAccessConfig>().HasKey(p => p.AreaKey);

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
