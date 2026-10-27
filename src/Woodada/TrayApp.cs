using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Woodada.Platform;
using Woodada.Rendering;
using Woodada.Rewards;
using Woodada.Settings;
using Woodada.Sprites;
using Woodada.Stats;
using Woodada.UI;
using Woodada.Update;

namespace Woodada;

/// <summary>창 없이 트레이 아이콘(macOS는 메뉴 막대 아이콘)으로 상주하는 앱 본체.</summary>
internal sealed class TrayApp : IDisposable
{
    private readonly IPlatform _platform = PlatformServices.Current;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly SettingsStore _store = new();
    private readonly StatsStore _stats = new();
    private readonly RewardStore _rewards;
    private readonly OverlayController _overlay;
    private readonly TrayIcon _tray;
    private readonly NativeMenu _menu = new();
    private readonly NativeMenuItem _startupItem;
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _updateCts = new();
    private SettingsWindow? _settingsWindow;

    public TrayApp(IClassicDesktopStyleApplicationLifetime lifetime, bool openSettings, bool updated, bool checkForUpdate)
    {
        _lifetime = lifetime;
        TryRun(_platform.MigrateLegacyStartup);
        ApplyStartupDefault();
        _rewards = new RewardStore(_stats);
        _rewards.GrantStartingItems(_store.Current);
        _overlay = new OverlayController(_store, _stats);

        var settingsItem = new NativeMenuItem("설정…");
        settingsItem.Click += (_, _) => ShowSettings();
        _startupItem = new NativeMenuItem(_platform.StartupMenuLabel)
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = SafeIsStartupEnabled(),
        };
        _startupItem.Click += (_, _) => ToggleStartup();
        var quitItem = new NativeMenuItem("종료");
        quitItem.Click += (_, _) => _lifetime.Shutdown();
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(_startupItem);
        if (AppInfo.IsDev) AddDevSkinItems();
        _menu.Items.Add(quitItem);

        _tray = new TrayIcon
        {
            // macOS는 다른 메뉴 막대 아이콘처럼 한 색으로 칠해지는 템플릿 아이콘을 쓴다.
            Icon = OperatingSystem.IsMacOS() ? AppIcon.LoadMenuBarTemplate() : AppIcon.Load(),
            ToolTipText = AppInfo.Tag + "Woodada",
            Menu = _menu,
            IsVisible = true,
        };
        // 아이콘을 넣은 뒤에 켜야 그 그림에 템플릿 표시가 붙는다.
        if (OperatingSystem.IsMacOS()) MacOSProperties.SetIsTemplateIcon(_tray, true);
        // Windows에서 아이콘을 왼쪽 클릭하면 설정을 연다. macOS는 누르면 메뉴가 열린다.
        _tray.Clicked += (_, _) => ShowSettings();

        _tooltipTimer.Tick += (_, _) =>
            _tray.ToolTipText = $"{AppInfo.Tag}CPU {_overlay.Sampler.Cpu:0}% · Memory {_overlay.Sampler.Memory:0}%";
        _tooltipTimer.Start();

        // 로그오프·종료 때는 앱이 정상 종료 경로를 못 탈 수 있으니 기록부터 저장한다.
        _lifetime.ShutdownRequested += (_, _) => _overlay.SaveStats();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _stats.Save();

