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

        // 2. Try fallback models in order
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

        // 3. No model available
        return new RouteResult
        {
            Success = false,
            Model = primaryModel,
            FailureReason = "No suitable model found for this request."
        };
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
