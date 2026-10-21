using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace ShipDesigner;

public record ChatMsg(string Role, string Content);

/// <summary>Клиенты ИИ: GigaChat (Сбер), DeepSeek, YandexGPT, любой OpenAI-совместимый API.</summary>
public static class Ai
{
    public static readonly string[] Providers = { "GigaChat", "DeepSeek", "YandexGPT", "OpenAI-совместимый" };
    public const string System = "Ты — опытный инженер-кораблестроитель и консультант по теории корабля (гидростатика, остойчивость, ходкость, прочность, Правила РС, IS Code 2008, МК ГМ-66). Отвечай по-русски, по делу, с формулами и числами. Используй переданные данные расчёта; если данных не хватает — скажи, каких. Предупреждай, что расчёты предварительные.";
    static HttpClient _plain; static HttpClient _giga; static bool _gigaTrust;
    static string _gigaToken; static DateTime _gigaExp; static string _gigaKeyUsed;
    static HttpClient Plain => _plain ??= new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    static HttpClient Giga(bool trustAll)
    {
        if (_giga == null || _gigaTrust != trustAll)
        {
            var h = new HttpClientHandler();
            if (trustAll) h.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            _giga = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(120) }; _gigaTrust = trustAll; _gigaToken = null;
        }
        return _giga;
    }

    public static Task<string> Ask(string provider, List<ChatMsg> messages) => provider switch
    {
        "GigaChat" => AskGiga(messages),
        "DeepSeek" => AskOpenAi("https://api.deepseek.com", App.Settings.DeepSeekKey, App.Settings.DeepSeekModel, messages, "DeepSeek"),
        "YandexGPT" => AskYandex(messages),
        _ => AskOpenAi(App.Settings.OpenAiBase, App.Settings.OpenAiKey, App.Settings.OpenAiModel, messages, "OpenAI-совместимый"),
    };

    static JsonArray Msgs(List<ChatMsg> m, string textKey = "content")
    {
        var a = new JsonArray { new JsonObject { ["role"] = "system", [textKey] = System } };
        foreach (var x in m) a.Add(new JsonObject { ["role"] = x.Role, [textKey] = x.Content });
        return a;
    }
    static async Task<JsonNode> Post(HttpClient c, string url, JsonNode body, Action<HttpRequestHeaders> hdr)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        hdr(req.Headers);
        using var r = await c.SendAsync(req);
        var s = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) throw new Exception($"HTTP {(int)r.StatusCode}: {Trim(s)}");
        return JsonNode.Parse(s);
    }
    static string Trim(string s) => s.Length > 400 ? s[..400] + "…" : s;

    static async Task<string> AskOpenAi(string baseUrl, string key, string model, List<ChatMsg> m, string name)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new Exception($"Не задан API-ключ {name} (вкладка «Настройки ИИ»).");
        var url = baseUrl.TrimEnd('/'); if (!url.EndsWith("/chat/completions")) url += "/chat/completions";
        var body = new JsonObject { ["model"] = model, ["messages"] = Msgs(m), ["temperature"] = App.Settings.Temperature };
        var j = await Post(Plain, url, body, h => h.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim()));
        return j?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "(пустой ответ)";
    }

    static async Task<string> AskGiga(List<ChatMsg> m)
    {
        var s = App.Settings;
        if (string.IsNullOrWhiteSpace(s.GigaKey)) throw new Exception("Не задан Authorization key GigaChat (developers.sber.ru → GigaChat API → «Получить ключ»). Вставьте его во вкладке «Настройки ИИ».");
        var c = Giga(s.GigaTrustAll);
        try
        {
            if (_gigaToken == null || DateTime.UtcNow > _gigaExp || _gigaKeyUsed != s.GigaKey)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://ngw.devices.sberbank.ru:9443/api/v2/oauth")
                { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["scope"] = s.GigaScope }) };
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", s.GigaKey.Trim().Replace("Basic ", ""));
                req.Headers.Add("RqUID", Guid.NewGuid().ToString()); req.Headers.Accept.ParseAdd("application/json");
                using var r = await c.SendAsync(req); var txt = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) throw new Exception($"Авторизация GigaChat: HTTP {(int)r.StatusCode}: {Trim(txt)}");
                var j = JsonNode.Parse(txt); _gigaToken = j["access_token"].GetValue<string>(); _gigaExp = DateTime.UtcNow.AddMinutes(25); _gigaKeyUsed = s.GigaKey;
            }
            var body = new JsonObject { ["model"] = s.GigaModel, ["messages"] = Msgs(m), ["temperature"] = Math.Max(0.01, s.Temperature) };
            var res = await Post(c, "https://gigachat.devices.sberbank.ru/api/v1/chat/completions", body, h => h.Authorization = new AuthenticationHeaderValue("Bearer", _gigaToken));
            return res?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "(пустой ответ)";
        }
        catch (HttpRequestException e) when (e.InnerException is System.Security.Authentication.AuthenticationException || e.Message.Contains("SSL"))
        {
            throw new Exception("Ошибка сертификата GigaChat. Установите корневой сертификат НУЦ Минцифры (gosuslugi.ru/crt) или включите «Не проверять сертификат» в настройках.");
        }
    }

    static async Task<string> AskYandex(List<ChatMsg> m)
    {
        var s = App.Settings;
        if (string.IsNullOrWhiteSpace(s.YandexKey) || string.IsNullOrWhiteSpace(s.YandexFolder)) throw new Exception("Для YandexGPT нужны API-ключ сервисного аккаунта и ID каталога (Yandex Cloud).");
        var body = new JsonObject
        {
            ["modelUri"] = $"gpt://{s.YandexFolder.Trim()}/{s.YandexModel.Trim()}",
            ["completionOptions"] = new JsonObject { ["stream"] = false, ["temperature"] = s.Temperature, ["maxTokens"] = "4000" },
            ["messages"] = Msgs(m, "text")
        };
        var j = await Post(Plain, "https://llm.api.cloud.yandex.net/foundationModels/v1/completion", body, h => { h.Authorization = new AuthenticationHeaderValue("Api-Key", s.YandexKey.Trim()); h.Add("x-folder-id", s.YandexFolder.Trim()); });
        return j?["result"]?["alternatives"]?[0]?["message"]?["text"]?.GetValue<string>() ?? "(пустой ответ)";
    }
}
