using System;
using System.ComponentModel;
using DevTools.Uno.Diagnostics.Internal;
using DevTools.Uno.Diagnostics.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace DevTools.Uno.Diagnostics.Views;

public sealed class PropertyValueCellPresenter : UserControl
{
    public static readonly DependencyProperty PropertyProperty = DependencyProperty.Register(
        nameof(Property),
        typeof(object),
        typeof(PropertyValueCellPresenter),
        new PropertyMetadata(null, OnPropertyChanged));

    private readonly Border _chrome;
    private readonly ContentControl _previewHost;
    private readonly TextBlock _valueText;
    private readonly TextBlock _badgeText;

    private PropertyGridNode? _subscribedProperty;

    public PropertyValueCellPresenter()
    {
        _previewHost = new ContentControl
        {
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _valueText = new TextBlock
        {
            Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsForegroundBrush", PropertyEditorUtilities.ForegroundFallbackColor),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };

        _badgeText = new TextBlock
        {
            FontSize = 11,
            Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsForegroundCaptionBrush", PropertyEditorUtilities.CaptionForegroundFallbackColor),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };

        var layout = new Grid
        {
            ColumnSpacing = 8,
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        layout.Children.Add(_previewHost);
        layout.Children.Add(_valueText);
        layout.Children.Add(_badgeText);
        Grid.SetColumn(_valueText, 1);
        Grid.SetColumn(_badgeText, 2);

        _chrome = new Border
        {
            Background = PropertyEditorUtilities.ResolveBrush("DevToolsSurfaceSubtleBrush", PropertyEditorUtilities.SurfaceFallbackColor),
            BorderBrush = PropertyEditorUtilities.ResolveBrush("DevToolsBorderBrush", PropertyEditorUtilities.BorderFallbackColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Child = layout,
        };

        Content = _chrome;
        Refresh();
    }

    public object? Property
    {
        get => GetValue(PropertyProperty);
        set => SetValue(PropertyProperty, value);
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PropertyValueCellPresenter)d).OnPropertyChanged(e.NewValue as PropertyGridNode);

    private void OnPropertyChanged(PropertyGridNode? newValue)
    {
        if (_subscribedProperty is not null)
        {
            _subscribedProperty.PropertyChanged -= OnPropertyNodeChanged;
        }

        _subscribedProperty = newValue;

        if (_subscribedProperty is not null)
        {
            _subscribedProperty.PropertyChanged += OnPropertyNodeChanged;
        }

        Refresh();
    }

    private void OnPropertyNodeChanged(object? sender, PropertyChangedEventArgs e)
        => Refresh();

    private void Refresh()
    {
        var property = Property as PropertyGridNode;
        if (property is null || property.IsGroup)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;

        var hasError = property.HasValidationError;
        _chrome.BorderBrush = hasError
            ? PropertyEditorUtilities.ResolveBrush("DevToolsValidationErrorBrush", PropertyEditorUtilities.ErrorFallbackColor)
            : PropertyEditorUtilities.ResolveBrush("DevToolsBorderBrush", PropertyEditorUtilities.BorderFallbackColor);
        _chrome.Background = hasError
            ? PropertyEditorUtilities.ResolveBrush("DevToolsValidationErrorSurfaceBrush", PropertyEditorUtilities.ErrorSurfaceFallbackColor)
            : PropertyEditorUtilities.ResolveBrush("DevToolsSurfaceSubtleBrush", PropertyEditorUtilities.SurfaceFallbackColor);

        _valueText.Text = property.ValueText;
        _valueText.Foreground = PropertyEditorUtilities.IsNullLikeText(property.ValueText)
            ? PropertyEditorUtilities.ResolveBrush("DevToolsForegroundSubtleBrush", PropertyEditorUtilities.SubtleForegroundFallbackColor)
            : PropertyEditorUtilities.ResolveBrush("DevToolsForegroundBrush", PropertyEditorUtilities.ForegroundFallbackColor);

        if (hasError)
        {
            _badgeText.Text = "Invalid";
            _badgeText.Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsValidationErrorBrush", PropertyEditorUtilities.ErrorFallbackColor);
            _badgeText.Visibility = Visibility.Visible;
        }
        else if (!property.IsEditable || property.EditorKind == PropertyEditorKind.ReadOnly)
        {
            _badgeText.Text = "Read only";
            _badgeText.Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsForegroundCaptionBrush", PropertyEditorUtilities.CaptionForegroundFallbackColor);
            _badgeText.Visibility = Visibility.Visible;
        }
        else
        {
            _badgeText.Visibility = Visibility.Collapsed;
        }

        _previewHost.Content = CreatePreview(property);
        _previewHost.Visibility = _previewHost.Content is null ? Visibility.Collapsed : Visibility.Visible;
        PropertyEditorUtilities.ApplyToolTip(_chrome, property.ValidationError);
    }

    private static FrameworkElement? CreatePreview(PropertyGridNode property)
    {
        var rawValue = property.GetValue();
        return property.EditorKind switch
        {
            PropertyEditorKind.Boolean => new CheckBox
            {
                IsChecked = rawValue is bool value ? value : null,
                IsThreeState = property.SupportsThreeState,
                IsEnabled = false,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
            },
            PropertyEditorKind.Color or PropertyEditorKind.Brush when PropertyEditorUtilities.CreatePreviewBrush(rawValue) is { } brush => new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(4),
                BorderBrush = PropertyEditorUtilities.ResolveBrush("DevToolsBorderBrush", PropertyEditorUtilities.BorderFallbackColor),
                BorderThickness = new Thickness(1),
                Background = brush,
                VerticalAlignment = VerticalAlignment.Center,
            },
            _ => null,
        };
    }
}
