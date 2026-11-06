// Copyright (c) 2026 Hyuntae Jeong. All rights reserved.
// 이 파일은 Woodada 스페셜 스킨 디자인으로, MIT License 적용 대상이 아닙니다. 사용 조건은 같은 폴더의 LICENSE를 보세요.
// This file is a Woodada special skin design and is NOT covered by the MIT License. See LICENSE in this folder.

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Woodada.Core;
using Woodada.Settings;

namespace Woodada.Sprites;

/// <summary>털실냥의 굴러가는 거리(몸 너비). 설정 창 미리보기·스킨 칸은 폭이 좁아서 따로 정한다.</summary>
/// <param name="SoftGap">가장 느릴 때 털실이 몸 몇 개쯤 앞서 나갔다가 따라잡히는지.</param>
/// <param name="HardGap">가장 빠를 때.</param>
public sealed record YarnTuning(double SoftGap, double HardGap)
{
    public static readonly YarnTuning Overlay = new(0.5, 3.2);
    /// <summary>설정 창 미리보기. 폭이 몸 6개 남짓이다.</summary>
    public static readonly YarnTuning Preview = new(0.5, 2.0);
    /// <summary>스킨 칸·도감·뽑기 무대의 제자리 달리기. 칸 안에 털실이 들어와야 한다.</summary>
    public static readonly YarnTuning Treadmill = new(0.3, 1.0);
}

/// <summary>
/// 털실 하나. 좌표는 트랙(러너 State.X와 같은 화면 좌표)이고, 높이·실 꼬리 Y는 바닥에서 위로 잰 값이다.
/// 그릴 때는 프레임 Box(털실 자리)에 맞춰 늘이고 줄여서, 오버레이가 Box를 논리 단위로 바꿔도 그대로 맞는다.
/// </summary>
public sealed class YarnBall
{
    internal const int Segments = 10;

    /// <summary>가운데 X, 바닥에서 뜬 높이(털실 아래쪽 기준).</summary>
    internal double X, H;
    internal double Vx, Vh;
    /// <summary>바닥에서 굴러가며 줄어드는 속도(초당).</summary>
    internal double Decel;
    internal double Angle, Squash;
    /// <summary>마지막으로 찬 힘(0 살포시 ~ 1 세게). 넘긴 털실이 떨어질 때 남길 속도와 바람 줄에 쓴다.</summary>
    internal double Power;
    /// <summary>뒷발로 넘겨 날아가는 중. 떨어지기 전에는 아무도 차지 않는다.</summary>
    internal bool Lob;
    /// <summary>각자 쓰는 털실이면 주인, 두 마리가 같이 쓰면 null.</summary>
    internal RunnerInstance? Owner;
    internal RunnerInstance? LastKicker;
    /// <summary>다음 차기까지 남은 시간. 두 마리가 같은 순간에 차지 않게.</summary>
    internal double Cool;
    internal double Radius, BodyHeight, BodyWidth;
    internal Color Color;
    /// <summary>Color를 고를 때 본 고양이 색들. 다르면 다시 고른다. 새 털실은 비어 있어서 처음에 꼭 고른다.</summary>
    internal string ColorKey = "";

    internal double SparkBorn = double.NaN, SparkX, SparkPower;
    internal int SparkDir = 1;

    internal readonly double[] RopeX = new double[Segments + 1], RopeY = new double[Segments + 1];
    internal readonly double[] RopePX = new double[Segments + 1], RopePY = new double[Segments + 1];

    internal void LayRope()
    {
        for (int i = 0; i <= Segments; i++)
        {
            RopeX[i] = RopePX[i] = X;
            RopeY[i] = RopePY[i] = 0;
        }
    }
}

/// <summary>
/// 털실냥이 차는 털실들. 같은 작업표시줄에서 털실냥이 두 마리 이상이면 털실 하나를 같이 쓰고, 한 마리면 제 털실을 찬다.
/// 오버레이·설정 창 미리보기·제자리 달리기가 저마다 하나씩 갖고, 러너들이 움직인 바로 뒤에 Step을 부른다.
/// </summary>
public sealed class YarnPlay
{
    private const double FixedStep = 1.0 / 60;

