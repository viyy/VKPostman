using System.ComponentModel;
using System.Windows;
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
        DataContextChanged += OnDataContextChanged;
    }

    private TemplatesViewModel? _vm;

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= VmOnPropertyChanged;
        _vm = DataContext as TemplatesViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += VmOnPropertyChanged;
    }

    /// <summary>
    /// InsertPlaceholder updates <c>BodyText</c> and <c>BodyCaret</c> on the VM,
    /// but the TextBox's own caret position won't follow a pure binding (there
    /// is no public DP for CaretIndex). So we listen for BodyCaret changes and
    /// drive the caret manually.
    /// </summary>
    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TemplatesViewModel.BodyCaret)) return;
        if (_vm is null) return;
        var target = _vm.BodyCaret;
        if (BodyTextBox.CaretIndex != target)
        {
            BodyTextBox.Focus();
            BodyTextBox.CaretIndex = Math.Clamp(target, 0, BodyTextBox.Text.Length);
        }
    }

    /// <summary>Push the TextBox's caret into the VM every time it moves.</summary>
    private void BodyTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.BodyCaret = BodyTextBox.CaretIndex;
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
