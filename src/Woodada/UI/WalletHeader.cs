using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Woodada.Settings;

namespace Woodada.UI;

/// <summary>설정 창 맨 위 띠. 가진 생선 수와 다음 생선까지 남은 정도를 보여준다.</summary>
internal sealed class WalletHeader : Control
{
    private readonly DispatcherTimer _animation = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _fish = -1;
    private double _progress;
    private long _gained;
    private double _gainedAt = double.NegativeInfinity;

    private const double GainShowSeconds = 1.6;

    public WalletHeader()
    {
        _animation.Tick += (_, _) =>
        {
            if (_clock.Elapsed.TotalSeconds - _gainedAt > GainShowSeconds) _animation.Stop();
            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) => _animation.Stop();
    }

    /// <summary>생선이 늘었으면 "+n"을 잠깐 띄운다. 처음 값은 그냥 보여준다.</summary>
    public void SetWallet(long fish, double progress)
    {
        if (_fish >= 0 && fish > _fish)
        {
            // 띄워 둔 사이에 또 늘면 합쳐서 보여준다.
            bool showing = _clock.Elapsed.TotalSeconds - _gainedAt < GainShowSeconds;
            _gained = (showing ? _gained : 0) + (fish - _fish);
            _gainedAt = _clock.Elapsed.TotalSeconds;
            _animation.Start();
        }
        if (fish == _fish && Math.Abs(progress - _progress) < 0.002) return;
        _fish = fish;
        _progress = progress;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double width = Bounds.Width, height = Bounds.Height;
        var rect = new Rect(Bounds.Size);
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(UI.Theme.Current.HeaderFrom, 0), new GradientStop(UI.Theme.Current.HeaderTo, 1) },
        };
        ctx.FillRectangle(gradient, rect);
        ctx.DrawLine(new Pen(GameArt.Brush(GameArt.CardBorder)), new Point(0, height - 0.5), new Point(width, height - 0.5));

        // 왼쪽: 큰 생선 + 개수
        const double pad = 18;
        // 생선 뼈는 옆으로 길어서 높이를 줄여야 숫자보다 커 보이지 않는다.
        double fishH = height * 0.36, fishW = fishH * GameArt.FishAspect;
        GameArt.DrawFish(ctx, new Rect(pad, (height - fishH) / 2, fishW, fishH));

        var count = GameArt.Format(GameArt.FormatCount(Math.Max(0, _fish)), GameArt.FontHeadline, GameArt.Text, FontWeight.Bold);
        double countX = pad + fishW + 10;
        double countY = (height - count.Height) / 2 - 2;
        ctx.DrawText(count, new Point(countX, countY));
        var label = GameArt.Format("생선", GameArt.FontBody, GameArt.SubText);
        // 숫자 아랫줄에 맞춘다.
        double baseline = countY + count.Baseline;
        ctx.DrawText(label, new Point(countX + count.Width + 6, baseline - label.Baseline));

        // 방금 생긴 생선: 개수 옆에서 위로 떠오르며 사라진다.
        double age = _clock.Elapsed.TotalSeconds - _gainedAt;
        if (age >= 0 && age < GainShowSeconds)
        {
            double t = age / GainShowSeconds;
            byte alpha = (byte)(255 * (1 - t * t));
            var gain = GameArt.Format($"+{_gained}", 14.7, ColorUtil.WithAlpha(GameArt.Good, alpha), FontWeight.Bold);
            double gx = countX + count.Width + label.Width + 14;
            double gy = height / 2 - 16 - t * 10;
            ctx.DrawText(gain, new Point(gx, gy));
        }

        // 오른쪽: 다음 생선까지 진행 막대
        const double barW = 200, barH = 12;
        double barX = width - pad - barW;
        double barY = height / 2 + 2;
        var caption = GameArt.Format("다음 생선까지", GameArt.FontNote, GameArt.SubText);
        ctx.DrawText(caption, new Point(barX, barY - 20));
        var percent = GameArt.Format($"{_progress:0%}", GameArt.FontNote, GameArt.Text);
        ctx.DrawText(percent, new Point(barX + barW - percent.Width, barY - 20));

        var track = new Rect(barX, barY, barW, barH);
        GameArt.FillRound(ctx, track, barH / 2, UI.Theme.Current.ProgressTrack, GameArt.CardBorder);
        double fillW = barW * Math.Clamp(_progress, 0, 1);
        if (fillW >= barH)
            GameArt.FillRound(ctx, new Rect(barX, barY, fillW, barH), barH / 2, GameArt.Fish);
        else if (fillW > 0)
            GameArt.FillRound(ctx, new Rect(barX, barY, barH, barH), barH / 2, ColorUtil.WithAlpha(GameArt.Fish, (byte)(255 * fillW / barH)));
    }
}
