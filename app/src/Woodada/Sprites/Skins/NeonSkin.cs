// Copyright (c) 2026 Hyuntae Jeong. All rights reserved.
// 이 파일은 Woodada 스페셜 스킨 디자인으로, MIT License 적용 대상이 아닙니다. 사용 조건은 같은 폴더의 LICENSE를 보세요.
// This file is a Woodada special skin design and is NOT covered by the MIT License. See LICENSE in this folder.

using Avalonia;
using Avalonia.Media;
using Woodada.Settings;

namespace Woodada.Sprites;

/// <summary>러너 한 마리의 네온 깜빡임 상태.</summary>
public sealed class NeonState
{
    internal double LastSeconds = double.NaN;
    /// <summary>깜빡이는 중이면 밝기 단계들. 앞에서부터 Until이 지나면 빠진다.</summary>
    internal readonly List<(double Until, double Lit)> Steps = new();

    /// <summary>지금 네온관이 켜진 정도(0~1). 깜빡임과 잔떨림이 들어 있다.</summary>
    public double Lit { get; internal set; } = 1;
}

/// <summary>
/// 네온 스킨: 몸 테두리를 따라 네온관이 빛나고(안쪽에 하얀 심), 몸 안은 고른 색이 어둡게 살짝 비친다.
/// 관 둘레로 빛이 번지고 발밑 작업표시줄에 빛 웅덩이가 생긴다. 가끔 접촉 불량 난 간판처럼 깜빡인다.
/// 사용률이 높을수록 더 환하고 더 자주 깜빡인다. 겉모습만 바뀌고 속도·생선은 그대로다.
/// 값은 tools/skin_preview.py neon 미리보기로 맞췄다. 길이 값은 화면 단위(Windows 물리 픽셀, macOS 포인트)다.
///
/// 그림은 프레임마다 네 층(발광 · 바탕 · 관 · 심)을 미리 만들어 두고, 밝기는 층마다 불투명도로만 바꾼다.
/// 그래서 깜빡임이 아무리 잦아도 그림을 새로 만들지 않는다.
/// </summary>
internal static class NeonSkin
{
    /// <summary>미리 만들어 두는 층.</summary>
    public enum Layer
    {
        /// <summary>관을 흐리게 번진 빛. GlowOpacity × 밝기 × 켜짐으로 찍는다.</summary>
        Glow,
        /// <summary>어둡게 비친 몸 + 꺼진 유리관 + 희미한 눈. 늘 그대로 찍는다.</summary>
        Base,
        /// <summary>켜진 네온관 + 켜진 눈. 켜짐만큼 찍는다.</summary>
        Tube,
        /// <summary>관 안쪽 1단위의 하얀 심.</summary>
        Core,
    }

    /// <summary>몸색에 색감이 없을 때(까망·하양·회색) 쓰는 네온 색.</summary>
    private static readonly Color DefaultNeon = Color.FromRgb(0x3E, 0xF0, 0xFF);

    /// <summary>관 두께(화면 단위). 1이면 몸 테두리 안쪽 1단위가 그대로 관이다.</summary>
    private static readonly int TubeWidth = 1; // 상수로 두면 두께 1일 때 넓히는 갈래가 죽은 코드 경고를 낸다
    /// <summary>하얀 심의 진하기. 사용률에 따라 이 값의 60~100%로 찍는다.</summary>
    private const double CoreOpacity = 0.80;
    /// <summary>몸 안을 고른 색(어둡게)으로 채우는 진하기. 낮을수록 속이 빈 네온 간판처럼 보인다.</summary>
    private const double FillOpacity = 0.10;
    /// <summary>꺼졌을 때도 보이는 눈의 진하기.</summary>
    private const double DimEyeOpacity = 0.35;

    /// <summary>발광 번짐(가우시안 표준편차). CSS blur()의 값과 같은 뜻이다.</summary>
    private const double GlowRadius = 5.0;
    public const double GlowOpacity = 0.80;

    /// <summary>느리게 걸을 때도 이만큼(0~1)은 환하고, FullUsage(%)에서 가장 환하다. 플래시·얼음과 같은 방식이다.</summary>
    private const double MinStrength = 0.40;
    private const double FullUsage = 40;

