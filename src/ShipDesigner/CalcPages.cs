using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using ShipDesigner.Core;
using static ShipDesigner.Ui;
using FontWeights = System.Windows.FontWeights;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
namespace ShipDesigner;

public class HullPage : ContentControl
{
    readonly ComboBox kind = Combo("Параметрический (L, B, T, Cb, Cm)", "Корпус Уигли (тест)", "Понтон-коробка (тест)", "Таблица плазовых ординат (CSV)");
    readonly Num L = new("Длина L, м", 80), B = new("Ширина B, м", 12), T = new("Осадка T, м", 4.5), D = new("Высота борта D, м", 7), Cb = new("Коэф. общей полноты Cb", 0.65), Cm = new("Коэф. полноты мидель-шп. Cm", 0.98),
        Shift = new("Смещение полноты в нос", 0, "−0.3…0.3: + делает нос полнее, − корму"), Rho = new("Плотность воды, т/м³", 1.025);
    readonly TextBox name = new() { FontSize = 13, Width = 260 };
    readonly TextBox csv = new() { FontFamily = new FontFamily("Consolas"), FontSize = 12, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 230 };
    readonly PlotView bodyPlan = new(), halfBreadth = new();
    public HullPage()
    {
        Load();
        var left = V(H("Корпус судна"), Row(T("Название проекта:"), name), kind, L.Element, B.Element, T.Element, D.Element, Cb.Element, Cm.Element, Shift.Element, Rho.Element,
            Row(B("Применить и пересчитать", Apply, true)),
            H("Плазовые ординаты", 15), T("Первая строка: x; z₁; z₂ … (высоты ватерлиний, м). Далее: x шпангоута; полуширины y на каждой ватерлинии. x — от кормового перпендикуляра."),
            csv, Row(B("Импорт CSV…", ImportCsv), B("Экспорт CSV…", ExportCsv), B("Ординаты из текущего корпуса", () => { csv.Text = OffsetsHull.FromHull(App.Hull).ToCsv(); kind.SelectedIndex = 3; })));
        var right = Rows(bodyPlan, halfBreadth);
        Content = Split(Scroll(left), right, 430);
        App.HullChanged += () => { Load(); Draw(); };
        Draw();
    }
    void Load()
    {
        var h = App.Project.Hull; name.Text = App.Project.Name;
        kind.SelectedIndex = h.Kind switch { "wigley" => 1, "box" => 2, "offsets" => 3, _ => 0 };
        L.Value = h.L; B.Value = h.B; T.Value = h.T; D.Value = h.D; Cb.Value = h.Cb; Cm.Value = h.Cm; Shift.Value = h.LcbShift; Rho.Value = App.Project.Rho;
        csv.Text = h.OffsetsCsv ?? OffsetsHull.FromHull(App.Hull ?? h.Build()).ToCsv();
    }
    void Apply()
    {
        var h = App.Project.Hull;
        h.Kind = kind.SelectedIndex switch { 1 => "wigley", 2 => "box", 3 => "offsets", _ => "parametric" };
        h.L = L.Value; h.B = B.Value; h.T = T.Value; h.D = D.Value; h.Cb = Cb.Value; h.Cm = Cm.Value; h.LcbShift = Shift.Value;
        if (h.Kind == "parametric" && (h.Cb <= 0.3 || h.Cb >= h.Cm || h.Cm >= 1)) throw new ArgumentException("Нужно 0.3 < Cb < Cm < 1");
        if (h.T > h.D) throw new ArgumentException("Осадка больше высоты борта");
        if (h.Kind == "offsets") { OffsetsHull.ParseCsv(csv.Text); h.OffsetsCsv = csv.Text; }
        App.Project.Name = name.Text; App.Project.Rho = Rho.Value;
        App.Project.Load = null; App.Rebuild();
    }
    void ImportCsv()
    {
        var d = new OpenFileDialog { Filter = "CSV/TXT|*.csv;*.txt|Все файлы|*.*" }; if (d.ShowDialog() != true) return;
        var t = File.ReadAllText(d.FileName); OffsetsHull.ParseCsv(t); csv.Text = t; kind.SelectedIndex = 3;
    }
    void ExportCsv() { var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "ordinaty.csv" }; if (d.ShowDialog() == true) File.WriteAllText(d.FileName, csv.Text); }
    void Draw()
    {
        var h = App.Hull; int st = 20;
        var m = Plot("Корпус (шпангоуты): слева корма, справа нос", "y, м", "z, м");
        for (int i = 0; i <= st; i++)
        {
            double x = h.L * i / st, s = x < h.L / 2 ? -1 : 1;
            var ser = Line(null, Enumerable.Range(0, 41).Select(j => { double z = h.D * j / 40; return (s * h.HalfBreadth(x, z), z); }));
            ser.Color = OxyColor.FromRgb(21, 101, 192); ser.StrokeThickness = 1.2; m.Series.Add(ser);
        }
        var wl = Line($"КВЛ {App.DesignDraft:0.##} м", new[] { (-h.B / 2, App.DesignDraft), (h.B / 2, App.DesignDraft) }); wl.Color = OxyColors.Green; m.Series.Add(wl);
        Equal(m); bodyPlan.Model = m;
        var m2 = Plot("Полуширота (ватерлинии)", "x от КП, м", "y, м");
        for (int k = 1; k <= 5; k++)
        {
            double z = App.DesignDraft * k / 5;
            m2.Series.Add(Line($"ВЛ {z:0.##}", Enumerable.Range(0, 101).Select(j => { double x = h.L * j / 100; return (x, h.HalfBreadth(x, z)); })));
        }
        halfBreadth.Model = m2;
    }
    static void Equal(PlotModel m) { m.PlotType = PlotType.Cartesian; }
}

