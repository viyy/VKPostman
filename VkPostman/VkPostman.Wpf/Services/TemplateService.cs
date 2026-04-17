using Microsoft.EntityFrameworkCore;
using VkPostman.Core.Models;
using VkPostman.Data;

namespace VkPostman.Wpf.Services;

public class TemplateService
{
    private readonly VkPostmanDbContext _db;

    public TemplateService(VkPostmanDbContext db)
    {
        _db = db;
    }

    public Task<List<PostTemplate>> GetAllAsync() =>
        _db.PostTemplates.OrderByDescending(t => t.UpdatedAt).ToListAsync();

    public Task<PostTemplate?> GetByIdAsync(int id) =>
        _db.PostTemplates.FirstOrDefaultAsync(t => t.Id == id);

    public async Task<PostTemplate> AddAsync(PostTemplate template)
    {
        template.CreatedAt = DateTime.UtcNow;
        template.UpdatedAt = DateTime.UtcNow;
        _db.PostTemplates.Add(template);
        await _db.SaveChangesAsync();
        return template;
    }

    public async Task UpdateAsync(PostTemplate template)
    {
        template.UpdatedAt = DateTime.UtcNow;
        _db.PostTemplates.Update(template);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var t = await _db.PostTemplates.FindAsync(id);
        if (t is null) return;
        _db.PostTemplates.Remove(t);
        await _db.SaveChangesAsync();
    }
}
