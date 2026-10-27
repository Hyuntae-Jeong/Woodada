using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Woodada.Settings;

namespace Woodada.Sprites;

/// <summary>
/// 플래시 스킨: 지나온 길을 따라 잔상이 촘촘히 겹치고, 등·옆구리에서 불꽃 줄기가 뒤로 흩날린다.
/// 잔상은 몸 쪽은 러너 색, 꼬리 쪽으로 갈수록 빨강 → 주황 → 노랑으로 바랜다. 빠를수록 꼬리가 길다.
/// 아주 빠르면 몸도 빨갛게 달아오른다. 평소엔 고른 색을 그대로 입는다.
/// 겉모습만 바뀌고 속도·생선은 그대로다. 값은 tools/skin_preview.py flash 미리보기로 맞췄다.
/// </summary>
internal static class FlashSkin
{
    /// <summary>잔상 개수.</summary>
    public const int AfterimageCount = 8;
    /// <summary>가장 빠를 때 잔상 꼬리 길이(몸 길이 대비).</summary>
    private const double TrailLength = 0.90;
    /// <summary>몸에 가장 가까운 잔상의 진하기. 꼬리 쪽으로 갈수록 옅어진다.</summary>
    private const double FirstOpacity = 0.60;
    /// <summary>
    /// 느리게 걸을 때도 이만큼(0~1)은 효과가 보이고, FullUsage(%)까지 오르며 다 진해진다.
    /// 평소 CPU는 5~15%라 문턱을 두면 산 스킨이 거의 늘 맨몸으로 보인다.
    /// </summary>
    private const double MinStrength = 0.35;
    private const double FullUsage = 40;

    /// <summary>이 사용률(%)부터 몸이 달아오르기 시작해 HeatFullUsage에서 완전히 HeatColor가 된다.</summary>
    private const double HeatStartUsage = 50;
    private const double HeatFullUsage = 60;
    private static readonly Color HeatColor = Color.FromRgb(0xD9, 0x62, 0x6B);
    /// <summary>몸이 이미 붉은 계열이면 빨갛게 달아올라도 티가 안 나서, 대신 밝은 주황으로 달아오른다.</summary>
    private static readonly Color HeatColorForRed = Color.FromRgb(0xFF, 0xB3, 0x47);
    /// <summary>HeatColor와 색상(hue)이 이만큼(°) 가깝고 색이 뚜렷하면 붉은 계열로 본다.</summary>
    private const double RedBodyDegrees = 25;
    /// <summary>
    /// 달아오르는 색을 몇 단계로 나눌지. 색마다 프레임을 새로 칠해 캐시하므로,
    /// 매끄럽게 섞으면 사용률이 조금만 흔들려도 새 그림을 계속 만든다.
    /// </summary>
    private const int HeatSteps = 5;

    /// <summary>불꽃 줄기 개수와, 가장 빠를 때 길이(몸 길이 대비).</summary>
    private const int WispCount = 4;
    private const double WispLength = 1.30;
    private const int WispSegments = 14;

    private static readonly Color[] FireStops =
    {
        Color.FromRgb(0xFF, 0x4D, 0x2E), Color.FromRgb(0xFF, 0x8A, 0x3D),
        Color.FromRgb(0xFF, 0xC2, 0x4A), Color.FromRgb(0xFF, 0xE9, 0xA0),
    };

    /// <summary>MinStrength~1. 효과가 얼마나 보일지.</summary>
    public static double Strength(double usagePercent) =>
        MinStrength + (1 - MinStrength) * Math.Clamp(usagePercent / FullUsage, 0, 1);

    /// <summary>지금 입을 몸 색. 평소엔 고른 색, 아주 빠르면 빨갛게(붉은 몸이면 주황으로) 달아오른다.</summary>
    public static Color BodyColor(Color chosen, double usagePercent)
    {
        double heat = Math.Clamp((usagePercent - HeatStartUsage) / (HeatFullUsage - HeatStartUsage), 0, 1);
        heat = Math.Round(heat * HeatSteps) / HeatSteps;
        return heat <= 0 ? chosen : Mix(chosen, HeatColorFor(chosen), heat);
    }

    private static Color HeatColorFor(Color chosen) =>
        ColorUtil.IsChromatic(chosen, 0.3) && ColorUtil.HueDistance(chosen.ToHsl().H, HeatColor.ToHsl().H) < RedBodyDegrees
            ? HeatColorForRed
            : HeatColor;

    /// <summary>빠를수록 길어지는 몫(0.35~1).</summary>
    private static double SpeedShare(double usagePercent) => 0.35 + 0.65 * Math.Clamp(usagePercent / 100, 0, 1);

    /// <summary>잔상 꼬리 전체 길이(몸 너비와 같은 단위).</summary>
    public static double TailLength(double bodyWidth, double usagePercent) =>
        bodyWidth * TrailLength * Strength(usagePercent) * SpeedShare(usagePercent);

    /// <summary>i번째 잔상(1 = 몸에 가장 가까움)의 진하기.</summary>
    public static double AfterimageOpacity(int i, double usagePercent) =>
        FirstOpacity * Strength(usagePercent) * Math.Pow(1 - (i - 1) / (double)AfterimageCount, 1.3);

