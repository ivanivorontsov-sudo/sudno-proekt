using System.Globalization;
using System.Text;
using System.Text.Json;
namespace ShipDesigner.Core;

public class ShipProject
{
    public string Name { get; set; } = "Новый проект";
    public HullSpec Hull { get; set; } = new();
    public double Rho { get; set; } = 1.025;
    public LoadCase Load { get; set; }
    public double FloodAngle { get; set; } = 60;
    public double SpeedKn { get; set; } = 14;
    public double EtaD { get; set; } = 0.65;
    public double ABT { get; set; }
    public double HB { get; set; }
    public double AT { get; set; }
    public double Cstern { get; set; }
    public Drawing Drawing { get; set; }
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public string ToJson() => JsonSerializer.Serialize(this, Opt);
    public static ShipProject FromJson(string s) => JsonSerializer.Deserialize<ShipProject>(s, Opt);
}

/// <summary>Сводка текущих расчётов для ИИ-помощника.</summary>
public static class Report
{
    static string F(double v, string f = "0.###") => v.ToString(f, CultureInfo.InvariantCulture);
    public static string Hydro(HydroRow h) =>
        $"T={F(h.T)} м; V={F(h.Volume, "0.#")} м³; Δ={F(h.Displacement, "0.#")} т; LCB={F(h.LCB)} м от КП; KB={F(h.KB)} м; Awp={F(h.Awp, "0.#")} м²; LCF={F(h.LCF)} м; BMt={F(h.BMt)} м; BMl={F(h.BMl, "0.#")} м; KMt={F(h.KMt)} м; TPC={F(h.TPC)} т/см; MCT={F(h.MCT)} т·м/см; Cb={F(h.Cb)}; Cm={F(h.Cm)}; Cp={F(h.Cp)}; Cw={F(h.Cw)}; S={F(h.WettedSurface, "0.#")} м²";
    public static string Full(ShipProject p, Hull hull, HydroRow design, StabilityResult st, ResistancePoint rp, Freeboard.Result fb)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Проект: {p.Name}. Корпус: {hull.Name}. L={F(hull.L)} м, B={F(hull.B)} м, D={F(hull.D)} м, плотность воды {F(p.Rho)} т/м³.");
        if (design != null) sb.AppendLine("Гидростатика на расчётной осадке: " + Hydro(design));
        if (st != null)
        {
            var e = st.Eq;
            sb.AppendLine($"Нагрузка «{p.Load?.Name}»: Δ={F(e.Displacement, "0.#")} т, LCG={F(e.LCG)} м, KG={F(e.KG)} м, KG с поправкой на свободную поверхность={F(e.KGcorr)} м, TCG={F(e.TCG)} м.");
            sb.AppendLine($"Посадка: Tср={F(e.Tmean)} м, дифферент={F(e.Trim)} м (+ на корму), Tк={F(e.Ta)} м, Tн={F(e.Tf)} м, крен={F(e.HeelDeg, "0.##")}°. GM0={F(e.GM0)} м, GM исправл.={F(e.GM)} м.");
            sb.AppendLine("Кривая GZ (угол°: GZ м): " + string.Join("; ", st.Curve.Select(c => $"{F(c.Angle, "0")}: {F(c.GZ)}")));
            sb.AppendLine($"GZmax={F(st.MaxGz)} м при {F(st.AngleMaxGz, "0")}°, угол заката ≈{F(st.VanishingAngle, "0.#")}° (если равен верхней границе расчёта — закат не достигнут).");
            sb.AppendLine("Критерии ИМО IS Code 2008: " + string.Join("; ", st.Criteria.Select(c => $"{c.Name}: {F(c.Actual)} (треб. ≥{F(c.Required)}) — {(c.Pass ? "выполнено" : "НЕ выполнено")}")));
        }
        if (rp != null) sb.AppendLine($"Сопротивление (Holtrop–Mennen) при {F(rp.SpeedKn, "0.#")} уз: Fn={F(rp.Fn)}, RT={F(rp.RT / 1000, "0.#")} кН (RF={F(rp.RF / 1000, "0.#")}, RW={F(rp.RW / 1000, "0.#")}), PE={F(rp.PE / 1000, "0")} кВт, PB≈{F(rp.PB / 1000, "0")} кВт.");
        if (fb != null) sb.AppendLine($"Надводный борт (упрощённо, тип B): минимальный {F(fb.Minimum, "0")} мм, фактический {F(fb.Actual, "0")} мм — {(fb.Pass ? "достаточен" : "НЕдостаточен")}.");
        return sb.ToString();
    }
}
