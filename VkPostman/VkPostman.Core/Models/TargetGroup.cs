using System.ComponentModel.DataAnnotations;

namespace VkPostman.Core.Models;

/// <summary>
/// A VK community the user wants to post to. Carries its own template + tags —
/// drafts reuse the same common content across many groups, but each group
/// renders its own version via <see cref="PostTemplate"/>.
/// </summary>
public class TargetGroup
{
    public int Id { get; set; }

    /// <summary>VK community ID (negative, e.g. -123456).</summary>
    [Required]
    public long VkGroupId { get; set; }

    /// <summary>Short name such as <c>nelfias_community</c> (no leading <c>@</c>).</summary>
    [Required, StringLength(100)]
    public string ScreenName { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    /// <summary>Tags appended to every post for this group via <c>{{ group_tags }}</c>.</summary>
    public List<string> MandatoryTags { get; set; } = [];

    /// <summary>Template used when rendering drafts for this group.</summary>
    public int? PostTemplateId { get; set; }
    public PostTemplate? PostTemplate { get; set; }

    /// <summary>Uncheck to temporarily hide from draft targeting.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Free-form notes about this community's rules, contacts, etc.</summary>
    [StringLength(1000)]
    public string Notes { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Browser URL for opening the community page (https://vk.com/<screen_name>).</summary>
    public string PublicUrl => $"https://vk.com/{ScreenName}";
}
