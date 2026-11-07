using System.Drawing;
using System.Windows.Forms;
using OverlayPoc.Models;
using OverlayPoc.Services;

namespace OverlayPoc;

/// <summary>
/// Administrative dashboard showing usage statistics, costs, and request history.
/// </summary>
internal sealed class DashboardForm : Form
{
    private readonly UsageTracker _tracker;
    private readonly ModelCatalog _catalog;

    // Summary cards
    private Label _totalRequests;
    private Label _totalTokens;
    private Label _totalCost;
    private Label _avgLatency;
    private Label _webSearches;
    private Label _webFetches;
    private Label _fallbacks;
    private Label _errors;

    // Per-model grid
    private DataGridView _modelGrid;

    // Recent requests
    private DataGridView _requestGrid;

    // Filters
    private ComboBox _modelFilter;
    private DateTimePicker _dateFrom;
    private DateTimePicker _dateTo;

    private readonly System.Windows.Forms.Timer _refreshTimer;

    public DashboardForm(UsageTracker tracker, ModelCatalog catalog)
    {
        _tracker = tracker;
        _catalog = catalog;

        Text = "OverlayPoc Dashboard";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        ClientSize = new Size(900, 700);

        BuildUi();
        RefreshData();

        // Auto-refresh every 5 seconds
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick += (_, _) => RefreshData();
        _refreshTimer.Start();

        FormClosed += (_, _) => _refreshTimer?.Stop();
    }

