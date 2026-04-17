namespace VkPostman.Core.Extensions;

public static class StringExtensions
{
    public static string ToVkHashtag(this string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return string.Empty;
            
        var clean = tag.Trim();
        if (clean.StartsWith("#"))
            return clean;
            
        // Remove spaces and special characters, keep only letters, digits, underscores
        var sanitized = new string(clean
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray());
            
        return sanitized.Length > 0 ? $"#{sanitized}" : string.Empty;
    }
    
    public static string ToVkProfileLink(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;
            
        var clean = input.Trim();
        
        // Already a full link
        if (clean.StartsWith("https://vk.com/") || clean.StartsWith("http://vk.com/"))
            return clean;
            
        // Short format vk.com/username
        if (clean.StartsWith("vk.com/"))
            return $"https://{clean}";
            
        // Handle @username format
        if (clean.StartsWith("@"))
            return $"https://vk.com/{clean[1..]}";
            
        // Assume it's a username
        return $"https://vk.com/{clean}";
    }
    
    public static List<string> ExtractHashtags(this string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
            
        var matches = System.Text.RegularExpressions.Regex.Matches(
            text, 
            @"#\w+");
            
        return matches.Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Value)
            .Distinct()
            .ToList();
    }
}
