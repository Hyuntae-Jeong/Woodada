using Avalonia.Controls;
using Avalonia.Platform;

namespace Woodada.Platform;

/// <summary>화면 1단위(Windows 물리 픽셀, macOS 포인트)의 실제 길이. 고양이가 달린 거리를 cm·km로 바꾸는 데 쓴다.</summary>
/// <param name="MillimetersPerPixel">지금 해상도 기준 1단위의 가로 길이(mm).</param>
/// <param name="Measured">모니터가 알려준 실제 크기로 계산했으면 true, 화면 배율로 추정했으면 false.</param>
/// <param name="MonitorName">모니터 모델명. 없으면 null.</param>
/// <param name="WidthMm">패널 가로 길이(mm). 추정이면 0.</param>
/// <param name="HeightMm">패널 세로 길이(mm). 추정이면 0.</param>
public readonly record struct PixelPitch(double MillimetersPerPixel, bool Measured, string? MonitorName, int WidthMm, int HeightMm);

/// <summary>
/// 모니터가 알려주는 패널 실제 크기(mm)를 지금 해상도로 나눠 1단위 길이를 구한다.
/// TV·프로젝터·원격 데스크톱처럼 크기를 알 수 없으면 화면 배율(DPI)로 추정한다.
/// </summary>
public static class MonitorPitch
{
    private static readonly Dictionary<(string Key, int Width, int Height), PixelPitch> Cache = new();

    /// <summary>해상도·배치가 바뀌면 불러서 다시 읽게 한다.</summary>
    public static void Invalidate() => Cache.Clear();

    public static PixelPitch For(Screen screen)
    {
        var key = (PlatformServices.Current.ScreenKey(screen), screen.Bounds.Width, screen.Bounds.Height);
        if (!Cache.TryGetValue(key, out var pitch))
        {
            pitch = Measure(screen) ?? Estimate(screen);
            Cache[key] = pitch;
        }
        return pitch;
    }

    private static PixelPitch? Measure(Screen screen)
    {
        if (PlatformServices.Current.MeasureMonitor(screen) is not { } size) return null;
        int wMm = size.WidthMm, hMm = size.HeightMm;

        // 세로로 돌려 놓은 모니터는 패널 크기(가로 기준)와 화면의 가로·세로가 뒤바뀐다.
        var b = screen.Bounds;
        if ((b.Width > b.Height) != (wMm > hMm)) (wMm, hMm) = (hMm, wMm);

        // 고유 해상도보다 낮춰 쓰면 1단위가 커지므로, 패널 크기를 "지금" 해상도로 나눈다.
        return new PixelPitch((double)wMm / b.Width, true, size.Name, wMm, hMm);
    }

    /// <summary>OS는 화면 배율을 대략 실제 픽셀 밀도에 맞춰 고르므로, 배율 DPI를 실제 DPI로 본다.</summary>
    private static PixelPitch Estimate(Screen screen) =>
        new(25.4 / (96 * Math.Max(0.5, screen.Scaling)), false, null, 0, 0);

    /// <summary>10cm 미만이나 5m 넘는 화면은 잘못 적힌 값으로 본다.</summary>
    public static bool Plausible(int w, int h) => w is >= 100 and <= 5000 && h is >= 50 and <= 5000;
}

/// <summary>모니터 찾기와 설정 화면에 보여줄 모니터 목록.</summary>
public static class ScreenCatalog
{
    public sealed record MonitorItem(string Key, string Label);

    /// <summary>저장된 모니터. 지금 연결돼 있지 않으면 주 모니터로 대체한다.</summary>
    public static Screen? Resolve(Screens screens, string? key)
    {
        var p = PlatformServices.Current;
        return screens.All.FirstOrDefault(s => p.ScreenKey(s) == key) ?? screens.Primary ?? screens.All.FirstOrDefault();
    }

    public static IReadOnlyList<MonitorItem> List(Screens screens)
    {
        var p = PlatformServices.Current;
        // 화면 배치 순서(왼쪽→오른쪽)대로 번호를 붙여야 사용자가 보는 배치와 맞는다.
        return screens.All
            .OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y)
            .Select((s, i) =>
            {
                var tags = new List<string>();
                if (s.IsPrimary) tags.Add("주 모니터");
                if (!p.FindBar(s).HasBar) tags.Add($"{p.BarName} 없음 → 화면 아래");
                string suffix = tags.Count > 0 ? $" ({string.Join(", ", tags)})" : "";
                return new MonitorItem(p.ScreenKey(s), $"모니터 {i + 1} — {s.Bounds.Width}×{s.Bounds.Height}{suffix}");
            })
            .ToList();
    }
}
