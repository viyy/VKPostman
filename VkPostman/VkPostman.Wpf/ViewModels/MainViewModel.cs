using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VkPostman.Wpf.Services;

namespace VkPostman.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? currentView;

    /// <summary>Banner text shown at the top of the window after an import/export.</summary>
    [ObservableProperty] private string? ioMessage;
    [ObservableProperty] private bool ioMessageIsError;

    private readonly IServiceProvider _sp;

    public MainViewModel(IServiceProvider sp)
    {
        _sp = sp;
        IsDraftsSelected = true;
    }

    [RelayCommand]
    private async Task ExportDataAsync()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export VK Postman data",
            Filter = "JSON|*.json",
            FileName = $"vk-postman-{DateTime.Now:yyyy-MM-dd-HHmmss}.json",
            DefaultExt = ".json",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var scope = _sp.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ExchangeService>();
            var data = await svc.BuildExportAsync();
            await svc.ExportToFileAsync(dlg.FileName);
            Flash($"Exported {data.Placeholders?.Count ?? 0} placeholders, " +
                  $"{data.Templates?.Count ?? 0} templates, " +
                  $"{data.Groups?.Count ?? 0} groups, {data.Drafts?.Count ?? 0} drafts.", isError: false);
        }
        catch (Exception ex)
        {
            Flash($"Export failed: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    private async Task ImportDataAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import VK Postman data",
            Filter = "JSON|*.json",
            DefaultExt = ".json",
        };
        if (dlg.ShowDialog() != true) return;

        var confirm = System.Windows.MessageBox.Show(
            $"Import \"{System.IO.Path.GetFileName(dlg.FileName)}\" will REPLACE all existing " +
            $"templates, groups, and drafts with the file's contents. Continue?",
            "Confirm import",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        try
        {
            using var scope = _sp.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ExchangeService>();
            var summary = await svc.ImportFromFileAsync(dlg.FileName);
            Flash($"Imported {summary.Placeholders} placeholders, {summary.Templates} templates, " +
                  $"{summary.Groups} groups, {summary.Drafts} drafts. Switch tabs to see the new data.", isError: false);

            // Nudge the current view to reload by flipping back to Drafts.
            IsDraftsSelected = false;
            IsDraftsSelected = true;
        }
        catch (Exception ex)
        {
            Flash($"Import failed: {ex.Message}", isError: true);
        }
    }

    private async void Flash(string message, bool isError)
    {
        IoMessage = message;
        IoMessageIsError = isError;
        await Task.Delay(5000);
        if (IoMessage == message) IoMessage = null;
    }

    // Mutually-exclusive sidebar selection — set via RadioButton.IsChecked binding.
    private bool _isDraftsSelected;
    public bool IsDraftsSelected
    {
        get => _isDraftsSelected;
        set
        {
            if (SetProperty(ref _isDraftsSelected, value) && value)
                CurrentView = _sp.GetRequiredService<DraftsViewModel>();
        }
    }

    private bool _isTemplatesSelected;
    public bool IsTemplatesSelected
    {
        get => _isTemplatesSelected;
        set
        {
            if (SetProperty(ref _isTemplatesSelected, value) && value)
                CurrentView = _sp.GetRequiredService<TemplatesViewModel>();
        }
    }

    private bool _isGroupsSelected;
    public bool IsGroupsSelected
    {
        get => _isGroupsSelected;
        set
        {
            if (SetProperty(ref _isGroupsSelected, value) && value)
                CurrentView = _sp.GetRequiredService<GroupsViewModel>();
        }
    }

    private bool _isPlaceholdersSelected;
    public bool IsPlaceholdersSelected
    {
        get => _isPlaceholdersSelected;
        set
        {
            if (SetProperty(ref _isPlaceholdersSelected, value) && value)
                CurrentView = _sp.GetRequiredService<PlaceholdersViewModel>();
        }
    }
}
