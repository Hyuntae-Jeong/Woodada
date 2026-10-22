using System.Drawing.Drawing2D;
using Woodada.Settings;

namespace Woodada.UI;

/// <summary>설정 창을 게임처럼 꾸미는 색과 작은 그림들(생선, 자물쇠, 둥근 상자).</summary>
internal static class GameArt
{
    // 색은 Theme.Current에서 읽는다. 테마를 바꾸면 새로 만든 창부터 반영된다.
    private static ThemePalette P => Theme.Current;
    public static Color Background => P.Background;
    public static Color Card => P.Card;
    public static Color CardBorder => P.CardBorder;
    public static Color Accent => P.Accent;
    public static Color OnAccent => P.OnAccent;
    public static Color AccentSoft => P.AccentSoft;
    public static Color Text => P.Text;
    public static Color SubText => P.SubText;
    public static Color Fish => P.Fish;
    public static Color FishDark => P.FishDark;
    public static Color FishMuted => P.FishMuted;
    public static Color Locked => P.Locked;
    public static Color LockedHover => P.LockedHover;
    public static Color Good => P.Good;
    public static Color Mystery => P.Mystery;

    /// <summary>고양이 색이 받침과 너무 비슷하면(하양 고양이 + 밝은 받침 등) 대비가 나는 받침을 고른다.</summary>
    public static Color SwatchBackFor(Color cat) =>
        LowContrast(cat, P.SwatchBack) ? P.SwatchBackAlt : P.SwatchBack;

