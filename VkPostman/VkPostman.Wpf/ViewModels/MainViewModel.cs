using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace VkPostman.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object? currentView;

    private readonly IServiceProvider _sp;

    public MainViewModel(IServiceProvider sp)
    {
        _sp = sp;
        // Drafts is the default landing page.
        IsDraftsSelected = true;
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
}
