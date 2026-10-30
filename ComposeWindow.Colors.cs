using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Mailcario;

public partial class ComposeWindow
{
    private static readonly Color[] ThemeColors =
    [
        Colors.White, Colors.Black, Color.FromRgb(231, 230, 230), Color.FromRgb(14, 40, 65),
        Color.FromRgb(21, 96, 130), Color.FromRgb(233, 113, 50), Color.FromRgb(25, 107, 36),
        Color.FromRgb(15, 158, 213), Color.FromRgb(160, 43, 147), Color.FromRgb(78, 167, 46)
    ];

    private static readonly Color[] StandardColors =
    [
        Color.FromRgb(192, 0, 0), Color.FromRgb(255, 0, 0), Color.FromRgb(255, 192, 0),
        Color.FromRgb(255, 255, 0), Color.FromRgb(146, 208, 80), Color.FromRgb(0, 176, 80),
        Color.FromRgb(0, 176, 240), Color.FromRgb(0, 112, 192), Color.FromRgb(0, 32, 96),
        Color.FromRgb(112, 48, 160)
    ];

    private Color? _selectedFontColor = Color.FromRgb(54, 142, 207);
    private Color? _selectedHighlightColor = Color.FromRgb(255, 238, 135);
    private bool _highContrastColorsOnly;
    private Popup? _colorPalettePopup;
    private TextRange? _paletteSelection;

    private void UpdateColorButtons()
    {
        Color automaticFont = (FindResource("Ink") as SolidColorBrush)?.Color ?? Colors.Black;
        ComposeFontColorButton.Content = ComposeRibbonIcons.Create("fontColor", _selectedFontColor ?? automaticFont);
        ComposeHighlightButton.Content = ComposeRibbonIcons.Create("highlight", _selectedHighlightColor ?? Colors.Transparent);
    }

    private void ApplyHighlightColorButton_Click(object sender, RoutedEventArgs e) =>
        ApplySelectedColor(true, _selectedHighlightColor);

    private void ApplyFontColorButton_Click(object sender, RoutedEventArgs e) =>
        ApplySelectedColor(false, _selectedFontColor);

    private void HighlightButton_Click(object sender, RoutedEventArgs e) =>
        ShowColorPalette((Button)sender, true);

    private void FontColorButton_Click(object sender, RoutedEventArgs e) =>
        ShowColorPalette((Button)sender, false);

    private void ShowColorPalette(Button target, bool highlight)
    {
        if (_colorPalettePopup is not null) _colorPalettePopup.IsOpen = false;
        _paletteSelection = new TextRange(BodyEditor.Selection.Start, BodyEditor.Selection.End);
        var popup = new Popup
        {
            PlacementTarget = target, Placement = PlacementMode.Bottom,
            AllowsTransparency = true, StaysOpen = false, PopupAnimation = PopupAnimation.Fade,
            Child = BuildColorPalette(highlight)
        };
        popup.Closed += (_, _) => { if (ReferenceEquals(_colorPalettePopup, popup)) _paletteSelection = null; };
        _colorPalettePopup = popup;
        popup.IsOpen = true;
    }

    private FrameworkElement BuildColorPalette(bool highlight)
    {
        var panel = new StackPanel();
        var border = new Border
        {
            Width = 283, Padding = new Thickness(8), CornerRadius = new CornerRadius(7),
            Background = (Brush)FindResource("Surface"), BorderBrush = (Brush)FindResource("Stroke"),
            BorderThickness = new Thickness(1), Child = panel
        };
        var contrast = new CheckBox
        {
            Content = Localizer.T("highContrastColorsOnly"), IsChecked = _highContrastColorsOnly,
            FontSize = 11, Margin = new Thickness(4, 2, 3, 5)
        };
        panel.Children.Add(contrast);
        AddPaletteDivider(panel);
        var automatic = new Button
        {
            Style = (Style)FindResource("ComposeRibbonRowButton"), Height = 28,
            Content = ComposeRibbonIcons.Labeled("automatic", Localizer.T("automaticColor"),
                highlight ? Colors.Transparent : Colors.Black)
        };
        automatic.Click += (_, _) => SelectPaletteColor(highlight, null);
        panel.Children.Add(automatic);
        AddPaletteDivider(panel);

        panel.Children.Add(PaletteHeading(Localizer.T("themeColors")));
        var themeGrid = new UniformGrid { Columns = 10, Margin = new Thickness(1, 2, 1, 7) };
        panel.Children.Add(themeGrid);
        AddPaletteDivider(panel);
        panel.Children.Add(PaletteHeading(Localizer.T("standardColors")));
        var standardGrid = new UniformGrid { Columns = 10, Margin = new Thickness(1, 2, 1, 7) };
        panel.Children.Add(standardGrid);
        AddPaletteDivider(panel);
        var more = new Button
        {
            Style = (Style)FindResource("ComposeRibbonRowButton"), Height = 28,
            Content = ComposeRibbonIcons.Labeled("palette", Localizer.T("moreColors"))
        };
        more.Click += (_, _) => ShowCustomColorDialog(highlight);
        panel.Children.Add(more);

        void Populate()
        {
            themeGrid.Children.Clear();
            standardGrid.Children.Clear();
            for (int column = 0; column < ThemeColors.Length; column++)
                themeGrid.Children.Add(ColorSwatch(ThemeColors[column], highlight));
            for (int row = 0; row < 5; row++)
                for (int column = 0; column < ThemeColors.Length; column++)
                    themeGrid.Children.Add(ColorSwatch(ThemeShade(ThemeColors[column], column, row), highlight));
            foreach (Color color in StandardColors)
                standardGrid.Children.Add(ColorSwatch(color, highlight));
        }
        contrast.Checked += (_, _) => { _highContrastColorsOnly = true; Populate(); };
        contrast.Unchecked += (_, _) => { _highContrastColorsOnly = false; Populate(); };
        Populate();
        return border;
    }

