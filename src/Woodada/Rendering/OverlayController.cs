using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Woodada.Core;
using Woodada.Platform;
using Woodada.Settings;
using Woodada.Sprites;
using Woodada.Stats;

namespace Woodada.Rendering;

/// <summary>프레임 루프: 지표 샘플링 → 러너 이동 → 오버레이 창에 그리기. 모든 OS에서 같은 코드다.</summary>
internal sealed class OverlayController : IDisposable
{
    private const double SampleInterval = 0.5;
    private const double GeometryInterval = 1.0;
    private const double TopmostInterval = 0.5;
    private const double MaxFrameDelta = 0.25; // 절전·디버거 정지 후 돌아왔을 때 순간이동 방지

    private readonly IPlatform _platform = PlatformServices.Current;
    private readonly SettingsStore _store;
    private readonly StatsStore _stats;
    private readonly OverlayWindow _window = new();
    private readonly DispatcherTimer _timer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<RunnerInstance> _runners = new();

    private double _last;
    private double _sampleAcc, _geometryAcc, _topmostAcc;
    private Runway _runway;
    private PixelPitch _pitch = new(0.25, false, null, 0, 0);
    private bool _fullScreenApp;
    private bool _lightBar;

    public OverlayController(SettingsStore store, StatsStore stats)
    {
        _store = store;
        _stats = stats;
        Sampler = new MetricSampler();
        _store.Changed += (_, _) => ApplySettings();

        ApplySettings();

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _window.Screens.Changed += OnDisplayChanged;
    }

