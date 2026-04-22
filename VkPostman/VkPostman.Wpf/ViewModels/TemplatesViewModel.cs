using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VkPostman.Core.Models;
using VkPostman.Wpf.Services;

namespace VkPostman.Wpf.ViewModels;

public partial class TemplatesViewModel : ObservableObject
{
    private readonly TemplateService _templates;

    public ObservableCollection<PostTemplate> Templates { get; } = new();

    public Array PlaceholderTypes { get; } = Enum.GetValues(typeof(PlaceholderType));

    [ObservableProperty] private PostTemplate? selectedTemplate;
    [ObservableProperty] private bool isEditing;
    [ObservableProperty] private string defaultTagsInput = "";

    // Mutable editor wrapper — PlaceholderDefinition is an immutable record,
    // so WPF two-way binding can't drive it directly.
    public ObservableCollection<PlaceholderEditRow> PlaceholderEditorItems { get; } = new();

    /// <summary>
    /// The chip toolbar shown under the Body textbox. Rebuilt whenever the
    /// schema changes, the body changes, or the caret moves into / out of a
    /// partial <c>{{ … </c>. Filters down to autocomplete matches when the
    /// caret is inside an unclosed expression.
    /// </summary>
    public ObservableCollection<PlaceholderSuggestion> PlaceholderSuggestions { get; } = new();

    /// <summary>The body text currently in the editor — bound to the TextBox.</summary>
    [ObservableProperty] private string bodyText = "";

    /// <summary>Where the caret sits in <see cref="BodyText"/> — bound from the TextBox.</summary>
    [ObservableProperty] private int bodyCaret;

    /// <summary>Non-null when the caret is between an open <c>{{</c> and a not-yet-closed end.</summary>
    [ObservableProperty] private string? autocompleteQuery;

    public Autosave<PostTemplate> Autosave { get; }

    public TemplatesViewModel(TemplateService templates)
    {
        _templates = templates;

        Autosave = new Autosave<PostTemplate>(
            get:  () => SelectedTemplate is { Id: > 0 } t ? t : null,
            save: SaveCurrentAsync);
        Autosave.Start();

        // Rebuild the chip toolbar whenever the schema grows or a row renames
        // its Key, so the "user-defined" chips track whatever's in the list.
        PlaceholderEditorItems.CollectionChanged += OnSchemaChanged;

        _ = LoadAsync();
    }

