using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace TodoDesktopApp.Controls;

public static class RoundedClip
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(RoundedClip),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value)
    {
        element.SetValue(EnabledProperty, value);
    }

    public static bool GetEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(EnabledProperty);
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            border.Loaded += Border_Loaded;
            border.SizeChanged += Border_SizeChanged;
            UpdateClip(border);
            return;
        }

        border.Loaded -= Border_Loaded;
        border.SizeChanged -= Border_SizeChanged;
        ClearClip(border);
    }

    private static void Border_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border border)
        {
            UpdateClip(border);
        }
    }

    private static void Border_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border)
        {
            UpdateClip(border);
        }
    }

    private static void UpdateClip(Border border)
    {
        if (border.Child is not FrameworkElement child)
        {
            return;
        }

        var width = child.ActualWidth;
        var height = child.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var thickness = border.BorderThickness;
        var radius = border.CornerRadius;
        var topLeft = NormalizeRadius(radius.TopLeft - Math.Max(thickness.Left, thickness.Top), width, height);
        var topRight = NormalizeRadius(radius.TopRight - Math.Max(thickness.Right, thickness.Top), width, height);
        var bottomRight = NormalizeRadius(radius.BottomRight - Math.Max(thickness.Right, thickness.Bottom), width, height);
        var bottomLeft = NormalizeRadius(radius.BottomLeft - Math.Max(thickness.Left, thickness.Bottom), width, height);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(topLeft, 0), true, true);
            context.LineTo(new Point(width - topRight, 0), true, false);
            AddCorner(context, new Point(width, topRight), topRight);
            context.LineTo(new Point(width, height - bottomRight), true, false);
            AddCorner(context, new Point(width - bottomRight, height), bottomRight);
            context.LineTo(new Point(bottomLeft, height), true, false);
            AddCorner(context, new Point(0, height - bottomLeft), bottomLeft);
            context.LineTo(new Point(0, topLeft), true, false);
            AddCorner(context, new Point(topLeft, 0), topLeft);
        }

        geometry.Freeze();
        child.Clip = geometry;
    }

    private static void ClearClip(Border border)
    {
        if (border.Child is FrameworkElement child)
        {
            child.Clip = null;
        }
    }

    private static double NormalizeRadius(double radius, double width, double height)
    {
        return Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));
    }

    private static void AddCorner(StreamGeometryContext context, Point point, double radius)
    {
        if (radius <= 0)
        {
            context.LineTo(point, true, false);
            return;
        }

        context.ArcTo(point, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
    }
}
