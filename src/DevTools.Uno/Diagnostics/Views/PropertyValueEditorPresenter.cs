using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using DevTools.Uno.Diagnostics.Internal;
using DevTools.Uno.Diagnostics.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace DevTools.Uno.Diagnostics.Views;

public sealed class PropertyValueEditorPresenter : UserControl
{
    public static readonly DependencyProperty PropertyProperty = DependencyProperty.Register(
        nameof(Property),
        typeof(object),
        typeof(PropertyValueEditorPresenter),
        new PropertyMetadata(null, OnPropertyChanged));

    public static readonly DependencyProperty ShowDetailsChromeProperty = DependencyProperty.Register(
        nameof(ShowDetailsChrome),
        typeof(bool),
        typeof(PropertyValueEditorPresenter),
        new PropertyMetadata(false, OnPresenterOptionsChanged));

    public static readonly DependencyProperty ShowValidationMessageProperty = DependencyProperty.Register(
        nameof(ShowValidationMessage),
        typeof(bool),
        typeof(PropertyValueEditorPresenter),
        new PropertyMetadata(false, OnPresenterOptionsChanged));

    private readonly StackPanel _root;
    private readonly StackPanel _headerPanel;
    private readonly TextBlock _titleText;
    private readonly TextBlock _metaText;
    private readonly TextBlock _hintText;
    private readonly ContentControl _editorHost;
    private readonly TextBlock _errorText;

    private PropertyGridNode? _subscribedProperty;
    private PropertyEditorControlBase? _editorControl;
    private PropertyEditorKind _editorKind;

    public PropertyValueEditorPresenter()
    {
        _titleText = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsForegroundBrush", PropertyEditorUtilities.ForegroundFallbackColor),
            TextWrapping = TextWrapping.WrapWholeWords,
        };

        _metaText = new TextBlock
        {
            FontSize = 12,
            Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsForegroundSubtleBrush", PropertyEditorUtilities.SubtleForegroundFallbackColor),
            TextWrapping = TextWrapping.WrapWholeWords,
        };

        _hintText = new TextBlock
        {
            FontSize = 12,
            Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsForegroundCaptionBrush", PropertyEditorUtilities.CaptionForegroundFallbackColor),
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed,
        };

        _headerPanel = new StackPanel
        {
            Spacing = 2,
            Visibility = Visibility.Collapsed,
        };
        _headerPanel.Children.Add(_titleText);
        _headerPanel.Children.Add(_metaText);
        _headerPanel.Children.Add(_hintText);

        _editorHost = new ContentControl();

        _errorText = new TextBlock
        {
            FontSize = 12,
            Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsValidationErrorBrush", PropertyEditorUtilities.ErrorFallbackColor),
            TextWrapping = TextWrapping.WrapWholeWords,
            Visibility = Visibility.Collapsed,
        };

        _root = new StackPanel
        {
            Spacing = 6,
        };
        _root.Children.Add(_headerPanel);
        _root.Children.Add(_editorHost);
        _root.Children.Add(_errorText);

        Content = _root;
        Refresh();
    }

    public object? Property
    {
        get => GetValue(PropertyProperty);
        set => SetValue(PropertyProperty, value);
    }

    public bool ShowDetailsChrome
    {
        get => (bool)GetValue(ShowDetailsChromeProperty);
        set => SetValue(ShowDetailsChromeProperty, value);
    }

    public bool ShowValidationMessage
    {
        get => (bool)GetValue(ShowValidationMessageProperty);
        set => SetValue(ShowValidationMessageProperty, value);
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PropertyValueEditorPresenter)d).OnPropertyChanged(e.NewValue as PropertyGridNode);

    private static void OnPresenterOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PropertyValueEditorPresenter)d).Refresh();

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

        _headerPanel.Visibility = ShowDetailsChrome ? Visibility.Visible : Visibility.Collapsed;

        if (ShowDetailsChrome)
        {
            RefreshHeader(property);
        }

        if (property is null)
        {
            if (_editorControl is not null)
            {
                _editorControl.Property = null;
            }

            _editorHost.Content = ShowDetailsChrome
                ? CreatePlaceholder("Select a property in the inspector to edit it.")
                : null;
            RefreshError(property);
            return;
        }

        if (property.IsGroup)
        {
            if (_editorControl is not null)
            {
                _editorControl.Property = null;
            }

            _editorHost.Content = CreatePlaceholder("Property groups organize child values and are not edited directly.");
            RefreshError(property);
            return;
        }

        var desiredKind = !property.IsEditable || property.EditorKind == PropertyEditorKind.ReadOnly
            ? PropertyEditorKind.ReadOnly
            : property.EditorKind;

        if (_editorControl is null || _editorKind != desiredKind)
        {
            if (_editorControl is not null)
            {
                _editorControl.Property = null;
            }
            _editorControl = PropertyEditorControlFactory.Create(property);
            _editorKind = desiredKind;
        }

        _editorControl.Property = property;
        _editorHost.Content = _editorControl;
        RefreshError(property);
    }

    private void RefreshHeader(PropertyGridNode? property)
    {
        if (property is null)
        {
            _titleText.Text = "No property selected";
            _metaText.Text = "Choose a property from the grid to inspect and edit.";
            _hintText.Visibility = Visibility.Collapsed;
            return;
        }

        if (property.IsGroup)
        {
            _titleText.Text = property.Name;
            _metaText.Text = "Property group";
            _hintText.Text = "Expand the group and select an individual property to edit its value.";
            _hintText.Visibility = Visibility.Visible;
            return;
        }

        _titleText.Text = property.Name;
        _metaText.Text = BuildMetaText(property);

        if (!string.IsNullOrWhiteSpace(property.PlaceholderText))
        {
            _hintText.Text = $"Expected format: {property.PlaceholderText}";
            _hintText.Visibility = Visibility.Visible;
        }
        else
        {
            _hintText.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshError(PropertyGridNode? property)
    {
        if (ShowValidationMessage && property?.HasValidationError == true)
        {
            _errorText.Text = property.ValidationError;
            _errorText.Visibility = Visibility.Visible;
            PropertyEditorUtilities.ApplyToolTip(_errorText, property.ValidationError);
            return;
        }

        _errorText.Visibility = Visibility.Collapsed;
        PropertyEditorUtilities.ApplyToolTip(_errorText, null);
    }

    private static string BuildMetaText(PropertyGridNode property)
    {
        var parts = new List<string>
        {
            property.TypeText,
        };

        if (!string.IsNullOrWhiteSpace(property.SourceText))
        {
            parts.Add(property.SourceText);
        }

        if (!string.IsNullOrWhiteSpace(property.PriorityText))
        {
            parts.Add(property.PriorityText);
        }

        if (property.IsAttachedProperty)
        {
            parts.Add("Attached");
        }

        if (!property.IsEditable || property.EditorKind == PropertyEditorKind.ReadOnly)
        {
            parts.Add("Read only");
        }

        return string.Join(" • ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal));
    }

    private static Border CreatePlaceholder(string text)
        => new()
        {
            Background = PropertyEditorUtilities.ResolveBrush("DevToolsSurfaceSubtleBrush", PropertyEditorUtilities.SurfaceFallbackColor),
            BorderBrush = PropertyEditorUtilities.ResolveBrush("DevToolsBorderBrush", PropertyEditorUtilities.BorderFallbackColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Child = new TextBlock
            {
                Text = text,
                Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsForegroundSubtleBrush", PropertyEditorUtilities.SubtleForegroundFallbackColor),
                TextWrapping = TextWrapping.WrapWholeWords,
            },
        };
}
