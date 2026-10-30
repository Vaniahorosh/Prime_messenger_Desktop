using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PrimeDesktop.Models;

namespace PrimeDesktop.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool flag && flag;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility vis && vis == Visibility.Visible;
    }
}

public class HexToBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.BrushConverter _converter = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return (System.Windows.Media.Brush)_converter.ConvertFromString(hex)!;
            }
            catch { }
        }
        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class ConnectionStateColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DeviceConnectionState state)
        {
            return state switch
            {
                DeviceConnectionState.Connected => "#2EA043", // Green
                DeviceConnectionState.Connecting => "#D29922", // Yellow/Amber
                _ => "#6E7681" // Gray
            };
        }
        return "#6E7681";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class MessageAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isFromMe = value is bool b && b;
        return isFromMe ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class MessageBubbleBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isFromMe = value is bool b && b;
        return Application.Current.Resources[isFromMe ? "SentBubbleBrush" : "ReceivedBubbleBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value == null;
        if (Invert) isNull = !isNull;
        return isNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class EqualityToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool matches = false;
        if (value != null && parameter != null)
        {
            matches = value.ToString() == parameter.ToString();
        }
        if (Invert) matches = !matches;
        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class NonLockingImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes.Length > 0)
                    {
                        using var ms = new MemoryStream(bytes);
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = ms;
                        image.EndInit();
                        image.Freeze();
                        return image;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class InitialsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string name && !string.IsNullOrWhiteSpace(name))
        {
            var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0)
            {
                return (char.ToUpper(parts[0][0]).ToString() + char.ToUpper(parts[1][0]).ToString());
            }
            if (parts.Length > 0 && parts[0].Length > 0)
            {
                return char.ToUpper(parts[0][0]).ToString();
            }
        }
        return "П";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}


public class ReactionSelectedBackgroundConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush SelectedBrush = new(System.Windows.Media.Color.FromArgb(255, 30, 58, 95));
    private static readonly System.Windows.Media.SolidColorBrush NormalBrush = new(System.Windows.Media.Color.FromArgb(255, 23, 33, 48));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? SelectedBrush : NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class ReactionSelectedBorderConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush SelectedBrush = new(System.Windows.Media.Color.FromArgb(255, 56, 139, 253));
    private static readonly System.Windows.Media.SolidColorBrush NormalBrush = new(System.Windows.Media.Color.FromArgb(255, 38, 54, 76));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? SelectedBrush : NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class ReactionCountBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush SelectedBrush = new(System.Windows.Media.Color.FromArgb(255, 88, 166, 255));
    private static readonly System.Windows.Media.SolidColorBrush NormalBrush = new(System.Windows.Media.Color.FromArgb(255, 148, 163, 184));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? SelectedBrush : NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
