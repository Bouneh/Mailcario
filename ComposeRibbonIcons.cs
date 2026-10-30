using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Mailcario;

internal static class ComposeRibbonIcons
{
    public static FrameworkElement Labeled(string kind, string label, Color? swatch = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        var icon = Create(kind, swatch);
        icon.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(icon);
        var text = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        row.Children.Add(text);
        return row;
    }

    public static FrameworkElement Create(string kind, Color? swatch = null)
    {
        var canvas = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false };
        switch (kind)
        {
            case "cut":
                AddStroke(canvas, "M6,6 L17,17 M6,14 L17,3", 1.7);
                AddCircle(canvas, 1.5, 3, 5, 5);
                AddCircle(canvas, 1.5, 12, 5, 5);
                break;
            case "copy":
                AddStroke(canvas, "M6,3 H17 V15 H6 Z M3,6 H2 V18 H13 V17", 1.5);
                break;
            case "painter":
                AddStroke(canvas, "M2,4 H15 V9 H2 Z M15,6 H18 V12 H10 V17", 1.5);
                AddStroke(canvas, "M8,17 H12", 1.5);
                break;
            case "grow":
            case "shrink":
                AddLetter(canvas, "A", 14, 1, 2);
                AddAccentStroke(canvas, kind == "grow" ? "M15,15 V4 M12,7 L15,4 L18,7" :
                    "M15,4 V15 M12,12 L15,15 L18,12", 1.6);
                break;
            case "bullets":
                AddStroke(canvas, "M7,5 H18 M7,10 H18 M7,15 H18", 1.5);
                foreach (double y in new[] { 5d, 10d, 15d }) AddFilledDot(canvas, 3, y, 1.4);
                break;
            case "numbering":
                AddStroke(canvas, "M8,5 H18 M8,10 H18 M8,15 H18", 1.5);
                AddNumbers(canvas);
                break;
            case "clear":
                AddLetter(canvas, "A", 15, 1, 1);
                AddAccentStroke(canvas, "M11,12 L17,6 L19,8 L13,14 Z M11,12 L9,14 L13,14", 1.5);
                break;
            case "highlight":
                AddStroke(canvas, "M5,3 L13,3 L16,6 L8,14 L4,10 Z M4,10 L2,13 L6,17 L8,14", 1.4);
                AddSwatch(canvas, swatch ?? Color.FromRgb(255, 238, 135));
                break;
            case "fontColor":
                AddLetter(canvas, "A", 15, 3, 0);
                AddSwatch(canvas, swatch ?? Color.FromRgb(54, 142, 207));
                break;
            case "automatic":
                var automatic = new Rectangle
                {
                    Width = 15, Height = 15, Fill = new SolidColorBrush(swatch ?? Colors.Black),
                    StrokeThickness = 1
                };
                automatic.SetResourceReference(Shape.StrokeProperty, "Stroke");
                Canvas.SetLeft(automatic, 2);
                Canvas.SetTop(automatic, 2);
                canvas.Children.Add(automatic);
                if (swatch is { A: 0 }) AddAccentStroke(canvas, "M3,16 L16,3", 1.5);
                break;
            case "palette":
                AddCircle(canvas, 1.3, 2, 2, 16);
                AddColoredDot(canvas, 6, 6, Color.FromRgb(225, 71, 71));
                AddColoredDot(canvas, 12, 6, Color.FromRgb(241, 190, 45));
                AddColoredDot(canvas, 6, 12, Color.FromRgb(52, 153, 210));
                AddColoredDot(canvas, 12, 12, Color.FromRgb(72, 169, 105));
                break;
        }
        return canvas;
    }

    private static void AddNumbers(Canvas canvas)
    {
        foreach ((string label, double top) in new[] { ("1", 0d), ("2", 5d), ("3", 10d) })
        {
            var text = new TextBlock { Text = label, FontSize = 7, FontWeight = FontWeights.SemiBold };
            text.SetResourceReference(TextBlock.ForegroundProperty, "IconNeutral");
            Canvas.SetLeft(text, 1);
            Canvas.SetTop(text, top);
            canvas.Children.Add(text);
        }
    }

    private static void AddLetter(Canvas canvas, string letter, double size, double left, double top)
    {
        var text = new TextBlock { Text = letter, FontSize = size, FontWeight = FontWeights.SemiBold };
        text.SetResourceReference(TextBlock.ForegroundProperty, "IconNeutral");
        Canvas.SetLeft(text, left);
        Canvas.SetTop(text, top);
        canvas.Children.Add(text);
    }

    private static void AddCircle(Canvas canvas, double stroke, double left, double top, double size)
    {
        var ellipse = new Ellipse { Width = size, Height = size, StrokeThickness = stroke };
        ellipse.SetResourceReference(Shape.StrokeProperty, "IconNeutral");
        Canvas.SetLeft(ellipse, left);
        Canvas.SetTop(ellipse, top);
        canvas.Children.Add(ellipse);
    }

    private static void AddFilledDot(Canvas canvas, double x, double y, double radius)
    {
        var ellipse = new Ellipse { Width = radius * 2, Height = radius * 2 };
        ellipse.SetResourceReference(Shape.FillProperty, "IconNeutral");
        Canvas.SetLeft(ellipse, x - radius);
        Canvas.SetTop(ellipse, y - radius);
        canvas.Children.Add(ellipse);
    }

    private static void AddColoredDot(Canvas canvas, double x, double y, Color color)
    {
        var dot = new Ellipse { Width = 3, Height = 3, Fill = new SolidColorBrush(color) };
        Canvas.SetLeft(dot, x);
        Canvas.SetTop(dot, y);
        canvas.Children.Add(dot);
    }

    private static void AddStroke(Canvas canvas, string data, double thickness)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data), StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        path.SetResourceReference(Shape.StrokeProperty, "IconNeutral");
        canvas.Children.Add(path);
    }

    private static void AddAccentStroke(Canvas canvas, string data, double thickness)
    {
        var path = new Path { Data = Geometry.Parse(data), StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round };
        path.SetResourceReference(Shape.StrokeProperty, "AccentDark");
        canvas.Children.Add(path);
    }

    private static void AddSwatch(Canvas canvas, Color color)
    {
        if (color.A == 0)
        {
            AddStroke(canvas, "M3,18 H17", 1.5);
            return;
        }
        var swatch = new Rectangle { Width = 16, Height = 2.8, Fill = new SolidColorBrush(color), RadiusX = 1 };
        Canvas.SetLeft(swatch, 2);
        Canvas.SetTop(swatch, 17);
        canvas.Children.Add(swatch);
    }
}
