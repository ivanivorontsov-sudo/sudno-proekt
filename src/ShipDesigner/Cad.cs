using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ShipDesigner.Core;
using Drawing = ShipDesigner.Core.Drawing;
using Layer = ShipDesigner.Core.Layer;
using static ShipDesigner.Ui;
namespace ShipDesigner;

public enum Tool { Select, Line, Polyline, Circle, Arc, Rect, Dim, Text, Pan }

public class CadCanvas : FrameworkElement
{
    public Drawing Drawing = new();
    public Tool Tool = Tool.Select;
    public string CurrentLayer = "0";
    public bool SnapObjects = true, SnapGrid = false, Ortho = false, ShowGrid = true;
    public double GridStep = 1;
    public double Scale = 5; public Point Offset = new(60, 500); // экран = Offset + (x, −y)·Scale
    readonly List<P2> pts = new(); P2? cursor; P2? snapMark; Point? panStart; Point panOffset0;
    public Entity Selected; P2? dragFrom;
    readonly Stack<string> undo = new(); readonly Stack<string> redo = new();
    public event Action<string> Status;
    public event Action Changed;

    public CadCanvas() { Focusable = true; ClipToBounds = true; Cursor = Cursors.Cross; }
    Point S(P2 p) => new(Offset.X + p.X * Scale, Offset.Y - p.Y * Scale);
    P2 W(Point p) => new((p.X - Offset.X) / Scale, (Offset.Y - p.Y) / Scale);
    public void SetDrawing(Drawing d) { Drawing = d; Selected = null; pts.Clear(); undo.Clear(); redo.Clear(); if (!Drawing.Layers.Any(l => l.Name == CurrentLayer)) CurrentLayer = Drawing.Layers[0].Name; ZoomExtents(); Changed?.Invoke(); }
    public void Snapshot() { undo.Push(Drawing.ToJson()); redo.Clear(); }
    public void Undo() { if (undo.Count == 0) return; redo.Push(Drawing.ToJson()); Drawing = Drawing.FromJson(undo.Pop()); Selected = null; InvalidateVisual(); Changed?.Invoke(); }
    public void Redo() { if (redo.Count == 0) return; undo.Push(Drawing.ToJson()); Drawing = Drawing.FromJson(redo.Pop()); Selected = null; InvalidateVisual(); Changed?.Invoke(); }
    public void ZoomExtents()
    {
        if (ActualWidth < 10) { Loaded += (_, _) => ZoomExtents(); return; }
        var (mn, mx) = Drawing.Extents(); double w = Math.Max(1e-6, mx.X - mn.X), h = Math.Max(1e-6, mx.Y - mn.Y);
        Scale = Math.Min((ActualWidth - 40) / w, (ActualHeight - 40) / h);
        Offset = new Point(20 - mn.X * Scale + (ActualWidth - 40 - w * Scale) / 2, ActualHeight - 20 + mn.Y * Scale - (ActualHeight - 40 - h * Scale) / 2);
        InvalidateVisual();
    }
    public void CancelTool() { pts.Clear(); InvalidateVisual(); }
    public void DeleteSelected() { if (Selected == null) return; Snapshot(); Drawing.Entities.Remove(Selected); Selected = null; InvalidateVisual(); Changed?.Invoke(); }

