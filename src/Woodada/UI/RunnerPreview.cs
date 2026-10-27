using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Woodada.Core;
using Woodada.Rendering;
using Woodada.Settings;
using Woodada.Sprites;

namespace Woodada.UI;

/// <summary>설정 화면의 미리보기. 오버레이와 같은 러너·스킨 코드로 왕복 달리기를 보여준다.</summary>
internal sealed class RunnerPreview : Control
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly RunnerInstance _runner = new(new RunnerConfig(), SpriteRegistry.Get("cat"));
    private readonly List<RunnerFrame> _frames = new();
    private double _last;

    public RunnerPreview()
    {
        _timer.Tick += (_, _) => Step();
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public IRunnerSprite Sprite
    {
        get => _runner.Sprite;
        set => _runner.Sprite = value;
    }

    public Color RunnerColor { get; set; } = Colors.Orange;
    public RunnerSkin Skin { get; set; }
    /// <summary>오버레이와 같은 화면 단위 크기. 논리 단위로 바꿔 그려서 실제 고양이와 같은 크기로 보인다.</summary>
    public int RunnerSize { get; set; } = 28;
    public Func<double>? UsageSource { get; set; }

    /// <summary>화면 단위 → 논리 단위. Windows는 창이 놓인 모니터의 배율, macOS는 1.</summary>
    private double UnitsPerDip => TopLevel.GetTopLevel(this) is WindowBase { DesktopScaling: > 0 } top ? top.DesktopScaling : 1;

    private double BarHeight => Math.Min(Bounds.Height / 3, 36);

    private void Step()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _last, 0.25);
        _last = now;

        _runner.HeightOverride = RunnerSize / UnitsPerDip;
        _runner.Config.Color = ColorUtil.ToHex(RunnerColor);
        _runner.Config.Skin = RunnerSkins.ToId(Skin);
        // 사용률은 RunnerInstance가 오버레이와 같은 시정수로 부드럽게 따라간다.
        _runner.Update(dt, UsageSource?.Invoke() ?? 0, new Runway(8, Math.Max(8, (int)Bounds.Width - 8), 0, HangBelow: false));
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var p = UI.Theme.Current;
        var all = new Rect(Bounds.Size);
        // 몸이 바탕과 비슷하면(밝은 테마의 하양 고양이·유령, 어두운 테마의 까망 고양이) 대비가 나는 바탕을 깐다.
        var look = RunnerSkins.LookColor(Skin, RunnerColor);
        bool alt = GameArt.LowContrast(look, p.PreviewBack);
        ctx.FillRectangle(GameArt.Brush(alt ? p.PreviewAltBack : p.PreviewBack), all);

        // 작업표시줄·독 흉내: 아래쪽에 띠를 깔고 그 윗선 위를 달린다.
        var bar = new Rect(0, Bounds.Height - BarHeight, Bounds.Width, BarHeight);
        ctx.FillRectangle(GameArt.Brush(alt ? p.PreviewAltBar : p.PreviewBar), bar);
        ctx.DrawLine(new Pen(GameArt.Brush(alt ? p.PreviewAltLine : p.PreviewLine)), new Point(0, bar.Y + 0.5), new Point(Bounds.Width, bar.Y + 0.5));

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        _frames.Clear();
        SkinPainter.AddRunner(_frames, _runner, 0, bar.Y + 1, _clock.Elapsed.TotalSeconds, lightBackground: !alt);
        SkinPainter.Draw(ctx, _frames, scaling);

        var text = GameArt.Format($"{_runner.Usage:0}%  ·  {_runner.State.Speed * UnitsPerDip:0} px/s", GameArt.FontNote, alt ? p.PreviewAltText : p.PreviewText);
        ctx.DrawText(text, new Point(8, bar.Y + 6));
    }
}
