using Microsoft.EntityFrameworkCore;
using VkPostman.Core.Models;
using VkPostman.Data;

namespace VkPostman.Wpf.Services;

/// <summary>CRUD for the shared placeholder library, plus a convenience map lookup.</summary>
public class PlaceholderService
{
    private readonly VkPostmanDbContext _db;

    public PlaceholderService(VkPostmanDbContext db)
    {
        _db = db;
    }

    public Task<List<PlaceholderDefinition>> GetAllAsync() =>
        _db.PlaceholderDefinitions.OrderBy(d => d.Key).ToListAsync();

    public Task<PlaceholderDefinition?> GetByKeyAsync(string key) =>
        _db.PlaceholderDefinitions.FirstOrDefaultAsync(d => d.Key == key);

    /// <summary>
    /// Returns every library definition keyed by its <see cref="PlaceholderDefinition.Key"/>.
    /// Used by <c>PostDraft.RenderForGroup</c> and the draft editor.
    /// </summary>
    public async Task<Dictionary<string, PlaceholderDefinition>> GetLibraryMapAsync()
    {
        var all = await _db.PlaceholderDefinitions.ToListAsync();
        return all.ToDictionary(d => d.Key, StringComparer.Ordinal);
    }

    public async Task<PlaceholderDefinition> AddAsync(PlaceholderDefinition def)
    {
        def.CreatedAt = DateTime.UtcNow;
        def.UpdatedAt = DateTime.UtcNow;
        _db.PlaceholderDefinitions.Add(def);
        await _db.SaveChangesAsync();
        return def;
    }

    public async Task UpdateAsync(PlaceholderDefinition def)
    {
        def.UpdatedAt = DateTime.UtcNow;
        _db.PlaceholderDefinitions.Update(def);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var d = await _db.PlaceholderDefinitions.FindAsync(id);
        if (d is null) return;
        _db.PlaceholderDefinitions.Remove(d);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Inserts a new definition for <paramref name="key"/> if the library
    /// doesn't have one yet. No-op otherwise. Used by the template editor's
    /// auto-sync when the user types <c>{{ new_key }}</c>.
    /// </summary>
    public async Task<PlaceholderDefinition> EnsureAsync(string key)
    {
        var existing = await GetByKeyAsync(key);
        if (existing is not null) return existing;

        return await AddAsync(new PlaceholderDefinition
        {
            Key         = key,
            DisplayName = ToDisplayName(key),
            Type        = PlaceholderType.Text,
        });
    }

    /// <summary>Returns templates whose body references <paramref name="key"/> — for blast-radius UI.</summary>
    public async Task<List<PostTemplate>> FindUsagesAsync(string key)
    {
        // We can't index into the body; a substring match is fine because the
        // library is small and templates are short.
        var needle = "{{";
        var all = await _db.PostTemplates.Where(t => t.BodyTemplate.Contains(needle)).ToListAsync();
        return all.Where(t => t.ExtractPlaceholders().Contains(key)).ToList();
    }

    private static string ToDisplayName(string key)
    {
        var spaced = key.Replace('_', ' ').Replace('-', ' ').Trim();
        if (spaced.Length == 0) return key;
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
