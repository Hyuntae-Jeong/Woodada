using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Woodada.UI;

/// <summary>설정 창 맨 위 띠. 가진 생선 수와 다음 생선까지 남은 정도를 보여준다.</summary>
internal sealed class WalletHeader : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 33 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _fish = -1;
    private double _progress;
    private long _gained;
    private double _gainedAt = double.NegativeInfinity;

    private const double GainShowSeconds = 1.6;

    public WalletHeader()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _animation.Tick += (_, _) =>
        {
            if (_clock.Elapsed.TotalSeconds - _gainedAt > GainShowSeconds) _animation.Stop();
            Invalidate();
        };
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
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f;
        var rect = ClientRectangle;
        using (var bg = new LinearGradientBrush(rect, Theme.Current.HeaderFrom, Theme.Current.HeaderTo, LinearGradientMode.Horizontal))
            g.FillRectangle(bg, rect);
        using (var line = new Pen(GameArt.CardBorder))
            g.DrawLine(line, 0, rect.Bottom - 1, rect.Right, rect.Bottom - 1);

        // 왼쪽: 큰 생선 + 개수
        float pad = 18 * s;
        // 생선 뼈는 옆으로 길어서 높이를 줄여야 숫자보다 커 보이지 않는다.
        float fishH = Height * 0.36f, fishW = fishH * GameArt.FishAspect;
        GameArt.DrawFish(g, new RectangleF(pad, (Height - fishH) / 2, fishW, fishH));

        using var countFont = new Font("Segoe UI", 20f, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 9f);
        using var smallFont = new Font("Segoe UI", 8.5f);
        string count = GameArt.FormatCount(Math.Max(0, _fish));
        var countSize = TextRenderer.MeasureText(g, count, countFont, Size.Empty, TextFormatFlags.NoPadding);
        float countX = pad + fishW + 10 * s;
        TextRenderer.DrawText(g, count, countFont, new Point((int)countX, (Height - countSize.Height) / 2 - (int)(2 * s)), GameArt.Text, TextFormatFlags.NoPadding);
        var labelSize = TextRenderer.MeasureText(g, "생선", labelFont, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "생선", labelFont,
            new Point((int)(countX + countSize.Width + 6 * s), (Height + countSize.Height) / 2 - labelSize.Height - (int)(6 * s)), GameArt.SubText, TextFormatFlags.NoPadding);

        // 방금 생긴 생선: 개수 옆에서 위로 떠오르며 사라진다.
        double age = _clock.Elapsed.TotalSeconds - _gainedAt;
        if (age >= 0 && age < GainShowSeconds)
        {
            double t = age / GainShowSeconds;
            int alpha = (int)(255 * (1 - t * t));
            using var gainFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            var color = Color.FromArgb(alpha, GameArt.Good);
            float gx = countX + countSize.Width + labelSize.Width + 14 * s;
            float gy = Height / 2f - 16 * s - (float)(t * 10 * s);
            TextRenderer.DrawText(g, $"+{_gained}", gainFont, new Point((int)gx, (int)gy), color, TextFormatFlags.NoPadding);
        }

        // 오른쪽: 다음 생선까지 진행 막대
        float barW = 200 * s, barH = 12 * s;
        float barX = Width - pad - barW;
        float barY = Height / 2f + 2 * s;
        string caption = "다음 생선까지";
        string percent = $"{_progress:0%}";
        TextRenderer.DrawText(g, caption, smallFont, new Point((int)barX, (int)(barY - 20 * s)), GameArt.SubText, TextFormatFlags.NoPadding);
        var pctSize = TextRenderer.MeasureText(g, percent, smallFont, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, percent, smallFont, new Point((int)(barX + barW - pctSize.Width), (int)(barY - 20 * s)), GameArt.Text, TextFormatFlags.NoPadding);

        var track = new RectangleF(barX, barY, barW, barH);
        GameArt.FillRound(g, track, barH / 2, Theme.Current.ProgressTrack, GameArt.CardBorder);
        float fillW = (float)(barW * Math.Clamp(_progress, 0, 1));
        if (fillW >= barH)
            GameArt.FillRound(g, new RectangleF(barX, barY, fillW, barH), barH / 2, GameArt.Fish);
        else if (fillW > 0)
            GameArt.FillRound(g, new RectangleF(barX, barY, barH, barH), barH / 2, Color.FromArgb((int)(255 * fillW / barH), GameArt.Fish));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }
}
