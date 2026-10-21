using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static ShipDesigner.Ui;
namespace ShipDesigner;

public class AiPage : ContentControl
{
    readonly ComboBox prov = Combo(Ai.Providers.Append("Все подключённые — сравнить").ToArray());
    readonly TextBox chat = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14, Padding = new Thickness(8), FontFamily = new FontFamily("Segoe UI") };
    readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70, FontSize = 14, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly CheckBox ctx = new() { Content = "Передавать данные текущих расчётов", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
    readonly Dictionary<string, List<ChatMsg>> hist = new();
    bool busy;
    public AiPage()
    {
        prov.SelectedItem = App.Settings.Provider; if (prov.SelectedIndex < 0) prov.SelectedIndex = 0;
        var quick = Row(
            B("Объясни результаты", () => Send("Объясни простыми словами результаты расчёта моего судна: гидростатику, посадку, остойчивость и мощность. Что хорошо, что вызывает вопросы?")),
            B("Проверь остойчивость", () => Send("Проверь остойчивость: выполнены ли критерии ИМО IS Code 2008, достаточен ли запас GM и площадей под диаграммой, нет ли риска излишней остойчивости (резкой качки)? Дай вывод.")),
            B("Предложи изменения", () => Send("Предложи конкретные изменения проекта (размерения, Cb, размещение грузов, KG, балласт, обводы), чтобы улучшить остойчивость и ходкость. Оцени количественно влияние каждого изменения.")),
            B("Оцени сопротивление", () => Send("Проанализируй сопротивление и потребную мощность: соответствует ли число Фруда типу судна, как снизить сопротивление, какой двигатель подобрать?")),
            B("Очистить диалог", () => { hist.Clear(); chat.Clear(); })
        );
        var send = B("Отправить (Ctrl+Enter)", () => Send(input.Text), true);
        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control) { e.Handled = true; Send(input.Text); } };
        var top = V(H("ИИ-помощник инженера"), Row(T("Модель:"), prov, ctx), T("Перед вопросом выполните нужные расчёты (гидростатика считается сразу; остойчивость, сопротивление и надводный борт — на своих вкладках): их результаты передаются ИИ как контекст.", 12), quick);
        var bottom = V(input, Row(send, B("Показать контекст", () => MessageBox.Show(App.Context(), "Данные для ИИ"))));
        var g = new Grid { Margin = new Thickness(10) };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(chat, 1); Grid.SetRow(bottom, 2); g.Children.Add(top); g.Children.Add(chat); g.Children.Add(bottom);
        Content = g;
    }
    bool Configured(string p) { var s = App.Settings; return p switch { "GigaChat" => s.GigaKey != "", "DeepSeek" => s.DeepSeekKey != "", "YandexGPT" => s.YandexKey != "" && s.YandexFolder != "", _ => s.OpenAiKey != "" }; }
    async void Send(string q)
    {
        if (busy || string.IsNullOrWhiteSpace(q)) return;
        busy = true; input.Clear();
        string sel = prov.SelectedItem as string;
        if (Ai.Providers.Contains(sel)) { App.Settings.Provider = sel; App.Settings.Save(); }
        var targets = Ai.Providers.Contains(sel) ? new[] { sel } : Ai.Providers.Where(Configured).ToArray();
        if (targets.Length == 0) { chat.AppendText("⚠ Нет ни одной настроенной модели — добавьте ключи во вкладке «Настройки ИИ».\n\n"); busy = false; return; }
        string content = ctx.IsChecked == true ? $"Данные расчёта:\n{App.Context()}\nВопрос: {q}" : q;
        chat.AppendText($"👤 Вы: {q}\n\n"); chat.ScrollToEnd();
        var tasks = targets.Select(async p =>
        {
            if (!hist.TryGetValue(p, out var h)) hist[p] = h = new();
            var msgs = h.Concat(new[] { new ChatMsg("user", content) }).ToList();
            try { var a = await Ai.Ask(p, msgs); h.Add(new ChatMsg("user", content)); h.Add(new ChatMsg("assistant", a)); if (h.Count > 12) h.RemoveRange(0, 2); return (p, a, true); }
            catch (Exception e) { return (p, e.Message, false); }
        }).ToList();
        chat.AppendText($"⏳ Ждём ответ: {string.Join(", ", targets)}…\n\n"); chat.ScrollToEnd();
        foreach (var t in tasks)
        {
            var (p, a, ok) = await t;
            chat.AppendText(ok ? $"🤖 {p}:\n{a}\n\n" : $"⚠ {p}: {a}\n\n"); chat.ScrollToEnd();
        }
        busy = false;
    }
}

