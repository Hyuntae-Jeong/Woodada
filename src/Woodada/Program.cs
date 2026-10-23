using Avalonia;
using Avalonia.Controls;
using Woodada.Update;

namespace Woodada;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 자동 업데이트의 설치 단계로 실행됐으면 창 없이 exe만 바꿔 끼우고 끝낸다(Windows).
        if (Updater.TryRunInstaller(args)) return;

        // 두 번 실행하면 고양이가 겹쳐 두 배로 뛰므로 단일 인스턴스로 막는다.
        using var mutex = new Mutex(initiallyOwned: true, AppInfo.MutexName, out bool isFirst);
        if (!isFirst) return;

        // 개발용 앱이 설치된 앱의 업데이트 임시 파일을 지우면 안 된다.
        if (!AppInfo.IsDev) Updater.CleanUpStaged();

        App.Args = args;
        // 설정 창을 닫아도 트레이에 남아 있어야 하므로 "종료"를 누를 때만 끝낸다.
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        // macOS: 독에 아이콘을 띄우지 않고 메뉴 막대 아이콘으로만 있는다(.app의 LSUIElement와 같은 효과).
        .With(new MacOSPlatformOptions { ShowInDock = false })
        .LogToTrace();
}
