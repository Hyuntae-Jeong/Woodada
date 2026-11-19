// Copyright (c) 2026 Hyuntae Jeong. All rights reserved.
// 이 파일은 Woodada 스페셜 스킨 디자인으로, MIT License 적용 대상이 아닙니다. 사용 조건은 같은 폴더의 LICENSE를 보세요.
// This file is a Woodada special skin design and is NOT covered by the MIT License. See LICENSE in this folder.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Woodada.Core;

namespace Woodada.Sprites;

/// <summary>다꾸냥이 스티커를 붙이는 빠르기와 남는 시간. 설정 창 미리보기·스킨 칸은 폭이 좁아서 따로 정한다.</summary>
/// <param name="SlowInterval">가장 느릴 때 붙이는 간격(초).</param>
/// <param name="FastInterval">가장 빠를 때 붙이는 간격(초).</param>
/// <param name="Gap">큰 스티커끼리 가운데 사이 간격(몸 너비).</param>
/// <param name="Life">붙은 뒤 떨어지기 시작할 때까지(초).</param>
/// <param name="FirstWait">입은 뒤 첫 스티커까지(초). 0.5~1.5배로 흔든다.</param>
/// <param name="MaxBehind">고양이 뒤로 이만큼(몸 너비)보다 멀어진 스티커는 바로 떨어진다. 제자리 달리기는 길이 끝없이 흘러가서 쓴다.</param>
public sealed record DakuTuning(double SlowInterval, double FastInterval, double Gap, double Life, double FirstWait,
    double MaxBehind = double.PositiveInfinity)
{
    public static readonly DakuTuning Overlay = new(12, 3, 3, 60, 2);
    /// <summary>설정 창 미리보기. 폭이 몸 6개 남짓이라 금방 차니 자주 붙이고 일찍 뗀다.</summary>
    public static readonly DakuTuning Preview = new(5, 1.5, 3, 20, 0.8);
    /// <summary>스킨 칸·도감·뽑기 무대의 제자리 달리기. 길이 칸 밖으로 금방 흘러가서 촘촘히 붙인다.</summary>
    public static readonly DakuTuning Treadmill = new(1.2, 0.5, 1.6, 6, 0.3, MaxBehind: 1.5);
}

/// <summary>
/// 붙은 스티커 한 장. 좌표는 트랙(러너 State.X와 같은 화면 좌표)이고, Y는 바닥에서 가운데까지 높이다.
/// 모양·색·기울기는 붙일 때 정하고, 그림은 그릴 배율이 정해진 뒤에 만든다(<see cref="DakuSkin.DrawSticker"/>).
/// </summary>
public sealed class DakuSticker
{
    internal double X, Y;
    /// <summary>스티커 키(트랙 단위). 그림판은 이보다 넉넉하다.</summary>
    internal double Size;
    /// <summary>붙인 고양이 키(트랙 단위). 흰 테두리 굵기와 떨어지는 거리를 이것으로 잰다.</summary>
    internal double BodyHeight;
    internal int Kind;
    internal bool Main;
    /// <summary>작은 스티커가 몇 번째로 붙었는지. 붙고 떨어질 때 하나씩 뒤따른다.</summary>
    internal int Order;
    internal double Tilt, Spin;
    internal bool Flip;
    internal Color Color;
    internal double RainbowHue;
    internal RunnerInstance? Owner;

    internal double Born, PopAt;
    internal double GoneAt = double.NaN;
    internal double PeelAngle = double.NaN;

    internal bool Gone => !double.IsNaN(GoneAt);

    // 그린 그림. 배율이 바뀌면(다른 모니터) 다시 만든다.
    internal Bitmap? Image, Back, Shade;
    internal int ImagePixels;
    internal int Border;
}

/// <summary>
/// 다꾸냥들이 붙인 스티커. 오버레이·설정 창 미리보기·제자리 달리기가 저마다 하나씩 갖고, 러너들이 움직인 바로 뒤에 Step을 부른다.
/// 다꾸냥이 두 마리면 같은 판에 붙이고, 서로 간격을 지킨다.
/// </summary>
public sealed class DakuBoard
{
    /// <summary>빈자리가 없으면 이만큼 뒤에 다시 본다(초).</summary>
    private const double Retry = 0.5;

    private readonly List<DakuSticker> _stickers = new();
    private readonly Dictionary<RunnerInstance, double> _next = new();
    /// <summary>고양이마다 지난번 키. 바뀌면 그 고양이 스티커를 뗀다.</summary>
    private readonly Dictionary<RunnerInstance, double> _heights = new();
    private readonly List<RunnerInstance> _players = new();

    public DakuTuning Tuning { get; set; } = DakuTuning.Overlay;

    /// <summary>스티커 시계(초). 붙고 떨어지는 연출을 이것으로 잰다.</summary>
    public double Time { get; private set; }

    public IReadOnlyList<DakuSticker> Stickers => _stickers;

    /// <summary>
    /// 스티커가 바닥에서 가장 높이 닿는 곳(트랙 단위). 고양이를 작게 줄이면 오버레이 띠도 낮아지는데,
    /// 큰 고양이가 붙여 둔 스티커가 다 떨어질 때까지는 띠를 이만큼 남겨 둬야 떨어지는 모습이 잘리지 않는다.
    /// 붙는 순간 톡 커지는 것과 떨어질 때 튀어 오르는 것(키의 1/4)까지 넉넉히 셈한다.
    /// </summary>
    public double Reach
    {
        get
        {
            double reach = 0;
            foreach (var k in _stickers) reach = Math.Max(reach, k.Y + k.Size * 0.75 + k.BodyHeight * 0.3);
            return reach;
        }
    }

