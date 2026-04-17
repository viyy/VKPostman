using System.Windows.Controls;
using System.Windows.Input;
using VkPostman.Core.Models;
using VkPostman.Wpf.ViewModels;

namespace VkPostman.Wpf.Views;

public partial class TemplatesView : UserControl
{
    public TemplatesView()
    {
        InitializeComponent();
    }

    private void TemplateDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TemplatesViewModel vm &&
            sender is ListBoxItem { DataContext: PostTemplate t })
        {
            vm.EditTemplateCommand.Execute(t);
        }
    }
}
