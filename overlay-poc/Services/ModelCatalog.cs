using System.Net.Http.Headers;
using System.Text.Json;
using OverlayPoc.Models;

namespace OverlayPoc.Services;

/// <summary>
/// Fetches and caches the OpenRouter model catalog.
/// Provides dynamic model capability verification.
/// </summary>
internal sealed class ModelCatalog
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private ModelCatalogData _cache = new();
    private readonly object _lock = new();
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public ModelCatalog(string apiKey, string baseUrl)
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>Get the full catalog, refreshing if stale.</summary>
    public async Task<ModelCatalogData> GetCatalogAsync(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (!forceRefresh && !_cache.IsStale && _cache.Models.Count > 0)
                return _cache;
        }

        await RefreshAsync();
        return _cache;
    }

    /// <summary>Get a specific model by ID.</summary>
    public async Task<ModelInfo> GetModelAsync(string modelId)
    {
        var catalog = await GetCatalogAsync();
        return catalog.Models.FirstOrDefault(m =>
            m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Check if a model supports a specific input modality.</summary>
    public async Task<bool> SupportsModalityAsync(string modelId, string modality)
    {
        var model = await GetModelAsync(modelId);
        return model?.InputModalities.Contains(modality, StringComparer.OrdinalIgnoreCase) ?? false;
    }

    /// <summary>Check if a model supports tools.</summary>
    public async Task<bool> SupportsToolsAsync(string modelId)
    {
        var model = await GetModelAsync(modelId);
        return model?.SupportsTools ?? false;
    }

    /// <summary>Get all models that support image input.</summary>
    public async Task<List<ModelInfo>> GetVisionModelsAsync()
    {
        var catalog = await GetCatalogAsync();
        return catalog.Models.Where(m => m.SupportsImageInput).ToList();
    }

    /// <summary>Force refresh the catalog from the API.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/models");
            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await SharedClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var models = new List<ModelInfo>();

            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var m in data.EnumerateArray())
                {
                    var info = ParseModel(m);
                    if (info is not null)
                        models.Add(info);
                }
            }

            lock (_lock)
            {
                _cache = new ModelCatalogData
                {
                    Models = models,
                    FetchedAt = DateTime.UtcNow
                };
            }

            Program.Log($"Model catalog refreshed: {models.Count} models");
        }
        catch (Exception ex)
        {
            Program.Log("Model catalog refresh failed: " + ex.Message);
        }
    }

    private static ModelInfo ParseModel(JsonElement m)
    {
        try
        {
            var info = new ModelInfo
            {
                Id = m.GetProperty("id").GetString() ?? "",
                Name = m.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Description = m.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                ContextLength = m.TryGetProperty("context_length", out var cl) ? cl.GetInt64() : 0,
            };

            // Architecture / modalities
            if (m.TryGetProperty("architecture", out var arch))
            {
                if (arch.TryGetProperty("input_modalities", out var inputMods))
                    info.InputModalities = inputMods.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                if (arch.TryGetProperty("output_modalities", out var outputMods))
                    info.OutputModalities = outputMods.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            }

            // Pricing
            if (m.TryGetProperty("pricing", out var pricing))
            {
                if (pricing.TryGetProperty("prompt", out var prompt) &&
                    decimal.TryParse(prompt.GetString(), out var promptPrice))
                    info.PromptPricePerToken = promptPrice;
                if (pricing.TryGetProperty("completion", out var completion) &&
                    decimal.TryParse(completion.GetString(), out var completionPrice))
                    info.CompletionPricePerToken = completionPrice;
            }

            // Supported parameters
            if (m.TryGetProperty("supported_parameters", out var sp))
            {
                info.SupportedParameters = sp.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                info.SupportsTools = info.SupportedParameters.Contains("tools") ||
                                     info.SupportedParameters.Contains("tool_choice");
                info.SupportsWebSearch = info.SupportedParameters.Contains("web_search_options");
            }

            return info;
        }
        catch
        {
            return null;
        }
    }
}
