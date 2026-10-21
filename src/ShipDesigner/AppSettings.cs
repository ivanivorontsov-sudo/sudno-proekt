using System.IO;
using System.Text.Json;
namespace ShipDesigner;

public class AppSettings
{
    public string Provider { get; set; } = "GigaChat";
    public string GigaKey { get; set; } = ""; public string GigaScope { get; set; } = "GIGACHAT_API_PERS"; public string GigaModel { get; set; } = "GigaChat"; public bool GigaTrustAll { get; set; }
    public string DeepSeekKey { get; set; } = ""; public string DeepSeekModel { get; set; } = "deepseek-chat";
    public string OpenAiBase { get; set; } = "https://api.openai.com/v1"; public string OpenAiKey { get; set; } = ""; public string OpenAiModel { get; set; } = "gpt-4o-mini";
    public string YandexKey { get; set; } = ""; public string YandexFolder { get; set; } = ""; public string YandexModel { get; set; } = "yandexgpt/latest";
    public double Temperature { get; set; } = 0.3;
    static string Path_ => Path.Combine(App.DataDir, "settings.json");
    public static AppSettings Load() { try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path_)) ?? new(); } catch { return new(); } }
    public void Save() => File.WriteAllText(Path_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
