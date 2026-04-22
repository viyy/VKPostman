using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace VkPostman.Wpf.ViewModels;

/// <summary>Binding converter: object → Visibility.Visible if non-null, else Collapsed.</summary>
public sealed class NotNullConverter : IValueConverter
{
    public static readonly NotNullConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value is not null;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>bool → Visibility.Visible if true, else Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        (value is true) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>int &gt; 0 → Visible, else Collapsed. Used to hide the placeholder card when there are none.</summary>
public sealed class IntToVisibilityConverter : IValueConverter
{
    public static readonly IntToVisibilityConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        (value is int n && n > 0) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>int == 0 → Visible, else Collapsed. The inverse of <see cref="IntToVisibilityConverter"/>.</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public static readonly ZeroToVisibilityConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        (value is int n && n == 0) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>bool → Visibility.Visible if false, else Collapsed.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        (value is true) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>AutosaveStatus → human label shown next to the Close button.</summary>
public sealed class AutosaveStatusLabelConverter : IValueConverter
{
    public static readonly AutosaveStatusLabelConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is Services.AutosaveStatus s ? s switch
        {
            Services.AutosaveStatus.Dirty  => "…",
            Services.AutosaveStatus.Saving => "Saving…",
            Services.AutosaveStatus.Saved  => "✓ Saved",
            Services.AutosaveStatus.Error  => "⚠ Save failed",
            _                              => "",
        } : "";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Picks the chip-background brush for placeholder suggestions — user-defined
/// keys get the standard surface colour, globals get a muted gray to hint
/// that they come from the draft/group, not the schema.
/// </summary>
public sealed class GlobalChipBackgroundConverter : IValueConverter
{
    public static readonly GlobalChipBackgroundConverter Instance = new();

    private static readonly SolidColorBrush _userBrush   = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush _globalBrush = new(Color.FromRgb(0xF4, 0xF5, 0xF7));

    static GlobalChipBackgroundConverter()
    {
        _userBrush.Freeze();
        _globalBrush.Freeze();
    }

    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        (value is true) ? _globalBrush : _userBrush;

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>
/// Turns the VM's AutocompleteQuery (nullable string) into a one-line hint
/// under the chip toolbar — "Completing {{ foo… }}" when active, empty
/// otherwise so the chip row is the only thing visible.
/// </summary>
public sealed class AutocompleteHintConverter : IValueConverter
{
    public static readonly AutocompleteHintConverter Instance = new();

    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        value is string s && !string.IsNullOrEmpty(s)
            ? $"Completing {{{{ {s}…"
            : string.Empty;

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
