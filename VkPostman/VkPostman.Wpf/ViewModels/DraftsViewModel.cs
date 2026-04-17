using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VkPostman.Core.Models;
using VkPostman.Core.Services;
using VkPostman.Wpf.Services;

namespace VkPostman.Wpf.ViewModels;

public partial class DraftsViewModel : ObservableObject
{
    private readonly DraftService _drafts;
    private readonly GroupService _groups;
    private readonly ITemplateEngine _engine;

    public ObservableCollection<PostDraft> Drafts { get; } = new();
    public ObservableCollection<GroupSelection> AllGroups { get; } = new();
    public ObservableCollection<PlaceholderRow> Placeholders { get; } = new();
    public ObservableCollection<GroupRender> PerGroupRenders { get; } = new();

    [ObservableProperty] private PostDraft? currentDraft;
    [ObservableProperty] private string themeTagsInput = "";

    public DraftsViewModel(DraftService drafts, GroupService groups, ITemplateEngine engine)
    {
        _drafts = drafts;
        _groups = groups;
        _engine = engine;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await ReloadDraftsAsync();
        await ReloadGroupsAsync();
    }

    private async Task ReloadDraftsAsync()
    {
        Drafts.Clear();
        foreach (var d in await _drafts.GetAllAsync())
            Drafts.Add(d);

        if (CurrentDraft is null && Drafts.Count > 0)
            LoadDraft(Drafts[0]);
    }

    private async Task ReloadGroupsAsync()
    {
        var allGroups = await _groups.GetAllAsync();
        AllGroups.Clear();
        foreach (var g in allGroups)
        {
            var sel = new GroupSelection(g, CurrentDraft?.TargetGroupIds.Contains(g.Id) ?? false);
            sel.PropertyChanged += OnGroupSelectionChanged;
            AllGroups.Add(sel);
        }
        RecomputePlaceholders();
        RecomputeRenders();
    }

    [RelayCommand]
    private async Task NewDraftAsync()
    {
        var d = await _drafts.CreateAsync();
        await ReloadDraftsAsync();
        LoadDraft(d);
    }

    [RelayCommand]
    private void LoadDraft(PostDraft? draft)
    {
        if (draft is null) return;
        CurrentDraft = draft;
        ThemeTagsInput = string.Join(" ", draft.ThemeTags);

        // Sync checkbox state of group selections to this draft.
        foreach (var sel in AllGroups)
        {
            sel.PropertyChanged -= OnGroupSelectionChanged;
            sel.IsSelected = draft.TargetGroupIds.Contains(sel.Group.Id);
            sel.PropertyChanged += OnGroupSelectionChanged;
        }

        RecomputePlaceholders();
        RecomputeRenders();
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (CurrentDraft is null) return;

        CurrentDraft.ThemeTags = ThemeTagsInput
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.TrimStart('#'))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        // Drain placeholder row values into the dict (handles both single-field and WikiLink rows).
        foreach (var row in Placeholders)
            CurrentDraft.PlaceholderValues[row.Key] = row.EncodedValue;

        CurrentDraft.TargetGroupIds = AllGroups
            .Where(s => s.IsSelected)
            .Select(s => s.Group.Id)
            .ToList();

