using System.Drawing.Drawing2D;
using Microsoft.Win32;
using Woodada.Interop;
using Woodada.Rendering;
using Woodada.Rewards;
using Woodada.Settings;
using Woodada.Sprites;
using Woodada.Stats;
using Woodada.UI;
using Woodada.Update;

namespace Woodada;

/// <summary>창 없이 트레이 아이콘으로 상주하는 앱 본체.</summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = AppInfo.StartupValueName;

    private readonly SettingsStore _store = new();
    private readonly StatsStore _stats = new();
    private readonly RewardStore _rewards;
    private readonly OverlayController _overlay;
    private readonly NotifyIcon _tray;
    private readonly Icon _trayIcon;
    private readonly Icon _windowIcon;
    private readonly System.Windows.Forms.Timer _tooltipTimer = new() { Interval = 1000 };
    private readonly ToolStripMenuItem _startupItem;
    private readonly CancellationTokenSource _updateCts = new();
    private SettingsForm? _settingsForm;

    public TrayAppContext(bool openSettings = false, bool updated = false, bool checkForUpdate = true)
    {
        MigrateLegacyStartup();
        _rewards = new RewardStore(_stats);
        _rewards.GrantStartingItems(_store.Current);
        _overlay = new OverlayController(_store, _stats);
        _trayIcon = LoadAppIcon(SystemInformation.SmallIconSize);
        _windowIcon = LoadAppIcon(SystemInformation.IconSize);

        _startupItem = new ToolStripMenuItem("Windows 시작 시 실행", null, (_, _) => ToggleStartup())
        {
            Checked = IsStartupEnabled(),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("설정…", null, (_, _) => ShowSettings()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripMenuItem("종료", null, (_, _) => ExitThread()));

        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = AppInfo.Tag + "Woodada",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowSettings();
        };

        _tooltipTimer.Tick += (_, _) =>
            _tray.Text = $"{AppInfo.Tag}CPU {_overlay.Sampler.Cpu:0}% · Memory {_overlay.Sampler.Memory:0}%";
        _tooltipTimer.Start();

        if (openSettings) ShowSettings();
        if (updated)
            _tray.ShowBalloonTip(3000, "Woodada", $"v{Updater.Current.ToString(3)}(으)로 업데이트했어요.", ToolTipIcon.Info);

        if (checkForUpdate) _ = UpdateInBackgroundAsync();
    }

    /// <summary>
    /// 켜질 때마다 새 릴리즈를 확인해, 있으면 묻지 않고 바꿔 끼운 뒤 다시 실행한다.
    /// 내려받기는 백그라운드에서 하고, 설치 단계로 넘기고 종료하는 건 UI 스레드에서 한다.
    /// 실제 exe 교체는 이 프로세스가 끝난 뒤 설치 단계(Updater.TryRunInstaller)가 한다.
    /// </summary>
    private async Task UpdateInBackgroundAsync()
    {
        try
        {
            var staged = await Task.Run(() => Updater.PrepareAsync(_updateCts.Token));
            if (staged is null || _updateCts.IsCancellationRequested) return;
            if (Updater.StartInstaller(staged)) ExitThread();
        }
        catch (Exception ex)
        {
            // 버려진 Task라 여기서 잡지 않으면 실패가 아무 흔적 없이 사라진다. 앱은 지금 버전으로 계속 돈다.
            Updater.Log($"자동 업데이트 실패: {ex.Message}");
        }
    }

    private void ShowSettings(SettingsForm.Page page = SettingsForm.Page.Cats)
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.ShowPage(page);
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_store, _stats, _rewards, _overlay.Sampler) { Icon = _windowIcon };
        _settingsForm.ShowPage(page);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    /// <summary>
    /// 옛 이름으로 "시작 시 실행"이 등록돼 있으면 새 이름·현재 exe 경로로 옮긴다.
    /// 그대로 두면 로그인할 때 없는 exe를 실행하려다 실패한다.
    /// </summary>
    private static void MigrateLegacyStartup()
    {
        if (AppInfo.IsDev) return; // 진짜 앱의 시작 프로그램 등록은 진짜 앱만 정리한다
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

    /// <summary>
    /// 앱 아이콘(Woodada.ico)에서 원하는 크기의 프레임을 꺼낸다.
    /// 트레이는 DPI에 맞는 작은 아이콘 크기(100%: 16px, 125%: 20px …)를 요청해야 흐리지 않다.
    /// </summary>
    private static Icon LoadAppIcon(Size size)
    {
        using var stream = typeof(TrayAppContext).Assembly.GetManifestResourceStream("Woodada.ico")
                           ?? throw new InvalidOperationException("리소스 없음: Woodada.ico");
        return new Icon(stream, size);
    }

    protected override void ExitThreadCore()
    {
        _updateCts.Cancel();
        _tooltipTimer.Dispose();
        _settingsForm?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _overlay.Dispose();
        _trayIcon.Dispose();
        _windowIcon.Dispose();
        base.ExitThreadCore();
    }
}
