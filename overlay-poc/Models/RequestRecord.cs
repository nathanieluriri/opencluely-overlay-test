namespace OverlayPoc.Models;

/// <summary>Records a single API request with its usage and cost.</summary>
internal sealed class RequestRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Model { get; set; } = "";
    public string RequestType { get; set; } = ""; // "text", "vision", "tool"
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CachedTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public decimal? ServerToolCostUsd { get; set; }
    public int WebSearchRequests { get; set; }
    public int WebFetchRequests { get; set; }
    public int ToolCallsExecuted { get; set; }
    public TimeSpan Latency { get; set; }
    public bool WasFallback { get; set; }
    public string FallbackReason { get; set; } = "";
    public bool HadImage { get; set; }
    public string ErrorMessage { get; set; } = "";
    public bool Success => string.IsNullOrEmpty(ErrorMessage);
}

/// <summary>Aggregated usage statistics.</summary>
internal sealed class UsageSummary
{
    public int TotalRequests { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCachedTokens { get; set; }
    public long TotalReasoningTokens { get; set; }
    public decimal TotalCostUsd { get; set; }
    public decimal TotalServerToolCostUsd { get; set; }
    public int TotalWebSearches { get; set; }
    public int TotalWebFetches { get; set; }
    public int TotalToolCalls { get; set; }
    public int TotalFallbacks { get; set; }
    public int TotalErrors { get; set; }
    public int TotalImageRequests { get; set; }
    public TimeSpan AverageLatency { get; set; }
    public Dictionary<string, ModelUsageSummary> PerModel { get; set; } = new();
}

internal sealed class ModelUsageSummary
{
    public string Model { get; set; } = "";
    public int Requests { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public int Errors { get; set; }
    public int Fallbacks { get; set; }
    public TimeSpan AverageLatency { get; set; }
}
