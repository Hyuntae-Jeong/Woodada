using Woodada.Interop;

namespace Woodada.Rendering;

/// <summary>
/// 작업표시줄 위에 깔리는 투명 띠 창.
/// 클릭을 통과시키고(TRANSPARENT), 포커스를 뺏지 않고(NOACTIVATE), Alt+Tab에 안 뜨게(TOOLWINDOW) 한다.
/// 위치·크기는 WinForms Bounds가 아니라 UpdateLayeredWindow로만 정한다(물리 픽셀 그대로 쓰려고).
/// </summary>
internal sealed class OverlayWindow : Form
{
    private bool _shown;

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Woodada Overlay";
        Bounds = new Rectangle(-32000, -32000, 1, 1);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
                          NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW |
                          NativeMethods.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    // 레이어드 창은 UpdateLayeredWindow가 내용을 책임지므로 WinForms 배경 그리기를 막는다.
    protected override void OnPaintBackground(PaintEventArgs e) { }

    public bool IsOverlayVisible => _shown;

    public void Present(LayeredSurface surface, Point screenLocation)
    {
        if (!IsHandleCreated) CreateHandle();
        surface.Flush();

        var size = new NativeMethods.SIZE(surface.Width, surface.Height);
        var src = new NativeMethods.POINT(0, 0);
        var dst = new NativeMethods.POINT(screenLocation.X, screenLocation.Y);
        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AC_SRC_ALPHA,
        };

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, surface.MemoryDC, ref src, 0,
                ref blend, NativeMethods.ULW_ALPHA);
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }

        if (!_shown)
        {
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            _shown = true;
            BringToTop();
        }
    }

    public void HideOverlay()
    {
        if (!_shown || !IsHandleCreated) return;
        NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
        _shown = false;
    }

    /// <summary>작업표시줄도 topmost라 클릭하면 우리 창 위로 올라온다. 주기적으로 다시 올린다.</summary>
    public void BringToTop()
    {
        if (!_shown || !IsHandleCreated) return;
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_NOOWNERZORDER);
    }
}
