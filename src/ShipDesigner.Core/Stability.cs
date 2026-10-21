namespace ShipDesigner.Core;

public class LoadItem { public string Name { get; set; } = ""; public double Mass { get; set; } public double LCG { get; set; } public double VCG { get; set; } public double TCG { get; set; } public double FSM { get; set; } }

public class LoadCase
{
    public string Name { get; set; } = "Полный груз";
    public List<LoadItem> Items { get; set; } = new();
    public double Mass => Items.Sum(i => i.Mass);
    public double LCG => Mass > 0 ? Items.Sum(i => i.Mass * i.LCG) / Mass : 0;
    public double KG => Mass > 0 ? Items.Sum(i => i.Mass * i.VCG) / Mass : 0;
    public double TCG => Mass > 0 ? Items.Sum(i => i.Mass * i.TCG) / Mass : 0;
    public double FSM => Items.Sum(i => i.FSM);
    /// <summary>Поправка на свободную поверхность GG' = ΣFSM/Δ, м.</summary>
    public double FreeSurface => Mass > 0 ? FSM / Mass : 0;
    public static LoadCase Default(Hull h, double targetDisp)
    {
        double ls = targetDisp * 0.35;
        return new LoadCase { Items = {
            new LoadItem { Name = "Порожнее судно", Mass = Math.Round(ls), LCG = Math.Round(h.L * 0.47, 2), VCG = Math.Round(h.D * 0.62, 2) },
            new LoadItem { Name = "Груз в трюме", Mass = Math.Round(targetDisp * 0.55), LCG = Math.Round(h.L * 0.52, 2), VCG = Math.Round(h.D * 0.45, 2) },
            new LoadItem { Name = "Топливо", Mass = Math.Round(targetDisp * 0.07), LCG = Math.Round(h.L * 0.3, 2), VCG = Math.Round(h.D * 0.15, 2), FSM = Math.Round(targetDisp * 0.07 * 0.3) },
            new LoadItem { Name = "Экипаж и запасы", Mass = Math.Round(targetDisp * 0.03), LCG = Math.Round(h.L * 0.2, 2), VCG = Math.Round(h.D * 1.1, 2) },
        } };
    }
}

public class Equilibrium
{
    public double Displacement, Tmean, Trim, Ta, Tf, LCG, LCB, LCF, MCT, KG, KGcorr, KMt, GM0, GM, HeelDeg, TCG;
    public HydroRow Hydro;
}

public class GzPoint { public double Angle, KN, GZ; }

public class CriterionResult { public string Name; public double Required, Actual; public bool Pass; public string Unit; }

public class StabilityResult
{
    public Equilibrium Eq; public List<GzPoint> Curve = new(); public List<CriterionResult> Criteria = new();
    public double MaxGz, AngleMaxGz, VanishingAngle;
    public bool AllPass => Criteria.All(c => c.Pass);
    /// <summary>Восстанавливающий момент, т·м.</summary>
    public double RightingMoment(GzPoint p) => Eq.Displacement * p.GZ;
}

public static class StabilityCalc
{
    public static Equilibrium Solve(HydroCalc hc, LoadCase lc)
    {
        double disp = lc.Mass;
        double T = hc.DraftForDisplacement(disp);
        var h = hc.Upright(T);
        var e = new Equilibrium { Displacement = disp, Tmean = T, Hydro = h, LCG = lc.LCG, LCB = h.LCB, LCF = h.LCF, MCT = h.MCT, KG = lc.KG, TCG = lc.TCG, KMt = h.KMt };
        // дифферент на корму положителен (м)
        e.Trim = disp * (h.LCB - lc.LCG) / (100 * h.MCT);
        double L = hc.Hull.L;
        e.Ta = T + e.Trim * (h.LCF / L);
        e.Tf = T - e.Trim * ((L - h.LCF) / L);
        e.GM0 = h.KMt - lc.KG;
        e.KGcorr = lc.KG + lc.FreeSurface;
        e.GM = h.KMt - e.KGcorr;
        e.HeelDeg = Math.Abs(e.GM) > 1e-6 ? Math.Atan(lc.TCG / e.GM) * 180 / Math.PI : 90;
        return e;
    }

