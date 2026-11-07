namespace OverlayPoc.Models;

/// <summary>Live model metadata fetched from OpenRouter /models.</summary>
internal sealed class ModelInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long ContextLength { get; set; }
    public List<string> InputModalities { get; set; } = new();
    public List<string> OutputModalities { get; set; } = new();
    public decimal PromptPricePerToken { get; set; }
    public decimal CompletionPricePerToken { get; set; }
    public bool SupportsTools { get; set; }
    public bool SupportsWebSearch { get; set; }
    public bool SupportsWebFetch { get; set; }
    public List<string> SupportedParameters { get; set; } = new();
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    public bool SupportsImageInput => InputModalities.Contains("image");
    public bool SupportsFileInput => InputModalities.Contains("file");

    /// <summary>Estimated cost for a given number of input and output tokens.</summary>
    public decimal EstimateCost(long inputTokens, long outputTokens) =>
        (inputTokens * PromptPricePerToken) + (outputTokens * CompletionPricePerToken);
}

/// <summary>Catalog of models fetched from OpenRouter.</summary>
internal sealed class ModelCatalogData
{
    public List<ModelInfo> Models { get; set; } = new();
    public DateTime FetchedAt { get; set; } = DateTime.MinValue;
    public bool IsStale => (DateTime.UtcNow - FetchedAt).TotalHours > 1;
}
