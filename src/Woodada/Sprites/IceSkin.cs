using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Woodada.Core;

namespace Woodada.Sprites;

/// <summary>발밑에 찍힌 서리 결정 하나. X는 러너 트랙 좌표(State.X와 같은 축)다.</summary>
internal record struct IceMark(double X, double Born, double Life, double Scale, double Spin);

/// <summary>등에서 떨어져 흩날리는 눈가루 하나. Y는 바닥선 기준(위가 음수)이다.</summary>
internal record struct IceFlake(double X, double Y, double Vx, double Vy, double Born, double Life);

/// <summary>러너 한 마리의 얼음 스킨 진행 상태. 바닥에 남은 서리와 흩날리는 눈가루.</summary>
public sealed class IceState
{
    internal readonly List<IceMark> Marks = new();
    internal readonly List<IceFlake> Flakes = new();
    internal double LastSeconds = double.NaN;
    internal double LastPhase = double.NaN;
    internal int Steps;
    internal double FlakeDebt;

    /// <summary>다른 모니터로 옮겨지는 등 순간이동했을 때. 옛 자리의 서리가 새 자리에 남으면 안 된다.</summary>
    public void Clear()
    {
        Marks.Clear();
        Flakes.Clear();
        LastPhase = double.NaN;
    }
}

/// <summary>
/// 얼음 스킨: 위는 밝고 아래로 갈수록 짙은 얼음빛 몸 + 등의 흰 서리선 + 몸을 가로지르는 반짝 줄 두 개.
/// 발이 닿는 곳마다 바닥선에 서리 결정이 찍혔다가 녹아 사라지고, 등에서 눈가루가 뒤로 흩날린다.
/// 빠를수록 서리가 촘촘하고 오래 남고 눈가루가 많다. 색은 얼음빛으로 고정이라 러너에 고른 색은 쓰지 않는다.
/// 겉모습만 바뀌고 속도·생선은 그대로다. 값은 tools/skin_preview.py ice 미리보기로 맞췄다.
/// </summary>
internal static class IceSkin
{
    private static readonly (byte R, byte G, byte B) TopColor = (0xEE, 0xF9, 0xFF);
    private static readonly (byte R, byte G, byte B) BottomColor = (0x8F, 0xCD, 0xF2);
    private static readonly (byte R, byte G, byte B) EyeColor = (0x1D, 0x3B, 0x57);
    private static readonly (byte R, byte G, byte B) OutlineColor = (0x3F, 0x86, 0xBD);

    /// <summary>얼음 몸의 가운데 색. 설정 창 미리보기가 바탕과 대비가 나는지 볼 때 쓴다.</summary>
    public static Color DisplayColor => Color.FromRgb(0xC0, 0xE5, 0xFA);

    /// <summary>등 서리선 진하기.</summary>
    private const double RimOpacity = 0.85;
    /// <summary>반짝 줄 진하기. 줄은 몸 너비 대비 (위 끝, 아래 끝) 자리에 비스듬히 긋는다.</summary>
    private const double GlintOpacity = 0.45;
    private static readonly (double Top, double Bottom)[] Glints = { (0.62, 0.42), (0.74, 0.56) };
    /// <summary>밝은 작업표시줄·독에서만 두르는 바깥 테두리 진하기. 옅은 얼음빛이 흰 바탕에 묻히지 않게.</summary>
    private const double OutlineOpacity = 0.70;

    /// <summary>서리 결정 크기(몸 높이 대비).</summary>
    private const double MarkSize = 0.13;
    /// <summary>서리가 남는 시간(초). 느릴 때 ~ 빠를 때.</summary>
    private const double MarkLifeSlow = 0.9;
    private const double MarkLifeFast = 1.6;
    /// <summary>남는 시간 중 이만큼은 그대로 있다가 녹기 시작한다.</summary>
    private const double MarkHold = 0.45;
    /// <summary>찍히는 순간 톡 커지는 시간(초).</summary>
    private const double MarkPopSeconds = 0.08;
    /// <summary>세기가 이보다 낮으면 두 걸음에 한 번만 찍는다.</summary>
    private const double EveryStepStrength = 0.75;
    /// <summary>가장 셀 때 1초에 흩날리는 눈가루 수.</summary>
    private const double FlakesPerSecond = 30;

