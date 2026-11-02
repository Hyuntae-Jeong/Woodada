using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Woodada.Settings;

namespace Woodada.UI;

/// <summary>설정 창에서 쓰는 작은 창들. 두 OS에서 똑같이 보이도록 직접 만든다.</summary>
internal static class Dialogs
{
    /// <summary>예/아니요를 묻는다.</summary>
    public static Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var dialog = NewDialog(title, 380);
        var yes = DialogButton("예", primary: true);
        var no = DialogButton("아니요", primary: false);
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        dialog.Content = DialogBody(
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = GameArt.FontLarge, Foreground = GameArt.Brush(GameArt.Text) },
            yes, no);
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) dialog.Close(false);
        };
        return dialog.ShowDialog<bool>(owner);
    }

    /// <summary>색을 고른다. 취소하면 null.</summary>
    public static Task<string?> PickColorAsync(Window owner, string initialHex)
    {
        var dialog = NewDialog("색 고르기", 360);
        var picker = new ColorView
        {
            Color = ColorUtil.Parse(initialHex, Colors.Black),
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            Height = 420,
        };
        var ok = DialogButton("확인", primary: true);
        var cancel = DialogButton("취소", primary: false);
        ok.Click += (_, _) => dialog.Close(ColorUtil.ToHex(picker.Color));
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Content = DialogBody(picker, ok, cancel);
        return dialog.ShowDialog<string?>(owner);
    }

    private static Window NewDialog(string title, double width) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        CanMinimize = false,
        CanMaximize = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = GameArt.Brush(GameArt.Background),
        Icon = AppIcon.Window,
    };

    private static Control DialogBody(Control content, GameButton primary, GameButton secondary)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0), Children = { primary, secondary },
        };
        return new StackPanel { Margin = new Thickness(20, 18, 20, 16), Children = { content, buttons } };
    }

    public static GameButton DialogButton(string text, bool primary) => new(text, primary) { MinWidth = 80 };
}

/// <summary>화면 오른쪽 아래에 잠깐 떴다 사라지는 알림. 누르면 onClick을 부르고 닫힌다.</summary>
internal static class Toast
{
    private static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(6);

    public static void Show(string title, string message, Action? onClick = null)
    {
        var body = new Border
        {
            Background = GameArt.Brush(GameArt.Card),
            BorderBrush = GameArt.Brush(GameArt.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12),
            Cursor = onClick is null ? Cursor.Default : new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.Bold, FontSize = GameArt.FontLarge, Foreground = GameArt.Brush(GameArt.Text) },
                    new TextBlock { Text = message, FontSize = GameArt.FontBody, Foreground = GameArt.Brush(GameArt.SubText), TextWrapping = TextWrapping.Wrap, MaxWidth = 300 },
                },
            },
        };
        var window = new Window
        {
            Title = title,
            SystemDecorations = SystemDecorations.None,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-32000, -32000),
            Content = body,
        };
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        void CloseOnce()
        {
            if (!closed) window.Close();
        }
        body.PointerReleased += (_, _) =>
        {
            onClick?.Invoke();
            CloseOnce();
        };
        // 크기가 정해진 뒤에 화면 오른쪽 아래(작업표시줄·독 위)로 옮긴다.
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (window.Screens.Primary is not { } screen) return;
            double s = window.DesktopScaling > 0 ? window.DesktopScaling : 1;
            var area = screen.WorkingArea;
            int margin = (int)(16 * s);
            window.Position = new PixelPoint(
                area.Right - (int)(window.Bounds.Width * s) - margin,
                area.Bottom - (int)(window.Bounds.Height * s) - margin);
        }, DispatcherPriority.Background);
        window.Show();
        DispatcherTimer.RunOnce(CloseOnce, ShowFor);
    }
}

/// <summary>앱 아이콘(Woodada.ico). 창과 Windows 트레이가 함께 쓴다.</summary>
internal static class AppIcon
{
    private static WindowIcon? _icon;

    public static WindowIcon Window => _icon ??= Load();

    public static WindowIcon Load() => FromResource("Woodada.ico");

    /// <summary>macOS 메뉴 막대 아이콘. 검정 + 투명 템플릿이라 macOS가 막대 색에 맞춰 칠한다.</summary>
    public static WindowIcon LoadMenuBarTemplate() => FromResource("MenuBarTemplate.png");

    private static WindowIcon FromResource(string name)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"리소스 없음: {name}");
        return new WindowIcon(stream);
    }
}
