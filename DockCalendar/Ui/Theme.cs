using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DockCalendar.Ui;

/// <summary>Follows the Windows light/dark app mode and accent colour.</summary>
internal static class Theme
{
    public static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Fills the brushes the XAML refers to through DynamicResource.</summary>
    public static void Apply(ResourceDictionary resources, bool dark)
    {
        var accent = Accent(dark);
        Set(resources, "Accent", accent);
        Set(resources, "OnAccent", dark ? Colors.Black : Colors.White);

        if (dark)
        {
            Set(resources, "Bg", Color.FromArgb(0xB4, 0x1C, 0x1C, 0x1C));
            Set(resources, "Fg", Colors.White);
            Set(resources, "FgDim", Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
            Set(resources, "FgFaint", Color.FromArgb(0x78, 0xFF, 0xFF, 0xFF));
            Set(resources, "Stroke", Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            Set(resources, "Hover", Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF));
            Set(resources, "Pressed", Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
            Set(resources, "Card", Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
            Set(resources, "Field", Color.FromArgb(0x33, 0x00, 0x00, 0x00));
        }
        else
        {
            Set(resources, "Bg", Color.FromArgb(0xB4, 0xF3, 0xF3, 0xF3));
            Set(resources, "Fg", Color.FromArgb(0xE4, 0x00, 0x00, 0x00));
            Set(resources, "FgDim", Color.FromArgb(0xA0, 0x00, 0x00, 0x00));
            Set(resources, "FgFaint", Color.FromArgb(0x66, 0x00, 0x00, 0x00));
            Set(resources, "Stroke", Color.FromArgb(0x1C, 0x00, 0x00, 0x00));
            Set(resources, "Hover", Color.FromArgb(0x10, 0x00, 0x00, 0x00));
            Set(resources, "Pressed", Color.FromArgb(0x08, 0x00, 0x00, 0x00));
            Set(resources, "Card", Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            Set(resources, "Field", Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        }
    }

    public static Color Parse(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (Exception)
        {
            return Color.FromRgb(0x03, 0x9B, 0xE5);
        }
    }

    public static SolidColorBrush Brush(Color color, byte alpha = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    // Windows pairs dark surfaces with a lighter accent shade and light ones with a darker shade.
    private static Color Accent(bool dark)
    {
        try
        {
            var settings = new global::Windows.UI.ViewManagement.UISettings();
            var value = settings.GetColorValue(dark
                ? global::Windows.UI.ViewManagement.UIColorType.AccentLight2
                : global::Windows.UI.ViewManagement.UIColorType.AccentDark1);
            return Color.FromRgb(value.R, value.G, value.B);
        }
        catch (Exception)
        {
            return dark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
        }
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }
}
