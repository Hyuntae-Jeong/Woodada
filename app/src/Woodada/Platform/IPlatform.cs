using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Woodada.Platform;

/// <summary>화면 가장자리에 붙은 막대(Windows 작업표시줄, macOS 독)가 어느 쪽에 있는지.</summary>
public enum BarEdge { Left, Top, Right, Bottom }

/// <summary>
/// 모니터 하나와 그 모니터의 작업표시줄·독 위치. 좌표는 Avalonia 화면 단위다
/// (Windows는 물리 픽셀, macOS는 포인트).
/// </summary>
/// <param name="Bounds">막대 사각형. 막대가 없으면 화면 아래 선(높이 0).</param>
/// <param name="HasBar">막대가 없으면(숨긴 독, 작업표시줄이 없는 보조 모니터) false. 러너는 화면 바닥을 달린다.</param>
public readonly record struct ScreenBar(PixelRect Bounds, BarEdge Edge, PixelRect Screen, bool HasBar)
{
    public static ScreenBar None(PixelRect screen) =>
        new(new PixelRect(screen.X, screen.Bottom, screen.Width, 0), BarEdge.Bottom, screen, false);
}

/// <summary>모니터가 알려 준 패널 실제 크기.</summary>
public readonly record struct MonitorSize(int WidthMm, int HeightMm, string? Name);

/// <summary>
/// OS마다 달라야 하는 부분만 모은 곳. 나머지(러너 움직임, 오버레이 그리기, 설정 창, 보상)는 모두 공통 코드다.
/// 구현은 Windows/·Mac/ 폴더에 하나씩 있고 <see cref="Current"/>가 실행 중인 OS에 맞는 것을 고른다.
/// </summary>
internal interface IPlatform
{
    /// <summary>설정·기록 폴더가 들어갈 곳(%APPDATA%, ~/Library/Application Support).</summary>
    string DataRoot { get; }

    /// <summary>러너가 달릴 막대 이름. 설정 창 문구에 쓴다("작업표시줄", "독").</summary>
    string BarName { get; }

    /// <summary>트레이 메뉴의 자동 실행 항목 이름.</summary>
    string StartupMenuLabel { get; }

    /// <summary>전체 화면 앱을 알아챌 수 있는지. 아니면 설정 창에서 그 옵션을 숨긴다.</summary>
    bool CanDetectFullScreen { get; }

    // ───────── 어디서 달릴지 ─────────

    /// <summary>이 모니터의 작업표시줄·독 위치.</summary>
    ScreenBar FindBar(Screen screen);

    /// <summary>설정에 저장할 모니터 식별자. 다시 켜도 같은 모니터를 가리켜야 한다.</summary>
    string ScreenKey(Screen screen);

    /// <summary>모니터 실제 크기. 알 수 없으면 null(화면 배율로 추정한다).</summary>
    MonitorSize? MeasureMonitor(Screen screen);

    /// <summary>게임·발표처럼 전체 화면 앱이 떠 있는지.</summary>
    bool IsFullScreenAppActive();

    /// <summary>작업표시줄·독이 밝은 색인지. 반투명 스킨이 배경에 묻히지 않게 외곽선 톤을 고르는 데 쓴다.</summary>
    bool IsBarLight();

    // ───────── 오버레이 창 ─────────

    /// <summary>오버레이 창이 처음 뜰 때 한 번: 클릭 통과, 포커스 안 뺏기, 모든 데스크톱에 보이기.</summary>
    void PrepareOverlay(Window window);

    /// <summary>작업표시줄을 누르면 그 위로 올라오는 OS가 있어 주기적으로 다시 맨 위로 올린다.</summary>
    void KeepOverlayOnTop(Window window);

    // ───────── 지표 ─────────

    /// <summary>부팅 후 누적된 CPU 시간. 두 번 읽은 차이로 사용률을 낸다.</summary>
    bool TryReadCpuTimes(out ulong idle, out ulong total);

    /// <summary>메모리 사용률 0~100.</summary>
    double? ReadMemoryPercent();

    // ───────── 자동 실행 ─────────

    bool IsStartupEnabled();
    void SetStartup(bool enabled);

    /// <summary>
    /// 지금 실행 파일 경로를 자동 실행에 등록해도 되는지. 맥에서 DMG 안이나 임시 격리 위치에서 띄운 앱은
    /// 곧 사라질 경로라 처음 실행 기본값으로 등록하지 않는다.
    /// </summary>
    bool CanRegisterStartupHere { get; }

    /// <summary>이름을 바꾸기 전 버전이 남긴 자동 실행 등록을 정리한다.</summary>
    void MigrateLegacyStartup();
}

internal static class PlatformServices
{
    public static IPlatform Current { get; } = Create();

    private static IPlatform Create()
    {
        if (OperatingSystem.IsWindows()) return new Windows.WindowsPlatform();
        if (OperatingSystem.IsMacOS()) return new Mac.MacPlatform();
        throw new PlatformNotSupportedException("Woodada는 Windows와 macOS에서만 돌아갑니다.");
    }
}
