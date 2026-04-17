using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using VkPostman.Core.Models;
using VkPostman.Data;

namespace VkPostman.Wpf.Services;

/// <summary>
/// JSON import/export for interchange with the PWA build. The shape must
/// stay byte-compatible with VkPostmanWeb/src/lib/exchange.ts — if you
/// change one side, change the other.
///
/// Import replaces ALL existing data and remaps IDs so cross-references
/// (group→template, draft→groups) stay consistent regardless of what
/// IDs the source database happened to use.
/// </summary>
public class ExchangeService
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly VkPostmanDbContext _db;

    public ExchangeService(VkPostmanDbContext db)
    {
        _db = db;
    }

    public async Task ExportToFileAsync(string path, CancellationToken ct = default)
    {
        var payload = await BuildExportAsync(ct);
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, payload, _json, ct);
    }

    public async Task<ExportFile> BuildExportAsync(CancellationToken ct = default)
    {
        var templates = await _db.PostTemplates.AsNoTracking().ToListAsync(ct);
        var groups    = await _db.TargetGroups.AsNoTracking().ToListAsync(ct);
        var drafts    = await _db.PostDrafts.AsNoTracking().ToListAsync(ct);

        return new ExportFile
        {
            FormatVersion = FormatVersion,
            ExportedAt    = DateTime.UtcNow,
            App           = "vk-postman",
            Templates     = templates,
            Groups        = groups,
            Drafts        = drafts,
        };
    }

    public async Task<ImportSummary> ImportFromFileAsync(string path, CancellationToken ct = default)
    {
        ExportFile? payload;
        await using (var fs = File.OpenRead(path))
        {
            payload = await JsonSerializer.DeserializeAsync<ExportFile>(fs, _json, ct);
        }
        if (payload is null) throw new InvalidDataException("Empty or unparseable export file.");
        if (payload.App != "vk-postman")
            throw new InvalidDataException($"Not a VK Postman export (app = {payload.App ?? "null"}).");
        if (payload.FormatVersion is < 1 or > FormatVersion)
            throw new InvalidDataException(
                $"Unsupported formatVersion {payload.FormatVersion}. This build understands up to {FormatVersion}.");

        return await ApplyImportAsync(payload, ct);
    }

    private async Task<ImportSummary> ApplyImportAsync(ExportFile payload, CancellationToken ct)
    {
        // Wipe the three tables, then insert fresh rows with remapped IDs.
        _db.PostDrafts.RemoveRange(_db.PostDrafts);
        _db.TargetGroups.RemoveRange(_db.TargetGroups);
        _db.PostTemplates.RemoveRange(_db.PostTemplates);
        await _db.SaveChangesAsync(ct);

        // --- templates: id remap ---
        var templateIdMap = new Dictionary<int, int>();
        foreach (var raw in payload.Templates ?? [])
        {
            var oldId = raw.Id;
            var entity = new PostTemplate
            {
                Name              = raw.Name ?? string.Empty,
                Description       = raw.Description ?? string.Empty,
                BodyTemplate      = raw.BodyTemplate ?? string.Empty,
                DefaultThemeTags  = raw.DefaultThemeTags ?? [],
                PlaceholderSchema = raw.PlaceholderSchema ?? [],
                CreatedAt         = raw.CreatedAt == default ? DateTime.UtcNow : raw.CreatedAt,
                UpdatedAt         = raw.UpdatedAt == default ? DateTime.UtcNow : raw.UpdatedAt,
            };
            _db.PostTemplates.Add(entity);
            await _db.SaveChangesAsync(ct);
            if (oldId != 0) templateIdMap[oldId] = entity.Id;
        }

        // --- groups: id remap + template FK remap ---
        var groupIdMap = new Dictionary<int, int>();
        foreach (var raw in payload.Groups ?? [])
        {
            var oldId = raw.Id;
            int? remappedTemplateId = null;
            if (raw.PostTemplateId is int t && templateIdMap.TryGetValue(t, out var nt))
                remappedTemplateId = nt;

            var entity = new TargetGroup
            {
                ScreenName     = (raw.ScreenName ?? string.Empty).TrimStart('@'),
                DisplayName    = raw.DisplayName ?? string.Empty,
                Description    = raw.Description ?? string.Empty,
                AvatarUrl      = raw.AvatarUrl,
                MandatoryTags  = raw.MandatoryTags ?? [],
                PostTemplateId = remappedTemplateId,
                IsActive       = raw.IsActive,
                Notes          = raw.Notes ?? string.Empty,
                CreatedAt      = raw.CreatedAt == default ? DateTime.UtcNow : raw.CreatedAt,
            };
            _db.TargetGroups.Add(entity);
            await _db.SaveChangesAsync(ct);
            if (oldId != 0) groupIdMap[oldId] = entity.Id;
        }

        // --- drafts: id remap + target-group FK list remap ---
        foreach (var raw in payload.Drafts ?? [])
        {
            var remappedTargets = (raw.TargetGroupIds ?? [])
                .Where(id => groupIdMap.ContainsKey(id))
                .Select(id => groupIdMap[id])
                .ToList();

            var entity = new PostDraft
            {
                Title             = raw.Title ?? string.Empty,
                CommonText        = raw.CommonText ?? string.Empty,
                PlaceholderValues = raw.PlaceholderValues ?? [],
                ThemeTags         = raw.ThemeTags ?? [],
                TargetGroupIds    = remappedTargets,
                CreatedAt         = raw.CreatedAt == default ? DateTime.UtcNow : raw.CreatedAt,
                UpdatedAt         = raw.UpdatedAt == default ? DateTime.UtcNow : raw.UpdatedAt,
            };
            _db.PostDrafts.Add(entity);
        }
        await _db.SaveChangesAsync(ct);

        return new ImportSummary(
            payload.Templates?.Count ?? 0,
            payload.Groups?.Count    ?? 0,
            payload.Drafts?.Count    ?? 0);
    }

    public sealed class ExportFile
    {
        public int FormatVersion { get; set; }
        public DateTime ExportedAt { get; set; }
        public string? App { get; set; }
        public List<PostTemplate>? Templates { get; set; }
        public List<TargetGroup>? Groups { get; set; }
        public List<PostDraft>? Drafts { get; set; }
    }

    public sealed record ImportSummary(int Templates, int Groups, int Drafts);
}
