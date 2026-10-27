using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Woodada.Settings;

namespace Woodada.Sprites;

/// <summary>
/// 검은 실루엣 PNG 프레임으로 달리는 러너 (RunCat 고양이, 닥스훈트 …).
/// 알파만 마스크로 쓰고 색은 설정값으로 칠한다. 프레임은 오른쪽을 보고, 눈은 머리 쪽의 투명 구멍이어야 한다.
/// 프레임은 (색, 장치 픽셀 높이)별로 미리 축소해 캐시해서 매 프레임 스케일링 비용이 들지 않는다.
/// 왼쪽을 볼 때는 그릴 때 좌우만 뒤집는다.
/// </summary>
public sealed class FrameSprite : IRunnerSprite
{
    // 설정 창의 색 칸(20개)과 미리보기, 플래시 잔상 색(러너마다 8개), 그리고 개발용 빌드의 스킨 칸·뽑기 무대까지
    // 함께 그려도 넘치지 않게. 넘치면 통째로 비우고 다시 칠하므로, 모자라면 매 프레임 그림을 새로 만든다.
    private const int MaxCacheEntries = 160;

    /// <summary>프레임을 어떤 모습으로 칠했는지.</summary>
    private enum FrameLook
    {
        /// <summary>러너 색 + 눈.</summary>
        Tinted,
        /// <summary>유령 스킨(색 고정, 사방에 발광 여백).</summary>
        Ghost,
        /// <summary>눈 없이 한 색으로 칠한 몸 모양. 잔상에 쓴다.</summary>
        Silhouette,
    }

    private readonly record struct FrameKey(FrameLook Look, uint Argb, int Height, bool LightBack, double UnitPixels);

    private readonly FrameMask[] _masks;
    private readonly PixelSize _crop;
    private readonly Dictionary<FrameKey, Bitmap[]> _cache = new();

    /// <param name="resourcePrefix">임베디드 리소스 이름 앞부분. 뒤에 "{i}.png"가 붙는다.</param>
    public FrameSprite(string id, string displayName, string resourcePrefix, int frameCount)
    {
        Id = id;
        DisplayName = displayName;
        var raw = Enumerable.Range(0, frameCount).Select(i => AlphaImage.Load($"{resourcePrefix}{i}.png")).ToArray();

        // 프레임마다 실루엣 범위가 달라서, 전 프레임 합집합으로 잘라야 발 위치(바닥선)가 흔들리지 않는다.
        PixelRect? union = null;
        foreach (var f in raw)
        {
            var b = f.OpaqueBounds();
            union = union is { } u ? u.Union(b) : b;
        }
        var crop = union ?? new PixelRect(0, 0, 1, 1);
        _crop = crop.Size;
        _masks = raw.Select(f => FrameMask.Build(f, crop)).ToArray();
    }

    public string Id { get; }
    public string DisplayName { get; }

    public Size Measure(double height) => new(height * _crop.Width / _crop.Height, height);

    public void Draw(DrawingContext ctx, Rect box, RunnerPose pose, Color color, double renderScaling) =>
        Draw(ctx, box, pose, color, renderScaling, RunnerSkin.None, lightBackground: false);

    public void Draw(DrawingContext ctx, Rect box, RunnerPose pose, Color color, double renderScaling, RunnerSkin skin, bool lightBackground)
    {
        double scale = renderScaling > 0 ? renderScaling : 1;
        int pixelHeight = Math.Max(4, (int)Math.Round(box.Height * scale));
        // 스킨 효과(외곽선 굵기·발광 번짐)는 화면 단위로 정해 둔 값이다. Windows는 화면 단위가 장치 픽셀이고,
        // macOS는 포인트라 레티나에서 장치 픽셀 2개가 된다.
        double unitPixels = OperatingSystem.IsMacOS() ? scale : 1;
        var key = skin == RunnerSkin.Ghost
            ? new FrameKey(FrameLook.Ghost, 0, pixelHeight, lightBackground, unitPixels) // 유령은 색이 고정이다
            : new FrameKey(FrameLook.Tinted, color.ToUInt32(), pixelHeight, false, 1);
        // 스킨 프레임은 발광이 번질 자리만큼 사방이 크다. 몸이 box에 오도록 그만큼 당겨 찍는다.
        double pad = skin == RunnerSkin.Ghost ? GhostSkin.PaddingPixels(unitPixels) / scale : 0;
        Blit(ctx, PickFrame(GetFrames(key), pose), box, pose.Facing, scale, pad);
    }

