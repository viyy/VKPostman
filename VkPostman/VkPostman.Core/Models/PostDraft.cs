using System.ComponentModel.DataAnnotations;
using VkPostman.Core.Services;

namespace VkPostman.Core.Models;

/// <summary>
/// A draft post — pure common content that gets rendered once per selected
/// <see cref="TargetGroup"/> using each group's own <see cref="PostTemplate"/>.
/// Placeholder definitions come from the shared library, passed in as a
/// dictionary keyed by <see cref="PlaceholderDefinition.Key"/>.
/// </summary>
public class PostDraft
{
    public int Id { get; set; }

    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Free-form body shared across every selected group; templates access it as <c>{{ common_text }}</c>.</summary>
    public string CommonText { get; set; } = string.Empty;

    /// <summary>Values entered for placeholders. Key matches <see cref="PlaceholderDefinition.Key"/>.</summary>
    public Dictionary<string, string> PlaceholderValues { get; set; } = new();

    /// <summary>Tags added to every group on top of each group's own <see cref="TargetGroup.MandatoryTags"/>.</summary>
    public List<string> ThemeTags { get; set; } = [];

    public List<int> TargetGroupIds { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ready when: at least one group is selected, each selected group has a template,
    /// and no required-ness check is applied (the library dropped IsRequired — fields
    /// render as their default value when blank rather than blocking publish).
    /// </summary>
    public bool IsReadyToCopy(IEnumerable<TargetGroup> selectedGroups)
    {
        var groups = selectedGroups.ToList();
        if (groups.Count == 0) return false;
        return groups.All(g => g.PostTemplate is not null);
    }

    /// <summary>
    /// Union of placeholder keys across the selected groups' templates. Each key maps
    /// to its library definition (or null if the library hasn't got one yet) plus the
    /// list of groups whose body references it.
    /// </summary>
    public IEnumerable<PlaceholderUsage> UnionedPlaceholders(
        IEnumerable<TargetGroup> selectedGroups,
        IReadOnlyDictionary<string, PlaceholderDefinition> library)
    {
        var byKey = new Dictionary<string, PlaceholderUsage>(StringComparer.Ordinal);
        foreach (var group in selectedGroups)
        {
            if (group.PostTemplate is null) continue;
            foreach (var key in group.PostTemplate.ExtractLibraryPlaceholders())
            {
                if (!byKey.TryGetValue(key, out var usage))
                {
                    library.TryGetValue(key, out var def);
                    usage = new PlaceholderUsage(key, def, new List<string>());
                    byKey[key] = usage;
                }
                usage.UsedByGroups.Add(group.DisplayName);
            }
        }
        return byKey.Values;
    }

    public string RenderForGroup(
        TargetGroup group,
        ITemplateEngine templateEngine,
        IReadOnlyDictionary<string, PlaceholderDefinition> library)
    {
        if (group.PostTemplate is null)
            throw new InvalidOperationException(
                $"Group '{group.DisplayName}' has no template assigned — cannot render.");

        // Per-key render through library definitions (VK links → @shortname, tags → #foo).
        var renderedValues = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var key in group.PostTemplate.ExtractLibraryPlaceholders())
        {
            var rawValue = PlaceholderValues.TryGetValue(key, out var v) ? v : "";
            renderedValues[key] = library.TryGetValue(key, out var def)
                ? def.Render(rawValue)
                : rawValue; // library miss — fall back to raw so render doesn't silently drop content
        }

        renderedValues["group_name"]  = group.DisplayName;
        renderedValues["group_tags"]  = string.Join(" ", group.MandatoryTags.Select(Hashify));
        renderedValues["theme_tags"]  = string.Join(" ", ThemeTags.Select(Hashify));
        renderedValues["common_text"] = CommonText ?? string.Empty;

        return templateEngine.Render(group.PostTemplate.BodyTemplate, renderedValues);
    }

    private static string Hashify(string t) => t.StartsWith('#') ? t : $"#{t}";
}

/// <summary>
/// A placeholder referenced by one or more selected templates, plus the library
/// definition (if any) and which groups reference it by display name.
/// </summary>
public sealed record PlaceholderUsage(string Key, PlaceholderDefinition? Definition, List<string> UsedByGroups);
