namespace Woodada.Sprites;

/// <summary>
/// 유령 스킨: 반투명한 몸 + 옅은 외곽선 + 바깥 발광, 그리고 둥실둥실 떠다니기.
/// 다리 달리기 프레임은 그대로 쓰고, 값은 tools/skin_preview.py ghost 미리보기로 맞췄다.
/// 색은 유령색으로 고정이라 러너에 고른 색은 쓰지 않는다.
/// 길이 값은 화면 단위(Windows 물리 픽셀, macOS 포인트)다.
/// </summary>
internal static class GhostSkin
{
    private static readonly (byte R, byte G, byte B) BodyColor = (0xDD, 0xEB, 0xFF);
    private const double BodyOpacity = 0.45;
    private const double OutlineOpacity = 0.50;
    /// <summary>발광 번짐(가우시안 표준편차). CSS blur()의 값과 같은 뜻이다.</summary>
    private const double GlowRadius = 7.0;
    private const double GlowOpacity = 0.55;

    /// <summary>키 대비 기본으로 떠 있는 높이.</summary>
    private const double FloatHeight = 0.25;
    /// <summary>키 대비 위아래 흔들림. FloatHeight보다 작아서 바닥에 닿지 않고 계속 떠 있다.</summary>
    private const double BobAmplitude = 0.10;
    private const double BobPeriodSeconds = 1.7;

    /// <summary>발광이 몸 밖으로 번지는 만큼 그림 사방에 두는 여백(화면 단위).</summary>
    private static int PaddingUnits => (int)Math.Ceiling(GlowRadius * 2) + 2;

    /// <param name="unitPixels">화면 단위 1이 장치 픽셀 몇 개인지(레티나 macOS는 2, Windows는 1).</param>
    public static int PaddingPixels(double unitPixels) => (int)Math.Ceiling(PaddingUnits * unitPixels);

    public static int Headroom(int height) => (int)Math.Ceiling(height * (FloatHeight + BobAmplitude)) + PaddingUnits;

    public static double Lift(int height, double seconds, double phase) =>
        Math.Max(0, height * (FloatHeight + BobAmplitude * Math.Sin(2 * Math.PI * seconds / BobPeriodSeconds + phase)));

    /// <summary>
    /// 목표 크기로 줄인 몸 실루엣·눈 알파로 유령 프레임 한 장을 만든다(premultiplied BGRA).
    /// 결과는 사방에 PaddingPixels만큼 커진다.
    /// </summary>
    /// <param name="silhouette">눈 구멍까지 채운 몸 알파 (w×h, 장치 픽셀).</param>
    /// <param name="eyes">눈 구멍 알파 (w×h).</param>
    /// <param name="lightBackground">밝은 작업표시줄·독이면 외곽선·발광을 어둡게 해서 배경에 묻히지 않게 한다.</param>
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

        // 외곽선: 몸을 사방으로 (화면 단위 1)만큼 민 모양의 합집합에서 몸을 뺀 바깥 테두리.
        int line = Math.Max(1, (int)Math.Round(unitPixels));
        var ring = new float[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float none = 1;
            for (int dy = -line; dy <= line; dy++)
            for (int dx = -line; dx <= line; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int sx = x - dx, sy = y - dy;
                if (sx >= 0 && sy >= 0 && sx < W && sy < H) none *= 1 - body[sy * W + sx];
            }
            int i = y * W + x;
            ring[i] = (1 - none) * (1 - body[i]);
        }

        // 발광: 몸을 흐리게 번지게 해서 두 번 겹친 진하기로, 몸 안쪽은 비우고 바깥에만 남긴다.
        var blurred = Blur(body, W, H, GlowRadius * unitPixels);
        var glow = new float[W * H];
        for (int i = 0; i < glow.Length; i++)
        {
            float b = blurred[i];
            glow[i] = (1 - (1 - b) * (1 - b)) * (1 - body[i]);
        }

        var lineColor = lightBackground ? Mix(BodyColor, (0x3C, 0x4A, 0x66), 0.65) : Mix(BodyColor, (255, 255, 255), 0.5);
        var glowColor = lightBackground ? Mix(BodyColor, (0x6F, 0x8F, 0xD0), 0.55) : BodyColor;
        // FrameSprite의 색칠과 같은 규칙: 밝은 몸이면 눈을 어둡게.
        var eyeColor = Luminance(BodyColor) > 0.7 ? ((byte)45, (byte)42, (byte)48) : ((byte)255, (byte)255, (byte)255);

        // premultiplied 색으로 아래층부터 차례로 덮는다: 발광 → 외곽선 → 반투명 몸 → 불투명 눈.
        var bytes = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            float r = 0, g = 0, bl = 0, a = 0;
            Over(glowColor, (float)(GlowOpacity * glow[i]));
            Over(lineColor, (float)(OutlineOpacity * ring[i]));
            Over(BodyColor, (float)(BodyOpacity * body[i]));
            Over(eyeColor, eye[i]);

            int o = i * 4;
            bytes[o] = ToByte(bl);
            bytes[o + 1] = ToByte(g);
            bytes[o + 2] = ToByte(r);
            bytes[o + 3] = ToByte(a);

            void Over((byte R, byte G, byte B) c, float alpha)
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

    private static (byte R, byte G, byte B) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t) =>
        ((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static double Luminance((byte R, byte G, byte B) c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

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
}
