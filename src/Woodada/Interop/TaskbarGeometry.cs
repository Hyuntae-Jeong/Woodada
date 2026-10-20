namespace Woodada.Interop;

public enum TaskbarEdge { Left = 0, Top = 1, Right = 2, Bottom = 3 }

/// <summary>모니터 하나와 그 모니터의 작업표시줄 위치(물리 픽셀).</summary>
/// <param name="Bounds">작업표시줄 창 사각형. 작업표시줄이 없는 모니터면 화면 아래 선(높이 0).</param>
/// <param name="HasTaskbar">"모든 디스플레이에 작업 표시줄 표시"가 꺼져 있으면 보조 모니터엔 없다.</param>
public readonly record struct TaskbarGeometry(Rectangle Bounds, TaskbarEdge Edge, Rectangle Screen, bool HasTaskbar)
{
    /// <summary>
    /// 지정한 모니터의 작업표시줄. 그 모니터가 연결돼 있지 않으면 주 모니터로 대체한다.
    /// </summary>
    public static TaskbarGeometry Query(string? deviceName)
    {
        var screen = MonitorCatalog.Resolve(deviceName);

        // 자동 숨김 중에는 작업표시줄 창이 화면 밖으로 밀려나 있으므로, 실제 창 위치를 쓰면
        // 작업표시줄이 내려갈 때 러너도 같이 내려간다.
        foreach (var hwnd in EnumerateTaskbars())
        {
            if (System.Windows.Forms.Screen.FromHandle(hwnd).DeviceName != screen.DeviceName) continue;
            if (!NativeMethods.GetWindowRect(hwnd, out var r)) continue;
            var rect = r.ToRectangle();
            return new TaskbarGeometry(rect, EdgeOf(rect, screen.Bounds), screen.Bounds, true);
        }

        var s = screen.Bounds;
        return new TaskbarGeometry(new Rectangle(s.Left, s.Bottom, s.Width, 0), TaskbarEdge.Bottom, s, false);
    }

    /// <summary>주 모니터는 Shell_TrayWnd, 나머지 모니터는 Shell_SecondaryTrayWnd 창이 작업표시줄이다.</summary>
    public static IEnumerable<IntPtr> EnumerateTaskbars()
    {
        var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) yield return primary;

        IntPtr prev = IntPtr.Zero;
        while (true)
        {
            prev = NativeMethods.FindWindowEx(IntPtr.Zero, prev, "Shell_SecondaryTrayWnd", null);
            if (prev == IntPtr.Zero) yield break;
            yield return prev;
        }
    }

    private static TaskbarEdge EdgeOf(Rectangle bar, Rectangle screen)
    {
        var c = new Point(bar.Left + bar.Width / 2, bar.Top + bar.Height / 2);
        var sc = new Point(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2);
        if (bar.Width >= bar.Height) return c.Y >= sc.Y ? TaskbarEdge.Bottom : TaskbarEdge.Top;
        return c.X >= sc.X ? TaskbarEdge.Right : TaskbarEdge.Left;
    }
}

/// <summary>설정 화면에 보여줄 모니터 목록.</summary>
public static class MonitorCatalog
{
    public sealed record MonitorItem(string DeviceName, string Label);

    public static Screen Resolve(string? deviceName) =>
        Screen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName)
        ?? Screen.PrimaryScreen
        ?? Screen.AllScreens[0];

    public static IReadOnlyList<MonitorItem> List()
    {
        var withTaskbar = TaskbarGeometry.EnumerateTaskbars()
            .Select(h => Screen.FromHandle(h).DeviceName)
            .ToHashSet();

        // 화면 배치 순서(왼쪽→오른쪽)대로 번호를 붙여야 사용자가 보는 배치와 맞는다.
        return Screen.AllScreens
            .OrderBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top)
            .Select((s, i) =>
            {
                var tags = new List<string>();
                if (s.Primary) tags.Add("주 모니터");
                if (!withTaskbar.Contains(s.DeviceName)) tags.Add("작업표시줄 없음 → 화면 아래");
                string suffix = tags.Count > 0 ? $" ({string.Join(", ", tags)})" : "";
                return new MonitorItem(s.DeviceName, $"모니터 {i + 1} — {s.Bounds.Width}×{s.Bounds.Height}{suffix}");
            })
            .ToList();
    }
}
