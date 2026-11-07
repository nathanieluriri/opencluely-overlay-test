using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OverlayPoc.Chat;

namespace OverlayPoc;

/// <summary>
/// Minimal OpenRouter chat-completions client with SSE streaming.
/// </summary>
internal static class OpenRouterClient
{
    private sealed class Message
    {
        public string role { get; set; } = "";
        public object content { get; set; } = "";
    }

    private sealed class ContentPart
    {
        public string type { get; set; } = "";
        public string text { get; set; }
        public ImageUrl image_url { get; set; }

        public static ContentPart Text(string text) => new() { type = "text", text = text };
        public static ContentPart Image(string base64Png) => new()
        {
            type = "image_url",
            image_url = new ImageUrl { url = "data:image/png;base64," + base64Png }
        };
    }

    private sealed class ImageUrl
    {
        public string url { get; set; } = "";
    }

    private sealed class Request
    {
        public string model { get; set; } = "";
        public List<Message> messages { get; set; } = new();
        public bool stream { get; set; } = true;
        public int max_tokens { get; set; } = 2048;
    }

    public static async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> conversation,
        LlmConfig config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{config.BaseUrl.TrimEnd('/')}/chat/completions");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://overlaypoc.local");
        request.Headers.TryAddWithoutValidation("X-Title", "OverlayPoc");
        request.Content = new StringContent(BuildRequestBody(conversation, config), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new ApplicationException($"OpenRouter {(int)response.StatusCode}: {Truncate(body, 300)}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (!line.StartsWith("data:")) continue;

            var data = line.Substring(5).Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") yield break;

            var delta = ExtractDelta(data);
            if (delta is not null)
                yield return delta;
        }
    }

    private static string BuildRequestBody(IReadOnlyList<ChatMessage> conversation, LlmConfig config)
    {
        var messages = new List<Message>();

        if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
            messages.Add(new Message { role = "system", content = config.SystemPrompt });

        foreach (var m in conversation)
            messages.Add(new Message { role = m.IsAssistant ? "assistant" : "user", content = m.Text });

        var request = new Request
        {
            model = config.Model,
            messages = messages,
            max_tokens = config.MaxTokens > 0 ? config.MaxTokens : 2048
        };

        return JsonSerializer.Serialize(request);
    }

    private static string BuildVisionRequestBody(
        IReadOnlyList<ChatMessage> conversation,
        string imageBase64,
        LlmConfig config)
    {
        var messages = new List<Message>();

        if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
            messages.Add(new Message { role = "system", content = config.SystemPrompt });

        // History as plain text
        foreach (var m in conversation.Take(conversation.Count - 1))
            messages.Add(new Message { role = m.IsAssistant ? "assistant" : "user", content = m.Text });

        // Last user message gets the image attached
        var last = conversation[^1];
        messages.Add(new Message
        {
            role = "user",
            content = new List<ContentPart>
            {
                ContentPart.Text(last.Text),
                ContentPart.Image(imageBase64)
            }
        });

        var request = new Request
        {
            model = config.VisionModel ?? config.Model,
            messages = messages,
            max_tokens = config.MaxTokens > 0 ? config.MaxTokens : 2048
        };

        return JsonSerializer.Serialize(request);
    }

    public static async IAsyncEnumerable<string> StreamWithImageAsync(
        IReadOnlyList<ChatMessage> conversation,
        string imageBase64,
        LlmConfig config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{config.BaseUrl.TrimEnd('/')}/chat/completions");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://overlaypoc.local");
        request.Headers.TryAddWithoutValidation("X-Title", "OverlayPoc");
        request.Content = new StringContent(BuildVisionRequestBody(conversation, imageBase64, config), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new ApplicationException($"OpenRouter {(int)response.StatusCode}: {Truncate(body, 300)}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (!line.StartsWith("data:")) continue;

            var data = line.Substring(5).Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") yield break;

            var delta = ExtractDelta(data);
            if (delta is not null)
                yield return delta;
        }
    }

    private static string ExtractDelta(string data)
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

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..max] + "…";
}