    public void Step(double dt, IReadOnlyList<RunnerInstance> runners, Runway runway)
    {
        Time += dt;
        _players.Clear();
        foreach (var r in runners)
            if (r.Skin == RunnerSkin.Daku) _players.Add(r);

        // 다른 스킨으로 갈아입었거나 꺼진 고양이: 그 고양이가 붙인 스티커가 우수수 떨어진다.
        foreach (var r in _next.Keys.ToList())
        {
            if (_players.Contains(r)) continue;
            _next.Remove(r);
            _heights.Remove(r);
            PeelAll(r);
        }
        foreach (var r in _players)
        {
            if (!_next.ContainsKey(r)) _next[r] = Time + Tuning.FirstWait * Jitter(0.5, 1.5);
            // 크기를 바꿔도 우수수 뗀다. 큰 고양이가 키 높이까지 붙인 스티커는 작아진 고양이의 오버레이 띠 위로 삐져나와 잘린다.
            if (_heights.TryGetValue(r, out double h) && Math.Abs(h - r.Height) > 0.01) PeelAll(r);
            _heights[r] = r.Height;
        }

        // 다른 모니터로 옮겨졌으면 옛 자리 스티커는 버린다.
        if (runway.Width <= 0) _stickers.Clear();
        else _stickers.RemoveAll(k => k.X < runway.Left - k.Size || k.X > runway.Right + k.Size);

        foreach (var r in _players)
        {
            if (Time < _next[r]) continue;
            if (runway.Width > 0 && TryPlace(r, runway))
            {
                // 두 마리면 각자 두 배 간격으로 붙인다. 안 그러면 사용률이 조금만 올라도 자리가 꽉 차서 더 바빠져도 똑같아 보인다.
                double interval = DakuSkin.Lerp(Tuning.SlowInterval, Tuning.FastInterval, DakuSkin.PowerOf(r)) * _players.Count;
                _next[r] = Time + interval * Jitter(0.7, 1.3);
            }
            else _next[r] = Time + Retry;
        }

        foreach (var k in _stickers)
        {
            if (k.Gone) continue;
            bool expired = Time - k.Born >= Tuning.Life;
            // 제자리 달리기: 고양이 뒤로 멀어진 스티커는 칸 밖으로 나가기 전에 떨어진다.
            if (!expired && k.Main && k.Owner is { } o && !double.IsPositiveInfinity(Tuning.MaxBehind))
            {
                double w = o.BodySize.Width;
                expired = Math.Abs(o.State.X + w / 2 - k.X) > Tuning.MaxBehind * w && o.State.Direction * (k.X - o.State.X) < 0;
            }
            if (!expired) continue;
            k.GoneAt = Time;
            // 작은 것은 제 큰 스티커를 조금씩 뒤따라 떨어진다.
            if (k.Main)
                foreach (var s in _stickers)
                    if (!s.Main && !s.Gone && s.Born == k.Born && s.Owner == k.Owner) s.GoneAt = Time + 0.12 * s.Order;
        }
        _stickers.RemoveAll(k => k.Gone && Time - k.GoneAt >= DakuSkin.VanishSeconds);
    }

    private static double Jitter(double a, double b) => a + Random.Shared.NextDouble() * (b - a);

    /// <summary>이 고양이가 붙인 스티커를 모두, 조금씩 어긋나게 떨어뜨린다.</summary>
    private void PeelAll(RunnerInstance owner)
    {
        foreach (var k in _stickers)
            if (k.Owner == owner && !k.Gone) k.GoneAt = Time + Random.Shared.NextDouble() * 0.5;
    }

    /// <summary>방금 지나온 자리에 큰 스티커 한 장과, 그 둘레에 작은 스티커 0~3장을 붙인다. 빈자리가 없으면 false.</summary>
    private bool TryPlace(RunnerInstance cat, Runway runway)
    {
        double w = cat.BodySize.Width, h = cat.Height;
        double s = h * Jitter(DakuSkin.MainMin, DakuSkin.MainMax);
        double x = cat.State.X + w / 2 - cat.State.Direction * w * 0.7;
        if (x < runway.Left + s * 0.6 || x > runway.Right - s * 0.6) return false;
        foreach (var k in _stickers)
            if (k.Main && Math.Abs(k.X - x) < Tuning.Gap * w) return false;

        // 높이: 고양이 키 안에서 아무 데나. 스티커 자체 키만큼은 안쪽으로.
        var main = NewSticker(cat, Random.Shared.Next(DakuSkin.MainKinds), s, main: true);
        main.X = x;
        main.Y = Jitter(s * 0.5, Math.Max(s * 0.5, h - s * 0.5));
        main.PopAt = Time;
        _stickers.Add(main);
        ScatterSmall(cat, main, runway);
        return true;
    }

