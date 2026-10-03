using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Tests.SqlServer;

/// <summary>
/// Small, hand-checkable dataset used by the SQL Server LINQ tests (and, identically, by an InMemory copy so the two
/// providers can be compared). Only ASCII labels are used, so nothing here depends on SQL Server's collation.
/// Three monthly snapshots of 2026, two stores, a few job titles, some new hires (hired in the snapshot month),
/// some missing hire dates, and resignations.
/// </summary>
public sealed class SqlServerSeedData
{
    public List<StoreReference> StoreReferences { get; } = new();
    public List<ActiveEmployee> Active { get; } = new();
    public List<Resignation> Resignations { get; } = new();

    public const int Year = 2026;
    public static readonly int[] Months = { 1, 2, 3 };

    public static SqlServerSeedData Build()
    {
        var d = new SqlServerSeedData();

        foreach (var month in Months)
        {
            d.StoreReferences.Add(new StoreReference { StoreName = "Store A", Month = month, Year = Year, StoreLeader = "Lead A", OperationManager = "OM One", OperationManagerEmail = "om1@example.com", OperationConsultant = "OC One" });
            d.StoreReferences.Add(new StoreReference { StoreName = "Store B", Month = month, Year = Year, StoreLeader = "Lead B", OperationManager = "OM Two", OperationManagerEmail = "om2@example.com", OperationConsultant = "OC Two" });
        }

        // (month, store, job, count, hireDate pattern)
        int id = 0;
        void Add(int month, string store, string job, string gender, DateOnly? hire)
        {
            d.Active.Add(new ActiveEmployee
            {
                EmployeeId = "E" + (++id), Name = "Employee " + id, Store = store, JobTitle = job, Gender = gender,
                PayrollGroup = "Group1", Month = month, Year = Year, HireDate = hire,
            });
        }

        // January
        Add(1, "Store A", "Crew", "Male", new DateOnly(2024, 6, 1));
        Add(1, "Store A", "Crew", "Female", new DateOnly(2025, 11, 20));
        Add(1, "Store A", "Manager", "Male", new DateOnly(2020, 2, 3));
        Add(1, "Store B", "Crew", "Female", new DateOnly(2026, 1, 8));      // new hire (January)
        // February
        Add(2, "Store A", "Crew", "Male", new DateOnly(2024, 6, 1));
        Add(2, "Store A", "Crew", "Female", new DateOnly(2025, 11, 20));
        Add(2, "Store A", "Crew", "Female", new DateOnly(2026, 2, 14));     // new hire (February)
        Add(2, "Store A", "Manager", "Male", new DateOnly(2020, 2, 3));
        Add(2, "Store B", "Crew", "Female", new DateOnly(2026, 1, 8));
        Add(2, "Store B", "Crew", "Male", null);                             // no hire date
        Add(2, "Store B", "Trainer", "Male", new DateOnly(2023, 9, 9));
        // March
        Add(3, "Store A", "Crew", "Male", new DateOnly(2024, 6, 1));
        Add(3, "Store A", "Crew", "Female", new DateOnly(2025, 11, 20));
        Add(3, "Store A", "Crew", "Female", new DateOnly(2026, 2, 14));
        Add(3, "Store A", "Crew", "Male", new DateOnly(2026, 3, 10));       // new hire (March)
        Add(3, "Store A", "Manager", "Male", new DateOnly(2020, 2, 3));
        Add(3, "Store B", "Crew", "Female", new DateOnly(2026, 1, 8));
        Add(3, "Store B", "Crew", "Male", null);
        Add(3, "Store B", "Trainer", "Male", new DateOnly(2026, 3, 2));     // new hire (March)
        Add(3, "Store B", "Manager", "Female", new DateOnly(2019, 5, 5));

        void Resign(int month, string store, string job, DateOnly hire, DateOnly left)
        {
            d.Resignations.Add(new Resignation
            {
                EmployeeId = "R" + (++id), Name = "Leaver " + id, Store = store, JobTitle = job, Gender = "Male",
                PayrollGroup = "Group1", Month = month, Year = Year, HireDate = hire, ResignationDate = left,
            });
        }
        Resign(2, "Store A", "Crew", new DateOnly(2025, 12, 1), new DateOnly(2026, 2, 20));
        Resign(2, "Store B", "Crew", new DateOnly(2024, 1, 1), new DateOnly(2026, 2, 25));
        Resign(3, "Store A", "Manager", new DateOnly(2018, 3, 3), new DateOnly(2026, 3, 15));
        Resign(3, "Store B", "Crew", new DateOnly(2026, 1, 20), new DateOnly(2026, 3, 28));
        Resign(3, "Store B", "Crew", new DateOnly(2025, 6, 6), new DateOnly(2026, 3, 30));

        return d;
    }

    public void AddTo(AppDbContext db)
    {
        db.StoreReferences.AddRange(StoreReferences);
        db.ActiveEmployees.AddRange(Active);
        db.Resignations.AddRange(Resignations);
    }

    public static async Task SeedAsync(AppDbContext db)
    {
        Build().AddTo(db);
        await db.SaveChangesAsync();
    }
}
