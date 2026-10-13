using Microsoft.EntityFrameworkCore;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

/// <summary>An Operation Consultant listed in the latest Store Reference period.</summary>
public sealed record ConsultantOption(string Email, string Name);

/// <summary>What the Admin has set for one Trainer.</summary>
public sealed record TrainerSettings(IReadOnlyList<string> ConsultantEmails, bool CanEdit);

/// <summary>Admin-side management of the Trainer role: which Operation Consultants a Trainer
/// covers and whether the Trainer may edit. The stores a Trainer sees are resolved in
/// StoreAccessService, never here.</summary>
public interface ITrainerService
{
    /// <summary>Distinct Operation Consultants (with an email) in the latest uploaded Store Reference period.</summary>
    Task<List<ConsultantOption>> GetConsultantOptionsAsync();

    Task<TrainerSettings> GetAsync(string trainerEmail);

    /// <summary>Replaces the Trainer's consultant picks and edit flag.</summary>
    Task SaveAsync(string trainerEmail, IEnumerable<string>? consultantEmails, bool canEdit);

    /// <summary>Moves a Trainer's picks and flag to a new email (the account's email was edited).</summary>
    Task RenameAsync(string oldEmail, string newEmail);

    /// <summary>Drops everything stored for the Trainer (account deleted, or no longer a Trainer).</summary>
    Task RemoveAsync(string trainerEmail);
}

public class TrainerService : ITrainerService
{
    private readonly AppDbContext _db;

    public TrainerService(AppDbContext db) => _db = db;

    private static string Norm(string? email) => (email ?? "").Trim().ToLowerInvariant();

    public async Task<List<ConsultantOption>> GetConsultantOptionsAsync()
    {
        // Same "latest uploaded period" convention as StoreAccessService: one small query for the
        // period, then only that period's rows — never the whole table.
        var latest = await _db.StoreReferences
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Select(s => new { s.Month, s.Year })
            .FirstOrDefaultAsync();
        if (latest == null) return new List<ConsultantOption>();

        var rows = await _db.StoreReferences
            .Where(s => s.Month == latest.Month && s.Year == latest.Year && s.OperationConsultantEmail != "")
            .Select(s => new { s.OperationConsultant, s.OperationConsultantEmail })
            .Distinct()
            .ToListAsync();

        return rows
            .Select(r => new { Email = Norm(r.OperationConsultantEmail), Name = (r.OperationConsultant ?? "").Trim() })
            .Where(r => r.Email.Length > 0)
            .GroupBy(r => r.Email)
            .Select(g => new ConsultantOption(g.Key, g.Select(x => x.Name).FirstOrDefault(n => n.Length > 0) ?? g.Key))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TrainerSettings> GetAsync(string trainerEmail)
    {
        var email = Norm(trainerEmail);
        var picks = await _db.TrainerConsultants.AsNoTracking()
            .Where(t => t.TrainerEmail == email).Select(t => t.ConsultantEmail).ToListAsync();
        var canEdit = await _db.TrainerSettings.AsNoTracking()
            .Where(t => t.TrainerEmail == email).Select(t => t.CanEdit).FirstOrDefaultAsync();
        return new TrainerSettings(picks, canEdit);
    }

    public async Task SaveAsync(string trainerEmail, IEnumerable<string>? consultantEmails, bool canEdit)
    {
        var email = Norm(trainerEmail);
        if (email.Length == 0) return;

        var wanted = (consultantEmails ?? Enumerable.Empty<string>())
            .Select(Norm).Where(e => e.Length > 0 && e.Length <= 256).ToHashSet();

        var existing = await _db.TrainerConsultants.Where(t => t.TrainerEmail == email).ToListAsync();
        _db.TrainerConsultants.RemoveRange(existing.Where(t => !wanted.Contains(t.ConsultantEmail)));
        var have = existing.Select(t => t.ConsultantEmail).ToHashSet();
        foreach (var consultant in wanted.Where(c => !have.Contains(c)))
            _db.TrainerConsultants.Add(new TrainerConsultant { TrainerEmail = email, ConsultantEmail = consultant });

        var setting = await _db.TrainerSettings.FirstOrDefaultAsync(t => t.TrainerEmail == email);
        if (setting == null) _db.TrainerSettings.Add(new TrainerSetting { TrainerEmail = email, CanEdit = canEdit });
        else setting.CanEdit = canEdit;

        await _db.SaveChangesAsync();
        StoreAccessService.InvalidateCache(); // the Trainer's store list is cached per email
    }

    public async Task RenameAsync(string oldEmail, string newEmail)
    {
        var from = Norm(oldEmail);
        var to = Norm(newEmail);
        if (from == to || from.Length == 0 || to.Length == 0) return;

        // Leftovers under the new address (a deleted account that once used it) must not collide with the moved picks.
        _db.TrainerConsultants.RemoveRange(await _db.TrainerConsultants.Where(t => t.TrainerEmail == to).ToListAsync());
        _db.TrainerSettings.RemoveRange(await _db.TrainerSettings.Where(t => t.TrainerEmail == to).ToListAsync());
        await _db.SaveChangesAsync();

        var picks = await _db.TrainerConsultants.Where(t => t.TrainerEmail == from).ToListAsync();
        foreach (var p in picks) p.TrainerEmail = to;
        var setting = await _db.TrainerSettings.FirstOrDefaultAsync(t => t.TrainerEmail == from);
        if (setting != null)
        {
            _db.TrainerSettings.Remove(setting);
            _db.TrainerSettings.Add(new TrainerSetting { TrainerEmail = to, CanEdit = setting.CanEdit });
        }
        await _db.SaveChangesAsync();
        StoreAccessService.InvalidateCache();
    }

    public async Task RemoveAsync(string trainerEmail)
    {
        var email = Norm(trainerEmail);
        _db.TrainerConsultants.RemoveRange(await _db.TrainerConsultants.Where(t => t.TrainerEmail == email).ToListAsync());
        _db.TrainerSettings.RemoveRange(await _db.TrainerSettings.Where(t => t.TrainerEmail == email).ToListAsync());
        await _db.SaveChangesAsync();
        StoreAccessService.InvalidateCache();
    }
}