    public MetricSampler Sampler { get; }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        MonitorPitch.Invalidate();
        RefreshEnvironment();
    }

    /// <summary>설정이 바뀌면 러너 목록을 다시 만들되, 같은 Id 러너는 위치·방향을 유지한다.</summary>
    private void ApplySettings()
    {
        var settings = _store.Current;
        RefreshEnvironment(); // 모니터가 바뀌었을 수 있으니 다음 주기를 기다리지 않고 바로 다시 잡는다
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, 1000 / settings.FramesPerSecond));

        var existing = _runners.ToDictionary(r => r.Config.Id);
        _runners.Clear();
        foreach (var cfg in settings.Runners.Where(r => r.Enabled))
        {
            var sprite = SpriteRegistry.Get(cfg.SpriteId);
            if (existing.TryGetValue(cfg.Id, out var runner))
            {
                runner.Config = cfg;
                runner.Sprite = sprite;
            }
            else
            {
                runner = new RunnerInstance(cfg, sprite);
            }
            _runners.Add(runner);
        }
    }

    private void RefreshEnvironment()
    {
        var settings = _store.Current;
        if (ScreenCatalog.Resolve(_window.Screens, settings.MonitorDeviceName) is not { } screen)
        {
            _runway = default;
            return;
        }
        _runway = Runway.FromBar(_platform.FindBar(screen));
        _pitch = MonitorPitch.For(screen);
        _lightBar = SafeIsBarLight();
        _fullScreenApp = settings.HideInFullScreen && _platform.CanDetectFullScreen && _platform.IsFullScreenAppActive();
    }

    private void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _last, MaxFrameDelta);
        _last = now;

        _sampleAcc += dt;
        if (_sampleAcc >= SampleInterval)
        {
            Sampler.Sample();
            _sampleAcc = 0;
        }

        _geometryAcc += dt;
        if (_geometryAcc >= GeometryInterval)
        {
            RefreshEnvironment();
            _geometryAcc = 0;
        }

        if (_runners.Count == 0 || _fullScreenApp || _runway.Width <= 0)
        {
            _window.HideOverlay();
            return;
        }

        foreach (var r in _runners)
        {
            double moved = r.Update(dt, Sampler.Read(r.Config.Metric), _runway);
            // 지금 달리는 모니터 기준으로 바로 환산해 둔다. 나중에 합계를 한꺼번에 바꾸면
            // 모니터를 옮겨 다닌 기록이 한 모니터 기준으로 뭉개진다.
            _stats.Add(r.Config.Id, moved, moved / Math.Max(1, r.BodySize.Width), moved * _pitch.MillimetersPerPixel);
        }
        _stats.SaveIfDue();
        Render();

        _topmostAcc += dt;
        if (_topmostAcc >= TopmostInterval)
        {
            _window.BringToTop();
            _topmostAcc = 0;
        }
    }

    private void Render()
    {
        // 몸 위 여유분: 출렁임, 그리고 스킨이 있으면 떠오르는 높이와 몸 밖으로 번지는 발광까지.
        int stripHeight = _runners.Max(r => r.Config.Size + RunnerSkins.Headroom(r.Skin, r.Config.Size));
        var strip = _runway.GetStrip(stripHeight);
        double seconds = _clock.Elapsed.TotalSeconds;

        var frames = new List<RunnerFrame>();
        foreach (var r in _runners)
        {
            var size = r.BodySize;
            var skin = r.Skin;
            var color = ColorUtil.Parse(r.Config.Color, Colors.Orange);
            double lift = RunnerSkins.Lift(skin, r.Config.Size, seconds, r.EffectPhase);
            var box = new Rect(r.State.X - strip.X, strip.Height - size.Height - lift, size.Width, size.Height);
            var pose = new RunnerPose(r.State.Direction, r.State.GaitPhase, r.State.Intensity);
            if (skin == RunnerSkin.Flash)
            {
                // 아주 빠르면 몸이 달아오른다. 잔상의 몸 쪽 색도 이 색에서 이어지게 같은 색을 넘긴다.
                color = FlashSkin.BodyColor(color, r.Usage);
                // 플래시 효과는 몸 아래에 깔려야 하니 몸보다 먼저 넣는다.
                AddFlashTrail(frames, r, strip, box, pose, color, seconds);
            }
            GlitchShape? glitch = null;
            if (skin == RunnerSkin.Glitch)
            {
                GlitchSkin.Advance(r.Glitch, seconds, r.Usage);
                glitch = r.Glitch.Shape;
            }
            frames.Add(new RunnerFrame(r.Sprite, box, pose, color, skin, _lightBar, Glitch: glitch));
        }

        _window.Present(strip, frames, _runway.HangBelow);
    }

    /// <summary>
    /// 플래시 스킨: 불꽃 줄기 → 잔상(꼬리 끝부터 몸 쪽으로) 순서로 넣는다. 잔상은 지나온 길을 따라 놓여서
    /// 벽에서 돌아서면 꼬리도 그 길을 따라 꺾인다.
    /// </summary>
    private static void AddFlashTrail(List<RunnerFrame> frames, RunnerInstance r, PixelRect strip, Rect box, RunnerPose pose,
        Color color, double seconds)
    {
        double usage = r.Usage;
        if (FlashSkin.Strength(usage) <= 0) return;

        frames.Add(new RunnerFrame(r.Sprite, box, pose, color, RunnerSkin.Flash,
            Layer: FrameLayer.FlashWisps, Usage: usage, Seconds: seconds, Phase: r.EffectPhase));

        double tail = FlashSkin.TailLength(box.Width, usage);
        for (int i = FlashSkin.AfterimageCount; i >= 1; i--)
        {
            double q = i / (double)FlashSkin.AfterimageCount;
            if (r.Behind(tail * q) is not { } s) continue;
            var ghostBox = new Rect(s.X - strip.X, box.Y, box.Width, box.Height);
            var ghostPose = new RunnerPose(s.Direction, s.GaitPhase, pose.Intensity);
            frames.Add(new RunnerFrame(r.Sprite, ghostBox, ghostPose, FlashSkin.TrailColor(q, color), RunnerSkin.Flash,
                Layer: FrameLayer.Afterimage, Opacity: FlashSkin.AfterimageOpacity(i, usage)));
        }
    }

    /// <summary>테마를 못 읽어도(레지스트리 권한 등) 러너는 계속 달려야 하니 어두운 막대로 본다.</summary>
    private bool SafeIsBarLight()
    {
        try { return _platform.IsBarLight(); }
        catch (Exception) { return false; }
    }

    /// <summary>로그오프·종료처럼 정상 종료 경로를 못 탈 수 있을 때 먼저 불러 기록을 남긴다.</summary>
    public void SaveStats() => _stats.Save();

    public void Dispose()
    {
        _window.Screens.Changed -= OnDisplayChanged;
        _timer.Stop();
        _stats.Save();
        _window.Close();
    }
}