    P2 Snap(Point sp, bool shift)
    {
        var w = W(sp); snapMark = null;
        if (SnapObjects)
        {
            double best = 10 / Scale; P2? b = null;
            foreach (var e in Drawing.Entities) { if (!Drawing.GetLayer(e.Layer).Visible) continue; foreach (var q in e.SnapPoints()) { double d = P2.Dist(q, w); if (d < best) { best = d; b = q; } } }
            if (b != null) { snapMark = b; return b.Value; }
        }
        if (SnapGrid && GridStep > 0) w = new P2(Math.Round(w.X / GridStep) * GridStep, Math.Round(w.Y / GridStep) * GridStep);
        if ((Ortho || shift) && pts.Count > 0) { var l = pts[^1]; w = Math.Abs(w.X - l.X) > Math.Abs(w.Y - l.Y) ? new P2(w.X, l.Y) : new P2(l.X, w.Y); }
        return w;
    }
    void Add(Entity e) { Snapshot(); e.Layer = CurrentLayer; Drawing.Entities.Add(e); Changed?.Invoke(); }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus(); var sp = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right && pts.Count == 0 || Tool == Tool.Pan && e.ChangedButton == MouseButton.Left)
        { panStart = sp; panOffset0 = Offset; CaptureMouse(); return; }
        if (e.ChangedButton == MouseButton.Right) { FinishPolyline(); return; }
        if (e.ChangedButton != MouseButton.Left) return;
        var p = Snap(sp, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        switch (Tool)
        {
            case Tool.Select:
                var w = W(sp);
                Selected = Drawing.Entities.Where(x => Drawing.GetLayer(x.Layer).Visible && !Drawing.GetLayer(x.Layer).Locked).OrderBy(x => x.Distance(w)).FirstOrDefault(x => x.Distance(w) < 6 / Scale);
                if (Selected != null) { dragFrom = p; Snapshot(); CaptureMouse(); }
                break;
            case Tool.Line: pts.Add(p); if (pts.Count == 2) { Add(new LineEnt { A = pts[0], B = pts[1] }); pts.RemoveAt(0); } break;
            case Tool.Polyline: if (e.ClickCount == 2) { FinishPolyline(); break; } pts.Add(p); break;
            case Tool.Rect: pts.Add(p); if (pts.Count == 2) { var a = pts[0]; var b = pts[1]; Add(new PolylineEnt { Closed = true, Points = { a, new P2(b.X, a.Y), b, new P2(a.X, b.Y) } }); pts.Clear(); } break;
            case Tool.Circle: pts.Add(p); if (pts.Count == 2) { Add(new CircleEnt { C = pts[0], R = P2.Dist(pts[0], pts[1]) }); pts.Clear(); } break;
            case Tool.Arc: pts.Add(p); if (pts.Count == 3) { var arc = ArcEnt.Through(pts[0], pts[1], pts[2]); if (arc != null) Add(arc); pts.Clear(); } break;
            case Tool.Dim:
                pts.Add(p);
                if (pts.Count == 3) { var d = new DimEnt { A = pts[0], B = pts[1], TextHeight = Math.Max(0.1, 14 / Scale) }; d.Offset = (pts[2].X - d.A.X) * d.Normal.X + (pts[2].Y - d.A.Y) * d.Normal.Y; Add(d); pts.Clear(); }
                break;
            case Tool.Text:
                var s = Ask("Текст", "Введите текст надписи:"); if (!string.IsNullOrEmpty(s)) Add(new TextEnt { P = p, Text = s, Height = Math.Max(0.1, 14 / Scale) });
                break;
        }
        InvalidateVisual();
    }
    public void FinishPolyline() { if (Tool == Tool.Polyline && pts.Count >= 2) Add(new PolylineEnt { Points = pts.ToList() }); pts.Clear(); InvalidateVisual(); }
    protected override void OnMouseUp(MouseButtonEventArgs e) { panStart = null; if (dragFrom != null) { dragFrom = null; Changed?.Invoke(); } ReleaseMouseCapture(); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var sp = e.GetPosition(this);
        if (panStart != null) { Offset = new Point(panOffset0.X + sp.X - panStart.Value.X, panOffset0.Y + sp.Y - panStart.Value.Y); InvalidateVisual(); return; }
        var p = Snap(sp, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); cursor = p;
        if (dragFrom != null && Selected != null) { Selected.Move(p - dragFrom.Value); dragFrom = p; }
        string extra = pts.Count > 0 ? $"   Δ = {P2.Dist(pts[^1], p):0.###}  угол = {Math.Atan2(p.Y - pts[^1].Y, p.X - pts[^1].X) * 180 / Math.PI:0.#}°" : "";
        Status?.Invoke($"X = {p.X:0.###}   Y = {p.Y:0.###}{extra}{(snapMark != null ? "   [привязка]" : "")}");
        InvalidateVisual();
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var sp = e.GetPosition(this); var w = W(sp); double k = e.Delta > 0 ? 1.2 : 1 / 1.2; Scale *= k;
        Offset = new Point(sp.X - w.X * Scale, sp.Y + w.Y * Scale); InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (Tool == Tool.Polyline) FinishPolyline(); else CancelTool(); Selected = null; }
        else if (e.Key == Key.Delete) DeleteSelected();
        else if (e.Key == Key.Enter) FinishPolyline();
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) Undo();
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) Redo();
        InvalidateVisual();
    }

    static readonly Dictionary<string, Pen> pens = new();
    static Pen PenFor(string hex, double th = 1.2)
    {
        var key = hex + th; if (pens.TryGetValue(key, out var p)) return p;
        Color c; try { c = (Color)ColorConverter.ConvertFromString(hex); } catch { c = Colors.Black; }
        p = new Pen(new SolidColorBrush(c), th); p.Freeze(); pens[key] = p; return p;
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (ShowGrid) DrawGrid(dc);
        foreach (var e in Drawing.Entities)
        {
            var ly = Drawing.GetLayer(e.Layer); if (!ly.Visible) continue;
            DrawEntity(dc, e, e == Selected ? PenFor("#FF6F00", 2.5) : PenFor(ly.Color), ly.Color);
        }
        // предпросмотр
        if (cursor != null && pts.Count > 0)
        {
            var pv = PenFor("#00897B", 1); var c = cursor.Value;
            switch (Tool)
            {
                case Tool.Line: case Tool.Polyline: for (int i = 0; i + 1 < pts.Count; i++) dc.DrawLine(pv, S(pts[i]), S(pts[i + 1])); dc.DrawLine(pv, S(pts[^1]), S(c)); break;
                case Tool.Rect: DrawEntity(dc, new PolylineEnt { Closed = true, Points = { pts[0], new P2(c.X, pts[0].Y), c, new P2(pts[0].X, c.Y) } }, pv, "#00897B"); break;
                case Tool.Circle: DrawEntity(dc, new CircleEnt { C = pts[0], R = P2.Dist(pts[0], c) }, pv, "#00897B"); break;
                case Tool.Arc: if (pts.Count == 2) { var a = ArcEnt.Through(pts[0], pts[1], c); if (a != null) DrawEntity(dc, a, pv, "#00897B"); } else dc.DrawLine(pv, S(pts[0]), S(c)); break;
                case Tool.Dim:
                    if (pts.Count == 1) dc.DrawLine(pv, S(pts[0]), S(c));
                    else { var d = new DimEnt { A = pts[0], B = pts[1], TextHeight = 14 / Scale }; d.Offset = (c.X - d.A.X) * d.Normal.X + (c.Y - d.A.Y) * d.Normal.Y; DrawEntity(dc, d, pv, "#00897B"); }
                    break;
            }
        }
        if (snapMark != null) { var s = S(snapMark.Value); dc.DrawRectangle(null, PenFor("#E91E63", 1.5), new Rect(s.X - 5, s.Y - 5, 10, 10)); }
        if (cursor != null) { var s = S(cursor.Value); var cp = PenFor("#90A4AE", 0.7); dc.DrawLine(cp, new Point(s.X - 12, s.Y), new Point(s.X + 12, s.Y)); dc.DrawLine(cp, new Point(s.X, s.Y - 12), new Point(s.X, s.Y + 12)); }
    }
    void DrawGrid(DrawingContext dc)
    {
        double step = GridStep; while (step * Scale < 8) step *= 5; while (step * Scale > 120) step /= 5;
        var a = W(new Point(0, ActualHeight)); var b = W(new Point(ActualWidth, 0));
        var pen = PenFor("#ECEFF1", 1);
        for (double x = Math.Floor(a.X / step) * step; x <= b.X; x += step) dc.DrawLine(pen, S(new P2(x, a.Y)), S(new P2(x, b.Y)));
        for (double y = Math.Floor(a.Y / step) * step; y <= b.Y; y += step) dc.DrawLine(pen, S(new P2(a.X, y)), S(new P2(b.X, y)));
        var ax = PenFor("#B0BEC5", 1); dc.DrawLine(ax, S(new P2(a.X, 0)), S(new P2(b.X, 0))); dc.DrawLine(ax, S(new P2(0, a.Y)), S(new P2(0, b.Y)));
    }
    void DrawEntity(DrawingContext dc, Entity e, Pen pen, string color)
    {
        switch (e)
        {
            case LineEnt l: dc.DrawLine(pen, S(l.A), S(l.B)); break;
            case CircleEnt c: dc.DrawEllipse(null, pen, S(c.C), c.R * Scale, c.R * Scale); break;
            case ArcEnt a:
                {
                    var g = new StreamGeometry(); using (var ctx = g.Open())
                    { ctx.BeginFigure(S(a.At(a.Start)), false, false); ctx.ArcTo(S(a.At(a.End)), new Size(a.R * Scale, a.R * Scale), 0, a.Sweep > 180, SweepDirection.Counterclockwise, true, false); }
                    dc.DrawGeometry(null, pen, g); break;
                }
            case PolylineEnt p when p.Points.Count > 1:
                {
                    var g = new StreamGeometry(); using (var ctx = g.Open()) { ctx.BeginFigure(S(p.Points[0]), false, p.Closed); ctx.PolyLineTo(p.Points.Skip(1).Select(S).ToList(), true, false); }
                    dc.DrawGeometry(null, pen, g); break;
                }
            case TextEnt t: DrawText(dc, t.Text, t.P, t.Height, pen.Brush, 0); break;
            case DimEnt d:
                {
                    var a2 = d.A2; var b2 = d.B2; dc.DrawLine(pen, S(d.A), S(a2)); dc.DrawLine(pen, S(d.B), S(b2)); dc.DrawLine(pen, S(a2), S(b2));
                    var dir = b2 - a2; double len = dir.Len; if (len < 1e-12) break; var u = dir * (1 / len); var nrm = d.Normal; double ar = d.TextHeight * 0.8;
                    foreach (var (tip, sgn) in new[] { (a2, 1.0), (b2, -1.0) })
                    { var back = tip + u * (ar * sgn); dc.DrawLine(pen, S(tip), S(back + nrm * (ar * 0.3))); dc.DrawLine(pen, S(tip), S(back - nrm * (ar * 0.3))); }
                    double ang = Math.Atan2(u.Y, u.X) * 180 / Math.PI; if (ang > 90 || ang < -90) ang += 180;
                    var mid = (a2 + b2) * 0.5 + nrm * (d.TextHeight * 0.2);
                    DrawText(dc, d.Label, mid, d.TextHeight, pen.Brush, ang, true); break;
                }
        }
    }
    void DrawText(DrawingContext dc, string s, P2 p, double h, Brush br, double angDeg, bool center = false)
    {
        double px = h * Scale; if (px < 3) return;
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), px, br, 1.0);
        var sp = S(p);
        dc.PushTransform(new RotateTransform(-angDeg, sp.X, sp.Y));
        dc.DrawText(ft, new Point(sp.X - (center ? ft.Width / 2 : 0), sp.Y - ft.Height * 0.85));
        dc.Pop();
    }

    public void SavePng(string path, double dpiScale = 2)
    {
        int w = (int)(ActualWidth * dpiScale), h = (int)(ActualHeight * dpiScale);
        var rtb = new RenderTargetBitmap(w, h, 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
        bool g = ShowGrid; ShowGrid = false; var cur = cursor; cursor = null; var sm = snapMark; snapMark = null; InvalidateVisual(); UpdateLayout();
        var dv = new DrawingVisual(); using (var dc = dv.RenderOpen()) OnRender(dc);
        rtb.Render(dv); ShowGrid = g; cursor = cur; snapMark = sm; InvalidateVisual();
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb)); using var fs = File.Create(path); enc.Save(fs);
    }
}

