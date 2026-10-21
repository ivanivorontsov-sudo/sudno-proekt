namespace ShipDesigner.Core;

/// <summary>Исходные данные метода Holtrop–Mennen (1982/1984).</summary>
public class HoltropInput
{
    public double L, B, T, Volume, Cb, Cm, Cwp, LcbPercent; // lcb — % от L, + в нос от миделя
    public double S = 0;          // смоченная поверхность, 0 — по формуле Холтропа
    public double ABT = 0, hB = 0, AT = 0, Cstern = 0, Sapp = 0, OnePlusK2 = 1.5;
    public double Rho = 1025, Nu = 1.1883e-6, G = 9.81;
    public double Cp => Cb / Cm;
}

public class ResistancePoint { public double SpeedKn, Fn, Re, CF, RF, RApp, RW, RB, RTR, RA, RT, PE, PD, PB; }

public static class Holtrop
{
    public static double FormFactor(HoltropInput i, out double LR)
    {
        double Cp = i.Cp, L = i.L;
        LR = L * (1 - Cp + 0.06 * Cp * i.LcbPercent / (4 * Cp - 1));
        double c14 = 1 + 0.011 * i.Cstern;
        return 0.93 + 0.487118 * c14 * Math.Pow(i.B / L, 1.06806) * Math.Pow(i.T / L, 0.46106) * Math.Pow(L / LR, 0.121563)
            * Math.Pow(L * L * L / i.Volume, 0.36486) * Math.Pow(1 - Cp, -0.604247);
    }
    public static double WettedSurface(HoltropInput i) =>
        i.L * (2 * i.T + i.B) * Math.Sqrt(i.Cm) * (0.453 + 0.4425 * i.Cb - 0.2862 * i.Cm - 0.003467 * i.B / i.T + 0.3696 * i.Cwp) + (i.Cb > 0 ? 2.38 * i.ABT / i.Cb : 0);
    public static double HalfEntrance(HoltropInput i, double LR) =>
        1 + 89 * Math.Exp(-Math.Pow(i.L / i.B, 0.80856) * Math.Pow(1 - i.Cwp, 0.30484) * Math.Pow(1 - i.Cp - 0.0225 * i.LcbPercent, 0.6367)
            * Math.Pow(LR / i.B, 0.34574) * Math.Pow(100 * i.Volume / (i.L * i.L * i.L), 0.16302));

    public static ResistancePoint Calc(HoltropInput i, double speedKn, double etaD = 0.65, double etaS = 0.98)
    {
        double V = speedKn * 0.5144444, L = i.L, B = i.B, T = i.T, Cp = i.Cp, rho = i.Rho, g = i.G, Vol = i.Volume;
        double k1 = FormFactor(i, out double LR);
        double S = i.S > 0 ? i.S : WettedSurface(i);
        double Fn = V / Math.Sqrt(g * L), Re = V * L / i.Nu;
        double q = 0.5 * rho * V * V;
        var p = new ResistancePoint { SpeedKn = speedKn, Fn = Fn, Re = Re };
        p.CF = V > 0 ? 0.075 / Math.Pow(Math.Log10(Re) - 2, 2) : 0;
        p.RF = q * S * p.CF;
        p.RApp = q * i.Sapp * i.OnePlusK2 * p.CF;
        // волновое
        double iE = HalfEntrance(i, LR);
        double BL = B / L;
        double c7 = BL < 0.11 ? 0.229577 * Math.Pow(BL, 0.33333) : BL < 0.25 ? BL : 0.5 - 0.0625 * L / B;
        double c1 = 2223105 * Math.Pow(c7, 3.78613) * Math.Pow(T / B, 1.07961) * Math.Pow(90 - iE, -1.37565);
        double TF = T;
        double c3 = i.ABT > 0 ? 0.56 * Math.Pow(i.ABT, 1.5) / (B * T * (0.31 * Math.Sqrt(i.ABT) + TF - i.hB)) : 0;
        double c2 = Math.Exp(-1.89 * Math.Sqrt(c3));
        double c5 = 1 - 0.8 * i.AT / (B * T * i.Cm);
        double lambda = L / B < 12 ? 1.446 * Cp - 0.03 * L / B : 1.446 * Cp - 0.36;
        double c16 = Cp < 0.8 ? 8.07981 * Cp - 13.8673 * Cp * Cp + 6.984388 * Cp * Cp * Cp : 1.73014 - 0.7067 * Cp;
        double m1 = 0.0140407 * L / T - 1.75254 * Math.Pow(Vol, 1.0 / 3) / L - 4.79323 * B / L - c16;
        double L3V = L * L * L / Vol;
        double c15 = L3V < 512 ? -1.69385 : L3V > 1726.91 ? 0 : -1.69385 + (L / Math.Pow(Vol, 1.0 / 3) - 8) / 2.36;
        if (Fn > 0.01)
        {
            double m4 = c15 * 0.4 * Math.Exp(-0.034 * Math.Pow(Fn, -3.29));
            p.RW = c1 * c2 * c5 * Vol * rho * g * Math.Exp(m1 * Math.Pow(Fn, -0.9) + m4 * Math.Cos(lambda * Math.Pow(Fn, -2)));
        }
        // бульб
        if (i.ABT > 0 && V > 0)
        {
            double PB = 0.56 * Math.Sqrt(i.ABT) / (TF - 1.5 * i.hB);
            double Fni = V / Math.Sqrt(g * (TF - i.hB - 0.25 * Math.Sqrt(i.ABT)) + 0.15 * V * V);
            p.RB = 0.11 * Math.Exp(-3 * Math.Pow(PB, -2)) * Math.Pow(Fni, 3) * Math.Pow(i.ABT, 1.5) * rho * g / (1 + Fni * Fni);
        }
        // транец
        if (i.AT > 0 && V > 0)
        {
            double FnT = V / Math.Sqrt(2 * g * i.AT / (B + B * i.Cwp));
            double c6 = FnT < 5 ? 0.2 * (1 - 0.2 * FnT) : 0;
            p.RTR = q * i.AT * c6;
        }
        double c4 = TF / L <= 0.04 ? TF / L : 0.04;
        double CA = 0.006 * Math.Pow(L + 100, -0.16) - 0.00205 + 0.003 * Math.Sqrt(L / 7.5) * Math.Pow(i.Cb, 4) * c2 * (0.04 - c4);
        p.RA = q * S * CA;
        p.RT = p.RF * k1 + p.RApp + p.RW + p.RB + p.RTR + p.RA;
        p.PE = p.RT * V; p.PD = p.PE / etaD; p.PB = p.PD / etaS;
        return p;
    }

