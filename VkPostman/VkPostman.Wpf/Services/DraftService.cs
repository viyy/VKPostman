using Microsoft.EntityFrameworkCore;
using VkPostman.Core.Models;
using VkPostman.Data;

namespace VkPostman.Wpf.Services;

/// <summary>Thin CRUD wrapper around <see cref="PostDraft"/>. No publishing logic — that's gone now.</summary>
public class DraftService
{
    private readonly VkPostmanDbContext _db;

    public DraftService(VkPostmanDbContext db)
    {
        _db = db;
    }

    public Task<List<PostDraft>> GetAllAsync() =>
        _db.PostDrafts.OrderByDescending(d => d.UpdatedAt).ToListAsync();

    public Task<PostDraft?> GetByIdAsync(int id) =>
        _db.PostDrafts.FirstOrDefaultAsync(d => d.Id == id);

    public async Task<PostDraft> CreateAsync(string? title = null)
    {
        var draft = new PostDraft
        {
            Title = string.IsNullOrWhiteSpace(title)
                ? $"Draft — {DateTime.Now:yyyy-MM-dd HH:mm}"
                : title!,
        };
        _db.PostDrafts.Add(draft);
        await _db.SaveChangesAsync();
        return draft;
    }

    public async Task UpdateAsync(PostDraft draft)
    {
        draft.UpdatedAt = DateTime.UtcNow;
        _db.PostDrafts.Update(draft);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var d = await _db.PostDrafts.FindAsync(id);
        if (d is null) return;
        _db.PostDrafts.Remove(d);
        await _db.SaveChangesAsync();
    }
}