    /// <summary>눈 없이 한 색으로 칠한 몸 모양을 opacity만큼 비치게 그린다. 플래시 스킨의 잔상이다.</summary>
    public void DrawSilhouette(DrawingContext ctx, Rect box, RunnerPose pose, Color color, double opacity, double renderScaling)
    {
        if (opacity <= 0) return;
        double scale = renderScaling > 0 ? renderScaling : 1;
        int pixelHeight = Math.Max(4, (int)Math.Round(box.Height * scale));
        var bmp = PickFrame(GetFrames(new FrameKey(FrameLook.Silhouette, color.ToUInt32(), pixelHeight, false, 1)), pose);
        using var _ = ctx.PushOpacity(Math.Min(1, opacity));
        Blit(ctx, bmp, box, pose.Facing, scale, 0);
    }

    public void DrawGlitch(DrawingContext ctx, Rect box, RunnerPose pose, Color color, double renderScaling, GlitchShape? shape)
    {
        double scale = renderScaling > 0 ? renderScaling : 1;
        int pixelHeight = Math.Max(4, (int)Math.Round(box.Height * scale));
        // 갈라짐 거리는 화면 단위라 레티나 macOS에서는 장치 픽셀 2개가 된다(유령 외곽선과 같은 규칙).
        double unitPixels = OperatingSystem.IsMacOS() ? scale : 1;
        var body = PickFrame(GetFrames(new FrameKey(FrameLook.Tinted, color.ToUInt32(), pixelHeight, false, 1)), pose);
        // 갈라짐 색은 몸색과 겹치지 않는 조합으로 고른다(체리·민트 같은 몸색은 빨강/청록이 묻힌다).
        var pair = GlitchSkin.PairFor(color);
        var a = PickFrame(GetFrames(new FrameKey(FrameLook.Silhouette, pair.A.ToUInt32(), pixelHeight, false, 1)), pose);
        var b = PickFrame(GetFrames(new FrameKey(FrameLook.Silhouette, pair.B.ToUInt32(), pixelHeight, false, 1)), pose);

        double px = 1 / scale; // 장치 픽셀 하나(논리 단위)
        double w = body.PixelSize.Width * px, h = body.PixelSize.Height * px;
        double x = Math.Round(box.X * scale) / scale;
        double y = Math.Round((box.Bottom - h) * scale) / scale;

        using var _ = ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None });
        if (pose.Facing < 0)
        {
            // 원본은 오른쪽을 본다. 갈라짐·줄 밀림·노이즈까지 통째로 좌우만 뒤집는다.
            using (ctx.PushTransform(Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(2 * x + w, 0)))
                DrawGlitchFacingRight(ctx, body, a, b, pair, x, y, w, h, px, unitPixels, shape);
        }
        else
        {
            DrawGlitchFacingRight(ctx, body, a, b, pair, x, y, w, h, px, unitPixels, shape);
        }
    }

    private static void DrawGlitchFacingRight(DrawingContext ctx, Bitmap body, Bitmap a, Bitmap b, GlitchSkin.SplitPair pair,
        double x, double y, double w, double h, double px, double unitPixels, GlitchShape? shape)
    {
        if (shape is null)
        {
            // 평소: 가장자리에 아주 옅게 색이 번져 있다.
            double fringe = Math.Max(1, Math.Round(unitPixels)) * px;
            using (ctx.PushOpacity(GlitchSkin.IdleFringeOpacity))
            {
                DrawSliced(ctx, b, x + fringe, y, px, null);
                DrawSliced(ctx, a, x - fringe, y, px, null);
            }
            DrawSliced(ctx, body, x, y, px, null);
            return;
        }

        double split = Math.Max(1, Math.Round(shape.Split * unitPixels)) * px * shape.SplitSide;
        using (ctx.PushOpacity(GlitchSkin.SplitOpacity))
        {
            DrawSliced(ctx, b, x + split, y, px, shape.Bands);
            DrawSliced(ctx, a, x - split, y, px, shape.Bands);
        }
        DrawSliced(ctx, body, x, y, px, shape.Bands);

        foreach (var n in shape.Noise)
        {
            // 픽셀 격자에 맞춰야 번지지 않고 또렷한 조각으로 보인다.
            double nx = x + Math.Round(n.X * w / px) * px, ny = y + Math.Round(n.Y * h / px) * px;
            double nw = Math.Max(2, Math.Round(n.Width * w / px)) * px, nh = Math.Max(1, Math.Round(n.Height * h / px)) * px;
            ctx.FillRectangle(GlitchSkin.NoiseBrush(n.ColorIndex, pair), new Rect(nx, ny, nw, nh));
        }
    }

    /// <summary>그림을 가로 줄로 나눠, bands에 든 줄은 옆으로 밀어 찍는다. bands가 없으면 통째로 찍는다.</summary>
    private static void DrawSliced(DrawingContext ctx, Bitmap bmp, double x, double y, double px, IReadOnlyList<GlitchBand>? bands)
    {
        int width = bmp.PixelSize.Width, height = bmp.PixelSize.Height;
        if (bands is null || bands.Count == 0)
        {
            ctx.DrawImage(bmp, new Rect(0, 0, width, height), new Rect(x, y, width * px, height * px));
            return;
        }

        // 줄 경계마다 자르고, 한 조각씩 그 조각이 속한 줄만큼 밀어 찍는다.
        var spans = bands.Select(band =>
        {
            int top = Math.Clamp((int)Math.Round(band.Top * height), 0, height);
            int bottom = Math.Clamp(top + Math.Max(1, (int)Math.Round(band.Height * height)), 0, height);
            return (Top: top, Bottom: bottom, Dx: Math.Round(band.Shift * width) * px);
        }).ToList();
        var cuts = new SortedSet<int> { 0, height };
        foreach (var s in spans) { cuts.Add(s.Top); cuts.Add(s.Bottom); }
        int prev = -1;
        foreach (int cut in cuts)
        {
            if (prev >= 0 && cut > prev)
            {
                int y0 = prev;
                double dx = spans.FirstOrDefault(s => y0 >= s.Top && y0 < s.Bottom).Dx;
                ctx.DrawImage(bmp, new Rect(0, y0, width, cut - y0), new Rect(x + dx, y + y0 * px, width * px, (cut - y0) * px));
            }
            prev = cut;
        }
    }

    private static Bitmap PickFrame(Bitmap[] frames, RunnerPose pose)
    {
        int count = frames.Length;
        int index = (int)(pose.GaitPhase * count) % count;
        if (index < 0) index += count;
        return frames[index];
    }

    /// <param name="pad">그림이 몸보다 사방으로 큰 만큼(논리 단위).</param>
    private static void Blit(DrawingContext ctx, Bitmap bmp, Rect box, int facing, double scale, double pad)
    {
        // 이미 장치 픽셀 크기로 만들어 둔 그림이라 픽셀 격자에 맞춰 1:1로 찍기만 한다.
        double w = bmp.PixelSize.Width / scale, h = bmp.PixelSize.Height / scale;
        double x = Math.Round(box.X * scale) / scale - pad;
        double y = Math.Round((box.Bottom + pad - h) * scale) / scale;
        var dest = new Rect(x, y, w, h);
        var src = new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);

        using var _ = ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None });
        if (facing < 0)
        {
            // 원본은 오른쪽을 본다. dest 안에서 좌우만 뒤집는다.
            using (ctx.PushTransform(Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(2 * x + w, 0)))
                ctx.DrawImage(bmp, src, dest);
        }
        else
        {
            ctx.DrawImage(bmp, src, dest);
        }
    }

    private Bitmap[] GetFrames(FrameKey key)
    {
        if (_cache.TryGetValue(key, out var frames)) return frames;

        // 그리는 중인 화면이 들고 있을 수 있으니 Dispose하지 않고 놓아 주기만 한다.
        if (_cache.Count >= MaxCacheEntries) _cache.Clear();

        int height = key.Height;
        int width = Math.Max(1, (int)Math.Round(height * _crop.Width / (double)_crop.Height));
        var color = Color.FromUInt32(key.Argb);
        frames = _masks.Select(m => key.Look switch
        {
            FrameLook.Ghost => m.RenderGhost(width, height, key.LightBack, key.UnitPixels),
            FrameLook.Silhouette => m.RenderSilhouette(color, width, height),
            _ => m.Render(color, width, height),
        }).ToArray();
        _cache[key] = frames;
        return frames;
    }

    /// <summary>PNG 한 장의 알파 채널(행 우선).</summary>
    private sealed class AlphaImage(int width, int height, byte[] alpha)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;

        public byte Alpha(int x, int y) => alpha[y * Width + x];

        public PixelRect OpaqueBounds()
        {
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (Alpha(x, y) <= 8) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            return maxX < 0 ? new PixelRect(0, 0, Width, Height) : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        public static AlphaImage Load(string resourceName)
        {
            using var stream = typeof(FrameSprite).Assembly.GetManifestResourceStream(resourceName)
                               ?? throw new InvalidOperationException($"리소스 없음: {resourceName}");
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException($"PNG를 읽을 수 없음: {resourceName}");
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var bmp = SKBitmap.Decode(codec, info);
            var pixels = bmp.GetPixelSpan();
            var alpha = new byte[info.Width * info.Height];
            for (int y = 0; y < info.Height; y++)
            for (int x = 0; x < info.Width; x++)
                alpha[y * info.Width + x] = pixels[y * bmp.RowBytes + x * 4 + 3];
            return new AlphaImage(info.Width, info.Height, alpha);
        }
    }

    /// <summary>잘라낸 프레임 하나의 몸통 알파와 눈(실루엣 안의 구멍) 마스크.</summary>
    private sealed class FrameMask
    {
        private int _width, _height;
        private byte[] _bodyAlpha = [];
        private bool[] _eye = [];

        public static FrameMask Build(AlphaImage src, PixelRect crop)
        {
            int w = crop.Width, h = crop.Height;
            var alpha = new byte[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                alpha[y * w + x] = src.Alpha(crop.X + x, crop.Y + y);

            return new FrameMask { _width = w, _height = h, _bodyAlpha = alpha, _eye = FindEyes(alpha, w, h) };
        }

        /// <summary>
        /// 원본 눈은 투명 구멍이라 색을 칠하면 배경이 비쳐 눈이 사라진다.
        /// 테두리에서 닿지 않는 작은 투명 영역 중 머리 쪽(오른쪽)에 있는 것을 눈으로 보고 채운다.
        /// </summary>
        private static bool[] FindEyes(byte[] alpha, int w, int h)
        {
            var visited = new bool[w * h];
            var eye = new bool[w * h];
            var queue = new Queue<int>();
            var component = new List<int>();

            for (int start = 0; start < w * h; start++)
            {
                if (visited[start] || alpha[start] >= 128) continue;
                component.Clear();
                bool touchesBorder = false;
                visited[start] = true;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    component.Add(i);
                    int x = i % w, y = i / w;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) touchesBorder = true;
                    Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
                }

                if (touchesBorder || component.Count > w * h / 100) continue;
                double cx = component.Average(i => i % w);
                if (cx < w * 0.55) continue; // 꼬리와 몸 사이에 생긴 틈은 눈이 아니다
                foreach (int i in component) eye[i] = true;
            }

            // 구멍 가장자리의 반투명 픽셀까지 1px 넓혀서 눈 테두리가 비치지 않게 한다.
            var grown = (bool[])eye.Clone();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!eye[y * w + x]) continue;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h) grown[ny * w + nx] = true;
                }
            }
            return grown;

            void Visit(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                int i = y * w + x;
                if (visited[i] || alpha[i] >= 128) return;
                visited[i] = true;
                queue.Enqueue(i);
            }
        }

        /// <summary>색을 칠한 원본 크기 프레임을 목표 크기로 줄여 화면에 바로 찍을 수 있는 그림으로 만든다.</summary>
        public Bitmap Render(Color color, int width, int height) => Scale(Tint(color), width, height);

        /// <summary>눈 구멍까지 메운 몸 모양을 한 색으로 칠한다. 잔상은 눈이 비치면 지저분해서 모양만 남긴다.</summary>
        public Bitmap RenderSilhouette(Color color, int width, int height)
        {
            var bytes = new byte[_width * _height * 4];
            for (int i = 0; i < _bodyAlpha.Length; i++)
            {
                int o = i * 4;
                bytes[o] = color.B;
                bytes[o + 1] = color.G;
                bytes[o + 2] = color.R;
                bytes[o + 3] = _eye[i] ? (byte)255 : _bodyAlpha[i];
            }
            return Scale(bytes, width, height);
        }

        /// <summary>원본 크기 BGRA(premultiply 전)를 목표 크기로 줄여 화면에 바로 찍을 수 있는 그림으로 만든다.</summary>
        private Bitmap Scale(byte[] bgra, int width, int height)
        {
            using var full = new SKBitmap(new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            Marshal.Copy(bgra, 0, full.GetPixels(), _width * _height * 4);

            using var scaled = full.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.High);
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, scaled.GetPixels(),
                new PixelSize(width, height), new Vector(96, 96), scaled.RowBytes);
        }

        /// <summary>유령 스킨 프레임. 몸 모양과 눈 구멍을 먼저 목표 크기로 줄여야 외곽선이 화면에서 늘 같은 굵기가 된다.</summary>
        public Bitmap RenderGhost(int width, int height, bool lightBackground, double unitPixels)
        {
            var silhouette = ScaledAlpha(i => _eye[i] ? (byte)255 : _bodyAlpha[i], width, height);
            var eyes = ScaledAlpha(i => _eye[i] ? (byte)(255 - _bodyAlpha[i]) : (byte)0, width, height);
            var bytes = GhostSkin.Compose(silhouette, eyes, width, height, lightBackground, unitPixels, out int w, out int h);
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

        /// <summary>알파만 있는 흰 그림을 원본 크기로 만들어 목표 크기로 줄인 뒤 알파만 읽는다.</summary>
        private byte[] ScaledAlpha(Func<int, byte> alpha, int width, int height)
        {
            var bytes = new byte[_width * _height * 4];
            for (int i = 0; i < _bodyAlpha.Length; i++)
            {
                int o = i * 4;
                bytes[o] = bytes[o + 1] = bytes[o + 2] = 255;
                bytes[o + 3] = alpha(i);
            }
            using var full = new SKBitmap(new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            Marshal.Copy(bytes, 0, full.GetPixels(), bytes.Length);
            using var scaled = full.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.High);
            var pixels = scaled.GetPixelSpan();
            var result = new byte[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                result[y * width + x] = pixels[y * scaled.RowBytes + x * 4 + 3];
            return result;
        }

        private byte[] Tint(Color color)
        {
            // 흰 고양이에 흰 눈은 안 보이니 밝은 색이면 눈을 어둡게.
            Color eyeColor = ColorUtil.Luminance(color) > 0.7 ? Color.FromRgb(45, 42, 48) : Colors.White;
            var bytes = new byte[_width * _height * 4];
            for (int i = 0; i < _bodyAlpha.Length; i++)
            {
                int a = _bodyAlpha[i];
                int o = i * 4;
                if (_eye[i])
                {
                    // 몸통 알파만큼 몸색, 나머지는 눈색 → 불투명.
                    double t = a / 255.0;
                    bytes[o] = (byte)(color.B * t + eyeColor.B * (1 - t));
                    bytes[o + 1] = (byte)(color.G * t + eyeColor.G * (1 - t));
                    bytes[o + 2] = (byte)(color.R * t + eyeColor.R * (1 - t));
                    bytes[o + 3] = 255;
                }
                else
                {
                    bytes[o] = color.B;
                    bytes[o + 1] = color.G;
                    bytes[o + 2] = color.R;
                    bytes[o + 3] = (byte)a;
                }
            }
            return bytes;
        }
    }
}
