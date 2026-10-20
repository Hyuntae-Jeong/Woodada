namespace Woodada;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 두 번 실행하면 고양이가 겹쳐 두 배로 뛰므로 단일 인스턴스로 막는다.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\Woodada.SingleInstance", out bool isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext(openSettings: args.Contains("--settings")));
    }
}