public class HydroPage : ContentControl
{
    readonly Num tmin = new("Осадка от, м", 0.5), tmax = new("Осадка до, м", 6), steps = new("Шагов", 12);
    readonly ContentControl table = new(); readonly PlotView p1 = new(), p2 = new();
    readonly ComboBox curves = Combo("Δ, Awp, TPC, MCT", "KB, BMt, KMt, LCB, LCF", "Коэффициенты Cb, Cm, Cp, Cw");
    List<HydroRow> rows;
    public HydroPage()
    {
        curves.SelectionChanged += (_, _) => DrawCurves();
        var top = V(H("Гидростатические кривые (на ровный киль)"), Row(tmin.Element, tmax.Element, steps.Element, B("Рассчитать", Calc, true), B("Экспорт CSV…", Export)),
            T("V — объёмное водоизмещение, Δ — массовое, LCB/LCF — абсциссы ЦВ и ЦТ ватерлинии от КП, KB — аппликата ЦВ, BMt/BMl — поперечный/продольный метацентрические радиусы, TPC — т на 1 см осадки, MCT — момент, дифферентующий на 1 см."));
        var g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition());
        var rp = new DockPanel(); DockPanel.SetDock(curves, Dock.Top); rp.Children.Add(curves); rp.Children.Add(p1);
        var plots = Split(table, rp, 640);
        Grid.SetRow(plots, 1); g.Children.Add(top); g.Children.Add(plots);
        Content = g;
        App.HullChanged += Reset; Reset();
    }
    void Reset() { tmin.Value = Math.Round(App.DesignDraft * 0.1, 2); tmax.Value = Math.Round(Math.Min(App.Hull.D, App.DesignDraft * 1.3), 2); Calc(); }
    void Calc()
    {
        rows = App.Calc.Table(tmin.Value, tmax.Value, (int)steps.Value);
        var t = new DataTable();
        string[] cols = { "T, м", "V, м³", "Δ, т", "LCB, м", "KB, м", "Awp, м²", "LCF, м", "BMt, м", "BMl, м", "KMt, м", "TPC, т на см", "MCT, т·м на см", "Cb", "Cm", "Cp", "Cw", "S, м²" };
        foreach (var c in cols) t.Columns.Add(c, typeof(string));
        foreach (var r in rows) t.Rows.Add(Fmt(r.T, "0.00"), Fmt(r.Volume, "0.0"), Fmt(r.Displacement, "0.0"), Fmt(r.LCB, "0.000"), Fmt(r.KB, "0.000"), Fmt(r.Awp, "0.0"), Fmt(r.LCF, "0.000"), Fmt(r.BMt, "0.000"), Fmt(r.BMl, "0.0"), Fmt(r.KMt, "0.000"), Fmt(r.TPC, "0.000"), Fmt(r.MCT, "0.00"), Fmt(r.Cb, "0.000"), Fmt(r.Cm, "0.000"), Fmt(r.Cp, "0.000"), Fmt(r.Cw, "0.000"), Fmt(r.WettedSurface, "0.0"));
        table.Content = Table(t);
        DrawCurves();
    }
    void DrawCurves()
    {
        if (rows == null) return;
        PlotModel m;
        switch (curves.SelectedIndex)
        {
            case 1: m = Plot("Остойчивость формы", "м", "T, м");
                foreach (var (n, f) in new (string, Func<HydroRow, double>)[] { ("KB", r => r.KB), ("BMt", r => r.BMt), ("KMt", r => r.KMt), ("LCB", r => r.LCB), ("LCF", r => r.LCF) })
                    m.Series.Add(Line(n, rows.Select(r => (f(r), r.T)), true)); break;
            case 2: m = Plot("Коэффициенты полноты", "", "T, м");
                foreach (var (n, f) in new (string, Func<HydroRow, double>)[] { ("Cb", r => r.Cb), ("Cm", r => r.Cm), ("Cp", r => r.Cp), ("Cw", r => r.Cw) })
                    m.Series.Add(Line(n, rows.Select(r => (f(r), r.T)), true)); break;
            default:
                m = Plot("Δ, Awp, TPC, MCT (нормировано на максимум)", "доля от максимума", "T, м");
                foreach (var (n, f) in new (string, Func<HydroRow, double>)[] { ("Δ", r => r.Displacement), ("Awp", r => r.Awp), ("TPC", r => r.TPC), ("MCT", r => r.MCT) })
                { double mx = rows.Max(f); m.Series.Add(Line($"{n} (макс {mx:0.##})", rows.Select(r => (f(r) / mx, r.T)), true)); }
                break;
        }
        p1.Model = m;
    }
    void Export()
    {
        var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "gidrostatika.csv" }; if (d.ShowDialog() != true) return;
        var sb = new System.Text.StringBuilder("T;V;Disp;LCB;KB;Awp;LCF;BMt;BMl;KMt;TPC;MCT;Cb;Cm;Cp;Cw;S\n");
        foreach (var r in rows) sb.AppendLine(string.Join(";", new[] { r.T, r.Volume, r.Displacement, r.LCB, r.KB, r.Awp, r.LCF, r.BMt, r.BMl, r.KMt, r.TPC, r.MCT, r.Cb, r.Cm, r.Cp, r.Cw, r.WettedSurface }.Select(v => Fmt(v))));
        File.WriteAllText(d.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));
    }
}

