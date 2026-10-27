using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Woodada.Platform;
using Woodada.Sprites;

namespace Woodada.Rendering;

/// <summary>오버레이에 한 번 그릴 것이 무엇인지.</summary>
internal enum FrameLayer
{
    /// <summary>러너 몸(스킨 포함).</summary>
    Body,
    /// <summary>플래시 스킨 잔상: 한 색 실루엣을 Opacity만큼.</summary>
    Afterimage,
    /// <summary>플래시 스킨 불꽃 줄기. Box는 몸 자리다.</summary>
    FlashWisps,
}

/// <summary>오버레이에 그릴 것 하나. 대부분 러너 몸이고, 플래시 스킨은 잔상·불꽃 줄기가 앞에 더 붙는다.</summary>
/// <param name="LightBackground">밝은 작업표시줄·독 위인지. 스킨이 외곽선 톤을 고르는 데 쓴다.</param>
/// <param name="Usage">사용률(%). 불꽃 줄기 길이·진하기를 정한다.</param>
/// <param name="Seconds">불꽃이 일렁이는 시계.</param>
/// <param name="Phase">러너마다 다른 효과 위상.</param>
internal readonly record struct RunnerFrame(IRunnerSprite Sprite, Rect Box, RunnerPose Pose, Color Color,
    RunnerSkin Skin = RunnerSkin.None, bool LightBackground = false,
    FrameLayer Layer = FrameLayer.Body, double Opacity = 1, double Usage = 0, double Seconds = 0, double Phase = 0);

/// <summary>
/// 작업표시줄·독 위에 깔리는 투명 띠 창. 클릭 통과·포커스 안 뺏기 같은 OS별 설정은 IPlatform이 한다.
/// 위치는 화면 단위(Windows 물리 픽셀, macOS 포인트), 크기는 Avalonia 논리 단위로 정한다.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private readonly RunnerLayer _layer = new();
    private readonly IPlatform _platform = PlatformServices.Current;
    private bool _shown;

    public OverlayWindow()
    {
        Title = "Woodada Overlay";
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        Content = _layer;
        Position = new PixelPoint(-32000, -32000);
        Width = Height = 1;
    }

    /// <param name="strip">창이 차지할 사각형(화면 단위).</param>
    /// <param name="frames">그릴 러너들. Box는 strip 왼쪽 위 기준 화면 단위다.</param>
    public void Present(PixelRect strip, IReadOnlyList<RunnerFrame> frames, bool hangBelow)
    {
        if (Position != strip.Position) Position = strip.Position;

        // 화면 단위 ÷ 논리 단위. Windows는 창이 놓인 모니터의 배율, macOS는 늘 1이다.
        // 배율이 다른 모니터로 옮기면 다음 프레임부터 새 배율로 맞춰진다.
        double s = DesktopScaling > 0 ? DesktopScaling : 1;
        double w = strip.Width / s, h = strip.Height / s;
        if (Math.Abs(Width - w) > 0.01) Width = w;
        if (Math.Abs(Height - h) > 0.01) Height = h;

        // Windows는 창 최소 높이(약 39px)보다 낮게 만들 수 없어 창이 strip보다 클 수 있다.
        // 그래서 그림은 창 크기가 아니라 strip 높이(h) 기준으로 놓는다.
        _layer.Update(frames.Select(f => f with { Box = new Rect(f.Box.X / s, f.Box.Y / s, f.Box.Width / s, f.Box.Height / s) }).ToList(), hangBelow, h);

        if (!_shown)
        {
            Show();
            _shown = true;
            // Show할 때 OS 창 설정이 초기화될 수 있으니 뜰 때마다 다시 건다.
            _platform.PrepareOverlay(this);
            _platform.KeepOverlayOnTop(this);
        }
    }

    public void HideOverlay()
    {
        if (!_shown) return;
        Hide();
        _shown = false;
    }

    public void BringToTop()
    {
        if (_shown) _platform.KeepOverlayOnTop(this);
    }

    /// <summary>러너들을 그리는 판. 컨트롤러가 매 프레임 그릴 목록을 넘겨준다.</summary>
    private sealed class RunnerLayer : Control
    {
        private IReadOnlyList<RunnerFrame> _frames = Array.Empty<RunnerFrame>();
        private bool _hangBelow;
        private double _stripHeight;

        public void Update(IReadOnlyList<RunnerFrame> frames, bool hangBelow, double stripHeight)
        {
            _frames = frames;
            _hangBelow = hangBelow;
            _stripHeight = stripHeight;
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            if (!_hangBelow)
            {
                DrawAll(ctx, scaling);
                return;
            }
            // 위쪽 작업표시줄: 아랫면에 거꾸로 매달려 달린다.
            using (ctx.PushTransform(Matrix.CreateScale(1, -1) * Matrix.CreateTranslation(0, _stripHeight)))
                DrawAll(ctx, scaling);
        }

        private void DrawAll(DrawingContext ctx, double scaling)
        {
            foreach (var f in _frames)
            {
                switch (f.Layer)
                {
                    case FrameLayer.Afterimage:
                        f.Sprite.DrawSilhouette(ctx, f.Box, f.Pose, f.Color, f.Opacity, scaling);
                        break;
                    case FrameLayer.FlashWisps:
                        FlashSkin.DrawWisps(ctx, f.Box, f.Pose.Facing, f.Usage, f.Seconds, f.Phase, f.Color, scaling);
                        break;
                    default:
                        f.Sprite.Draw(ctx, f.Box, f.Pose, f.Color, scaling, f.Skin, f.LightBackground);
                        break;
                }
            }
        }
    }
}
