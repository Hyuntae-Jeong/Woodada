using Avalonia.Media;
using Woodada.Core;
using Woodada.Settings;

namespace Woodada.Sprites;

/// <summary>러너 한 마리의 줄줄이 스킨 진행 상태. 지금 몇 마리를 데리고 있고, 자리마다 얼마나 붙었는지.</summary>
public sealed class KittenState
{
    internal int Shown;
    internal double UpSince, DownSince;
    internal double LastSeconds = double.NaN;
    /// <summary>자리마다 줄에 붙은 정도(0~1). 0이면 안 보이고, 오르는 동안 뒤에서 뛰어와 합류한다.</summary>
    internal readonly double[] Presence = new double[KittenSkin.MaxKittens];
    internal readonly double[] Phase = new double[KittenSkin.MaxKittens];
    /// <summary>지난번에 그 새끼가 있던, 엄마가 지나온 길 위의 자리(엄마의 달린 거리 기준).</summary>
    internal readonly double[] LastAlong = new double[KittenSkin.MaxKittens];
    internal readonly double[] HopSeed = new double[KittenSkin.MaxKittens];

    public KittenState()
    {
        // 새끼마다 걸음·깡총 박자를 어긋나게 해서 넷이 똑같이 움직이지 않게 한다.
        for (int i = 0; i < KittenSkin.MaxKittens; i++)
        {
            Phase[i] = Random.Shared.NextDouble();
            HopSeed[i] = Random.Shared.NextDouble() * 2 * Math.PI;
        }
        Reset();
    }

    /// <summary>처음 놓이거나 다른 모니터로 순간이동했을 때. 첫째만 데리고 다시 시작한다.</summary>
    public void Reset()
    {
        Shown = 1;
        UpSince = DownSince = double.NaN;
        for (int i = 0; i < KittenSkin.MaxKittens; i++)
        {
            Presence[i] = i == 0 ? 1 : 0;
            LastAlong[i] = double.NaN;
        }
    }
}

/// <summary>
/// 줄줄이 스킨: 엄마(러너) 뒤로 작은 새끼들이 엄마가 지나온 길을 그대로 밟으며 따라온다.
/// 화면 끝에서 엄마가 돌아서면 새끼들도 그 자리까지 가서 돌아선다. 사용률이 오르면 새끼가 한 마리씩
/// 뒤에서 뛰어와 합류하고, 내려가면 맨 끝 새끼가 처지며 사라진다. 새끼 색은 엄마 색을 번갈아 밝게·어둡게 한 것.
/// 겉모습만 바뀌고 속도·생선은 그대로다. 값은 tools/skin_preview.py kittens 미리보기로 맞췄다.
/// </summary>
internal static class KittenSkin
{
    public const int MaxKittens = 4;
    /// <summary>새끼 키(엄마 대비).</summary>
    public const double Scale = 0.55;
    /// <summary>사용률이 이만큼(%) 오를 때마다 한 마리씩 는다. 0~20%: 1마리, 20~40%: 2마리 …</summary>
    private const double UsagePerKitten = 20;
    /// <summary>
    /// 줄 때의 문턱 여유(%). 늘 때는 문턱을 넘으면, 줄 때는 이만큼 더 내려가야 준다.
    /// CPU는 늘 출렁여서 여유가 없으면 20% 언저리에서 새끼가 붙었다 떨어졌다를 되풀이한다.
    /// </summary>
    private const double Hysteresis = 5;
    /// <summary>문턱을 이만큼(초) 계속 넘어 있어야 한 마리 늘고, 문턱−여유 아래로 이만큼 있어야 한 마리 준다.</summary>
    private const double JoinDelay = 1.0;
    private const double LeaveDelay = 4.0;
    /// <summary>뒤에서 뛰어와 줄에 붙는 데 걸리는 시간(초). 처지며 사라질 때는 LeaveSlowdown배 천천히.</summary>
    private const double JoinSeconds = 0.5;
    private const double LeaveSlowdown = 1.6;
    /// <summary>몸 사이 간격(새끼 몸 길이 대비).</summary>
    private const double Gap = 0.25;
    /// <summary>
    /// 새끼 다리 빠르기(엄마 대비). 작은 몸 비율대로 놀리면(약 1.8배) 종종거리며 촐싹대 보인다.
    /// </summary>
    private const double Cadence = 1.25;
    /// <summary>가장 빠를 때 깡총 높이(새끼 키 대비). 느릴 때 제자리에서 통통 튀면 촐싹대 보여서 빠를수록만 높게 뛴다.</summary>
    private const double HopHeight = 0.08;
    private const double HopFullUsage = 60;

