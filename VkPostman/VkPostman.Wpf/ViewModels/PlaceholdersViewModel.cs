using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VkPostman.Core.Models;
using VkPostman.Wpf.Services;

namespace VkPostman.Wpf.ViewModels;

/// <summary>
/// Standalone library view — list every placeholder definition + which templates
/// reference it. Edits here propagate to every template using the key.
/// </summary>
public partial class PlaceholdersViewModel : ObservableObject
{
    private readonly PlaceholderService _svc;

    public ObservableCollection<PlaceholderDefinition> Items { get; } = new();

    /// <summary>
    /// Display-name list of templates using the currently selected key. Recomputed
    /// when the selection changes so the editor shows blast radius for renames /
    /// type changes.
    /// </summary>
    public ObservableCollection<string> UsedByTemplateNames { get; } = new();

    public Array PlaceholderTypes { get; } = Enum.GetValues(typeof(PlaceholderType));

    [ObservableProperty] private PlaceholderDefinition? selectedItem;
    [ObservableProperty] private bool isEditing;

    public Autosave<PlaceholderDefinition> Autosave { get; }

    public PlaceholdersViewModel(PlaceholderService svc)
    {
        _svc = svc;
        Autosave = new Autosave<PlaceholderDefinition>(
            get:  () => SelectedItem is { Id: > 0 } d ? d : null,
            save: SaveCurrentAsync);
        Autosave.Start();

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Items.Clear();
        foreach (var d in await _svc.GetAllAsync())
            Items.Add(d);
    }

    private async Task SaveCurrentAsync(PlaceholderDefinition d)
    {
        await _svc.UpdateAsync(d);
    }

    [RelayCommand]
    private async Task NewPlaceholderAsync()
    {
        await Autosave.FlushAsync();
        var d = new PlaceholderDefinition
        {
            Key         = $"field{Items.Count + 1}",
            DisplayName = "New field",
            Type        = PlaceholderType.Text,
        };
        await _svc.AddAsync(d);
        Items.Add(d);
        SelectedItem = d;
        IsEditing = true;
        await Autosave.ResetAsync();
    }

    [RelayCommand]
    private async Task CloseEditorAsync()
    {
        await Autosave.FlushAsync();
        IsEditing = false;
        SelectedItem = null;
        UsedByTemplateNames.Clear();
    }

    [RelayCommand]
    private async Task DeletePlaceholderAsync(PlaceholderDefinition? d)
    {
        if (d is null) return;
        Autosave.Stop();
        try
        {
            // Check blast radius first; for now we just warn via count, no blocking.
            var uses = await _svc.FindUsagesAsync(d.Key);
            var msg = uses.Count > 0
                ? $"'{d.Key}' is referenced by {uses.Count} template(s): {string.Join(", ", uses.Select(t => t.Name))}. " +
                  "Delete anyway? Those templates will render the placeholder as empty."
                : $"Delete placeholder '{d.Key}'?";
            if (System.Windows.MessageBox.Show(msg, "Confirm delete",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.OK)
                return;

            await _svc.DeleteAsync(d.Id);
            Items.Remove(d);
            if (SelectedItem?.Id == d.Id)
            {
                SelectedItem = null;
                IsEditing = false;
                UsedByTemplateNames.Clear();
            }
        }
        finally
        {
            Autosave.Start();
        }
    }

    partial void OnSelectedItemChanged(PlaceholderDefinition? value)
    {
        if (value is null)
        {
            IsEditing = false;
            UsedByTemplateNames.Clear();
            return;
        }
        IsEditing = true;
        _ = Autosave.ResetAsync();
        _ = RefreshUsagesAsync(value.Key);
    }

    private async Task RefreshUsagesAsync(string key)
    {
        UsedByTemplateNames.Clear();
        var uses = await _svc.FindUsagesAsync(key);
        foreach (var t in uses) UsedByTemplateNames.Add(t.Name);
    }
}
