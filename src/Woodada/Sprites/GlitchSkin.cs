using Avalonia.Media;
using Woodada.Settings;

namespace Woodada.Sprites;

/// <summary>몸에서 옆으로 밀리는 가로 줄 하나. 길이는 몸 크기 대비 비율이다.</summary>
/// <param name="Top">줄 위쪽(몸 높이 대비 0~1).</param>
/// <param name="Height">줄 두께(몸 높이 대비).</param>
/// <param name="Shift">옆으로 밀리는 거리(몸 너비 대비, 음수면 뒤로).</param>
public readonly record struct GlitchBand(double Top, double Height, double Shift);

/// <summary>몸 주변에 튀는 노이즈 조각 하나. 위치·크기는 몸 크기 대비 비율이다.</summary>
/// <param name="ColorIndex">0: 갈라짐 색 A, 1: 갈라짐 색 B, 2: 흰색. 실제 색은 몸색에 맞춘 조합(GlitchSkin.PairFor)에서 고른다.</param>
public readonly record struct GlitchNoise(double X, double Y, double Width, double Height, int ColorIndex);

/// <summary>지지직거리는 한 프레임의 모양.</summary>
/// <param name="Split">두 색이 몸에서 갈라지는 거리(화면 단위).</param>
/// <param name="SplitSide">+1이면 A색이 뒤로, B색이 앞으로 밀린다. -1이면 반대.</param>
public sealed record GlitchShape(IReadOnlyList<GlitchBand> Bands, double Split, int SplitSide, IReadOnlyList<GlitchNoise> Noise);

/// <summary>러너 한 마리의 글리치 진행 상태. 언제 터졌고 지금 어떤 모양인지.</summary>
public sealed class GlitchState
{
    internal double BurstUntil;
    internal double LastSeconds = double.NaN;

    /// <summary>지금 지지직거리는 모양. 평소엔 null.</summary>
    public GlitchShape? Shape { get; internal set; }
}

/// <summary>
/// 글리치 스킨: 평소엔 가장자리에 빨강·청록이 옅게 번진 채 달리다가, 가끔 화면이 고장 난 것처럼
/// 몸이 두 색으로 갈라지고 가로 줄이 밀리며 노이즈 조각이 튄다. CPU 사용률이 높을수록 더 자주, 더 세게 튄다.
/// 겉모습만 바뀌고 속도·생선은 그대로다. 값은 tools/skin_preview.py glitch 미리보기로 맞췄다.
/// </summary>
internal static class GlitchSkin
{
    /// <summary>1초에 몇 번 터지는지. CPU 0%일 때와 100%일 때 사이를 사용률로 잇는다.</summary>
    private const double RateAtIdle = 0.4;
    private const double RateAtFull = 3.0;
    /// <summary>한 번 터지면 지지직거리는 시간(초). 매번 0.6~1.4배로 흔든다.</summary>
    private const double BurstSeconds = 0.22;
    /// <summary>터지는 동안 프레임마다 모양을 새로 뽑을 확률. 1이 아니라서 가끔 한 모양이 잠깐 머문다.</summary>
    private const double RerollChance = 0.6;

    /// <summary>두 색이 갈라지는 거리(화면 단위, 사용률·무작위로 0.6~1.4배).</summary>
    private const double SplitDistance = 2.0;
    /// <summary>밀리는 줄 개수와, 가장 셀 때 밀리는 거리(몸 너비 대비).</summary>
    private const int BandCount = 3;
    private const double BandShift = 0.18;
    /// <summary>가장 셀 때 튀는 노이즈 조각 개수.</summary>
    private const int NoiseCount = 4;

    /// <summary>평소에 가장자리로 번져 보이는 두 색의 진하기. 글리치 스킨이라는 걸 늘 알 수 있게.</summary>
    public const double IdleFringeOpacity = 0.20;
    /// <summary>터질 때 갈라진 두 색의 진하기.</summary>
    public const double SplitOpacity = 0.85;
    public const double NoiseOpacity = 0.90;

    /// <summary>몸이 갈라질 때의 두 색. 기본은 빨강/청록이고, 몸색과 겹치면 다른 조합으로 바꾼다.</summary>
    public readonly record struct SplitPair(Color A, Color B);

