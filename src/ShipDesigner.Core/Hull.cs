using System.Globalization;
using System.Text.Json.Serialization;
namespace ShipDesigner.Core;

/// <summary>Корпус: функция полуширины y(x, z). x — от кормового перпендикуляра (0..L), z — от основной плоскости (0..D).</summary>
public abstract class Hull
{
    public abstract string Name { get; }
    public abstract double L { get; }
    public abstract double B { get; }
    public abstract double D { get; }
    public abstract double HalfBreadth(double x, double z);

    /// <summary>Многоугольник шпангоута (правый и левый борт), обход против часовой стрелки, координаты (y, z).</summary>
    public List<(double y, double z)> Section(double x, int nz = 60)
    {
        var right = new List<(double, double)>();
        for (int j = 0; j <= nz; j++) { double z = D * j / nz; right.Add((HalfBreadth(x, z), z)); }
        var poly = new List<(double y, double z)>();
        // правый борт снизу вверх, затем левый сверху вниз
        foreach (var p in right) poly.Add(p);
        for (int j = right.Count - 1; j >= 0; j--) poly.Add((-right[j].Item1, right[j].Item2));
        return poly;
    }
}

/// <summary>Понтон (баржа-коробка) — аналитический тест.</summary>
public class BoxHull : Hull
{
    public double Length, Beam, Depth;
    public BoxHull(double l, double b, double d) { Length = l; Beam = b; Depth = d; }
    public override string Name => $"Понтон {Length}×{Beam}×{Depth} м";
    public override double L => Length; public override double B => Beam; public override double D => Depth;
    public override double HalfBreadth(double x, double z) => x < 0 || x > Length || z < 0 || z > Depth ? 0 : Beam / 2;
}

/// <summary>Корпус Уигли: y = B/2·(1−(2ξ)²)·(1−(z'/T)²), Cb = 4/9.</summary>
public class WigleyHull : Hull
{
    public double Length, Beam, DesignDraft, Depth;
    public WigleyHull(double l, double b, double t, double d) { Length = l; Beam = b; DesignDraft = t; Depth = d; }
    public override string Name => $"Корпус Уигли L={Length} B={Beam} T={DesignDraft}";
    public override double L => Length; public override double B => Beam; public override double D => Depth;
    public override double HalfBreadth(double x, double z)
    {
        if (x < 0 || x > Length || z < 0 || z > Depth) return 0;
        double xi = (x - Length / 2) / Length; // −0.5..0.5
        double f = 1 - 4 * xi * xi;
        double zz = Math.Min(z, DesignDraft);
        double g = 1 - Math.Pow((DesignDraft - zz) / DesignDraft, 2);
        return Beam / 2 * f * g;
    }
}

/// <summary>Параметрический корпус по L, B, T, D, Cb, Cm: y = B/2·(1−|ξ|^p)·(1−(1−z/T)^q(ξ)).
/// q(ξ) плавно уменьшается от q₀ = Cm/(1−Cm) на миделе до ~1.2 в оконечностях (V-образные шпангоуты в носу и корме),
/// поэтому Cm на миделе точный, а показатель p подбирается численно так, чтобы Cb на осадке T совпал с заданным.</summary>
public class ParametricHull : Hull
{
    public double Length, Beam, DesignDraft, Depth, Cb, Cm;
    public double Lcb = 0;  // смещение полноты к носу (−0.3..0.3): + полнее нос
    double p0 = double.NaN;
    public ParametricHull(double l, double b, double t, double d, double cb, double cm = 0.98) { Length = l; Beam = b; DesignDraft = t; Depth = d; Cb = cb; Cm = cm; }
    double Q0 => Cm / (1 - Cm);
    double Qx(double xi) { double w = Math.Pow(Math.Max(0, 1 - Math.Abs(xi)), 2); return Math.Exp(Math.Log(1.2) + (Math.Log(Q0) - Math.Log(1.2)) * w); }
    public override string Name => $"Параметрический L={Length} B={Beam} T={DesignDraft} Cb={Cb}";
    public override double L => Length; public override double B => Beam; public override double D => Depth;
    double Shape(double xi, double p, double z)
    {
        double pp = p * (1 + Lcb * Math.Sign(xi));
        double f = 1 - Math.Pow(Math.Abs(xi), Math.Max(0.3, pp));
        double zz = Math.Min(z, DesignDraft);
        double g = 1 - Math.Pow(1 - zz / DesignDraft, Qx(xi));
        return f * g;
    }
    /// <summary>Cb формы при показателе p: ∫∫ f·g по ξ∈[−1,1], ζ∈[0,1] / 2.</summary>
    double CbFor(double p)
    {
        int nx = 200; double s = 0;
        for (int i = 0; i < nx; i++)
        {
            double xi = -1 + 2 * (i + 0.5) / nx;
            double f = 1 - Math.Pow(Math.Abs(xi), Math.Max(0.3, p * (1 + Lcb * Math.Sign(xi))));
            double q = Qx(xi); s += f * q / (q + 1); // ∫(1−(1−ζ)^q)dζ = q/(q+1)
        }
        return s / nx;
    }
    double P
    {
        get
        {
            if (!double.IsNaN(p0)) return p0;
            double lo = 0.3, hi = 200;
            for (int k = 0; k < 80; k++) { double m = Math.Sqrt(lo * hi); if (CbFor(m) < Cb) lo = m; else hi = m; }
            return p0 = Math.Sqrt(lo * hi);
        }
    }
    public override double HalfBreadth(double x, double z)
    {
        if (x < 0 || x > Length || z < 0 || z > Depth) return 0;
        double xi = 2 * (x - Length / 2) / Length; // −1..1
        return Beam / 2 * Shape(xi, P, z);
    }
}