    private void OnSchemaChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (PlaceholderEditRow r in e.NewItems) r.PropertyChanged += OnSchemaRowChanged;
        if (e.OldItems is not null)
            foreach (PlaceholderEditRow r in e.OldItems) r.PropertyChanged -= OnSchemaRowChanged;
        RecomputeSuggestions();
    }

    private void OnSchemaRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaceholderEditRow.Key))
            RecomputeSuggestions();
    }

    private async Task LoadAsync()
    {
        Templates.Clear();
        foreach (var t in await _templates.GetAllAsync())
            Templates.Add(t);
    }

    /// <summary>Autosave callback — drains UI buffers into the entity and persists.</summary>
    private async Task SaveCurrentAsync(PostTemplate t)
    {
        t.DefaultThemeTags = DefaultTagsInput
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.TrimStart('#'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        t.PlaceholderSchema = PlaceholderEditorItems
            .Select(r => r.ToDefinition())
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
            PlaceholderSchema = new List<PlaceholderDefinition>(),
            DefaultThemeTags = new List<string>(),
        };
        await _templates.AddAsync(t);
        Templates.Insert(0, t);

        SelectedTemplate = t;
        DefaultTagsInput = "";
        PlaceholderEditorItems.Clear();
        IsEditing = true;
        await Autosave.ResetAsync();
    }

    [RelayCommand]
    private async Task EditTemplateAsync(PostTemplate? template)
    {
        if (template is null) return;
        await Autosave.FlushAsync();

        SelectedTemplate = template;
        DefaultTagsInput = string.Join(" ", template.DefaultThemeTags);
        PlaceholderEditorItems.Clear();
        foreach (var def in template.PlaceholderSchema)
            PlaceholderEditorItems.Add(PlaceholderEditRow.From(def));
        IsEditing = true;
        await Autosave.ResetAsync();
    }

    [RelayCommand]
    private void AddPlaceholder()
    {
        PlaceholderEditorItems.Add(new PlaceholderEditRow
        {
            Key         = $"field{PlaceholderEditorItems.Count + 1}",
            DisplayName = "New field",
            IsRequired  = false,
            Type        = PlaceholderType.Text,
        });
    }

    [RelayCommand]
    private void RemovePlaceholder(PlaceholderEditRow? row)
    {
        if (row is not null) PlaceholderEditorItems.Remove(row);
    }

    [RelayCommand]
    private async Task CloseEditorAsync()
    {
        await Autosave.FlushAsync();
        IsEditing = false;
        SelectedTemplate = null;
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
            }
        }
        finally
        {
            Autosave.Start();
        }
    }

    partial void OnSelectedTemplateChanged(PostTemplate? value)
    {
        if (value is null) return;
        DefaultTagsInput = string.Join(" ", value.DefaultThemeTags);
        PlaceholderEditorItems.Clear();
        foreach (var def in value.PlaceholderSchema)
            PlaceholderEditorItems.Add(PlaceholderEditRow.From(def));
        BodyText = value.BodyTemplate ?? string.Empty;
        IsEditing = true;
        _ = Autosave.ResetAsync();
    }

    // ---- Body / autocomplete / auto-sync ------------------------------------

    /// <summary>
    /// Body is edited via <see cref="BodyText"/> (bound to the TextBox), then
    /// pushed back onto the entity so autosave sees the change.
    /// </summary>
    partial void OnBodyTextChanged(string value)
    {
        if (SelectedTemplate is null) return;
        SelectedTemplate.BodyTemplate = value;
        RecomputeAutocompleteQuery();
        AutoAddReferencedPlaceholders();
        RecomputeSuggestions();
    }

    partial void OnBodyCaretChanged(int value)
    {
        _ = value; // used only to trigger the query recompute
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

    /// <summary>Appends a row to the schema for every referenced key that isn't already there.</summary>
    private void AutoAddReferencedPlaceholders()
    {
        if (SelectedTemplate is null) return;
        var existing = PlaceholderEditorItems.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in SelectedTemplate.ExtractPlaceholders())
        {
            if (PostTemplate.IsBuiltInPlaceholder(key)) continue;
            if (!existing.Add(key)) continue;
            PlaceholderEditorItems.Add(new PlaceholderEditRow
            {
                Key         = key,
                DisplayName = ToDisplayName(key),
                IsRequired  = false,
                Type        = PlaceholderType.Text,
            });
        }
    }

    private static string ToDisplayName(string key)
    {
        var spaced = key.Replace('_', ' ').Replace('-', ' ').Trim();
        if (spaced.Length == 0) return key;
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private void RecomputeSuggestions()
    {
        PlaceholderSuggestions.Clear();
        var schemaKeys = PlaceholderEditorItems
            .Select(r => r.Key)
            .Where(k => !string.IsNullOrWhiteSpace(k));

        var all = schemaKeys
            .Concat(PostTemplate.BuiltInPlaceholders)
            .Distinct(StringComparer.Ordinal);

        var q = AutocompleteQuery;
        if (!string.IsNullOrEmpty(q))
        {
            all = all.Where(k => k.StartsWith(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var key in all)
        {
            PlaceholderSuggestions.Add(new PlaceholderSuggestion(
                Key: key,
                IsGlobal: PostTemplate.IsBuiltInPlaceholder(key)));
        }
    }

    /// <summary>
    /// Insert <c>{{ key }}</c> at the caret — or, if we're mid-autocomplete,
    /// replace the partial <c>{{ prefix</c> with a complete token.
    /// </summary>
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
        // Move the caret after the inserted token. The view binds BodyCaret
        // two-way so updating it here repositions the TextBox selection.
        BodyCaret = newCaret;
    }
}

/// <summary>A row in the chip toolbar — one placeholder key, plus whether it's a global.</summary>
public sealed record PlaceholderSuggestion(string Key, bool IsGlobal)
{
    /// <summary>Rendered label, e.g. <c>{{ common_text }}</c>.</summary>
    public string Display => $"{{{{ {Key} }}}}";
}

public partial class PlaceholderEditRow : ObservableObject
{
    [ObservableProperty] private string key = "";
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private bool isRequired;
    [ObservableProperty] private PlaceholderType type = PlaceholderType.Text;
    [ObservableProperty] private string? description;
    [ObservableProperty] private string? defaultValue;

    public static PlaceholderEditRow From(PlaceholderDefinition def) => new()
    {
        Key = def.Key,
        DisplayName = def.DisplayName,
        IsRequired = def.IsRequired,
        Type = def.Type,
        Description = def.Description,
        DefaultValue = def.DefaultValue,
    };

    public PlaceholderDefinition ToDefinition() => new(
        Key: Key,
        DisplayName: DisplayName,
        IsRequired: IsRequired,
        Type: Type,
        Description: Description,
        DefaultValue: DefaultValue);
}
