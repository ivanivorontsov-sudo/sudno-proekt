namespace ShipDesigner.Core;

/// <summary>Теоретический чертёж: «бок» (профиль с батоксами), «полуширота» (ватерлинии), «корпус» (шпангоуты). Единицы — метры.</summary>
public static class LinesPlan
{
    public static Drawing Generate(Hull h, double T, int stations = 20, int waterlines = 6, int buttocks = 3)
    {
        var d = new Drawing();
        d.Layers = new List<Layer> {
            new() { Name = "0" }, new() { Name = "Сетка", Color = "#9E9E9E" }, new() { Name = "Шпангоуты", Color = "#1565C0" },
            new() { Name = "Ватерлинии", Color = "#2E7D32" }, new() { Name = "Батоксы", Color = "#C62828" }, new() { Name = "Контур", Color = "#000000" },
            new() { Name = "Надписи", Color = "#424242" }, new() { Name = "Размеры", Color = "#6A1B9A" } };
        double L = h.L, B = h.B, D = h.D, gap = Math.Max(B, D) * 0.6;
        double th = Math.Max(0.12, Math.Max(L, 2 * B) / 160);
        // --- Бок (профиль): начало (0,0), x вдоль L, y — высота
        var side = new P2(0, 0);
        var half = new P2(0, -(B / 2 + gap));               // полуширота под боком
        var body = new P2(L + gap + B / 2, 0);              // корпус справа
        void Ln(string layer, P2 a, P2 b) => d.Entities.Add(new LineEnt { Layer = layer, A = a, B = b });
        void Pl(string layer, IEnumerable<P2> pts) { var l = pts.ToList(); if (l.Count > 1) d.Entities.Add(new PolylineEnt { Layer = layer, Points = l }); }
        void Tx(P2 p, string s) => d.Entities.Add(new TextEnt { Layer = "Надписи", P = p, Text = s, Height = th * 1.5 });
        // сетка шпангоутов
        for (int i = 0; i <= stations; i++)
        {
            double x = L * i / stations;
            Ln("Сетка", side + new P2(x, 0), side + new P2(x, D));
            Ln("Сетка", half + new P2(x, 0), half + new P2(x, B / 2));
            Tx(half + new P2(x - th, -th * 3), i.ToString());
        }
        var wls = Enumerable.Range(0, waterlines + 1).Select(k => T * k / waterlines).Where(z => z > 1e-6).ToList();
        if (D > T + 1e-6) wls.Add(D);
        foreach (var z in wls)
        {
            Ln("Сетка", side + new P2(0, z), side + new P2(L, z));
            Ln("Сетка", body + new P2(-B / 2, z), body + new P2(B / 2, z));
        }
        Ln("Сетка", body + new P2(0, 0), body + new P2(0, D));
        Ln("Сетка", half + new P2(0, 0), half + new P2(L, 0));
        var bts = Enumerable.Range(1, buttocks).Select(k => B / 2 * k / (buttocks + 1)).ToList();
        foreach (var yb in bts) { Ln("Сетка", half + new P2(0, yb), half + new P2(L, yb)); Ln("Сетка", body + new P2(yb, 0), body + new P2(yb, D)); Ln("Сетка", body + new P2(-yb, 0), body + new P2(-yb, D)); }
        int n = 200;
        // ватерлинии на полушироте
        foreach (var z in wls) Pl("Ватерлинии", Enumerable.Range(0, n + 1).Select(k => { double x = L * k / n; return half + new P2(x, h.HalfBreadth(x, z)); }));
        // палуба (наибольшая ширина) на полушироте
        Pl("Контур", Enumerable.Range(0, n + 1).Select(k => { double x = L * k / n; return half + new P2(x, MaxHB(h, x)); }));
        // профиль: киль/контур по диаметральной плоскости
        var keel = new List<P2>(); var deck = new List<P2>();
        for (int k = 0; k <= n; k++) { double x = L * k / n; var (zmin, ok) = Lowest(h, x, 1e-4); if (ok) keel.Add(side + new P2(x, zmin)); deck.Add(side + new P2(x, D)); }
        Pl("Контур", keel); Ln("Контур", side + new P2(0, D), side + new P2(L, D));
        if (keel.Count > 0) { Ln("Контур", keel[0], side + new P2(keel[0].X, D)); Ln("Контур", keel[^1], side + new P2(keel[^1].X, D)); }
        // батоксы на боку
        foreach (var yb in bts)
        {
            var seg = new List<P2>();
            for (int k = 0; k <= n; k++)
            {
                double x = L * k / n; var (zb, ok) = Lowest(h, x, yb);
                if (ok) seg.Add(side + new P2(x, zb)); else { Pl("Батоксы", seg); seg = new(); }
            }
            Pl("Батоксы", seg);
        }
        // корпус: корма (x<L/2) слева, нос справа
        int nz = 60;
        for (int i = 0; i <= stations; i++)
        {
            double x = L * i / stations; bool aft = x < L / 2 - 1e-9; double s = aft ? -1 : 1;
            var pts = Enumerable.Range(0, nz + 1).Select(j => { double z = D * j / nz; return body + new P2(s * h.HalfBreadth(x, z), z); }).Where(p => true).ToList();
            Pl("Шпангоуты", pts);
            if (Math.Abs(x - L / 2) < 1e-9) Pl("Шпангоуты", pts.Select(p => new P2(2 * body.X - p.X, p.Y)));
        }
        // ГВЛ и подписи
        Ln("Ватерлинии", side + new P2(-gap * 0.2, T), side + new P2(L + gap * 0.2, T));
        Tx(side + new P2(L + gap * 0.25, T), $"КВЛ T={T:0.###}");
        Tx(side + new P2(0, D + th * 4), "БОК"); Tx(half + new P2(0, B / 2 + th * 4), "ПОЛУШИРОТА"); Tx(body + new P2(-B / 2, D + th * 4), "КОРПУС");
        Tx(side + new P2(0, -th * 3), "КП"); Tx(side + new P2(L - th * 3, -th * 3), "НП");
        d.Entities.Add(new DimEnt { Layer = "Размеры", A = side + new P2(0, D), B = side + new P2(L, D), Offset = gap * 0.4, TextHeight = th * 1.5 });
        d.Entities.Add(new DimEnt { Layer = "Размеры", A = body + new P2(-B / 2, 0), B = body + new P2(B / 2, 0), Offset = -gap * 0.3, TextHeight = th * 1.5 });
        d.Entities.Add(new DimEnt { Layer = "Размеры", A = body + new P2(B / 2 + gap * 0.1, 0), B = body + new P2(B / 2 + gap * 0.1, D), Offset = -gap * 0.2, TextHeight = th * 1.5 });
        Tx(new P2(0, half.Y - th * 10), $"{h.Name}. L={L:0.##} м, B={B:0.##} м, D={D:0.##} м, T={T:0.##} м");
        return d;
    }
    static double MaxHB(Hull h, double x) { double m = 0; for (int j = 0; j <= 40; j++) m = Math.Max(m, h.HalfBreadth(x, h.D * j / 40)); return m; }
    /// <summary>Наименьшая высота z, на которой полуширота ≥ y.</summary>
    static (double, bool) Lowest(Hull h, double x, double y)
    {
        int m = 200; double prevZ = 0; bool prevIn = h.HalfBreadth(x, 0) >= y;
        if (prevIn) return (0, true);
        for (int j = 1; j <= m; j++)
        {
            double z = h.D * j / m; double hb = h.HalfBreadth(x, z);
            if (hb >= y)
            {
                double hb0 = h.HalfBreadth(x, prevZ); double t = hb - hb0 > 1e-12 ? (y - hb0) / (hb - hb0) : 1;
                return (prevZ + t * (z - prevZ), true);
            }
            prevZ = z;
        }
        return (0, false);
    }
}
