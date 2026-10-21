using ShipDesigner.Core;
using Xunit;
using Xunit.Abstractions;
namespace ShipDesigner.Tests;

public class Tests
{
    readonly ITestOutputHelper o; public Tests(ITestOutputHelper o) => this.o = o;
    static void Near(double exp, double act, double relTol, string what = "") =>
        Assert.True(Math.Abs(exp - act) <= relTol * Math.Max(1e-9, Math.Abs(exp)), $"{what}: ожидалось {exp}, получено {act}");

    [Fact]
    public void BoxBarge_Hydrostatics_Exact()
    {
        var hc = new HydroCalc(new BoxHull(100, 20, 10), 1.025);
        var h = hc.Upright(5);
        Near(10000, h.Volume, 1e-9, "V"); Near(10250, h.Displacement, 1e-9, "Δ");
        Near(2.5, h.KB, 1e-9, "KB"); Near(50, h.LCB, 1e-9, "LCB"); Near(50, h.LCF, 1e-9, "LCF");
        Near(2000, h.Awp, 1e-9, "Awp"); Near(400.0 / 60, h.BMt, 1e-9, "BMt"); Near(10000.0 / 60, h.BMl, 1e-9, "BMl");
        Near(20.5, h.TPC, 1e-9, "TPC"); Near(10250 * (10000.0 / 60) / 10000, h.MCT, 1e-9, "MCT");
        Near(1, h.Cb, 1e-9, "Cb"); Near(1, h.Cm, 1e-9, "Cm"); Near(1, h.Cw, 1e-9, "Cw");
        Near(100 * (20 + 2 * 5), h.WettedSurface, 1e-9, "S");
    }

    [Theory]
    [InlineData(10)] [InlineData(20)] [InlineData(25)]
    public void BoxBarge_GZ_WallSidedFormula(double deg)
    {
        var hc = new HydroCalc(new BoxHull(100, 20, 10), 1.025);
        double KG = 6, BM = 400.0 / 60, GM = 2.5 + BM - KG, phi = deg * Math.PI / 180;
        double gzExact = Math.Sin(phi) * (GM + 0.5 * BM * Math.Tan(phi) * Math.Tan(phi));
        double gz = hc.KN(10000, deg) - KG * Math.Sin(phi);
        o.WriteLine($"{deg}°: GZ={gz:0.00000} точное={gzExact:0.00000}");
        Near(gzExact, gz, 1e-6, "GZ");
    }

    [Fact]
    public void Wigley_Hydrostatics_Analytic()
    {
        double L = 100, B = 10, T = 6.25;
        var hc = new HydroCalc(new WigleyHull(L, B, T, 10), 1.0, 201, 400);
        var h = hc.Upright(T);
        o.WriteLine(Report.Hydro(h));
        Near(4.0 / 9 * L * B * T, h.Volume, 2e-4, "V");
        Near(4.0 / 9, h.Cb, 2e-4, "Cb"); Near(2.0 / 3, h.Cm, 2e-4, "Cm"); Near(2.0 / 3, h.Cw, 1e-4, "Cw"); Near(2.0 / 3, h.Cp, 3e-4, "Cp");
        Near(5.0 / 8 * T, h.KB, 2e-4, "KB"); Near(L / 2, h.LCB, 1e-6, "LCB");
        Near(2.0 / 3 * Math.Pow(B / 2, 3) * L * 16 / 35 / (4.0 / 9 * L * B * T), h.BMt, 1e-3, "BMt");
        // IL = 2·(B/2)·L³·∫ξ²(1−4ξ²)dξ = B·L³·(1/12 − 1/20)
        Near(B * L * L * L * (1.0 / 12 - 4.0 / 80) / (4.0 / 9 * L * B * T), h.BMl, 1e-3, "BMl");
    }

    [Fact]
    public void Parametric_Hull_MatchesRequestedCoefficients()
    {
        var hc = new HydroCalc(new ParametricHull(120, 18, 7, 11, 0.70, 0.98), 1.025, 201, 300);
        var h = hc.Upright(7);
        o.WriteLine(Report.Hydro(h));
        Near(0.70, h.Cb, 5e-3, "Cb"); Near(0.98, h.Cm, 5e-3, "Cm");
    }

    [Fact]
    public void DraftForDisplacement_Inverse()
    {
        var hc = new HydroCalc(new BoxHull(100, 20, 10), 1.025);
        Near(4, hc.DraftForDisplacement(100 * 20 * 4 * 1.025), 1e-6, "T");
    }

