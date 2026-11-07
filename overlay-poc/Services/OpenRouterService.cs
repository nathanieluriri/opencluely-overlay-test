using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OverlayPoc.Chat;
using OverlayPoc.Models;

namespace OverlayPoc.Services;

/// <summary>
/// Centralised AI gateway. All model requests pass through this service.
/// Supports Chat Completions API (text + vision) and Responses API (server tools).
/// Tracks usage, cost, and latency for every request.
/// </summary>
internal sealed class OpenRouterService
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly LlmConfig _config;
    private readonly UsageTracker _tracker;
    private readonly ModelRouter _router;
    private readonly ToolRegistry _toolRegistry;

    public OpenRouterService(LlmConfig config, UsageTracker tracker, ModelCatalog catalog, ToolRegistry toolRegistry = null)
    {
        _config = config;
        _tracker = tracker;
        _router = new ModelRouter(catalog, config);
        _toolRegistry = toolRegistry;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Chat Completions API — text and vision (no server tools)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Stream a text-only chat completion.</summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        IReadOnlyList<ChatMessage> conversation,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var record = new RequestRecord { RequestType = "text" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var route = await _router.RouteAsync(new RouteRequest
        {
            RequiresTools = false,
            MaxOutputTokens = _config.MaxTokens
        });

        record.Model = route.Model;
        record.WasFallback = route.IsFallback;
        record.FallbackReason = route.FallbackReason;

        if (!route.Success)
        {
            record.ErrorMessage = route.FailureReason;
            record.Latency = sw.Elapsed;
            _tracker.Record(record);
            throw new ApplicationException(route.FailureReason);
        }

        // yield in try/finally (no catch) is allowed by C#
        try
        {
            await foreach (var delta in StreamChatCompletionsAsync(
                conversation, route.Model, null, _config, ct))
            {
                yield return delta;
            }
        }
        finally
        {
            sw.Stop();
            record.Latency = sw.Elapsed;
            _tracker.Record(record);
        }
    }

    /// <summary>Stream a vision chat completion with an image.</summary>
    public async IAsyncEnumerable<string> StreamVisionAsync(
        IReadOnlyList<ChatMessage> conversation,
        string imageBase64,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var record = new RequestRecord { RequestType = "vision", HadImage = true };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var route = await _router.RouteAsync(new RouteRequest
        {
            HasImage = true,
            RequiresTools = false,
            MaxOutputTokens = _config.MaxTokens
        });

        record.Model = route.Model;
        record.WasFallback = route.IsFallback;
        record.FallbackReason = route.FallbackReason;

        if (!route.Success)
        {
            record.ErrorMessage = route.FailureReason;
            record.Latency = sw.Elapsed;
            _tracker.Record(record);
            throw new ApplicationException(route.FailureReason);
        }

        try
        {
            await foreach (var delta in StreamChatCompletionsAsync(
                conversation, route.Model, imageBase64, _config, ct))
            {
                yield return delta;
            }
        }
        finally
        {
            sw.Stop();
            record.Latency = sw.Elapsed;
            _tracker.Record(record);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Responses API — server tools (web search, web fetch)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Stream a response with server tools enabled.</summary>
    public async IAsyncEnumerable<ResponseChunk> StreamWithToolsAsync(
        IReadOnlyList<ChatMessage> conversation,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var record = new RequestRecord { RequestType = "tool" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var route = await _router.RouteAsync(new RouteRequest
        {
            RequiresTools = true,
            MaxOutputTokens = _config.MaxTokens
        });

        record.Model = route.Model;
        record.WasFallback = route.IsFallback;
        record.FallbackReason = route.FallbackReason;

        if (!route.Success)
        {
            record.ErrorMessage = route.FailureReason;
            record.Latency = sw.Elapsed;
            _tracker.Record(record);
            throw new ApplicationException(route.FailureReason);
        }

        try
        {
            await foreach (var chunk in StreamResponsesApiAsync(conversation, route.Model, _config, ct, record))
            {
                yield return chunk;
            }
        }
        finally
        {
            sw.Stop();
            record.Latency = sw.Elapsed;
            _tracker.Record(record);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Private: Chat Completions streaming
    // ══════════════════════════════════════════════════════════════════

    private static async IAsyncEnumerable<string> StreamChatCompletionsAsync(
        IReadOnlyList<ChatMessage> conversation,
        string model,
        string imageBase64,
        LlmConfig config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{config.BaseUrl.TrimEnd('/')}/chat/completions");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://overlaypoc.local");
        request.Headers.TryAddWithoutValidation("X-Title", "OverlayPoc");

        var body = imageBase64 is not null
            ? BuildVisionRequestBody(conversation, imageBase64, model, config)
            : BuildTextRequestBody(conversation, model, config);

        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new ApplicationException($"OpenRouter {(int)response.StatusCode}: {Truncate(errorBody, 300)}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (ct.IsCancellationRequested) yield break;
            if (!line.StartsWith("data:")) continue;

            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") yield break;

            var delta = ExtractChatDelta(data);
            if (delta is not null)
                yield return delta;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Private: Responses API streaming
    // ══════════════════════════════════════════════════════════════════

    private async IAsyncEnumerable<ResponseChunk> StreamResponsesApiAsync(
        IReadOnlyList<ChatMessage> conversation,
        string model,
        LlmConfig config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
        RequestRecord record)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{config.BaseUrl.TrimEnd('/')}/responses");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://overlaypoc.local");
        request.Headers.TryAddWithoutValidation("X-Title", "OverlayPoc");

        var body = BuildResponsesRequestBody(conversation, model, config);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new ApplicationException($"OpenRouter {(int)response.StatusCode}: {Truncate(errorBody, 300)}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (ct.IsCancellationRequested) yield break;
            if (!line.StartsWith("data:")) continue;

            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") yield break;

            var chunk = ExtractResponsesChunk(data, record);
            if (chunk is not null)
                yield return chunk;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Request body builders
    // ══════════════════════════════════════════════════════════════════

    private static string BuildTextRequestBody(
        IReadOnlyList<ChatMessage> conversation, string model, LlmConfig config)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
            messages.Add(new { role = "system", content = config.SystemPrompt });

        foreach (var m in conversation)
            messages.Add(new { role = m.IsAssistant ? "assistant" : "user", content = m.Text });

        return JsonSerializer.Serialize(new
        {
            model,
            messages,
            stream = true,
            max_tokens = config.MaxTokens > 0 ? config.MaxTokens : 2048
        });
    }

    private static string BuildVisionRequestBody(
        IReadOnlyList<ChatMessage> conversation, string imageBase64,
        string model, LlmConfig config)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
            messages.Add(new { role = "system", content = config.SystemPrompt });

        // History as plain text
        foreach (var m in conversation.Take(conversation.Count - 1))
            messages.Add(new { role = m.IsAssistant ? "assistant" : "user", content = m.Text });

        // Last user message gets the image
        var last = conversation[^1];
        messages.Add(new
        {
            role = "user",
            content = new object[]
            {
                new { type = "text", text = last.Text },
                new { type = "image_url", image_url = new { url = "data:image/png;base64," + imageBase64 } }
            }
        });

        return JsonSerializer.Serialize(new
        {
            model,
            messages,
            stream = true,
            max_tokens = config.MaxTokens > 0 ? config.MaxTokens : 2048
        });
    }

    private string BuildResponsesRequestBody(
        IReadOnlyList<ChatMessage> conversation, string model, LlmConfig config)
    {
        // Build input items
        var input = new List<object>();

        // System prompt as developer message
        if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
        {
            input.Add(new
            {
                type = "message",
                role = "developer",
                content = new object[] { new { type = "input_text", text = config.SystemPrompt } }
            });
        }

        foreach (var m in conversation)
        {
            input.Add(new
            {
                type = "message",
                role = m.IsAssistant ? "assistant" : "user",
                content = new object[] { new { type = "input_text", text = m.Text } }
            });
        }

        // Build tools array
        var tools = new List<object>();

        if (config.EnableWebSearch)
        {
            var searchParams = new Dictionary<string, object>();

            // Use provider-aware search engine resolution
            var engine = config.ResolveSearchEngine(model);
            if (!string.IsNullOrWhiteSpace(engine) && engine != "auto")
                searchParams["engine"] = engine;

            if (config.WebSearchMaxResults > 0)
                searchParams["max_results"] = config.WebSearchMaxResults;
            if (config.WebSearchMaxTotalResults > 0)
                searchParams["max_total_results"] = config.WebSearchMaxTotalResults;
            if (!string.IsNullOrWhiteSpace(config.WebSearchContextSize))
                searchParams["search_context_size"] = config.WebSearchContextSize;
            if (config.WebSearchAllowedDomains?.Count > 0)
                searchParams["allowed_domains"] = config.WebSearchAllowedDomains;
            if (config.WebSearchExcludedDomains?.Count > 0)
                searchParams["excluded_domains"] = config.WebSearchExcludedDomains;

            tools.Add(new
            {
                type = "openrouter:web_search",
                parameters = searchParams.Count > 0 ? searchParams : null
            });
        }

        if (config.EnableWebFetch)
        {
            var fetchParams = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(config.WebFetchEngine) && config.WebFetchEngine != "auto")
                fetchParams["engine"] = config.WebFetchEngine;
            if (config.WebFetchMaxContentTokens > 0)
                fetchParams["max_content_tokens"] = config.WebFetchMaxContentTokens;
            if (config.WebFetchMaxUses > 0)
                fetchParams["max_uses"] = config.WebFetchMaxUses;
            if (config.WebFetchAllowedDomains?.Count > 0)
                fetchParams["allowed_domains"] = config.WebFetchAllowedDomains;
            if (config.WebFetchBlockedDomains?.Count > 0)
                fetchParams["blocked_domains"] = config.WebFetchBlockedDomains;

            tools.Add(new
            {
                type = "openrouter:web_fetch",
                parameters = fetchParams.Count > 0 ? fetchParams : null
            });
        }

        // Add user-defined custom tools from the registry
        if (_toolRegistry is not null)
        {
            var customTools = _toolRegistry.BuildToolSchemas();
            tools.AddRange(customTools);
        }

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["input"] = input,
            ["stream"] = true
        };

        if (tools.Count > 0)
        {
            requestBody["tools"] = tools;
            if (config.MaxToolCallsPerRequest > 0)
                requestBody["max_tool_calls"] = config.MaxToolCallsPerRequest;
        }

        if (config.MaxTokens > 0)
            requestBody["max_output_tokens"] = config.MaxTokens;

        return JsonSerializer.Serialize(requestBody);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Response parsers
    // ══════════════════════════════════════════════════════════════════

    private static string ExtractChatDelta(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
                throw new ApplicationException("OpenRouter error: " + error);

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ResponseChunk ExtractResponsesChunk(string data, RequestRecord record)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return null;

            var type = typeProp.GetString();

            switch (type)
            {
                case "response.output_text.delta":
                {
                    if (root.TryGetProperty("delta", out var delta) &&
                        delta.ValueKind == JsonValueKind.String)
                    {
                        return new ResponseChunk { Text = delta.GetString() };
                    }
                    return null;
                }

                case "response.completed":
                {
                    // Extract usage from final response
                    if (root.TryGetProperty("response", out var resp) &&
                        resp.TryGetProperty("usage", out var usage))
                    {
                        ExtractUsage(usage, record);
                    }
                    return new ResponseChunk { IsComplete = true };
                }

                case "response.output_item.done":
                {
                    // Check for web_search_call or web_fetch_call items
                    if (root.TryGetProperty("item", out var item) &&
                        item.TryGetProperty("type", out var itemType))
                    {
                        var itemTypeStr = itemType.GetString();
                        if (itemTypeStr == "openrouter:web_search")
                        {
                            record.WebSearchRequests++;
                            return new ResponseChunk { ToolCall = "web_search" };
                        }
                        if (itemTypeStr == "openrouter:web_fetch")
                        {
                            record.WebFetchRequests++;
                            return new ResponseChunk { ToolCall = "web_fetch" };
                        }
                    }
                    return null;
                }

                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ExtractUsage(JsonElement usage, RequestRecord record)
    {
        if (usage.TryGetProperty("input_tokens", out var inputTokens))
            record.InputTokens = inputTokens.GetInt64();
        if (usage.TryGetProperty("output_tokens", out var outputTokens))
            record.OutputTokens = outputTokens.GetInt64();
        if (usage.TryGetProperty("input_tokens_details", out var inputDetails) &&
            inputDetails.TryGetProperty("cached_tokens", out var cached))
            record.CachedTokens = cached.GetInt64();
        if (usage.TryGetProperty("output_tokens_details", out var outputDetails) &&
            outputDetails.TryGetProperty("reasoning_tokens", out var reasoning))
            record.ReasoningTokens = reasoning.GetInt64();
        if (usage.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number)
            record.CostUsd = cost.GetDecimal();
        if (usage.TryGetProperty("cost_details", out var costDetails) &&
            costDetails.TryGetProperty("server_tool_cost", out var serverToolCost) &&
            serverToolCost.ValueKind == JsonValueKind.Number)
            record.ServerToolCostUsd = serverToolCost.GetDecimal();
        if (usage.TryGetProperty("server_tool_use_details", out var toolDetails))
        {
            if (toolDetails.TryGetProperty("web_search_requests", out var webSearch))
                record.WebSearchRequests = webSearch.GetInt32();
            if (toolDetails.TryGetProperty("tool_calls_executed", out var toolCalls))
                record.ToolCallsExecuted = toolCalls.GetInt32();
        }
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..max] + "…";
}

/// <summary>A chunk of a streaming response.</summary>
internal sealed class ResponseChunk
{
    public string Text { get; set; }
    public bool IsComplete { get; set; }
    public string ToolCall { get; set; } // "web_search", "web_fetch", etc.
}
