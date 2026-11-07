using System.Text.Json;
using System.Text.Json.Serialization;

namespace OverlayPoc;

/// <summary>
/// OpenRouter connection settings. Loaded from %LOCALAPPDATA%\OverlayPoc\config.json
/// with OPENROUTER_API_KEY taking priority for the key.
/// </summary>
internal sealed class LlmConfig
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string Model { get; set; } = "openrouter/auto";
    public string SystemPrompt { get; set; } =
        "You are a precise, objective assistant. Answer accurately and concisely. If you are not sure, say so instead of guessing.";
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Model for vision/screenshot requests. Defaults to deepseek.</summary>
    public string VisionModel { get; set; } = "deepseek/deepseek-chat-v3-0324";

    /// <summary>Prompt used when sending a screenshot to the vision model.</summary>
    public string VisionPrompt { get; set; } =
        "Analyze this screenshot. If you see a question with multiple choice options: " +
        "1. First line: state the answer as a numbered option (e.g., \"2. Option text\"). " +
        "2. Keep the first line as brief as possible. " +
        "3. If no question is visible, describe what you see concisely.";

    /// <summary>After copying the current selection, send Right Arrow to collapse
    /// the highlight so it does not linger on screen. Set false to disable.</summary>
    public bool DeselectAfterCopy { get; set; } = true;

    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}

internal static class LlmConfigLoader
{
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverlayPoc");

    public static string ConfigPath { get; } = Path.Combine(DirectoryPath, "config.json");

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static LlmConfig Load()
    {
        var config = new LlmConfig();

        try
        {
            if (File.Exists(ConfigPath))
            {
                var fromFile = JsonSerializer.Deserialize<LlmConfig>(
                    File.ReadAllText(ConfigPath), ReadOptions);

                if (fromFile is not null)
                {
                    if (!string.IsNullOrWhiteSpace(fromFile.BaseUrl)) config.BaseUrl = fromFile.BaseUrl;
                    if (!string.IsNullOrWhiteSpace(fromFile.Model)) config.Model = fromFile.Model;
                    if (!string.IsNullOrWhiteSpace(fromFile.SystemPrompt)) config.SystemPrompt = fromFile.SystemPrompt;
                    if (fromFile.MaxTokens > 0) config.MaxTokens = fromFile.MaxTokens;
                    if (!string.IsNullOrWhiteSpace(fromFile.VisionModel)) config.VisionModel = fromFile.VisionModel;
                    if (!string.IsNullOrWhiteSpace(fromFile.VisionPrompt)) config.VisionPrompt = fromFile.VisionPrompt;
                    config.DeselectAfterCopy = fromFile.DeselectAfterCopy;
                    config.ApiKey = fromFile.ApiKey ?? "";
                }
            }
        }
        catch (Exception ex)
        {
            Program.Log("Load config failed: " + ex.Message);
        }

        // Environment variable wins for the API key.
        var envKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
            config.ApiKey = envKey;

        return config;
    }
}