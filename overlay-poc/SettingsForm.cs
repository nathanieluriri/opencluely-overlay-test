using System.Drawing;
using System.Windows.Forms;
using OverlayPoc.Models;

namespace OverlayPoc;

/// <summary>
/// Settings dialog: API key, model selection, prompts, tool config, cost controls.
/// Reads/writes %LOCALAPPDATA%\OverlayPoc\config.json.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TextBox _apiKey = new();
    private readonly TextBox _model = new();
    private readonly TextBox _visionModel = new();
    private readonly TextBox _fallbackModels = new();
    private readonly TextBox _baseUrl = new();
    private readonly TextBox _systemPrompt = new();
    private readonly TextBox _visionPrompt = new();
    private readonly NumericUpDown _maxTokens = new();
    private readonly CheckBox _deselectAfterCopy = new();

    // Tool settings
    private readonly CheckBox _enableWebSearch = new();
    private readonly TextBox _webSearchEngine = new();
    private readonly NumericUpDown _webSearchMaxResults = new();
    private readonly TextBox _webSearchAllowedDomains = new();
    private readonly TextBox _webSearchExcludedDomains = new();

    private readonly CheckBox _enableWebFetch = new();
    private readonly TextBox _webFetchEngine = new();
    private readonly NumericUpDown _webFetchMaxUses = new();
    private readonly TextBox _webFetchAllowedDomains = new();
    private readonly TextBox _webFetchBlockedDomains = new();

    // Cost controls
    private readonly NumericUpDown _spendingLimit = new();
    private readonly NumericUpDown _maxToolCalls = new();

    private readonly Button _save = new();
    private readonly Button _cancel = new();
    private readonly Button _fetchModels = new();
    private readonly ListBox _modelList = new();

    private readonly LlmConfig _config;

    public SettingsForm(LlmConfig config)
    {
        _config = config;

        Text = "OverlayPoc Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        ClientSize = new Size(560, 900);

        BuildUi();
        LoadFromConfig();
    }

    /// <summary>Track whether the user clicked Save before closing.</summary>
    public bool Saved { get; private set; }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOPMOST;
            return cp;
        }
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 22,
            Padding = new Padding(16),
            BackColor = Theme.Background,
            AutoScroll = true
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 22; i++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var row = 0;

        // ── Connection ──
        root.Controls.Add(MakeSectionLabel("CONNECTION"), 0, row++);
        root.Controls.Add(new Label(), 1, row++); // spacer

        root.Controls.Add(MakeLabel("API Key"), 0, row);
        _apiKey.Dock = DockStyle.Fill;
        StyleInput(_apiKey);
        _apiKey.UseSystemPasswordChar = true;
        root.Controls.Add(_apiKey, 1, row++);

        root.Controls.Add(MakeLabel("Base URL"), 0, row);
        _baseUrl.Dock = DockStyle.Fill;
        StyleInput(_baseUrl);
        root.Controls.Add(_baseUrl, 1, row++);

        // ── Models ──
        root.Controls.Add(MakeSectionLabel("MODELS"), 0, row++);
        root.Controls.Add(new Label(), 1, row++);

        root.Controls.Add(MakeLabel("Default Model"), 0, row);
        _model.Dock = DockStyle.Fill;
        StyleInput(_model);
        root.Controls.Add(_model, 1, row++);

        root.Controls.Add(MakeLabel("Vision Model"), 0, row);
        _visionModel.Dock = DockStyle.Fill;
        StyleInput(_visionModel);
        root.Controls.Add(_visionModel, 1, row++);

        root.Controls.Add(MakeLabel("Fallback Models"), 0, row);
        _fallbackModels.Dock = DockStyle.Fill;
        StyleInput(_fallbackModels);
        _fallbackModels.PlaceholderText = "comma-separated, e.g. deepseek/deepseek-v4.1-flash, openai/gpt-4o-mini";
        root.Controls.Add(_fallbackModels, 1, row++);

        root.Controls.Add(MakeLabel("Max Tokens"), 0, row);
        _maxTokens.Dock = DockStyle.Left;
        StyleInput(_maxTokens);
        _maxTokens.Minimum = 1;
        _maxTokens.Maximum = 128000;
        _maxTokens.Width = 100;
        root.Controls.Add(_maxTokens, 1, row++);

        root.Controls.Add(new Label(), 0, row);
        _deselectAfterCopy.Text = "Deselect after copy (Right Arrow)";
        _deselectAfterCopy.ForeColor = Theme.TextPrimary;
        _deselectAfterCopy.BackColor = Theme.Background;
        _deselectAfterCopy.AutoSize = true;
        root.Controls.Add(_deselectAfterCopy, 1, row++);

        // ── Prompts ──
        root.Controls.Add(MakeSectionLabel("PROMPTS"), 0, row++);
        root.Controls.Add(new Label(), 1, row++);

        root.Controls.Add(MakeLabel("System Prompt"), 0, row);
        _systemPrompt.Dock = DockStyle.Fill;
        _systemPrompt.Multiline = true;
        _systemPrompt.Height = 70;
        StyleInput(_systemPrompt);
        _systemPrompt.ScrollBars = ScrollBars.Vertical;
        root.Controls.Add(_systemPrompt, 1, row++);

        root.Controls.Add(MakeLabel("Vision Prompt"), 0, row);
        _visionPrompt.Dock = DockStyle.Fill;
        _visionPrompt.Multiline = true;
        _visionPrompt.Height = 70;
        StyleInput(_visionPrompt);
        _visionPrompt.ScrollBars = ScrollBars.Vertical;
        root.Controls.Add(_visionPrompt, 1, row++);

        // ── Web Search ──
        root.Controls.Add(MakeSectionLabel("WEB SEARCH"), 0, row++);
        root.Controls.Add(new Label(), 1, row++);

        root.Controls.Add(new Label(), 0, row);
        _enableWebSearch.Text = "Enable web search tool";
        _enableWebSearch.ForeColor = Theme.TextPrimary;
        _enableWebSearch.BackColor = Theme.Background;
        _enableWebSearch.AutoSize = true;
        root.Controls.Add(_enableWebSearch, 1, row++);

        root.Controls.Add(MakeLabel("Search Engine"), 0, row);
        _webSearchEngine.Dock = DockStyle.Fill;
        StyleInput(_webSearchEngine);
        _webSearchEngine.PlaceholderText = "auto, exa, parallel, perplexity, firecrawl, native";
        root.Controls.Add(_webSearchEngine, 1, row++);

        root.Controls.Add(MakeLabel("Max Results"), 0, row);
        _webSearchMaxResults.Dock = DockStyle.Left;
        StyleInput(_webSearchMaxResults);
        _webSearchMaxResults.Minimum = 1;
        _webSearchMaxResults.Maximum = 25;
        _webSearchMaxResults.Width = 80;
        root.Controls.Add(_webSearchMaxResults, 1, row++);

        root.Controls.Add(MakeLabel("Allowed Domains"), 0, row);
        _webSearchAllowedDomains.Dock = DockStyle.Fill;
        StyleInput(_webSearchAllowedDomains);
        _webSearchAllowedDomains.PlaceholderText = "comma-separated, e.g. arxiv.org, nature.com";
        root.Controls.Add(_webSearchAllowedDomains, 1, row++);

        root.Controls.Add(MakeLabel("Excluded Domains"), 0, row);
        _webSearchExcludedDomains.Dock = DockStyle.Fill;
        StyleInput(_webSearchExcludedDomains);
        _webSearchExcludedDomains.PlaceholderText = "comma-separated, e.g. reddit.com";
        root.Controls.Add(_webSearchExcludedDomains, 1, row++);

        // ── Web Fetch ──
        root.Controls.Add(MakeSectionLabel("WEB FETCH"), 0, row++);
        root.Controls.Add(new Label(), 1, row++);

        root.Controls.Add(new Label(), 0, row);
        _enableWebFetch.Text = "Enable web fetch tool";
        _enableWebFetch.ForeColor = Theme.TextPrimary;
        _enableWebFetch.BackColor = Theme.Background;
        _enableWebFetch.AutoSize = true;
        root.Controls.Add(_enableWebFetch, 1, row++);

        root.Controls.Add(MakeLabel("Fetch Engine"), 0, row);
        _webFetchEngine.Dock = DockStyle.Fill;
        StyleInput(_webFetchEngine);
        _webFetchEngine.PlaceholderText = "auto, exa, openrouter, firecrawl, parallel, native";
        root.Controls.Add(_webFetchEngine, 1, row++);

        root.Controls.Add(MakeLabel("Max Fetch Uses"), 0, row);
        _webFetchMaxUses.Dock = DockStyle.Left;
        StyleInput(_webFetchMaxUses);
        _webFetchMaxUses.Minimum = 1;
        _webFetchMaxUses.Maximum = 50;
        _webFetchMaxUses.Width = 80;
        root.Controls.Add(_webFetchMaxUses, 1, row++);

        root.Controls.Add(MakeLabel("Allowed Domains"), 0, row);
        _webFetchAllowedDomains.Dock = DockStyle.Fill;
        StyleInput(_webFetchAllowedDomains);
        _webFetchAllowedDomains.PlaceholderText = "comma-separated";
        root.Controls.Add(_webFetchAllowedDomains, 1, row++);

        root.Controls.Add(MakeLabel("Blocked Domains"), 0, row);
        _webFetchBlockedDomains.Dock = DockStyle.Fill;
        StyleInput(_webFetchBlockedDomains);
        _webFetchBlockedDomains.PlaceholderText = "comma-separated";
        root.Controls.Add(_webFetchBlockedDomains, 1, row++);

        // ── Cost Controls ──
        root.Controls.Add(MakeSectionLabel("COST CONTROLS"), 0, row++);
        root.Controls.Add(new Label(), 1, row++);

        root.Controls.Add(MakeLabel("Spending Limit ($)"), 0, row);
        _spendingLimit.Dock = DockStyle.Left;
        StyleInput(_spendingLimit);
        _spendingLimit.Minimum = 0;
        _spendingLimit.Maximum = 10000;
        _spendingLimit.DecimalPlaces = 2;
        _spendingLimit.Width = 100;
        root.Controls.Add(_spendingLimit, 1, row++);

        root.Controls.Add(MakeLabel("Max Tool Calls"), 0, row);
        _maxToolCalls.Dock = DockStyle.Left;
        StyleInput(_maxToolCalls);
        _maxToolCalls.Minimum = 1;
        _maxToolCalls.Maximum = 30;
        _maxToolCalls.Width = 80;
        root.Controls.Add(_maxToolCalls, 1, row++);

        // ── Model Browser ──
        root.Controls.Add(MakeSectionLabel("MODEL BROWSER"), 0, row++);
        root.Controls.Add(new Label(), 1, row++);

        root.Controls.Add(new Label(), 0, row);
        _fetchModels.Text = "Fetch available models";
        _fetchModels.Dock = DockStyle.Left;
        _fetchModels.FlatStyle = FlatStyle.Flat;
        _fetchModels.BackColor = Theme.ButtonBackground;
        _fetchModels.ForeColor = Theme.TextPrimary;
        _fetchModels.Width = 160;
        _fetchModels.Click += async (_, _) => await FetchModelsAsync();
        root.Controls.Add(_fetchModels, 1, row++);

        root.Controls.Add(MakeLabel("Double-click = Default, Ctrl+V = Vision"), 0, row);
        _modelList.Dock = DockStyle.Fill;
        _modelList.Height = 100;
        StyleInput(_modelList);
        _modelList.DoubleClick += (_, _) =>
        {
            if (_modelList.SelectedItem is string id)
                _model.Text = id;
        };
        _modelList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.V && e.Control && _modelList.SelectedItem is string id)
                _visionModel.Text = id;
        };
        root.Controls.Add(_modelList, 1, row++);

        // ── Buttons ──
        root.Controls.Add(new Label(), 0, row++);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Theme.Background
        };
        _save.Text = "Save";
        _save.FlatStyle = FlatStyle.Flat;
        _save.BackColor = Theme.ButtonBackgroundActive;
        _save.ForeColor = Theme.TextPrimary;
        _save.Width = 90;
        _save.Click += (_, _) => { SaveToConfig(); Saved = true; Close(); };

        _cancel.Text = "Cancel";
        _cancel.FlatStyle = FlatStyle.Flat;
        _cancel.BackColor = Theme.ButtonBackground;
        _cancel.ForeColor = Theme.TextPrimary;
        _cancel.Width = 90;
        _cancel.Click += (_, _) => Close();

        buttons.Controls.Add(_save);
        buttons.Controls.Add(_cancel);
        root.Controls.Add(buttons, 1, row++);

        Controls.Add(root);
    }

    private static void StyleInput(Control input)
    {
        input.BackColor = Theme.InputBackground;
        input.ForeColor = Theme.TextPrimary;
        if (input is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle;
        if (input is NumericUpDown nud) nud.BorderStyle = BorderStyle.FixedSingle;
        if (input is ListBox lb) lb.BorderStyle = BorderStyle.FixedSingle;
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        ForeColor = Theme.TextSecondary,
        Font = Theme.LabelBold,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Padding = new Padding(0, 6, 0, 0)
    };

    private Label MakeSectionLabel(string text) => new()
    {
        Text = text,
        ForeColor = Theme.TextOnAccent,
        Font = Theme.CaptionBold,
        AutoSize = true,
        Padding = new Padding(0, 12, 0, 2)
    };

    private void LoadFromConfig()
    {
        _apiKey.Text = _config.ApiKey;
        _baseUrl.Text = _config.BaseUrl;
        _model.Text = _config.Model;
        _visionModel.Text = _config.VisionModel ?? "";
        _fallbackModels.Text = string.Join(", ", _config.FallbackModels);
        _systemPrompt.Text = _config.SystemPrompt;
        _visionPrompt.Text = _config.VisionPrompt;
        _maxTokens.Value = Math.Clamp(_config.MaxTokens, 1, 128000);
        _deselectAfterCopy.Checked = _config.DeselectAfterCopy;

        _enableWebSearch.Checked = _config.EnableWebSearch;
        _webSearchEngine.Text = _config.WebSearchEngine;
        _webSearchMaxResults.Value = Math.Clamp(_config.WebSearchMaxResults, 1, 25);
        _webSearchAllowedDomains.Text = string.Join(", ", _config.WebSearchAllowedDomains);
        _webSearchExcludedDomains.Text = string.Join(", ", _config.WebSearchExcludedDomains);

        _enableWebFetch.Checked = _config.EnableWebFetch;
        _webFetchEngine.Text = _config.WebFetchEngine;
        _webFetchMaxUses.Value = Math.Clamp(_config.WebFetchMaxUses, 1, 50);
        _webFetchAllowedDomains.Text = string.Join(", ", _config.WebFetchAllowedDomains);
        _webFetchBlockedDomains.Text = string.Join(", ", _config.WebFetchBlockedDomains);

        _spendingLimit.Value = Math.Clamp(_config.SpendingLimitUsd, 0, 10000);
        _maxToolCalls.Value = Math.Clamp(_config.MaxToolCallsPerRequest, 1, 30);
    }

    private void SaveToConfig()
    {
        _config.ApiKey = _apiKey.Text.Trim();
        _config.BaseUrl = _baseUrl.Text.Trim();
        _config.Model = _model.Text.Trim();
        _config.VisionModel = string.IsNullOrWhiteSpace(_visionModel.Text) ? null : _visionModel.Text.Trim();
        _config.FallbackModels = _fallbackModels.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _config.SystemPrompt = _systemPrompt.Text.Trim();
        _config.VisionPrompt = _visionPrompt.Text.Trim();
        _config.MaxTokens = (int)_maxTokens.Value;
        _config.DeselectAfterCopy = _deselectAfterCopy.Checked;

        _config.EnableWebSearch = _enableWebSearch.Checked;
        _config.WebSearchEngine = _webSearchEngine.Text.Trim();
        _config.WebSearchMaxResults = (int)_webSearchMaxResults.Value;
        _config.WebSearchAllowedDomains = _webSearchAllowedDomains.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _config.WebSearchExcludedDomains = _webSearchExcludedDomains.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _config.EnableWebFetch = _enableWebFetch.Checked;
        _config.WebFetchEngine = _webFetchEngine.Text.Trim();
        _config.WebFetchMaxUses = (int)_webFetchMaxUses.Value;
        _config.WebFetchAllowedDomains = _webFetchAllowedDomains.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _config.WebFetchBlockedDomains = _webFetchBlockedDomains.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _config.SpendingLimitUsd = _spendingLimit.Value;
        _config.MaxToolCallsPerRequest = (int)_maxToolCalls.Value;

        try
        {
            Directory.CreateDirectory(LlmConfigLoader.DirectoryPath);
            var json = System.Text.Json.JsonSerializer.Serialize(_config,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(LlmConfigLoader.ConfigPath, json);
            Program.Log("Settings saved");
        }
        catch (Exception ex)
        {
            Program.Log("Settings save failed: " + ex.Message);
            _fetchModels.Text = "Save failed!";
        }
    }

    private async Task FetchModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiKey.Text))
        {
            _fetchModels.Text = "Enter API key first";
            return;
        }

        _fetchModels.Enabled = false;
        _fetchModels.Text = "Fetching...";
        _modelList.Items.Clear();

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey.Text.Trim());

            var url = $"{_baseUrl.Text.Trim().TrimEnd('/')}/models";
            var json = await client.GetStringAsync(url);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                var models = new List<string>();
                foreach (var m in data.EnumerateArray())
                {
                    if (m.TryGetProperty("id", out var id))
                        models.Add(id.GetString() ?? "");
                }
                models.Sort();
                foreach (var m in models)
                    _modelList.Items.Add(m);
                Program.Log($"Fetched {models.Count} models");
            }
        }
        catch (Exception ex)
        {
            Program.Log("Fetch models failed: " + ex.Message);
            _fetchModels.Text = "Fetch failed";
        }
        finally
        {
            _fetchModels.Enabled = true;
            if (_fetchModels.Text == "Fetching...")
                _fetchModels.Text = "Fetch available models";
        }
    }
}