    /// <summary>
    /// 큰 스티커 둘레에 작은 스티커 0~최대 장. 작은 스티커는 혼자 붙지 않는다(따로 있으면 지저분해 보인다).
    /// 제 큰 스티커 가장자리에는 걸쳐도 되고, 나중에 붙어서 그 위에 올라온다. 작은 것끼리·다른 무리와는 안 겹친다.
    /// 고양이 키 안에만, 하나씩 톡톡 뒤따라 붙는다.
    /// </summary>
    private void ScatterSmall(RunnerInstance cat, DakuSticker main, Runway runway)
    {
        double h = cat.Height;
        int want = Random.Shared.Next(DakuSkin.SmallCount + 1), placed = 0;
        for (int tries = 0; tries < want * 30 && placed < want; tries++)
        {
            double s = h * Jitter(DakuSkin.SmallMin, DakuSkin.SmallLargest), r = s * 0.5, mainR = main.Size * 0.5;
            // 큰 스티커 가장자리 안쪽(반지름의 60%)부터 키의 Spread만큼 떨어진 곳까지. 안쪽이면 가장자리에 걸쳐 붙는다.
            // 높이는 고양이 키 안이라 좁으니 옆으로 더 퍼뜨리고, 거리는 √로 뽑아 넓은 쪽에도 고르게 흩어지게 한다.
            double a = Jitter(0, Math.PI * 2);
            double d = DakuSkin.Lerp(mainR * 0.6, mainR + r + h * DakuSkin.Spread, Math.Sqrt(Random.Shared.NextDouble()));
            double x = main.X + Math.Cos(a) * d * 1.6, y = main.Y - Math.Sin(a) * d;
            if (x < runway.Left + r || x > runway.Right - r || y < r || y > h - r) continue;
            bool crowded = false;
            foreach (var k in _stickers)
                if (k != main && Math.Sqrt((k.X - x) * (k.X - x) + (k.Y - y) * (k.Y - y)) < k.Size * 0.5 + r + h * 0.12)
                {
                    crowded = true;
                    break;
                }
            if (crowded) continue;
            placed++;
            var small = NewSticker(cat, DakuSkin.MainKinds + Random.Shared.Next(DakuSkin.SmallKinds), s, main: false);
            small.X = x;
            small.Y = y;
            small.Born = main.Born;
            small.PopAt = Time + 0.09 * placed;
            small.Order = placed;
            _stickers.Add(small);
        }
    }

    private DakuSticker NewSticker(RunnerInstance cat, int kind, double size, bool main) => new()
    {
        Kind = kind,
        Size = size,
        BodyHeight = cat.Height,
        Main = main,
        Owner = cat,
        Born = Time,
        Tilt = Jitter(-DakuSkin.MaxTilt, DakuSkin.MaxTilt) * Math.PI / 180,
        Spin = Jitter(-1, 1),
        Flip = Random.Shared.Next(2) == 0,
        Color = DakuSkin.Pastel(Jitter(0, 360)),
        RainbowHue = Jitter(0, 360),
    };
}

/// <summary>
/// 다꾸냥: 고양이가 흰 테두리를 두른 스티커 모습으로 달리고, 지나간 자리에 가끔 스티커를 붙여 작업표시줄 위를 다이어리처럼 꾸민다.
/// 빨리 달릴수록 자주 붙이고, 큰 스티커끼리는 몸 몇 개만큼 떨어져서 빈자리가 없으면 안 붙인다. 스티커는 1분쯤 남았다가
/// 귀퉁이부터 들리며 떨어지고, 다른 스킨을 입거나 크기를 바꾸면 우수수 다 떨어진다. 몸색은 고른 색 그대로고 속도·생선도 그대로다.
/// 값은 tools/skin_preview.py daku 미리보기 기본값 그대로다.
/// </summary>
internal static class DakuSkin
{
    /// <summary>큰 스티커 13종: 모양 8 + RunCat 자세 5.</summary>
    public const int MainKinds = 13;
    /// <summary>작은 스티커 8종(반짝이·점·작은 하트·작은 별·음표·초승달·물방울·클로버).</summary>
    public const int SmallKinds = 8;
    private const int CatPoseFirst = 8;

    /// <summary>큰 스티커 키(고양이 키 대비).</summary>
    public const double MainMin = 0.45, MainMax = 0.85;
    /// <summary>작은 스티커 키(고양이 키 대비)와, 큰 스티커 하나에 붙는 최대 장 수.</summary>
    public const double SmallMin = 0.22, SmallLargest = 0.40;
    public const int SmallCount = 3;
    /// <summary>작은 스티커가 큰 스티커 바깥으로 퍼지는 범위(고양이 키 대비).</summary>
    public const double Spread = 0.30;
    /// <summary>기울기 최대(도).</summary>
    public const double MaxTilt = 20;
    /// <summary>흰 테두리 굵기(고양이 키 대비)와 그림자 진하기.</summary>
    private const double Outline = 0.07;
    private const double ShadowOpacity = 0.30;

    /// <summary>붙이는 순간 톡 눌리는 시간(초).</summary>
    private const double PopSeconds = 0.22;
    /// <summary>떨어지는 시간(초). 귀퉁이부터 접히는 동안(Fold)과 툭 떨어져 사라지는 동안(Drop)으로 나눈다.</summary>
    private const double PeelSeconds = 1.1, Fold = 0.65, Drop = 0.55;
    public const double VanishSeconds = PeelSeconds * (Fold + Drop);

    private static readonly Color BackPaper = Color.FromRgb(0xF3, 0xF1, 0xEE);

    public static double Lerp(double a, double b, double k) => a + (b - a) * k;

    /// <summary>달리는 세기: 가장 느릴 때 0, 가장 빠를 때 1(사용률^0.7과 같다).</summary>
    public static double PowerOf(RunnerInstance r)
    {
        double w = Math.Max(1, r.BodySize.Width);
        return Math.Clamp((r.State.Speed / w - 0.4) / 6.6, 0, 1);
    }

    /// <summary>오버레이 띠에서 몸 위로 남겨 둘 여유. 키 꼭대기에 붙은 스티커가 톡 커지거나 떨어지며 튀어도 보이게.</summary>
    public static int Headroom(int height) => (int)Math.Ceiling(height * 0.45) + 2;

