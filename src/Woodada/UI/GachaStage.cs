using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Woodada.Rewards;
using Woodada.Sprites;

namespace Woodada.UI;

/// <summary>
/// 뽑기 무대. 바탕화면과 작업표시줄을 흉내 낸 어두운 상자다.
/// 뽑기 전: "누가 달려올까요?" · 뽑는 중: CPU가 치솟으며 검은 실루엣이 달려와 멈추고, 등급 색으로 빛나다 번쩍 ·
/// 뽑은 뒤: 그 스킨이 작업표시줄 위에서 제자리 달리기를 한다. 뽑는 중에 누르면 건너뛴다.
/// 높이 300짜리 시안 좌표로 그리고 실제 높이에 맞춰 줄인다.
/// </summary>
internal sealed class GachaStage : Control
{
    private enum Mode { Idle, Playing, Showing }

    private const double DesignHeight = 300;
    /// <summary>작업표시줄 윗선(시안 좌표).</summary>
    private const double BarTop = 256;
    private const double PlaySeconds = 3.0;
    private const double RunnerHeight = 54;
    private const double ShowcaseHeight = 92;
    /// <summary>결과 고양이가 달리는 사용률. 효과가 다 보이되 플래시 몸이 달아오르기(50%) 전이다.</summary>
    private const double ShowcaseUsage = 45;

