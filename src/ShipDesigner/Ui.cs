using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.Wpf;
using FontWeights = System.Windows.FontWeights;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
namespace ShipDesigner;

static class Ui
{
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
    public static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x1E, 0x8E, 0x3E));
    public static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    public static TextBlock H(string s, double size = 18) => new() { Text = s, FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap, Foreground = Accent };
    public static TextBlock T(string s = "", double size = 13) => new() { Text = s, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
    public static Button B(string s, Action a, bool primary = false)
    {
        var b = new Button { Content = s, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 3, 6, 3), FontSize = 13 };
        if (primary) { b.Background = Accent; b.Foreground = Brushes.White; }
        b.Click += (_, _) => { try { a(); } catch (Exception e) { MessageBox.Show(e.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning); } };
        return b;
    }
    public static StackPanel V(params UIElement[] items) { var p = new StackPanel(); foreach (var i in items) if (i != null) p.Children.Add(i); return p; }
    public static WrapPanel Row(params UIElement[] items) { var p = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) }; foreach (var i in items) if (i != null) p.Children.Add(i); return p; }
    public static ScrollViewer Scroll(UIElement c) => new() { Content = c, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10) };
    public static Grid Split(UIElement left, UIElement right, double leftWidth = 330)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sp = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Gainsboro };
        Grid.SetColumn(sp, 1); Grid.SetColumn(right, 2); g.Children.Add(left); g.Children.Add(sp); g.Children.Add(right); return g;
    }
    public static Grid Rows(UIElement top, UIElement bottom, double topStar = 1, double bottomStar = 1)
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(topStar, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(bottomStar, GridUnitType.Star) });
        var sp = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Gainsboro };
        Grid.SetRow(sp, 1); Grid.SetRow(bottom, 2); g.Children.Add(top); g.Children.Add(sp); g.Children.Add(bottom); return g;
    }
    public static ComboBox Combo(params string[] items) { var c = new ComboBox { FontSize = 13, Margin = new Thickness(0, 3, 6, 3), MinWidth = 120 }; foreach (var i in items) c.Items.Add(i); c.SelectedIndex = 0; return c; }

    /// <summary>Числовое поле с подписью.</summary>
    public class Num
    {
        public TextBox Box; public UIElement Element;
        public Num(string label, double value, string tip = null, double width = 90)
        {
            Box = new TextBox { Text = Fmt(value), Width = width, FontSize = 13, Margin = new Thickness(4, 2, 8, 2), ToolTip = tip };
            var lab = new TextBlock { Text = label, Width = 210, VerticalAlignment = VerticalAlignment.Center, FontSize = 13, ToolTip = tip };
            Element = new StackPanel { Orientation = Orientation.Horizontal, Children = { lab, Box } };
        }
        public double Value
        {
            get { if (double.TryParse(Box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v; throw new FormatException("Неверное число: " + Box.Text); }
            set => Box.Text = Fmt(value);
        }
    }
    public static string Fmt(double v, string f = "0.####") => v.ToString(f, CultureInfo.InvariantCulture);

    public static PlotModel Plot(string title, string xTitle, string yTitle)
    {
        var m = new PlotModel { Title = title, TitleFontSize = 14 };
        m.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = xTitle, MajorGridlineStyle = LineStyle.Solid, MinorGridlineStyle = LineStyle.Dot, MajorGridlineColor = OxyColor.FromRgb(220, 220, 220) });
        m.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = yTitle, MajorGridlineStyle = LineStyle.Solid, MinorGridlineStyle = LineStyle.Dot, MajorGridlineColor = OxyColor.FromRgb(220, 220, 220) });
        m.Legends.Add(new Legend { LegendPosition = LegendPosition.RightBottom, LegendPlacement = LegendPlacement.Inside, LegendBackground = OxyColor.FromAColor(200, OxyColors.White) });
        return m;
    }
    public static LineSeries Line(string title, IEnumerable<(double x, double y)> pts, bool markers = false)
    {
        var s = new LineSeries { Title = title, StrokeThickness = 2, TrackerFormatString = "{0}\n{1}: {2:0.###}\n{3}: {4:0.###}" };
        if (markers) { s.MarkerType = MarkerType.Circle; s.MarkerSize = 3; }
        foreach (var (x, y) in pts) s.Points.Add(new DataPoint(x, y));
        return s;
    }
    public static PlotView View(PlotModel m) => new() { Model = m, Margin = new Thickness(4) };

    public static DataGrid Table(System.Data.DataTable t) => new() { ItemsSource = t.DefaultView, IsReadOnly = true, AutoGenerateColumns = true, FontSize = 12, CanUserSortColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xF3, 0xF7, 0xFC)) };

    public static string Ask(string title, string prompt, string def = "")
    {
        var tb = new TextBox { Text = def, Margin = new Thickness(0, 6, 0, 6), FontSize = 14 };
        var w = new Window { Title = title, Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = App.Main, ResizeMode = ResizeMode.NoResize };
        string res = null;
        var ok = B("OK", () => { res = tb.Text; w.Close(); }, true); ok.IsDefault = true;
        w.Content = new StackPanel { Margin = new Thickness(12), Children = { T(prompt), tb, Row(ok, B("Отмена", () => w.Close())) } };
        w.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        w.ShowDialog(); return res;
    }
}
