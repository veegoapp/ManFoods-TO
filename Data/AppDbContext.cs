using Microsoft.EntityFrameworkCore;
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

        // One store_reference row per (StoreName, Month, Year) — a duplicate
        // would mean two different OC/OM/Head Manager emails claiming the same
        // store for the same period, which StoreAccessService would then treat
        // as "both are assigned." UploadService rejects any upload that would
        // create one; this index is the database-level backstop. Same caveat
        // as the StoreActionPlan index above: only takes effect for a fresh
        // EnsureCreated() database — scripts/migrate.sql is the real schema
        // change for the existing production database.
        modelBuilder.Entity<StoreReference>()
            .HasIndex(s => new { s.StoreName, s.Month, s.Year })
            .IsUnique()
            .HasDatabaseName("ux_store_reference_store_month_year");
    }
}
