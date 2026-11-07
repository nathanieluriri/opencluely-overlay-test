using System.Text.Json;
using OverlayPoc.Models;

namespace OverlayPoc.Services;

/// <summary>
/// Centralised usage and cost tracking. Records every API request,
/// aggregates statistics, and persists to disk for the dashboard.
/// </summary>
internal sealed class UsageTracker
{
    private static readonly string DataPath = Path.Combine(
        LlmConfigLoader.DirectoryPath, "usage.json");

    private readonly List<RequestRecord> _records = new();
    private readonly object _lock = new();
    private bool _loaded;

    public event Action<RequestRecord> OnRecordAdded;

    public bool Enabled { get; set; } = true;

    public void Record(RequestRecord record)
    {
        if (!Enabled) return;
        if (!_loaded) Load();

        lock (_lock)
        {
            _records.Add(record);
            // Keep only last 10,000 records to prevent unbounded growth
            if (_records.Count > 10_000)
                _records.RemoveRange(0, _records.Count - 10_000);
        }

        OnRecordAdded?.Invoke(record);
        Save();
    }

    public UsageSummary GetSummary(DateTime? since = null)
    {
        if (!_loaded) Load();

        var cutoff = since ?? DateTime.MinValue;
        var filtered = _records.Where(r => r.Timestamp >= cutoff).ToList();

        var summary = new UsageSummary
        {
            TotalRequests = filtered.Count,
            TotalInputTokens = filtered.Sum(r => r.InputTokens),
            TotalOutputTokens = filtered.Sum(r => r.OutputTokens),
            TotalCachedTokens = filtered.Sum(r => r.CachedTokens),
            TotalReasoningTokens = filtered.Sum(r => r.ReasoningTokens),
            TotalCostUsd = filtered.Sum(r => r.CostUsd ?? 0),
            TotalServerToolCostUsd = filtered.Sum(r => r.ServerToolCostUsd ?? 0),
            TotalWebSearches = filtered.Sum(r => r.WebSearchRequests),
            TotalWebFetches = filtered.Sum(r => r.WebFetchRequests),
            TotalToolCalls = filtered.Sum(r => r.ToolCallsExecuted),
            TotalFallbacks = filtered.Count(r => r.WasFallback),
            TotalErrors = filtered.Count(r => !r.Success),
            TotalImageRequests = filtered.Count(r => r.HadImage),
        };

        if (filtered.Count > 0)
        {
            var avgMs = filtered.Average(r => r.Latency.TotalMilliseconds);
            summary.AverageLatency = TimeSpan.FromMilliseconds(avgMs);
        }

        // Per-model breakdown
        foreach (var group in filtered.GroupBy(r => r.Model))
        {
            var modelSummary = new ModelUsageSummary
            {
                Model = group.Key,
                Requests = group.Count(),
                InputTokens = group.Sum(r => r.InputTokens),
                OutputTokens = group.Sum(r => r.OutputTokens),
                CostUsd = group.Sum(r => r.CostUsd ?? 0),
                Errors = group.Count(r => !r.Success),
                Fallbacks = group.Count(r => r.WasFallback),
            };
            if (group.Any())
                modelSummary.AverageLatency = TimeSpan.FromMilliseconds(
                    group.Average(r => r.Latency.TotalMilliseconds));
            summary.PerModel[group.Key] = modelSummary;
        }

        return summary;
    }

    public List<RequestRecord> GetRecent(int count = 100, string modelFilter = null)
    {
        if (!_loaded) Load();

        lock (_lock)
        {
            var query = _records.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(modelFilter))
                query = query.Where(r => r.Model.Contains(modelFilter, StringComparison.OrdinalIgnoreCase));
            return query.OrderByDescending(r => r.Timestamp).Take(count).ToList();
        }
    }

    public decimal GetTotalSpend(DateTime? since = null)
    {
        if (!_loaded) Load();
        var cutoff = since ?? DateTime.MinValue;
        return _records.Where(r => r.Timestamp >= cutoff).Sum(r => r.CostUsd ?? 0);
    }

    public bool IsOverLimit(decimal limitUsd)
    {
        if (limitUsd <= 0) return false;
        return GetTotalSpend() >= limitUsd;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _records.Clear();
        }
        Save();
    }

    private void Load()
    {
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (File.Exists(DataPath))
                {
                    var json = File.ReadAllText(DataPath);
                    var loaded = JsonSerializer.Deserialize<List<RequestRecord>>(json);
                    if (loaded is not null)
                        _records.AddRange(loaded);
                }
            }
            catch (Exception ex)
            {
                Program.Log("Usage load failed: " + ex.Message);
            }
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(LlmConfigLoader.DirectoryPath);
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(DataPath, json);
            }
        }
        catch (Exception ex)
        {
            Program.Log("Usage save failed: " + ex.Message);
        }
    }
}
