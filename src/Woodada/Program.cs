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
        using var instance = TryLockSingleInstance();
        if (instance is null) return;

        // 개발용 앱이 설치된 앱의 업데이트 임시 파일을 지우면 안 된다.
        if (!AppInfo.IsDev) Updater.CleanUpStaged();

        App.Args = args;
        // 설정 창을 닫아도 트레이에 남아 있어야 하므로 "종료"를 누를 때만 끝낸다.
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>이미 떠 있으면 null. 돌려받은 잠금은 앱이 끝날 때까지 쥐고 있어야 한다.</summary>
    private static IDisposable? TryLockSingleInstance()
    {
        if (OperatingSystem.IsWindows())
        {
            var mutex = new Mutex(initiallyOwned: true, AppInfo.MutexName, out bool isFirst);
            if (isFirst) return mutex;
            mutex.Dispose();
            return null;
        }

        // macOS: .NET의 이름 있는 Mutex는 프로세스 세션(setsid)마다 따로 잡힌다.
        // Finder·로그인 항목·터미널에서 띄운 앱은 세션이 서로 달라 서로를 못 보므로 파일 잠금(flock)을 쓴다.
        // 앱이 비정상 종료해도 OS가 잠금을 풀어 준다.
        try
        {
            Directory.CreateDirectory(AppInfo.DataFolder);
            return new FileStream(Path.Combine(AppInfo.DataFolder, "instance.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null; // 다른 Woodada가 잠금을 쥐고 있다
        }
        catch (Exception)
        {
            // 폴더 권한 같은 문제로 잠금 파일을 못 만들면, 아예 안 뜨는 것보다 그냥 띄우는 게 낫다.
            return Stream.Null;
        }
    }

    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        // macOS: 독에 아이콘을 띄우지 않고 메뉴 막대 아이콘으로만 있는다(.app의 LSUIElement와 같은 효과).
        .With(new MacOSPlatformOptions { ShowInDock = false })
        .LogToTrace();
}
