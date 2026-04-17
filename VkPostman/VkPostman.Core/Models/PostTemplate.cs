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
    
    public List<string> ExtractPlaceholders()
    {
        var placeholders = new List<string>();
        var matches = System.Text.RegularExpressions.Regex.Matches(
            BodyTemplate, 
            @"\{([^}]+)\}");
            
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            placeholders.Add(match.Groups[1].Value);
        }
        
        return placeholders.Distinct().ToList();
    }
}
