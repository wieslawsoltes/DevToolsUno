using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DevTools.Uno.Diagnostics.Internal;

internal static class PropertyEditorUtilities
{
    public static Color PanelFallbackColor => Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    public static Color SurfaceFallbackColor => Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF6);

    public static Color BorderFallbackColor => Color.FromArgb(0xFF, 0xD1, 0xD5, 0xDB);

    public static Color ForegroundFallbackColor => Color.FromArgb(0xFF, 0x11, 0x18, 0x27);

    public static Color SubtleForegroundFallbackColor => Color.FromArgb(0xFF, 0x6B, 0x72, 0x80);

    public static Color CaptionForegroundFallbackColor => Color.FromArgb(0xFF, 0x9C, 0xA3, 0xAF);

    public static Color ErrorFallbackColor => Color.FromArgb(0xFF, 0xDC, 0x26, 0x26);

    public static Color ErrorSurfaceFallbackColor => Color.FromArgb(0xFF, 0xFE, 0xF2, 0xF2);

    public static Color TransparentFallbackColor => Color.FromArgb(0x00, 0x00, 0x00, 0x00);

    public static Brush ResolveBrush(string key, Color fallback)
        => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    public static object? TryFindResource(string key)
    {
        if (Application.Current?.Resources is not { } resources)
        {
            return null;
        }

        return TryFindResource(resources, key);
    }

    public static void ApplyToolTip(FrameworkElement target, string? text)
        => ToolTipService.SetToolTip(target, string.IsNullOrWhiteSpace(text) ? null : text);

    public static string FormatEditableText(object? value, string fallback)
    {
        if (ReferenceEquals(value, DependencyProperty.UnsetValue) || value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            string text => text,
            Color color => FormatColor(color),
            SolidColorBrush solidColorBrush => FormatColor(solidColorBrush.Color),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? fallback,
        };
    }

    public static string FormatDouble(double value)
        => value.ToString("G", CultureInfo.InvariantCulture);

    public static bool TryParseDouble(string? text, out double value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0d;
            return true;
        }

        return double.TryParse(
            text,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value);
    }

    public static string FormatColor(Color color)
        => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Brush? CreatePreviewBrush(object? value)
        => value switch
        {
            Brush brush => brush,
            Color color => new SolidColorBrush(color),
            _ => null,
        };

    public static bool TryParseColor(string? text, out Color color)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed[0] != '#')
        {
            color = default;
            return false;
        }

        return TryParseHexColor(trimmed, out color);
    }

    public static bool IsNullLikeText(string? text)
        => string.IsNullOrWhiteSpace(text) ||
           string.Equals(text, "(null)", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(text, "(unset)", StringComparison.OrdinalIgnoreCase);

    public static TextBox CreateEditorTextBox()
        => new()
        {
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(TransparentFallbackColor),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
        };

    private static object? TryFindResource(ResourceDictionary dictionary, string key)
    {
        if (dictionary.ContainsKey(key))
        {
            return dictionary[key];
        }

        for (var index = dictionary.MergedDictionaries.Count - 1; index >= 0; index--)
        {
            if (TryFindResource(dictionary.MergedDictionaries[index], key) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryParseHexColor(string text, out Color color)
    {
        var digits = text[1..];
        switch (digits.Length)
        {
            case 3:
                if (TryExpandHex(digits[0], out var red3) &&
                    TryExpandHex(digits[1], out var green3) &&
                    TryExpandHex(digits[2], out var blue3))
                {
                    color = Color.FromArgb(0xFF, red3, green3, blue3);
                    return true;
                }

                break;
            case 4:
                if (TryExpandHex(digits[0], out var alpha4) &&
                    TryExpandHex(digits[1], out var red4) &&
                    TryExpandHex(digits[2], out var green4) &&
                    TryExpandHex(digits[3], out var blue4))
                {
                    color = Color.FromArgb(alpha4, red4, green4, blue4);
                    return true;
                }

                break;
            case 6:
                if (byte.TryParse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red6) &&
                    byte.TryParse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green6) &&
                    byte.TryParse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue6))
                {
                    color = Color.FromArgb(0xFF, red6, green6, blue6);
                    return true;
                }

                break;
            case 8:
                if (byte.TryParse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var alpha8) &&
                    byte.TryParse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red8) &&
                    byte.TryParse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green8) &&
                    byte.TryParse(digits.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue8))
                {
                    color = Color.FromArgb(alpha8, red8, green8, blue8);
                    return true;
                }

                break;
        }

        color = default;
        return false;
    }

    private static bool TryExpandHex(char value, out byte expanded)
        => byte.TryParse($"{value}{value}", NumberStyles.HexNumber, CultureInfo.InvariantCulture, out expanded);
}
