using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Woodada.Settings;
using Woodada.Sprites;

namespace Woodada.UI;

/// <summary>
/// 고양이 탭의 고양이 한 마리 카드: 이름 · 표시 여부 · 달리는 미리보기 · 크기.
/// 카드 어디를 눌러도 이 고양이가 선택되고, 아래 색 칸이 이 고양이에게 입혀진다.
/// </summary>
internal sealed class RunnerCard : CardPanel
{
    private readonly TextBlock _title = new();
    private readonly TextBlock _colorName = new();
    private readonly CheckBox _visible = new();
    private readonly RunnerPreview _preview = new();
    private readonly Slider _size = new();
    private readonly TextBlock _sizeLabel = new();
    private bool _binding;

    public RunnerCard(string runnerId, Func<double> usage)
    {
        RunnerId = runnerId;
        Width = 330;
        Height = 208;
        Margin = new Thickness(0, 0, 12, 0);

        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
        };
        Child = layout;

        _title.FontSize = GameArt.FontTitle;
        _title.FontWeight = FontWeight.Bold;
        _title.Foreground = GameArt.Brush(GameArt.Text);
        _title.VerticalAlignment = VerticalAlignment.Center;
        layout.Children.Add(_title);

        _visible.Content = "표시";
        _visible.Foreground = GameArt.Brush(GameArt.Text);
        _visible.MinHeight = 0;
        _visible.Padding = new Thickness(6, 0, 0, 0);
        _visible.IsCheckedChanged += (_, _) =>
        {
            if (!_binding) VisibleToggled?.Invoke(this, _visible.IsChecked == true);
        };
        Grid.SetColumn(_visible, 1);
        layout.Children.Add(_visible);

        _colorName.FontSize = GameArt.FontBody;
        _colorName.Margin = new Thickness(1, 0, 0, 4);
        Grid.SetRow(_colorName, 1);
        Grid.SetColumnSpan(_colorName, 2);
        layout.Children.Add(_colorName);

        _preview.Margin = new Thickness(0, 0, 0, 4);
        _preview.UsageSource = usage;
        _preview.ClipToBounds = true;
        Grid.SetRow(_preview, 2);
        Grid.SetColumnSpan(_preview, 2);
        layout.Children.Add(_preview);

        var sizeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        sizeRow.Children.Add(new TextBlock
        {
            Text = "크기", FontSize = GameArt.FontBody, Foreground = GameArt.Brush(GameArt.SubText),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        _size.Minimum = AppSettings.MinSize;
        _size.Maximum = AppSettings.MaxSize;
        _size.SmallChange = 1;
        _size.LargeChange = 4;
        _size.TickFrequency = 1;
        _size.IsSnapToTickEnabled = true;
        _size.VerticalAlignment = VerticalAlignment.Center;
        _size.ValueChanged += (_, _) =>
        {
            int value = (int)Math.Round(_size.Value);
            _sizeLabel.Text = $"{value} px";
            if (!_binding) RunnerSizeChanged?.Invoke(this, value);
        };
        Grid.SetColumn(_size, 1);
        sizeRow.Children.Add(_size);
        _sizeLabel.FontSize = GameArt.FontBody;
        _sizeLabel.Foreground = GameArt.Brush(GameArt.Text);
        _sizeLabel.VerticalAlignment = VerticalAlignment.Center;
        _sizeLabel.MinWidth = 44;
        _sizeLabel.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(_sizeLabel, 2);
        sizeRow.Children.Add(_sizeLabel);
        Grid.SetRow(sizeRow, 3);
        Grid.SetColumnSpan(sizeRow, 2);
        layout.Children.Add(sizeRow);

        // 카드 안 어디를 눌러도(체크박스·슬라이더 포함) 이 고양이를 고른다.
        AddHandler(PointerPressedEvent, (_, _) => Picked?.Invoke(this, EventArgs.Empty), RoutingStrategies.Tunnel, handledEventsToo: true);
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
            _title.Text = SettingsWindow.RunnerName(r);
            _colorName.Text = trialHex is not null ? "입어 보는 중이에요 · 아직 사지 않았어요"
                : r.Enabled ? $"{colorName} · {SettingsWindow.MetricName(r.Metric)} 사용률로 달려요"
                : $"{colorName} · 숨겨 둠";
            _colorName.Foreground = GameArt.Brush(trialHex is not null ? GameArt.Accent : GameArt.SubText);
            _visible.IsChecked = r.Enabled;
            _size.Value = Math.Clamp(r.Size, AppSettings.MinSize, AppSettings.MaxSize);
            _sizeLabel.Text = $"{(int)_size.Value} px";
            _preview.Sprite = SpriteRegistry.Get(r.SpriteId);
            _preview.RunnerColor = ColorUtil.Parse(trialHex ?? r.Color, Colors.Black);
            _preview.RunnerSize = r.Size;
        }
        finally
        {
            _binding = false;
        }
    }
}
