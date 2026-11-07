using System.Text.Json;
using System.Text.Json.Serialization;

namespace OverlayPoc.Models;

/// <summary>
/// OpenRouter connection settings. Loaded from %LOCALAPPDATA%\OverlayPoc\config.json
/// with OPENROUTER_API_KEY taking priority for the key.
/// </summary>
internal sealed class LlmConfig
{
    // ── Connection ─────────────────────────────────────────────────────
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    // ── Model selection ────────────────────────────────────────────────
    /// <summary>Primary default model. DeepSeek V4 Pro is the default intelligence layer.</summary>
    public string Model { get; set; } = "deepseek/deepseek-v4-pro";

    /// <summary>Model for vision/screenshot requests. Falls back to Model if not set.</summary>
    public string VisionModel { get; set; } = "deepseek/deepseek-v4.1-flash";

    /// <summary>Configurable fallback list, tried in order when the primary model fails.</summary>
    public List<string> FallbackModels { get; set; } = new()
    {
        "deepseek/deepseek-v4.1-flash",
        "openai/gpt-4o-mini",
        "google/gemini-2.5-flash",
        "deepseek/deepseek-chat",
        "openai/gpt-4o",
        "google/gemini-2.5-flash-lite"
    };

    // ── Prompts ────────────────────────────────────────────────────────
    public string SystemPrompt { get; set; } =
        "You are a precise, objective assistant. Answer accurately and concisely. " +
        "If you are not sure, say so instead of guessing. " +
        "When you use information from web search or web fetch, cite the source URL.";

    /// <summary>Prompt used when sending a screenshot to the vision model.</summary>
    public string VisionPrompt { get; set; } =
        "Analyze this screenshot. If you see a question with multiple choice options: " +
        "1. First line: state the answer as a numbered option (e.g., \"2. Option text\"). " +
        "2. Keep the first line as brief as possible. " +
        "3. If no question is visible, describe what you see concisely.";

    // ── Token limits ───────────────────────────────────────────────────
    public int MaxTokens { get; set; } = 2048;

    // ── Behaviour toggles ──────────────────────────────────────────────
    /// <summary>After copying the current selection, send Right Arrow to collapse
    /// the highlight so it does not linger on screen.</summary>
    public bool DeselectAfterCopy { get; set; } = true;

    // ── Web search / fetch tools ───────────────────────────────────────
    public bool EnableWebSearch { get; set; } = false;
    public string WebSearchEngine { get; set; } = "auto"; // auto, native, exa, firecrawl, parallel, perplexity
    public int WebSearchMaxResults { get; set; } = 5;
    public int WebSearchMaxTotalResults { get; set; } = 20;
    public string WebSearchContextSize { get; set; } = "medium"; // low, medium, high
    public List<string> WebSearchAllowedDomains { get; set; } = new();
    public List<string> WebSearchExcludedDomains { get; set; } = new();

    public bool EnableWebFetch { get; set; } = false;
    public string WebFetchEngine { get; set; } = "auto"; // auto, native, exa, openrouter, firecrawl, parallel
    public int WebFetchMaxContentTokens { get; set; } = 50000;
    public int WebFetchMaxUses { get; set; } = 10;
    public List<string> WebFetchAllowedDomains { get; set; } = new();
    public List<string> WebFetchBlockedDomains { get; set; } = new();

    // ── Cost controls ──────────────────────────────────────────────────
    public decimal SpendingLimitUsd { get; set; } = 0; // 0 = unlimited
    public int MaxToolCallsPerRequest { get; set; } = 30;
    public bool EnableUsageTracking { get; set; } = true;

    // ── Provider-specific search engine overrides ──────────────────────
    /// <summary>Search engine for OpenAI models. "native" uses OpenAI's built-in search.</summary>
    public string OpenAISearchEngine { get; set; } = "native";

    /// <summary>Search engine for Gemini models. "native" uses Google's built-in grounding.</summary>
    public string GeminiSearchEngine { get; set; } = "native";

    /// <summary>Search engine for all other models (DeepSeek, etc.).</summary>
    public string DefaultSearchEngine { get; set; } = "exa";