/// <summary>Таблица плазовых ординат: полуширины на шпангоутах x_i и ватерлиниях z_j (линейная интерполяция).</summary>
public class OffsetsHull : Hull
{
    public string Title = "Таблица ординат";
    public double[] X; public double[] Z; public double[][] Y; // Y[i][j]
    public override string Name => Title;
    public override double L => X[^1] - X[0];
    public override double B => 2 * Y.Max(r => r.Max());
    public override double D => Z[^1];
    public override double HalfBreadth(double x, double z)
    {
        x += X[0];
        if (x < X[0] || x > X[^1] || z < Z[0] || z > Z[^1]) return 0;
        int i = Idx(X, x), j = Idx(Z, z);
        double tx = (x - X[i]) / (X[i + 1] - X[i]), tz = (z - Z[j]) / (Z[j + 1] - Z[j]);
        double a = Y[i][j] * (1 - tz) + Y[i][j + 1] * tz, b = Y[i + 1][j] * (1 - tz) + Y[i + 1][j + 1] * tz;
        return a * (1 - tx) + b * tx;
    }
    static int Idx(double[] a, double v) { for (int k = 0; k < a.Length - 2; k++) if (v <= a[k + 1]) return k; return a.Length - 2; }

    public static OffsetsHull FromHull(Hull h, int nx = 21, int nz = 11)
    {
        var o = new OffsetsHull { Title = h.Name + " (ординаты)", X = new double[nx], Z = new double[nz], Y = new double[nx][] };
        for (int j = 0; j < nz; j++) o.Z[j] = h.D * j / (nz - 1);
        for (int i = 0; i < nx; i++) { o.X[i] = h.L * i / (nx - 1); o.Y[i] = new double[nz]; for (int j = 0; j < nz; j++) o.Y[i][j] = Math.Round(h.HalfBreadth(o.X[i], o.Z[j]), 4); }
        return o;
    }

    /// <summary>CSV: первая строка «x;z1;z2;...», далее «x_i;y_i1;y_i2;...». Разделитель ; , или табуляция.</summary>
    public static OffsetsHull ParseCsv(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
        char sep = lines[0].Contains(';') ? ';' : lines[0].Contains('\t') ? '\t' : ',';
        double P(string s) => double.Parse(s.Trim().Replace(',', '.'), CultureInfo.InvariantCulture);
        var head = lines[0].Split(sep);
        var z = head.Skip(1).Select(P).ToArray();
        var xs = new List<double>(); var ys = new List<double[]>();
        foreach (var l in lines.Skip(1)) { var c = l.Split(sep); xs.Add(P(c[0])); ys.Add(c.Skip(1).Take(z.Length).Select(P).ToArray()); }
        if (xs.Count < 2 || z.Length < 2) throw new FormatException("Нужно минимум 2 шпангоута и 2 ватерлинии");
        if (ys.Any(r => r.Length != z.Length)) throw new FormatException("В каждой строке должно быть столько ординат, сколько ватерлиний");
        return new OffsetsHull { X = xs.ToArray(), Z = z, Y = ys.ToArray() };
    }
    public string ToCsv()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("x;" + string.Join(";", Z.Select(F)));
        for (int i = 0; i < X.Length; i++) sb.AppendLine(F(X[i]) + ";" + string.Join(";", Y[i].Select(F)));
        return sb.ToString();
    }
    static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>Описание корпуса для сохранения в проект.</summary>
public class HullSpec
{
    public string Kind { get; set; } = "parametric"; // parametric | wigley | box | offsets
    public double L { get; set; } = 80;
    public double B { get; set; } = 12;
    public double T { get; set; } = 4.5;
    public double D { get; set; } = 7;
    public double Cb { get; set; } = 0.65;
    public double Cm { get; set; } = 0.98;
    public double LcbShift { get; set; } = 0;
    public string OffsetsCsv { get; set; }
    public Hull Build() => Kind switch
    {
        "box" => new BoxHull(L, B, D),
        "wigley" => new WigleyHull(L, B, T, D),
        "offsets" when !string.IsNullOrWhiteSpace(OffsetsCsv) => OffsetsHull.ParseCsv(OffsetsCsv),
        _ => new ParametricHull(L, B, T, D, Cb, Cm) { Lcb = LcbShift },
    };
}
