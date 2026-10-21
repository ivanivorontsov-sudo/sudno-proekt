namespace ShipDesigner.Core;

public class HydroRow
{
    public double T, Volume, Displacement, LCB, KB, Awp, LCF, BMt, BMl, KMt, KMl, TPC, MCT, Cb, Cm, Cp, Cw, Lwl, Bwl, Amid, WettedSurface, It, Il;
}

public static class Geometry
{
    /// <summary>Отсечение многоугольника полуплоскостью f(p) ≤ 0 (Сазерленд–Ходжман).</summary>
    public static List<(double y, double z)> Clip(List<(double y, double z)> poly, Func<(double y, double z), double> f)
    {
        var res = new List<(double, double)>();
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % n];
            double fa = f(a), fb = f(b);
            if (fa <= 0) res.Add(a);
            if ((fa < 0 && fb > 0) || (fa > 0 && fb < 0))
            {
                double t = fa / (fa - fb);
                res.Add((a.y + t * (b.y - a.y), a.z + t * (b.z - a.z)));
            }
        }
        return res;
    }
    /// <summary>Площадь (по модулю) и центр тяжести многоугольника.</summary>
    public static (double area, double cy, double cz) AreaCentroid(List<(double y, double z)> p)
    {
        double a = 0, cy = 0, cz = 0; int n = p.Count;
        for (int i = 0; i < n; i++)
        {
            var (y0, z0) = p[i]; var (y1, z1) = p[(i + 1) % n];
            double cr = y0 * z1 - y1 * z0; a += cr; cy += (y0 + y1) * cr; cz += (z0 + z1) * cr;
        }
        a /= 2; if (Math.Abs(a) < 1e-12) return (0, 0, 0);
        return (Math.Abs(a), cy / (6 * a), cz / (6 * a));
    }
    public static double Perimeter(List<(double y, double z)> p)
    {
        double s = 0; for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += Math.Sqrt((a.y - b.y) * (a.y - b.y) + (a.z - b.z) * (a.z - b.z)); }
        return s;
    }
    /// <summary>Интеграл по Симпсону для равномерной сетки (n нечётное); иначе трапеции.</summary>
    public static double Simpson(double[] f, double h)
    {
        int n = f.Length; if (n < 2) return 0;
        if (n % 2 == 0) { double s0 = 0; for (int i = 0; i < n - 1; i++) s0 += (f[i] + f[i + 1]) / 2 * h; return s0; }
        double s = f[0] + f[^1];
        for (int i = 1; i < n - 1; i++) s += f[i] * (i % 2 == 1 ? 4 : 2);
        return s * h / 3;
    }
}

/// <summary>Гидростатика, кривые Бонжана, плечи остойчивости формы. Интегрирование по шпангоутам (Симпсон) и многоугольникам сечений.</summary>
public class HydroCalc
{
    public Hull Hull; public double Rho = 1.025; public int Nx = 101, Nz = 80;
    readonly double[] xs; readonly List<(double y, double z)>[] sections;
    public HydroCalc(Hull hull, double rho = 1.025, int nx = 101, int nz = 80)
    {
        Hull = hull; Rho = rho; Nx = nx % 2 == 0 ? nx + 1 : nx; Nz = nz;
        xs = new double[Nx]; sections = new List<(double, double)>[Nx];
        for (int i = 0; i < Nx; i++) { xs[i] = hull.L * i / (Nx - 1); sections[i] = hull.Section(xs[i], Nz); }
    }
    public double[] Stations => xs;
    double H => Hull.L / (Nx - 1);

    /// <summary>Площадь шпангоута ниже осадки t (кривая Бонжана).</summary>
    public double SectionArea(int i, double t) => Geometry.AreaCentroid(Geometry.Clip(sections[i], p => p.z - t)).area;