    /// <summary>발밑 빛 웅덩이 진하기. 어두운 바탕에선 더 진하게, 밝은 바탕에선 옅게.</summary>
    private const double FloorOpacity = 0.45;
    private const double FloorOnDark = 0.55;
    private const double FloorOnLight = 0.30;

    /// <summary>켜진 동안 늘 있는 잔떨림. 밝기가 매 프레임 최대 이만큼 무작위로 떨어진다.</summary>
    private const double Buzz = 0.06;
    /// <summary>1초에 몇 번 깜빡이는지. 사용률 0%와 100% 사이를 잇는다.</summary>
    private const double FlickerAtIdle = 0.08;
    private const double FlickerAtFull = 0.6;
    /// <summary>이만큼(초) 그려지지 않다가 다시 그려지면 새로 입은 것으로 보고 간판 켜지듯 켠다.</summary>
    private const double RebootGap = 0.5;

    private static int PaddingUnits => (int)Math.Ceiling(GlowRadius * 2) + TubeWidth + 1;

    /// <param name="unitPixels">화면 단위 1이 장치 픽셀 몇 개인지(레티나 macOS는 2, Windows는 1).</param>
    public static int PaddingPixels(double unitPixels) => (int)Math.Ceiling(PaddingUnits * unitPixels);

    /// <summary>몸 위로 번지는 발광이 잘리지 않을 여유(화면 단위).</summary>
    public static int Headroom => PaddingUnits;

    /// <summary>
    /// 네온관 색. 몸색에 색감이 있으면 같은 색상(hue)을 채도 최대로 끌어올리고, 무채색이면 청록.
    /// 설정 창이 스킨 칸 바탕을 고를 때도 이 색을 본다.
    /// </summary>
    public static Color TubeColor(Color chosen) =>
        ColorUtil.IsChromatic(chosen, 0.25) ? new HslColor(1, chosen.ToHsl().H, 1, 0.6).ToRgb() : DefaultNeon;

    public static double Strength(double usagePercent) =>
        MinStrength + (1 - MinStrength) * Math.Clamp(usagePercent / FullUsage, 0, 1);

    /// <summary>하얀 심 층을 찍을 불투명도.</summary>
    public static double CoreOpacityFor(double usagePercent, double lit) => lit * CoreOpacity * (0.6 + 0.4 * Strength(usagePercent));

    /// <summary>
    /// 한 프레임 진행한다. 깜빡이는 중이면 단계를 넘기고, 아니면 사용률에 따른 확률로 새 깜빡임을 시작한다.
    /// 한동안 안 그려지다 다시 그려지면(새로 입었거나 창을 다시 열었을 때) 간판 켜지듯 켠다.
    /// </summary>
    public static void Advance(NeonState state, double seconds, double usagePercent)
    {
        double gap = seconds - state.LastSeconds;
        bool reboot = double.IsNaN(state.LastSeconds) || gap > RebootGap || gap < 0;
        double dt = reboot ? 0 : gap;
        state.LastSeconds = seconds;
        if (reboot) StartFlicker(state, seconds, boot: true);

        while (state.Steps.Count > 0 && seconds >= state.Steps[0].Until) state.Steps.RemoveAt(0);
        if (state.Steps.Count == 0)
        {
            double u = Math.Clamp(usagePercent / 100, 0, 1);
            double rate = FlickerAtIdle + (FlickerAtFull - FlickerAtIdle) * u;
            if (Random.Shared.NextDouble() < rate * dt) StartFlicker(state, seconds, boot: false);
        }
        double level = state.Steps.Count > 0 ? state.Steps[0].Lit : 1;
        state.Lit = level * (1 - Buzz * Random.Shared.NextDouble());
    }

