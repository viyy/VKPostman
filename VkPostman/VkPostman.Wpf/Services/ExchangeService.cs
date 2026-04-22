using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using VkPostman.Core.Models;
using VkPostman.Data;

namespace VkPostman.Wpf.Services;

/// <summary>
/// JSON import/export for interchange with the PWA build. The shape must
/// stay byte-compatible with VkPostmanWeb/src/lib/exchange.ts.
///
/// Format v2 (current) adds a top-level <c>placeholders</c> array for the
/// shared library. Importer also accepts v1 files (no placeholders array,
/// templates carry inline <c>placeholderSchema</c>) by upserting those
/// inline definitions into the library during import.
/// </summary>
public class ExchangeService
{
    public const int FormatVersion = 2;

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
        var placeholders = await _db.PlaceholderDefinitions.AsNoTracking().ToListAsync(ct);
        var templates    = await _db.PostTemplates.AsNoTracking().ToListAsync(ct);
        var groups       = await _db.TargetGroups.AsNoTracking().ToListAsync(ct);
        var drafts       = await _db.PostDrafts.AsNoTracking().ToListAsync(ct);

        return new ExportFile
        {
            FormatVersion = FormatVersion,
            ExportedAt    = DateTime.UtcNow,
            App           = "vk-postman",
            Placeholders  = placeholders,
            Templates     = templates,
            Groups        = groups,
            Drafts        = drafts,
        };
    }

    public async Task<ImportSummary> ImportFromFileAsync(string path, CancellationToken ct = default)
    {
        // Parse into a JsonDocument first so we can detect and up-convert v1
        // files (which carried placeholder definitions inline inside each
        // template) before binding to the current POCO shape.
        JsonNode? root;
        await using (var fs = File.OpenRead(path))
        {
            root = await JsonNode.ParseAsync(fs, cancellationToken: ct);
        }
        if (root is not JsonObject obj)
            throw new InvalidDataException("Empty or unparseable export file.");

        var app = obj["app"]?.GetValue<string>();
        if (app != "vk-postman")
            throw new InvalidDataException($"Not a VK Postman export (app = {app ?? "null"}).");

        var formatVersion = obj["formatVersion"]?.GetValue<int>() ?? 0;
        if (formatVersion is < 1 or > FormatVersion)
            throw new InvalidDataException(
                $"Unsupported formatVersion {formatVersion}. This build understands 1..{FormatVersion}.");

        if (formatVersion == 1)
            MigrateV1ToV2(obj);

        var payload = obj.Deserialize<ExportFile>(_json)
            ?? throw new InvalidDataException("Could not bind payload to ExportFile.");

        return await ApplyImportAsync(payload, ct);
    }

    /// <summary>
    /// Flattens a v1 payload into v2 shape in place: collect every unique
    /// <c>templates[].placeholderSchema[]</c> entry into a new top-level
    /// <c>placeholders</c> array (first-write-wins on key conflicts), then
    /// strip the inline schema from each template.
    /// </summary>
    private static void MigrateV1ToV2(JsonObject root)
    {
        var library = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int idCounter = 1;

        if (root["templates"] is JsonArray templates)
        {
            foreach (var tNode in templates.OfType<JsonObject>())
            {
                if (tNode["placeholderSchema"] is JsonArray schema)
                {
                    foreach (var defNode in schema.OfType<JsonObject>())
                    {
                        var key = defNode["key"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(key) || !seen.Add(key)) continue;
                        var def = new JsonObject
                        {
                            ["id"]           = idCounter++,
                            ["key"]          = key,
                            ["displayName"]  = defNode["displayName"]?.DeepClone() ?? JsonValue.Create(key),
                            ["type"]         = defNode["type"]?.DeepClone() ?? JsonValue.Create(0),
                            ["description"]  = defNode["description"]?.DeepClone(),
                            ["defaultValue"] = defNode["defaultValue"]?.DeepClone(),
                        };
                        library.Add(def);
                    }
                    tNode.Remove("placeholderSchema");
                }
            }
        }

        root["placeholders"] = library;
        root["formatVersion"] = 2;
    }

    private async Task<ImportSummary> ApplyImportAsync(ExportFile payload, CancellationToken ct)
    {
        // Wipe the four tables, then insert fresh rows with remapped IDs.
        _db.PostDrafts.RemoveRange(_db.PostDrafts);
        _db.TargetGroups.RemoveRange(_db.TargetGroups);
        _db.PostTemplates.RemoveRange(_db.PostTemplates);
        _db.PlaceholderDefinitions.RemoveRange(_db.PlaceholderDefinitions);
        await _db.SaveChangesAsync(ct);

        // --- placeholder library ---
        foreach (var raw in payload.Placeholders ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw.Key)) continue;
            _db.PlaceholderDefinitions.Add(new PlaceholderDefinition
            {
                Key          = raw.Key!,
                DisplayName  = raw.DisplayName ?? raw.Key!,
                Type         = raw.Type,
                Description  = raw.Description,
                DefaultValue = raw.DefaultValue,
                CreatedAt    = raw.CreatedAt == default ? DateTime.UtcNow : raw.CreatedAt,
                UpdatedAt    = raw.UpdatedAt == default ? DateTime.UtcNow : raw.UpdatedAt,
            });
        }
        await _db.SaveChangesAsync(ct);

        // --- templates: id remap ---
        var templateIdMap = new Dictionary<int, int>();
        foreach (var raw in payload.Templates ?? [])
        {
            var oldId = raw.Id;
            var entity = new PostTemplate
            {
                Name             = raw.Name ?? string.Empty,
                Description      = raw.Description ?? string.Empty,
                BodyTemplate     = raw.BodyTemplate ?? string.Empty,
                DefaultThemeTags = raw.DefaultThemeTags ?? [],
                CreatedAt        = raw.CreatedAt == default ? DateTime.UtcNow : raw.CreatedAt,
                UpdatedAt        = raw.UpdatedAt == default ? DateTime.UtcNow : raw.UpdatedAt,
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
            payload.Placeholders?.Count ?? 0,
            payload.Templates?.Count    ?? 0,
            payload.Groups?.Count       ?? 0,
            payload.Drafts?.Count       ?? 0);
    }

    public sealed class ExportFile
    {
        public int FormatVersion { get; set; }
        public DateTime ExportedAt { get; set; }
        public string? App { get; set; }
        public List<PlaceholderDefinition>? Placeholders { get; set; }
        public List<PostTemplate>? Templates { get; set; }
        public List<TargetGroup>? Groups { get; set; }
        public List<PostDraft>? Drafts { get; set; }
    }

    public sealed record ImportSummary(int Placeholders, int Templates, int Groups, int Drafts);
}