    private void BuildUi()
    {
        // Initialize controls
        _modelGrid = new DataGridView();
        _requestGrid = new DataGridView();
        _modelFilter = new ComboBox();
        _dateFrom = new DateTimePicker();
        _dateTo = new DateTimePicker();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
            BackColor = Theme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // summary
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // filters
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); // model grid
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); // request grid

        // ── Summary cards ──
        var summaryPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = Theme.Background
        };

        _totalRequests = AddCard(summaryPanel, "Requests", "0");
        _totalTokens = AddCard(summaryPanel, "Tokens", "0 in / 0 out");
        _totalCost = AddCard(summaryPanel, "Cost", "$0.00");
        _avgLatency = AddCard(summaryPanel, "Avg Latency", "0ms");
        _webSearches = AddCard(summaryPanel, "Web Searches", "0");
        _webFetches = AddCard(summaryPanel, "Web Fetches", "0");
        _fallbacks = AddCard(summaryPanel, "Fallbacks", "0");
        _errors = AddCard(summaryPanel, "Errors", "0");

        root.Controls.Add(summaryPanel, 0, 0);

        // ── Filters ──
        var filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            BackColor = Theme.Background,
            Padding = new Padding(0, 8, 0, 8)
        };

        filterPanel.Controls.Add(MakeFilterLabel("Model:"));
        _modelFilter.Width = 200;
        _modelFilter.DropDownStyle = ComboBoxStyle.DropDown;
        _modelFilter.BackColor = Theme.InputBackground;
        _modelFilter.ForeColor = Theme.TextPrimary;
        _modelFilter.TextChanged += (_, _) => RefreshData();
        filterPanel.Controls.Add(_modelFilter);

        filterPanel.Controls.Add(MakeFilterLabel("  From:"));
        _dateFrom.Format = DateTimePickerFormat.Short;
        _dateFrom.Value = DateTime.Today.AddDays(-7);
        _dateFrom.ValueChanged += (_, _) => RefreshData();
        filterPanel.Controls.Add(_dateFrom);

        filterPanel.Controls.Add(MakeFilterLabel("  To:"));
        _dateTo.Format = DateTimePickerFormat.Short;
        _dateTo.Value = DateTime.Today;
        _dateTo.ValueChanged += (_, _) => RefreshData();
        filterPanel.Controls.Add(_dateTo);

        var refreshBtn = new Button
        {
            Text = "Refresh",
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.ButtonBackground,
            ForeColor = Theme.TextPrimary,
            Width = 80
        };
        refreshBtn.Click += (_, _) => RefreshData();
        filterPanel.Controls.Add(refreshBtn);

        var clearBtn = new Button
        {
            Text = "Clear Data",
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.ButtonBackground,
            ForeColor = Theme.TextPrimary,
            Width = 90
        };
        clearBtn.Click += (_, _) =>
        {
            _tracker.Clear();
            RefreshData();
        };
        filterPanel.Controls.Add(clearBtn);

        root.Controls.Add(filterPanel, 0, 1);

        // ── Model grid ──
        _modelGrid.Dock = DockStyle.Fill;
        _modelGrid.BackgroundColor = Theme.Surface;
        _modelGrid.ForeColor = Theme.TextPrimary;
        _modelGrid.GridColor = Theme.ButtonBackground;
        _modelGrid.BorderStyle = BorderStyle.None;
        _modelGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _modelGrid.ReadOnly = true;
        _modelGrid.AllowUserToAddRows = false;
        _modelGrid.RowHeadersVisible = false;
        _modelGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _modelGrid.Columns.Add("Model", "Model");
        _modelGrid.Columns.Add("Requests", "Requests");
        _modelGrid.Columns.Add("Input", "Input Tokens");
        _modelGrid.Columns.Add("Output", "Output Tokens");
        _modelGrid.Columns.Add("Cost", "Cost (USD)");
        _modelGrid.Columns.Add("Errors", "Errors");
        _modelGrid.Columns.Add("Fallbacks", "Fallbacks");
        _modelGrid.Columns.Add("Latency", "Avg Latency");

        var modelLabel = new Label
        {
            Text = "COST PER MODEL",
            ForeColor = Theme.TextMuted,
            Font = Theme.CaptionBold,
            Dock = DockStyle.Top,
            Height = 20
        };

        var modelPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        modelPanel.Controls.Add(_modelGrid);
        modelPanel.Controls.Add(modelLabel);
        _modelGrid.Top = 20;
        root.Controls.Add(modelPanel, 0, 2);

        // ── Request grid ──
        _requestGrid.Dock = DockStyle.Fill;
        _requestGrid.BackgroundColor = Theme.Surface;
        _requestGrid.ForeColor = Theme.TextPrimary;
        _requestGrid.GridColor = Theme.ButtonBackground;
        _requestGrid.BorderStyle = BorderStyle.None;
        _requestGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _requestGrid.ReadOnly = true;
        _requestGrid.AllowUserToAddRows = false;
        _requestGrid.RowHeadersVisible = false;
        _requestGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _requestGrid.Columns.Add("Time", "Time");
        _requestGrid.Columns.Add("Model", "Model");
        _requestGrid.Columns.Add("Type", "Type");
        _requestGrid.Columns.Add("Input", "In Tokens");
        _requestGrid.Columns.Add("Output", "Out Tokens");
        _requestGrid.Columns.Add("Cost", "Cost");
        _requestGrid.Columns.Add("Latency", "Latency");
        _requestGrid.Columns.Add("Status", "Status");

        var requestLabel = new Label
        {
            Text = "RECENT REQUESTS",
            ForeColor = Theme.TextMuted,
            Font = Theme.CaptionBold,
            Dock = DockStyle.Top,
            Height = 20
        };

        var requestPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        requestPanel.Controls.Add(_requestGrid);
        requestPanel.Controls.Add(requestLabel);
        _requestGrid.Top = 20;
        root.Controls.Add(requestPanel, 0, 3);

        Controls.Add(root);
    }

    private Label AddCard(FlowLayoutPanel parent, string title, string value)
    {
        var card = new Panel
        {
            Width = 120,
            Height = 70,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 8, 8),
            Padding = new Padding(8, 6, 8, 4)
        };

        var titleLabel = new Label
        {
            Text = title,
            ForeColor = Theme.TextMuted,
            Font = Theme.Caption,
            Dock = DockStyle.Top,
            Height = 18
        };

        var valueLabel = new Label
        {
            Text = value,
            ForeColor = Theme.TextPrimary,
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        card.Controls.Add(valueLabel);
        card.Controls.Add(titleLabel);
        parent.Controls.Add(card);

        return valueLabel;
    }

    private static Label MakeFilterLabel(string text) => new()
    {
        Text = text,
        ForeColor = Theme.TextSecondary,
        Font = Theme.BodySmall,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Padding = new Padding(0, 4, 0, 0)
    };

    private void RefreshData()
    {
        try
        {
            var since = _dateFrom.Value.Date;
            var summary = _tracker.GetSummary(since);

            // Summary cards
            _totalRequests.Text = summary.TotalRequests.ToString("N0");
            _totalTokens.Text = $"{summary.TotalInputTokens:N0} in / {summary.TotalOutputTokens:N0} out";
            _totalCost.Text = $"${summary.TotalCostUsd:F4}";
            _avgLatency.Text = $"{summary.AverageLatency.TotalMilliseconds:F0}ms";
            _webSearches.Text = summary.TotalWebSearches.ToString("N0");
            _webFetches.Text = summary.TotalWebFetches.ToString("N0");
            _fallbacks.Text = summary.TotalFallbacks.ToString("N0");
            _errors.Text = summary.TotalErrors.ToString("N0");

            // Model grid
            _modelGrid.Rows.Clear();
            foreach (var kvp in summary.PerModel.OrderByDescending(x => x.Value.CostUsd))
            {
                var m = kvp.Value;
                _modelGrid.Rows.Add(
                    m.Model,
                    m.Requests,
                    m.InputTokens.ToString("N0"),
                    m.OutputTokens.ToString("N0"),
                    $"${m.CostUsd:F4}",
                    m.Errors,
                    m.Fallbacks,
                    $"{m.AverageLatency.TotalMilliseconds:F0}ms"
                );
            }

            // Request grid
            var modelFilter = _modelFilter.Text?.Trim();
            var records = _tracker.GetRecent(200, modelFilter);
            _requestGrid.Rows.Clear();
            foreach (var r in records)
            {
                _requestGrid.Rows.Add(
                    r.Timestamp.ToLocalTime().ToString("MM/dd HH:mm:ss"),
                    r.Model,
                    r.RequestType,
                    r.InputTokens,
                    r.OutputTokens,
                    r.CostUsd.HasValue ? $"${r.CostUsd.Value:F4}" : "-",
                    $"{r.Latency.TotalMilliseconds:F0}ms",
                    r.Success ? "OK" : "Error"
                );

                if (!r.Success)
                    _requestGrid.Rows[_requestGrid.Rows.Count - 1].DefaultCellStyle.ForeColor = Color.IndianRed;
            }
        }
        catch (Exception ex)
        {
            Program.Log("Dashboard refresh failed: " + ex.Message);
        }
    }
}
