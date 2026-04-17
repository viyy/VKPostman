namespace VkPostman.Core.Models;

public enum PlaceholderType
{
    /// <summary>Arbitrary free-form text; rendered as-is.</summary>
    Text = 0,

    /// <summary>A VK user or community. Input: any of <c>nelfias</c>, <c>@nelfias</c>, <c>vk.com/nelfias</c>. Rendered as <c>@shortname</c>.</summary>
    VkLink = 1,

    /// <summary>An arbitrary http(s) URL; rendered as-is.</summary>
    Url = 2,

    /// <summary>Space-separated tags; rendered as <c>#tag1 #tag2 …</c>.</summary>
    TagList = 3,

    /// <summary>VK wiki-style mention with two parts. Input: target (e.g. <c>nelfias</c> or <c>club123</c>) + display text. Rendered as <c>[target|display]</c>.</summary>
    WikiLink = 4,
}
