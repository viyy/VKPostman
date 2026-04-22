using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace VkPostman.Core.Models;

/// <summary>
/// A reusable post template. Placeholders it uses are <b>derived</b> from the body
/// via <see cref="ExtractPlaceholders"/> — they aren't stored separately. Each key
/// found in the body is resolved against the shared <see cref="PlaceholderDefinition"/>
/// library at render time, so definitions are maintained in exactly one place.
/// </summary>
public class PostTemplate
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>Tags applied to every draft rendered with this template via <c>{{ theme_tags }}</c>.</summary>
    public List<string> DefaultThemeTags { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Placeholder keys automatically injected by the renderer for every group.
    /// These must NOT live in the shared library because they're filled from the
    /// draft / group, not from a user-fillable field.
    /// </summary>
    public static readonly IReadOnlyList<string> BuiltInPlaceholders =
        new[] { "common_text", "group_tags", "theme_tags", "group_name" };

    public static bool IsBuiltInPlaceholder(string key) =>
        BuiltInPlaceholders.Contains(key);

    private static readonly Regex PlaceholderRegex =
        new(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Unique set of placeholder keys referenced in <see cref="BodyTemplate"/>,
    /// in first-appearance order. Includes built-ins — filter them out at the
    /// call site if you only want library-backed keys.
    /// </summary>
    public IEnumerable<string> ExtractPlaceholders()
    {
        if (string.IsNullOrEmpty(BodyTemplate)) yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in PlaceholderRegex.Matches(BodyTemplate))
        {
            var key = m.Groups[1].Value;
            if (seen.Add(key)) yield return key;
        }
    }

    /// <summary>Keys in the body that aren't globals — i.e. those the library must define.</summary>
    public IEnumerable<string> ExtractLibraryPlaceholders() =>
        ExtractPlaceholders().Where(k => !IsBuiltInPlaceholder(k));
}