    private static readonly IImmutableBrush DeskBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromRgb(0x1A, 0x25, 0x37), 0), new GradientStop(Color.FromRgb(0x2A, 0x3D, 0x63), 1) },
    }.ToImmutable();
    private static readonly IImmutableBrush GoldGlow = Glow(Color.FromRgb(0xFF, 0xD6, 0x66), 0.28);
    private static readonly IImmutableBrush BarBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly IImmutableBrush BarLine = new ImmutableSolidColorBrush(Colors.White, 0.08);
    private static readonly IImmutableBrush IconBrush = new ImmutableSolidColorBrush(Colors.White, 0.17);
    private static readonly IImmutableBrush MeterBack = new ImmutableSolidColorBrush(Colors.Black, 0.35);
    private static readonly IImmutableBrush MeterTrack = new ImmutableSolidColorBrush(Colors.White, 0.15);
    private static readonly IImmutableBrush MeterFill = new ImmutableSolidColorBrush(Color.FromRgb(0x5C, 0x9B, 0xD5));
    private static readonly IImmutableBrush MeterHot = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x59));
    private static readonly IImmutableBrush Black = new ImmutableSolidColorBrush(Colors.Black);
    private static readonly IImmutableBrush White = new ImmutableSolidColorBrush(Colors.White);
    private static readonly IImmutableBrush Dust = new ImmutableSolidColorBrush(Color.FromRgb(200, 210, 225));
    private static readonly IImmutableBrush GoldBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x66));
    private static readonly Color Shadow = Color.FromRgb(0x0B, 0x0F, 0x16);

    /// <summary>결과 고양이 뒤에서 천천히 도는 빛줄기 12개.</summary>
    private static readonly Geometry Rays = BuildRays();
    private static readonly Geometry BubbleTail = BuildTail();

    // 붓·글자를 매 프레임 새로 만들지 않는다(오버레이의 플래시 불꽃 줄기와 같은 이유).
    private readonly Dictionary<RunnerSkin, (IImmutableBrush Accent, IImmutableBrush Glow, IImmutableBrush Ray)> _skinBrushes = new();
    private readonly Dictionary<(string, double, uint, FontWeight), FormattedText> _texts = new();

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TreadmillRunner _showcase = new();
    private readonly TreadmillRunner[] _parade = RewardCatalog.Skins.Select(_ => new TreadmillRunner()).ToArray();
    private readonly List<Particle> _particles = new();
    private Mode _mode = Mode.Idle;
    private RunnerSkin _result;
    private double _start;
    private double _lastTick;
    private double _runPhase;
    private double _designWidth = 640;

    public GachaStage()
    {
        _timer.Tick += (_, _) => Tick();
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    /// <summary>스킨을 다 모았으면 뽑기 전 화면이 "도감 완성!"이 된다.</summary>
    public bool Complete { get; set; }

    public bool IsPlaying => _mode == Mode.Playing;

    /// <summary>연출이 끝났거나 건너뛰어 결과가 보이기 시작할 때.</summary>
    public event EventHandler? Finished;

    private double Now => _clock.Elapsed.TotalSeconds;

    public void Play(RunnerSkin result)
    {
        _result = result;
        _mode = Mode.Playing;
        _start = Now;
        _runPhase = 0;
        _particles.Clear();
        Cursor = new Cursor(StandardCursorType.Hand);
        InvalidateVisual();
    }

    public void ShowIdle()
    {
        _mode = Mode.Idle;
        _start = Now;
        Cursor = Cursor.Default;
        InvalidateVisual();
    }

    private void Finish()
    {
        if (_mode != Mode.Playing) return;
        _mode = Mode.Showing;
        _start = Now;
        Cursor = Cursor.Default;
        if (RewardCatalog.FindSkin(_result) is { } info) Burst(_designWidth / 2, 200, info.Accent);
        Finished?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == Mode.Playing) Finish();
    }

    private void Tick()
    {
        double now = Now;
        double dt = Math.Clamp(now - _lastTick, 0, 0.05);
        _lastTick = now;
        if (!IsEffectivelyVisible) return;

        if (_mode == Mode.Playing)
        {
            double t = now - _start;
            // 달려오면서 점점 느려진다. 다리도 그만큼 천천히 움직이다 멈춘다.
            double p = Seg(t, 0, 2.0);
            _runPhase += dt * (p < 1 ? 5 + 20 * (1 - p) : 0);
            if (t >= PlaySeconds) Finish();
        }
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var q = _particles[i];
            q.Life += dt;
            if (q.Life >= q.MaxLife) { _particles.RemoveAt(i); continue; }
            q.Vy += 320 * dt;
            q.X += q.Vx * dt;
            q.Y += q.Vy * dt;
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        double s = h / DesignHeight;
        double width = w / s;
        _designWidth = width;
        // 스프라이트는 장치 픽셀 크기로 그림을 만들어 두므로, 줄인 만큼을 배율에 곱해 넘겨야 흐리지 않다.
        double scaling = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * s;
        double t = Now - _start;

        using (ctx.PushClip(new Rect(Bounds.Size)))
        using (ctx.PushTransform(Matrix.CreateScale(s, s)))
        {
            switch (_mode)
            {
                case Mode.Idle:
                    DrawIdle(ctx, width, t, scaling);
                    break;
                case Mode.Playing:
                    DrawPlaying(ctx, width, t, scaling);
                    break;
                default:
                    DrawShowing(ctx, width, t, scaling);
                    break;
            }
            DrawParticles(ctx);
        }
    }

    // ───────────────────────── 장면 ─────────────────────────

    private void DrawIdle(DrawingContext ctx, double width, double t, double scaling)
    {
        if (!Complete)
        {
            DrawDesk(ctx, width, 12 + 3 * Math.Sin(t * 1.3));
            TextAt(ctx, "누가 달려올까요?", 22, Color.FromArgb(191, 255, 255, 255), FontWeight.Bold, width / 2, 150);
            TextAt(ctx, "?", 54, Color.FromArgb(46, 255, 255, 255), FontWeight.Bold, width / 2, 208 + 4 * Math.Sin(t * 2));
            return;
        }
        // 다 모았으면 모든 스킨이 나란히 달린다. 많아지면 간격을 좁혀 무대 안에 넣는다.
        DrawDesk(ctx, width, null);
        ctx.DrawEllipse(GoldGlow, null, new Point(width / 2, 230), 220, 220);
        DrawTwinkles(ctx, width, t, GoldBrush);
        int count = _parade.Length;
        double spacing = count > 1 ? Math.Min(90, (width - 120) / (count - 1)) : 0;
        for (int i = 0; i < count; i++)
        {
            var skin = RewardCatalog.Skins[i].Skin;
            _parade[i].Draw(ctx, width / 2 + (i - (count - 1) / 2.0) * spacing, BarTop, 46, skin, RewardCatalog.ShowcaseColor(skin), 35, scaling);
        }
        TextAt(ctx, "도감 완성!", 24, Colors.White, FontWeight.Bold, width / 2, 54);
        TextAt(ctx, "새 스킨이 나오면 뽑기가 다시 열려요", 13, Color.FromArgb(166, 255, 255, 255), FontWeight.Medium, width / 2, 84);
    }

    private void DrawPlaying(DrawingContext ctx, double width, double t, double scaling)
    {
        var accent = RewardCatalog.FindSkin(_result)?.Accent ?? Colors.White;
        DrawDesk(ctx, width, 12 + 88 * InOut(Seg(t, 0.1, 1.5)));

        double dim = Seg(t, 2.0, 2.4);
        if (dim > 0)
            using (ctx.PushOpacity(0.35 * dim))
                ctx.FillRectangle(Black, new Rect(0, 0, width, BarTop));

        // 왼쪽 밖에서 달려와 가운데에서 멈춘다.
        double p = Seg(t, 0, 2.0);
        double cx = Lerp(-50, width / 2, Out(p));
        var sprite = SpriteRegistry.Get("cat");
        var size = sprite.Measure(RunnerHeight);
        var box = new Rect(cx - size.Width / 2, BarTop - size.Height, size.Width, size.Height);
        var pose = new RunnerPose(1, _runPhase / 5 % 1, 0.8);

        if (p is > 0.6 and < 1)
        {
            // 멈추면서 이는 먼지
            for (int i = 0; i < 3; i++)
            {
                double a = (1 - p) * 2.5 * Hash(Math.Floor(t * 30) + i);
                double r = 3 + i * 1.5;
                using (ctx.PushOpacity(Math.Clamp(0.35 * a, 0, 1)))
                    ctx.DrawEllipse(Dust, null, new Point(box.X - 6 - i * 9, BarTop - 4 - i * 2), r, r);
            }
        }

        // 멈춘 뒤 등급 색으로 두근두근 빛난다.
        double pulse = Seg(t, 2.2, 2.6) * (0.55 + 0.45 * Math.Sin(t * 14));
        if (pulse > 0)
        {
            foreach (var (dx, dy) in Ring(2.5))
                sprite.DrawSilhouette(ctx, box.Translate(new Vector(dx, dy)), pose, accent, pulse, scaling);
        }
        // 어두운 바탕에서도 모양이 보이게 옅은 테두리를 두른 검은 실루엣.
        foreach (var (dx, dy) in Ring(1.2))
            sprite.DrawSilhouette(ctx, box.Translate(new Vector(dx, dy)), pose, Colors.White, 0.22, scaling);
        sprite.DrawSilhouette(ctx, box, pose, Shadow, 1, scaling);

        double b = Seg(t, 2.0, 2.3);
        if (b > 0)
        {
            double k = Back(b);
            using (ctx.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(cx + 6, BarTop - size.Height - 26)))
            {
                ctx.DrawEllipse(White, null, new Point(0, 0), 16, 16);
                ctx.DrawGeometry(White, null, BubbleTail);
                TextAt(ctx, "?", 20, Color.FromRgb(0x20, 0x25, 0x30), FontWeight.Bold, 0, 1);
            }
        }

        double flash = Seg(t, 2.75, 3.0);
        if (flash > 0)
            using (ctx.PushOpacity(flash))
                ctx.FillRectangle(White, new Rect(0, 0, width, DesignHeight));
    }

    private void DrawShowing(DrawingContext ctx, double width, double t, double scaling)
    {
        if (RewardCatalog.FindSkin(_result) is not { } info) return;
        var brushes = BrushesFor(info);
        DrawDesk(ctx, width, null);
        using (ctx.PushOpacity(0.35))
            ctx.FillRectangle(Black, new Rect(0, 0, width, BarTop));

        ctx.DrawEllipse(brushes.Glow, null, new Point(width / 2, 230), 170, 170);
        using (ctx.PushTransform(Matrix.CreateRotation(t * 0.25) * Matrix.CreateTranslation(width / 2, 230)))
            ctx.DrawGeometry(brushes.Ray, null, Rays);
        DrawTwinkles(ctx, width, t, brushes.Accent);

        // 발밑을 축으로 톡 튀어나오듯 커진다.
        double k = Back(Seg(t, 0.05, 0.5));
        if (k > 0.01)
        {
            var pop = Matrix.CreateTranslation(-width / 2, -BarTop) * Matrix.CreateScale(k, k) * Matrix.CreateTranslation(width / 2, BarTop);
            using (ctx.PushTransform(pop))
                _showcase.Draw(ctx, width / 2, BarTop, ShowcaseHeight, info.Skin, RewardCatalog.ShowcaseColor(info.Skin), ShowcaseUsage, scaling);
        }

        double ta = Seg(t, 0.25, 0.6);
        if (ta > 0)
        {
            using (ctx.PushOpacity(ta))
            {
                TextAt(ctx, new string('★', info.Stars) + new string('☆', 4 - info.Stars), 15, info.Accent, FontWeight.Bold, width / 2, 30);
                TextAt(ctx, info.Name, 24, Colors.White, FontWeight.Bold, width / 2, 58 - 6 * (1 - ta));
                TextAt(ctx, $"{info.Tier} · NEW", 12, Color.FromArgb(179, 255, 255, 255), FontWeight.Medium, width / 2, 84);
            }
        }

        double flash = 1 - Seg(t, 0, 0.35);
        if (flash > 0)
            using (ctx.PushOpacity(flash))
                ctx.FillRectangle(White, new Rect(0, 0, width, DesignHeight));
    }

    /// <summary>바탕화면 + 작업표시줄. cpu가 있으면 왼쪽 위에 CPU 막대를 띄운다.</summary>
    private void DrawDesk(DrawingContext ctx, double width, double? cpu)
    {
        ctx.FillRectangle(DeskBrush, new Rect(0, 0, width, DesignHeight));
        ctx.FillRectangle(BarBrush, new Rect(0, BarTop, width, DesignHeight - BarTop));
        ctx.FillRectangle(BarLine, new Rect(0, BarTop, width, 1));
        const int icons = 6;
        const double icon = 22, gap = 10;
        double start = width / 2 - (icons * icon + (icons - 1) * gap) / 2;
        for (int i = 0; i < icons; i++)
            ctx.DrawRectangle(IconBrush, null, new Rect(start + i * (icon + gap), BarTop + 11, icon, icon), 5, 5);

        if (cpu is not { } c) return;
        ctx.DrawRectangle(MeterBack, null, new Rect(14, 14, 120, 36), 8, 8);
        var label = Color.FromArgb(204, 255, 255, 255);
        var cpuText = Text("CPU", 11, label, FontWeight.Bold);
        ctx.DrawText(cpuText, new Point(24, 26 - cpuText.Height / 2));
        var pct = Text($"{Math.Round(c):0}%", 11, label, FontWeight.Bold);
        ctx.DrawText(pct, new Point(124 - pct.Width, 26 - pct.Height / 2));
        ctx.DrawRectangle(MeterTrack, null, new Rect(24, 36, 100, 6), 3, 3);
        ctx.DrawRectangle(c > 80 ? MeterHot : MeterFill, null, new Rect(24, 36, Math.Max(6, c), 6), 3, 3);
    }

    private static void DrawTwinkles(DrawingContext ctx, double width, double t, IImmutableBrush accentBrush)
    {
        for (int i = 0; i < 14; i++)
        {
            double k = Math.Max(0, Math.Sin(t * 2.4 + i * 1.7));
            if (k <= 0) continue;
            double x = Hash(i + 3) * width, y = Hash(i + 33) * DesignHeight * 0.7 + 10, s = 1.5 + 2.5 * k;
            bool white = i % 3 == 0;
            using (ctx.PushOpacity((white ? 0.8 : 0.7) * k))
            {
                var brush = white ? White : accentBrush;
                ctx.FillRectangle(brush, new Rect(x - s / 2, y - 0.6, s, 1.2));
                ctx.FillRectangle(brush, new Rect(x - 0.6, y - s / 2, 1.2, s));
            }
        }
    }

    // ───────────────────────── 파티클 ─────────────────────────

    private sealed class Particle
    {
        public double X, Y, Vx, Vy, Life, MaxLife, Size;
        public bool White;
    }

    private RunnerSkin _particleSkin;

    private void Burst(double x, double y, Color color)
    {
        _particleSkin = _result;
        for (int i = 0; i < 50; i++)
        {
            double angle = Random.Shared.NextDouble() * Math.PI * 2;
            double speed = 260 * (0.3 + Random.Shared.NextDouble() * 0.9);
            _particles.Add(new Particle
            {
                X = x, Y = y, Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed - 60,
                MaxLife = 0.6 + Random.Shared.NextDouble() * 0.7, Size = 1.5 + Random.Shared.NextDouble() * 3,
                White = Random.Shared.NextDouble() < 0.3,
            });
        }
    }

    private void DrawParticles(DrawingContext ctx)
    {
        if (_particles.Count == 0) return;
        var accent = RewardCatalog.FindSkin(_particleSkin) is { } info ? BrushesFor(info).Accent : White;
        foreach (var q in _particles)
        {
            using (ctx.PushOpacity(1 - q.Life / q.MaxLife))
                ctx.FillRectangle(q.White ? White : accent, new Rect(q.X - q.Size / 2, q.Y - q.Size / 2, q.Size, q.Size));
        }
    }

    // ───────────────────────── 도우미 ─────────────────────────

    private (IImmutableBrush Accent, IImmutableBrush Glow, IImmutableBrush Ray) BrushesFor(SkinInfo info)
    {
        if (_skinBrushes.TryGetValue(info.Skin, out var b)) return b;
        b = (new ImmutableSolidColorBrush(info.Accent), Glow(info.Accent, 0.45), new ImmutableSolidColorBrush(info.Accent, 0.05));
        _skinBrushes[info.Skin] = b;
        return b;
    }

    private static IImmutableBrush Glow(Color color, double alpha) => new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.FromArgb((byte)(255 * alpha), color.R, color.G, color.B), 0),
            new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
        },
    }.ToImmutable();

    private FormattedText Text(string text, double size, Color color, FontWeight weight)
    {
        var key = (text, size, color.ToUInt32(), weight);
        if (_texts.TryGetValue(key, out var ft)) return ft;
        if (_texts.Count > 64) _texts.Clear();
        ft = GameArt.Format(text, size, color, weight);
        _texts[key] = ft;
        return ft;
    }

    /// <summary>글자 가운데를 (x, y)에 맞춘다.</summary>
    private void TextAt(DrawingContext ctx, string text, double size, Color color, FontWeight weight, double x, double y)
    {
        var ft = Text(text, size, color, weight);
        ctx.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
    }

    private static IEnumerable<(double, double)> Ring(double d)
    {
        yield return (d, 0);
        yield return (-d, 0);
        yield return (0, d);
        yield return (0, -d);
    }

    private static Geometry BuildRays()
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI / 6;
            Point Rot(double x, double y) => new(x * Math.Cos(a) - y * Math.Sin(a), x * Math.Sin(a) + y * Math.Cos(a));
            c.BeginFigure(new Point(0, 0), true);
            c.LineTo(Rot(-26, -420));
            c.LineTo(Rot(26, -420));
            c.EndFigure(true);
        }
        return g;
    }

    private static Geometry BuildTail()
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(new Point(-5, 12), true);
        c.LineTo(new Point(-10, 22));
        c.LineTo(new Point(4, 14));
        c.EndFigure(true);
        return g;
    }

    private static double Seg(double t, double a, double b) => Math.Clamp((t - a) / (b - a), 0, 1);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double Out(double t) => 1 - Math.Pow(1 - t, 3);
    private static double InOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    private static double Back(double t)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    /// <summary>같은 n이면 늘 같은 0~1 값. 반짝이 자리처럼 프레임마다 흔들리면 안 되는 무작위에 쓴다.</summary>
    private static double Hash(double n)
    {
        double x = Math.Sin(n * 127.1 + 311.7) * 43758.5453;
        return x - Math.Floor(x);
    }
}
