using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using VkPostman.Core.Models;
using VkPostman.Data;
using VkPostman.Wpf.Services;

namespace VkPostman.Wpf.ViewModels;

public partial class GroupsViewModel : ObservableObject
{
    private readonly GroupService _groups;
    private readonly VkPostmanDbContext _db;

    public ObservableCollection<TargetGroup> Groups { get; } = new();
    public ObservableCollection<PostTemplate> Templates { get; } = new();

    [ObservableProperty] private TargetGroup? selectedGroup;
    [ObservableProperty] private bool isEditing;

    // Edit-form fields — bound directly to SelectedGroup where possible, but tags need a string buffer.
    [ObservableProperty] private string editMandatoryTagsInput = "";

    public GroupsViewModel(GroupService groups, VkPostmanDbContext db)
    {
        _groups = groups;
        _db = db;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Groups.Clear();
        foreach (var g in await _groups.GetAllAsync())
            Groups.Add(g);

        Templates.Clear();
        foreach (var t in await _db.PostTemplates.OrderBy(x => x.Name).ToListAsync())
            Templates.Add(t);
    }

    [RelayCommand]
    private void NewGroup()
    {
        SelectedGroup = new TargetGroup
        {
            DisplayName = "New group",
            ScreenName = "",
            IsActive = true,
        };
        EditMandatoryTagsInput = "";
        IsEditing = true;
    }

    [RelayCommand]
    private void EditGroup(TargetGroup? group)
    {
        if (group is null) return;
        SelectedGroup = group;
        EditMandatoryTagsInput = string.Join(" ", group.MandatoryTags);
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveGroupAsync()
    {
        if (SelectedGroup is null) return;

        SelectedGroup.MandatoryTags = EditMandatoryTagsInput
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.TrimStart('#'))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (SelectedGroup.Id == 0)
        {
            await _groups.AddAsync(SelectedGroup);
        }
        else
        {
            await _groups.UpdateAsync(SelectedGroup);
        }

        IsEditing = false;
        await LoadAsync();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        SelectedGroup = null;
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(TargetGroup? group)
    {
        if (group is null) return;
        await _groups.DeleteAsync(group.Id);
        await LoadAsync();
    }
}
