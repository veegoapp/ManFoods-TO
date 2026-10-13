using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MvcApp.Data;
using MvcApp.Models;

namespace MvcApp.Services;

/// <summary>One person in the reporting-line tree. <paramref name="Kind"/> is a <see cref="CoverageKinds"/> person
/// kind; <paramref name="Stores"/> are the stores that sit directly under this person (a level below was blank).</summary>
public sealed record CoverageNode(string Kind, string Name, string Email, List<CoverageNode> Children, List<string> Stores);

/// <summary>The latest Store Reference period as a reporting-line tree: Director, Manager, Senior OC, Consultant,
/// Head Manager, then stores. A blank level is skipped, so a store sits under its nearest named level above;
/// <paramref name="Unassigned"/> are stores with no named level at all.</summary>
public sealed record CoverageTree(List<CoverageNode> Roots, List<string> Unassigned);

/// <summary>What the Admin has set for one Trainer. <paramref name="Coverage"/> holds "kind|value" entries.</summary>
public sealed record TrainerSettings(IReadOnlyList<string> Coverage, bool CanEdit);

/// <summary>Admin-side management of the Trainer role: what a Trainer covers and whether the Trainer may edit.
/// The stores a Trainer sees are resolved in StoreAccessService, never here.</summary>
public interface ITrainerService
{
    /// <summary>The reporting-line tree of the latest uploaded Store Reference period (cached until the next upload).</summary>
    Task<CoverageTree> GetTreeAsync();

    Task<TrainerSettings> GetAsync(string trainerEmail);

    /// <summary>Saves the Trainer's edit flag and, when <paramref name="replaceCoverage"/> is true, replaces what they
    /// cover with <paramref name="coverage"/> ("kind|value" entries; invalid ones are ignored). With
    /// <paramref name="replaceCoverage"/> false the existing coverage is left alone.</summary>
    Task SaveAsync(string trainerEmail, IEnumerable<string>? coverage, bool canEdit, bool replaceCoverage = true);

    /// <summary>Moves a Trainer's coverage and flag to a new email (the account's email was edited).</summary>
    Task RenameAsync(string oldEmail, string newEmail);

    /// <summary>Drops everything stored for the Trainer (account deleted, or no longer a Trainer).</summary>
    Task RemoveAsync(string trainerEmail);
}

public class TrainerService : ITrainerService
{
    private const string TreeCacheKey = "trainer:coverage-tree";
    private static readonly TimeSpan TreeTtl = TimeSpan.FromMinutes(10);
    private const int MaxValueLength = 450;
    private const int MaxEmailLength = 256;

    private readonly AppDbContext _db;
    // Optional so the service can still be constructed as `new TrainerService(db)` in tests.
    private readonly IMemoryCache? _cache;