    [Fact]
    public void Equilibrium_TrimAndHeel_Box()
    {
        var hc = new HydroCalc(new BoxHull(100, 20, 10), 1.025);
        var lc = new LoadCase { Items = { new LoadItem { Mass = 10250, LCG = 49, VCG = 6, TCG = 0.1 } } };
        var e = StabilityCalc.Solve(hc, lc);
        double MCT = 10250 * (10000.0 / 60) / 10000;
        Near(5, e.Tmean, 1e-6, "T"); Near(10250 * 1 / (100 * MCT), e.Trim, 1e-6, "trim");
        Near(5 + e.Trim / 2, e.Ta, 1e-6, "Ta"); Near(5 - e.Trim / 2, e.Tf, 1e-6, "Tf");
        Near(Math.Atan(0.1 / (2.5 + 400.0 / 60 - 6)) * 180 / Math.PI, e.HeelDeg, 1e-6, "heel");
    }

    [Fact]
    public void Imo_Criteria_Run()
    {
        var hc = new HydroCalc(new ParametricHull(80, 13, 5, 7.5, 0.65), 1.025, 61, 60);
        double disp = hc.Upright(5).Displacement;
        var lc = new LoadCase { Items = { new LoadItem { Mass = disp, LCG = 40, VCG = 4.5 } } };
        var r = StabilityCalc.Analyze(hc, lc);
        foreach (var c in r.Criteria) o.WriteLine($"{c.Name}: {c.Actual:0.000} ≥ {c.Required} {c.Pass}");
        Assert.Equal(6, r.Criteria.Count);
        Assert.True(r.MaxGz > 0);
    }

    [Fact]
    public void Holtrop_PaperExample()
    {
        // Holtrop & Mennen (1982/1984), пример: L=205, B=32, T=10, ∇=37500, lcb=−0.75 %, Cm=0.98, Cwp=0.75, ABT=20, hB=4, AT=16, Cstern=10
        var i = new HoltropInput { L = 205, B = 32, T = 10, Volume = 37500, Cb = 37500.0 / (205 * 32 * 10), Cm = 0.98, Cwp = 0.75, LcbPercent = -0.75, ABT = 20, hB = 4, AT = 16, Cstern = 10 };
        double k = Holtrop.FormFactor(i, out _);
        double S = Holtrop.WettedSurface(i);
        var p = Holtrop.Calc(i, 25);
        o.WriteLine($"1+k1={k:0.0000} S={S:0.00} RF={p.RF / 1000:0.0} RW={p.RW / 1000:0.0} RB={p.RB / 1000:0.00} RTR={p.RTR / 1000:0.00} RA={p.RA / 1000:0.0} RT={p.RT / 1000:0.0} kN");
        Near(7381.45, S, 2e-3, "S");
        Near(869.63, p.RF / 1000, 1e-3, "RF");
        Assert.InRange(k, 1.10, 1.25);
        Assert.InRange(p.RT / 1000, 1650, 1950); // опубликовано ≈1793 кН
    }

    [Fact]
    public void Offsets_Csv_RoundTrip()
    {
        var w = new WigleyHull(100, 10, 6.25, 10);
        var o1 = OffsetsHull.FromHull(w, 21, 11);
        var o2 = OffsetsHull.ParseCsv(o1.ToCsv());
        Near(w.HalfBreadth(30, 3), o2.HalfBreadth(30, 3), 2e-2, "y");
        var h = new HydroCalc(o2, 1).Upright(6.25);
        Near(2777.8, h.Volume, 2e-2, "V from offsets");
    }

    [Fact]
    public void LinesPlan_And_Exports()
    {
        var d = LinesPlan.Generate(new ParametricHull(80, 12, 4.5, 7, 0.65), 4.5);
        Assert.True(d.Entities.Count > 50);
        var dir = Path.Combine(Path.GetTempPath(), "sdtest"); Directory.CreateDirectory(dir);
        var dxf = Path.Combine(dir, "lines.dxf"); DrawingExport.SaveDxf(d, dxf);
        var back = DrawingExport.LoadDxf(dxf);
        o.WriteLine($"entities {d.Entities.Count} -> dxf back {back.Entities.Count}");
        Assert.True(back.Entities.Count >= d.Entities.Count(e => e is not DimEnt));
        var pdf = Path.Combine(dir, "lines.pdf"); DrawingExport.SavePdf(d, pdf, "Тест");
        Assert.True(new FileInfo(pdf).Length > 1000);
        var json = d.ToJson(); var d2 = Drawing.FromJson(json); Assert.Equal(d.Entities.Count, d2.Entities.Count);
    }

    [Fact]
    public void Arc_Through3Points()
    {
        var a = ArcEnt.Through(new P2(1, 0), new P2(0, 1), new P2(-1, 0));
        Near(1, a.R, 1e-9); Near(0, a.Start, 1e-9); Near(180, a.End, 1e-9);
    }

    [Fact]
    public void Freeboard_Simplified()
    {
        var r = Freeboard.Calc(100, 8, 6, 0.75);
        o.WriteLine($"{r}"); Assert.True(r.Minimum > 1271);
    }
}