    private static int CountFor(double usagePercent) =>
        Math.Min(MaxKittens, 1 + (int)Math.Floor(Math.Max(0, usagePercent) / UsagePerKitten));

    /// <summary>마릿수·합류 정도·걸음을 한 프레임 진행한다. 그릴 때마다 한 번 부른다.</summary>
    /// <param name="odometer">엄마가 지금까지 달린 거리.</param>
    public static void Advance(KittenState state, double seconds, double usagePercent, double odometer, double bodyWidth, double kittenWidth)
    {
        double dt = double.IsNaN(state.LastSeconds) ? 0 : Math.Clamp(seconds - state.LastSeconds, 0, 0.25);
        state.LastSeconds = seconds;

        // "잠깐 넘었다"가 아니라 "한동안 넘어 있으면" 한 마리씩 바꾼다.
        if (CountFor(usagePercent) > state.Shown)
        {
            if (double.IsNaN(state.UpSince)) state.UpSince = seconds;
            if (seconds - state.UpSince >= JoinDelay) { state.Shown++; state.UpSince = seconds; }
        }
        else state.UpSince = double.NaN;
        if (CountFor(usagePercent + Hysteresis) < state.Shown)
        {
            if (double.IsNaN(state.DownSince)) state.DownSince = seconds;
            if (seconds - state.DownSince >= LeaveDelay) { state.Shown--; state.DownSince = seconds; }
        }
        else state.DownSince = double.NaN;

        double strides = SpeedProfile.ForBodyWidth(bodyWidth).StridesPerBody;
        for (int i = 0; i < MaxKittens; i++)
        {
            double target = i < state.Shown ? 1 : 0;
            double rate = 1 / (target > 0 ? JoinSeconds : JoinSeconds * LeaveSlowdown);
            // 이미 닿았으면 그대로 둔다. 없으면 1과 그 아래를 매 프레임 오가며 새끼가 앞뒤로 떤다.
            state.Presence[i] = Math.Clamp(state.Presence[i] + Math.Sign(target - state.Presence[i]) * dt * rate, 0, 1);

            // 걸음: 따라간 거리만큼 엄마와 같은 방식으로, Cadence배 빠르게.
            double along = odometer - Distance(i, state.Presence[i], bodyWidth, kittenWidth);
            if (!double.IsNaN(state.LastAlong[i]))
                state.Phase[i] = (state.Phase[i] + Math.Abs(along - state.LastAlong[i]) / Math.Max(1, bodyWidth) * strides * Cadence) % 1.0;
            state.LastAlong[i] = along;
        }
    }

    /// <summary>i번째 새끼가 엄마 뒤 얼마나 떨어져 있는지(지나온 길을 따라 잰 거리). 합류·처지는 동안은 더 멀다.</summary>
    public static double Distance(int index, double presence, double bodyWidth, double kittenWidth) =>
        bodyWidth * (0.5 + Gap) + kittenWidth * (0.5 + index * (1 + Gap)) + (1 - presence) * kittenWidth * 3;

    /// <summary>지금 깡총 뛴 높이(새끼 키 대비). 걸음 한 바퀴에 한 번.</summary>
    public static double Hop(KittenState state, int index, double usagePercent) =>
        HopHeight * Math.Clamp(usagePercent / HopFullUsage, 0, 1) * Math.Max(0, Math.Sin(state.Phase[index] * 2 * Math.PI + state.HopSeed[index]));

    /// <summary>
    /// 새끼 색. 엄마 색을 번갈아 밝게·어둡게 해서 작아도 엄마와 구분되게 한다.
    /// 까망처럼 아주 어두운 엄마는 더 어둡게 할 수 없으니 늘 밝게, 하양처럼 아주 밝으면 늘 어둡게.
    /// </summary>
    public static Color ColorFor(Color mother, int index)
    {
        double lum = ColorUtil.Luminance(mother);
        bool lighter = lum < 0.25 || (lum < 0.8 && index % 2 == 0);
        return lighter
            ? ColorUtil.Mix(mother, Colors.White, 0.22 + 0.06 * index)
            : ColorUtil.Mix(mother, Colors.Black, 0.16 + 0.04 * index);
    }
}
