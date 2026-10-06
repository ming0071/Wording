using System.Windows;
using System.Windows.Controls;

namespace Wording.Desktop.Views;

/// <summary>Equal-width columns with each wrapped row sized to its own content.</summary>
public sealed class ReviewDefinitionsPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(nameof(Columns), typeof(int),
        typeof(ReviewDefinitionsPanel), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Math.Max(1, Columns);
        var width = availableSize.Width / columns;
        double height = 0, rowHeight = 0, desiredWidth = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(width, double.PositiveInfinity));
            desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if ((i + 1) % columns == 0 || i == InternalChildren.Count - 1)
            {
                height += rowHeight;
                rowHeight = 0;
            }
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : desiredWidth * columns, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Max(1, Columns);
        var width = finalSize.Width / columns;
        double top = 0;
        for (var start = 0; start < InternalChildren.Count; start += columns)
        {
            var end = Math.Min(start + columns, InternalChildren.Count);
            var height = Enumerable.Range(start, end - start).Max(i => InternalChildren[i].DesiredSize.Height);
            for (var i = start; i < end; i++)
                InternalChildren[i].Arrange(new Rect((i - start) * width, top, width, height));
            top += height;
        }
        return finalSize;
    }
}