    /// <summary>
    /// 느리게 걸을 때도 이만큼(0~1)은 효과가 보이고, FullUsage(%)까지 오르며 다 진해진다.
    /// 플래시와 같은 기준이다. 평소 CPU는 5~15%라 문턱을 두면 스킨이 거의 늘 맨몸으로 보인다.
    /// </summary>
    private const double MinStrength = 0.40;
    private const double FullUsage = 40;

    private static readonly Color MarkOnDark = Color.FromRgb(0xDF, 0xF3, 0xFF);
    private static readonly Color MarkOnLight = Color.FromRgb(0x3F, 0x8F, 0xC8);
    private static readonly Color FlakeOnDark = Colors.White;
    private static readonly Color FlakeOnLight = Color.FromRgb(0x5A, 0xA7, 0xDA);

    /// <summary>MinStrength~1. 효과가 얼마나 보일지.</summary>
    private static double Strength(double usagePercent) =>
        MinStrength + (1 - MinStrength) * Math.Clamp(usagePercent / FullUsage, 0, 1);

    /// <summary>밝은 바탕 테두리를 두를 여백(장치 픽셀).</summary>
    public static int PaddingPixels(double unitPixels) => Math.Max(1, (int)Math.Round(unitPixels));

    // ───────────────────────── 몸 ─────────────────────────

    /// <summary>
    /// 목표 크기로 줄인 몸 실루엣·눈 알파로 얼음 프레임 한 장을 만든다(premultiplied BGRA).
    /// 결과는 사방에 PaddingPixels만큼 커진다.
    /// </summary>
    /// <param name="silhouette">눈 구멍까지 채운 몸 알파 (w×h, 장치 픽셀).</param>
    /// <param name="eyes">눈 구멍 알파 (w×h).</param>
    /// <param name="lightBackground">밝은 작업표시줄·독이면 바깥 테두리를 두른다.</param>
    public static byte[] Compose(byte[] silhouette, byte[] eyes, int w, int h, bool lightBackground, double unitPixels,
        out int width, out int height)
    {
        int p = PaddingPixels(unitPixels), W = w + 2 * p, H = h + 2 * p;
        width = W;
        height = H;
        var body = new float[W * H];
        var eye = new float[W * H];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            body[(y + p) * W + x + p] = silhouette[y * w + x] / 255f;
            eye[(y + p) * W + x + p] = eyes[y * w + x] / 255f;
        }

        // 서리선·반짝 줄 굵기. 기본 크기(28)에서 1픽셀이고 몸이 커지면 같이 굵어진다.
        int px = Math.Max(1, (int)Math.Round(h / 28.0));
        var sides = new[] { (-p, 0), (p, 0), (0, -p), (0, p) };

        var bytes = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int i = y * W + x;
            float r = 0, g = 0, bl = 0, a = 0;

            if (lightBackground)
            {
                // 바깥 테두리: 상하좌우로 p만큼 민 몸의 합집합에서 몸을 뺀 자리.
                float none = 1;
                foreach (var (dx, dy) in sides)
                {
                    int sx = x + dx, sy = y + dy;
                    if (sx >= 0 && sy >= 0 && sx < W && sy < H) none *= 1 - body[sy * W + sx];
                }
                Over(OutlineColor, (float)(OutlineOpacity * (1 - none) * (1 - body[i])));
            }

            if (body[i] > 0)
            {
                // 위에서 아래로 짙어지는 얼음빛
                double t = Math.Clamp((y - p) / Math.Max(1.0, h - 1), 0, 1);
                var c = Mix(TopColor, BottomColor, t);
                // 반짝 줄: 몸을 비스듬히 가로지르는 가는 흰 줄
                foreach (var (top, bottom) in Glints)
                {
                    double lineX = p + w * (top + (bottom - top) * (y - p) / Math.Max(1.0, h));
                    double cover = Math.Clamp(px / 2.0 + 0.5 - Math.Abs(x + 0.5 - lineX), 0, 1);
                    c = Mix(c, (255, 255, 255), GlintOpacity * cover);
                }
                // 등 서리선: 몸인데 px만큼 위는 몸이 아닌 곳 = 윗가장자리
                float above = y - px >= 0 ? body[(y - px) * W + x] : 0;
                c = Mix(c, (255, 255, 255), RimOpacity * (1 - above));
                Over(c, body[i]);
            }
            Over(EyeColor, eye[i]);