    /// <summary>파스텔: 색상은 아무거나, 채도·밝기는 높게. 한 화면에 이것저것 섞이는 게 더 다꾸 같다.</summary>
    public static Color Pastel(double hue) =>
        FromHsl(hue, Lerp(0.62, 0.85, Random.Shared.NextDouble()), Lerp(0.76, 0.84, Random.Shared.NextDouble()));

    private static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        var (r, g, b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
        static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
    }

    // ───────────────────────── 몸 ─────────────────────────

    /// <summary>키(장치 픽셀)에 맞는 흰 테두리 굵기.</summary>
    private static int BorderFor(double bodyPixels) => Math.Max(1, (int)Math.Round(bodyPixels * Outline));

    private static (int Offset, double Sigma) ShadowFor(int border) =>
        (Math.Max(1, (int)Math.Round(border * 0.7)), Math.Max(0.5, border * 0.6));

    /// <summary>몸 프레임이 사방으로 커지는 만큼(장치 픽셀). 흰 테두리 + 아래로 뜬 그림자.</summary>
    public static int BodyPadding(int pixelHeight)
    {
        int b = BorderFor(pixelHeight);
        var (offset, sigma) = ShadowFor(b);
        return b + offset + (int)Math.Ceiling(sigma * 2) + 1;
    }

    /// <summary>
    /// 고른 색으로 칠한 몸(premultiplied BGRA, w×h)을 흰 테두리·그림자를 두른 스티커로 만든다. 결과는 사방에 BodyPadding만큼 크다.
    /// </summary>
    public static byte[] ComposeBody(byte[] tinted, int w, int h, out int width, out int height)
    {
        int p = BodyPadding(h), W = w + 2 * p, H = h + 2 * p;
        width = W;
        height = H;
        var art = new byte[W * H * 4];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(tinted, y * w * 4, art, ((y + p) * W + p) * 4, w * 4);
        return Stickerize(art, W, H, BorderFor(h), out _);
    }