    public HydroRow Upright(double T)
    {
        int n = Nx; var A = new double[n]; var Ax = new double[n]; var Az = new double[n]; var yw = new double[n]; var yw3 = new double[n];
        var ywx = new double[n]; var ywx2 = new double[n]; var girth = new double[n];
        for (int i = 0; i < n; i++)
        {
            var c = Geometry.Clip(sections[i], p => p.z - T);
            var (a, _, cz) = Geometry.AreaCentroid(c);
            A[i] = a; Ax[i] = a * xs[i]; Az[i] = a * cz;
            double y = Hull.HalfBreadth(xs[i], T); yw[i] = y; yw3[i] = y * y * y; ywx[i] = y * xs[i]; ywx2[i] = y * xs[i] * xs[i];
            girth[i] = c.Count > 2 ? Geometry.Perimeter(c) - 2 * y : 0;
        }
        var r = new HydroRow { T = T };
        r.Volume = Geometry.Simpson(A, H);
        if (r.Volume <= 1e-9) return r;
        r.Displacement = r.Volume * Rho;
        r.LCB = Geometry.Simpson(Ax, H) / r.Volume;
        r.KB = Geometry.Simpson(Az, H) / r.Volume;
        r.Awp = 2 * Geometry.Simpson(yw, H);
        r.LCF = r.Awp > 0 ? 2 * Geometry.Simpson(ywx, H) / r.Awp : 0;
        r.It = 2.0 / 3.0 * Geometry.Simpson(yw3, H);
        r.Il = 2 * Geometry.Simpson(ywx2, H) - r.Awp * r.LCF * r.LCF;
        r.BMt = r.It / r.Volume; r.BMl = r.Il / r.Volume;
        r.KMt = r.KB + r.BMt; r.KMl = r.KB + r.BMl;
        r.TPC = r.Awp * Rho / 100;
        r.Lwl = WaterlineLength(T);
        r.Bwl = 2 * yw.Max();
        r.MCT = r.Displacement * r.BMl / (100 * Hull.L);
        r.Amid = SectionAreaAt(Hull.L / 2, T);
        double Lref = Hull.L;
        r.Cb = r.Volume / (Lref * r.Bwl * T);
        r.Cm = r.Amid / (r.Bwl * T);
        r.Cp = r.Cm > 0 ? r.Cb / r.Cm : 0;
        r.Cw = r.Awp / (Lref * r.Bwl);
        r.WettedSurface = Geometry.Simpson(girth, H);
        return r;
    }
    public double SectionAreaAt(double x, double T) => Geometry.AreaCentroid(Geometry.Clip(Hull.Section(x, Nz), p => p.z - T)).area;
    public double WaterlineLength(double T)
    {
        double a = -1, b = -1; int m = 400;
        for (int k = 0; k <= m; k++) { double x = Hull.L * k / m; if (Hull.HalfBreadth(x, T) > 1e-6) { if (a < 0) a = x; b = x; } }
        return a < 0 ? 0 : b - a;
    }
    public List<HydroRow> Table(double tMin, double tMax, int steps)
    {
        var l = new List<HydroRow>();
        for (int k = 0; k <= steps; k++) { double t = tMin + (tMax - tMin) * k / steps; if (t > 1e-6) l.Add(Upright(t)); }
        return l;
    }
    /// <summary>Осадка (на ровный киль) по водоизмещению, т.</summary>
    public double DraftForDisplacement(double disp)
    {
        double lo = 1e-4, hi = Hull.D;
        if (Upright(hi).Displacement < disp) throw new InvalidOperationException("Водоизмещение больше, чем у корпуса, погружённого до палубы");
        for (int k = 0; k < 50; k++) { double m = (lo + hi) / 2; if (Upright(m).Displacement < disp) lo = m; else hi = m; }
        return (lo + hi) / 2;
    }

    /// <summary>Объём и центр величины при крене φ и «высоте» ватерлинии d (−y·sinφ + z·cosφ ≤ d).</summary>
    (double V, double yB, double zB) Heeled(double phi, double d)
    {
        double s = Math.Sin(phi), c = Math.Cos(phi);
        var A = new double[Nx]; var Ay = new double[Nx]; var Az = new double[Nx];
        for (int i = 0; i < Nx; i++)
        {
            var cl = Geometry.Clip(sections[i], p => -p.y * s + p.z * c - d);
            var (a, cy, cz) = Geometry.AreaCentroid(cl); A[i] = a; Ay[i] = a * cy; Az[i] = a * cz;
        }
        double V = Geometry.Simpson(A, H);
        return V < 1e-12 ? (0, 0, 0) : (V, Geometry.Simpson(Ay, H) / V, Geometry.Simpson(Az, H) / V);
    }
    /// <summary>Плечо остойчивости формы KN (м) при крене φ (град) для объёмного водоизмещения V (без дифферента).</summary>
    public double KN(double volume, double phiDeg)
    {
        double phi = phiDeg * Math.PI / 180, s = Math.Sin(phi), c = Math.Cos(phi);
        double R = Math.Sqrt(Hull.B * Hull.B / 4 + Hull.D * Hull.D) + 1;
        double lo = -R, hi = R;
        for (int k = 0; k < 60; k++) { double m = (lo + hi) / 2; if (Heeled(phi, m).V < volume) lo = m; else hi = m; }
        var (_, yB, zB) = Heeled(phi, (lo + hi) / 2);
        return yB * c + zB * s;
    }
    /// <summary>Пантокарены: KN(φ) для набора водоизмещений.</summary>
    public double[,] CrossCurves(double[] displacements, double[] angles)
    {
        var r = new double[displacements.Length, angles.Length];
        for (int i = 0; i < displacements.Length; i++) for (int j = 0; j < angles.Length; j++) r[i, j] = KN(displacements[i] / Rho, angles[j]);
        return r;
    }
}
