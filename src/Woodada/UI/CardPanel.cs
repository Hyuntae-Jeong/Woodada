using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Woodada.UI;

/// <summary>둥근 흰 카드. Highlighted면 테두리를 강조색으로 굵게 그린다.</summary>
internal class CardPanel : Border
{
    public CardPanel()
    {
        Background = GameArt.Brush(GameArt.Card);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(12, 10);
        Highlighted = false;
    }

    public bool Highlighted
    {
        set
        {
            BorderBrush = GameArt.Brush(value ? GameArt.Accent : GameArt.CardBorder);
            BorderThickness = new Thickness(value ? 2.5 : 1);
        }
    }
}

/// <summary>강조색 점선 테두리 카드. 아직 열리지 않은 칸(곧 만나요)에 쓴다. 옷장의 COMING SOON 칸과 같은 테두리다.</summary>
internal sealed class DashedCard : Decorator
{
    public DashedCard()
    {
        Padding = new Thickness(6);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size).Deflate(0.7);
        var dash = new Pen(GameArt.Brush(GameArt.Accent), 1.4, new DashStyle(new double[] { 4, 3 }, 0));
        ctx.DrawRectangle(GameArt.Brush(GameArt.Card), dash, r, 10, 10);
    }
}

/// <summary>
/// 직접 그리는 칸의 공통 바탕: 마우스를 올렸는지 알고, 눌렀다 뗀 버튼으로 Click·RightClick을 낸다.
/// 투명한 곳도 눌리도록 전체를 투명하게 한 번 칠한다.
/// </summary>
internal abstract class ClickableControl : Control
{
    public event EventHandler? Click;
    public event EventHandler? RightClick;

    protected bool Hover { get; private set; }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        Hover = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Hover = false;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
        if (e.InitialPressMouseButton == MouseButton.Left) Click?.Invoke(this, EventArgs.Empty);
        else if (e.InitialPressMouseButton == MouseButton.Right) RightClick?.Invoke(this, EventArgs.Empty);
    }

    public sealed override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        Paint(ctx);
    }

    protected abstract void Paint(DrawingContext ctx);

    protected double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
}

/// <summary>
/// 게임풍 둥근 버튼. 기본 테마 버튼은 마우스를 올리면 회색으로 바뀌어 강조색 버튼과 어울리지 않아 직접 그린다.
/// 끄면(IsEnabled = false) 잠긴 칸 색으로 바뀌고 눌리지 않는다.
/// </summary>
internal sealed class GameButton : ClickableControl
{
    private string _text;

    public GameButton(string text, bool primary = false)
    {
        _text = text;
        Primary = primary;
        Cursor = new Cursor(StandardCursorType.Hand);
        Height = 28;
        MinWidth = 64;
    }

    public bool Primary { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) InvalidateVisual();
    }

    private FormattedText Label(Color color) =>
        GameArt.Format(_text, GameArt.FontBody, color, Primary ? FontWeight.Bold : FontWeight.Normal);

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Max(MinWidth, Label(GameArt.Text).Width + 24), Height);

    protected override void Paint(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size).Deflate(0.5);
        bool enabled = IsEffectivelyEnabled;
        Color fill, border, text;
        if (!enabled)
        {
            fill = GameArt.Locked;
            border = GameArt.CardBorder;
            text = GameArt.SubText;
        }
        else if (Primary)
        {
            var a = GameArt.Accent;
            fill = Hover ? Color.FromRgb((byte)(a.R * 0.85), (byte)(a.G * 0.85), (byte)(a.B * 0.85)) : a;
            border = fill;
            text = GameArt.OnAccent;
        }
        else
        {
            fill = Hover ? GameArt.AccentSoft : GameArt.Card;
            border = GameArt.CardBorder;
            text = GameArt.Text;
        }
        GameArt.FillRound(ctx, r, 6, fill, border);
        GameArt.DrawCentered(ctx, Label(text), r);
    }
}

/// <summary>위쪽 탭 대신 쓰는 알약 모양 버튼. Locked면 글자 옆에 작은 자물쇠를 붙인다(아직 열리지 않은 탭).</summary>
internal sealed class NavPill : ClickableControl
{
    private readonly string _text;
    private bool _selected;

    public bool Locked { get; init; }

    public NavPill(string text)
    {
        _text = text;
        Cursor = new Cursor(StandardCursorType.Hand);
        Width = 96;
        Height = 32;
        Margin = new Thickness(0, 0, 6, 0);
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            InvalidateVisual();
        }
    }

    protected override void Paint(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size).Deflate(0.5);
        if (_selected) GameArt.FillRound(ctx, r, r.Height / 2, GameArt.Accent);
        else if (Hover) GameArt.FillRound(ctx, r, r.Height / 2, GameArt.AccentSoft);
        var label = GameArt.Format(_text, GameArt.FontLarge, _selected ? GameArt.OnAccent : GameArt.Text, FontWeight.Bold);
        if (!Locked)
        {
            GameArt.DrawCentered(ctx, label, r);
            return;
        }
        // 글자 + 자물쇠를 한 덩어리로 가운데에 놓는다.
        const double lockW = 8, lockH = 10, gap = 5;
        double x = r.X + (r.Width - label.Width - gap - lockW) / 2;
        ctx.DrawText(label, new Point(x, r.Y + (r.Height - label.Height) / 2));
        GameArt.DrawLock(ctx, new Rect(x + label.Width + gap, r.Y + (r.Height - lockH) / 2, lockW, lockH),
            _selected ? GameArt.OnAccent : GameArt.SubText);
    }
}
