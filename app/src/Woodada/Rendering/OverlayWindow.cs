using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Woodada.Platform;

namespace Woodada.Rendering;

/// <summary>
/// 작업표시줄·독 위에 깔리는 투명 띠 창. 클릭 통과·포커스 안 뺏기 같은 OS별 설정은 IPlatform이 한다.
/// 위치는 화면 단위(Windows 물리 픽셀, macOS 포인트), 크기는 Avalonia 논리 단위로 정한다.
/// </summary>
/// <remarks>
/// 그리는 방법은 두 가지다. 보통은 Avalonia가 창에 그린다. Windows에서 캡처에서 뺄 때(layered)는 같은 그림을
/// 메모리에 그려 <see cref="ILayeredSurface"/>로 OS에 직접 넘긴다. 방법이 바뀌면 창을 새로 만든다(OverlayController).
/// </remarks>
internal sealed class OverlayWindow : Window
{
    private readonly RunnerLayer _layer = new();
    private readonly IPlatform _platform = PlatformServices.Current;
    private readonly ILayeredSurface? _surface;
    private RenderTargetBitmap? _canvas;
    private bool _shown;
    private bool _captureVisible = true;

    public OverlayWindow(bool layered = false)
    {
        Title = "Woodada Overlay";
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        Position = new PixelPoint(-32000, -32000);
        Width = Height = 1;

        // 직접 넘기는 창은 Avalonia의 투명 설정(DWM 뒤 흐림)을 걸지 않는다. 걸면 캡처 도구에 검은 띠가 남는다.
        if (layered) _surface = _platform.CreateLayeredSurface();
        else TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        // 직접 넘길 때는 창에 아무것도 올리지 않는다. Avalonia가 같은 그림을 한 번 더 그리지 않게.
        Content = _surface is null ? _layer : null;
    }

    /// <summary>그림을 OS에 직접 넘기는 창인지.</summary>
    public bool IsLayered => _surface is not null;

    /// <param name="strip">창이 차지할 사각형(화면 단위).</param>
    /// <param name="frames">그릴 러너들. Box는 strip 왼쪽 위 기준 화면 단위다.</param>
    public void Present(PixelRect strip, IReadOnlyList<RunnerFrame> frames, bool hangBelow)
    {
        if (_surface is not null)
        {
            PresentLayered(strip, frames, hangBelow);
            return;
        }

        if (Position != strip.Position) Position = strip.Position;

        // 화면 단위 ÷ 논리 단위. Windows는 창이 놓인 모니터의 배율, macOS는 늘 1이다.
        // 배율이 다른 모니터로 옮기면 다음 프레임부터 새 배율로 맞춰진다.
        double s = DesktopScaling > 0 ? DesktopScaling : 1;
        double w = strip.Width / s, h = strip.Height / s;
        if (Math.Abs(Width - w) > 0.01) Width = w;
        if (Math.Abs(Height - h) > 0.01) Height = h;

        // Windows는 창 최소 높이(약 39px)보다 낮게 만들 수 없어 창이 strip보다 클 수 있다.
        // 그래서 그림은 창 크기가 아니라 strip 높이(h) 기준으로 놓는다.
        _layer.Update(ToLogical(frames, s), hangBelow, h);
        EnsureShown();
    }

    /// <summary>
    /// 메모리 그림판에 그려 OS에 넘긴다. 창 위치와 크기도 OS가 그림판에 맞춘다.
    /// 배율은 창이 아니라 띠가 놓일 모니터에서 읽는다. 창을 Avalonia가 옮기지 않아 창 배율이 늦게 따라올 수 있어서다.
    /// </summary>
    private void PresentLayered(PixelRect strip, IReadOnlyList<RunnerFrame> frames, bool hangBelow)
    {
        if (strip.Width <= 0 || strip.Height <= 0) return;
        double s = Screens.ScreenFromPoint(strip.Center)?.Scaling ?? (DesktopScaling > 0 ? DesktopScaling : 1);
        _layer.Update(ToLogical(frames, s), hangBelow, strip.Height / s);

        var size = new PixelSize(strip.Width, strip.Height);
        if (_canvas is null || _canvas.PixelSize != size)
        {
            _canvas?.Dispose();
            _canvas = new RenderTargetBitmap(size);
        }
        using (var ctx = _canvas.CreateDrawingContext(true))
        using (ctx.PushTransform(Matrix.CreateScale(s, s)))
            _layer.DrawTo(ctx, s);

        IntPtr bits = _surface!.Prepare(size);
        int stride = size.Width * 4;
        _canvas.CopyPixels(new PixelRect(size), bits, stride * size.Height, stride);

        if (_shown)
        {
            _surface.Present(this, strip.Position);
            return;
        }
        // 처음 뜰 때(숨었다 다시 뜰 때도): OS 창 설정(WS_EX_LAYERED)과 캡처 제외를 먼저 걸고, 그림을 넘긴 뒤에 보인다.
        // Avalonia의 Show는 부르지 않는다(ILayeredSurface.SetVisible 참고).
        _platform.PrepareOverlay(this);
        _platform.SetOverlayCaptureVisible(this, _captureVisible);
        _surface.Present(this, strip.Position);
        _surface.SetVisible(this, true);
        _shown = true;
        _platform.KeepOverlayOnTop(this);
    }

    private static List<RunnerFrame> ToLogical(IReadOnlyList<RunnerFrame> frames, double s) =>
        frames.Select(f => f with { Box = new Rect(f.Box.X / s, f.Box.Y / s, f.Box.Width / s, f.Box.Height / s) }).ToList();

    private void EnsureShown()
    {
        if (_shown) return;
        Show();
        _shown = true;
        // Show할 때 OS 창 설정이 초기화될 수 있으니 뜰 때마다 다시 건다.
        _platform.PrepareOverlay(this);
        _platform.SetOverlayCaptureVisible(this, _captureVisible);
        _platform.KeepOverlayOnTop(this);
    }

    /// <summary>스크린샷·녹화·화면 공유에 찍힐지. 떠 있으면 바로, 아니면 다음에 뜰 때 건다.</summary>
    public void SetCaptureVisible(bool visible)
    {
        if (_captureVisible == visible) return;
        _captureVisible = visible;
        if (_shown) _platform.SetOverlayCaptureVisible(this, visible);
    }

    public void HideOverlay()
    {
        if (!_shown) return;
        if (_surface is not null) _surface.SetVisible(this, false);
        else Hide();
        _shown = false;
    }

    public void BringToTop()
    {
        if (_shown) _platform.KeepOverlayOnTop(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _canvas?.Dispose();
        _canvas = null;
        _surface?.Dispose();
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

        public override void Render(DrawingContext ctx) => DrawTo(ctx, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);

        /// <summary>창 밖(메모리 그림판)에도 그릴 수 있게 배율을 받는다. ctx는 논리 단위여야 한다.</summary>
        public void DrawTo(DrawingContext ctx, double scaling)
        {
            if (!_hangBelow)
            {
                SkinPainter.Draw(ctx, _frames, scaling);
                return;
            }
            // 위쪽 작업표시줄: 아랫면에 거꾸로 매달려 달린다.
            using (ctx.PushTransform(Matrix.CreateScale(1, -1) * Matrix.CreateTranslation(0, _stripHeight)))
                SkinPainter.Draw(ctx, _frames, scaling);
        }
    }
}