    /// <summary>
    /// 깜빡임 한 번 = 꺼졌다 켜졌다 하는 단계 몇 개. 끝은 늘 켜짐(단계가 다 빠지면 1)이다.
    /// boot면 꺼진 채 잠깐 있다가 몇 번 더듬고 켜진다.
    /// </summary>
    private static void StartFlicker(NeonState state, double seconds, bool boot)
    {
        state.Steps.Clear();
        double at = seconds;
        if (boot)
        {
            at += Rand(0.25, 0.4);
            state.Steps.Add((at, 0));
        }
        int count = boot ? 5 + Random.Shared.Next(3) : 2 + Random.Shared.Next(4);
        for (int i = 0; i < count; i++)
        {
            bool off = i % 2 == 0;
            at += off ? Rand(0.03, 0.09) : Rand(0.04, 0.16);
            state.Steps.Add((at, off ? Rand(0, 0.25) : Rand(0.6, 1)));
        }
    }

    // ───────────────────────── 발밑 빛 웅덩이 ─────────────────────────

    /// <summary>
    /// 발밑 작업표시줄 위의 납작한 빛 웅덩이. 몸보다 먼저 그린다.
    /// 붓은 네온 색마다 하나씩만 만들어 두고, 진하기는 불투명도로 바꾼다(매 프레임 붓을 만들면 메모리가 출렁인다).
    /// </summary>
    public static void DrawFloor(DrawingContext ctx, Rect body, Color chosen, double lit, double usagePercent, bool lightBackground)
    {
        double opacity = FloorOpacity * Strength(usagePercent) * lit * (lightBackground ? FloorOnLight : FloorOnDark);
        if (opacity <= 0.01) return;
        double rx = body.Width * 1.1, ry = rx * 0.28;
        double cx = body.Center.X, cy = body.Bottom + 1;
        using var _ = ctx.PushOpacity(opacity);
        ctx.DrawEllipse(FloorBrush(TubeColor(chosen)), null, new Rect(cx - rx, cy - ry, rx * 2, ry * 2));
    }

    private static readonly Dictionary<Color, IBrush> FloorBrushes = new();

