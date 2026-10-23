using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Woodada.Update;

namespace Woodada;

/// <summary>Avalonia 앱. 화면은 모두 코드로 만들고, 기본 컨트롤 모양만 Fluent 테마를 쓴다.</summary>
internal sealed class App : Application
{
    private TrayApp? _tray;

    public static string[] Args { get; set; } = Array.Empty<string>();

    public override void Initialize()
    {
        Name = "Woodada";
        RequestedThemeVariant = ThemeVariant.Light; // 설정 창 색(Theme)이 밝은 바탕에 맞춰져 있다
        Styles.Add(new FluentTheme());
        // 나만의 색을 고르는 ColorView 모양.
        Styles.Add(new StyleInclude(new Uri("avares://Woodada/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _tray = new TrayApp(desktop,
                openSettings: Args.Contains("--settings"),
                updated: Args.Contains(Updater.UpdatedArg),
                checkForUpdate: !Args.Contains(Updater.SkipUpdateArg));
            desktop.Exit += (_, _) => _tray?.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
