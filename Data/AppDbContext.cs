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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
    }
}
