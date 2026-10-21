using Woodada.Update;

namespace Woodada;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 자동 업데이트의 설치 단계로 실행됐으면 창 없이 exe만 바꿔 끼우고 끝낸다.
        if (Updater.TryRunInstaller(args)) return;

        // 두 번 실행하면 고양이가 겹쳐 두 배로 뛰므로 단일 인스턴스로 막는다.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\Woodada.SingleInstance", out bool isFirst);
        if (!isFirst) return;

        Updater.CleanUpStaged();

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext(
            openSettings: args.Contains("--settings"),
            updated: args.Contains(Updater.UpdatedArg),
            checkForUpdate: !args.Contains(Updater.SkipUpdateArg)));
    }
}