public class BonjeanPage : ContentControl
{
    readonly PlotView pv = new(), pa = new();
    public BonjeanPage()
    {
        Content = Rows(pv, pa, 1.3, 1);
        App.HullChanged += Draw; Draw();
    }
    void Draw()
    {
        var h = App.Hull; var hc = App.Calc; int st = 10;
        var m = Plot("Кривые Бонжана: площадь шпангоута ω(z) (смещены по станциям)", "x от КП, м  (+ площадь ω·k)", "z, м");
        double maxA = 0; for (int i = 0; i <= st; i++) maxA = Math.Max(maxA, hc.SectionAreaAt(h.L * i / st, h.D));
        double k = h.L / st * 0.9 / Math.Max(1e-9, maxA);
        for (int i = 0; i <= st; i++)
        {
            double x = h.L * i / st;
            var s = Line(i == 0 ? "ω(z)" : null, Enumerable.Range(0, 31).Select(j => { double z = h.D * j / 30; return (x + hc.SectionAreaAt(x, z) * k, z); }));
            s.Color = OxyColor.FromRgb(21, 101, 192); m.Series.Add(s);
            var b = Line(null, new[] { (x, 0.0), (x, h.D) }); b.Color = OxyColors.LightGray; b.StrokeThickness = 1; m.Series.Add(b);
        }
        m.Subtitle = $"Масштаб площадей: 1 м по оси x = {1 / k:0.##} м²";
        pv.Model = m;
        var m2 = Plot("Строевая по шпангоутам ω(x)", "x от КП, м", "ω, м²");
        foreach (var f in new[] { 0.5, 0.75, 1.0, 1.25 })
        {
            double t = App.DesignDraft * f; if (t > h.D) continue;
            m2.Series.Add(Line($"T = {t:0.##} м", Enumerable.Range(0, 81).Select(j => { double x = h.L * j / 80; return (x, hc.SectionAreaAt(x, t)); })));
        }
        pa.Model = m2;
    }
}