    public TrainerService(AppDbContext db, IMemoryCache? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    private static string Norm(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>Parses "kind|value" (the value may itself contain '|', as store names do). Person values are
    /// lower-cased emails; null when the entry is malformed or too long.</summary>
    private static (string Kind, string Value)? Parse(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return null;
        var cut = entry.IndexOf('|');
        if (cut <= 0) return null;
        var kind = entry[..cut].Trim().ToLowerInvariant();
        if (!CoverageKinds.IsValid(kind)) return null;
        var raw = entry[(cut + 1)..].Trim();
        var value = kind == CoverageKinds.Store ? raw : raw.ToLowerInvariant();
        if (value.Length == 0 || value.Length > MaxValueLength) return null;
        return (kind, value);
    }

    public async Task<CoverageTree> GetTreeAsync()
    {
        if (_cache != null && _cache.TryGetValue(TreeCacheKey, out CoverageTree? cached) && cached != null) return cached;

        var tree = await BuildTreeAsync();
        _cache?.Set(TreeCacheKey, tree, new MemoryCacheEntryOptions()
            .AddExpirationToken(StoreAccessService.ChangeToken())
            .SetAbsoluteExpiration(TreeTtl));
        return tree;
    }

    private async Task<CoverageTree> BuildTreeAsync()
    {
        // Same "latest uploaded period" convention as StoreAccessService: one small query for the period,
        // then only that period's rows — never the whole table.
        var latest = await _db.StoreReferences
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .Select(s => new { s.Month, s.Year })
            .FirstOrDefaultAsync();
        if (latest == null) return new CoverageTree(new List<CoverageNode>(), new List<string>());

        var rows = await _db.StoreReferences.AsNoTracking()
            .Where(s => s.Month == latest.Month && s.Year == latest.Year)
            .Select(s => new
            {
                s.StoreName,
                s.OperationDirector, s.OperationDirectorEmail,
                s.OperationManager, s.OperationManagerEmail,
                s.SeniorOperationConsultant, s.SeniorOperationConsultantEmail,
                s.OperationConsultant, s.OperationConsultantEmail,
                s.HeadManager, s.HeadManagerEmail,
            })
            .ToListAsync();

        var roots = new List<CoverageNode>();
        var unassigned = new List<string>();
        var nodes = new Dictionary<string, CoverageNode>();   // path key -> node
        var seenStores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            var store = (r.StoreName ?? "").Trim();
            if (store.Length == 0 || !seenStores.Add(store)) continue;

            // The person levels of this row, top first. A level with no email is dropped (access is
            // granted by email, so a person without one cannot be picked).
            var chain = new[]
            {
                (CoverageKinds.Director, r.OperationDirector, r.OperationDirectorEmail),
                (CoverageKinds.Manager, r.OperationManager, r.OperationManagerEmail),
                (CoverageKinds.SeniorConsultant, r.SeniorOperationConsultant, r.SeniorOperationConsultantEmail),
                (CoverageKinds.Consultant, r.OperationConsultant, r.OperationConsultantEmail),
                (CoverageKinds.HeadManager, r.HeadManager, r.HeadManagerEmail),
            };

            CoverageNode? parent = null;
            var path = "";
            foreach (var (kind, name, rawEmail) in chain)
            {
                var email = Norm(rawEmail);
                if (email.Length == 0) continue;
                path += "/" + kind + "|" + email;
                if (!nodes.TryGetValue(path, out var node))
                {
                    node = new CoverageNode(kind, (name ?? "").Trim().Length > 0 ? name!.Trim() : email, email, new List<CoverageNode>(), new List<string>());
                    nodes[path] = node;
                    (parent == null ? roots : parent.Children).Add(node);
                }
                parent = node;
            }

            if (parent == null) unassigned.Add(store);
            else parent.Stores.Add(store);
        }

        static void Sort(List<CoverageNode> list)
        {
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var n in list)
            {
                n.Stores.Sort(StringComparer.OrdinalIgnoreCase);
                Sort(n.Children);
            }
        }
        Sort(roots);
        unassigned.Sort(StringComparer.OrdinalIgnoreCase);
        return new CoverageTree(roots, unassigned);
    }

    public async Task<TrainerSettings> GetAsync(string trainerEmail)
    {
        var email = Norm(trainerEmail);
        var items = await _db.TrainerCoverages.AsNoTracking()
            .Where(t => t.TrainerEmail == email).Select(t => new { t.Kind, t.Value }).ToListAsync();
        var canEdit = await _db.TrainerSettings.AsNoTracking()
            .Where(t => t.TrainerEmail == email).Select(t => t.CanEdit).FirstOrDefaultAsync();
        return new TrainerSettings(items.Select(i => i.Kind + "|" + i.Value).ToList(), canEdit);
    }

    public async Task SaveAsync(string trainerEmail, IEnumerable<string>? coverage, bool canEdit, bool replaceCoverage = true)
    {
        var email = Norm(trainerEmail);
        if (email.Length == 0 || email.Length > MaxEmailLength) return;

        if (replaceCoverage)
        {
            var wanted = (coverage ?? Enumerable.Empty<string>())
                .Select(Parse).Where(p => p != null).Select(p => p!.Value).ToHashSet();

            var existing = await _db.TrainerCoverages.Where(t => t.TrainerEmail == email).ToListAsync();
            _db.TrainerCoverages.RemoveRange(existing.Where(t => !wanted.Contains((t.Kind, t.Value))));
            var have = existing.Select(t => (t.Kind, t.Value)).ToHashSet();
            foreach (var (kind, value) in wanted.Where(w => !have.Contains(w)))
                _db.TrainerCoverages.Add(new TrainerCoverage { TrainerEmail = email, Kind = kind, Value = value });
        }

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

        // Leftovers under the new address (a deleted account that once used it) must not collide with the moved rows.
        _db.TrainerCoverages.RemoveRange(await _db.TrainerCoverages.Where(t => t.TrainerEmail == to).ToListAsync());
        _db.TrainerSettings.RemoveRange(await _db.TrainerSettings.Where(t => t.TrainerEmail == to).ToListAsync());
        await _db.SaveChangesAsync();

        var items = await _db.TrainerCoverages.Where(t => t.TrainerEmail == from).ToListAsync();
        foreach (var i in items) i.TrainerEmail = to;
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
        _db.TrainerCoverages.RemoveRange(await _db.TrainerCoverages.Where(t => t.TrainerEmail == email).ToListAsync());
        _db.TrainerSettings.RemoveRange(await _db.TrainerSettings.Where(t => t.TrainerEmail == email).ToListAsync());
        await _db.SaveChangesAsync();
        StoreAccessService.InvalidateCache();
    }
}
