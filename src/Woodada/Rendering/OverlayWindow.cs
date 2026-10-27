using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Woodada.Platform;

namespace Woodada.Rendering;

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

        private void DrawAll(DrawingContext ctx, double scaling) => SkinPainter.Draw(ctx, _frames, scaling);
    }
}
