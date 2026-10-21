using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
namespace ShipDesigner.Core;

public static class DrawingExport
{
    static AciColor Col(string hex)
    {
        try { var c = System.Drawing.ColorTranslator.FromHtml(hex); return (c.R == 0 && c.G == 0 && c.B == 0) ? AciColor.ByLayer : new AciColor(c.R, c.G, c.B); } catch { return AciColor.ByLayer; }
    }
    public static void SaveDxf(Drawing d, string path)
    {
        var doc = new DxfDocument();
        var layers = new Dictionary<string, netDxf.Tables.Layer>();
        foreach (var l in d.Layers)
        {
            var name = l.Name == "0" ? "0" : San(l.Name);
            var nl = name == "0" ? doc.Layers["0"] : new netDxf.Tables.Layer(name);
            try { var c = System.Drawing.ColorTranslator.FromHtml(l.Color); nl.Color = (c.R == 0 && c.G == 0 && c.B == 0) ? AciColor.Default : new AciColor(c.R, c.G, c.B); } catch { }
            nl.IsVisible = l.Visible;
            if (name != "0") doc.Layers.Add(nl);
            layers[l.Name] = nl;
        }
        netDxf.Tables.Layer Ly(string n) => layers.TryGetValue(n, out var x) ? x : doc.Layers["0"];
        foreach (var e in d.Entities)
        {
            EntityObject o = e switch
            {
                LineEnt l => new Line(new Vector2(l.A.X, l.A.Y), new Vector2(l.B.X, l.B.Y)),
                CircleEnt c => new Circle(new Vector2(c.C.X, c.C.Y), c.R),
                ArcEnt a => new Arc(new Vector2(a.C.X, a.C.Y), a.R, a.Start, a.End),
                PolylineEnt p => new Polyline2D(p.Points.Select(q => new Polyline2DVertex(q.X, q.Y)), p.Closed),
                TextEnt t => new Text(t.Text, new Vector2(t.P.X, t.P.Y), t.Height),
                DimEnt dm => MakeDim(dm),
                _ => null
            };
            if (o == null) continue;
            o.Layer = Ly(e.Layer);
            doc.Entities.Add(o);
        }
        doc.Save(path);
    }
    static EntityObject MakeDim(DimEnt dm)
    {
        var dim = new AlignedDimension(new Vector2(dm.A.X, dm.A.Y), new Vector2(dm.B.X, dm.B.Y), dm.Offset);
        dim.Style = new DimensionStyle("SD" + Guid.NewGuid().ToString("N")[..6]) { TextHeight = dm.TextHeight, ArrowSize = dm.TextHeight, DimLineExtend = 0, ExtLineExtend = dm.TextHeight * 0.5 };
        return dim;
    }
    static string San(string s) { var sb = new StringBuilder(); foreach (var ch in s) sb.Append("<>/\\\":;?*|=`".Contains(ch) ? '_' : ch); return sb.ToString(); }

    public static Drawing LoadDxf(string path)
    {
        var doc = DxfDocument.Load(path) ?? throw new InvalidDataException("Не удалось прочитать DXF");
        var d = new Drawing { Layers = new() };
        foreach (var l in doc.Layers) d.Layers.Add(new Layer { Name = l.Name, Visible = l.IsVisible, Color = l.Color.IsByLayer || l.Color.Index == 7 ? "#000000" : $"#{l.Color.R:X2}{l.Color.G:X2}{l.Color.B:X2}" });
        foreach (var x in doc.Entities.Lines) d.Entities.Add(new LineEnt { Layer = x.Layer.Name, A = new(x.StartPoint.X, x.StartPoint.Y), B = new(x.EndPoint.X, x.EndPoint.Y) });
        foreach (var x in doc.Entities.Circles) d.Entities.Add(new CircleEnt { Layer = x.Layer.Name, C = new(x.Center.X, x.Center.Y), R = x.Radius });
        foreach (var x in doc.Entities.Arcs) d.Entities.Add(new ArcEnt { Layer = x.Layer.Name, C = new(x.Center.X, x.Center.Y), R = x.Radius, Start = x.StartAngle, End = x.EndAngle });
        foreach (var x in doc.Entities.Polylines2D) d.Entities.Add(new PolylineEnt { Layer = x.Layer.Name, Closed = x.IsClosed, Points = x.Vertexes.Select(v => new P2(v.Position.X, v.Position.Y)).ToList() });
        foreach (var x in doc.Entities.Texts) d.Entities.Add(new TextEnt { Layer = x.Layer.Name, P = new(x.Position.X, x.Position.Y), Text = x.Value, Height = x.Height });
        foreach (var x in doc.Entities.MTexts) d.Entities.Add(new TextEnt { Layer = x.Layer.Name, P = new(x.Position.X, x.Position.Y), Text = x.PlainText(), Height = x.Height });
        if (d.Layers.Count == 0) d.Layers.Add(new Layer());
        return d;
    }

