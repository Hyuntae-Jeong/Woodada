using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Woodada.Settings;

namespace Woodada.Sprites;

/// <summary>
/// RunCat365(Apache-2.0)의 달리는 고양이 5프레임.
/// 원본은 검은 실루엣이라 알파만 마스크로 쓰고 색은 설정값으로 칠한다.
/// 프레임은 (색, 높이, 방향)별로 미리 축소해 캐시해서 매 프레임 스케일링 비용이 들지 않는다.
/// </summary>
public sealed class RunCatSprite : IRunnerSprite
{
    private const int FrameCount = 5;
    private const int MaxCacheEntries = 24;

    private readonly FrameMask[] _masks;
    private readonly Size _crop;
    private readonly Dictionary<(int Argb, int Height, int Facing), Bitmap[]> _cache = new();

    public RunCatSprite()
    {
        var raw = Enumerable.Range(0, FrameCount).Select(i => LoadArgb($"RunCat.cat_running_{i}.png")).ToArray();

        // 프레임마다 실루엣 범위가 달라서, 전 프레임 합집합으로 잘라야 발 위치(바닥선)가 흔들리지 않는다.
        var union = Rectangle.Empty;
        foreach (var f in raw)
        {
            var b = f.OpaqueBounds();
            union = union.IsEmpty ? b : Rectangle.Union(union, b);
        }
        _crop = union.Size;
        _masks = raw.Select(f => FrameMask.Build(f, union)).ToArray();
    }

    public string Id => "cat";
    public string DisplayName => "고양이";

    public SizeF Measure(int heightPx) => new(heightPx * _crop.Width / (float)_crop.Height, heightPx);

    public void Draw(Graphics g, RectangleF box, RunnerPose pose, Color color)
    {
        int height = Math.Max(4, (int)Math.Round(box.Height));
        var frames = GetFrames(color, height, pose.Facing < 0 ? -1 : 1);
        int index = (int)(pose.GaitPhase * FrameCount) % FrameCount;
        if (index < 0) index += FrameCount;
        var bmp = frames[index];

        var oldInterp = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor; // 이미 목표 크기라 1:1로 찍기만 한다
        g.DrawImage(bmp, (float)Math.Round(box.X), (float)Math.Round(box.Bottom - bmp.Height), bmp.Width, bmp.Height);
        g.InterpolationMode = oldInterp;
    }

    private Bitmap[] GetFrames(Color color, int height, int facing)
    {
        var key = (color.ToArgb(), height, facing);
        if (_cache.TryGetValue(key, out var frames)) return frames;

        if (_cache.Count >= MaxCacheEntries)
        {
            foreach (var set in _cache.Values)
                foreach (var b in set) b.Dispose();
            _cache.Clear();
        }

        int width = Math.Max(1, (int)Math.Round(height * _crop.Width / (double)_crop.Height));
        frames = _masks.Select(m =>
        {
            using var full = m.Tint(color);
            var scaled = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(scaled))
            using (var attrs = new ImageAttributes())
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                attrs.SetWrapMode(WrapMode.TileFlipXY); // 가장자리 픽셀이 반투명하게 번지는 것 방지
                g.DrawImage(full, new Rectangle(0, 0, width, height), 0, 0, full.Width, full.Height, GraphicsUnit.Pixel, attrs);
            }
            if (facing < 0) scaled.RotateFlip(RotateFlipType.RotateNoneFlipX); // 원본은 오른쪽을 본다
            return scaled;
        }).ToArray();

        _cache[key] = frames;
        return frames;
    }

    private static ArgbImage LoadArgb(string resourceName)
    {
        using var stream = typeof(RunCatSprite).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"리소스 없음: {resourceName}");
        using var bmp = new Bitmap(stream);
        return ArgbImage.From(bmp);
    }

    /// <summary>32bpp ARGB 픽셀 배열 (BGRA 순서, 행 우선).</summary>
    private sealed class ArgbImage(int width, int height, byte[] bgra)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public byte[] Bgra { get; } = bgra;

        public byte Alpha(int x, int y) => Bgra[(y * Width + x) * 4 + 3];

        public Rectangle OpaqueBounds()
        {
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (Alpha(x, y) <= 8) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            return maxX < 0 ? new Rectangle(0, 0, Width, Height) : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        public static ArgbImage From(Bitmap source)
        {
            using var bmp = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[bmp.Width * bmp.Height * 4];
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bmp.Width * 4, bmp.Width * 4);
                return new ArgbImage(bmp.Width, bmp.Height, bytes);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
    }

    /// <summary>잘라낸 프레임 하나의 몸통 알파와 눈(실루엣 안의 구멍) 마스크.</summary>
    private sealed class FrameMask
    {
        private int _width, _height;
        private byte[] _bodyAlpha = [];
        private bool[] _eye = [];

        public static FrameMask Build(ArgbImage src, Rectangle crop)
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

        public Bitmap Tint(Color color)
        {
            // 흰 고양이에 흰 눈은 안 보이니 밝은 색이면 눈을 어둡게.
            Color eyeColor = ColorUtil.Luminance(color) > 0.7 ? Color.FromArgb(255, 45, 42, 48) : Color.White;
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

            var bmp = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < _height; y++)
                    Marshal.Copy(bytes, y * _width * 4, data.Scan0 + y * data.Stride, _width * 4);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }
    }
}