    private readonly List<YarnBall> _balls = new();
    private readonly List<RunnerInstance> _players = new();
    private double _accumulator;

    public YarnTuning Tuning { get; set; } = YarnTuning.Overlay;

    /// <summary>털실 시계. 차는 순간 튄 선이 얼마나 지났는지 잰다.</summary>
    public double Time { get; private set; }

    public IReadOnlyList<YarnBall> Balls => _balls;

    /// <summary>털실 프레임에 같이 실을 스프라이트(그리는 데는 안 쓴다).</summary>
    public IRunnerSprite? Sprite => _players.Count > 0 ? _players[0].Sprite : null;

    public void Step(double dt, IReadOnlyList<RunnerInstance> runners, Runway runway)
    {
        _players.Clear();
        foreach (var r in runners)
            if (r.Skin == RunnerSkin.Yarn) _players.Add(r);
        if (_players.Count == 0 || runway.Width <= 0)
        {
            _balls.Clear();
            _accumulator = 0;
            return;
        }

        // 두 마리 이상이면 하나를 같이, 한 마리면 제 것. 입은 고양이가 바뀌면 새로 놓는다.
        var owner = _players.Count == 1 ? _players[0] : null;
        if (_balls.Count != 1 || _balls[0].Owner != owner)
        {
            _balls.Clear();
            _balls.Add(new YarnBall());
            Respawn(_balls[0], owner, drop: false);
        }

        var ball = _balls[0];
        double height = 0, width = 0;
        foreach (var r in _players) { height += r.Height; width += r.BodySize.Width; }
        ball.BodyHeight = height / _players.Count;
        ball.BodyWidth = width / _players.Count;
        ball.Radius = Math.Max(2, ball.BodyHeight * YarnSkin.BallSize / 2);
        UpdateColor(ball);

        // 다른 모니터로 옮겨졌으면 고양이 앞에 다시 놓는다.
        if (ball.X < runway.Left - ball.Radius || ball.X > runway.Right + ball.Radius) Respawn(ball, owner, drop: false);

        // 미리보기에서 맞춘 움직임이 fps와 상관없이 똑같이 나오게 고정 간격으로 나눠 진행한다.
        _accumulator = Math.Min(_accumulator + dt, 0.25);
        while (_accumulator >= FixedStep)
        {
            _accumulator -= FixedStep;
            Time += FixedStep;
            foreach (var b in _balls) StepBall(b, FixedStep, runway);
        }
    }

    /// <summary>털실을 고양이(주인, 같이 쓰면 첫째) 앞에 놓는다. drop이면 위에서 떨어뜨린다.</summary>
    private void Respawn(YarnBall b, RunnerInstance? owner, bool drop)
    {
        var cat = owner ?? _players[0];
        double w = cat.BodySize.Width, p = YarnSkin.PowerOf(cat);
        b.BodyHeight = cat.Height;
        b.BodyWidth = w;
        b.Radius = Math.Max(2, cat.Height * YarnSkin.BallSize / 2);
        b.Owner = owner;
        b.X = Center(cat) + cat.State.Direction * (w / 2 + b.Radius + GapOf(p) * w * 0.5);
        b.H = drop ? cat.Height * 0.9 : 0;
        b.Vx = cat.State.Direction * cat.State.Speed;
        b.Vh = 0;
        b.Decel = DecelFor(w, cat.State.Speed, p);
        b.Squash = 0;
        b.Power = p;
        b.Lob = false;
        b.LastKicker = cat;
        b.Cool = 0;
        b.SparkBorn = double.NaN;
        b.LayRope();
    }

    /// <summary>털실 색: 입은 고양이들 색과 색상환에서 가장 먼 털실 색. 모두 무채색이면 빨강.</summary>
    private void UpdateColor(YarnBall ball)
    {
        // 키를 YarnPlay가 아니라 털실에 둔다. 스킨을 벗었다 다시 입으면 털실은 새로 생기는데 고양이 색은 그대로라,
        // 키를 밖에 두면 새 털실이 색을 못 받고 투명하게 그려진다.
        string key = string.Join(",", _players.Select(r => r.Config.Color));
        if (key == ball.ColorKey) return;
        ball.ColorKey = key;
        ball.Color = YarnSkin.ColorFor(_players.Select(r => ColorUtil.Parse(r.Config.Color, Colors.Orange)));
    }