        await _drafts.UpdateAsync(CurrentDraft);
        await ReloadDraftsAsync();
        RecomputeRenders();
    }

    [RelayCommand]
    private async Task DeleteDraftAsync()
    {
        if (CurrentDraft is null) return;
        await _drafts.DeleteAsync(CurrentDraft.Id);
        CurrentDraft = null;
        await ReloadDraftsAsync();
    }

    [RelayCommand]
    private void CopyRendered(GroupRender? render)
    {
        if (render is null) return;
        try { Clipboard.SetText(render.Rendered); }
        catch { /* clipboard can throw transiently; best-effort */ }
    }

    [RelayCommand]
    private void OpenGroupInBrowser(GroupRender? render)
    {
        if (render is null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = render.Group.PublicUrl,
                UseShellExecute = true,
            });
        }
        catch { /* default browser open can fail; user can copy the URL manually */ }
    }

    partial void OnCurrentDraftChanged(PostDraft? value)
    {
        // If the draft changes, re-sync selections & placeholders.
        if (value is null) return;
    }

    private void OnGroupSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupSelection.IsSelected))
        {
            RecomputePlaceholders();
            RecomputeRenders();
        }
    }

    private IEnumerable<TargetGroup> SelectedGroups() =>
        AllGroups.Where(s => s.IsSelected).Select(s => s.Group);

    private void RecomputePlaceholders()
    {
        if (CurrentDraft is null)
        {
            Placeholders.Clear();
            return;
        }

        var usages = CurrentDraft.UnionedPlaceholders(SelectedGroups())
            .OrderBy(u => u.Definition.DisplayName)
            .ToList();

        // Preserve typed-but-not-yet-saved values by reading from Placeholders first, then falling back.
        var liveValues = Placeholders.ToDictionary(p => p.Key, p => (string?)p.EncodedValue, StringComparer.Ordinal);

        Placeholders.Clear();
        foreach (var u in usages)
        {
            var existing = liveValues.TryGetValue(u.Definition.Key, out var liveV)
                ? liveV
                : (CurrentDraft.PlaceholderValues.TryGetValue(u.Definition.Key, out var dbV) ? dbV : null);

            var row = new PlaceholderRow(u.Definition, string.Join(", ", u.UsedByGroups), existing);
            row.PropertyChanged += (_, e) =>
            {
                // Any value-bearing property change → re-encode and re-render.
                if (e.PropertyName is nameof(PlaceholderRow.Value)
                                    or nameof(PlaceholderRow.WikiTarget)
                                    or nameof(PlaceholderRow.WikiDisplay))
                {
                    CurrentDraft!.PlaceholderValues[row.Key] = row.EncodedValue;
                    RecomputeRenders();
                }
            };
            Placeholders.Add(row);
        }
    }

    /// <summary>Helper for the per-row `liveValues` preservation — returns the current encoded value.</summary>
    private string? PlaceholderLiveValue(PlaceholderRow row) => row.EncodedValue;

    private void RecomputeRenders()
    {
        PerGroupRenders.Clear();
        if (CurrentDraft is null) return;

        foreach (var g in SelectedGroups())
        {
            string rendered;
            try
            {
                rendered = g.PostTemplate is null
                    ? "[This group has no template assigned.]"
                    : CurrentDraft.RenderForGroup(g, _engine);
            }
            catch (Exception ex)
            {
                rendered = $"[Render error: {ex.Message}]";
            }
            PerGroupRenders.Add(new GroupRender(g, rendered));
        }
    }

    partial void OnThemeTagsInputChanged(string value)
    {
        if (CurrentDraft is null) return;
        CurrentDraft.ThemeTags = value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.TrimStart('#'))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        RecomputeRenders();
    }
}

public partial class GroupSelection : ObservableObject
{
    public TargetGroup Group { get; }

    [ObservableProperty] private bool isSelected;

    public string HasTemplateHint =>
        Group.PostTemplate is null ? "⚠ no template assigned" : $"template: {Group.PostTemplate.Name}";

    public bool HasTemplate => Group.PostTemplate is not null;

    public GroupSelection(TargetGroup group, bool isSelected)
    {
        Group = group;
        this.isSelected = isSelected;
    }
}

public partial class PlaceholderRow : ObservableObject
{
    public PlaceholderDefinition Definition { get; }
    public string Key => Definition.Key;
    public string DisplayName => Definition.DisplayName;
    public bool IsRequired => Definition.IsRequired;
    public string UsageHint { get; }
    public string TypeLabel => Definition.Type switch
    {
        PlaceholderType.VkLink   => "VK link",
        PlaceholderType.WikiLink => "wiki link",
        PlaceholderType.Url      => "URL",
        PlaceholderType.TagList  => "tags",
        _                        => "text",
    };

    public bool IsWikiLink    => Definition.Type == PlaceholderType.WikiLink;
    public bool IsSingleField => !IsWikiLink;

    /// <summary>The single-field value (Text / VkLink / Url / TagList).</summary>
    [ObservableProperty] private string? value;

    /// <summary>WikiLink field 1 — the <c>@target</c> / <c>club123</c> part.</summary>
    [ObservableProperty] private string? wikiTarget;

    /// <summary>WikiLink field 2 — the displayed text shown to readers.</summary>
    [ObservableProperty] private string? wikiDisplay;

    /// <summary>What we actually persist into the draft's PlaceholderValues dictionary.</summary>
    public string EncodedValue =>
        Definition.Type == PlaceholderType.WikiLink
            ? PlaceholderDefinition.PackWikiLink(WikiTarget, WikiDisplay)
            : Value ?? "";

    public PlaceholderRow(PlaceholderDefinition definition, string usageHint, string? existing = null)
    {
        Definition = definition;
        UsageHint = usageHint;
        Populate(existing);
    }

    private void Populate(string? existing)
    {
        if (Definition.Type == PlaceholderType.WikiLink)
        {
            var (target, display) = PlaceholderDefinition.SplitWikiLink(existing);
            WikiTarget  = target;
            WikiDisplay = display;
        }
        else
        {
            Value = existing ?? "";
        }
    }
}

public sealed record GroupRender(TargetGroup Group, string Rendered);
