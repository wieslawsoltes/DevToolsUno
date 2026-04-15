using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DevToolsUno.Diagnostics.Views;

public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(WrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(WrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var maxWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width);
        var currentLineWidth = 0d;
        var currentLineHeight = 0d;
        var desiredWidth = 0d;
        var desiredHeight = 0d;
        var lineCount = 0;

        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            child.Measure(new Size(maxWidth, availableSize.Height));
            var childSize = child.DesiredSize;

            var requiresWrap = !double.IsInfinity(maxWidth) &&
                               currentLineWidth > 0 &&
                               currentLineWidth + HorizontalSpacing + childSize.Width > maxWidth;

            if (requiresWrap)
            {
                FlushLine(VerticalSpacing, ref currentLineWidth, ref currentLineHeight, ref desiredWidth, ref desiredHeight, ref lineCount);
            }

            currentLineWidth = currentLineWidth > 0
                ? currentLineWidth + HorizontalSpacing + childSize.Width
                : childSize.Width;
            currentLineHeight = Math.Max(currentLineHeight, childSize.Height);
        }

        FlushLine(VerticalSpacing, ref currentLineWidth, ref currentLineHeight, ref desiredWidth, ref desiredHeight, ref lineCount);
        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var maxWidth = Math.Max(0, finalSize.Width);
        var lineStartIndex = 0;
        var lineWidth = 0d;
        var lineHeight = 0d;
        var y = 0d;

        for (var index = 0; index < Children.Count; index++)
        {
            var child = Children[index];
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var childSize = child.DesiredSize;
            var requiresWrap = lineWidth > 0 && lineWidth + HorizontalSpacing + childSize.Width > maxWidth;

            if (requiresWrap)
            {
                ArrangeLine(lineStartIndex, index, y, lineHeight);
                y += lineHeight + VerticalSpacing;
                lineStartIndex = index;
                lineWidth = childSize.Width;
                lineHeight = childSize.Height;
                continue;
            }

            lineWidth = lineWidth > 0
                ? lineWidth + HorizontalSpacing + childSize.Width
                : childSize.Width;
            lineHeight = Math.Max(lineHeight, childSize.Height);
        }

        ArrangeLine(lineStartIndex, Children.Count, y, lineHeight);
        return finalSize;
    }

    private void ArrangeLine(int startIndex, int endIndex, double y, double lineHeight)
    {
        var x = 0d;

        for (var index = startIndex; index < endIndex; index++)
        {
            var child = Children[index];
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var childSize = child.DesiredSize;
            child.Arrange(new Rect(x, y, childSize.Width, Math.Max(lineHeight, childSize.Height)));
            x += childSize.Width + HorizontalSpacing;
        }
    }

    private static void FlushLine(
        double verticalSpacing,
        ref double currentLineWidth,
        ref double currentLineHeight,
        ref double desiredWidth,
        ref double desiredHeight,
        ref int lineCount)
    {
        if (currentLineWidth <= 0 && currentLineHeight <= 0)
        {
            return;
        }

        if (lineCount > 0)
        {
            desiredHeight += verticalSpacing;
        }

        desiredWidth = Math.Max(desiredWidth, currentLineWidth);
        desiredHeight += currentLineHeight;
        currentLineWidth = 0;
        currentLineHeight = 0;
        lineCount++;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WrapPanel panel)
        {
            panel.InvalidateMeasure();
            panel.InvalidateArrange();
        }
    }
}
