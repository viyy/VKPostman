using System.Windows.Controls;
using System.Windows.Input;
using VkPostman.Core.Models;
using VkPostman.Wpf.ViewModels;

namespace VkPostman.Wpf.Views;

public partial class GroupsView : UserControl
{
    public GroupsView()
    {
        InitializeComponent();
    }

    /// <summary>Double-click a row to jump directly into the editor.</summary>
    private void GroupDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GroupsViewModel vm &&
            sender is ListBoxItem { DataContext: TargetGroup g })
        {
            vm.EditGroupCommand.Execute(g);
        }
    }
}
