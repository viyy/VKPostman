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

    // Bound to the PlaceholderSchema list in the editor; ObservableCollection for live add/remove.
    // We use PlaceholderEditRow because PlaceholderDefinition is a record with init-only
    // setters that WPF two-way binding can't mutate in place.
    public ObservableCollection<PlaceholderEditRow> PlaceholderEditorItems { get; } = new();

    public TemplatesViewModel(TemplateService templates)
    {
        _templates = templates;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Templates.Clear();
        foreach (var t in await _templates.GetAllAsync())
            Templates.Add(t);
    }

    [RelayCommand]
    private void NewTemplate()
    {
        SelectedTemplate = new PostTemplate
        {
            Name = "New template",
            BodyTemplate = "{{ common_text }}\n\n{{ group_tags }} {{ theme_tags }}",
            PlaceholderSchema = new List<PlaceholderDefinition>(),
            DefaultThemeTags = new List<string>(),
        };
        DefaultTagsInput = "";
        PlaceholderEditorItems.Clear();
        IsEditing = true;
    }

    [RelayCommand]
    private void EditTemplate(PostTemplate? template)
    {
        if (template is null) return;
        SelectedTemplate = template;
        DefaultTagsInput = string.Join(" ", template.DefaultThemeTags);
        PlaceholderEditorItems.Clear();
        foreach (var def in template.PlaceholderSchema)
            PlaceholderEditorItems.Add(PlaceholderEditRow.From(def));
        IsEditing = true;
    }

    [RelayCommand]
    private void AddPlaceholder()
    {
        PlaceholderEditorItems.Add(new PlaceholderEditRow
        {
            Key = $"field{PlaceholderEditorItems.Count + 1}",
            DisplayName = "New field",
            IsRequired = false,
            Type = PlaceholderType.Text,
        });
    }

    [RelayCommand]
    private void RemovePlaceholder(PlaceholderEditRow? row)
    {
        if (row is not null) PlaceholderEditorItems.Remove(row);
    }

    [RelayCommand]
    private async Task SaveTemplateAsync()
    {
        if (SelectedTemplate is null) return;

        SelectedTemplate.DefaultThemeTags = DefaultTagsInput
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.TrimStart('#'))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        SelectedTemplate.PlaceholderSchema = PlaceholderEditorItems
            .Select(r => r.ToDefinition())
            .ToList();

        if (SelectedTemplate.Id == 0)
            await _templates.AddAsync(SelectedTemplate);
        else
            await _templates.UpdateAsync(SelectedTemplate);

        IsEditing = false;
        await LoadAsync();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        SelectedTemplate = null;
    }

    [RelayCommand]
    private async Task DeleteTemplateAsync(PostTemplate? template)
    {
        if (template is null) return;
        await _templates.DeleteAsync(template.Id);
        await LoadAsync();
    }
}

/// <summary>Mutable editor-side wrapper for <see cref="PlaceholderDefinition"/>.</summary>
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
