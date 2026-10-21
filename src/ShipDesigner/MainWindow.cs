using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ShipDesigner.Core;
namespace ShipDesigner;

public class MainWindow : Window
{
    public TabControl Tabs = new() { FontSize = 13 };
    public CadPage Cad;
    public MainWindow()
    {
        Title = "Судно-Проект — расчёт судов и чертежи"; Width = 1360; Height = 860; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/app.ico", UriKind.Absolute)); } catch { }
        var menu = new Menu { FontSize = 13 };
        var file = new MenuItem { Header = "_Файл" };
        file.Items.Add(Mi("Новый проект", () => { App.Project = new ShipProject(); App.ProjectPath = null; App.Rebuild(); UpdateTitle(); }));
        file.Items.Add(Mi("Открыть проект…", Open));
        file.Items.Add(Mi("Сохранить", () => Save(false)));
        file.Items.Add(Mi("Сохранить как…", () => Save(true)));
        file.Items.Add(new Separator());
        file.Items.Add(Mi("Выход", Close));
        var help = new MenuItem { Header = "_Справка" };
        help.Items.Add(Mi("О программе", () => MessageBox.Show(About, "О программе")));
        menu.Items.Add(file); menu.Items.Add(help);
        Cad = new CadPage();
        Tabs.Items.Add(new TabItem { Header = "🚢 Корпус", Content = new HullPage() });
        Tabs.Items.Add(new TabItem { Header = "📊 Гидростатика", Content = new HydroPage() });
        Tabs.Items.Add(new TabItem { Header = "📈 Бонжан", Content = new BonjeanPage() });
        Tabs.Items.Add(new TabItem { Header = "⚖ Нагрузка и остойчивость", Content = new StabilityPage() });
        Tabs.Items.Add(new TabItem { Header = "🌊 Сопротивление и мощность", Content = new ResistancePage() });
        Tabs.Items.Add(new TabItem { Header = "📏 Надводный борт", Content = new FreeboardPage() });
        Tabs.Items.Add(new TabItem { Header = "✏ Чертежи", Content = Cad });
        Tabs.Items.Add(new TabItem { Header = "🤖 ИИ-помощник", Content = new AiPage() });
        Tabs.Items.Add(new TabItem { Header = "⚙ Настройки ИИ", Content = new SettingsPage() });
        var dp = new DockPanel(); DockPanel.SetDock(menu, Dock.Top); dp.Children.Add(menu); dp.Children.Add(Tabs);
        Content = dp; UpdateTitle();
    }
    const string About = "Судно-Проект 1.0\n\nГидростатика, кривые Бонжана, пантокарены и диаграмма статической остойчивости, критерии ИМО IS Code 2008, посадка по нагрузке, сопротивление по Holtrop–Mennen, надводный борт (упрощённо), 2D-чертёжный модуль с экспортом DXF/PDF/PNG и ИИ-помощник (GigaChat, DeepSeek, YandexGPT, OpenAI-совместимые).\n\nРасчёты учебные/предварительные — для проектной документации проверяйте сертифицированным ПО и по правилам РС.";
    static MenuItem Mi(string h, Action a) { var m = new MenuItem { Header = h }; m.Click += (_, _) => { try { a(); } catch (Exception e) { MessageBox.Show(e.Message, "Ошибка"); } }; return m; }
    void UpdateTitle() => Title = $"Судно-Проект — {App.Project.Name}" + (App.ProjectPath != null ? $" ({Path.GetFileName(App.ProjectPath)})" : "");
    void Open()
    {
        var d = new OpenFileDialog { Filter = "Проект судна (*.shipproj)|*.shipproj|Все файлы|*.*" };
        if (d.ShowDialog() != true) return;
        App.Project = ShipProject.FromJson(File.ReadAllText(d.FileName)); App.ProjectPath = d.FileName; App.Rebuild();
        if (App.Project.Drawing != null) Cad.Canvas.SetDrawing(App.Project.Drawing);
        UpdateTitle();
    }
    void Save(bool ask)
    {
        if (ask || App.ProjectPath == null)
        {
            var d = new SaveFileDialog { Filter = "Проект судна (*.shipproj)|*.shipproj", FileName = App.Project.Name };
            if (d.ShowDialog() != true) return; App.ProjectPath = d.FileName;
        }
        App.Project.Drawing = Cad.Canvas.Drawing;
        File.WriteAllText(App.ProjectPath, App.Project.ToJson()); UpdateTitle();
    }
}