    private static double Center(RunnerInstance r) => r.State.X + r.BodySize.Width / 2;

    private double GapOf(double p) => YarnSkin.Lerp(Tuning.SoftGap, Tuning.HardGap, p);

    /// <summary>차인 털실이 고양이보다 (세기−1)배 빠르게 출발해서, 몸 gap개쯤 앞서 나갔다가 따라잡히게 감속을 맞춘다.</summary>
    private double DecelFor(double w, double v, double p) =>
        Math.Max(w * 0.03, Math.Pow((YarnSkin.KickOf(p) - 1) * v, 2) / (2 * GapOf(p) * w));

    private void StepBall(YarnBall b, double dt, Runway runway)
    {
        double rad = b.Radius, g = YarnSkin.Gravity * b.BodyHeight;

        // 공중이면 포물선, 바닥이면 굴러가며 느려진다.
        b.Cool = Math.Max(0, b.Cool - dt);
        b.Vh -= g * dt;
        b.H += b.Vh * dt;
        if (b.H <= 0)
        {
            b.H = 0;
            // 넘긴 털실이 처음 땅에 닿으면 찬 것처럼 고양이보다 조금 빠른 속도만 남긴다. 안 그러면 날아온 속도 그대로 멀리 굴러간다.
            if (b.Lob && b.Vh < 0 && b.LastKicker is { } kicker)
                b.Vx = Math.Sign(b.Vx) * Math.Min(Math.Abs(b.Vx), YarnSkin.KickOf(b.Power) * kicker.State.Speed);
            if (b.Vh < -Math.Sqrt(2 * g * b.BodyHeight * 0.04))
            {
                b.Vh = -b.Vh * YarnSkin.Bounce;
                b.Squash = Math.Max(b.Squash, 0.7);
            }
            else b.Vh = 0;
            if (b.Vh == 0) b.Lob = false;
        }
        if (b.H == 0)
        {
            double m = Math.Abs(b.Vx) - b.Decel * dt;
            b.Vx = m > 0 ? Math.Sign(b.Vx) * m : 0;
        }
        b.X += b.Vx * dt;
        if (b.X < runway.Left + rad) { b.X = runway.Left + rad; b.Vx = Math.Abs(b.Vx) * 0.5; }
        if (b.X > runway.Right - rad) { b.X = runway.Right - rad; b.Vx = -Math.Abs(b.Vx) * 0.5; }
        b.Angle += b.Vx * dt / rad;
        b.Squash *= Math.Exp(-dt * 14);

        if (!b.Lob && b.Cool <= 0) Play(b);
        StepRope(b, dt);
    }

    /// <summary>누가 차고, 넘기고, 맡기는지.</summary>
    private void Play(YarnBall b)
    {
        double rad = b.Radius, height = b.BodyHeight;

        // 차기: 털실이 내 앞, 발끝 닿는 곳에 있고 나보다 빨리 달아나는 중이 아니면 찬다.
        foreach (var c in _players)
        {
            if (b.Owner is not null && c != b.Owner) continue;
            double w = c.BodySize.Width, dir = c.State.Direction;
            double front = dir > 0 ? c.State.X + w : c.State.X;
            if (dir * (b.X - Center(c)) > 0 && dir * (b.X - front) < rad * 0.4 && b.H < height * 0.45 && dir * b.Vx < c.State.Speed * 1.05)
            {
                Kick(b, c, front);
                return;
            }
        }
        if (b.H >= height * 0.2) return;

        // 넘기기: 털실이 내 뒤로 빠졌을 때. 같이 쓰는 털실이면, 털실을 앞에 두고 다가오는 고양이가 가까이 있으면 맡긴다.
        RunnerInstance? behind = null;
        bool coming = false;
        foreach (var c in _players)
        {
            if (b.Owner is not null && c != b.Owner) continue;
            double w = c.BodySize.Width, ahead = c.State.Direction * (b.X - Center(c));
            if (ahead <= 0 && -ahead < w * 2.5 && (behind is null || Math.Abs(Center(c) - b.X) < Math.Abs(Center(behind) - b.X))) behind = c;
            if (ahead > 0 && ahead < w * YarnSkin.YieldBodies) coming = true;
        }
        if (behind is not null)
        {
            if (!coming) Lob(b, behind);
        }
        else if (b.Owner is { } o && o.State.Direction * (Center(o) - b.X) >= o.BodySize.Width * 2.5)
        {
            // 혼자 쓰는 털실이 주인 뒤로 너무 멀리 처지면 위에서 새로 떨어뜨린다.
            Respawn(b, o, drop: true);
        }
    }

