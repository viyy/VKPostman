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
    private readonly PlaceholderService _placeholders;
    private readonly ITemplateEngine _engine;

    /// <summary>Fresh-every-render snapshot of the library, keyed by key.</summary>
    private Dictionary<string, PlaceholderDefinition> _library = new(StringComparer.Ordinal);

    public ObservableCollection<PostDraft> Drafts { get; } = new();
    public ObservableCollection<GroupSelection> AllGroups { get; } = new();
    public ObservableCollection<PlaceholderRow> Placeholders { get; } = new();
    public ObservableCollection<GroupRender> PerGroupRenders { get; } = new();

    [ObservableProperty] private PostDraft? currentDraft;
    [ObservableProperty] private string themeTagsInput = "";

    public DraftsViewModel(
        DraftService drafts,
        GroupService groups,
        PlaceholderService placeholders,
        ITemplateEngine engine)
    {
        _drafts = drafts;
        _groups = groups;
        _placeholders = placeholders;
        _engine = engine;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        _library = await _placeholders.GetLibraryMapAsync();
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

        foreach (var row in Placeholders)
            CurrentDraft.PlaceholderValues[row.Key] = row.EncodedValue;

        CurrentDraft.TargetGroupIds = AllGroups
            .Where(s => s.IsSelected)
            .Select(s => s.Group.Id)
            .ToList();

        await _drafts.UpdateAsync(CurrentDraft);

        // Refresh library in case the user added a placeholder in the Templates
        // tab between edits — otherwise we'd render with stale definitions.
        _library = await _placeholders.GetLibraryMapAsync();
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
        catch { }
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

        var usages = CurrentDraft.UnionedPlaceholders(SelectedGroups(), _library)
            .OrderBy(u => u.Definition?.DisplayName ?? u.Key)
            .ToList();

        var liveValues = Placeholders.ToDictionary(p => p.Key, p => (string?)p.EncodedValue, StringComparer.Ordinal);

        Placeholders.Clear();
        foreach (var u in usages)
        {
            var existing = liveValues.TryGetValue(u.Key, out var liveV)
                ? liveV
                : (CurrentDraft.PlaceholderValues.TryGetValue(u.Key, out var dbV) ? dbV : null);

            var row = new PlaceholderRow(u.Key, u.Definition, string.Join(", ", u.UsedByGroups), existing);
            row.PropertyChanged += (_, e) =>
            {
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
                    : CurrentDraft.RenderForGroup(g, _engine, _library);
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

/// <summary>
/// One row in the Drafts editor placeholder list. Wraps a library definition
/// (may be null while a newly-referenced key is still being persisted).
/// </summary>
public partial class PlaceholderRow : ObservableObject
{
    public string Key { get; }
    public PlaceholderDefinition? Definition { get; }
    public string DisplayName => Definition?.DisplayName ?? Key;
    public string UsageHint { get; }
    public string TypeLabel => (Definition?.Type ?? PlaceholderType.Text) switch
    {
        PlaceholderType.VkLink   => "VK link",
        PlaceholderType.WikiLink => "wiki link",
        PlaceholderType.Url      => "URL",
        PlaceholderType.TagList  => "tags",
        _                        => "text",
    };

    public bool IsWikiLink    => Definition?.Type == PlaceholderType.WikiLink;
    public bool IsSingleField => !IsWikiLink;

    [ObservableProperty] private string? value;
    [ObservableProperty] private string? wikiTarget;
    [ObservableProperty] private string? wikiDisplay;

    public string EncodedValue =>
        IsWikiLink
            ? PlaceholderDefinition.PackWikiLink(WikiTarget, WikiDisplay)
            : Value ?? "";

    public PlaceholderRow(string key, PlaceholderDefinition? definition, string usageHint, string? existing = null)
    {
        Key = key;
        Definition = definition;
        UsageHint = usageHint;
        if (IsWikiLink)
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
