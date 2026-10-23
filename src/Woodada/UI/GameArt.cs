using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Woodada.Settings;

namespace Woodada.UI;

/// <summary>설정 창을 게임처럼 꾸미는 색·글자와 작은 그림들(생선, 자물쇠, 둥근 상자).</summary>
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

    // 글자 크기(논리 픽셀). 예전 WinForms 버전의 포인트 크기 × 4/3 이다.
    public const double FontSmall = 11;      // 8.25pt: 칸 이름·가격
    public const double FontNote = 11.3;     // 8.5pt
    public const double FontBody = 12;       // 9pt: 기본
    public const double FontLarge = 13.3;    // 10pt: 탭·설명
    public const double FontSection = 14;    // 10.5pt: 구역 제목
    public const double FontTitle = 15.3;    // 11.5pt: 카드 제목
    public const double FontTotal = 16;      // 12pt
    public const double FontHeadline = 26.7; // 20pt: 오늘 달린 거리·생선 수

    public static IBrush Brush(Color c) => new ImmutableSolidColorBrush(c);

    public static Typeface Face(FontWeight weight = FontWeight.Normal) => new(FontFamily.Default, FontStyle.Normal, weight);

    public static FormattedText Format(string text, double size, Color color, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face(weight), size, Brush(color));

    /// <summary>글자를 상자 가운데에 그린다. 넘치면 끝을 …으로 줄인다.</summary>
    public static void DrawCentered(DrawingContext ctx, FormattedText text, Rect box)
    {
        text.MaxTextWidth = Math.Max(1, box.Width);
        text.MaxLineCount = 1;
        text.Trimming = TextTrimming.CharacterEllipsis;
        text.TextAlignment = TextAlignment.Center;
        ctx.DrawText(text, new Point(box.X, box.Y + (box.Height - text.Height) / 2));
    }

    /// <summary>고양이 색이 받침과 너무 비슷하면(하양 고양이 + 밝은 받침 등) 대비가 나는 받침을 고른다.</summary>
    public static Color SwatchBackFor(Color cat) =>
        LowContrast(cat, P.SwatchBack) ? P.SwatchBackAlt : P.SwatchBack;

    public static bool LowContrast(Color a, Color b) =>
        Math.Abs(ColorUtil.Luminance(a) - ColorUtil.Luminance(b)) < 0.22;

    public static void FillRound(DrawingContext ctx, Rect r, double radius, Color fill, Color? border = null, double borderWidth = 1)
    {
        double rr = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        var pen = border is { } b ? new Pen(Brush(b), borderWidth) : null;
        ctx.DrawRectangle(Brush(fill), pen, r, rr, rr);
    }

    // ───────────────────────── 생선 ─────────────────────────

    // 생선 뼈 아이콘은 120×64 칸에 그린 설계를 그대로 옮겼다. 그중 실제로 그림이 있는 영역만 잘라 box에 맞춘다.
    private static readonly Rect FishArtBounds = new(5, 11, 113, 42);

    /// <summary>생선 아이콘의 폭 ÷ 높이. 높이를 정하면 폭은 이 비율로 잡는다.</summary>
    public const double FishAspect = 113.0 / 42.0;

    // 머리: 앞(왼쪽)은 둥글고 뒤는 살짝 납작한 동그라미.
    private static readonly Geometry FishHead = Build(g =>
    {
        g.BeginFigure(new Point(6, 32), true);
        g.CubicBezierTo(new Point(6, 17), new Point(19, 11), new Point(33, 13));
        g.CubicBezierTo(new Point(38, 14), new Point(41, 22), new Point(41, 32));
        g.CubicBezierTo(new Point(41, 42), new Point(38, 50), new Point(33, 51));
        g.CubicBezierTo(new Point(19, 53), new Point(6, 47), new Point(6, 32));
        g.EndFigure(true);
    });

    // 꼬리: 두 갈래 끝을 둥글린 V. 테두리를 같은 색으로 한 번 더 그어 모서리를 둥글게 만든다.
    private static readonly Geometry FishTail = Build(g =>
    {
        g.BeginFigure(new Point(92, 32), true);
        g.LineTo(new Point(110, 17));
        g.CubicBezierTo(new Point(114, 14.33), new Point(116, 15.33), new Point(116, 20));
        g.LineTo(new Point(113, 32));
        g.LineTo(new Point(116, 44));
        g.CubicBezierTo(new Point(116, 48.67), new Point(114, 49.67), new Point(110, 47));
        g.EndFigure(true);
    });

    private static Geometry Build(Action<StreamGeometryContext> draw)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open()) draw(g);
        return geometry;
    }

    /// <summary>
    /// 왼쪽을 보는 둥근 생선 뼈: 동그란 머리와 흰 눈, 가시 4개, 끝이 둥근 갈래 꼬리.
    /// 고양이 그림과 어울리도록 모든 선 끝과 모서리를 둥글게 한다. box 안에 비율을 지켜 가운데 맞춘다.
    /// </summary>
    public static void DrawFish(DrawingContext ctx, Rect box, Color? color = null)
    {
        if (box.Width <= 0 || box.Height <= 0) return;
        double scale = Math.Min(box.Width / FishArtBounds.Width, box.Height / FishArtBounds.Height);
        double drawnW = FishArtBounds.Width * scale, drawnH = FishArtBounds.Height * scale;
        var transform = Matrix.CreateTranslation(-FishArtBounds.X, -FishArtBounds.Y)
                        * Matrix.CreateScale(scale, scale)
                        * Matrix.CreateTranslation(box.X + (box.Width - drawnW) / 2, box.Y + (box.Height - drawnH) / 2);

        var body = Brush(color ?? Fish);
        using (ctx.PushTransform(transform))
        {
            ctx.DrawGeometry(body, null, FishHead);

            // 등뼈와 가시. 뒤로 갈수록 가시가 짧아진다. 마지막 가시는 꼬리에 붙어 보이지 않게 띄운다.
            // 가격표처럼 작게 그릴 때(높이 16px 미만)는 가시 4개가 뭉개져 빗처럼 보이므로 굵은 가시 3개로 줄인다.
            bool small = drawnH < 16;
            var bone = new Pen(body, small ? 7.5 : 6, lineCap: PenLineCap.Round);
            ctx.DrawLine(bone, new Point(40, 32), new Point(94, 32));
            var spines = small
                ? new[] { (55.0, 16.0, 48.0), (68, 19, 45), (81, 22, 42) }
                : new[] { (52.0, 15.0, 49.0), (63, 17, 47), (74, 20, 44), (84, 23, 41) };
            foreach (var (x, top, bottom) in spines) ctx.DrawLine(bone, new Point(x, top), new Point(x, bottom));

            ctx.DrawGeometry(body, new Pen(body, 3, lineJoin: PenLineJoin.Round), FishTail);

            // 눈: 고양이처럼 흰 점.
            ctx.DrawEllipse(Brushes.White, null, new Point(22, 27), 4.5, 4.5);
        }
    }

    /// <summary>가격표의 생선 높이. 글자 줄 높이보다 조금 작게 그려야 숫자와 어울린다.</summary>
    private const double PriceFishRatio = 0.85;

    /// <summary>생선 아이콘 + 숫자. 그린 폭을 돌려준다.</summary>
    public static double DrawPrice(DrawingContext ctx, string text, double fontSize, FontWeight weight, Point origin, double height,
        Color textColor, Color? fishColor = null)
    {
        double fishH = height * PriceFishRatio, fishW = fishH * FishAspect, gap = height * 0.25;
        DrawFish(ctx, new Rect(origin.X, origin.Y + (height - fishH) / 2, fishW, fishH), fishColor);
        var formatted = Format(text, fontSize, textColor, weight);
        ctx.DrawText(formatted, new Point(origin.X + fishW + gap, origin.Y + (height - formatted.Height) / 2));
        return fishW + gap + formatted.Width;
    }

    public static Size MeasurePrice(string text, double fontSize, FontWeight weight, double height)
    {
        var formatted = Format(text, fontSize, Colors.Black, weight);
        double fishW = height * PriceFishRatio * FishAspect;
        return new Size(fishW + height * 0.25 + formatted.Width, height);
    }

    public static void DrawLock(DrawingContext ctx, Rect box, Color color)
    {
        double w = box.Width, h = box.Height;
        var brush = Brush(color);
        // 고리: 위쪽 반원.
        var ring = new Rect(box.X + w * 0.2, box.Y + h * 0.05, w * 0.6, h * 0.7);
        var arc = Build(g =>
        {
            g.BeginFigure(new Point(ring.Left, ring.Center.Y), false);
            g.ArcTo(new Point(ring.Right, ring.Center.Y), new Size(ring.Width / 2, ring.Height / 2), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        });
        ctx.DrawGeometry(null, new Pen(brush, Math.Max(1.5, w * 0.16)), arc);
        FillRound(ctx, new Rect(box.X, box.Y + h * 0.42, w, h * 0.58), w * 0.18, color);
    }

    public static string FormatCount(long n) => n.ToString("#,0", CultureInfo.InvariantCulture);
}
