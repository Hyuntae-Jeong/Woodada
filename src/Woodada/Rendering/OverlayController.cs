using System.Diagnostics;
using Woodada.Core;
using Woodada.Interop;
using Woodada.Settings;
using Woodada.Sprites;
using Woodada.Stats;

namespace Woodada.Rendering;

/// <summary>프레임 루프: 지표 샘플링 → 러너 이동 → 띠 비트맵 그리기 → 레이어드 창 갱신.</summary>
internal sealed class OverlayController : IDisposable
{
    private const double SampleInterval = 0.5;
    private const double GeometryInterval = 1.0;
    private const double TopmostInterval = 0.5;
    private const double MaxFrameDelta = 0.25; // 절전·디버거 정지 후 돌아왔을 때 순간이동 방지

    private readonly SettingsStore _store;
    private readonly StatsStore _stats;
    private readonly OverlayWindow _window = new();
    private readonly LayeredSurface _surface = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<RunnerInstance> _runners = new();

    private double _last;
    private double _sampleAcc, _geometryAcc, _topmostAcc;
    private Runway _runway;
    private PixelPitch _pitch;
    private bool _fullScreenApp;

    public OverlayController(SettingsStore store, StatsStore stats)
    {
        _store = store;
        _stats = stats;
        Sampler = new MetricSampler();
        _store.Changed += (_, _) => ApplySettings();

        ApplySettings();

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        // 로그오프·종료 때는 앱이 정상 종료 경로를 못 탈 수 있으니 기록부터 저장한다.
        Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
    }

    public MetricSampler Sampler { get; }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        MonitorPitch.Invalidate();
        RefreshEnvironment();
    }

    private void OnSessionEnding(object? sender, Microsoft.Win32.SessionEndingEventArgs e) => _stats.Save();

    /// <summary>설정이 바뀌면 러너 목록을 다시 만들되, 같은 Id 러너는 위치·방향을 유지한다.</summary>
    private void ApplySettings()
    {
        var settings = _store.Current;
        RefreshEnvironment(); // 모니터가 바뀌었을 수 있으니 다음 주기를 기다리지 않고 바로 다시 잡는다
        _timer.Interval = Math.Max(1, 1000 / settings.FramesPerSecond);

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
        _runway = Runway.FromTaskbar(TaskbarGeometry.Query(_store.Current.MonitorDeviceName));
        _pitch = MonitorPitch.For(MonitorCatalog.Resolve(_store.Current.MonitorDeviceName));

        _fullScreenApp = false;
        if (_store.Current.HideInFullScreen && NativeMethods.SHQueryUserNotificationState(out int state) == 0)
        {
            _fullScreenApp = state is NativeMethods.QUNS_BUSY or NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN
                or NativeMethods.QUNS_PRESENTATION_MODE;
        }
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
            // 지금 달리는 모니터 기준으로 바로 환산해 둔다. 나중에 픽셀 합계를 한꺼번에 바꾸면
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
        int maxHeight = _runners.Max(r => r.Config.Size);
        // 몸이 위아래로 출렁이는 여유분.
        int stripHeight = maxHeight + Math.Max(4, maxHeight / 8);
        var strip = _runway.GetStrip(stripHeight);

        _surface.EnsureSize(strip.Width, strip.Height);
        var g = _surface.Graphics;
        g.ResetTransform();
        g.Clear(Color.Transparent);

        if (_runway.HangBelow)
        {
            // 위쪽 작업표시줄: 아랫면에 거꾸로 매달려 달린다.
            g.TranslateTransform(0, strip.Height);
            g.ScaleTransform(1, -1);
        }

        foreach (var r in _runners)
        {
            var size = r.BodySize;
            var box = new RectangleF((float)(r.State.X - strip.Left), strip.Height - size.Height, size.Width, size.Height);
            var pose = new RunnerPose(r.State.Direction, r.State.GaitPhase, r.State.Intensity);
            r.Sprite.Draw(g, box, pose, ColorUtil.Parse(r.Config.Color, Color.Orange));
        }

        _window.Present(_surface, strip.Location);
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
        _stats.Save();
        _timer.Dispose();
        _window.Dispose();
        _surface.Dispose();
    }
}
