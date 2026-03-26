using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using DevTools.Uno.Diagnostics.Internal;
using DevTools.Uno.Diagnostics.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace DevTools.Uno.Diagnostics.Views;

internal abstract class PropertyEditorControlBase : UserControl
{
    private readonly Border _chromeBorder;
    private PropertyGridNode? _property;
    private bool _isSynchronizing;

    protected PropertyEditorControlBase()
    {
        _chromeBorder = new Border
        {
            Background = PropertyEditorUtilities.ResolveBrush("DevToolsPanelSurfaceBrush", PropertyEditorUtilities.PanelFallbackColor),
            BorderBrush = PropertyEditorUtilities.ResolveBrush("DevToolsBorderBrush", PropertyEditorUtilities.BorderFallbackColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        Content = _chromeBorder;
    }

    public PropertyGridNode? Property
    {
        get => _property;
        set
        {
            if (ReferenceEquals(_property, value))
            {
                SyncFromProperty();
                UpdateValidationChrome();
                return;
            }

            if (_property is not null)
            {
                _property.PropertyChanged -= OnPropertyChanged;
            }

            _property = value;

            if (_property is not null)
            {
                _property.PropertyChanged += OnPropertyChanged;
            }

            OnPropertyAssigned();
            SyncFromProperty();
            UpdateValidationChrome();
        }
    }

    protected bool IsSynchronizing => _isSynchronizing;

    protected void SetEditorContent(UIElement content)
        => _chromeBorder.Child = content;

    protected void SetChromePadding(Thickness padding)
        => _chromeBorder.Padding = padding;

    protected PropertyEditorCommitResult Commit(object? value)
    {
        var result = Property?.CommitValue(value) ?? PropertyEditorCommitResult.Failed("No property is selected.");
        UpdateValidationChrome();
        return result;
    }

    protected void RunSynchronizing(Action update)
    {
        _isSynchronizing = true;
        try
        {
            update();
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    protected virtual void OnPropertyAssigned()
    {
    }

    protected abstract void SyncFromProperty();

    protected virtual void UpdateValidationChrome()
    {
        var hasError = Property?.HasValidationError == true;
        _chromeBorder.BorderBrush = hasError
            ? PropertyEditorUtilities.ResolveBrush("DevToolsValidationErrorBrush", PropertyEditorUtilities.ErrorFallbackColor)
            : PropertyEditorUtilities.ResolveBrush("DevToolsBorderBrush", PropertyEditorUtilities.BorderFallbackColor);
        _chromeBorder.Background = hasError
            ? PropertyEditorUtilities.ResolveBrush("DevToolsValidationErrorSurfaceBrush", PropertyEditorUtilities.ErrorSurfaceFallbackColor)
            : PropertyEditorUtilities.ResolveBrush("DevToolsPanelSurfaceBrush", PropertyEditorUtilities.PanelFallbackColor);
        PropertyEditorUtilities.ApplyToolTip(_chromeBorder, Property?.ValidationError);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PropertyGridNode.ValidationError) or nameof(PropertyGridNode.HasValidationError))
        {
            UpdateValidationChrome();
            return;
        }

        SyncFromProperty();
        UpdateValidationChrome();
    }
}

internal sealed class ReadOnlyPropertyEditorControl : PropertyEditorControlBase
{
    private readonly TextBlock _textBlock;

    public ReadOnlyPropertyEditorControl()
    {
        _textBlock = new TextBlock
        {
            Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsForegroundMutedBrush", PropertyEditorUtilities.SubtleForegroundFallbackColor),
            TextWrapping = TextWrapping.WrapWholeWords,
        };

        SetEditorContent(_textBlock);
    }

    protected override void SyncFromProperty()
        => _textBlock.Text = Property?.ValueText ?? string.Empty;
}

internal sealed class TextPropertyEditorControl : PropertyEditorControlBase
{
    private readonly TextBox _textBox;

    public TextPropertyEditorControl()
    {
        _textBox = PropertyEditorUtilities.CreateEditorTextBox();
        _textBox.KeyDown += OnKeyDown;
        _textBox.LostFocus += OnLostFocus;
        SetEditorContent(_textBox);
    }

    protected override void OnPropertyAssigned()
    {
        _textBox.PlaceholderText = Property?.PlaceholderText ?? string.Empty;
        _textBox.InputScope = Property?.EditorKind == PropertyEditorKind.Numeric ? CreateNumericInputScope() : null;
    }

    protected override void SyncFromProperty()
    {
        var property = Property;
        RunSynchronizing(() =>
        {
            _textBox.PlaceholderText = property?.PlaceholderText ?? string.Empty;
            _textBox.InputScope = property?.EditorKind == PropertyEditorKind.Numeric ? CreateNumericInputScope() : null;
            _textBox.Text = PropertyEditorUtilities.FormatEditableText(property?.GetValue(), property?.ValueText ?? string.Empty);
        });
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitFromText();
            e.Handled = true;
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
        => CommitFromText();

    private void CommitFromText()
    {
        if (IsSynchronizing)
        {
            return;
        }

        Commit(_textBox.Text);
    }

    private static InputScope CreateNumericInputScope()
    {
        var scope = new InputScope();
        scope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
        return scope;
    }
}

internal sealed class BooleanPropertyEditorControl : PropertyEditorControlBase
{
    private readonly CheckBox _checkBox;
    private readonly TextBlock _stateText;

    public BooleanPropertyEditorControl()
    {
        SetChromePadding(new Thickness(8, 4, 8, 4));

        _checkBox = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        _checkBox.Checked += OnStateChanged;
        _checkBox.Unchecked += OnStateChanged;
        _checkBox.Indeterminate += OnStateChanged;

        _stateText = new TextBlock
        {
            Foreground = PropertyEditorUtilities.ResolveBrush("DevToolsForegroundMutedBrush", PropertyEditorUtilities.SubtleForegroundFallbackColor),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        panel.Children.Add(_checkBox);
        panel.Children.Add(_stateText);

        SetEditorContent(panel);
    }

    protected override void OnPropertyAssigned()
        => _checkBox.IsThreeState = Property?.SupportsThreeState == true;

    protected override void SyncFromProperty()
    {
        var property = Property;
        var rawValue = property?.GetValue();
        bool? isChecked = rawValue is bool value ? value : null;

        RunSynchronizing(() =>
        {
            _checkBox.IsThreeState = property?.SupportsThreeState == true;
            _checkBox.IsChecked = isChecked;
            _stateText.Text = isChecked switch
            {
                true => "True",
                false => "False",
                _ => property?.SupportsNullValue == true ? "(null)" : property?.ValueText ?? string.Empty,
            };
        });
    }

    private void OnStateChanged(object sender, RoutedEventArgs e)
    {
        if (IsSynchronizing)
        {
            return;
        }

        Commit(_checkBox.IsChecked);
    }
}

internal sealed class EnumPropertyEditorControl : PropertyEditorControlBase
{
    private readonly ComboBox _comboBox;

    public EnumPropertyEditorControl()
    {
        _comboBox = new ComboBox
        {
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(PropertyEditorUtilities.TransparentFallbackColor),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 120,
            DisplayMemberPath = nameof(PropertyEditorOption.DisplayText),
        };
        _comboBox.SelectionChanged += OnSelectionChanged;
        SetEditorContent(_comboBox);
    }

    protected override void SyncFromProperty()
    {
        var property = Property;
        RunSynchronizing(() =>
        {
            _comboBox.ItemsSource = property?.EditorOptions;
            _comboBox.SelectedItem = FindMatchingOption(property);
        });
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsSynchronizing || _comboBox.SelectedItem is not PropertyEditorOption option)
        {
            return;
        }

        Commit(option.Value);
    }

    private static PropertyEditorOption? FindMatchingOption(PropertyGridNode? property)
    {
        if (property is null)
        {
            return null;
        }

        var rawValue = property.GetValue();
        return property.EditorOptions.FirstOrDefault(option => Equals(option.Value, rawValue)) ??
               property.EditorOptions.FirstOrDefault(option => option.IsNullOption && rawValue is null);
    }
}

internal abstract class QuadPropertyEditorControlBase : PropertyEditorControlBase
{
    private readonly TextBox _first;
    private readonly TextBox _second;
    private readonly TextBox _third;
    private readonly TextBox _fourth;

    protected QuadPropertyEditorControlBase(
        (string Placeholder, string ToolTip) first,
        (string Placeholder, string ToolTip) second,
        (string Placeholder, string ToolTip) third,
        (string Placeholder, string ToolTip) fourth)
    {
        SetChromePadding(new Thickness(8, 4, 8, 4));

        _first = CreatePartTextBox(first.Placeholder, first.ToolTip);
        _second = CreatePartTextBox(second.Placeholder, second.ToolTip);
        _third = CreatePartTextBox(third.Placeholder, third.ToolTip);
        _fourth = CreatePartTextBox(fourth.Placeholder, fourth.ToolTip);

        var grid = new Grid
        {
            ColumnSpacing = 6,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(_first);
        grid.Children.Add(_second);
        grid.Children.Add(_third);
        grid.Children.Add(_fourth);
        Grid.SetColumn(_second, 1);
        Grid.SetColumn(_third, 2);
        Grid.SetColumn(_fourth, 3);

        SetEditorContent(grid);
    }

    protected override void SyncFromProperty()
    {
        var property = Property;
        if (property is { SupportsNullValue: true } && property.GetValue() is null)
        {
            RunSynchronizing(() =>
            {
                _first.Text = string.Empty;
                _second.Text = string.Empty;
                _third.Text = string.Empty;
                _fourth.Text = string.Empty;
            });
            return;
        }

        var (first, second, third, fourth) = GetParts(property?.GetValue());
        RunSynchronizing(() =>
        {
            _first.Text = PropertyEditorUtilities.FormatDouble(first);
            _second.Text = PropertyEditorUtilities.FormatDouble(second);
            _third.Text = PropertyEditorUtilities.FormatDouble(third);
            _fourth.Text = PropertyEditorUtilities.FormatDouble(fourth);
        });
    }

    protected abstract (double First, double Second, double Third, double Fourth) GetParts(object? rawValue);

    protected abstract object CreateValue(double first, double second, double third, double fourth);

    private void CommitFromEditors()
    {
        if (IsSynchronizing)
        {
            return;
        }

        var property = Property;
        if (property is { SupportsNullValue: true } &&
            string.IsNullOrWhiteSpace(_first.Text) &&
            string.IsNullOrWhiteSpace(_second.Text) &&
            string.IsNullOrWhiteSpace(_third.Text) &&
            string.IsNullOrWhiteSpace(_fourth.Text))
        {
            Commit(null);
            return;
        }

        if (PropertyEditorUtilities.TryParseDouble(_first.Text, out var first) &&
            PropertyEditorUtilities.TryParseDouble(_second.Text, out var second) &&
            PropertyEditorUtilities.TryParseDouble(_third.Text, out var third) &&
            PropertyEditorUtilities.TryParseDouble(_fourth.Text, out var fourth))
        {
            Commit(CreateValue(first, second, third, fourth));
            return;
        }

        Commit($"{_first.Text},{_second.Text},{_third.Text},{_fourth.Text}");
    }

    private TextBox CreatePartTextBox(string placeholder, string toolTip)
    {
        var textBox = PropertyEditorUtilities.CreateEditorTextBox();
        textBox.MinWidth = 52;
        textBox.TextAlignment = TextAlignment.Center;
        textBox.PlaceholderText = placeholder;
        PropertyEditorUtilities.ApplyToolTip(textBox, toolTip);
        textBox.KeyDown += OnKeyDown;
        textBox.LostFocus += OnLostFocus;
        return textBox;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitFromEditors();
            e.Handled = true;
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
        => CommitFromEditors();
}

internal sealed class ThicknessPropertyEditorControl : QuadPropertyEditorControlBase
{
    public ThicknessPropertyEditorControl()
        : base(("L", "Left"), ("T", "Top"), ("R", "Right"), ("B", "Bottom"))
    {
    }

    protected override (double First, double Second, double Third, double Fourth) GetParts(object? rawValue)
        => rawValue is Thickness thickness
            ? (thickness.Left, thickness.Top, thickness.Right, thickness.Bottom)
            : default;

    protected override object CreateValue(double first, double second, double third, double fourth)
        => new Thickness(first, second, third, fourth);
}

internal sealed class CornerRadiusPropertyEditorControl : QuadPropertyEditorControlBase
{
    public CornerRadiusPropertyEditorControl()
        : base(("TL", "Top left"), ("TR", "Top right"), ("BR", "Bottom right"), ("BL", "Bottom left"))
    {
    }

    protected override (double First, double Second, double Third, double Fourth) GetParts(object? rawValue)
        => rawValue is CornerRadius cornerRadius
            ? (cornerRadius.TopLeft, cornerRadius.TopRight, cornerRadius.BottomRight, cornerRadius.BottomLeft)
            : default;

    protected override object CreateValue(double first, double second, double third, double fourth)
        => new CornerRadius(first, second, third, fourth);
}

internal sealed class GridLengthPropertyEditorControl : PropertyEditorControlBase
{
    private sealed class GridLengthUnitOption
    {
        public required string DisplayText { get; init; }

        public GridUnitType? UnitType { get; init; }

        public bool IsNullOption { get; init; }
    }

    private readonly TextBox _valueTextBox;
    private readonly ComboBox _unitComboBox;

    public GridLengthPropertyEditorControl()
    {
        SetChromePadding(new Thickness(8, 4, 8, 4));

        _valueTextBox = PropertyEditorUtilities.CreateEditorTextBox();
        _valueTextBox.MinWidth = 72;
        _valueTextBox.PlaceholderText = "1";
        _valueTextBox.KeyDown += OnKeyDown;
        _valueTextBox.LostFocus += OnLostFocus;

        _unitComboBox = new ComboBox
        {
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(PropertyEditorUtilities.TransparentFallbackColor),
            MinWidth = 88,
            DisplayMemberPath = nameof(GridLengthUnitOption.DisplayText),
        };
        _unitComboBox.SelectionChanged += OnSelectionChanged;

        var grid = new Grid
        {
            ColumnSpacing = 8,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(_valueTextBox);
        grid.Children.Add(_unitComboBox);
        Grid.SetColumn(_unitComboBox, 1);

        SetEditorContent(grid);
    }

    protected override void SyncFromProperty()
    {
        var property = Property;
        var options = CreateUnitOptions(property?.SupportsNullValue == true).ToList();
        var rawValue = property?.GetValue();

        RunSynchronizing(() =>
        {
            _unitComboBox.ItemsSource = options;

            if (rawValue is null && property?.SupportsNullValue == true)
            {
                _unitComboBox.SelectedItem = options.FirstOrDefault(x => x.IsNullOption);
                _valueTextBox.Text = string.Empty;
                _valueTextBox.IsEnabled = false;
                return;
            }

            if (rawValue is GridLength gridLength)
            {
                _unitComboBox.SelectedItem = options.FirstOrDefault(x => x.UnitType == gridLength.GridUnitType);
                _valueTextBox.Text = gridLength.GridUnitType == GridUnitType.Auto
                    ? string.Empty
                    : PropertyEditorUtilities.FormatDouble(gridLength.Value);
                _valueTextBox.IsEnabled = gridLength.GridUnitType != GridUnitType.Auto;
                return;
            }

            _unitComboBox.SelectedItem = options.FirstOrDefault(x => x.UnitType == GridUnitType.Pixel);
            _valueTextBox.Text = PropertyEditorUtilities.FormatEditableText(rawValue, property?.ValueText ?? string.Empty);
            _valueTextBox.IsEnabled = true;
        });
    }

    private static IEnumerable<GridLengthUnitOption> CreateUnitOptions(bool supportsNullValue)
    {
        if (supportsNullValue)
        {
            yield return new GridLengthUnitOption
            {
                DisplayText = "(null)",
                IsNullOption = true,
            };
        }

        yield return new GridLengthUnitOption
        {
            DisplayText = "Auto",
            UnitType = GridUnitType.Auto,
        };
        yield return new GridLengthUnitOption
        {
            DisplayText = "Pixel",
            UnitType = GridUnitType.Pixel,
        };
        yield return new GridLengthUnitOption
        {
            DisplayText = "Star",
            UnitType = GridUnitType.Star,
        };
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsSynchronizing)
        {
            return;
        }

        CommitFromState();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitFromState();
            e.Handled = true;
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
        => CommitFromState();

    private void CommitFromState()
    {
        if (IsSynchronizing || _unitComboBox.SelectedItem is not GridLengthUnitOption option)
        {
            return;
        }

        _valueTextBox.IsEnabled = option.UnitType is GridUnitType.Pixel or GridUnitType.Star;

        if (option.IsNullOption)
        {
            Commit(null);
            return;
        }

        if (option.UnitType == GridUnitType.Auto)
        {
            Commit(GridLength.Auto);
            return;
        }

        if (PropertyEditorUtilities.TryParseDouble(_valueTextBox.Text, out var numericValue))
        {
            if (option.UnitType == GridUnitType.Star && string.IsNullOrWhiteSpace(_valueTextBox.Text))
            {
                numericValue = 1;
            }

            Commit(new GridLength(numericValue, option.UnitType!.Value));
            return;
        }

        var suffix = option.UnitType == GridUnitType.Star ? "*" : string.Empty;
        Commit($"{_valueTextBox.Text}{suffix}");
    }
}

internal abstract class ColorLikePropertyEditorControlBase : PropertyEditorControlBase
{
    private readonly Border _previewSwatch;
    private readonly TextBox _textBox;
    private readonly string _defaultPlaceholder;

    protected ColorLikePropertyEditorControlBase(string defaultPlaceholder)
    {
        _defaultPlaceholder = defaultPlaceholder;
        SetChromePadding(new Thickness(8, 4, 8, 4));

        _previewSwatch = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(5),
            BorderBrush = PropertyEditorUtilities.ResolveBrush("DevToolsBorderBrush", PropertyEditorUtilities.BorderFallbackColor),
            BorderThickness = new Thickness(1),
            Background = PropertyEditorUtilities.ResolveBrush("DevToolsSurfaceSubtleBrush", PropertyEditorUtilities.SurfaceFallbackColor),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _textBox = PropertyEditorUtilities.CreateEditorTextBox();
        _textBox.PlaceholderText = defaultPlaceholder;
        _textBox.TextChanged += OnTextChanged;
        _textBox.KeyDown += OnKeyDown;
        _textBox.LostFocus += OnLostFocus;

        var grid = new Grid
        {
            ColumnSpacing = 8,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(_previewSwatch);
        grid.Children.Add(_textBox);
        Grid.SetColumn(_textBox, 1);

        SetEditorContent(grid);
    }

    protected override void OnPropertyAssigned()
        => _textBox.PlaceholderText = Property?.PlaceholderText ?? _defaultPlaceholder;

    protected override void SyncFromProperty()
    {
        var property = Property;
        var rawValue = property?.GetValue();
        RunSynchronizing(() =>
        {
            _textBox.PlaceholderText = property?.PlaceholderText ?? _defaultPlaceholder;
            _textBox.Text = GetEditorText(rawValue);
            UpdatePreview(rawValue);
        });
    }

    protected virtual string GetEditorText(object? rawValue)
        => PropertyEditorUtilities.FormatEditableText(rawValue, Property?.ValueText ?? string.Empty);

    protected abstract void CommitFromText(string text);

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsSynchronizing)
        {
            return;
        }

        if (PropertyEditorUtilities.TryParseColor(_textBox.Text, out var color))
        {
            UpdatePreview(color);
            return;
        }

        UpdatePreview(Property?.GetValue());
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitFromText(_textBox.Text);
            e.Handled = true;
        }
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
        => CommitFromText(_textBox.Text);

    private void UpdatePreview(object? rawValue)
    {
        _previewSwatch.Background = PropertyEditorUtilities.CreatePreviewBrush(rawValue) ??
                                    PropertyEditorUtilities.ResolveBrush("DevToolsSurfaceSubtleBrush", PropertyEditorUtilities.SurfaceFallbackColor);
    }
}

internal sealed class ColorPropertyEditorControl : ColorLikePropertyEditorControlBase
{
    public ColorPropertyEditorControl()
        : base("#AARRGGBB")
    {
    }

    protected override void CommitFromText(string text)
    {
        if (IsSynchronizing)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text) && Property?.SupportsNullValue == true)
        {
            Commit(null);
            return;
        }

        if (PropertyEditorUtilities.TryParseColor(text, out var color))
        {
            Commit(color);
            return;
        }

        Commit(text);
    }
}

internal sealed class BrushPropertyEditorControl : ColorLikePropertyEditorControlBase
{
    public BrushPropertyEditorControl()
        : base("#AARRGGBB")
    {
    }

    protected override string GetEditorText(object? rawValue)
        => rawValue switch
        {
            SolidColorBrush solidColorBrush => PropertyEditorUtilities.FormatColor(solidColorBrush.Color),
            null => string.Empty,
            _ => Property?.ValueText ?? string.Empty,
        };

    protected override void CommitFromText(string text)
    {
        if (IsSynchronizing)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text) && Property?.SupportsNullValue == true)
        {
            Commit(null);
            return;
        }

        if (PropertyEditorUtilities.TryParseColor(text, out var color))
        {
            Commit(color);
            return;
        }

        Commit(text);
    }
}

internal static class PropertyEditorControlFactory
{
    public static PropertyEditorControlBase Create(PropertyGridNode property)
        => !property.IsEditable || property.EditorKind == PropertyEditorKind.ReadOnly
            ? new ReadOnlyPropertyEditorControl()
            : property.EditorKind switch
            {
                PropertyEditorKind.Boolean => new BooleanPropertyEditorControl(),
                PropertyEditorKind.Enum => new EnumPropertyEditorControl(),
                PropertyEditorKind.Numeric => new TextPropertyEditorControl(),
                PropertyEditorKind.Text => new TextPropertyEditorControl(),
                PropertyEditorKind.Thickness => new ThicknessPropertyEditorControl(),
                PropertyEditorKind.CornerRadius => new CornerRadiusPropertyEditorControl(),
                PropertyEditorKind.GridLength => new GridLengthPropertyEditorControl(),
                PropertyEditorKind.Color => new ColorPropertyEditorControl(),
                PropertyEditorKind.Brush => new BrushPropertyEditorControl(),
                _ => new ReadOnlyPropertyEditorControl(),
            };
}