    private static IBrush FloorBrush(Color neon)
    {
        if (FloorBrushes.TryGetValue(neon, out var brush)) return brush;
        if (FloorBrushes.Count > 64) FloorBrushes.Clear(); // 나만의 색을 계속 바꿔도 끝없이 늘지 않게
        brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(neon, 0),
                new GradientStop(Color.FromArgb(0, neon.R, neon.G, neon.B), 1),
            },
        }.ToImmutable();
        FloorBrushes[neon] = brush;
        return brush;
    }

    // ───────────────────────── 프레임 그림 ─────────────────────────

    /// <summary>
    /// 목표 크기로 줄인 몸 실루엣·눈 알파로 네온 프레임의 한 층을 만든다(premultiplied BGRA).
    /// 결과는 사방에 PaddingPixels만큼 커진다.
    /// </summary>
    /// <param name="silhouette">눈 구멍까지 채운 몸 알파 (w×h, 장치 픽셀).</param>
    /// <param name="eyes">눈 구멍 알파 (w×h).</param>
    public static byte[] Compose(byte[] silhouette, byte[] eyes, int w, int h, Color chosen, Layer layer, double unitPixels,
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

        // 관: 몸 안쪽 테두리(몸 − 깎은 몸). 두께가 1보다 크면 바깥으로 더 넓힌다.
        // 미리보기의 캔버스 합성과 같은 식이다: 깎기는 밀린 몸들의 곱, 넓히기는 합집합.
        int unit = Math.Max(1, (int)Math.Round(unitPixels));
        var inner = Erode(body, W, H, unit);
        var edge = new float[W * H];
        for (int i = 0; i < edge.Length; i++) edge[i] = body[i] * (1 - inner[i]);
        float[] ring = edge;
        if (TubeWidth > 1)
        {
            var wide = Dilate(body, W, H, (TubeWidth - 1) * unit);
            ring = new float[W * H];
            for (int i = 0; i < ring.Length; i++) ring[i] = wide[i] * (1 - inner[i]);
        }

        var neon = TubeColor(chosen);
        var coreColor = ColorUtil.Mix(neon, Colors.White, 0.7);
        var dimColor = ColorUtil.Mix(neon, Color.FromRgb(0x55, 0x55, 0x60), 0.72);
        var fillColor = ColorUtil.Mix(chosen, Colors.Black, 0.45);

        float[]? glow = null;
        if (layer == Layer.Glow)
        {
            // 넓게 번진 빛 두 번 + 좁게 번진 빛 한 번을 겹친 진하기.
            var wideBlur = Blur(ring, W, H, GlowRadius * unitPixels);
            var tightBlur = Blur(ring, W, H, Math.Max(0.5, GlowRadius * 0.35) * unitPixels);
            glow = new float[W * H];
            for (int i = 0; i < glow.Length; i++)
                glow[i] = 1 - (1 - wideBlur[i]) * (1 - wideBlur[i]) * (1 - tightBlur[i]);
        }

        var bytes = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            float r = 0, g = 0, bl = 0, a = 0;
            switch (layer)
            {
                case Layer.Glow:
                    Over(neon, glow![i]);
                    break;
                case Layer.Base:
                    Over(fillColor, (float)(FillOpacity * body[i]));
                    Over(dimColor, ring[i]);
                    Over(coreColor, (float)(DimEyeOpacity * eye[i]));
                    break;
                case Layer.Tube:
                    Over(neon, ring[i]);
                    Over(coreColor, eye[i]);
                    break;
                case Layer.Core:
                    Over(coreColor, edge[i]);
                    break;
            }

            int o = i * 4;
            bytes[o] = ToByte(bl);
            bytes[o + 1] = ToByte(g);
            bytes[o + 2] = ToByte(r);
            bytes[o + 3] = ToByte(a);

            void Over(Color c, float alpha)
            {
                if (alpha <= 0) return;
                r = c.R / 255f * alpha + r * (1 - alpha);
                g = c.G / 255f * alpha + g * (1 - alpha);
                bl = c.B / 255f * alpha + bl * (1 - alpha);
                a = alpha + a * (1 - alpha);
            }
        }
        return bytes;
    }

    /// <summary>반경 r 안의 정수 오프셋(원판). 미리보기의 disk()와 같다.</summary>
    private static IEnumerable<(int Dx, int Dy)> Disk(int r)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
            if (dx * dx + dy * dy <= r * r + r * 0.5) yield return (dx, dy);
    }

    private static float[] Erode(float[] src, int w, int h, int r)
    {
        var offsets = Disk(r).ToArray();
        var dst = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = 1;
            foreach (var (dx, dy) in offsets)
            {
                int sx = x - dx, sy = y - dy;
                v *= sx >= 0 && sy >= 0 && sx < w && sy < h ? src[sy * w + sx] : 0;
                if (v == 0) break;
            }
            dst[y * w + x] = v;
        }
        return dst;
    }

    private static float[] Dilate(float[] src, int w, int h, int r)
    {
        var offsets = Disk(r).ToArray();
        var dst = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float none = 1;
            foreach (var (dx, dy) in offsets)
            {
                int sx = x - dx, sy = y - dy;
                if (sx >= 0 && sy >= 0 && sx < w && sy < h) none *= 1 - src[sy * w + sx];
            }
            dst[y * w + x] = 1 - none;
        }
        return dst;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    /// <summary>가로·세로로 나눠 거는 가우시안 블러. 바깥은 투명으로 본다.</summary>
    private static float[] Blur(float[] src, int w, int h, double sigma)
    {
        int radius = (int)Math.Ceiling(sigma * 3);
        var kernel = new float[radius * 2 + 1];
        float sum = 0;
        for (int i = -radius; i <= radius; i++) sum += kernel[i + radius] = (float)Math.Exp(-i * i / (2 * sigma * sigma));
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;

        var tmp = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float acc = 0;
            for (int k = -radius; k <= radius; k++)
            {
                int sx = x + k;
                if (sx >= 0 && sx < w) acc += src[y * w + sx] * kernel[k + radius];
            }
            tmp[y * w + x] = acc;
        }

        var dst = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float acc = 0;
            for (int k = -radius; k <= radius; k++)
            {
                int sy = y + k;
                if (sy >= 0 && sy < h) acc += tmp[sy * w + x] * kernel[k + radius];
            }
            dst[y * w + x] = acc;
        }
        return dst;
    }

    private static double Rand(double min, double max) => min + Random.Shared.NextDouble() * (max - min);
}