public class StabilityPage : ContentControl
{
    readonly DataGrid grid = new() { AutoGenerateColumns = false, FontSize = 12, CanUserAddRows = true, Height = 220 };
    readonly System.Collections.ObjectModel.ObservableCollection<LoadItem> items = new();
    readonly Num flood = new("Угол заливания, °", 60), maxA = new("Расчёт GZ до угла, °", 80);
    readonly TextBlock summary = T(); readonly StackPanel crit = new();
    readonly PlotView gz = new(), cross = new();
    public StabilityPage()
    {
        void Col(string h, string p, double w = 80) => grid.Columns.Add(new DataGridTextColumn { Header = h, Binding = new System.Windows.Data.Binding(p) { ConverterCulture = System.Globalization.CultureInfo.InvariantCulture }, Width = w });
        Col("Статья нагрузки", "Name", 150); Col("Масса, т", "Mass"); Col("LCG от КП, м", "LCG"); Col("VCG (KG), м", "VCG"); Col("TCG, м", "TCG", 60); Col("Мом. своб. пов., т·м", "FSM", 110);
        grid.ItemsSource = items;
        var left = V(H("Нагрузка"), grid, Row(B("Типовая нагрузка", () => { App.Project.Load = LoadCase.Default(App.Hull, App.Design.Displacement); Fill(); }), B("Удалить строку", () => { if (grid.SelectedItem is LoadItem li) items.Remove(li); })),
            flood.Element, maxA.Element, Row(B("Рассчитать посадку и остойчивость", Calc, true), B("Пантокарены", Cross)), summary, H("Критерии ИМО (IS Code 2008, часть A 2.2)", 15), crit);
        Content = Split(Scroll(left), Rows(gz, cross, 1.4, 1), 560);
        App.HullChanged += Fill; Fill();
    }
    void Fill() { items.Clear(); foreach (var i in App.Project.Load.Items) items.Add(i); App.Project.FloodAngle = App.Project.FloodAngle; flood.Value = App.Project.FloodAngle; summary.Text = ""; crit.Children.Clear(); gz.Model = null; cross.Model = null; }
    void Calc()
    {
        grid.CommitEdit(DataGridEditingUnit.Row, true);
        App.Project.Load.Items = items.Where(i => i.Mass != 0).ToList();
        App.Project.FloodAngle = flood.Value;
        var r = StabilityCalc.Analyze(App.Calc, App.Project.Load, flood.Value, maxA.Value, 5);
        App.Stab = r; var e = r.Eq;
        summary.Text = $"Δ = {e.Displacement:0.#} т,  LCG = {e.LCG:0.###} м,  KG = {e.KG:0.###} м (с поправкой {e.KGcorr:0.###} м)\n" +
            $"Tср = {e.Tmean:0.###} м,  дифферент = {e.Trim:+0.###;-0.###} м ({(e.Trim >= 0 ? "на корму" : "на нос")}),  Tк = {e.Ta:0.###} м,  Tн = {e.Tf:0.###} м\n" +
            $"KMt = {e.KMt:0.###} м,  GM₀ = {e.GM0:0.###} м,  GM испр. = {e.GM:0.###} м,  крен = {e.HeelDeg:0.##}°\n" +
            $"GZmax = {r.MaxGz:0.###} м при {r.AngleMaxGz:0}°,  угол заката {(r.VanishingAngle >= maxA.Value - 1e-6 ? "> " + maxA.Value.ToString("0") : "≈ " + r.VanishingAngle.ToString("0.#"))}°,  макс. восст. момент = {e.Displacement * r.MaxGz:0} т·м";
        crit.Children.Clear();
        foreach (var c in r.Criteria) crit.Children.Add(new TextBlock { Text = $"{(c.Pass ? "✔" : "✘")} {c.Name}: {c.Actual:0.###} {c.Unit} (треб. ≥ {c.Required} {c.Unit})", Foreground = c.Pass ? Good : Bad, FontSize = 13, Margin = new Thickness(0, 2, 0, 2) });
        crit.Children.Add(new TextBlock { Text = r.AllPass ? "Все общие критерии выполнены." : "Есть невыполненные критерии — см. ИИ-помощник за советами.", FontWeight = FontWeights.SemiBold, Foreground = r.AllPass ? Good : Bad, Margin = new Thickness(0, 6, 0, 0) });
        var m = Plot("Диаграмма статической остойчивости", "Угол крена, °", "Плечо, м");
        m.Series.Add(Line("GZ", r.Curve.Select(p => (p.Angle, p.GZ)), true));
        m.Series.Add(Line("KN", r.Curve.Select(p => (p.Angle, p.KN)))); ((LineSeries)m.Series[^1]).LineStyle = LineStyle.Dash;
        double rad = 180 / Math.PI; m.Series.Add(Line("GM (касательная)", new[] { (0.0, 0.0), (rad, e.GM) })); ((LineSeries)m.Series[^1]).LineStyle = LineStyle.Dot;
        m.Annotations.Add(new OxyPlot.Annotations.LineAnnotation { Type = OxyPlot.Annotations.LineAnnotationType.Vertical, X = flood.Value, Color = OxyColors.Red, Text = "угол заливания" });
        m.Axes[0].Minimum = 0; m.Axes[0].Maximum = maxA.Value;
        m.Axes[1].Minimum = Math.Min(-0.1, r.Curve.Min(p => p.GZ)); m.Axes[1].Maximum = Math.Max(r.MaxGz * 1.4, 0.3);
        gz.Model = m;
    }
    void Cross()
    {
        double d0 = App.Calc.Upright(App.DesignDraft * 0.4).Displacement, d1 = App.Calc.Upright(Math.Min(App.Hull.D * 0.95, App.DesignDraft * 1.2)).Displacement;
        var angles = new double[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        var disp = Enumerable.Range(0, 7).Select(i => d0 + (d1 - d0) * i / 6).ToArray();
        var kn = App.Calc.CrossCurves(disp, angles);
        var m = Plot("Пантокарены KN(Δ)", "Δ, т", "KN, м");
        for (int j = 0; j < angles.Length; j++) m.Series.Add(Line($"{angles[j]}°", disp.Select((d, i) => (d, kn[i, j])), true));
        m.Legends[0].LegendPosition = OxyPlot.Legends.LegendPosition.RightTop;
        cross.Model = m;
    }
}

public class ResistancePage : ContentControl
{
    readonly Num v = new("Расчётная скорость, уз", 14), vmax = new("Диапазон до, уз", 20), eta = new("Пропульсивный КПД ηD", 0.65), etas = new("КПД валопровода ηS", 0.98),
        abt = new("Площадь бульба ABT, м²", 0), hb = new("Центр бульба hB, м", 0), at = new("Площадь транца AT, м²", 0), cst = new("Форма кормы Cstern", 0, "−25 бульбовая корма, −10 V-образная, 0 норм., 10 U-образная"),
        sapp = new("Площадь выступ. частей, м²", 0), margin = new("Морской запас, %", 15);
    readonly TextBlock res = T(); readonly PlotView p1 = new(), p2 = new();
    public ResistancePage()
    {
        var left = V(H("Сопротивление: Holtrop–Mennen"), T("Главные размерения, ∇, Cb, Cm, Cwp, lcb и смоченная поверхность берутся из гидростатики на расчётной осадке."),
            v.Element, vmax.Element, eta.Element, etas.Element, margin.Element, abt.Element, hb.Element, at.Element, cst.Element, sapp.Element, Row(B("Рассчитать", Calc, true)), res,
            T("Метод применим примерно при Fn < 0.45, 0.55 < Cp < 0.85, 3.9 < L/B < 15. Результат — предварительная оценка (точность ±10–15 %)."));
        Content = Split(Scroll(left), Rows(p1, p2), 420);
        App.HullChanged += () => { res.Text = ""; p1.Model = null; p2.Model = null; };
    }
    void Calc()
    {
        var hi = Holtrop.FromHydro(App.Design, App.Hull.L);
        hi.ABT = abt.Value; hi.hB = hb.Value; hi.AT = at.Value; hi.Cstern = cst.Value; hi.Sapp = sapp.Value; hi.Rho = App.Project.Rho * 1000;
        var pts = new List<ResistancePoint>();
        for (double s = 1; s <= vmax.Value + 1e-9; s += 0.5) pts.Add(Holtrop.Calc(hi, s, eta.Value, etas.Value));
        var p = Holtrop.Calc(hi, v.Value, eta.Value, etas.Value); App.Resist = p; App.Project.SpeedKn = v.Value;
        double k = Holtrop.FormFactor(hi, out _);
        double mg = 1 + margin.Value / 100;
        res.Text = $"L={hi.L:0.##} м  B={hi.B:0.##}  T={hi.T:0.##}  ∇={hi.Volume:0} м³  Cb={hi.Cb:0.###}  Cp={hi.Cp:0.###}  lcb={hi.LcbPercent:0.##}%\nS={hi.S:0.#} м²  1+k₁={k:0.###}\n\n" +
            $"При {p.SpeedKn} уз: Fn = {p.Fn:0.###},  Re = {p.Re:0.##E+0}\nRF = {p.RF / 1000:0.#} кН,  RW = {p.RW / 1000:0.#} кН,  RA = {p.RA / 1000:0.#} кН,  RB = {p.RB / 1000:0.##},  RTR = {p.RTR / 1000:0.##},  RAPP = {p.RApp / 1000:0.##}\n" +
            $"RT = {p.RT / 1000:0.#} кН\nPE = {p.PE / 1000:0} кВт,  PD = {p.PD / 1000:0} кВт,  PB = {p.PB / 1000:0} кВт\nС морским запасом {margin.Value:0}%: PB ≈ {p.PB * mg / 1000:0} кВт ({p.PB * mg / 735.5:0} л.с.)" +
            (p.Fn > 0.45 ? "\n⚠ Fn > 0.45 — за пределами применимости метода." : "");
        var m = Plot("Составляющие сопротивления", "V, уз", "R, кН");
        m.Series.Add(Line("RT", pts.Select(q => (q.SpeedKn, q.RT / 1000))));
        m.Series.Add(Line("RF·(1+k₁)", pts.Select(q => (q.SpeedKn, q.RF * k / 1000))));
        m.Series.Add(Line("RW", pts.Select(q => (q.SpeedKn, q.RW / 1000))));
        m.Series.Add(Line("RA", pts.Select(q => (q.SpeedKn, q.RA / 1000))));
        m.Legends[0].LegendPosition = OxyPlot.Legends.LegendPosition.LeftTop;
        p1.Model = m;
        var m2 = Plot("Мощность", "V, уз", "P, кВт");
        m2.Series.Add(Line("PE (буксировочная)", pts.Select(q => (q.SpeedKn, q.PE / 1000))));
        m2.Series.Add(Line("PB (на валу двигателя)", pts.Select(q => (q.SpeedKn, q.PB / 1000))));
        m2.Series.Add(Line($"PB + {margin.Value:0}% запас", pts.Select(q => (q.SpeedKn, q.PB * mg / 1000))));
        m2.Legends[0].LegendPosition = OxyPlot.Legends.LegendPosition.LeftTop;
        p2.Model = m2;
    }
}

public class FreeboardPage : ContentControl
{
    readonly Num deck = new("Толщина палубы, м", 0.012); readonly TextBlock res = T("", 14); readonly PlotView pv = new();
    public FreeboardPage()
    {
        var left = V(H("Надводный борт (упрощённо)"), T("Оценка по мотивам Международной конвенции о грузовой марке 1966 г. (тип B): табличный надводный борт, поправки на Cb и на высоту борта. Без поправок на надстройки, седловатость и бак. Табличные значения приближённые — для окончательного расчёта используйте официальные таблицы КГМ/Правила РС."),
            deck.Element, Row(B("Рассчитать", Calc, true)), res);
        Content = Split(Scroll(left), pv, 480);
        App.HullChanged += () => { res.Text = ""; pv.Model = null; };
    }
    void Calc()
    {
        var h = App.Hull; double T = App.DesignDraft;
        double cb = App.Calc.Upright(0.85 * h.D).Cb;
        var r = Freeboard.Calc(h.L, h.D, T, cb, deck.Value); App.Fb = r;
        res.Text = $"L = {h.L:0.##} м, D = {h.D:0.##} м, T = {T:0.###} м, Cb(0.85D) = {cb:0.###}\n\nТабличный борт: {r.Tabular:0} мм\nПоправка на Cb: +{r.CbCorr:0} мм\nПоправка на высоту борта: +{r.DepthCorr:0} мм\n\nМинимальный надводный борт: {r.Minimum:0} мм\nФактический: {r.Actual:0} мм → {(r.Pass ? "✔ достаточен" : "✘ НЕДОСТАТОЧЕН")}\nНаибольшая допустимая осадка ≈ {r.MaxDraft:0.###} м";
        res.Foreground = r.Pass ? Brushes.Black : Bad;
        var m = Plot("Мидель-шпангоут и грузовая марка", "y, м", "z, м"); m.PlotType = PlotType.Cartesian;
        m.Series.Add(Line("Мидель", Enumerable.Range(0, 41).Select(j => { double z = h.D * j / 40; return (h.HalfBreadth(h.L / 2, z), z); }).Concat(Enumerable.Range(0, 41).Reverse().Select(j => { double z = h.D * j / 40; return (-h.HalfBreadth(h.L / 2, z), z); }))));
        m.Series.Add(Line("Фактическая ВЛ", new[] { (-h.B / 2 - 1, T), (h.B / 2 + 1, T) }));
        m.Series.Add(Line("Предельная ВЛ", new[] { (-h.B / 2 - 1, r.MaxDraft), (h.B / 2 + 1, r.MaxDraft) })); ((LineSeries)m.Series[^1]).LineStyle = LineStyle.Dash;
        pv.Model = m;
    }
}