    /// <summary>
    /// art(투명 바탕 위 그림, 둘레에 여백이 있어야 함)를 스티커로 감싼다: 모양 둘레를 흰 테두리로 넓히고, 아래로 살짝 뜬 그림자를 깐다.
    /// whiteAlpha에는 흰 종이 모양(떨어질 때 뒷면)을 돌려준다.
    /// </summary>
    private static byte[] Stickerize(byte[] art, int W, int H, int border, out float[] whiteAlpha)
    {
        var src = new float[W * H];
        for (int i = 0; i < src.Length; i++) src[i] = art[i * 4 + 3] / 255f;

        // 흰 테두리: 동그란 붓(반지름 border)으로 민 모양들을 겹친다.
        var white = new float[W * H];
        var keep = new float[W * H];
        Array.Fill(keep, 1f);
        int r = border;
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dy * dy > r * r + r * 0.5) continue;
            for (int y = Math.Max(0, dy); y < Math.Min(H, H + dy); y++)
            {
                int sy = y - dy;
                for (int x = Math.Max(0, dx); x < Math.Min(W, W + dx); x++)
                {
                    float a = src[sy * W + x - dx];
                    if (a > 0) keep[y * W + x] *= 1 - a;
                }
            }
        }
        for (int i = 0; i < white.Length; i++) white[i] = 1 - keep[i];
        whiteAlpha = white;

        // 그림자: 흰 모양을 아래로 조금 내려 흐리게.
        var (offset, sigma) = ShadowFor(border);
        var shadow = new float[W * H];
        for (int y = offset; y < H; y++)
            Array.Copy(white, (y - offset) * W, shadow, y * W, W);
        shadow = Blur(shadow, W, H, sigma);

        var bytes = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            int o = i * 4;
            // 그림자(검정) → 흰 종이 → 그림 순으로 겹친다. 모두 premultiplied.
            float a = (float)(ShadowOpacity * shadow[i]), cb = 0, cg = 0, cr = 0;
            float wa = white[i];
            cb = 255 * wa + cb * (1 - wa);
            cg = 255 * wa + cg * (1 - wa);
            cr = 255 * wa + cr * (1 - wa);
            a = wa + a * (1 - wa);
            float aa = art[o + 3] / 255f;
            cb = art[o] + cb * (1 - aa);
            cg = art[o + 1] + cg * (1 - aa);
            cr = art[o + 2] + cr * (1 - aa);
            a = aa + a * (1 - aa);
            bytes[o] = ToByte(cb);
            bytes[o + 1] = ToByte(cg);
            bytes[o + 2] = ToByte(cr);
            bytes[o + 3] = ToByte(a * 255);
        }
        return bytes;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    /// <summary>가우스 흐림(가로·세로 따로).</summary>
    private static float[] Blur(float[] src, int w, int h, double sigma)
    {
        int radius = Math.Max(1, (int)Math.Ceiling(sigma * 2));
        var kernel = new float[radius * 2 + 1];
        float sum = 0;
        for (int i = -radius; i <= radius; i++) sum += kernel[i + radius] = (float)Math.Exp(-i * i / (2 * sigma * sigma));
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;

        var tmp = new float[w * h];
        var dst = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = 0;
            for (int k = -radius; k <= radius; k++)
            {
                int sx = x + k;
                if (sx >= 0 && sx < w) v += src[y * w + sx] * kernel[k + radius];
            }
            tmp[y * w + x] = v;
        }
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = 0;
            for (int k = -radius; k <= radius; k++)
            {
                int sy = y + k;
                if (sy >= 0 && sy < h) v += tmp[sy * w + x] * kernel[k + radius];
            }
            dst[y * w + x] = v;
        }
        return dst;
    }

    // ───────────────────────── 스티커 그림 ─────────────────────────

    /// <summary>스티커 그림을 이 크기(장치 픽셀)로 만들어 둔다. 크기가 같으면 그대로 쓴다.</summary>
    private static void EnsureImages(DakuSticker k, int pixels)
    {
        if (k.Image is not null && k.ImagePixels == pixels) return;
        double s = pixels, bodyPixels = s * k.BodyHeight / Math.Max(1e-6, k.Size);
        // 작은 스티커는 흰 테두리도 크기에 맞춰 얇게. 안 그러면 흰 덩어리만 보인다.
        int b = Math.Max(1, (int)Math.Round(BorderFor(bodyPixels) * Math.Clamp(s / (bodyPixels * 0.6), 0.5, 1)));
        var (offset, sigma) = ShadowFor(b);
        int pad = (int)Math.Ceiling(s * 0.9 + b * 2 + 3 + offset + sigma * 2), S = pad * 2;

        var info = new SKImageInfo(S, S, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = new SKBitmap(info);
        using (var canvas = new SKCanvas(surface))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(S / 2f, S / 2f);
            canvas.RotateRadians((float)k.Tilt);
            if (k.Kind >= CatPoseFirst && k.Kind < MainKinds) DrawCatPose(canvas, k, s);
            else new Shapes(canvas).Draw(k.Kind, (float)s, k.Color, k.RainbowHue);
        }
        var art = surface.GetPixelSpan().ToArray();
        var bytes = Stickerize(art, S, S, b, out var white);

        var back = new byte[S * S * 4];
        var shade = new byte[S * S * 4];
        for (int i = 0; i < white.Length; i++)
        {
            float a = white[i];
            int o = i * 4;
            back[o] = ToByte(BackPaper.B * a);
            back[o + 1] = ToByte(BackPaper.G * a);
            back[o + 2] = ToByte(BackPaper.R * a);
            back[o + 3] = shade[o + 3] = ToByte(255 * a);
        }
        k.Image = FromPremultiplied(bytes, S, S);
        k.Back = FromPremultiplied(back, S, S);
        k.Shade = FromPremultiplied(shade, S, S);
        k.ImagePixels = pixels;
        k.Border = b;
    }

    /// <summary>RunCat 자세 스티커: 파스텔 몸 + 눈. 키를 s로 맞추면 고양이는 옆으로 길어서 조금 줄인다.</summary>
    private static void DrawCatPose(SKCanvas canvas, DakuSticker k, double s)
    {
        if (SpriteRegistry.Get("cat") is not FrameSprite cat) return;
        int h = Math.Max(2, (int)Math.Round(s * 0.85));
        int w = Math.Max(2, (int)Math.Round(cat.Measure(h).Width));
        var pixels = cat.TintedPixels(k.Kind - CatPoseFirst, k.Color, w, h);
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        Marshal.Copy(pixels, 0, bmp.GetPixels(), pixels.Length);
        if (k.Flip) canvas.Scale(-1, 1);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(bmp, -w / 2f, -h / 2f, paint);
    }

    private static Bitmap FromPremultiplied(byte[] bytes, int w, int h)
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(),
                new PixelSize(w, h), new Vector(96, 96), w * 4);
        }
        finally
        {
            handle.Free();
        }
    }

    // ───────────────────────── 그리기 ─────────────────────────

    /// <summary>스티커 자리(트랙 좌표 → 그릴 좌표).</summary>
    public static Rect BoxFor(DakuSticker k, double originX, double floorY) =>
        new(k.X - k.Size / 2 - originX, floorY - k.Y - k.Size / 2, k.Size, k.Size);

    /// <summary>스티커 한 장. 붙는 순간 톡 눌리고, 떨어질 때 귀퉁이부터 접혀 넘어간 뒤 툭 떨어진다.</summary>
    public static void DrawSticker(DrawingContext ctx, Rect box, DakuSticker k, double seconds, double renderScaling)
    {
        if (seconds < k.PopAt) return;
        double scale = renderScaling > 0 ? renderScaling : 1;
        EnsureImages(k, Math.Max(2, (int)Math.Round(box.Width * scale)));
        var img = k.Image!;
        double w = img.PixelSize.Width / scale, h = img.PixelSize.Height / scale;
        // 가만히 붙어 있을 때 흐리지 않게 픽셀 격자에 맞춘다.
        var c = new Point(Math.Round((box.Center.X - w / 2) * scale) / scale + w / 2, Math.Round((box.Center.Y - h / 2) * scale) / scale + h / 2);
        var local = new Rect(-w / 2, -h / 2, w, h);

        if (k.Gone && seconds >= k.GoneAt)
        {
            DrawPeeling(ctx, k, local, c, box.Width / k.Size, seconds - k.GoneAt, scale);
            return;
        }

        double p = Math.Min(1, (seconds - k.PopAt) / PopSeconds);
        double sx = p < 0.5 ? 1.25 - 0.13 * (p / 0.5) : 1.12 - 0.12 * ((p - 0.5) / 0.5);
        double sy = p < 0.5 ? 1.25 - 0.4 * (p / 0.5) : 0.85 + 0.15 * ((p - 0.5) / 0.5);
        double alpha = Math.Min(1, p * 4);
        using (ctx.PushTransform(Matrix.CreateScale(sx, sy) * Matrix.CreateTranslation(c.X, c.Y)))
        using (ctx.PushOpacity(alpha))
        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = p >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
            ctx.DrawImage(img, local);
    }

    private static double Ease(double q) => q * q * (3 - 2 * q);

    /// <param name="unit">트랙 단위 1이 그릴 좌표로 얼마인지.</param>
    private static void DrawPeeling(DrawingContext ctx, DakuSticker k, Rect local, Point c, double unit, double q, double scale)
    {
        // 어느 귀퉁이부터 들릴지는 스티커마다 다르게(처음 떨어질 때 한 번 정한다).
        if (double.IsNaN(k.PeelAngle)) k.PeelAngle = Random.Shared.NextDouble() * Math.PI * 2;
        double ux = Math.Cos(k.PeelAngle), uy = Math.Sin(k.PeelAngle);
        double R = k.Size * unit / 2 + (k.Border + 1) / scale;
        double fold = PeelSeconds * Fold, drop = PeelSeconds * Drop;
        double f = Ease(Math.Min(1, q / fold));
        double d = R * (1 - 1.55 * f);   // 가장자리에서 시작해 반 넘게 접힌다

        var m = Matrix.Identity;
        double opacity = 1;
        if (q > fold)
        {
            // 툭 떨어진다: 접힌 쪽으로 살짝 튀었다가 돌면서 아래로, 옅어지며.
            double g = Math.Min(1, (q - fold) / drop), body = k.BodyHeight * unit;
            m = Matrix.CreateScale(1 - 0.25 * g, 1 - 0.25 * g) * Matrix.CreateRotation(k.Spin * 2.2 * g)
                * Matrix.CreateTranslation(ux * body * 0.15 * g, -body * 0.25 * g + body * 1.6 * g * g);
            opacity = 1 - g;
        }
        using (ctx.PushTransform(m * Matrix.CreateTranslation(c.X, c.Y)))
        using (ctx.PushOpacity(opacity))
            DrawFolded(ctx, k, local, ux, uy, d);
    }

    /// <summary>선(p·u = d) 너머를 접어 넘긴 스티커. 평평한 쪽은 그대로, 넘어온 쪽은 선에 비친 자리에 뒷면(흰 종이)으로.</summary>
    private static void DrawFolded(DrawingContext ctx, DakuSticker k, Rect local, double ux, double uy, double d)
    {
        double far = Math.Max(local.Width, local.Height) * 3;
        using (ctx.PushGeometryClip(Strip(ux, uy, d - far, d, far)))
            ctx.DrawImage(k.Image!, local);

        // 넘어온 뒷면: 원래 접힌 쪽(p·u > d)의 뒷면을 선에 대해 뒤집어 평평한 쪽 위에 얹는다.
        var reflect = new Matrix(1 - 2 * ux * ux, -2 * ux * uy, -2 * ux * uy, 1 - 2 * uy * uy, 2 * d * ux, 2 * d * uy);
        using (ctx.PushGeometryClip(Strip(ux, uy, d - far, d, far)))
        using (ctx.PushTransform(reflect))
        using (ctx.PushGeometryClip(Strip(ux, uy, d, d + far, far)))
        {
            ctx.DrawImage(k.Back!, local);
            // 접힌 선 쪽이 살짝 어둡게: 들려서 그림자가 진 것처럼. 선에서 멀어질수록 옅어지는 띠 네 개로 칠한다.
            double band = local.Width / 4 / 4;
            for (int i = 0; i < 4; i++)
                using (ctx.PushGeometryClip(Strip(ux, uy, d + band * i, d + band * (i + 1), far)))
                using (ctx.PushOpacity(0.18 * (1 - (i + 0.5) / 4)))
                    ctx.DrawImage(k.Shade!, local);
        }
    }

    /// <summary>u 방향으로 from~to 사이, 옆으로는 ±far까지 덮는 띠.</summary>
    private static StreamGeometry Strip(double ux, double uy, double from, double to, double far)
    {
        double vx = -uy, vy = ux;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(ux * from + vx * far, uy * from + vy * far), true);
            g.LineTo(new Point(ux * from - vx * far, uy * from - vy * far));
            g.LineTo(new Point(ux * to - vx * far, uy * to - vy * far));
            g.LineTo(new Point(ux * to + vx * far, uy * to + vy * far));
            g.EndFigure(true);
        }
        return geo;
    }

    // ───────────────────────── 모양 ─────────────────────────

    /// <summary>스티커 모양을 그린다. 가운데가 (0,0)이고 키가 s다. 미리보기의 canvas 그리기를 그대로 옮겼다.</summary>
    private sealed class Shapes(SKCanvas canvas)
    {
        private SKPath _path = new();
        private SKColor _fill, _stroke;
        private float _width;

        public void Draw(int kind, float s, Color color, double rainbowHue)
        {
            var dark = Mix(color, 0.25);
            _fill = ToSk(color);
            _stroke = ToSk(dark);
            _width = Math.Max(1, s / 16);
            switch (kind)
            {
                case 0: Star(s); break;
                case 1: Heart(s); break;
                case 2: Flower(s); break;
                case 3: Cloud(s); break;
                case 4: Ribbon(s); break;
                case 5: Strawberry(s); break;
                case 6: Paw(s); break;
                case 7: Rainbow(s, rainbowHue); break;
                case MainKinds + 0: Sparkle(s); break;
                case MainKinds + 1: Begin(); Arc(0, 0, s * 0.42f, 0, Tau); FillStroke(); break;
                case MainKinds + 2: Heart(s * 1.1f); break;
                case MainKinds + 3: Star(s); break;
                case MainKinds + 4: Note(s, color); break;
                case MainKinds + 5: Moon(s); break;
                case MainKinds + 6: DropShape(s); break;
                case MainKinds + 7: Clover(s); break;
            }
            _path.Dispose();
        }

        private const float Tau = MathF.PI * 2, Pi = MathF.PI;

        private void Star(float s)
        {
            Begin();
            for (int i = 0; i < 10; i++)
            {
                float r = i % 2 == 1 ? s * 0.25f : s * 0.52f, a = -Pi / 2 + i * Pi / 5;
                LineTo(MathF.Cos(a) * r, MathF.Sin(a) * r + s * 0.04f);
            }
            _path.Close();
            FillStroke();
        }

        private void Heart(float s)
        {
            Begin();
            _path.MoveTo(0, s * 0.42f);
            _path.CubicTo(-s * 0.62f, s * 0.02f, -s * 0.5f, -s * 0.5f, 0, -s * 0.2f);
            _path.CubicTo(s * 0.5f, -s * 0.5f, s * 0.62f, s * 0.02f, 0, s * 0.42f);
            FillStroke();
        }

        private void Flower(float s)
        {
            for (int i = 0; i < 5; i++)
            {
                float a = -Pi / 2 + i * Tau / 5;
                Begin(); Arc(MathF.Cos(a) * s * 0.26f, MathF.Sin(a) * s * 0.26f, s * 0.21f, 0, Tau); FillStroke();
            }
            Begin(); Arc(0, 0, s * 0.15f, 0, Tau);
            Fill(new SKColor(0xFF, 0xF2, 0xA6));
            Stroke(_stroke, _width);
        }

        private void Cloud(float s)
        {
            Begin();
            Arc(-s * 0.26f, s * 0.08f, s * 0.2f, Pi * 0.5f, Pi * 1.5f);
            Arc(-s * 0.04f, -s * 0.08f, s * 0.26f, Pi * 1.05f, Pi * 1.85f);
            Arc(s * 0.27f, s * 0.06f, s * 0.2f, Pi * 1.4f, Pi * 0.5f);
            _path.Close();
            FillStroke();
        }

        private void Ribbon(float s)
        {
            foreach (int side in new[] { -1, 1 })
            {
                Begin();
                _path.MoveTo(0, 0);
                _path.QuadTo(side * s * 0.3f, -s * 0.42f, side * s * 0.52f, -s * 0.22f);
                _path.QuadTo(side * s * 0.6f, s * 0.02f, side * s * 0.5f, s * 0.2f);
                _path.QuadTo(side * s * 0.3f, s * 0.3f, 0, 0);
                FillStroke();
                Begin();
                _path.MoveTo(side * s * 0.05f, s * 0.06f);
                _path.LineTo(side * s * 0.26f, s * 0.46f);
                _path.LineTo(side * s * 0.12f, s * 0.42f);
                _path.LineTo(side * s * 0.02f, s * 0.5f);
                _path.Close();
                FillStroke();
            }
            Begin(); Arc(0, 0, s * 0.11f, 0, Tau); FillStroke();
        }

        private void Strawberry(float s)
        {
            Begin();
            _path.MoveTo(0, s * 0.48f);
            _path.QuadTo(-s * 0.5f, s * 0.1f, -s * 0.36f, -s * 0.18f);
            _path.QuadTo(0, -s * 0.34f, s * 0.36f, -s * 0.18f);
            _path.QuadTo(s * 0.5f, s * 0.1f, 0, s * 0.48f);
            FillStroke();
            foreach (var (px, py) in new[] { (-0.16f, -0.04f), (0.14f, -0.06f), (0f, 0.1f), (-0.12f, 0.22f), (0.12f, 0.2f), (0f, 0.34f) })
            {
                Begin(); Ellipse(px * s, py * s, Math.Max(0.6f, s * 0.035f), Math.Max(0.8f, s * 0.05f), 0);
                Fill(SKColors.White);
            }
            Begin();
            for (int i = 0; i < 10; i++)
            {
                float r = i % 2 == 1 ? s * 0.08f : s * 0.24f, a = Pi + i * Pi / 9;
                LineTo(MathF.Cos(a) * r, -s * 0.24f + MathF.Sin(a) * r * 0.55f);
            }
            _path.Close();
            Fill(new SKColor(0x9F, 0xD9, 0x9B));
            Stroke(new SKColor(0x5F, 0x9E, 0x5B), _width);
        }

        private void Paw(float s)
        {
            Begin(); Ellipse(0, s * 0.18f, s * 0.27f, s * 0.21f, 0); FillStroke();
            foreach (var (px, py, a) in new[] { (-0.33f, -0.08f, -0.4f), (-0.12f, -0.3f, -0.12f), (0.12f, -0.3f, 0.12f), (0.33f, -0.08f, 0.4f) })
            {
                Begin(); Ellipse(px * s, py * s, s * 0.11f, s * 0.14f, a); FillStroke();
            }
        }

        private void Rainbow(float s, double hue)
        {
            const int bands = 4;
            float outer = s * 0.56f, th = s * 0.1f, cy = s * 0.26f;
            for (int i = 0; i < bands; i++)
            {
                float r = outer - th * (i + 0.5f);
                Begin(); Arc(0, cy, r, Pi, Tau, moveTo: true);
                Stroke(ToSk(Pastel(hue + i * 55)), th + 0.6f, SKStrokeCap.Butt);
            }
            Begin(); Arc(0, cy, outer, Pi, Tau, moveTo: true); Stroke(_stroke, _width);
            Begin(); Arc(0, cy, outer - th * bands, Pi, Tau, moveTo: true); Stroke(_stroke, _width);
            Begin();
            _path.MoveTo(-outer, cy); _path.LineTo(-outer + th * bands, cy);
            _path.MoveTo(outer, cy); _path.LineTo(outer - th * bands, cy);
            Stroke(_stroke, _width);
        }

        /// <summary>반짝이: 네 끝이 뾰족하고 옆구리가 오목하다.</summary>
        private void Sparkle(float s)
        {
            float R = s * 0.52f, r = s * 0.12f;
            Begin();
            _path.MoveTo(0, -R);
            for (int i = 0; i < 4; i++)
            {
                float a = -Pi / 2 + i * Pi / 2, b = a + Pi / 4, c = a + Pi / 2;
                _path.QuadTo(MathF.Cos(b) * r, MathF.Sin(b) * r, MathF.Cos(c) * R, MathF.Sin(c) * R);
            }
            _path.Close();
            FillStroke();
        }

        /// <summary>음표: 기운 동그란 머리 + 기둥 + 깃발.</summary>
        private void Note(float s, Color color)
        {
            Begin(); Ellipse(-s * 0.14f, s * 0.3f, s * 0.18f, s * 0.13f, -0.4f); FillStroke();
            Begin();
            _path.MoveTo(s * 0.03f, s * 0.28f);
            _path.LineTo(s * 0.03f, -s * 0.44f);
            _path.QuadTo(s * 0.22f, -s * 0.3f, s * 0.32f, -s * 0.12f);
            Stroke(ToSk(color), Math.Max(1, s * 0.09f));
        }

        /// <summary>초승달: 바깥 원호와 안쪽 원호를 이어 닫는다.</summary>
        private void Moon(float s)
        {
            float R = s * 0.46f;
            Begin();
            Arc(0, 0, R, Pi * 0.35f, Pi * 1.65f);
            Arc(R * 0.42f, -R * 0.08f, R * 0.8f, Pi * 1.45f, Pi * 0.55f, ccw: true);
            _path.Close();
            FillStroke();
        }

        private void DropShape(float s)
        {
            Begin();
            _path.MoveTo(0, -s * 0.48f);
            _path.CubicTo(s * 0.18f, -s * 0.2f, s * 0.36f, 0, s * 0.36f, s * 0.16f);
            Arc(0, s * 0.16f, s * 0.36f, 0, Pi);
            _path.CubicTo(-s * 0.36f, 0, -s * 0.18f, -s * 0.2f, 0, -s * 0.48f);
            FillStroke();
        }

        private void Clover(float s)
        {
            for (int i = 0; i < 4; i++)
            {
                float a = Pi / 4 + i * Pi / 2;
                Begin(); Arc(MathF.Cos(a) * s * 0.19f, MathF.Sin(a) * s * 0.19f - s * 0.04f, s * 0.19f, 0, Tau); FillStroke();
            }
            Begin();
            _path.MoveTo(0, s * 0.1f);
            _path.QuadTo(s * 0.06f, s * 0.32f, s * 0.2f, s * 0.46f);
            Stroke(_stroke, _width);
        }

        // canvas 2D의 beginPath·lineTo·arc·ellipse를 SKPath로 흉내 낸다.

        private void Begin()
        {
            _path.Dispose();
            _path = new SKPath();
        }

        private void LineTo(float x, float y)
        {
            if (_path.IsEmpty) _path.MoveTo(x, y);
            else _path.LineTo(x, y);
        }

        /// <summary>canvas arc와 같다: 지금 점에서 호의 시작점까지 선을 잇고, 시계(ccw면 반시계) 방향으로 돈다.</summary>
        private void Arc(float cx, float cy, float r, float a0, float a1, bool ccw = false, bool moveTo = false)
        {
            float sweep;
            if (!ccw)
            {
                sweep = a1 - a0;
                if (sweep >= Tau) sweep = Tau;
                else { sweep %= Tau; if (sweep < 0) sweep += Tau; }
            }
            else
            {
                sweep = a0 - a1;
                if (sweep >= Tau) sweep = Tau;
                else { sweep %= Tau; if (sweep < 0) sweep += Tau; }
                sweep = -sweep;
            }
            var oval = new SKRect(cx - r, cy - r, cx + r, cy + r);
            if (Math.Abs(sweep) >= Tau - 1e-4)
            {
                // 한 바퀴는 ArcTo가 못 그려서 반 바퀴씩 두 번.
                _path.ArcTo(oval, a0 * 180 / Pi, 180 * Math.Sign(sweep), moveTo || _path.IsEmpty);
                _path.ArcTo(oval, a0 * 180 / Pi + 180 * Math.Sign(sweep), 180 * Math.Sign(sweep), false);
                return;
            }
            _path.ArcTo(oval, a0 * 180 / Pi, sweep * 180 / Pi, moveTo || _path.IsEmpty);
        }

        private void Ellipse(float cx, float cy, float rx, float ry, float rotation)
        {
            using var oval = new SKPath();
            oval.AddOval(new SKRect(-rx, -ry, rx, ry));
            var m = SKMatrix.CreateRotation(rotation).PostConcat(SKMatrix.CreateTranslation(cx, cy));
            _path.AddPath(oval, ref m);
        }

        private void FillStroke()
        {
            Fill(_fill);
            Stroke(_stroke, _width);
        }

        private void Fill(SKColor color)
        {
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };
            canvas.DrawPath(_path, paint);
        }

        private void Stroke(SKColor color, float width, SKStrokeCap cap = SKStrokeCap.Round)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = width,
                StrokeCap = cap, StrokeJoin = SKStrokeJoin.Round,
            };
            canvas.DrawPath(_path, paint);
        }

        private static Color Mix(Color c, double toBlack) =>
            Color.FromRgb((byte)(c.R * (1 - toBlack)), (byte)(c.G * (1 - toBlack)), (byte)(c.B * (1 - toBlack)));

        private static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);
    }
}
