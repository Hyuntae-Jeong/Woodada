using Woodada.Core;
using Woodada.Settings;
using Woodada.Sprites;

namespace Woodada.UI;

/// <summary>
/// 고양이 탭의 고양이 한 마리 카드: 이름 · 표시 여부 · 달리는 미리보기 · 크기.
/// 카드 어디를 눌러도 이 고양이가 선택되고, 아래 색 칸이 이 고양이에게 입혀진다.
/// </summary>
internal sealed class RunnerCard : CardPanel
{
    private readonly Label _title = new();
    private readonly Label _colorName = new();
    private readonly CheckBox _visible = new();
    private readonly RunnerPreview _preview = new();
    private readonly TrackBar _size = new();
    private readonly Label _sizeLabel = new();
    private bool _binding;

    public RunnerCard(string runnerId, Func<double> usage)
    {
        RunnerId = runnerId;
        Size = new Size(330, 208);
        Margin = new Padding(0, 0, 12, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = GameArt.Card };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        _title.AutoSize = true;
        _title.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        _title.ForeColor = GameArt.Text;
        _title.Margin = new Padding(0, 0, 0, 0);
        layout.Controls.Add(_title, 0, 0);

        _visible.Text = "표시";
        _visible.AutoSize = true;
        _visible.ForeColor = GameArt.Text;
        _visible.Margin = new Padding(0, 3, 0, 0);
        _visible.CheckedChanged += (_, _) =>
        {
            if (!_binding) VisibleToggled?.Invoke(this, _visible.Checked);
        };
        layout.Controls.Add(_visible, 1, 0);

        _colorName.AutoSize = true;
        _colorName.ForeColor = GameArt.SubText;
        _colorName.Margin = new Padding(1, 0, 0, 4);
        layout.Controls.Add(_colorName, 0, 1);
        layout.SetColumnSpan(_colorName, 2);

        _preview.Dock = DockStyle.Fill;
        _preview.Margin = new Padding(0, 0, 0, 4);
        _preview.UsageSource = usage;
        layout.Controls.Add(_preview, 0, 2);
        layout.SetColumnSpan(_preview, 2);

        var sizeRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0), BackColor = GameArt.Card };
        sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sizeRow.Controls.Add(new Label { Text = "크기", AutoSize = true, ForeColor = GameArt.SubText, Margin = new Padding(0, 8, 6, 0) }, 0, 0);
        _size.Minimum = AppSettings.MinSize;
        _size.Maximum = AppSettings.MaxSize;
        _size.TickStyle = TickStyle.None;
        _size.LargeChange = 4;
        _size.Dock = DockStyle.Fill;
        _size.BackColor = GameArt.Card;
        _size.Margin = new Padding(0, 2, 0, 0);
        _size.ValueChanged += (_, _) =>
        {
            _sizeLabel.Text = $"{_size.Value} px";
            if (!_binding) RunnerSizeChanged?.Invoke(this, _size.Value);
        };
        sizeRow.Controls.Add(_size, 1, 0);
        _sizeLabel.AutoSize = true;
        _sizeLabel.ForeColor = GameArt.Text;
        _sizeLabel.Margin = new Padding(4, 8, 0, 0);
        sizeRow.Controls.Add(_sizeLabel, 2, 0);
        layout.Controls.Add(sizeRow, 0, 3);
        layout.SetColumnSpan(sizeRow, 2);

        // 카드 안 어디를 눌러도(체크박스·슬라이더 포함) 이 고양이를 고른다.
        HookSelect(this);
    }

    public string RunnerId { get; }

    public event EventHandler? Picked;
    public event EventHandler<bool>? VisibleToggled;
    public event EventHandler<int>? RunnerSizeChanged;

    /// <summary>
    /// 설정값을 화면에 옮긴다. 이벤트는 내보내지 않는다.
    /// trialHex가 있으면 사기 전에 입어 보는 색으로 미리보기만 칠한다. 설정에는 저장되지 않는다.
    /// </summary>
    public void Bind(RunnerConfig r, string colorName, string? trialHex = null)
    {
        _binding = true;
        try
        {
            _title.Text = SettingsForm.RunnerName(r);
            _colorName.Text = trialHex is not null ? "입어 보는 중이에요 · 아직 사지 않았어요"
                : r.Enabled ? $"{colorName} · {SettingsForm.MetricName(r.Metric)} 사용률로 달려요"
                : $"{colorName} · 숨겨 둠";
            _colorName.ForeColor = trialHex is not null ? GameArt.Accent : GameArt.SubText;
            _visible.Checked = r.Enabled;
            _size.Value = Math.Clamp(r.Size, _size.Minimum, _size.Maximum);
            _sizeLabel.Text = $"{_size.Value} px";
            _preview.Sprite = SpriteRegistry.Get(r.SpriteId);
            _preview.RunnerColor = ColorUtil.Parse(trialHex ?? r.Color, Color.Black);
            _preview.RunnerSize = r.Size;
        }
        finally
        {
            _binding = false;
        }
    }

    private void HookSelect(Control c)
    {
        c.MouseDown += (_, _) => Picked?.Invoke(this, EventArgs.Empty);
        foreach (Control child in c.Controls) HookSelect(child);
    }
}