            int o = i * 4;
            bytes[o] = ToByte(bl);
            bytes[o + 1] = ToByte(g);
            bytes[o + 2] = ToByte(r);
            bytes[o + 3] = ToByte(a);

            void Over((byte R, byte G, byte B) col, float alpha)
            {
                if (alpha <= 0) return;
                r = col.R / 255f * alpha + r * (1 - alpha);
                g = col.G / 255f * alpha + g * (1 - alpha);
                bl = col.B / 255f * alpha + bl * (1 - alpha);
                a = alpha + a * (1 - alpha);
            }
        }
        return bytes;
    }

    private static (byte R, byte G, byte B) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t) =>
        ((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    // ───────────────────────── 서리 · 눈가루 ─────────────────────────

    /// <summary>발이 닿았으면 서리를 찍고, 눈가루를 흩날리고, 다 녹은 것은 치운다. 그릴 때마다 한 번 부른다.</summary>
    /// <param name="body">몸 크기(그리는 단위).</param>
    public static void Advance(IceState state, double seconds, double usagePercent, RunnerState runner, Size body)
    {
        double dt = double.IsNaN(state.LastSeconds) ? 0 : Math.Clamp(seconds - state.LastSeconds, 0, 0.25);
        state.LastSeconds = seconds;
        double strength = Strength(usagePercent);

        // 걸음 한 바퀴에 발이 두 번 닿는다. 느리면 두 걸음에 한 번만 찍는다.
        double phase = runner.GaitPhase;
        if (!double.IsNaN(state.LastPhase))
        {
            double advance = (phase - state.LastPhase + 1) % 1;
            int steps = (int)Math.Floor((state.LastPhase + advance) * 2) - (int)Math.Floor(state.LastPhase * 2);
            for (int s = 0; s < steps; s++)
            {
                state.Steps++;
                if (strength < EveryStepStrength && state.Steps % 2 != 0) continue;
                // 앞발·뒷발 자리에 따로 찍으면 둘씩 뭉쳐 "✳✳"처럼 보인다. 몸 가운데 한 줄로, 살짝만 흔든다.
                double life = MarkLifeSlow + (MarkLifeFast - MarkLifeSlow) * (strength - MinStrength) / (1 - MinStrength);
                state.Marks.Add(new IceMark(runner.X + body.Width * (0.5 + Rand(-0.06, 0.06)), seconds, life,
                    Rand(0.8, 1.2), Rand(-0.25, 0.25)));
            }
        }
        state.LastPhase = phase;
        state.Marks.RemoveAll(m => seconds - m.Born >= m.Life);

        // 눈가루: 등에서 떨어져 뒤로 흩날리며 바닥에 닿기 전에 사라진다.
        double k = body.Height / 28;
        state.FlakeDebt += FlakesPerSecond * strength * dt;
        while (state.FlakeDebt >= 1)
        {
            state.FlakeDebt--;
            state.Flakes.Add(new IceFlake(
                runner.X + body.Width * Rand(0.25, 0.75), -body.Height * Rand(0.55, 0.95),
                -runner.Direction * Rand(6, 20) * k, Rand(2, 8) * k, seconds, Rand(0.5, 1.1)));
        }
        for (int i = 0; i < state.Flakes.Count; i++)
        {
            var f = state.Flakes[i];
            state.Flakes[i] = f with { X = f.X + f.Vx * dt, Y = f.Y + f.Vy * dt };
        }
        state.Flakes.RemoveAll(f => seconds - f.Born >= f.Life || f.Y >= 0);
    }

    /// <summary>서리와 눈가루를 그린다. 몸보다 먼저 그려서 몸이 위에 오게 한다.</summary>
    /// <param name="originX">트랙 좌표에서 이만큼 뺀 자리에 그린다.</param>
    /// <param name="floorY">발이 닿는 선.</param>
    /// <param name="bodyHeight">몸 높이(그리는 단위).</param>
    public static void DrawFrost(DrawingContext ctx, IceState state, double originX, double floorY, double bodyHeight,
        bool lightBackground, double seconds, double renderScaling)
    {
        double scale = renderScaling > 0 ? renderScaling : 1;
        // 장치 픽셀 하나(논리 단위). 기본 크기에서 1픽셀이고 몸이 커지면 같이 굵어진다.
        double px = Math.Max(1, Math.Round(bodyHeight * scale / 28)) / scale;
        var markColor = lightBackground ? MarkOnLight : MarkOnDark;

        foreach (var m in state.Marks)
        {
            double age = seconds - m.Born, p = age / m.Life;
            if (p < 0 || p >= 1) continue;
            double melt = p < MarkHold ? 0 : (p - MarkHold) / (1 - MarkHold);
            double a = 1 - melt * melt, grow = Math.Min(1, age / MarkPopSeconds);
            double size = bodyHeight * MarkSize * m.Scale * grow * (1 - 0.6 * melt);
            double x = Snap(m.X - originX, scale), y = floorY;

            // 바닥에 얇게 깔린 서리
            ctx.FillRectangle(BrushFor(markColor, 0.55 * a), new Rect(Snap(x - size * 0.9, scale), y - px, Math.Max(px, size * 1.8), px));
            // 결정: 세 줄을 60°씩 엇갈려 그은 ✳
            double r = Math.Max(1.5 * px, size), cy = y - px - r * 0.85;
            var pen = PenFor(markColor, a, px);
            for (int i = 0; i < 3; i++)
            {
                double ang = Math.PI / 2 + i * Math.PI / 3 + m.Spin;
                double dx = Math.Cos(ang) * r, dy = Math.Sin(ang) * r;
                ctx.DrawLine(pen, new Point(x - dx, cy - dy), new Point(x + dx, cy + dy));
            }
            ctx.FillRectangle(BrushFor(lightBackground ? markColor : Colors.White, a), new Rect(x - px / 2, cy - px / 2, px, px));
        }

        var flakeColor = lightBackground ? FlakeOnLight : FlakeOnDark;
        foreach (var f in state.Flakes)
        {
            double a = 1 - (seconds - f.Born) / f.Life;
            if (a <= 0) continue;
            ctx.FillRectangle(BrushFor(flakeColor, a), new Rect(Snap(f.X - originX, scale), Snap(floorY + f.Y, scale), px, px));
        }
    }

    private static double Snap(double v, double scale) => Math.Round(v * scale) / scale;

    // 서리는 녹으며 옅어지므로 진하기를 16단계로 끊어 붓·펜을 돌려 쓴다. 매 프레임 새로 만들지 않게.
    private const int AlphaSteps = 16;
    private static readonly Dictionary<uint, IImmutableBrush> BrushCache = new();
    private static readonly Dictionary<(uint Argb, double Thickness), ImmutablePen> PenCache = new();

    private static Color Quantize(Color c, double alpha) =>
        Color.FromArgb((byte)(Math.Round(Math.Clamp(alpha, 0, 1) * AlphaSteps) * 255 / AlphaSteps), c.R, c.G, c.B);

    private static IImmutableBrush BrushFor(Color c, double alpha)
    {
        var q = Quantize(c, alpha);
        if (!BrushCache.TryGetValue(q.ToUInt32(), out var brush))
            BrushCache[q.ToUInt32()] = brush = new ImmutableSolidColorBrush(q);
        return brush;
    }

    private static ImmutablePen PenFor(Color c, double alpha, double thickness)
    {
        var key = (Quantize(c, alpha).ToUInt32(), thickness);
        if (PenCache.TryGetValue(key, out var pen)) return pen;
        if (PenCache.Count >= 256) PenCache.Clear(); // 크기·배율이 계속 바뀌어도 끝없이 쌓이지 않게
        return PenCache[key] = new ImmutablePen(BrushFor(c, alpha), thickness, lineCap: PenLineCap.Round);
    }

    private static double Rand(double min, double max) => min + (max - min) * Random.Shared.NextDouble();
}
