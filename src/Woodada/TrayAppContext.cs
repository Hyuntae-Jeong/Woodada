using System.Drawing.Drawing2D;
using Microsoft.Win32;
using Woodada.Interop;
using Woodada.Rendering;
using Woodada.Settings;
using Woodada.Sprites;
using Woodada.UI;

namespace Woodada;

/// <summary>창 없이 트레이 아이콘으로 상주하는 앱 본체.</summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Woodada";

    private readonly SettingsStore _store = new();
    private readonly OverlayController _overlay;
    private readonly NotifyIcon _tray;
    private readonly Icon _icon;
    private readonly System.Windows.Forms.Timer _tooltipTimer = new() { Interval = 1000 };
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _startupItem;
    private SettingsForm? _settingsForm;

    public TrayAppContext(bool openSettings = false)
    {
        MigrateLegacyStartup();
        _overlay = new OverlayController(_store);
        _icon = CreateTrayIcon();

        _pauseItem = new ToolStripMenuItem("일시정지", null, (_, _) => TogglePause());
        _startupItem = new ToolStripMenuItem("Windows 시작 시 실행", null, (_, _) => ToggleStartup())
        {
            Checked = IsStartupEnabled(),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("설정…", null, (_, _) => ShowSettings()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        int runnerSlot = menu.Items.Count; // 고양이 표시 토글은 메뉴를 열 때마다 현재 설정으로 다시 만든다
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("종료", null, (_, _) => ExitThread()));

        var runnerItems = new List<ToolStripMenuItem>();
        menu.Opening += (_, _) =>
        {
            foreach (var item in runnerItems) menu.Items.Remove(item);
            runnerItems.Clear();
            foreach (var r in _store.Current.Runners)
            {
                string id = r.Id;
                var item = new ToolStripMenuItem($"{SettingsForm.RunnerName(r)} 표시", null, (_, _) => ToggleRunner(id)) { Checked = r.Enabled };
                runnerItems.Add(item);
                menu.Items.Insert(runnerSlot + runnerItems.Count - 1, item);
            }
        };

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "Woodada",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowSettings();
        };

        _tooltipTimer.Tick += (_, _) =>
            _tray.Text = $"Woodada — CPU {_overlay.Sampler.Cpu:0}% · 메모리 {_overlay.Sampler.Memory:0}%";
        _tooltipTimer.Start();

        if (openSettings) ShowSettings();
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_store, _overlay.Sampler) { Icon = _icon };
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void ToggleRunner(string id)
    {
        var settings = _store.Current.Clone();
        var r = settings.Runners.FirstOrDefault(x => x.Id == id);
        if (r is null) return;
        r.Enabled = !r.Enabled;
        _store.Update(settings);
    }

    private void TogglePause()
    {
        _overlay.Paused = !_overlay.Paused;
        _pauseItem.Checked = _overlay.Paused;
    }

    /// <summary>
    /// 옛 이름으로 "시작 시 실행"이 등록돼 있으면 새 이름·현재 exe 경로로 옮긴다.
    /// 그대로 두면 로그인할 때 없는 exe를 실행하려다 실패한다.
    /// </summary>
    private static void MigrateLegacyStartup()
    {
        string[] legacyNames = { "Udada", "TaskbarRunner" };
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;
        bool hadLegacy = false;
        foreach (var name in legacyNames)
        {
            if (key.GetValue(name) is null) continue;
            key.DeleteValue(name, throwOnMissingValue: false);
            hadLegacy = true;
        }
        if (hadLegacy && key.GetValue(RunValueName) is null)
            key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    private void ToggleStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (IsStartupEnabled()) key.DeleteValue(RunValueName, throwOnMissingValue: false);
        else key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        _startupItem.Checked = IsStartupEnabled();
    }

    /// <summary>트레이 아이콘도 에셋 없이 고양이 스프라이트로 그린다.</summary>
    private static Icon CreateTrayIcon()
    {
        const int px = 32;
        using var bmp = new Bitmap(px, px);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var sprite = SpriteRegistry.Get("cat");
            // 가로로 긴 스프라이트라 32px 칸에 가로 기준으로 맞춘다.
            int fit = 26;
            while (fit > 8 && sprite.Measure(fit).Width > px - 1) fit--;
            var size = sprite.Measure(fit);
            sprite.Draw(g, new RectangleF((px - size.Width) / 2, (px - size.Height) / 2, size.Width, size.Height),
                new RunnerPose(1, 0.2, 0.5), ColorUtil.Parse("#F4A261", Color.Orange));
        }
        IntPtr h = bmp.GetHicon();
        using var tmp = Icon.FromHandle(h);
        var icon = (Icon)tmp.Clone();
        NativeMethods.DestroyIcon(h);
        return icon;
    }

    protected override void ExitThreadCore()
    {
        _tooltipTimer.Dispose();
        _settingsForm?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _overlay.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