public class SettingsPage : ContentControl
{
    public SettingsPage()
    {
        var s = App.Settings;
        TextBox Tb(string v, bool pwd = false) => new() { Text = v, Width = 420, FontSize = 13, Margin = new Thickness(4, 2, 4, 2) };
        UIElement L(string label, UIElement c) => new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = label, Width = 200, VerticalAlignment = VerticalAlignment.Center }, c } };
        var gk = Tb(s.GigaKey); var gs = Combo("GIGACHAT_API_PERS", "GIGACHAT_API_B2B", "GIGACHAT_API_CORP"); gs.SelectedItem = s.GigaScope; var gm = Combo("GigaChat", "GigaChat-Pro", "GigaChat-Max", "GigaChat-2", "GigaChat-2-Pro", "GigaChat-2-Max"); gm.IsEditable = true; gm.Text = s.GigaModel;
        var gt = new CheckBox { Content = "Не проверять сертификат (если не установлен сертификат НУЦ Минцифры — небезопасно)", IsChecked = s.GigaTrustAll };
        var dk = Tb(s.DeepSeekKey); var dm = Combo("deepseek-chat", "deepseek-reasoner"); dm.IsEditable = true; dm.Text = s.DeepSeekModel;
        var yk = Tb(s.YandexKey); var yf = Tb(s.YandexFolder); var ym = Combo("yandexgpt/latest", "yandexgpt-lite/latest", "yandexgpt/rc"); ym.IsEditable = true; ym.Text = s.YandexModel;
        var ob = Tb(s.OpenAiBase); var ok = Tb(s.OpenAiKey); var om = Tb(s.OpenAiModel);
        var temp = new Num("Температура (0–1)", s.Temperature);
        var status = T();
        Content = Scroll(V(H("Настройки ИИ"), T("Ключи хранятся только на этом компьютере в %APPDATA%\\ShipDesigner\\settings.json."),
            H("GigaChat (Сбер)", 15), T("developers.sber.ru → Личный кабинет → GigaChat API → «Получить ключ» → скопируйте Authorization key.", 12), L("Authorization key:", gk), L("Scope:", gs), L("Модель:", gm), gt,
            H("DeepSeek", 15), T("platform.deepseek.com → API keys.", 12), L("API-ключ:", dk), L("Модель:", dm),
            H("YandexGPT", 15), T("Yandex Cloud: сервисный аккаунт с ролью ai.languageModels.user → API-ключ; ID каталога.", 12), L("API-ключ:", yk), L("ID каталога (folder id):", yf), L("Модель:", ym),
            H("OpenAI-совместимый API", 15), T("OpenAI, OpenRouter, Mistral, локальный Ollama/LM Studio (http://localhost:11434/v1) и др.", 12), L("Базовый URL:", ob), L("API-ключ:", ok), L("Модель:", om),
            temp.Element,
            Row(B("Сохранить", () =>
            {
                s.GigaKey = gk.Text.Trim(); s.GigaScope = gs.SelectedItem as string ?? "GIGACHAT_API_PERS"; s.GigaModel = gm.Text.Trim(); s.GigaTrustAll = gt.IsChecked == true;
                s.DeepSeekKey = dk.Text.Trim(); s.DeepSeekModel = dm.Text.Trim(); s.YandexKey = yk.Text.Trim(); s.YandexFolder = yf.Text.Trim(); s.YandexModel = ym.Text.Trim();
                s.OpenAiBase = ob.Text.Trim(); s.OpenAiKey = ok.Text.Trim(); s.OpenAiModel = om.Text.Trim(); s.Temperature = Math.Clamp(temp.Value, 0, 1);
                s.Save(); status.Text = "Сохранено ✔";
            }, true), B("Проверить подключения", async () =>
            {
                status.Text = "Проверяем…";
                var sb = new System.Text.StringBuilder();
                foreach (var p in Ai.Providers)
                {
                    try { var a = await Ai.Ask(p, new() { new ChatMsg("user", "Ответь одним словом: работает?") }); sb.AppendLine($"✔ {p}: {a.Trim()}"); }
                    catch (Exception e) { sb.AppendLine($"✘ {p}: {e.Message}"); }
                }
                status.Text = sb.ToString();
            })), status));
    }
}
