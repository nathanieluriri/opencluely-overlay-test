using System.Text.Json;
using System.Text.Json.Serialization;

namespace OverlayPoc.Services;

/// <summary>
/// Extensible registry for user-defined (client-side) tools.
/// Tools have schemas, permissions, and execution limits.
/// </summary>
internal sealed class ToolRegistry
{
    private readonly List<RegisteredTool> _tools = new();
    private readonly object _lock = new();

    public IReadOnlyList<RegisteredTool> Tools
    {
        get { lock (_lock) { return _tools.ToList(); } }
    }

    public void Register(RegisteredTool tool)
    {
        lock (_lock)
        {
            _tools.RemoveAll(t => t.Name.Equals(tool.Name, StringComparison.OrdinalIgnoreCase));
            _tools.Add(tool);
        }
    }

    public bool Unregister(string name)
    {
        lock (_lock)
        {
            return _tools.RemoveAll(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }

    public RegisteredTool Find(string name)
    {
        lock (_lock)
        {
            return _tools.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Build the tools array for the API request.</summary>
    public List<object> BuildToolSchemas()
    {
        lock (_lock)
        {
            return _tools.Where(t => t.Enabled).Select(t => t.ToSchema()).ToList();
        }
    }

    /// <summary>Execute a tool call from the model.</summary>
    public async Task<ToolResult> ExecuteAsync(string name, string argumentsJson, CancellationToken ct)
    {
        var tool = Find(name);
        if (tool is null)
            return new ToolResult { Success = false, Error = $"Unknown tool: {name}" };

        if (!tool.Enabled)
            return new ToolResult { Success = false, Error = $"Tool disabled: {name}" };

        if (tool.ExecutionCount >= tool.MaxExecutions)
            return new ToolResult { Success = false, Error = $"Tool execution limit reached: {name}" };

        tool.ExecutionCount++;

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var result = await tool.ExecuteAsync(doc.RootElement, ct);
            return new ToolResult { Success = true, Content = result };
        }
        catch (Exception ex)
        {
            return new ToolResult { Success = false, Error = ex.Message };
        }
    }
}

/// <summary>A registered user-defined tool.</summary>
internal sealed class RegisteredTool
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ParametersJsonSchema { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
    public int MaxExecutions { get; set; } = 10;
    public int ExecutionCount { get; set; }
    public Func<JsonElement, CancellationToken, Task<string>> ExecuteAsync { get; set; }

    public object ToSchema()
    {
        using var doc = JsonDocument.Parse(ParametersJsonSchema);
        return new
        {
            type = "function",
            name = Name,
            description = Description,
            parameters = JsonSerializer.Deserialize<object>(ParametersJsonSchema)
        };
    }
}

/// <summary>Result of a tool execution.</summary>
internal sealed class ToolResult
{
    public bool Success { get; set; }
    public string Content { get; set; } = "";
    public string Error { get; set; } = "";
}