    private void Kick(YarnBall b, RunnerInstance cat, double front)
    {
        double w = cat.BodySize.Width, v = cat.State.Speed, p = YarnSkin.PowerOf(cat), dir = cat.State.Direction;
        double g = YarnSkin.Gravity * b.BodyHeight;
        b.Vx = dir * YarnSkin.KickOf(p) * v * Jitter(0.9, 1.1);
        b.Decel = DecelFor(w, v, p);
        b.Vh = YarnSkin.Lerp(YarnSkin.HopSoft, YarnSkin.HopHard, p) * Math.Sqrt(2 * g * b.BodyHeight * 0.6) * Jitter(0.7, 1.15);
        // 살포시 차면 거의 안 찌그러지고, 세게 차면 확 찌그러진다.
        b.Squash = 0.25 + 0.75 * p;
        Touched(b, cat, p, front);
    }

    /// <summary>뒷발로 넘기기: 머리 위로 띄워 앞으로 보낸다. 날아가는 동안 고양이가 달린 거리까지 셈해서 앞에 떨어지게.</summary>
    private void Lob(YarnBall b, RunnerInstance cat)
    {
        double w = cat.BodySize.Width, v = cat.State.Speed, p = YarnSkin.PowerOf(cat), dir = cat.State.Direction;
        double g = YarnSkin.Gravity * b.BodyHeight;
        // 느리면 낮게 톡, 빠르면 높이 붕.
        double vh = Math.Sqrt(2 * g * b.BodyHeight * YarnSkin.LobHeight * YarnSkin.LobScale(p)), flight = 2 * vh / g;
        double land = Center(cat) + dir * (v * flight + w / 2 + b.Radius + GapOf(p) * w * 0.6);
        b.Vx = (land - b.X) / flight;
        b.Vh = vh;
        b.Lob = true;
        b.Decel = DecelFor(w, v, p);
        b.Squash = 0.25 + 0.75 * p;
        Touched(b, cat, p, b.X);
    }

    private void Touched(YarnBall b, RunnerInstance cat, double p, double sparkX)
    {
        b.Power = p;
        b.LastKicker = cat;
        b.Cool = YarnSkin.KickCooldown;
        b.SparkBorn = Time;
        b.SparkX = sparkX;
        b.SparkDir = cat.State.Direction;
        b.SparkPower = p;
    }

    private static double Jitter(double a, double b) => a + Random.Shared.NextDouble() * (b - a);

    /// <summary>실 꼬리: 털실 가운데에 매달린 짧은 밧줄(버렛 적분). 바닥에 닿은 마디는 끌리며 느려진다.</summary>
    private static void StepRope(YarnBall b, double dt)
    {
        // 미리보기는 1/30초마다 0.98·0.55를 곱했다. 간격이 달라도 초당 줄어드는 비율이 같게 맞춘다.
        double air = Math.Pow(0.98, dt * 30), drag = Math.Pow(0.55, dt * 30);
        double seg = Math.Max(0.5, YarnSkin.ThreadLength * b.BodyWidth / YarnBall.Segments);
        double g = YarnSkin.Gravity * b.BodyHeight * 0.6;
        var x = b.RopeX; var y = b.RopeY; var px = b.RopePX; var py = b.RopePY;
        x[0] = b.X;
        y[0] = b.H + b.Radius;
        for (int i = 1; i <= YarnBall.Segments; i++)
        {
            bool onFloor = y[i] <= 0.1;
            double vx = (x[i] - px[i]) * (onFloor ? drag : air), vy = (y[i] - py[i]) * air;
            px[i] = x[i];
            py[i] = y[i];
            x[i] += vx;
            y[i] += vy - g * dt * dt;
        }
        for (int k = 0; k < 6; k++)
        {
            for (int i = 1; i <= YarnBall.Segments; i++)
            {
                double dx = x[i] - x[i - 1], dy = y[i] - y[i - 1];
                double d = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy)), diff = (d - seg) / d;
                if (i == 1)
                {
                    x[i] -= dx * diff;
                    y[i] -= dy * diff;
                }
                else
                {
                    x[i - 1] += dx * diff * 0.5;
                    y[i - 1] += dy * diff * 0.5;
                    x[i] -= dx * diff * 0.5;
                    y[i] -= dy * diff * 0.5;
                }
            }
            for (int i = 1; i <= YarnBall.Segments; i++)
                if (y[i] < 0) y[i] = 0;
        }
    }
}