    /// <summary>Простой векторный PDF (A3 альбом, вписать по размеру). Текст — латиница/кириллица через встроенный шрифт не поддерживается: кириллица транслитерируется.</summary>
    public static void SavePdf(Drawing d, string path, string title = "")
    {
        double W = 1190.55, H = 841.89, m = 30; // A3 landscape pt
        var (mn, mx) = d.Extents();
        double sx = (W - 2 * m) / Math.Max(1e-9, mx.X - mn.X), sy = (H - 2 * m - 20) / Math.Max(1e-9, mx.Y - mn.Y), k = Math.Min(sx, sy);
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        (double, double) T(P2 p) => (m + (p.X - mn.X) * k, m + 20 + (p.Y - mn.Y) * k);
        var c = new StringBuilder("0.5 w 1 J 1 j\n");
        string lastCol = null;
        void Color(string hex) { if (hex == lastCol) return; lastCol = hex; try { var cc = System.Drawing.ColorTranslator.FromHtml(hex); c.Append($"{F(cc.R / 255.0)} {F(cc.G / 255.0)} {F(cc.B / 255.0)} RG {F(cc.R / 255.0)} {F(cc.G / 255.0)} {F(cc.B / 255.0)} rg\n"); } catch { } }
        void Path(IEnumerable<P2> pts, bool close) { bool first = true; foreach (var p in pts) { var (x, y) = T(p); c.Append($"{F(x)} {F(y)} {(first ? "m" : "l")}\n"); first = false; } c.Append(close ? "s\n" : "S\n"); }
        void Txt(P2 p, string s, double h) { var (x, y) = T(p); c.Append($"BT /F1 {F(Math.Max(4, h * k))} Tf {F(x)} {F(y)} Td ({Esc(Translit(s))}) Tj ET\n"); }
        foreach (var e in d.Entities)
        {
            var ly = d.GetLayer(e.Layer); if (!ly.Visible) continue; Color(ly.Color);
            switch (e)
            {
                case LineEnt l: Path(new[] { l.A, l.B }, false); break;
                case PolylineEnt p: Path(p.Points, p.Closed); break;
                case CircleEnt ci: Path(Enumerable.Range(0, 73).Select(i => ci.C + new P2(ci.R * Math.Cos(i * Math.PI / 36), ci.R * Math.Sin(i * Math.PI / 36))), true); break;
                case ArcEnt a: Path(Enumerable.Range(0, 49).Select(i => a.At(a.Start + a.Sweep * i / 48)), false); break;
                case TextEnt t: Txt(t.P, t.Text, t.Height); break;
                case DimEnt dm:
                    Path(new[] { dm.A, dm.A2 }, false); Path(new[] { dm.B, dm.B2 }, false); Path(new[] { dm.A2, dm.B2 }, false);
                    Txt((dm.A2 + dm.B2) * 0.5 + dm.Normal * (dm.TextHeight * 0.3), dm.Label, dm.TextHeight); break;
            }
        }
        c.Append($"0 0 0 rg BT /F1 10 Tf {F(m)} {F(m - 12)} Td ({Esc(Translit(title + "  ShipDesigner  " + DateTime.Now.ToString("dd.MM.yyyy")))}) Tj ET\n");
        var content = Encoding.ASCII.GetBytes(c.ToString());
        var objs = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(W)} {F(H)}] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            null,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        using var fs = File.Create(path);
        void W_(string s) { var b = Encoding.ASCII.GetBytes(s); fs.Write(b); }
        W_("%PDF-1.4\n"); var offs = new List<long>();
        for (int i = 0; i < objs.Count; i++)
        {
            offs.Add(fs.Position);
            if (i == 3) { W_($"4 0 obj\n<< /Length {content.Length} >>\nstream\n"); fs.Write(content); W_("\nendstream\nendobj\n"); }
            else W_($"{i + 1} 0 obj\n{objs[i]}\nendobj\n");
        }
        long xref = fs.Position;
        W_($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) W_($"{o:D10} 00000 n \n");
        W_($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    public static string Translit(string s)
    {
        const string ru = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        string[] la = { "a", "b", "v", "g", "d", "e", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "kh", "ts", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya" };
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            int i = ru.IndexOf(char.ToLower(ch));
            if (i >= 0) { var t = la[i]; sb.Append(char.IsUpper(ch) && t.Length > 0 ? char.ToUpper(t[0]) + t[1..] : t); }
            else if (ch < 128) sb.Append(ch);
            else sb.Append(ch switch { '×' => "x", '°' => " deg", '≥' => ">=", '≤' => "<=", '—' or '–' => "-", '·' => "*", _ => "?" });
        }
        return sb.ToString();
    }
}
