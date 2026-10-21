using System.IO;
using System.Windows;
using ShipDesigner.Core;
namespace ShipDesigner;

public static class App
{
    public static AppSettings Settings = AppSettings.Load();
    public static ShipProject Project = new();
    public static Hull Hull;
    public static HydroCalc Calc;
    public static HydroRow Design;
    public static StabilityResult Stab;
    public static ResistancePoint Resist;
    public static Freeboard.Result Fb;
    public static string ProjectPath;
    public static MainWindow Main;
    public static event Action HullChanged;

    public static void Rebuild()
    {
        Hull = Project.Hull.Build();
        Calc = new HydroCalc(Hull, Project.Rho, 81, 60);
        double T = Project.Hull.Kind == "offsets" ? Hull.D * 0.6 : Math.Min(Project.Hull.T, Hull.D);
        Design = Calc.Upright(T);
        Project.Load ??= LoadCase.Default(Hull, Design.Displacement);
        Stab = null; Resist = null; Fb = null;
        HullChanged?.Invoke();
    }
    public static double DesignDraft => Design?.T ?? Project.Hull.T;
    public static string Context() => Report.Full(Project, Hull, Design, Stab, Resist, Fb);
    public static string DataDir { get { var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShipDesigner"); Directory.CreateDirectory(d); return d; } }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(e.Exception.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true; };
        try { if (args.Length > 0 && File.Exists(args[0])) { App.Project = ShipProject.FromJson(File.ReadAllText(args[0])); App.ProjectPath = args[0]; } } catch { }
        App.Rebuild();
        App.Main = new MainWindow();
        app.Run(App.Main);
    }
}
