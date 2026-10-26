using System;
using System.Windows;
using System.Windows.Media;

namespace PrimeDesktop.Services;

public static class ThemeManager
{
    public static void ApplyAccentColor(string hexColor)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            Application.Current.Resources["AccentColor"] = color;
            Application.Current.Resources["AccentBrush"] = new SolidColorBrush(color);

            // Light hover / translucent shade (25% opacity)
            var hoverColor = Color.FromArgb(64, color.R, color.G, color.B);
            Application.Current.Resources["AccentHoverBrush"] = new SolidColorBrush(hoverColor);

            // Soft chip shade (15% opacity)
            var softColor = Color.FromArgb(38, color.R, color.G, color.B);
            Application.Current.Resources["AccentSoftBrush"] = new SolidColorBrush(softColor);

            // Border shade (40% opacity)
            var borderColor = Color.FromArgb(102, color.R, color.G, color.B);
            Application.Current.Resources["AccentBorderBrush"] = new SolidColorBrush(borderColor);
        }
        catch { }
    }
}