    public static bool LowContrast(Color a, Color b) =>
        Math.Abs(ColorUtil.Luminance(a) - ColorUtil.Luminance(b)) < 0.22;

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color fill, Color? border = null, float borderWidth = 1)
    {
        using var path = RoundRect(r, radius);
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        if (border is { } b)
        {
            using var pen = new Pen(b, borderWidth);
            g.DrawPath(pen, path);
        }
    }

    // 생선 뼈 아이콘은 120×64 칸에 그린 설계를 그대로 옮겼다. 그중 실제로 그림이 있는 영역만 잘라 box에 맞춘다.
    private static readonly RectangleF FishArtBounds = new(5, 11, 113, 42);

    /// <summary>생선 아이콘의 폭 ÷ 높이. 높이를 정하면 폭은 이 비율로 잡는다.</summary>
    public const float FishAspect = 113f / 42f;

    /// <summary>
    /// 왼쪽을 보는 둥근 생선 뼈: 동그란 머리와 흰 눈, 가시 4개, 끝이 둥근 갈래 꼬리.
    /// 고양이 그림과 어울리도록 모든 선 끝과 모서리를 둥글게 한다. box 안에 비율을 지켜 가운데 맞춘다.
    /// </summary>
    public static void DrawFish(Graphics g, RectangleF box, Color? color = null)
    {
        if (box.Width <= 0 || box.Height <= 0) return;
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float scale = Math.Min(box.Width / FishArtBounds.Width, box.Height / FishArtBounds.Height);
        float drawnW = FishArtBounds.Width * scale, drawnH = FishArtBounds.Height * scale;
        g.TranslateTransform(box.Left + (box.Width - drawnW) / 2, box.Top + (box.Height - drawnH) / 2);
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(-FishArtBounds.Left, -FishArtBounds.Top);

        var body = color ?? Fish;
        using var brush = new SolidBrush(body);

        // 머리: 앞(왼쪽)은 둥글고 뒤는 살짝 납작한 동그라미.
        using (var head = new GraphicsPath())
        {
            head.AddBeziers(new[]
            {
                new PointF(6, 32), new PointF(6, 17), new PointF(19, 11), new PointF(33, 13),
                new PointF(38, 14), new PointF(41, 22), new PointF(41, 32),
                new PointF(41, 42), new PointF(38, 50), new PointF(33, 51),
                new PointF(19, 53), new PointF(6, 47), new PointF(6, 32),
            });
            head.CloseFigure();
            g.FillPath(brush, head);
        }

        // 등뼈와 가시. 뒤로 갈수록 가시가 짧아진다. 마지막 가시는 꼬리에 붙어 보이지 않게 띄운다.
        // 가격표처럼 작게 그릴 때(높이 16px 미만)는 가시 4개가 뭉개져 빗처럼 보이므로 굵은 가시 3개로 줄인다.
        bool small = drawnH < 16;
        using (var bone = new Pen(body, small ? 7.5f : 6) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(bone, 40, 32, 94, 32);
            if (small)
            {
                g.DrawLine(bone, 55, 16, 55, 48);
                g.DrawLine(bone, 68, 19, 68, 45);
                g.DrawLine(bone, 81, 22, 81, 42);
            }
            else
            {
                g.DrawLine(bone, 52, 15, 52, 49);
                g.DrawLine(bone, 63, 17, 63, 47);
                g.DrawLine(bone, 74, 20, 74, 44);
                g.DrawLine(bone, 84, 23, 84, 41);
            }
        }

        // 꼬리: 두 갈래 끝을 둥글린 V. 테두리를 같은 색으로 한 번 더 그어 모서리를 둥글게 만든다.
        using (var tail = new GraphicsPath())
        {
            tail.AddLine(92, 32, 110, 17);
            tail.AddBezier(110, 17, 114, 14.33f, 116, 15.33f, 116, 20);
            tail.AddLine(116, 20, 113, 32);
            tail.AddLine(113, 32, 116, 44);
            tail.AddBezier(116, 44, 116, 48.67f, 114, 49.67f, 110, 47);
            tail.CloseFigure();
            g.FillPath(brush, tail);
            using var edge = new Pen(body, 3) { LineJoin = LineJoin.Round };
            g.DrawPath(edge, tail);
        }

        // 눈: 고양이처럼 흰 점.
        using (var eye = new SolidBrush(Color.White))
            g.FillEllipse(eye, 22 - 4.5f, 27 - 4.5f, 9, 9);

        g.Restore(state);
    }

    /// <summary>가격표의 생선 높이. 글자 줄 높이보다 조금 작게 그려야 숫자와 어울린다.</summary>
    private const float PriceFishRatio = 0.85f;

    /// <summary>생선 아이콘 + 숫자. 그린 폭을 돌려준다.</summary>
    public static float DrawPrice(Graphics g, string text, Font font, PointF origin, float height, Color textColor, Color? fishColor = null)
    {
        float fishH = height * PriceFishRatio, fishW = fishH * FishAspect, gap = height * 0.25f;
        DrawFish(g, new RectangleF(origin.X, origin.Y + (height - fishH) / 2, fishW, fishH), fishColor);
        var size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
        int textY = (int)(origin.Y + (height - size.Height) / 2);
        TextRenderer.DrawText(g, text, font, new Point((int)(origin.X + fishW + gap), textY), textColor, TextFormatFlags.NoPadding);
        return fishW + gap + size.Width;
    }

    public static Size MeasurePrice(Graphics g, string text, Font font, float height)
    {
        var size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
        float fishW = height * PriceFishRatio * FishAspect;
        return new Size((int)Math.Ceiling(fishW + height * 0.25f + size.Width), (int)height);
    }

    public static void DrawLock(Graphics g, RectangleF box, Color color)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float w = box.Width, h = box.Height;
        var body = new RectangleF(box.Left, box.Top + h * 0.42f, w, h * 0.58f);
        using (var pen = new Pen(color, Math.Max(1.5f, w * 0.16f)))
            g.DrawArc(pen, box.Left + w * 0.2f, box.Top + h * 0.05f, w * 0.6f, h * 0.7f, 180, 180);
        FillRound(g, body, w * 0.18f, color);
        g.Restore(state);
    }

    public static string FormatCount(long n) => n.ToString("#,0");
}
