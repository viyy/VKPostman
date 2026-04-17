using System.Collections.ObjectModel;
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

    public Autosave<PostTemplate> Autosave { get; }

    public TemplatesViewModel(TemplateService templates)
    {
        _templates = templates;

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
        IsEditing = true;
        _ = Autosave.ResetAsync();
    }
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