/// <summary>
/// 털실냥: 고양이가 털실 뭉치를 툭툭 차면서 달린다. 천천히 달리면 코앞에 살포시 굴려 두고, 빨리 달릴수록 세게 차서
/// 털실이 통통 튀며 멀리 굴러간다. 벽에서 돌아서거나 털실이 뒤로 빠지면 뒷발로 머리 위로 넘긴다.
/// 털실냥 두 마리는 털실 하나를 같이 써서, 마주 오면 패스하고 다른 고양이가 다가오면 맡긴다.
/// 고양이 몸은 그대로고 속도·생선도 그대로다. 값은 tools/skin_preview.py yarn 미리보기로 맞췄다.
/// </summary>
internal static class YarnSkin
{
    /// <summary>털실 지름(키 대비).</summary>
    public const double BallSize = 0.40;
    /// <summary>찬 털실이 고양이보다 몇 배 빠르게 출발하는지. 가장 느릴 때 ~ 가장 빠를 때.</summary>
    public const double KickSoft = 1.5, KickHard = 2.4;
    /// <summary>찰 때 튀는 높이(키의 60% 높이까지 뛰는 속도 대비).</summary>
    public const double HopSoft = 0.04, HopHard = 0.65;
    /// <summary>바닥에 떨어질 때 튀어 오르는 비율.</summary>
    public const double Bounce = 0.35;
    /// <summary>뒷발로 넘길 때 높이(키 대비). 느리면 0.6배, 빠르면 1.25배.</summary>
    public const double LobHeight = 1.4;
    /// <summary>실 꼬리 길이(몸 너비 대비).</summary>
    public const double ThreadLength = 1.1;
    public const int Strands = 3;
    /// <summary>털실이 뒤로 빠졌을 때, 이만큼(몸 너비) 안에서 다른 고양이가 털실을 향해 오고 있으면 넘기지 않고 맡긴다.</summary>
    public const double YieldBodies = 6;
    /// <summary>중력(키 대비, 초당²).</summary>
    public const double Gravity = 30;
    public const double KickCooldown = 0.15;

    /// <summary>털실 다섯 가지. 고양이 색과 가장 먼 것을 고른다.</summary>
    private static readonly Color[] YarnColors =
    {
        Color.FromRgb(0xE0, 0x47, 0x4C), // 빨강
        Color.FromRgb(0xF2, 0x8A, 0xB2), // 분홍
        Color.FromRgb(0x4C, 0x8B, 0xF5), // 파랑
        Color.FromRgb(0xF2, 0xC1, 0x4E), // 노랑
        Color.FromRgb(0x5D, 0xBB, 0x63), // 초록
    };

    private static readonly Color SparkOnDark = Colors.White;
    private static readonly Color SparkOnLight = Color.FromRgb(0x3A, 0x3F, 0x4A);

    public static double Lerp(double a, double b, double k) => a + (b - a) * k;

    /// <summary>차는 힘: 지금 달리는 속도로 정한다. 가장 느릴 때 0, 가장 빠를 때 1(사용률^0.7과 같다).</summary>
    public static double PowerOf(RunnerInstance r)
    {
        double w = Math.Max(1, r.BodySize.Width);
        return Math.Clamp((r.State.Speed / w - 0.4) / 6.6, 0, 1);
    }

    public static double KickOf(double power) => Lerp(KickSoft, KickHard, power);

    public static double LobScale(double power) => Lerp(0.6, 1.25, power);

