using System.Diagnostics;
using System.Runtime.InteropServices;
using Woodada.Update;

namespace Woodada;

/// <summary>브라우저로 여는 GitHub 페이지들과, 설정 창에 보여 줄 버전 글자.</summary>
internal static class ExternalLinks
{
    private const string RepoUrl = "https://github.com/Hyuntae-Jeong/Woodada";

    /// <summary>"v1.2.2". 개발용 빌드는 버전이 0.0.0이라 숫자 대신 개발용이라고 적는다.</summary>
    public static string VersionText => AppInfo.IsDev ? Loc.T("개발용", "dev build") : $"v{Updater.Current.ToString(3)}";

    /// <summary>
    /// 버전·OS 칸을 미리 채운 버그 제보 양식(.github/ISSUE_TEMPLATE/bug_report.yml).
    /// GitHub 이슈 양식은 글자 칸(input·textarea)만 id를 쿼리로 넘겨 채울 수 있다. 드롭다운은 채워지지 않는다.
    /// 개발용 빌드는 버전이 0.0.0이라 버전 칸을 비워 둔다.
    /// </summary>
    public static string BugReportUrl()
    {
        var query = new List<string> { "template=bug_report.yml" };
        if (!AppInfo.IsDev) query.Add("version=" + Uri.EscapeDataString(Updater.Current.ToString(3)));
        if (OsText() is { } os) query.Add("os=" + Uri.EscapeDataString(os));
        return $"{RepoUrl}/issues/new?{string.Join('&', query)}";
    }

    /// <summary>"Windows 11 (build 26200)", "macOS 15.1 (Apple Silicon)". OS 버전에 따라 생기는 버그가 있어 번호까지 적는다.</summary>
    private static string? OsText()
    {
        var v = Environment.OSVersion.Version;
        // Windows 11도 주 버전은 10이라 빌드 번호(22000부터 11)로 가른다.
        if (OperatingSystem.IsWindows()) return $"Windows {(v.Build >= 22000 ? 11 : 10)} (build {v.Build})";
        if (OperatingSystem.IsMacOS())
            return $"macOS {v.ToString(2)} ({(RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "Apple Silicon" : "Intel")})";
        return null;
    }

    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Updater.Log($"페이지를 열지 못했습니다: {ex.Message}");
        }
    }
}