    private static readonly SplitPair RedCyan = new(Color.FromRgb(0xFF, 0x2E, 0x4D), Color.FromRgb(0x22, 0xE6, 0xF0));
    private static readonly SplitPair MagentaGreen = new(Color.FromRgb(0xFF, 0x2B, 0xD6), Color.FromRgb(0x38, 0xF2, 0x7A));
    private static readonly SplitPair PurpleYellow = new(Color.FromRgb(0x8A, 0x4D, 0xFF), Color.FromRgb(0xFF, 0xE1, 0x4A));

    /// <summary>몸색이 갈라짐 색 중 하나와 색상(hue)이 이만큼(°) 가까우면 겹친다고 본다.</summary>
    private const double PairConflictDegrees = 30;

    /// <summary>
    /// 이 몸색에 쓸 갈라짐 색. 기본은 빨강/청록인데, 체리(빨강)나 민트·고등어(청록)처럼 둘 중 하나와 겹치면
    /// 갈라진 색이 몸에 묻혀 안 보인다. 그때만 나머지 두 조합 중 몸색과 더 멀리 떨어진 쪽을 쓴다.
    /// 까망·하양·회색 같은 무채색은 늘 빨강/청록이다.
    /// </summary>
    public static SplitPair PairFor(Color body)
    {
        if (!ColorUtil.IsChromatic(body, 0.25)) return RedCyan;
        double hue = body.ToHsl().H;
        if (Distance(hue, RedCyan) >= PairConflictDegrees) return RedCyan;
        return Distance(hue, MagentaGreen) >= Distance(hue, PurpleYellow) ? MagentaGreen : PurpleYellow;

        static double Distance(double hue, SplitPair p) =>
            Math.Min(ColorUtil.HueDistance(hue, p.A.ToHsl().H), ColorUtil.HueDistance(hue, p.B.ToHsl().H));
    }

    public static Color NoiseColor(int index, SplitPair pair) => index switch { 0 => pair.A, 1 => pair.B, _ => Colors.White };

    /// <summary>
    /// 한 프레임 진행한다. 평소엔 사용률에 따른 확률로 새 글리치를 시작하고,
    /// 터지는 동안엔 대부분 프레임에서 모양을 새로 뽑아 지지직거리게 한다.
    /// </summary>
    public static void Advance(GlitchState state, double seconds, double usagePercent)
    {
        double dt = double.IsNaN(state.LastSeconds) ? 0 : Math.Clamp(seconds - state.LastSeconds, 0, 0.25);
        state.LastSeconds = seconds;
        double u = Math.Clamp(usagePercent / 100, 0, 1);

        bool bursting = seconds < state.BurstUntil;
        if (!bursting)
        {
            state.Shape = null;
            double rate = RateAtIdle + (RateAtFull - RateAtIdle) * u;
            if (Random.Shared.NextDouble() < rate * dt)
            {
                state.BurstUntil = seconds + BurstSeconds * Rand(0.6, 1.4);
                bursting = true;
            }
        }
        if (bursting && (state.Shape is null || Random.Shared.NextDouble() < RerollChance)) state.Shape = Roll(u);
    }

    /// <summary>새 글리치 모양. 사용률이 높을수록(power) 줄이 더 밀리고 색이 더 갈라지고 조각이 많다.</summary>
    private static GlitchShape Roll(double usage)
    {
        double power = 0.45 + 0.55 * usage;
        var bands = new List<GlitchBand>(BandCount);
        for (int i = 0; i < BandCount; i++)
        {
            double height = Rand(0.06, 0.18);
            bands.Add(new GlitchBand(Rand(0, 1 - height), height, Rand(-1, 1) * BandShift * power));
        }
        var noise = new List<GlitchNoise>();
        int count = (int)Math.Round(NoiseCount * power);
        for (int i = 0; i < count; i++)
            noise.Add(new GlitchNoise(Rand(-0.3, 1.2), Rand(0, 1), Rand(0.06, 0.3), Rand(0.03, 0.08), Random.Shared.Next(3)));
        double split = Math.Max(1, SplitDistance * power * Rand(0.6, 1.4));
        return new GlitchShape(bands, split, Random.Shared.Next(2) == 0 ? 1 : -1, noise);
    }

    private static double Rand(double min, double max) => min + Random.Shared.NextDouble() * (max - min);
}
