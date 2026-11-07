using OverlayPoc.Models;

namespace OverlayPoc.Services;

/// <summary>
/// Intelligent model selection. Routes requests to the appropriate model
/// based on modality requirements, tool support, cost, and availability.
/// </summary>
internal sealed class ModelRouter
{
    private readonly ModelCatalog _catalog;
    private readonly LlmConfig _config;

    public ModelRouter(ModelCatalog catalog, LlmConfig config)
    {
        _catalog = catalog;
        _config = config;
    }

    /// <summary>
    /// Select the best model for a request.
    /// Returns the model ID and whether it's a fallback.
    /// When tools are required, prefers models with native search support
    /// (OpenAI, Gemini) over models that need Exa fallback.
    /// </summary>
    public async Task<RouteResult> RouteAsync(RouteRequest request)
    {
        var primaryModel = request.HasImage
            ? _config.VisionModel ?? _config.Model
            : _config.Model;

        // 1. Try the primary model first
        var result = await TryModelAsync(primaryModel, request, isFallback: false);
        if (result.Success)
            return result;

        // 2. When tools are required, try provider-native models first
        //    (OpenAI/Gemini have built-in search, DeepSeek needs Exa)
        if (request.RequiresTools)
        {
            var nativeModels = GetNativeSearchModels(request.HasImage);
            foreach (var nativeId in nativeModels)
            {
                if (nativeId.Equals(primaryModel, StringComparison.OrdinalIgnoreCase))
                    continue;

                result = await TryModelAsync(nativeId, request, isFallback: true,
                    reason: $"Native search provider preferred over {primaryModel}");
                if (result.Success)
                {
                    Program.Log($"Fallback (native search): {primaryModel} → {nativeId}");
                    return result;
                }
            }
        }

        // 3. Try fallback models in order
        foreach (var fallbackId in _config.FallbackModels)
        {
            if (fallbackId.Equals(primaryModel, StringComparison.OrdinalIgnoreCase))
                continue;

            result = await TryModelAsync(fallbackId, request, isFallback: true,
                reason: result.FailureReason);
            if (result.Success)
            {
                Program.Log($"Fallback: {primaryModel} → {fallbackId} ({result.FallbackReason})");
                return result;
            }
        }

        // 4. No model available
        return new RouteResult
        {
            Success = false,
            Model = primaryModel,
            FailureReason = "No suitable model found for this request."
        };
    }

    /// <summary>Get models with native web search support, ordered by cost.</summary>
    private List<string> GetNativeSearchModels(bool needVision)
    {
        // OpenAI models with native search (GPT-4.1+, GPT-5+, o3, o4-mini)
        // Gemini models with native grounding (2.5+, 3+, 3.1+, 3.5+)
        var candidates = new List<(string Id, decimal Cost, bool Vision)>
        {
            // OpenAI — cheapest first
            ("openai/gpt-4o-mini", 0.00000015m, true),
            ("openai/gpt-4.1-nano", 0.00000010m, true),
            ("openai/gpt-4.1-mini", 0.00000040m, true),
            ("openai/gpt-4o", 0.0000025m, true),
            ("openai/gpt-4.1", 0.000002m, true),
            ("openai/gpt-5-mini", 0.00000025m, true),
            ("openai/gpt-5-nano", 0.00000005m, true),
            ("openai/gpt-5", 0.00000125m, true),
            ("openai/o3", 0.000002m, true),

            // Gemini — cheapest first
            ("google/gemini-2.5-flash-lite", 0.00000010m, true),
            ("google/gemini-2.5-flash", 0.00000030m, true),
            ("google/gemini-3.1-flash-lite", 0.00000025m, true),
            ("google/gemini-3.5-flash-lite", 0.00000030m, true),
            ("google/gemini-2.5-pro", 0.00000125m, true),
            ("google/gemini-3.5-flash", 0.0000015m, true),
            ("google/gemini-3.1-pro-preview", 0.000002m, true),
        };

        return candidates
            .Where(c => !needVision || c.Vision)
            .OrderBy(c => c.Cost)
            .Select(c => c.Id)
            .ToList();
    }

    private async Task<RouteResult> TryModelAsync(
        string modelId, RouteRequest request, bool isFallback, string reason = "")
    {
        var model = await _catalog.GetModelAsync(modelId);

        // Model not in catalog — allow it anyway (may be new or BYOK)
        if (model is null)
        {
            return new RouteResult
            {
                Success = true,
                Model = modelId,
                IsFallback = isFallback,
                FallbackReason = reason,
                ModelInfo = null
            };
        }

        // Check image modality
        if (request.HasImage && !model.SupportsImageInput)
        {
            return new RouteResult
            {
                Success = false,
                Model = modelId,
                FailureReason = $"{modelId} does not support image input"
            };
        }

        // Check tool support
        if (request.RequiresTools && !model.SupportsTools)
        {
            return new RouteResult
            {
                Success = false,
                Model = modelId,
                FailureReason = $"{modelId} does not support tool calling"
            };
        }

        // Check context window
        if (request.EstimatedInputTokens > 0 && model.ContextLength > 0)
        {
            var totalNeeded = request.EstimatedInputTokens + request.MaxOutputTokens;
            if (totalNeeded > model.ContextLength)
            {
                return new RouteResult
                {
                    Success = false,
                    Model = modelId,
                    FailureReason = $"{modelId} context too small ({model.ContextLength} < {totalNeeded})"
                };
            }
        }

        return new RouteResult
        {
            Success = true,
            Model = modelId,
            IsFallback = isFallback,
            FallbackReason = reason,
            ModelInfo = model
        };
    }
}

internal sealed class RouteRequest
{
    public bool HasImage { get; set; }
    public bool RequiresTools { get; set; }
    public long EstimatedInputTokens { get; set; }
    public long MaxOutputTokens { get; set; } = 2048;
}

internal sealed class RouteResult
{
    public bool Success { get; set; }
    public string Model { get; set; } = "";
    public bool IsFallback { get; set; }
    public string FallbackReason { get; set; } = "";
    public string FailureReason { get; set; } = "";
    public ModelInfo ModelInfo { get; set; }
}