    // ── Helpers ────────────────────────────────────────────────────────
    [JsonIgnore]
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Resolve the search engine to use for a given model.</summary>
    public string ResolveSearchEngine(string modelId)
    {
        if (!string.IsNullOrWhiteSpace(WebSearchEngine) && WebSearchEngine != "auto")
            return WebSearchEngine;

        if (modelId.StartsWith("openai/", StringComparison.OrdinalIgnoreCase))
            return OpenAISearchEngine;
        if (modelId.StartsWith("google/", StringComparison.OrdinalIgnoreCase))
            return GeminiSearchEngine;
        return DefaultSearchEngine;
    }
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
                    if (!string.IsNullOrWhiteSpace(fromFile.VisionModel)) config.VisionModel = fromFile.VisionModel;
                    if (fromFile.FallbackModels?.Count > 0) config.FallbackModels = fromFile.FallbackModels;
                    if (!string.IsNullOrWhiteSpace(fromFile.SystemPrompt)) config.SystemPrompt = fromFile.SystemPrompt;
                    if (!string.IsNullOrWhiteSpace(fromFile.VisionPrompt)) config.VisionPrompt = fromFile.VisionPrompt;
                    if (fromFile.MaxTokens > 0) config.MaxTokens = fromFile.MaxTokens;
                    config.DeselectAfterCopy = fromFile.DeselectAfterCopy;
                    config.ApiKey = fromFile.ApiKey ?? "";

                    config.EnableWebSearch = fromFile.EnableWebSearch;
                    if (!string.IsNullOrWhiteSpace(fromFile.WebSearchEngine)) config.WebSearchEngine = fromFile.WebSearchEngine;
                    if (fromFile.WebSearchMaxResults > 0) config.WebSearchMaxResults = fromFile.WebSearchMaxResults;
                    if (fromFile.WebSearchMaxTotalResults > 0) config.WebSearchMaxTotalResults = fromFile.WebSearchMaxTotalResults;
                    if (!string.IsNullOrWhiteSpace(fromFile.WebSearchContextSize)) config.WebSearchContextSize = fromFile.WebSearchContextSize;
                    if (fromFile.WebSearchAllowedDomains?.Count > 0) config.WebSearchAllowedDomains = fromFile.WebSearchAllowedDomains;
                    if (fromFile.WebSearchExcludedDomains?.Count > 0) config.WebSearchExcludedDomains = fromFile.WebSearchExcludedDomains;

                    config.EnableWebFetch = fromFile.EnableWebFetch;
                    if (!string.IsNullOrWhiteSpace(fromFile.WebFetchEngine)) config.WebFetchEngine = fromFile.WebFetchEngine;
                    if (fromFile.WebFetchMaxContentTokens > 0) config.WebFetchMaxContentTokens = fromFile.WebFetchMaxContentTokens;
                    if (fromFile.WebFetchMaxUses > 0) config.WebFetchMaxUses = fromFile.WebFetchMaxUses;
                    if (fromFile.WebFetchAllowedDomains?.Count > 0) config.WebFetchAllowedDomains = fromFile.WebFetchAllowedDomains;
                    if (fromFile.WebFetchBlockedDomains?.Count > 0) config.WebFetchBlockedDomains = fromFile.WebFetchBlockedDomains;

                    config.SpendingLimitUsd = fromFile.SpendingLimitUsd;
                    if (fromFile.MaxToolCallsPerRequest > 0) config.MaxToolCallsPerRequest = fromFile.MaxToolCallsPerRequest;
                    config.EnableUsageTracking = fromFile.EnableUsageTracking;
                    if (!string.IsNullOrWhiteSpace(fromFile.OpenAISearchEngine)) config.OpenAISearchEngine = fromFile.OpenAISearchEngine;
                    if (!string.IsNullOrWhiteSpace(fromFile.GeminiSearchEngine)) config.GeminiSearchEngine = fromFile.GeminiSearchEngine;
                    if (!string.IsNullOrWhiteSpace(fromFile.DefaultSearchEngine)) config.DefaultSearchEngine = fromFile.DefaultSearchEngine;
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