        if (openSettings) ShowSettings();
        if (updated) Toast.Show("Woodada", $"v{Updater.Current.ToString(3)}(으)로 업데이트했어요.");
        if (checkForUpdate) _ = CheckForUpdateAsync();
    }

    /// <summary>
    /// 켜질 때마다 새 릴리즈를 확인한다.
    /// Windows: 있으면 묻지 않고 바꿔 끼운 뒤 다시 실행한다. 내려받기는 백그라운드에서, 설치 단계로 넘기고 종료하는 건 UI 스레드에서 한다.
    /// macOS: 알림과 메뉴 항목으로 새 버전을 알려 주기만 한다.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var staged = await Task.Run(() => Updater.PrepareAsync(_updateCts.Token));
                if (staged is null || _updateCts.IsCancellationRequested) return;
                if (Updater.StartInstaller(staged)) _lifetime.Shutdown();
                return;
            }

            var release = await Task.Run(() => Updater.FindNewerReleaseAsync(_updateCts.Token));
            if (release is null || _updateCts.IsCancellationRequested) return;
            string version = release.Version.ToString(3);
            var download = new NativeMenuItem($"새 버전 v{version} 받기…");
            download.Click += (_, _) => OpenUrl(release.PageUrl);
            _menu.Items.Insert(0, download);
            Toast.Show("새 버전이 나왔어요", $"Woodada v{version}을 받을 수 있어요. 눌러서 다운로드 페이지를 열어요.",
                () => OpenUrl(release.PageUrl));
        }
        catch (Exception ex)
        {
            // 버려진 Task라 여기서 잡지 않으면 실패가 아무 흔적 없이 사라진다. 앱은 지금 버전으로 계속 돈다.
            Updater.Log($"업데이트 확인 실패: {ex.Message}");
        }
    }

    private static void OpenUrl(string url)
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

    private void ShowSettings(SettingsWindow.Page page = SettingsWindow.Page.Cats)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.ShowPage(page);
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_store, _stats, _rewards, _overlay.Sampler);
        _settingsWindow.ShowPage(page);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>
    /// 처음 설치해서 띄우면 "시작 시 실행"을 켠다. 한 번만 하므로 그 뒤 사용자가 끄면 꺼진 채로 둔다.
    /// 개발용 빌드는 PC를 켤 때마다 뜨면 귀찮으니 건너뛴다.
    /// 맥에서 DMG 안에서 바로 띄운 경우처럼 등록할 수 없는 자리면 표시를 남기지 않고 다음 실행 때 다시 해 본다.
    /// </summary>
    private void ApplyStartupDefault()
    {
        var settings = _store.Current;
        if (settings.StartupDefaultApplied || AppInfo.IsDev || !_platform.CanRegisterStartupHere) return;
        try { _platform.SetStartup(true); }
        catch (Exception) { return; } // 등록이 막혔으면(권한·보안 프로그램) 다음 실행 때 다시 해 본다
        var updated = settings.Clone();
        updated.StartupDefaultApplied = true;
        _store.Update(updated);
    }

    /// <summary>
    /// 스페셜 스킨은 출시 전이라 설정 창에 넣지 않고, 개발용 앱 트레이에서만 입혀 본다.
    /// 러너 목록(CPU·메모리)은 고정이라 메뉴는 한 번만 만들고, 설정이 바뀔 때 체크 표시만 맞춘다.
    /// </summary>
    private void AddDevSkinItems()
    {
        _menu.Items.Add(new NativeMenuItemSeparator());
        var items = new List<(string Id, NativeMenuItem Item)>();
        foreach (var r in _store.Current.Runners)
        {
            string id = r.Id;
            var item = new NativeMenuItem($"[개발용] {SettingsWindow.RunnerName(r)} 유령 스킨")
            {
                ToggleType = NativeMenuItemToggleType.CheckBox,
            };
            item.Click += (_, _) => ToggleSkin(id, RunnerSkin.Ghost);
            _menu.Items.Add(item);
            items.Add((id, item));
        }
        _menu.Items.Add(new NativeMenuItemSeparator());

        void Sync()
        {
            foreach (var (id, item) in items)
                item.IsChecked = RunnerSkins.Parse(_store.Current.Runners.FirstOrDefault(x => x.Id == id)?.Skin) == RunnerSkin.Ghost;
        }
        Sync();
        _store.Changed += (_, _) => Sync();
    }

    private void ToggleSkin(string id, RunnerSkin skin)
    {
        var settings = _store.Current.Clone();
        var r = settings.Runners.FirstOrDefault(x => x.Id == id);
        if (r is null) return;
        r.Skin = RunnerSkins.Parse(r.Skin) == skin ? null : RunnerSkins.ToId(skin);
        _store.Update(settings);
    }

    private bool SafeIsStartupEnabled()
    {
        try { return _platform.IsStartupEnabled(); }
        catch (Exception) { return false; }
    }

    private void ToggleStartup()
    {
        TryRun(() => _platform.SetStartup(!_platform.IsStartupEnabled()));
        _startupItem.IsChecked = SafeIsStartupEnabled();
    }

    /// <summary>자동 실행 등록이 막혀 있어도(권한·보안 프로그램) 앱은 계속 돌아야 한다.</summary>
    private static void TryRun(Action action)
    {
        try { action(); }
        catch (Exception) { }
    }

    public void Dispose()
    {
        _updateCts.Cancel();
        _tooltipTimer.Stop();
        _settingsWindow?.Close();
        _tray.IsVisible = false;
        _tray.Dispose();
        _overlay.Dispose();
    }
}
