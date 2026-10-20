using System.Diagnostics;
using Woodada.Core;
using Woodada.Settings;
using Woodada.Sprites;

namespace Woodada.UI;

/// <summary>설정 화면의 미리보기. 오버레이와 같은 RunnerState·스프라이트 코드로 왕복 달리기를 보여준다.</summary>
internal sealed class RunnerPreview : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly RunnerState _state = new();
    private double _last;
    private double _usage;

    public RunnerPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _timer.Tick += (_, _) => Step();
        _timer.Start();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IRunnerSprite Sprite { get; set; } = SpriteRegistry.Get("cat");
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color RunnerColor { get; set; } = Color.Orange;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int RunnerSize { get; set; } = 28;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<double>? UsageSource { get; set; }

    private void Step()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _last, 0.25);
        _last = now;

        double raw = UsageSource?.Invoke() ?? 0;
        _usage += (raw - _usage) * Math.Min(1, dt / 0.6);

        var size = Sprite.Measure(RunnerSize);
        var track = new TrackBounds(8, Width - 8 - size.Width);
        if (_state.X < track.MinX || _state.X > track.MaxX) _state.X = Math.Max(track.MinX, 0);
        _state.Advance(dt, _usage, track, SpeedProfile.ForBodyWidth(size.Width));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        // 작업표시줄 흉내: 아래쪽에 띠를 깔고 그 윗선 위를 달린다.
        int barHeight = Math.Min(Height / 3, 36);
        var bar = new Rectangle(0, Height - barHeight, Width, barHeight);
        using (var barBrush = new SolidBrush(Color.FromArgb(255, 238, 236, 233)))
            g.FillRectangle(barBrush, bar);
        using (var linePen = new Pen(Color.FromArgb(255, 214, 211, 206)))
            g.DrawLine(linePen, 0, bar.Top, Width, bar.Top);

        var size = Sprite.Measure(RunnerSize);
        var box = new RectangleF((float)_state.X, bar.Top - size.Height + 1, size.Width, size.Height);
        Sprite.Draw(g, box, new RunnerPose(_state.Direction, _state.GaitPhase, _state.Intensity), RunnerColor);

        using var font = new Font(Font.FontFamily, 8.5f);
        using var textBrush = new SolidBrush(Color.FromArgb(255, 110, 106, 100));
        g.DrawString($"{_usage:0}%  ·  {_state.Speed:0} px/s", font, textBrush, 8, bar.Top + 6);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
