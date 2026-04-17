using Microsoft.EntityFrameworkCore;
using VkPostman.Core.Models;
using VkPostman.Data;

namespace VkPostman.Wpf.Services;

/// <summary>CRUD for <see cref="TargetGroup"/>. The VK API is gone — users enter groups by hand.</summary>
public class GroupService
{
    private readonly VkPostmanDbContext _db;

    public GroupService(VkPostmanDbContext db)
    {
        _db = db;
    }

    public Task<List<TargetGroup>> GetAllAsync() =>
        _db.TargetGroups
            .Include(g => g.PostTemplate)
            .OrderBy(g => g.DisplayName)
            .ToListAsync();

    public Task<TargetGroup?> GetByIdAsync(int id) =>
        _db.TargetGroups
            .Include(g => g.PostTemplate)
            .FirstOrDefaultAsync(g => g.Id == id);

    public async Task<TargetGroup> AddAsync(TargetGroup group)
    {
        // Normalize the screen name — accept @foo, vk.com/foo, etc.
        group.ScreenName = VkLinkNormalizer.Normalize(group.ScreenName).TrimStart('@');
        if (string.IsNullOrWhiteSpace(group.ScreenName))
            throw new ArgumentException("Screen name is required.");

        if (string.IsNullOrWhiteSpace(group.DisplayName))
            group.DisplayName = group.ScreenName;

        _db.TargetGroups.Add(group);
        await _db.SaveChangesAsync();
        return group;
    }

    public async Task UpdateAsync(TargetGroup group)
    {
        group.ScreenName = VkLinkNormalizer.Normalize(group.ScreenName).TrimStart('@');
        _db.TargetGroups.Update(group);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var g = await _db.TargetGroups.FindAsync(id);
        if (g is null) return;
        _db.TargetGroups.Remove(g);
        await _db.SaveChangesAsync();
    }
}
