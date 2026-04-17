using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace VkPostman.Core.Models;

public record PlaceholderDefinition(
    [property: Required, StringLength(50)] string Key,
    [property: Required, StringLength(100)] string DisplayName,
    bool IsRequired,
    PlaceholderType Type,
    [property: StringLength(200)] string? Description = null,
    [property: StringLength(100)] string? ValidationPattern = null,
    [property: StringLength(200)] string? DefaultValue = null)
{
    /// <summary>
    /// ASCII unit-separator — used to pack two fields (target + display text) into the single
    /// string slot the <see cref="PostDraft.PlaceholderValues"/> dictionary provides. Humans
    /// don't type this, so it's safe as a delimiter.
    /// </summary>
    public const char WikiLinkSeparator = '\u001F';

    public bool Validate(string? value)
    {
        if (IsRequired && string.IsNullOrWhiteSpace(value))
            return false;

        if (string.IsNullOrEmpty(value))
            return true;

        return Type switch
        {
            PlaceholderType.VkLink   => VkLinkNormalizer.LooksLikeVkLink(value),
            PlaceholderType.Url      => Uri.TryCreate(value, UriKind.Absolute, out _),
            PlaceholderType.TagList  => ValidateTagList(value),
            PlaceholderType.WikiLink => ValidateWikiLink(value),
            PlaceholderType.Text     => true,
            _                        => true,
        };
    }

    /// <summary>
    /// Turns the user's raw value into the string that should be substituted into the template.
    /// </summary>
    public string Render(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultValue ?? string.Empty;

        return Type switch
        {
            PlaceholderType.VkLink   => VkLinkNormalizer.Normalize(value),
            PlaceholderType.TagList  => NormalizeTagList(value),
            PlaceholderType.WikiLink => RenderWikiLink(value),
            PlaceholderType.Text     => value,
            PlaceholderType.Url      => value,
            _                        => value,
        };
    }

    private static bool ValidateTagList(string value)
    {
        var tags = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tags.All(tag => tag.Length > 0 && (tag.StartsWith('#') || char.IsLetter(tag[0])));
    }

    private static bool ValidateWikiLink(string value)
    {
        var (target, _) = SplitWikiLink(value);
        // Display text is optional (we fall back to target) — we just require a non-empty target.
        return !string.IsNullOrWhiteSpace(target);
    }

    private static string NormalizeTagList(string value) =>
        string.Join(" ", value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.StartsWith('#') ? t : $"#{t}"));

    private static string RenderWikiLink(string packed)
    {
        var (rawTarget, display) = SplitWikiLink(packed);
        if (string.IsNullOrWhiteSpace(rawTarget))
            return string.Empty;

        // Strip any vk.com/ or @ the user typed in the target field.
        var target = VkLinkNormalizer.Normalize(rawTarget).TrimStart('@');
        var label  = string.IsNullOrWhiteSpace(display) ? target : display;
        return $"[{target}|{label}]";
    }

    /// <summary>Unpacks a packed WikiLink value into (target, display).</summary>
    public static (string Target, string Display) SplitWikiLink(string? packed)
    {
        if (string.IsNullOrEmpty(packed)) return ("", "");
        var parts = packed.Split(WikiLinkSeparator, 2);
        return (parts[0], parts.Length > 1 ? parts[1] : "");
    }

    public static string PackWikiLink(string? target, string? display) =>
        $"{target ?? ""}{WikiLinkSeparator}{display ?? ""}";
}

/// <summary>
/// Canonicalizes the many shapes a user might type when referring to a VK profile or community.
/// Accepts: <c>nelfias</c>, <c>@nelfias</c>, <c>vk.com/nelfias</c>, <c>https://vk.com/nelfias</c>,
/// trailing slashes, query strings. Outputs: <c>@shortname</c> (VK's post editor auto-links it).
/// </summary>
public static class VkLinkNormalizer
{
    private static readonly Regex _vkHost =
        new(@"^(https?://)?(www\.|m\.)?(vk\.com|vk\.ru)/", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _shortnameShape =
        new(@"^[A-Za-z_][A-Za-z0-9_\.]{0,63}$", RegexOptions.Compiled);

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var trimmed = input.Trim();

        var hostMatch = _vkHost.Match(trimmed);
        if (hostMatch.Success)
            trimmed = trimmed[hostMatch.Length..];

        if (trimmed.StartsWith('@'))
            trimmed = trimmed[1..];

        var slashIdx = trimmed.IndexOfAny(['/', '?', '#']);
        if (slashIdx >= 0) trimmed = trimmed[..slashIdx];

        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : $"@{trimmed}";
    }

    public static bool LooksLikeVkLink(string input)
    {
        var normalized = Normalize(input);
        if (string.IsNullOrEmpty(normalized)) return false;
        return _shortnameShape.IsMatch(normalized[1..]);
    }
}
