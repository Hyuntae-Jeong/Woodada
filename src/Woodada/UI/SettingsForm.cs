using Woodada.Core;
using Woodada.Interop;
using Woodada.Settings;
using Woodada.Sprites;
using Woodada.Stats;

namespace Woodada.UI;

/// <summary>
/// 설정 탭 — 위: 표시할 고양이 · 모니터 · 공통 옵션 / 아래: 고양이별 색·크기 + 미리보기.
/// 바꾸는 즉시 오버레이에 반영하고 저장한다.
/// 프로필 탭 — 오늘·지금까지 달린 거리를 고양이별로 보여준다.
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly (string Name, string Hex)[] Palette =
    {
        ("까망", "#1E1E22"), ("치즈", "#F4A261"), ("고등어", "#6D8BA8"), ("하양", "#ECE7DF"),
        ("삼색", "#C9825B"), ("회색", "#9A9AA0"), ("민트", "#7FB8A4"), ("라벤더", "#AE8FC7"),
        ("레몬", "#E6C55A"), ("체리", "#D9626B"),
    };

    public enum Page { Settings, Profile }

    private readonly SettingsStore _store;
    private readonly StatsStore _stats;
    private readonly MetricSampler _sampler;
    private AppSettings _working;

    private readonly TabControl _tabs = new();
    private readonly TabPage _settingsPage = new("설정");
    private readonly TabPage _profilePage = new("프로필");
    private readonly Button _reset = new();

    private readonly FlowLayoutPanel _visibility = new();
    private readonly ComboBox _monitor = new();
    private readonly CheckBox _hideFullScreen = new();
    private readonly ListBox _list = new();
    private readonly FlowLayoutPanel _swatches = new();
    private readonly TrackBar _size = new();
    private readonly Label _sizeLabel = new();
    private readonly Label _previewLabel = new();
    private readonly RunnerPreview _preview = new();
    private bool _loading;

    private readonly Label _todayHeadline = new();
    private readonly Label _todayCompare = new();
    private readonly TableLayoutPanel _runnerStats = new();
    private readonly Dictionary<string, (Label Today, Label Total)> _runnerStatLabels = new();
    private readonly Label _totalLine = new();
    private readonly Label _totalCompare = new();
    private readonly Label _pitchNote = new();
    private readonly System.Windows.Forms.Timer _profileTimer = new() { Interval = 1000 };

    public SettingsForm(SettingsStore store, StatsStore stats, MetricSampler sampler)
    {
        _store = store;
        _stats = stats;
        _sampler = sampler;
        _working = store.Current.Clone();

        Text = AppInfo.IsDev ? "Woodada 설정 (개발용)" : "Woodada 설정";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 600);

        BuildLayout();
        LoadAll();

        // 트레이 메뉴에서 표시 여부를 바꿔도 열려 있는 설정 창이 따라가게 한다.
        _store.Changed += OnStoreChanged;
        // 프로필 탭을 보고 있는 동안엔 고양이가 달리는 만큼 숫자가 올라가게 한다.
        _profileTimer.Tick += (_, _) => RefreshProfile();
        FormClosed += (_, _) =>
        {
            _store.Changed -= OnStoreChanged;
            _profileTimer.Dispose();
        };
    }

    public void ShowPage(Page page)
    {
        _tabs.SelectedTab = page == Page.Profile ? _profilePage : _settingsPage;
        // 창이 뜨기 전에 탭을 바꾸면 SelectedIndexChanged가 오지 않으므로 직접 맞춘다.
        OnPageChanged();
    }

    private RunnerConfig? Selected => _list.SelectedItem as RunnerConfig;

    // ───────────────────────── 레이아웃 ─────────────────────────

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(6) };
        settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        settings.Controls.Add(BuildGeneralGroup(), 0, 0);
        settings.Controls.Add(BuildAppearanceGroup(), 0, 1);
        _settingsPage.UseVisualStyleBackColor = true;
        _settingsPage.Controls.Add(settings);

        _profilePage.UseVisualStyleBackColor = true;
        _profilePage.Controls.Add(BuildProfile());

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(_settingsPage);
        _tabs.TabPages.Add(_profilePage);
        _tabs.SelectedIndexChanged += (_, _) => OnPageChanged();
        root.Controls.Add(_tabs, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0),
        };
        // 모달이 아닌 창(Show)이라 DialogResult만으로는 닫히지 않는다. 직접 Close 한다.
        var close = new Button { Text = "닫기", AutoSize = true };
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        _reset.Text = "선택한 고양이 기본값으로";
        _reset.AutoSize = true;
        _reset.Click += (_, _) => ResetSelected();
        buttons.Controls.Add(_reset);
        AcceptButton = close;
        CancelButton = close;
        root.Controls.Add(buttons, 0, 1);
    }

    private void OnPageChanged()
    {
        bool profile = _tabs.SelectedTab == _profilePage;
        _reset.Visible = !profile;
        if (profile)
        {
            BuildRunnerStatRows();
            RefreshProfile();
            _profileTimer.Start();
        }
        else
        {
            _profileTimer.Stop();
        }
    }

    private GroupBox BuildGeneralGroup()
    {
        var group = new GroupBox { Text = "일반", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 6, 10, 8) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.Controls.Add(grid);

        grid.Controls.Add(RowLabel("표시할 고양이"), 0, 0);
        _visibility.AutoSize = true;
        _visibility.WrapContents = false;
        _visibility.Margin = new Padding(0, 2, 0, 4);
        grid.Controls.Add(_visibility, 1, 0);

        grid.Controls.Add(RowLabel("모니터"), 0, 1);
        var monitorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
        _monitor.DropDownStyle = ComboBoxStyle.DropDownList;
        _monitor.Width = 380;
        _monitor.DisplayMember = nameof(MonitorCatalog.MonitorItem.Label);
        _monitor.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _monitor.SelectedItem is not MonitorCatalog.MonitorItem m) return;
            _working.MonitorDeviceName = m.DeviceName;
            Commit();
        };
        var refresh = new Button { Text = "다시 찾기", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        refresh.Click += (_, _) => LoadMonitors();
        monitorRow.Controls.Add(_monitor);
        monitorRow.Controls.Add(refresh);
        grid.Controls.Add(monitorRow, 1, 1);

        _hideFullScreen.Text = "전체 화면 앱 실행 중에는 숨기기";
        _hideFullScreen.AutoSize = true;
        _hideFullScreen.Margin = new Padding(0, 4, 0, 0);
        _hideFullScreen.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _working.HideInFullScreen = _hideFullScreen.Checked;
            Commit();
        };
        grid.Controls.Add(_hideFullScreen, 1, 2);
        return group;
    }

    private GroupBox BuildAppearanceGroup()
    {
        var group = new GroupBox { Text = "고양이별 모양", Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 10), Margin = new Padding(3, 10, 3, 3) };
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.Controls.Add(split);

        _list.Dock = DockStyle.Fill;
        _list.IntegralHeight = false;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = 36;
        _list.DrawItem += DrawRunnerItem;
        _list.SelectedIndexChanged += (_, _) => LoadSelected();
        split.Controls.Add(_list, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12, 0, 0, 0) };
        for (int i = 0; i < 5; i++) right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Controls.Add(right, 1, 0);

        right.Controls.Add(RowLabel("색"));
        _swatches.AutoSize = true;
        _swatches.WrapContents = true;
        _swatches.MaximumSize = new Size(420, 0);
        _swatches.Margin = new Padding(0, 0, 0, 8);
        foreach (var (name, hex) in Palette) _swatches.Controls.Add(MakeSwatch(name, hex));
        var custom = new Button { Text = "직접 선택…", AutoSize = true, Margin = new Padding(6, 3, 3, 3) };
        custom.Click += (_, _) => PickCustomColor();
        _swatches.Controls.Add(custom);
        right.Controls.Add(_swatches);

        right.Controls.Add(RowLabel("크기"));
        var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        _size.Minimum = AppSettings.MinSize;
        _size.Maximum = AppSettings.MaxSize;
        _size.TickFrequency = 8;
        _size.LargeChange = 4;
        _size.Width = 300;
        _size.ValueChanged += (_, _) =>
        {
            _sizeLabel.Text = $"{_size.Value} px";
            Edit(r => r.Size = _size.Value);
        };
        _sizeLabel.AutoSize = true;
        _sizeLabel.Margin = new Padding(6, 10, 0, 0);
        sizeRow.Controls.Add(_size);
        sizeRow.Controls.Add(_sizeLabel);
        right.Controls.Add(sizeRow);

        _previewLabel.AutoSize = true;
        _previewLabel.Margin = new Padding(0, 2, 0, 4);
        right.Controls.Add(_previewLabel);

        _preview.Dock = DockStyle.Fill;
        _preview.MinimumSize = new Size(0, 110);
        _preview.BackColor = Color.FromArgb(255, 250, 249, 247);
        right.Controls.Add(_preview);
        return group;
    }

    private static Label RowLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        Margin = new Padding(0, 6, 12, 4),
    };

    private Button MakeSwatch(string name, string hex)
    {
        var color = ColorUtil.Parse(hex, Color.Orange);
        var b = new Button
        {
            Width = 28, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = color, Tag = hex,
            Margin = new Padding(3), Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = ColorUtil.Scale(color, 0.6);
        new ToolTip().SetToolTip(b, name);
        b.Click += (_, _) => Edit(r => r.Color = hex);
        return b;
    }

    // ───────────────────────── 프로필 ─────────────────────────

    private Control BuildProfile()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20, 18, 20, 12) };
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _todayHeadline.AutoSize = true;
        _todayHeadline.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
        _todayHeadline.Margin = new Padding(0, 0, 0, 2);
        layout.Controls.Add(_todayHeadline);

        _todayCompare.AutoSize = true;
        _todayCompare.Font = new Font("Segoe UI", 10f);
        _todayCompare.ForeColor = SystemColors.GrayText;
        _todayCompare.Margin = new Padding(2, 0, 0, 18);
        layout.Controls.Add(_todayCompare);

        var group = new GroupBox { Text = "고양이별 기록", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 6, 10, 8) };
        _runnerStats.Dock = DockStyle.Fill;
        _runnerStats.AutoSize = true;
        _runnerStats.ColumnCount = 4;
        _runnerStats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        _runnerStats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _runnerStats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        _runnerStats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        group.Controls.Add(_runnerStats);
        layout.Controls.Add(group);

        _totalLine.AutoSize = true;
        _totalLine.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _totalLine.Margin = new Padding(2, 18, 0, 2);
        layout.Controls.Add(_totalLine);

        _totalCompare.AutoSize = true;
        _totalCompare.Font = new Font("Segoe UI", 10f);
        _totalCompare.ForeColor = SystemColors.GrayText;
        _totalCompare.Margin = new Padding(2, 0, 0, 0);
        layout.Controls.Add(_totalCompare);

        layout.Controls.Add(new Panel { Dock = DockStyle.Fill });

        _pitchNote.AutoSize = true;
        _pitchNote.Font = new Font("Segoe UI", 8.5f);
        _pitchNote.ForeColor = SystemColors.GrayText;
        _pitchNote.Margin = new Padding(2, 8, 0, 0);
        layout.Controls.Add(_pitchNote);
        return layout;
    }

    /// <summary>러너 구성(색·표시 여부)이 바뀌었을 수 있으니 탭을 열 때마다 표를 새로 만든다. 숫자는 RefreshProfile이 채운다.</summary>
    private void BuildRunnerStatRows()
    {
        _runnerStats.SuspendLayout();
        foreach (Control c in _runnerStats.Controls.Cast<Control>().ToList()) c.Dispose();
        _runnerStats.Controls.Clear();
        _runnerStats.RowStyles.Clear();
        _runnerStatLabels.Clear();

        _runnerStats.RowCount = _working.Runners.Count + 1;
        _runnerStats.Controls.Add(StatHeader("오늘"), 2, 0);
        _runnerStats.Controls.Add(StatHeader("지금까지"), 3, 0);

        int row = 1;
        foreach (var r in _working.Runners)
        {
            _runnerStats.Controls.Add(new RunnerIcon(r) { Size = new Size(48, 34), Margin = new Padding(0, 2, 4, 2) }, 0, row);
            _runnerStats.Controls.Add(StatCell(RunnerName(r) + (r.Enabled ? "" : " (숨김)"), ContentAlignment.MiddleLeft), 1, row);
            var today = StatCell("", ContentAlignment.MiddleRight);
            var total = StatCell("", ContentAlignment.MiddleRight);
            _runnerStats.Controls.Add(today, 2, row);
            _runnerStats.Controls.Add(total, 3, row);
            _runnerStatLabels[r.Id] = (today, total);
            row++;
        }
        _runnerStats.ResumeLayout();
    }

    private static Label StatHeader(string text) => new()
    {
        Text = text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill, AutoSize = false, Height = 22,
        ForeColor = SystemColors.GrayText, Margin = new Padding(0),
    };

    private static Label StatCell(string text, ContentAlignment align) => new()
    {
        Text = text, TextAlign = align, Dock = DockStyle.Fill, AutoSize = false, Height = 38,
        Font = new Font("Segoe UI", 10.5f), Margin = new Padding(0),
    };

    private void RefreshProfile()
    {
        double today = _stats.TodayMillimeters();
        _todayHeadline.Text = today > 0 ? $"오늘 {DistanceText.Format(today)} 달렸어요" : "오늘은 아직 쉬는 중이에요";
        _todayCompare.Text = today > 0 ? DistanceText.Compare(today) : "고양이가 달리기 시작하면 여기 쌓여요";

        foreach (var (id, labels) in _runnerStatLabels)
        {
            labels.Today.Text = DistanceText.Format(_stats.TodayMillimeters(id));
            labels.Total.Text = DistanceText.Format(_stats.Total(id).Millimeters);
        }

        double total = _stats.TotalMillimeters();
        _totalLine.Text = $"지금까지 총 {DistanceText.Format(total)}";
        _totalCompare.Text = DistanceText.Compare(total);

        var pitch = MonitorPitch.For(MonitorCatalog.Resolve(_working.MonitorDeviceName));
        _pitchNote.Text = pitch.Measured
            ? $"{pitch.MonitorName ?? "모니터"} 실제 크기({pitch.WidthMm} × {pitch.HeightMm} mm)로 계산했어요 · 1px = {pitch.MillimetersPerPixel:0.000} mm"
            : $"모니터 크기를 알 수 없어 화면 배율로 추정했어요 · 1px ≈ {pitch.MillimetersPerPixel:0.000} mm";
    }

    // ───────────────────────── 데이터 ↔ 화면 ─────────────────────────

    private void LoadAll()
    {
        _loading = true;
        try
        {
            int selected = Math.Max(0, _list.SelectedIndex);
            _list.Items.Clear();
            foreach (var r in _working.Runners) _list.Items.Add(r);
            _list.SelectedIndex = Math.Min(selected, _list.Items.Count - 1);

            _visibility.Controls.Clear();
            foreach (var r in _working.Runners)
            {
                var cb = new CheckBox
                {
                    Text = RunnerName(r), Checked = r.Enabled, AutoSize = true, Tag = r.Id, Margin = new Padding(0, 4, 18, 0),
                };
                cb.CheckedChanged += (_, _) => SetEnabled((string)cb.Tag!, cb.Checked);
                _visibility.Controls.Add(cb);
            }

            _hideFullScreen.Checked = _working.HideInFullScreen;
            LoadMonitors();
            // 프로필을 보는 중에 트레이 메뉴로 표시 여부를 바꾸면 표의 "(숨김)"도 따라가게 한다.
            if (_tabs.SelectedTab == _profilePage) BuildRunnerStatRows();
        }
        finally
        {
            _loading = false;
        }
        LoadSelected();
    }

    private void LoadMonitors()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            var items = MonitorCatalog.List();
            _monitor.Items.Clear();
            foreach (var m in items) _monitor.Items.Add(m);
            // 저장된 모니터가 지금 없으면(분리됨) 실제로 쓰이는 주 모니터를 선택해 보여준다.
            string current = MonitorCatalog.Resolve(_working.MonitorDeviceName).DeviceName;
            _monitor.SelectedItem = items.FirstOrDefault(m => m.DeviceName == current) ?? items.FirstOrDefault();
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void LoadSelected()
    {
        if (Selected is not { } r) return;
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            _size.Value = Math.Clamp(r.Size, _size.Minimum, _size.Maximum);
            _sizeLabel.Text = $"{_size.Value} px";
            var metric = r.Metric;
            _preview.UsageSource = () => _sampler.Read(metric);
            UpdatePreview();
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void SetEnabled(string id, bool enabled)
    {
        if (_loading) return;
        var r = _working.Runners.FirstOrDefault(x => x.Id == id);
        if (r is null) return;
        r.Enabled = enabled;
        _list.Invalidate();
        Commit();
    }

    private void Edit(Action<RunnerConfig> change)
    {
        if (_loading || Selected is not { } r) return;
        change(r);
        UpdatePreview();
        _list.Invalidate();
        Commit();
    }

    private void Commit() => _store.Update(_working.Clone());

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        // 내가 방금 저장한 값이면 화면과 같으므로 표시 여부만 비교해서 다를 때만 다시 읽는다.
        var current = _store.Current;
        bool differs = current.Runners.Count != _working.Runners.Count ||
                       current.Runners.Zip(_working.Runners).Any(p => p.First.Enabled != p.Second.Enabled);
        if (!differs) return;
        _working = current.Clone();
        LoadAll();
    }

    private void ResetSelected()
    {
        if (Selected is not { } r) return;
        var def = AppSettings.CreateDefault().Runners.FirstOrDefault(d => d.Id == r.Id);
        if (def is null) return;
        Edit(x =>
        {
            x.Color = def.Color;
            x.Size = def.Size;
        });
        LoadSelected();
    }

    private void PickCustomColor()
    {
        if (Selected is not { } r) return;
        using var dlg = new ColorDialog { FullOpen = true, Color = ColorUtil.Parse(r.Color, Color.Black) };
        if (dlg.ShowDialog(this) == DialogResult.OK) Edit(x => x.Color = ColorUtil.ToHex(dlg.Color));
    }

    private void UpdatePreview()
    {
        if (Selected is not { } r) return;
        _preview.Sprite = SpriteRegistry.Get(r.SpriteId);
        _preview.RunnerColor = ColorUtil.Parse(r.Color, Color.Black);
        _preview.RunnerSize = r.Size;
        _previewLabel.Text = $"미리보기 — 현재 {MetricName(r.Metric)} 사용률로 달리는 중";
        foreach (Control c in _swatches.Controls)
        {
            if (c is Button { Tag: string hex } b)
                b.FlatAppearance.BorderSize = string.Equals(hex, r.Color, StringComparison.OrdinalIgnoreCase) ? 3 : 1;
        }
    }

    private void DrawRunnerItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _list.Items[e.Index] is not RunnerConfig r) return;
        e.DrawBackground();
        var g = e.Graphics;
        var sprite = SpriteRegistry.Get(r.SpriteId);
        var size = sprite.Measure(e.Bounds.Height - 12);
        var box = new RectangleF(e.Bounds.Left + 6, e.Bounds.Top + 6, size.Width, size.Height);
        sprite.Draw(g, box, new RunnerPose(1, 0.0, 0.3), ColorUtil.Parse(r.Color, Color.Black));

        bool selected = (e.State & DrawItemState.Selected) != 0;
        var textColor = selected ? SystemColors.HighlightText : (r.Enabled ? SystemColors.ControlText : SystemColors.GrayText);
        string text = RunnerName(r) + (r.Enabled ? "" : " (숨김)");
        TextRenderer.DrawText(g, text, e.Font, new Point((int)box.Right + 8, e.Bounds.Top + (e.Bounds.Height - e.Font!.Height) / 2), textColor);
        e.DrawFocusRectangle();
    }

    public static string RunnerName(RunnerConfig r) => $"{MetricName(r.Metric)} {SpriteRegistry.Get(r.SpriteId).DisplayName}";

    public static string MetricName(MetricKind m) => m switch
    {
        MetricKind.Cpu => "CPU",
        MetricKind.Memory => "메모리",
        _ => m.ToString(),
    };
}