    /// <summary>오버레이 띠에서 몸 위로 남겨 둘 여유. 가장 세게 넘긴 털실이 꼭대기까지 보이게.</summary>
    public static int Headroom(int height) => (int)Math.Ceiling(height * (LobHeight * LobScale(1) + BallSize - 1)) + 2;

    public static Color ColorFor(IEnumerable<Color> bodies)
    {
        var hues = bodies.Where(c => ColorUtil.IsChromatic(c, 0.25)).Select(c => c.ToHsl().H).ToList();
        if (hues.Count == 0) return YarnColors[0];
        return YarnColors.MaxBy(y => hues.Min(h => ColorUtil.HueDistance(h, y.ToHsl().H)));
    }

    /// <summary>털실 자리(트랙 좌표 → 그릴 좌표). 바닥에서 뜬 만큼 올라간다.</summary>
    public static Rect BoxFor(YarnBall b, double originX, double floorY) =>
        new(b.X - b.Radius - originX, floorY - b.H - b.Radius * 2, b.Radius * 2, b.Radius * 2);

    /// <summary>몸 뒤에 깔리는 것: 실 꼬리와 바닥 그림자.</summary>
    public static void DrawBack(DrawingContext ctx, Rect box, YarnBall b, bool lightBackground, double renderScaling)
    {
        double scale = renderScaling > 0 ? renderScaling : 1;
        double k = box.Width / (b.Radius * 2), rad = box.Width / 2, body = b.BodyHeight * k;
        double cx = box.Center.X, floor = box.Bottom + b.H * k;

        double lw = Math.Max(1 / scale, body / 28);
        var ropePen = PenFor(ColorUtil.Mix(b.Color, Colors.Black, 0.12), 1, lw);
        Point At(int i) => new(cx + (b.RopeX[i] - b.X) * k, floor - b.RopeY[i] * k - lw / 2);
        for (int i = 0; i < YarnBall.Segments; i++) ctx.DrawLine(ropePen, At(i), At(i + 1));

        // 바닥 그림자: 높이 뜰수록 작고 옅게
        double up = Math.Min(1, b.H / (b.BodyHeight * 1.5));
        var shadow = BrushFor(Colors.Black, (lightBackground ? 0.18 : 0.35) * (1 - up * 0.7));
        ctx.DrawEllipse(shadow, null, new Point(cx, floor), rad * (1 - up * 0.4), Math.Max(0.8 / scale, rad * 0.22));
    }