    public static StabilityResult Analyze(HydroCalc hc, LoadCase lc, double floodAngle = 60, double maxAngle = 80, double step = 5)
    {
        var r = new StabilityResult { Eq = Solve(hc, lc) };
        double V = r.Eq.Displacement / hc.Rho;
        for (double a = 0; a <= maxAngle + 1e-9; a += step)
        {
            double kn = a == 0 ? 0 : hc.KN(V, a);
            double gz = kn - r.Eq.KGcorr * Math.Sin(a * Math.PI / 180) - r.Eq.TCG * Math.Cos(a * Math.PI / 180);
            r.Curve.Add(new GzPoint { Angle = a, KN = kn, GZ = gz });
        }
        var mx = r.Curve.OrderByDescending(p => p.GZ).First(); r.MaxGz = mx.GZ; r.AngleMaxGz = mx.Angle;
        r.VanishingAngle = maxAngle;
        for (int i = 1; i < r.Curve.Count; i++)
            if (r.Curve[i - 1].Angle > 0 && r.Curve[i - 1].GZ > 0 && r.Curve[i].GZ <= 0)
            { var p0 = r.Curve[i - 1]; var p1 = r.Curve[i]; r.VanishingAngle = p0.Angle + (p1.Angle - p0.Angle) * p0.GZ / (p0.GZ - p1.GZ); break; }
        r.Criteria = ImoCriteria(r.Curve, r.Eq.GM, floodAngle);
        return r;
    }

    public static double GzAt(List<GzPoint> c, double a)
    {
        for (int i = 1; i < c.Count; i++) if (a <= c[i].Angle) { var p = c[i - 1]; var q = c[i]; return p.GZ + (q.GZ - p.GZ) * (a - p.Angle) / (q.Angle - p.Angle); }
        return c[^1].GZ;
    }
    /// <summary>Площадь под кривой GZ, м·рад (трапеции по сетке 0.5°).</summary>
    public static double Area(List<GzPoint> c, double a0, double a1)
    {
        double s = 0, h = 0.5;
        for (double a = a0; a < a1 - 1e-9; a += h) { double b = Math.Min(a + h, a1); s += (GzAt(c, a) + GzAt(c, b)) / 2 * (b - a) * Math.PI / 180; }
        return s;
    }
    /// <summary>Общие критерии IS Code 2008 (ИМО, часть A, 2.2).</summary>
    public static List<CriterionResult> ImoCriteria(List<GzPoint> c, double gm, double floodAngle = 60)
    {
        double a40 = Math.Min(40, floodAngle);
        var mx = c.Where(p => p.Angle <= 90).OrderByDescending(p => p.GZ).First();
        double gz30 = c.Where(p => p.Angle >= 30).Select(p => p.GZ).DefaultIfEmpty(0).Max();
        var l = new List<CriterionResult>
        {
            new() { Name = "Площадь под кривой GZ 0–30°", Required = 0.055, Actual = Area(c, 0, 30), Unit = "м·рад" },
            new() { Name = $"Площадь под кривой GZ 0–{a40:0}°", Required = 0.090, Actual = Area(c, 0, a40), Unit = "м·рад" },
            new() { Name = $"Площадь под кривой GZ 30–{a40:0}°", Required = 0.030, Actual = Area(c, 30, a40), Unit = "м·рад" },
            new() { Name = "GZ при крене ≥ 30°", Required = 0.20, Actual = gz30, Unit = "м" },
            new() { Name = "Угол максимума GZ", Required = 25, Actual = mx.Angle, Unit = "°" },
            new() { Name = "Начальная метацентрическая высота GM₀ (с поправкой)", Required = 0.15, Actual = gm, Unit = "м" },
        };
        foreach (var x in l) x.Pass = x.Actual >= x.Required - 1e-9;
        return l;
    }
}
