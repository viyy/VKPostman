using System.ComponentModel.DataAnnotations;

namespace VkPostman.Core.Models;

public class PostTemplate
{
    public int Id { get; set; }
    
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;
    
    [Required]
    public string BodyTemplate { get; set; } = string.Empty;
    
    public List<string> DefaultThemeTags { get; set; } = [];
    
    public List<PlaceholderDefinition> PlaceholderSchema { get; set; } = [];
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public List<string> GetRequiredPlaceholders()
        => PlaceholderSchema.Where(p => p.IsRequired).Select(p => p.Key).ToList();

    /// <summary>
    /// Placeholder keys automatically injected by the renderer for every group.
    /// These must NOT be auto-added to the schema because they're filled from
    /// the draft / group, not from a user-fillable field in the editor.
    /// </summary>
    public static readonly IReadOnlyList<string> BuiltInPlaceholders =
        new[] { "common_text", "group_tags", "theme_tags", "group_name" };

    public static bool IsBuiltInPlaceholder(string key) =>
        BuiltInPlaceholders.Contains(key);

    /// <summary>
    /// Unique set of placeholder keys referenced in <see cref="BodyTemplate"/>,
    /// in first-appearance order. Used by the template editor to auto-append
    /// new placeholder rows as the user types <c>{{ name }}</c> into the body.
    /// </summary>
    public IEnumerable<string> ExtractPlaceholders()
    {
        if (string.IsNullOrEmpty(BodyTemplate)) yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matches = System.Text.RegularExpressions.Regex.Matches(
            BodyTemplate,
            @"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var key = m.Groups[1].Value;
            if (seen.Add(key)) yield return key;
        }
    }
}
