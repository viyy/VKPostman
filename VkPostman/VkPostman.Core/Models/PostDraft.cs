using System.ComponentModel.DataAnnotations;
using VkPostman.Core.Services;

namespace VkPostman.Core.Models;

/// <summary>
/// A draft post — pure common content that gets rendered once per selected
/// <see cref="TargetGroup"/> using each group's own <see cref="PostTemplate"/>.
/// No publishing/photo concerns; the rendered string is copied by hand into VK.
/// </summary>
public class PostDraft
{
    public int Id { get; set; }

    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Free-form body shared across every selected group; templates access it as <c>{{ common_text }}</c>.</summary>
    public string CommonText { get; set; } = string.Empty;

    /// <summary>Union of placeholder values across the selected groups' templates.</summary>
    public Dictionary<string, string> PlaceholderValues { get; set; } = new();

    /// <summary>Tags added to every group on top of each group's own <see cref="TargetGroup.MandatoryTags"/>.</summary>
    public List<string> ThemeTags { get; set; } = [];

    public List<int> TargetGroupIds { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ready when: at least one group is selected, each selected group has a template, and
    /// every required placeholder across the union of those templates has a non-empty value.
    /// </summary>
    public bool IsReadyToCopy(IEnumerable<TargetGroup> selectedGroups)
    {
        var groups = selectedGroups.ToList();
        if (groups.Count == 0) return false;
        if (groups.Any(g => g.PostTemplate is null)) return false;

        var requiredKeys = groups
            .SelectMany(g => g.PostTemplate!.GetRequiredPlaceholders())
            .Distinct();

        return requiredKeys.All(k =>
            PlaceholderValues.TryGetValue(k, out var v) &&
            !string.IsNullOrWhiteSpace(v));
    }

    /// <summary>
    /// Union of placeholders across the selected groups' templates, carrying which groups
    /// each one comes from so the UI can surface "used by: Group A, Group B". Stricter
    /// required-ness wins if two templates disagree.
    /// </summary>
    public IEnumerable<PlaceholderUsage> UnionedPlaceholders(IEnumerable<TargetGroup> selectedGroups)
    {
        var byKey = new Dictionary<string, PlaceholderUsage>(StringComparer.Ordinal);
        foreach (var group in selectedGroups)
        {
            if (group.PostTemplate is null) continue;
            foreach (var def in group.PostTemplate.PlaceholderSchema)
            {
                if (!byKey.TryGetValue(def.Key, out var usage))
                {
                    usage = new PlaceholderUsage(def, new List<string>());
                    byKey[def.Key] = usage;
                }
                if (def.IsRequired && !usage.Definition.IsRequired)
                {
                    byKey[def.Key] = usage with
                    {
                        Definition = usage.Definition with { IsRequired = true }
                    };
                }
                usage.UsedByGroups.Add(group.DisplayName);
            }
        }
        return byKey.Values;
    }

    public string RenderForGroup(TargetGroup group, ITemplateEngine templateEngine)
    {
        if (group.PostTemplate is null)
            throw new InvalidOperationException(
                $"Group '{group.DisplayName}' has no template assigned — cannot render.");

        // Let each placeholder's type normalize its value (VK links → @shortname, tags → #foo).
        var schemaByKey = group.PostTemplate.PlaceholderSchema
            .ToDictionary(d => d.Key, StringComparer.Ordinal);

        var renderedValues = PlaceholderValues.ToDictionary(
            kv => kv.Key,
            kv => (object)(schemaByKey.TryGetValue(kv.Key, out var def) ? def.Render(kv.Value) : kv.Value),
            StringComparer.Ordinal);

        var context = new Dictionary<string, object>(renderedValues)
        {
            ["group_name"]  = group.DisplayName,
            ["group_tags"]  = string.Join(" ", group.MandatoryTags.Select(Hashify)),
            ["theme_tags"]  = string.Join(" ", ThemeTags.Select(Hashify)),
            ["common_text"] = CommonText ?? string.Empty,
        };

        return templateEngine.Render(group.PostTemplate.BodyTemplate, context);
    }

    private static string Hashify(string t) => t.StartsWith('#') ? t : $"#{t}";
}

/// <summary>A placeholder seen in one or more selected templates, plus which groups use it.</summary>
public sealed record PlaceholderUsage(PlaceholderDefinition Definition, List<string> UsedByGroups);