    /// <summary>몸 앞에 그리는 것: 바람 줄, 털실, 찰 때 튄 선.</summary>
    public static void DrawFront(DrawingContext ctx, Rect box, YarnBall b, double seconds, bool lightBackground, double renderScaling)
    {
        double scale = renderScaling > 0 ? renderScaling : 1;
        double k = box.Width / (b.Radius * 2), rad = box.Width / 2, body = b.BodyHeight * k;
        double cx = box.Center.X, cy = box.Center.Y, floor = box.Bottom + b.H * k;
        var ink = lightBackground ? SparkOnLight : SparkOnDark;

        // 바람 줄: 빨리 굴러가는 털실 뒤로 끌린다. 세게 찼을 때만 보인다.
        double fast = Math.Min(1, (Math.Abs(b.Vx) / Math.Max(1, b.BodyWidth) - 5) / 8);
        if (fast > 0)
        {
            int dir = Math.Sign(b.Vx);
            var pen = PenFor(ink, 0.55 * fast, Math.Max(1 / scale, body / 32));
            foreach (var (dy, len) in Streaks)
            {
                double x0 = cx - dir * rad * 1.3;
                ctx.DrawLine(pen, new Point(x0, cy + dy * rad), new Point(x0 - dir * rad * 2.2 * len * fast, cy + dy * rad));
            }
        }

        // 털실. 차이거나 바닥에 떨어진 순간 옆으로 퍼졌다가 돌아온다(바닥 쪽을 축으로).
        double sq = b.Squash * 0.22;
        double bottom = box.Bottom;
        var squash = Matrix.CreateTranslation(-cx, -bottom) * Matrix.CreateScale(1 + sq, 1 - sq) * Matrix.CreateTranslation(cx, bottom);
        using (ctx.PushTransform(squash))
        {
            var center = new Point(cx, cy);
            var dark = ColorUtil.Mix(b.Color, Colors.Black, 0.38);
            ctx.DrawEllipse(BrushFor(b.Color, 1), null, center, rad, rad);
            // 감긴 실 줄: 구르는 만큼 돌아간다. 털실 밖으로 안 나가게 조금 작게 그린다.
            var strandPen = PenFor(dark, 1, Math.Max(1 / scale, rad / 5));
            for (int i = 0; i < Strands; i++)
            {
                double angle = b.Angle + i * Math.PI / Strands;
                var spin = Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(cx, cy);
                using (ctx.PushTransform(spin))
                    ctx.DrawEllipse(null, strandPen, center, rad * 0.9, rad * (0.28 + 0.5 * (i % 2)));
            }
            // 하이라이트와 테두리(밝은 바탕에서도 털실 모양이 보이게)
            ctx.DrawEllipse(BrushFor(ColorUtil.Mix(b.Color, Colors.White, 0.45), 0.7), null,
                new Point(cx - rad * 0.35, cy - rad * 0.38), rad * 0.28, rad * 0.28);
            double outline = Math.Max(1 / scale, rad / 6);
            ctx.DrawEllipse(null, PenFor(dark, 1, outline), center, rad - outline / 2, rad - outline / 2);
        }

        // 찰 때 발끝에서 튀는 짧은 선. 살포시 차면 옅은 선 하나, 세게 차면 길고 많은 선이 오래 남는다.
        if (double.IsNaN(b.SparkBorn)) return;
        double p = b.SparkPower;
        double age = (seconds - b.SparkBorn) / Lerp(0.12, 0.22, p);
        if (age is < 0 or >= 1) return;
        double len2 = body * 0.18 * Lerp(0.45, 1.5, p) * (0.5 + age);
        double off = body * 0.1 + body * 0.15 * age * Lerp(0.6, 1.4, p);
        var sparkPen = PenFor(ink, (1 - age) * Lerp(0.45, 1, p), Math.Max(1 / scale, body / 28 * Lerp(0.8, 1.4, p)));
        double sx = cx + (b.SparkX - b.X) * k, sy = floor - rad;
        foreach (double a in SparkAngles[p < 0.3 ? 0 : p < 0.6 ? 1 : 2])
        {
            double dx = Math.Cos(a) * b.SparkDir, dy = Math.Sin(a);
            var start = new Point(sx + dx * off, sy + dy * off);
            ctx.DrawLine(sparkPen, start, new Point(start.X + dx * len2, start.Y + dy * len2));
        }
    }

    private static readonly (double Dy, double Len)[] Streaks = { (-0.45, 0.8), (0, 1.2), (0.45, 0.7) };
    private static readonly double[][] SparkAngles =
    {
        new[] { -0.35 },
        new[] { -0.9, -0.35, 0.2 },
        new[] { -1.15, -0.75, -0.35, 0.05, 0.4 },
    };

    // 붓·펜을 매 프레임 새로 만들지 않는다(플래시 불꽃 줄기와 같은 이유). 진하기·굵기는 단계로 끊어 캐시가 맞게 한다.
    private const int AlphaSteps = 32;
    private static readonly Dictionary<(uint, int), IImmutableBrush> Brushes = new();
    private static readonly Dictionary<(uint, int, int), ImmutablePen> Pens = new();

    private static IImmutableBrush BrushFor(Color color, double opacity)
    {
        int a = (int)Math.Round(Math.Clamp(opacity, 0, 1) * AlphaSteps);
        var key = (color.ToUInt32(), a);
        if (!Brushes.TryGetValue(key, out var brush))
            Brushes[key] = brush = new ImmutableSolidColorBrush(color, a / (double)AlphaSteps);
        return brush;
    }

    private static ImmutablePen PenFor(Color color, double opacity, double width)
    {
        int a = (int)Math.Round(Math.Clamp(opacity, 0, 1) * AlphaSteps);
        int w = (int)Math.Round(width * 8);
        var key = (color.ToUInt32(), a, w);
        if (!Pens.TryGetValue(key, out var pen))
            Pens[key] = pen = new ImmutablePen(new ImmutableSolidColorBrush(color, a / (double)AlphaSteps), Math.Max(1, w) / 8.0,
                lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return pen;
    }
}
