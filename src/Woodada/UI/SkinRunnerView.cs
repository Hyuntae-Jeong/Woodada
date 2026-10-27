using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Woodada.Rewards;
using Woodada.Sprites;

namespace Woodada.UI;

/// <summary>
/// 어두운 작은 무대에서 스킨 하나가 제자리 달리기를 한다(도감 칸). 아직 없는 스킨은 회색 실루엣과 물음표.
/// Teaser(일반 버전 예고)면 마우스를 올렸을 때 정체는 숨긴 채 검은 실루엣이 등급 색으로 빛나며 달린다.
/// 스킨 칸 그림도 같은 모양을 쓰도록 그리는 부분은 정적 메서드로 뺐다.
/// </summary>
internal sealed class SkinRunnerView : Control
{
    private static readonly IImmutableBrush StageBack = new ImmutableSolidColorBrush(Color.FromRgb(0x18, 0x20, 0x2E));
    private static readonly IImmutableBrush Floor = new ImmutableSolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x46));
    private static readonly Color MysteryBody = Color.FromRgb(0x3A, 0x44, 0x56);
    private static readonly FormattedText[] QuestionMarks = new FormattedText[40];

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly TreadmillRunner _runner = new();

    public SkinRunnerView()
    {
        _timer.Tick += (_, _) =>
        {
            if (IsEffectivelyVisible && (Owned || _hot)) InvalidateVisual();
        };
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public RunnerSkin Skin { get; set; }

    public bool Owned { get; set; }

    public bool Teaser { get; set; }

    private bool _hot;

    protected override void OnPointerEntered(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hot = Teaser;
        InvalidateVisual();
    }

    protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hot = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var box = new Rect(Bounds.Size);
        if (box.Width <= 0 || box.Height <= 0) return;
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        DrawStage(ctx, box, 7);
        double floor = box.Bottom - 12, height = Math.Min(34, box.Height * 0.6);
        using (ctx.PushClip(box))
        {
            if (Owned) _runner.Draw(ctx, box.Center.X, floor, height, Skin, RewardCatalog.ShowcaseColor(Skin), 30, scaling);
            else if (_hot && RewardCatalog.FindSkin(Skin) is { } info) DrawTeaserRunner(ctx, box.Center.X, floor, height, info.Accent, scaling);
            else DrawMystery(ctx, box.Center.X, floor, height, scaling);
        }
    }

    /// <summary>바탕과 그 위의 짧은 바닥 줄. 보통은 어둡고, 까망 고양이처럼 어두운 몸이면 light로 밝게 깐다.</summary>
    public static void DrawStage(DrawingContext ctx, Rect box, double radius, bool light = false, double floorInset = 12)
    {
        ctx.DrawRectangle(light ? LightBack : StageBack, null, box, radius, radius);
        double w = Math.Min(100, box.Width * 0.76);
        ctx.FillRectangle(light ? LightFloor : Floor, new Rect(box.Center.X - w / 2, box.Bottom - floorInset, w, 3));
    }

    // 테마 색을 따르되, 매 프레임 붓을 새로 만들지 않게 색이 바뀔 때만 다시 만든다.
    private static ImmutableSolidColorBrush? _lightBack;

    private static IImmutableBrush LightBack
    {
        get
        {
            var color = UI.Theme.Current.SwatchBack;
            if (_lightBack is null || _lightBack.Color != color) _lightBack = new ImmutableSolidColorBrush(color);
            return _lightBack;
        }
    }
    private static readonly IImmutableBrush LightFloor = new ImmutableSolidColorBrush(Color.FromRgb(0xD5, 0xDA, 0xE1));

    private static readonly Color Shadow = Color.FromRgb(0x0B, 0x0F, 0x16);

    /// <summary>예고: 검은 실루엣이 등급 색 테두리로 두근두근 빛나며 제자리에서 달린다.</summary>
    private static void DrawTeaserRunner(DrawingContext ctx, double centerX, double floorY, double height, Color accent, double scaling)
    {
        var sprite = SpriteRegistry.Get("cat");
        var size = sprite.Measure(height);
        var box = new Rect(centerX - size.Width / 2, floorY - size.Height, size.Width, size.Height);
        double seconds = Environment.TickCount64 / 1000.0;
        var pose = new RunnerPose(1, seconds * 2.4 % 1, 0.6);
        double glow = 0.75 + 0.25 * Math.Sin(seconds * 8);
        foreach (var (dx, dy) in new[] { (1.5, 0.0), (-1.5, 0.0), (0.0, 1.5), (0.0, -1.5) })
            sprite.DrawSilhouette(ctx, box.Translate(new Vector(dx, dy)), pose, accent, glow, scaling);
        sprite.DrawSilhouette(ctx, box, pose, Shadow, 1, scaling);
    }

    /// <summary>아직 없는 스킨: 회색 실루엣 위에 물음표.</summary>
    public static void DrawMystery(DrawingContext ctx, double centerX, double floorY, double height, double scaling)
    {
        var sprite = SpriteRegistry.Get("cat");
        var size = sprite.Measure(height);
        sprite.DrawSilhouette(ctx, new Rect(centerX - size.Width / 2, floorY - size.Height, size.Width, size.Height),
            new RunnerPose(1, 0.4, 0.5), MysteryBody, 1, scaling);
        int fontSize = Math.Clamp((int)Math.Round(height * 0.47), 8, QuestionMarks.Length - 1);
        var q = QuestionMarks[fontSize] ??= GameArt.Format("?", fontSize, Color.FromArgb(191, 255, 255, 255), FontWeight.Bold);
        ctx.DrawText(q, new Point(centerX + 2 - q.Width / 2, floorY - height * 0.55 - q.Height / 2));
    }
}

/// <summary>생선 아이콘 + 가격. 살 수 없으면 흐리게 그린다.</summary>
internal sealed class PriceLabel : Control
{
    private const double LineHeight = 14;
    private int _price;
    private bool _affordable = true;

    public int Price
    {
        get => _price;
        set
        {
            if (_price == value) return;
            _price = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public bool Affordable
    {
        get => _affordable;
        set
        {
            if (_affordable == value) return;
            _affordable = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        GameArt.MeasurePrice(GameArt.FormatCount(_price), GameArt.FontBody, FontWeight.Bold, LineHeight);

    public override void Render(DrawingContext ctx)
    {
        GameArt.DrawPrice(ctx, GameArt.FormatCount(_price), GameArt.FontBody, FontWeight.Bold,
            new Point(0, (Bounds.Height - LineHeight) / 2), LineHeight,
            _affordable ? GameArt.FishDark : GameArt.SubText, _affordable ? GameArt.Fish : GameArt.FishMuted);
    }
}
