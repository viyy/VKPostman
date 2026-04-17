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

    /// <summary>The MandatoryTags edit buffer — split into the list on every autosave tick.</summary>
    [ObservableProperty] private string editMandatoryTagsInput = "";

    public Autosave<TargetGroup> Autosave { get; }

    public GroupsViewModel(GroupService groups, VkPostmanDbContext db)
    {
        _groups = groups;
        _db = db;

        Autosave = new Autosave<TargetGroup>(
            get:  () => SelectedGroup is { Id: > 0 } g ? g : null,
            save: SaveCurrentAsync);
        Autosave.Start();

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

    /// <summary>Called by the Autosave service — syncs the tag input into the entity and persists.</summary>
    private async Task SaveCurrentAsync(TargetGroup g)
    {
        g.MandatoryTags = ParseTags(EditMandatoryTagsInput);
        await _groups.UpdateAsync(g);
    }

    private static List<string> ParseTags(string input) =>
        input.Split(' ', StringSplitOptions.RemoveEmptyEntries)
             .Select(t => t.TrimStart('#'))
             .Where(t => !string.IsNullOrWhiteSpace(t))
             .ToList();

    [RelayCommand]
    private async Task NewGroupAsync()
    {
        await Autosave.FlushAsync();

        // Simpler path than the PWA flow: create an in-memory draft and let
        // the first autosave tick persist it once the user types anything.
        // On add we write immediately so the list shows the new row now.
        var g = new TargetGroup
        {
            DisplayName = "New group",
            ScreenName = "",
            IsActive = true,
        };
        Groups.Add(g);
        await _groups.AddAsync(g);
        SelectedGroup = g;
        EditMandatoryTagsInput = "";
        IsEditing = true;
        await Autosave.ResetAsync();
    }

    [RelayCommand]
    private async Task EditGroupAsync(TargetGroup? group)
    {
        if (group is null) return;
        await Autosave.FlushAsync();
        SelectedGroup = group;
        EditMandatoryTagsInput = string.Join(" ", group.MandatoryTags);
        IsEditing = true;
        await Autosave.ResetAsync();
    }

    [RelayCommand]
    private async Task CloseEditorAsync()
    {
        await Autosave.FlushAsync();
        IsEditing = false;
        SelectedGroup = null;
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(TargetGroup? group)
    {
        if (group is null) return;
        Autosave.Stop();
        try
        {
            await _groups.DeleteAsync(group.Id);
            Groups.Remove(group);
            if (SelectedGroup?.Id == group.Id)
            {
                SelectedGroup = null;
                IsEditing = false;
            }
        }
        finally
        {
            Autosave.Start();
        }
    }

    // Reset autosave baseline whenever a different group is picked directly
    // (rather than through the EditGroupCommand — e.g. keyboard navigation).
    partial void OnSelectedGroupChanged(TargetGroup? value)
    {
        if (value is null) return;
        EditMandatoryTagsInput = string.Join(" ", value.MandatoryTags);
        IsEditing = true;
        _ = Autosave.ResetAsync();
    }
}
