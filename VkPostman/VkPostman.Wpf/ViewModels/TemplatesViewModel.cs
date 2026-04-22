using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VkPostman.Core.Models;
using VkPostman.Wpf.Services;

namespace VkPostman.Wpf.ViewModels;

public partial class TemplatesViewModel : ObservableObject
{
    private readonly TemplateService _templates;
    private readonly PlaceholderService _placeholders;

    public ObservableCollection<PostTemplate> Templates { get; } = new();

    /// <summary>Every library definition (for chip-toolbar lookups + autocomplete).</summary>
    public ObservableCollection<PlaceholderDefinition> Library { get; } = new();

    [ObservableProperty] private PostTemplate? selectedTemplate;
    [ObservableProperty] private bool isEditing;
    [ObservableProperty] private string defaultTagsInput = "";

    /// <summary>Editable body text bound to the TextBox (mirrored into <see cref="SelectedTemplate"/>).</summary>
    [ObservableProperty] private string bodyText = "";

    /// <summary>Caret position inside the body (two-way bridged from the view).</summary>
    [ObservableProperty] private int bodyCaret;

    /// <summary>When caret is inside an unclosed <c>{{</c>, the partial typed after it.</summary>
    [ObservableProperty] private string? autocompleteQuery;

    /// <summary>Chip-toolbar items, filtered when autocomplete is active.</summary>
    public ObservableCollection<PlaceholderSuggestion> PlaceholderSuggestions { get; } = new();

    /// <summary>Rows shown in the "Placeholders used" section — derived from body + library.</summary>
    public ObservableCollection<UsedPlaceholderRow> UsedPlaceholders { get; } = new();

    public Autosave<PostTemplate> Autosave { get; }

    public TemplatesViewModel(TemplateService templates, PlaceholderService placeholders)
    {
        _templates = templates;
        _placeholders = placeholders;

        Autosave = new Autosave<PostTemplate>(
            get:  () => SelectedTemplate is { Id: > 0 } t ? t : null,
            save: SaveCurrentAsync);
        Autosave.Start();

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Templates.Clear();
        foreach (var t in await _templates.GetAllAsync())
            Templates.Add(t);
        await RefreshLibraryAsync();
    }

    private async Task RefreshLibraryAsync()
    {
        Library.Clear();
        foreach (var d in await _placeholders.GetAllAsync())
            Library.Add(d);
        RecomputeSuggestions();
        RecomputeUsedPlaceholders();
    }

