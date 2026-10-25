using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Woodada.Core;
using Woodada.Sprites;

namespace Woodada.UI;

/// <summary>설정 화면의 미리보기. 오버레이와 같은 RunnerState·스프라이트 코드로 왕복 달리기를 보여준다.</summary>
internal sealed class RunnerPreview : Control
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly RunnerState _state = new();
    private double _last;
    private double _usage;

    public RunnerPreview()
    {
        _timer.Tick += (_, _) => Step();
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public IRunnerSprite Sprite { get; set; } = SpriteRegistry.Get("cat");
    public Color RunnerColor { get; set; } = Colors.Orange;
    /// <summary>오버레이와 같은 화면 단위 크기. 논리 단위로 바꿔 그려서 실제 고양이와 같은 크기로 보인다.</summary>
    public int RunnerSize { get; set; } = 28;
    public Func<double>? UsageSource { get; set; }

    /// <summary>화면 단위 → 논리 단위. Windows는 창이 놓인 모니터의 배율, macOS는 1.</summary>
    private double UnitsPerDip => TopLevel.GetTopLevel(this) is WindowBase { DesktopScaling: > 0 } top ? top.DesktopScaling : 1;

    private void Step()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _last, 0.25);
        _last = now;

        double raw = UsageSource?.Invoke() ?? 0;
        _usage += (raw - _usage) * Math.Min(1, dt / 0.6);

        var size = Sprite.Measure(RunnerSize / UnitsPerDip);
        var track = new TrackBounds(8, Bounds.Width - 8 - size.Width);
        if (_state.X < track.MinX || _state.X > track.MaxX) _state.X = Math.Max(track.MinX, 0);
        _state.Advance(dt, _usage, track, SpeedProfile.ForBodyWidth(size.Width));
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var p = UI.Theme.Current;
        var all = new Rect(Bounds.Size);
        // 고양이 색이 바탕과 비슷하면(밝은 테마의 하양 고양이, 어두운 테마의 까망 고양이) 대비가 나는 바탕을 깐다.
        bool alt = GameArt.LowContrast(RunnerColor, p.PreviewBack);
        ctx.FillRectangle(GameArt.Brush(alt ? p.PreviewAltBack : p.PreviewBack), all);

        // 작업표시줄·독 흉내: 아래쪽에 띠를 깔고 그 윗선 위를 달린다.
        double barHeight = Math.Min(Bounds.Height / 3, 36);
        var bar = new Rect(0, Bounds.Height - barHeight, Bounds.Width, barHeight);
        ctx.FillRectangle(GameArt.Brush(alt ? p.PreviewAltBar : p.PreviewBar), bar);
        ctx.DrawLine(new Pen(GameArt.Brush(alt ? p.PreviewAltLine : p.PreviewLine)), new Point(0, bar.Y + 0.5), new Point(Bounds.Width, bar.Y + 0.5));

        var size = Sprite.Measure(RunnerSize / UnitsPerDip);
        var box = new Rect(_state.X, bar.Y - size.Height + 1, size.Width, size.Height);
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        Sprite.Draw(ctx, box, new RunnerPose(_state.Direction, _state.GaitPhase, _state.Intensity), RunnerColor, scaling);

        var text = GameArt.Format($"{_usage:0}%  ·  {_state.Speed * UnitsPerDip:0} px/s", GameArt.FontNote, alt ? p.PreviewAltText : p.PreviewText);
        ctx.DrawText(text, new Point(8, bar.Y + 6));
    }
}
