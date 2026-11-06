using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Woodada.Settings;

namespace Woodada.UI;

/// <summary>
/// 설정 창 맨 위 띠. 가진 생선 수, 다음 생선까지 남은 정도, 생선 1마리에 걸리는 시간을 보여준다.
/// 오른쪽 아래 "?"를 누르면 생선이 어떻게 모이는지 알려 주는 창을 띄운다(<see cref="HelpRequested"/>).
/// </summary>
internal sealed class WalletHeader : Control
{
    private readonly DispatcherTimer _animation = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _fish = -1;
    private double _progress;
    private string _pace = "";
    private long _gained;
    private double _gainedAt = double.NegativeInfinity;

    private const double GainShowSeconds = 1.6;
    private const double HelpSize = 14;
    private const double HelpMarkSize = 10;
    private const double CapHalf = 0.36; // 대문자·숫자 높이(글자 크기의 약 0.72)의 반

    // 그릴 때 정해지는 "?" 자리. 누르기·마우스 올리기를 여기서 판단한다.
    private Rect _helpRect;
    private bool _helpHover;

    /// <summary>"?"를 눌렀다.</summary>
    public event EventHandler? HelpRequested;

    public WalletHeader()
    {
        _animation.Tick += (_, _) =>
        {
            if (_clock.Elapsed.TotalSeconds - _gainedAt > GainShowSeconds) _animation.Stop();
            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) => _animation.Stop();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHelpHover(_helpRect.Contains(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHelpHover(false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !_helpRect.Contains(e.GetPosition(this))) return;
        e.Handled = true;
        HelpRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetHelpHover(bool hover)
    {
        if (hover == _helpHover) return;
        _helpHover = hover;
        Cursor = hover ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        InvalidateVisual();
    }

    /// <summary>생선이 늘었으면 "+n"을 잠깐 띄운다. 처음 값은 그냥 보여준다.</summary>
    public void SetWallet(long fish, double progress, double? secondsPerFish)
    {
        string pace = PaceText(secondsPerFish);
        if (_fish >= 0 && fish > _fish)
        {
            // 띄워 둔 사이에 또 늘면 합쳐서 보여준다.
            bool showing = _clock.Elapsed.TotalSeconds - _gainedAt < GainShowSeconds;
            _gained = (showing ? _gained : 0) + (fish - _fish);
            _gainedAt = _clock.Elapsed.TotalSeconds;
            _animation.Start();
        }
        if (fish == _fish && Math.Abs(progress - _progress) < 0.002 && pace == _pace) return;
        _fish = fish;
        _progress = progress;
        _pace = pace;
        InvalidateVisual();
    }

    /// <summary>
    /// 1분 안쪽은 5초, 1시간 안쪽은 1분, 그 위는 10분 단위로 반올림한다.
    /// 정확한 숫자보다 대략 얼마나 걸리는지 감만 주려는 것이다.
    /// </summary>
    private static string PaceText(double? secondsPerFish)
    {
        if (secondsPerFish is not { } s) return Loc.T("속도 재는 중…", "Measuring pace…");
        if (s >= 24 * 3600) return Loc.T("고양이가 쉬고 있어요", "Cats are resting");

        double sec = Math.Max(5, Math.Round(s / 5) * 5);
        if (sec < 60) return Loc.T($"생선 1마리에 약 {sec:0}초", $"About {sec:0}s per fish");

        double min = Math.Round(s / 60);
        if (min < 60) return Loc.T($"생선 1마리에 약 {min:0}분", $"About {min:0} min per fish");

        int total = (int)(Math.Round(s / 600) * 10);
        int h = total / 60, m = total % 60;
        return m == 0
            ? Loc.T($"생선 1마리에 약 {h}시간", $"About {h}h per fish")
            : Loc.T($"생선 1마리에 약 {h}시간 {m}분", $"About {h}h {m}m per fish");
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
        // 글자 상자는 위아래 여백이 글꼴마다 달라서, 실제로 칠해지는 숫자 모양의 가운데를 생선 그림 가운데에 맞춘다.
        // 잉크 아래끝 = Height + OverhangAfter, 잉크 높이 = Extent.
        double inkMid = count.Height + count.OverhangAfter - count.Extent / 2;
        double countY = height / 2 - inkMid;
        ctx.DrawText(count, new Point(countX, countY));
        var label = GameArt.Format(Loc.T("생선", "fish"), GameArt.FontBody, GameArt.SubText);
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

        // 오른쪽: 다음 생선까지 진행 막대, 그 아래 생선 1마리에 걸리는 시간
        const double barW = 200, barH = 12;
        double barX = width - pad - barW;
        double barY = height / 2 - 9;
        var caption = GameArt.Format(Loc.T("다음 생선까지", "Next fish"), GameArt.FontNote, GameArt.SubText);
        ctx.DrawText(caption, new Point(barX, barY - 19));
        var percent = GameArt.Format($"{_progress:0%}", GameArt.FontNote, GameArt.Text);
        ctx.DrawText(percent, new Point(barX + barW - percent.Width, barY - 19));
        var pace = GameArt.Format(_pace, GameArt.FontNote, GameArt.SubText);
        double paceY = barY + barH + 4;
        ctx.DrawText(pace, new Point(barX, paceY));

        // 속도 줄 오른쪽 끝, 진행 막대 끝선에 맞춘 "?" 단추. 평소엔 속도 글자처럼 회색으로 조용히 있다가
        // 마우스를 올리면 생선 색으로 바뀐다.
        // 글자 상자 가운데가 아니라 글자 모양 가운데(기준선에서 대문자 높이의 반 위)에 맞춰야 나란해 보인다.
        double paceMid = paceY + pace.Baseline - GameArt.FontNote * CapHalf;
        _helpRect = new Rect(barX + barW - HelpSize, paceMid - HelpSize / 2, HelpSize, HelpSize);
        GameArt.FillRound(ctx, _helpRect, HelpSize / 2,
            _helpHover ? GameArt.AccentSoft : Colors.Transparent, _helpHover ? GameArt.Fish : GameArt.SubText, 1.2);
        var mark = GameArt.Format("?", HelpMarkSize, _helpHover ? GameArt.FishDark : GameArt.SubText, FontWeight.Bold);
        ctx.DrawText(mark, new Point(_helpRect.Center.X - mark.Width / 2, paceMid - (mark.Baseline - HelpMarkSize * CapHalf)));

        var track = new Rect(barX, barY, barW, barH);
        GameArt.FillRound(ctx, track, barH / 2, UI.Theme.Current.ProgressTrack, GameArt.CardBorder);
        double fillW = barW * Math.Clamp(_progress, 0, 1);
        if (fillW >= barH)
            GameArt.FillRound(ctx, new Rect(barX, barY, fillW, barH), barH / 2, GameArt.Fish);
        else if (fillW > 0)
            GameArt.FillRound(ctx, new Rect(barX, barY, barH, barH), barH / 2, ColorUtil.WithAlpha(GameArt.Fish, (byte)(255 * fillW / barH)));
    }
}