    /// <summary>꼬리 쪽 위치 t(0 = 몸, 1 = 끝)의 색. 몸에 붙은 쪽은 러너 색과 섞어 몸에서 자연스럽게 이어진다.</summary>
    public static Color TrailColor(double t, Color body)
    {
        t = Math.Clamp(t, 0, 1);
        double x = t * (FireStops.Length - 1);
        int i = Math.Min(FireStops.Length - 2, (int)x);
        var fire = Mix(FireStops[i], FireStops[i + 1], x - i);
        return t < 0.2 ? Mix(body, fire, 0.5 + t * 2.5) : fire;
    }

    /// <summary>
    /// 불꽃 줄기: 몸 뒤쪽에서 뒤로 뻗는 가는 줄. 끝으로 갈수록 가늘고 옅어지며 위로 살짝 들리고 일렁인다.
    /// 몸보다 먼저(아래에) 그린다.
    /// </summary>
    /// <param name="box">몸 자리(논리 단위).</param>
    /// <param name="phase">러너마다 다른 시작 위상. 두 마리가 똑같이 일렁이지 않게 한다.</param>
    public static void DrawWisps(DrawingContext ctx, Rect box, int facing, double usagePercent, double seconds, double phase,
        Color body, double renderScaling)
    {
        double k = Strength(usagePercent);
        if (k <= 0) return;
        double scale = renderScaling > 0 ? renderScaling : 1;
        double w = box.Width, h = box.Height;
        int dir = facing < 0 ? -1 : 1;
        // 줄기는 등 한가운데쯤에서 나와야 몸에서 뿜어져 나오는 것처럼 보인다.
        double back = box.X + (dir > 0 ? w * 0.35 : w * 0.65);
        double maxWidth = Math.Max(1.5, h * scale * 0.07); // 장치 픽셀

        var pens = WispPens(body, k, maxWidth, scale);

        for (int j = 0; j < WispCount; j++)
        {
            double rnd = 0.5 + 0.5 * Math.Sin(j * 12.9898 + phase);
            double len = w * WispLength * k * SpeedShare(usagePercent) * (0.55 + 0.45 * rnd);
            double yFrac = WispCount == 1 ? 0.45 : 0.12 + 0.6 * j / (WispCount - 1);
            double y0 = box.Bottom - h * (1 - yFrac);

            Point At(double q) => new(
                back - dir * len * q,
                y0 + Math.Sin(q * 4.5 - seconds * 14 + j * 1.7 + phase) * h * 0.07 * q - h * 0.08 * q);

            for (int s = 0; s < WispSegments; s++)
            {
                double a = s / (double)WispSegments, b = (s + 1) / (double)WispSegments;
                ctx.DrawLine(pens[s], At(a), At(b));
            }
        }
    }

    /// <summary>
    /// 줄기 마디별 펜(색·진하기·굵기). 매 프레임 붓·펜을 새로 만들면 화면 합성 쪽 객체까지 함께 생겨
    /// 초당 수천 개씩 쓰레기가 쌓이고 메모리가 수백 MB씩 출렁인다. 그래서 한 번 만든 펜을 다시 쓴다.
    /// </summary>
    private static ImmutablePen[] WispPens(Color body, double strength, double maxWidth, double scale)
    {
        // 진하기는 사용률 따라 계속 변하니 단계로 끊어야 캐시가 맞는다. 32단계면 진하기 차이가 2%대라 눈에 안 띈다.
        int step = (int)Math.Round(strength * StrengthSteps);
        var key = new WispKey(body.ToUInt32(), step, maxWidth, scale);
        if (WispPenCache.TryGetValue(key, out var pens)) return pens;

        // 펜은 불변이라 Dispose할 것이 없다. 넘치면 비우고 다시 채운다.
        if (WispPenCache.Count >= MaxWispPenSets) WispPenCache.Clear();

        double k = step / (double)StrengthSteps;
        pens = new ImmutablePen[WispSegments];
        for (int s = 0; s < WispSegments; s++)
        {
            double a = s / (double)WispSegments;
            var brush = new ImmutableSolidColorBrush(TrailColor(a, body), 0.75 * k * (1 - a));
            double thickness = Math.Max(0.6, (1 - a) * maxWidth) / scale;
            pens[s] = new ImmutablePen(brush, thickness, lineCap: PenLineCap.Round);
        }
        WispPenCache[key] = pens;
        return pens;
    }

    private const int StrengthSteps = 32;
    // 러너 둘 × 진하기 단계(MinStrength~1이라 약 21개) × 달아오름 단계(6개)를 넉넉히 담는다
    private const int MaxWispPenSets = 256;

    private readonly record struct WispKey(uint Body, int StrengthStep, double MaxWidth, double Scale);

    // 오버레이 그리기는 UI 스레드에서만 하므로 잠금 없이 쓴다
    private static readonly Dictionary<WispKey, ImmutablePen[]> WispPenCache = new();

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
