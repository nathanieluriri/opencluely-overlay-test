using System.Drawing;
using System.Windows.Forms;

namespace OverlayPoc;

/// <summary>
/// Settings dialog: API key, model selection, system prompt, max tokens.
/// Reads/writes %LOCALAPPDATA%\OverlayPoc\config.json.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TextBox _apiKey = new();
    private readonly TextBox _model = new();
    private readonly TextBox _visionModel = new();
    private readonly TextBox _baseUrl = new();
    private readonly TextBox _systemPrompt = new();
    private readonly NumericUpDown _maxTokens = new();
    private readonly CheckBox _deselectAfterCopy = new();
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
        ClientSize = new Size(520, 640);

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
            cp.ExStyle |= NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(16),
            BackColor = Theme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 10; i++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var row = 0;

        // API Key
        root.Controls.Add(MakeLabel("API Key"), 0, row);
        _apiKey.Dock = DockStyle.Fill;
        _apiKey.BackColor = Theme.InputBackground;
        _apiKey.ForeColor = Theme.TextPrimary;
        _apiKey.BorderStyle = BorderStyle.FixedSingle;
        _apiKey.UseSystemPasswordChar = true;
        root.Controls.Add(_apiKey, 1, row++);

        // Base URL
        root.Controls.Add(MakeLabel("Base URL"), 0, row);
        _baseUrl.Dock = DockStyle.Fill;
        _baseUrl.BackColor = Theme.InputBackground;
        _baseUrl.ForeColor = Theme.TextPrimary;
        _baseUrl.BorderStyle = BorderStyle.FixedSingle;
        root.Controls.Add(_baseUrl, 1, row++);

        // Model
        root.Controls.Add(MakeLabel("Model"), 0, row);
        _model.Dock = DockStyle.Fill;
        _model.BackColor = Theme.InputBackground;
        _model.ForeColor = Theme.TextPrimary;
        _model.BorderStyle = BorderStyle.FixedSingle;
        root.Controls.Add(_model, 1, row++);

        // Vision Model
        root.Controls.Add(MakeLabel("Vision Model"), 0, row);
        _visionModel.Dock = DockStyle.Fill;
        _visionModel.BackColor = Theme.InputBackground;
        _visionModel.ForeColor = Theme.TextPrimary;
        _visionModel.BorderStyle = BorderStyle.FixedSingle;
        root.Controls.Add(_visionModel, 1, row++);

        // Max Tokens
        root.Controls.Add(MakeLabel("Max Tokens"), 0, row);
        _maxTokens.Dock = DockStyle.Left;
        _maxTokens.BackColor = Theme.InputBackground;
        _maxTokens.ForeColor = Theme.TextPrimary;
        _maxTokens.BorderStyle = BorderStyle.FixedSingle;
        _maxTokens.Minimum = 1;
        _maxTokens.Maximum = 128000;
        _maxTokens.Width = 100;
        root.Controls.Add(_maxTokens, 1, row++);

        // Deselect after copy
        root.Controls.Add(new Label(), 0, row); // spacer
        _deselectAfterCopy.Text = "Deselect after copy (Right Arrow)";
        _deselectAfterCopy.ForeColor = Theme.TextPrimary;
        _deselectAfterCopy.BackColor = Theme.Background;
        _deselectAfterCopy.AutoSize = true;
        root.Controls.Add(_deselectAfterCopy, 1, row++);

        // System Prompt
        root.Controls.Add(MakeLabel("System Prompt"), 0, row);
        _systemPrompt.Dock = DockStyle.Fill;
        _systemPrompt.Multiline = true;
        _systemPrompt.Height = 100;
        _systemPrompt.BackColor = Theme.InputBackground;
        _systemPrompt.ForeColor = Theme.TextPrimary;
        _systemPrompt.BorderStyle = BorderStyle.FixedSingle;
        _systemPrompt.ScrollBars = ScrollBars.Vertical;
        root.Controls.Add(_systemPrompt, 1, row++);

        // Fetch models button
        root.Controls.Add(new Label(), 0, row);
        _fetchModels.Text = "Fetch available models";
        _fetchModels.Dock = DockStyle.Left;
        _fetchModels.FlatStyle = FlatStyle.Flat;
        _fetchModels.BackColor = Theme.ButtonBackground;
        _fetchModels.ForeColor = Theme.TextPrimary;
        _fetchModels.Width = 160;
        _fetchModels.Click += async (_, _) => await FetchModelsAsync();
        root.Controls.Add(_fetchModels, 1, row++);

        // Model list
        root.Controls.Add(MakeLabel("Models"), 0, row);
        _modelList.Dock = DockStyle.Fill;
        _modelList.Height = 120;
        _modelList.BackColor = Theme.InputBackground;
        _modelList.ForeColor = Theme.TextPrimary;
        _modelList.BorderStyle = BorderStyle.FixedSingle;
        _modelList.DoubleClick += (_, _) =>
        {
            if (_modelList.SelectedItem is string id)
                _model.Text = id;
        };
        root.Controls.Add(_modelList, 1, row++);

        // Buttons
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
        root.Controls.Add(new Label(), 0, row);
        root.Controls.Add(buttons, 1, row++);

        Controls.Add(root);
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

    private void LoadFromConfig()
    {
        _apiKey.Text = _config.ApiKey;
        _baseUrl.Text = _config.BaseUrl;
        _model.Text = _config.Model;
        _visionModel.Text = _config.VisionModel ?? "";
        _systemPrompt.Text = _config.SystemPrompt;
        _maxTokens.Value = Math.Clamp(_config.MaxTokens, 1, 128000);
        _deselectAfterCopy.Checked = _config.DeselectAfterCopy;
    }

    private void SaveToConfig()
    {
        _config.ApiKey = _apiKey.Text.Trim();
        _config.BaseUrl = _baseUrl.Text.Trim();
        _config.Model = _model.Text.Trim();
        _config.VisionModel = string.IsNullOrWhiteSpace(_visionModel.Text) ? null : _visionModel.Text.Trim();
        _config.SystemPrompt = _systemPrompt.Text.Trim();
        _config.MaxTokens = (int)_maxTokens.Value;
        _config.DeselectAfterCopy = _deselectAfterCopy.Checked;

        try
        {
            Directory.CreateDirectory(LlmConfigLoader.DirectoryPath);
            var json = System.Text.Json.JsonSerializer.Serialize(_config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
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