    public static HoltropInput FromHydro(HydroRow h, double L)
    {
        double lcbPct = (h.LCB - L / 2) / L * 100;
        return new HoltropInput { L = h.Lwl > 0 ? h.Lwl : L, B = h.Bwl, T = h.T, Volume = h.Volume, Cb = Math.Clamp(h.Volume / ((h.Lwl > 0 ? h.Lwl : L) * h.Bwl * h.T), 0.3, 0.95), Cm = Math.Clamp(h.Cm, 0.5, 0.999), Cwp = Math.Clamp(h.Awp / ((h.Lwl > 0 ? h.Lwl : L) * h.Bwl), 0.4, 0.99), LcbPercent = lcbPct, S = h.WettedSurface };
    }
}

/// <summary>Упрощённый расчёт минимального надводного борта (по мотивам МК ГМ-66, тип B). Ориентировочно!</summary>
public static class Freeboard
{
    // Табличный надводный борт для судов типа B, мм (приближённые узловые значения)
    static readonly (double L, double F)[] TableB = { (24, 200), (30, 250), (40, 334), (50, 443), (60, 573), (70, 721), (80, 887), (90, 1069), (100, 1271), (120, 1690), (150, 2315), (200, 3264), (250, 4064), (300, 4726), (365, 5303) };
    public static double Tabular(double L)
    {
        if (L <= TableB[0].L) return TableB[0].F;
        for (int k = 1; k < TableB.Length; k++) if (L <= TableB[k].L) { var a = TableB[k - 1]; var b = TableB[k]; return a.F + (b.F - a.F) * (L - a.L) / (b.L - a.L); }
        return TableB[^1].F;
    }
    public record Result(double Tabular, double CbCorr, double DepthCorr, double Minimum, double Actual, bool Pass, double MaxDraft);
    public static Result Calc(double L, double D, double T, double cb, double deckThickness = 0)
    {
        double f = Tabular(L);
        double cbc = cb > 0.68 ? f * ((cb + 0.68) / 1.36) - f : 0;
        double R = L < 120 ? L / 0.48 : 250;
        double dc = D > L / 15 ? (D - L / 15) * R : 0; // мм (D в м · R мм/м)
        double min = f + cbc + dc;
        double actual = (D + deckThickness - T) * 1000;
        return new Result(f, cbc, dc, min, actual, actual >= min, D + deckThickness - min / 1000);
    }
}
