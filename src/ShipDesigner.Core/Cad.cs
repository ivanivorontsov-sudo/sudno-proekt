using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace ShipDesigner.Core;

public record struct P2(double X, double Y)
{
    public static P2 operator +(P2 a, P2 b) => new(a.X + b.X, a.Y + b.Y);
    public static P2 operator -(P2 a, P2 b) => new(a.X - b.X, a.Y - b.Y);
    public static P2 operator *(P2 a, double k) => new(a.X * k, a.Y * k);
    public double Len => Math.Sqrt(X * X + Y * Y);
    public static double Dist(P2 a, P2 b) => (a - b).Len;
}

public class Layer { public string Name { get; set; } = "0"; public string Color { get; set; } = "#000000"; public bool Visible { get; set; } = true; public bool Locked { get; set; } }

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LineEnt), "line")]
[JsonDerivedType(typeof(CircleEnt), "circle")]
[JsonDerivedType(typeof(ArcEnt), "arc")]
[JsonDerivedType(typeof(PolylineEnt), "polyline")]
[JsonDerivedType(typeof(TextEnt), "text")]
[JsonDerivedType(typeof(DimEnt), "dim")]
public abstract class Entity
{
    public string Layer { get; set; } = "0";
    public abstract IEnumerable<P2> SnapPoints();
    public abstract double Distance(P2 p);
    public abstract (P2 min, P2 max) Bounds();
    public abstract void Move(P2 d);
}
public class LineEnt : Entity
{
    public P2 A { get; set; } public P2 B { get; set; }
    public override IEnumerable<P2> SnapPoints() { yield return A; yield return B; yield return (A + B) * 0.5; }
    public override double Distance(P2 p) => SegDist(p, A, B);
    public static double SegDist(P2 p, P2 a, P2 b)
    {
        var ab = b - a; double l2 = ab.X * ab.X + ab.Y * ab.Y; if (l2 < 1e-18) return P2.Dist(p, a);
        double t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / l2, 0, 1); return P2.Dist(p, a + ab * t);
    }
    public override (P2, P2) Bounds() => (new(Math.Min(A.X, B.X), Math.Min(A.Y, B.Y)), new(Math.Max(A.X, B.X), Math.Max(A.Y, B.Y)));
    public override void Move(P2 d) { A += d; B += d; }
}
public class CircleEnt : Entity
{
    public P2 C { get; set; } public double R { get; set; }
    public override IEnumerable<P2> SnapPoints() { yield return C; yield return C + new P2(R, 0); yield return C + new P2(-R, 0); yield return C + new P2(0, R); yield return C + new P2(0, -R); }
    public override double Distance(P2 p) => Math.Abs(P2.Dist(p, C) - R);
    public override (P2, P2) Bounds() => (C - new P2(R, R), C + new P2(R, R));
    public override void Move(P2 d) => C += d;
}
/// <summary>Дуга против часовой стрелки от Start до End (градусы).</summary>
public class ArcEnt : Entity
{
    public P2 C { get; set; } public double R { get; set; } public double Start { get; set; } public double End { get; set; }
    public P2 At(double deg) => C + new P2(R * Math.Cos(deg * Math.PI / 180), R * Math.Sin(deg * Math.PI / 180));
    public double Sweep => ((End - Start) % 360 + 360) % 360;
    public override IEnumerable<P2> SnapPoints() { yield return C; yield return At(Start); yield return At(End); yield return At(Start + Sweep / 2); }
    public override double Distance(P2 p)
    {
        double a = Math.Atan2(p.Y - C.Y, p.X - C.X) * 180 / Math.PI; double rel = ((a - Start) % 360 + 360) % 360;
        return rel <= Sweep ? Math.Abs(P2.Dist(p, C) - R) : Math.Min(P2.Dist(p, At(Start)), P2.Dist(p, At(End)));
    }
    public override (P2, P2) Bounds() => (C - new P2(R, R), C + new P2(R, R));
    public override void Move(P2 d) => C += d;
    /// <summary>Дуга через три точки.</summary>
    public static ArcEnt Through(P2 a, P2 b, P2 c)
    {
        double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-12) return null;
        double ux = ((a.X * a.X + a.Y * a.Y) * (b.Y - c.Y) + (b.X * b.X + b.Y * b.Y) * (c.Y - a.Y) + (c.X * c.X + c.Y * c.Y) * (a.Y - b.Y)) / d;
        double uy = ((a.X * a.X + a.Y * a.Y) * (c.X - b.X) + (b.X * b.X + b.Y * b.Y) * (a.X - c.X) + (c.X * c.X + c.Y * c.Y) * (b.X - a.X)) / d;
        var ctr = new P2(ux, uy); double r = P2.Dist(ctr, a);
        double ang(P2 p) => Math.Atan2(p.Y - uy, p.X - ux) * 180 / Math.PI;
        double s = ang(a), m = ang(b), e = ang(c);
        double sweepE = ((e - s) % 360 + 360) % 360, sweepM = ((m - s) % 360 + 360) % 360;
        return sweepM <= sweepE ? new ArcEnt { C = ctr, R = r, Start = s, End = e } : new ArcEnt { C = ctr, R = r, Start = e, End = s };
    }
}
public class PolylineEnt : Entity
{
    public List<P2> Points { get; set; } = new(); public bool Closed { get; set; }
    public override IEnumerable<P2> SnapPoints() => Points;
    public override double Distance(P2 p)
    {
        double m = double.MaxValue; for (int i = 0; i + 1 < Points.Count; i++) m = Math.Min(m, LineEnt.SegDist(p, Points[i], Points[i + 1]));
        if (Closed && Points.Count > 2) m = Math.Min(m, LineEnt.SegDist(p, Points[^1], Points[0])); return m;
    }
    public override (P2, P2) Bounds() => (new(Points.Min(q => q.X), Points.Min(q => q.Y)), new(Points.Max(q => q.X), Points.Max(q => q.Y)));
    public override void Move(P2 d) { for (int i = 0; i < Points.Count; i++) Points[i] += d; }
}
public class TextEnt : Entity
{
    public P2 P { get; set; } public string Text { get; set; } = ""; public double Height { get; set; } = 2.5;
    public override IEnumerable<P2> SnapPoints() { yield return P; }
    public override double Distance(P2 p) { var (a, b) = Bounds(); return p.X >= a.X && p.X <= b.X && p.Y >= a.Y && p.Y <= b.Y ? 0 : P2.Dist(p, P); }
    public override (P2, P2) Bounds() => (P, P + new P2(Height * 0.6 * Math.Max(1, Text.Length), Height));
    public override void Move(P2 d) => P += d;
}
/// <summary>Параллельный (выровненный) размер между A и B с выносом Offset.</summary>
public class DimEnt : Entity
{
    public P2 A { get; set; } public P2 B { get; set; } public double Offset { get; set; } = 5; public double TextHeight { get; set; } = 2.5;
    public double Value => P2.Dist(A, B);
    public P2 Normal { get { var d = B - A; double l = d.Len; return l < 1e-12 ? new P2(0, 1) : new P2(-d.Y / l, d.X / l); } }
    public P2 A2 => A + Normal * Offset; public P2 B2 => B + Normal * Offset;
    public string Label => Value.ToString("0.##", CultureInfo.InvariantCulture);
    public override IEnumerable<P2> SnapPoints() { yield return A; yield return B; }
    public override double Distance(P2 p) => Math.Min(LineEnt.SegDist(p, A2, B2), Math.Min(LineEnt.SegDist(p, A, A2), LineEnt.SegDist(p, B, B2)));
    public override (P2, P2) Bounds() { var pts = new[] { A, B, A2, B2 }; return (new(pts.Min(q => q.X), pts.Min(q => q.Y)), new(pts.Max(q => q.X), pts.Max(q => q.Y))); }
    public override void Move(P2 d) { A += d; B += d; }
}

public class Drawing
{
    public List<Layer> Layers { get; set; } = new() { new Layer { Name = "0" } };
    public List<Entity> Entities { get; set; } = new();
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public string ToJson() => JsonSerializer.Serialize(this, Opt);
    public static Drawing FromJson(string s) => JsonSerializer.Deserialize<Drawing>(s, Opt);
    public Layer GetLayer(string name) { var l = Layers.FirstOrDefault(x => x.Name == name); if (l == null) { l = new Layer { Name = name }; Layers.Add(l); } return l; }
    public (P2 min, P2 max) Extents()
    {
        var vis = Entities.Where(e => GetLayer(e.Layer).Visible).ToList();
        if (vis.Count == 0) return (new P2(0, 0), new P2(100, 100));
        var bs = vis.Select(e => e.Bounds()).ToList();
        return (new P2(bs.Min(b => b.min.X), bs.Min(b => b.min.Y)), new P2(bs.Max(b => b.max.X), bs.Max(b => b.max.Y)));
    }
}
