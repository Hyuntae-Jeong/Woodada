namespace Woodada;

/// <summary>
/// 설치된 앱과 개발용 빌드를 가르는 이름들.
/// Debug 빌드(dev.cmd, dotnet run)는 설치된 Woodada와 나란히 띄워 볼 수 있도록 실행 잠금·저장 폴더·
/// 시작 프로그램 등록 이름을 따로 쓴다. 릴리즈 CI는 Release로 빌드하므로 사용자에게는 영향이 없다.
/// </summary>
internal static class AppInfo
{
#if DEBUG
    // const로 두면 분기 한쪽이 늘 "도달할 수 없는 코드" 경고가 된다.
    public static readonly bool IsDev = true;

    /// <summary>%APPDATA% 아래 설정·기록 폴더.</summary>
    public const string DataFolderName = "Woodada-dev";

    public const string MutexName = @"Local\Woodada.Dev.SingleInstance";

    /// <summary>HKCU\...\Run 값 이름. 개발용 앱이 진짜 앱의 "시작 시 실행"을 덮어쓰지 않게 한다.</summary>
    public const string StartupValueName = "Woodada-dev";

    /// <summary>트레이 툴팁·창 제목 앞에 붙여 어느 쪽 앱인지 바로 알아보게 한다.</summary>
    public const string Tag = "[DEV] ";
#else
    public static readonly bool IsDev = false;
    public const string DataFolderName = "Woodada";
    public const string MutexName = @"Local\Woodada.SingleInstance";
    public const string StartupValueName = "Woodada";
    public const string Tag = "";
#endif

    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DataFolderName);
}
