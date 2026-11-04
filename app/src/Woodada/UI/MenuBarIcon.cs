using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Woodada.UI;

/// <summary>
/// macOS 메뉴 막대 아이콘 오른쪽에 생선 수를 붙인 그림. Avalonia 트레이 아이콘에는 글자를 넣는 기능이 없어서
/// 고양이와 숫자를 한 장으로 그린다. 검정 + 투명 템플릿이라 숫자도 고양이처럼 막대 색에 맞춰 칠해진다.
/// </summary>
internal static class MenuBarIcon
{
    /// <summary>
    /// 원본 고양이 그림(44×37px)은 메뉴 막대에서 높이가 메뉴 글꼴(14pt) × 4/3 ≈ 18.7pt로 맞춰져 보인다.
    /// 그 비율로 글자 크기를 정해야 숫자가 시계 글자와 같은 크기로 보인다.
    /// </summary>
    private const double PixelsPerPoint = 37 / (14 * 4.0 / 3);

    private const double FontPoints = 13;   // 시계와 같은 크기
    private const double GapPoints = 4;     // 고양이와 숫자 사이
    private static readonly double FontSize = FontPoints * PixelsPerPoint;
    private static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);

    private static Bitmap? _cat;
    private static double _digitWidth;

    /// <summary>
    /// 9,999까지는 쉼표를 넣어 다 보여 주고, 그 위로는 12.3k·1.2M처럼 줄인다.
    /// 메뉴 막대는 자리가 좁고, 노치가 있는 맥북은 폭이 넓으면 아이콘이 가려질 수 있다. 반올림하지 않고 내려서 실제보다 많아 보이지 않게 한다.
    /// </summary>
    public static string FormatCount(long fish)
    {
        fish = Math.Max(0, fish);
        if (fish < 10_000) return fish.ToString("#,0", CultureInfo.InvariantCulture);
        if (fish < 1_000_000) return Short(fish / 100, "k");
        return Short(fish / 100_000, "M");
    }

    private static string Short(long tenths, string unit) =>
        (tenths % 10 == 0
            ? (tenths / 10).ToString(CultureInfo.InvariantCulture)
            : (tenths / 10.0).ToString("0.0", CultureInfo.InvariantCulture)) + unit;

    /// <summary>고양이 + 숫자 그림. UI 스레드에서 부른다.</summary>
    public static WindowIcon Render(string text)
    {
        var cat = _cat ??= LoadCat();
        int catW = cat.PixelSize.Width, height = cat.PixelSize.Height;
        if (_digitWidth == 0)
            _digitWidth = Enumerable.Range(0, 10).Max(d => Format(d.ToString(CultureInfo.InvariantCulture)).WidthIncludingTrailingWhitespace);

        // 숫자는 모두 같은 폭 칸에 넣는다. 생선이 늘어도 글자가 좌우로 흔들리거나 옆 아이콘을 밀지 않는다.
        var glyphs = text.Select(c => (Text: Format(c.ToString()), Digit: char.IsAsciiDigit(c))).ToList();
        double textW = glyphs.Sum(g => g.Digit ? _digitWidth : g.Text.WidthIncludingTrailingWhitespace);
        double gap = GapPoints * PixelsPerPoint;
        int width = (int)Math.Ceiling(catW + gap + textW + 1);

        // 숫자 잉크(0의 윗선~아랫선)를 그림 세로 가운데에 둔다. 메뉴 막대가 그림을 가운데에 놓으니 시계 숫자와 높이가 맞는다.
        var zero = Format("0").BuildGeometry(new Point(0, 0))?.Bounds ?? new Rect(0, 0, 0, FontSize);
        double top = (height - zero.Height) / 2 - zero.Y;

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.DrawImage(cat, new Rect(0, 0, catW, height));
            double x = catW + gap;
            foreach (var (glyph, digit) in glyphs)
            {
                double cell = digit ? _digitWidth : glyph.WidthIncludingTrailingWhitespace;
                ctx.DrawText(glyph, new Point(x + (cell - glyph.WidthIncludingTrailingWhitespace) / 2, top));
                x += cell;
            }
        }

        using var png = new MemoryStream();
        bitmap.Save(png);
        png.Position = 0;
        return new WindowIcon(png);
    }

    private static FormattedText Format(string text) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, FontSize, Brushes.Black);

    private static Bitmap LoadCat()
    {
        using var stream = typeof(MenuBarIcon).Assembly.GetManifestResourceStream("MenuBarTemplate.png")
                           ?? throw new InvalidOperationException("리소스 없음: MenuBarTemplate.png");
        return new Bitmap(stream);
    }
}