public class CadPage : ContentControl
{
    public CadCanvas Canvas = new();
    readonly TextBlock status = new() { Margin = new Thickness(6, 2, 6, 2) };
    readonly ListBox layers = new() { Height = 220 };
    readonly Dictionary<Tool, ToggleButton> toolButtons = new();
    public CadPage()
    {
        Canvas.Status += s => status.Text = s;
        Canvas.Changed += RefreshLayers;
        var tb = new WrapPanel { Margin = new Thickness(4) };
        foreach (var (t, label, tip) in new[] { (Tool.Select, "⬚ Выбор", "Выбрать объект; перетащить — переместить; Delete — удалить"), (Tool.Pan, "✋ Сдвиг", "Сдвиг вида (также средняя/правая кнопка мыши)"),
            (Tool.Line, "╱ Отрезок", "Последовательные отрезки; Esc — завершить"), (Tool.Polyline, "〰 Полилиния", "Двойной щелчок / Enter / правая кнопка — завершить"),
            (Tool.Rect, "▭ Прямоуг.", "Два угла"), (Tool.Circle, "◯ Окружность", "Центр и точка на окружности"), (Tool.Arc, "◠ Дуга", "Три точки: начало, точка на дуге, конец"),
            (Tool.Dim, "↔ Размер", "Две точки и положение размерной линии"), (Tool.Text, "A Текст", "Точка вставки текста") })
        {
            var b = new ToggleButton { Content = label, ToolTip = tip, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 4) };
            var tt = t; b.Click += (_, _) => SetTool(tt); toolButtons[t] = b; tb.Children.Add(b);
        }
        tb.Children.Add(Chk("Привязка к точкам", true, v => Canvas.SnapObjects = v));
        tb.Children.Add(Chk("К сетке", false, v => Canvas.SnapGrid = v));
        tb.Children.Add(Chk("Орто (Shift)", false, v => Canvas.Ortho = v));
        tb.Children.Add(Chk("Сетка", true, v => { Canvas.ShowGrid = v; Canvas.InvalidateVisual(); }));
        var gs = new TextBox { Text = "1", Width = 50, Margin = new Thickness(4, 0, 4, 4), ToolTip = "Шаг сетки" }; gs.TextChanged += (_, _) => { if (double.TryParse(gs.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0) { Canvas.GridStep = v; Canvas.InvalidateVisual(); } };
        tb.Children.Add(new TextBlock { Text = "шаг:", VerticalAlignment = VerticalAlignment.Center }); tb.Children.Add(gs);
        SetTool(Tool.Select);
        var side = V(H("Чертёж", 15),
            B("Теоретический чертёж из корпуса", GenLines, true),
            Row(B("↶ Отменить", Canvas.Undo), B("↷ Вернуть", Canvas.Redo)), Row(B("Показать всё", Canvas.ZoomExtents), B("Удалить", Canvas.DeleteSelected)),
            B("Очистить лист", () => { Canvas.Snapshot(); Canvas.Drawing.Entities.Clear(); Canvas.InvalidateVisual(); }),
            H("Файлы", 15), Row(B("Открыть…", Open), B("Сохранить…", SaveJson)), Row(B("Экспорт DXF…", () => Export("dxf")), B("PNG…", () => Export("png")), B("PDF…", () => Export("pdf"))),
            H("Слои", 15), T("Галочка — видимость; выбранный слой — текущий для новых объектов.", 11), layers,
            Row(B("+ Слой", AddLayer), B("Цвет", ColorLayer), B("Объект → слой", MoveToLayer)));
        layers.SelectionChanged += (_, _) => { if (layers.SelectedItem is ListBoxItem li && li.Tag is Layer l) Canvas.CurrentLayer = l.Name; };
        var border = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = Canvas };
        var dp = new DockPanel(); DockPanel.SetDock(tb, Dock.Top); DockPanel.SetDock(status, Dock.Bottom); dp.Children.Add(tb); dp.Children.Add(status); dp.Children.Add(border);
        Content = Split(Scroll(side), dp, 285);
        RefreshLayers();
        status.Text = "Колесо мыши — масштаб, средняя/правая кнопка — сдвиг, Shift — орто, Esc — отмена, Ctrl+Z — отменить.";
    }
    static CheckBox Chk(string s, bool v, Action<bool> a) { var c = new CheckBox { Content = s, IsChecked = v, Margin = new Thickness(8, 4, 4, 4), VerticalAlignment = VerticalAlignment.Center }; c.Checked += (_, _) => a(true); c.Unchecked += (_, _) => a(false); return c; }
    void SetTool(Tool t) { Canvas.FinishPolyline(); Canvas.Tool = t; foreach (var kv in toolButtons) kv.Value.IsChecked = kv.Key == t; Canvas.Focus(); }
    void RefreshLayers()
    {
        layers.Items.Clear();
        foreach (var l in Canvas.Drawing.Layers)
        {
            var cb = new CheckBox { IsChecked = l.Visible, VerticalAlignment = VerticalAlignment.Center }; var ll = l;
            cb.Checked += (_, _) => { ll.Visible = true; Canvas.InvalidateVisual(); }; cb.Unchecked += (_, _) => { ll.Visible = false; Canvas.InvalidateVisual(); };
            Color c; try { c = (Color)ColorConverter.ConvertFromString(l.Color); } catch { c = Colors.Black; }
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Children = { cb, new Border { Width = 14, Height = 14, Background = new SolidColorBrush(c), Margin = new Thickness(4, 0, 4, 0) }, new TextBlock { Text = $"{l.Name} ({Canvas.Drawing.Entities.Count(e => e.Layer == l.Name)})" } } };
            var item = new ListBoxItem { Content = sp, Tag = l }; layers.Items.Add(item);
            if (l.Name == Canvas.CurrentLayer) layers.SelectedItem = item;
        }
    }
    void AddLayer() { var n = Ask("Новый слой", "Имя слоя:", "Слой " + (Canvas.Drawing.Layers.Count)); if (string.IsNullOrWhiteSpace(n)) return; Canvas.Drawing.GetLayer(n); Canvas.CurrentLayer = n; RefreshLayers(); }
    void ColorLayer()
    {
        var l = Canvas.Drawing.GetLayer(Canvas.CurrentLayer);
        var s = Ask("Цвет слоя", "Цвет в формате #RRGGBB (например #C62828):", l.Color); if (string.IsNullOrWhiteSpace(s)) return;
        ColorConverter.ConvertFromString(s); l.Color = s; RefreshLayers(); Canvas.InvalidateVisual();
    }
    void MoveToLayer() { if (Canvas.Selected == null) { MessageBox.Show("Сначала выберите объект инструментом «Выбор»."); return; } Canvas.Snapshot(); Canvas.Selected.Layer = Canvas.CurrentLayer; RefreshLayers(); Canvas.InvalidateVisual(); }
    void GenLines()
    {
        if (Canvas.Drawing.Entities.Count > 0 && MessageBox.Show("Заменить текущий чертёж теоретическим чертежом корпуса?", "Чертёж", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Canvas.Snapshot();
        Canvas.SetDrawing(LinesPlan.Generate(App.Hull, App.DesignDraft));
    }
    void Open()
    {
        var d = new OpenFileDialog { Filter = "Чертёж (*.dxf;*.sdraw)|*.dxf;*.sdraw|Все файлы|*.*" }; if (d.ShowDialog() != true) return;
        Canvas.SetDrawing(d.FileName.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) ? DrawingExport.LoadDxf(d.FileName) : Drawing.FromJson(File.ReadAllText(d.FileName)));
    }
    void SaveJson() { var d = new SaveFileDialog { Filter = "Чертёж Судно-Проект (*.sdraw)|*.sdraw", FileName = "chertezh.sdraw" }; if (d.ShowDialog() == true) File.WriteAllText(d.FileName, Canvas.Drawing.ToJson()); }
    void Export(string kind)
    {
        var d = new SaveFileDialog { Filter = kind switch { "dxf" => "AutoCAD DXF|*.dxf", "png" => "PNG|*.png", _ => "PDF|*.pdf" }, FileName = "chertezh." + kind };
        if (d.ShowDialog() != true) return;
        if (kind == "dxf") DrawingExport.SaveDxf(Canvas.Drawing, d.FileName);
        else if (kind == "png") Canvas.SavePng(d.FileName);
        else DrawingExport.SavePdf(Canvas.Drawing, d.FileName, App.Project.Name);
        status.Text = "Сохранено: " + d.FileName;
    }
}