    private async Task SaveCurrentAsync(PostTemplate t)
    {
        t.DefaultThemeTags = DefaultTagsInput
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.TrimStart('#'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        await _templates.UpdateAsync(t);
    }

    [RelayCommand]
    private async Task NewTemplateAsync()
    {
        await Autosave.FlushAsync();
        var t = new PostTemplate
        {
            Name = "New template",
            BodyTemplate = "{{ common_text }}\n\n{{ group_tags }} {{ theme_tags }}",
            DefaultThemeTags = new List<string>(),
        };
        await _templates.AddAsync(t);
        Templates.Insert(0, t);
        SelectedTemplate = t;
        DefaultTagsInput = "";
        BodyText = t.BodyTemplate;
        IsEditing = true;
        await Autosave.ResetAsync();
    }

    [RelayCommand]
    private async Task EditTemplateAsync(PostTemplate? template)
    {
        if (template is null) return;
        await Autosave.FlushAsync();
        SelectedTemplate = template;
    }

    [RelayCommand]
    private async Task CloseEditorAsync()
    {
        await Autosave.FlushAsync();
        IsEditing = false;
        SelectedTemplate = null;
        UsedPlaceholders.Clear();
    }

    [RelayCommand]
    private async Task DeleteTemplateAsync(PostTemplate? template)
    {
        if (template is null) return;
        Autosave.Stop();
        try
        {
            await _templates.DeleteAsync(template.Id);
            Templates.Remove(template);
            if (SelectedTemplate?.Id == template.Id)
            {
                SelectedTemplate = null;
                IsEditing = false;
                UsedPlaceholders.Clear();
            }
        }
        finally
        {
            Autosave.Start();
        }
    }

    partial void OnSelectedTemplateChanged(PostTemplate? value)
    {
        if (value is null)
        {
            IsEditing = false;
            UsedPlaceholders.Clear();
            return;
        }
        DefaultTagsInput = string.Join(" ", value.DefaultThemeTags);
        BodyText = value.BodyTemplate ?? string.Empty;
        IsEditing = true;
        _ = Autosave.ResetAsync();
        RecomputeUsedPlaceholders();
    }

    // ---- Body / autocomplete / auto-sync ------------------------------------

    partial void OnBodyTextChanged(string value)
    {
        if (SelectedTemplate is null) return;
        SelectedTemplate.BodyTemplate = value;
        RecomputeAutocompleteQuery();
        _ = AutoEnsureReferencedPlaceholdersAsync();
        RecomputeSuggestions();
        RecomputeUsedPlaceholders();
    }

    partial void OnBodyCaretChanged(int value)
    {
        _ = value;
        RecomputeAutocompleteQuery();
        RecomputeSuggestions();
    }

    private void RecomputeAutocompleteQuery()
    {
        var body = BodyText ?? string.Empty;
        var caret = Math.Clamp(BodyCaret, 0, body.Length);
        var left = body[..caret];
        var openIdx = left.LastIndexOf("{{", StringComparison.Ordinal);
        if (openIdx < 0) { AutocompleteQuery = null; return; }
        var between = left[(openIdx + 2)..];
        if (between.Contains("}}", StringComparison.Ordinal)) { AutocompleteQuery = null; return; }
        AutocompleteQuery = between.TrimStart();
    }

    /// <summary>
    /// For every body-referenced key that isn't a built-in, make sure there's a
    /// library entry. Creates a Text-type row with a default display name if not.
    /// </summary>
    private async Task AutoEnsureReferencedPlaceholdersAsync()
    {
        if (SelectedTemplate is null) return;
        var keys = SelectedTemplate.ExtractLibraryPlaceholders().ToList();
        var knownKeys = Library.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        var added = false;
        foreach (var key in keys)
        {
            if (knownKeys.Contains(key)) continue;
            var def = await _placeholders.EnsureAsync(key);
            Library.Add(def);
            knownKeys.Add(key);
            added = true;
        }
        if (added)
        {
            RecomputeSuggestions();
            RecomputeUsedPlaceholders();
        }
    }

    private void RecomputeSuggestions()
    {
        PlaceholderSuggestions.Clear();
        var all = Library.Select(d => d.Key)
            .Concat(PostTemplate.BuiltInPlaceholders)
            .Distinct(StringComparer.Ordinal);

        var q = AutocompleteQuery;
        if (!string.IsNullOrEmpty(q))
            all = all.Where(k => k.StartsWith(q, StringComparison.OrdinalIgnoreCase));

        foreach (var key in all)
        {
            PlaceholderSuggestions.Add(new PlaceholderSuggestion(
                Key: key,
                IsGlobal: PostTemplate.IsBuiltInPlaceholder(key)));
        }
    }

    private void RecomputeUsedPlaceholders()
    {
        UsedPlaceholders.Clear();
        if (SelectedTemplate is null) return;

        foreach (var key in SelectedTemplate.ExtractLibraryPlaceholders())
        {
            var def = Library.FirstOrDefault(d => d.Key == key);
            UsedPlaceholders.Add(new UsedPlaceholderRow(
                Key: key,
                DisplayName: def?.DisplayName ?? "(pending…)",
                Type: def?.Type.ToString() ?? "Text"));
        }
    }

    [RelayCommand]
    private void InsertPlaceholder(string? key)
    {
        if (string.IsNullOrEmpty(key) || SelectedTemplate is null) return;

        var body = BodyText ?? string.Empty;
        var caret = Math.Clamp(BodyCaret, 0, body.Length);
        string newBody;
        int newCaret;

        if (!string.IsNullOrEmpty(AutocompleteQuery))
        {
            var left = body[..caret];
            var openIdx = left.LastIndexOf("{{", StringComparison.Ordinal);
            var replacement = $"{{{{ {key} }}}}";
            newBody = body[..openIdx] + replacement + body[caret..];
            newCaret = openIdx + replacement.Length;
        }
        else
        {
            var token = $"{{{{ {key} }}}}";
            newBody = body[..caret] + token + body[caret..];
            newCaret = caret + token.Length;
        }

        BodyText = newBody;
        BodyCaret = newCaret;
    }
}

/// <summary>Chip-toolbar entry in the template editor.</summary>
public sealed record PlaceholderSuggestion(string Key, bool IsGlobal)
{
    public string Display => $"{{{{ {Key} }}}}";
}

/// <summary>One row in the "Placeholders used" section of the template editor.</summary>
public sealed record UsedPlaceholderRow(string Key, string DisplayName, string Type);