    private Button ColorSwatch(Color color, bool highlight)
    {
        Color? selected = highlight ? _selectedHighlightColor : _selectedFontColor;
        var swatch = new Button
        {
            Style = (Style)FindResource("ComposeColorSwatchButton"),
            Background = new SolidColorBrush(color),
            BorderBrush = (Brush)FindResource(selected == color ? "AccentDark" : "Stroke"),
            BorderThickness = new Thickness(selected == color ? 2 : 1),
            ToolTip = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
            IsEnabled = !_highContrastColorsOnly || ContrastRatio(color, ContrastBackground(highlight)) >= 4.5
        };
        swatch.Click += (_, _) => SelectPaletteColor(highlight, color);
        return swatch;
    }

    private void SelectPaletteColor(bool highlight, Color? color) =>
        SelectPaletteColorAtRange(highlight, color, _paletteSelection);

    private void SelectPaletteColorAtRange(bool highlight, Color? color, TextRange? range)
    {
        if (highlight) _selectedHighlightColor = color;
        else _selectedFontColor = color;
        UpdateColorButtons();
        ApplySelectedColor(highlight, color, range);
        if (_colorPalettePopup is not null) _colorPalettePopup.IsOpen = false;
        _paletteSelection = null;
    }

    private void ApplySelectedColor(bool highlight, Color? color, TextRange? range = null)
    {
        Brush brush = color is { } selected ? new SolidColorBrush(selected) :
            highlight ? Brushes.Transparent : (Brush)FindResource("Ink");
        (range ?? BodyEditor.Selection).ApplyPropertyValue(highlight ? TextElement.BackgroundProperty :
            TextElement.ForegroundProperty, brush);
        BodyEditor.Focus();
    }

    private void ShowCustomColorDialog(bool highlight)
    {
        TextRange? selection = _paletteSelection;
        if (_colorPalettePopup is not null) _colorPalettePopup.IsOpen = false;
        Color current = (highlight ? _selectedHighlightColor : _selectedFontColor) ?? Colors.White;
        using var dialog = new Forms.ColorDialog
        {
            FullOpen = true, Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };
        var owner = new ColorDialogOwner(new WindowInteropHelper(this).Handle);
        if (dialog.ShowDialog(owner) == Forms.DialogResult.OK)
            SelectPaletteColorAtRange(highlight, Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B), selection);
    }

    private Color ContrastBackground(bool highlight)
    {
        Brush brush = highlight ? BodyEditor.Foreground : BodyEditor.Background;
        return brush is SolidColorBrush solid ? solid.Color : Colors.White;
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Channel(byte value)
        {
            double normalized = value / 255d;
            return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static Color ThemeShade(Color baseColor, int column, int row)
    {
        if (column < 3)
        {
            byte[] gray = column == 0 ? [244, 218, 184, 137, 82] :
                column == 1 ? [217, 166, 115, 64, 26] : [232, 196, 156, 106, 55];
            return Color.FromRgb(gray[row], gray[row], gray[row]);
        }
        double blend = row switch { 0 => 0.8, 1 => 0.6, 2 => 0.35, 3 => -0.25, _ => -0.5 };
        byte Shade(byte value) => (byte)Math.Clamp(Math.Round(blend >= 0
            ? value + (255 - value) * blend : value * (1 + blend)), 0, 255);
        return Color.FromRgb(Shade(baseColor.R), Shade(baseColor.G), Shade(baseColor.B));
    }

    private static TextBlock PaletteHeading(string text)
    {
        var heading = new TextBlock
        {
            Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(3, 6, 0, 2)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        return heading;
    }

    private void AddPaletteDivider(Panel panel) => panel.Children.Add(new Border
    {
        Height = 1, Background = (Brush)FindResource("Stroke"), Margin = new Thickness(0, 4, 0, 4)
    });

    private sealed class ColorDialogOwner(nint handle) : Forms.IWin32Window
    {
        public nint Handle => handle;
    }
}
