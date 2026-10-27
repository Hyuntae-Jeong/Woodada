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

    /// <summary>
    /// 다음 업데이트 확인 때 한 번 울리는 타이머. 울리면 멈추고, 확인을 마친 뒤 6시간 뒤로 다시 건다.
    /// 그 사이에는 깨어나지 않아 CPU를 쓰지 않는다.
    /// </summary>
    private readonly DispatcherTimer _updateTimer = new();

    /// <summary>설정 창이 열려 있을 때 받아 둔 업데이트. 창을 닫으면 설치한다.</summary>
    private Updater.Staged? _pendingInstall;

    /// <summary>바꿔 끼울 수 없을 때(macOS .dmg 안 등) 메뉴 맨 위에 붙이는 "새 버전 받기" 항목과, 마지막으로 알림을 띄운 버전.</summary>
    private NativeMenuItem? _downloadItem;
    private string _downloadUrl = "";
    private Version? _notifiedVersion;

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
        // 로컬 빌드(0.0.0)는 업데이트하지 않는다.
        if (Updater.IsReleaseBuild)
        {
            _updateTimer.Tick += (_, _) => _ = RunUpdateCheckAsync();
            // 마지막 확인 뒤로 6시간이 지났으면 바로, 아니면 남은 시간 뒤에 확인한다. 껐다 켜도 주기가 이어진다.
            // 설치에 실패해 다시 뜬 실행(checkForUpdate=false)은 같은 업데이트를 또 받지 않도록 6시간을 꽉 채워 기다린다.
            var wait = checkForUpdate ? Updater.TimeUntilNextCheck() : Updater.CheckInterval;
            if (wait <= TimeSpan.Zero) _ = RunUpdateCheckAsync();
            else ScheduleUpdateCheck(wait);
        }
    }

    private void ScheduleUpdateCheck(TimeSpan wait)
    {
        _updateTimer.Interval = wait;
        _updateTimer.Start();
    }

    /// <summary>새 릴리즈를 한 번 확인하고 6시간 뒤로 다음 확인을 건다. 실패해도 다음 확인은 6시간 뒤다.</summary>
    private async Task RunUpdateCheckAsync()
    {
        _updateTimer.Stop();
        await CheckForUpdateAsync();
        if (!_updateCts.IsCancellationRequested) ScheduleUpdateCheck(Updater.CheckInterval);
    }

    /// <summary>
    /// 새 릴리즈가 있으면 묻지 않고 바꿔 끼운 뒤 다시 실행한다. 내려받기는 백그라운드에서, 설치 단계로 넘기고 종료하는 건 UI 스레드에서 한다.
    /// 설정 창이 열려 있으면 쓰던 화면이 갑자기 닫히지 않게 창을 닫을 때까지 설치를 미룬다.
    /// macOS에서 바꿔 끼울 수 없는 곳(.dmg 안, 쓰기 권한 없는 폴더)에서 돌면 알림과 메뉴 항목으로 새 버전을 알려 주기만 한다.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        try
        {
            if (_pendingInstall is not null) return; // 이미 받아 둔 업데이트가 설정 창이 닫히길 기다리는 중
            if (OperatingSystem.IsWindows() || Updater.CanInstallHere())
            {
                var staged = await Task.Run(() => Updater.PrepareAsync(_updateCts.Token));
                if (staged is null || _updateCts.IsCancellationRequested) return;
                if (_settingsWindow is not null)
                {
                    _pendingInstall = staged;
                    Updater.Log($"v{staged.Version.ToString(3)}을 받아 두었습니다. 설정 창을 닫으면 설치합니다");
                    return;
                }
                Install(staged);
                return;
            }

            var release = await Task.Run(() => Updater.FindNewerReleaseAsync(_updateCts.Token));
            if (release is null || _updateCts.IsCancellationRequested) return;
            NotifyNewRelease(release);
        }
        catch (Exception ex)
        {
            // 버려진 Task라 여기서 잡지 않으면 실패가 아무 흔적 없이 사라진다. 앱은 지금 버전으로 계속 돈다.
            Updater.Log($"업데이트 확인 실패: {ex.Message}");
        }
    }

    private void Install(Updater.Staged staged)
    {
        if (Updater.StartInstaller(staged)) _lifetime.Shutdown();
    }

    /// <summary>
    /// 바꿔 끼울 수 없을 때만 온다. 메뉴 맨 위에 "새 버전 받기"를 하나만 두고 버전을 맞춘다.
    /// 알림은 새 버전마다 한 번만 띄운다. 6시간 뒤 확인에서 같은 버전이 또 보여도 다시 알리지 않는다.
    /// </summary>
    private void NotifyNewRelease(Updater.Release release)
    {
        string version = release.Version.ToString(3);
        _downloadUrl = release.PageUrl;
        if (_downloadItem is null)
        {
            _downloadItem = new NativeMenuItem();
            _downloadItem.Click += (_, _) => OpenUrl(_downloadUrl);
            _menu.Items.Insert(0, _downloadItem);
        }
        _downloadItem.Header = $"새 버전 v{version} 받기…";

        if (release.Version == _notifiedVersion) return;
        _notifiedVersion = release.Version;
        Toast.Show("새 버전이 나왔어요", $"Woodada v{version}을 받을 수 있어요. 눌러서 다운로드 페이지를 열어요.",
            () => OpenUrl(_downloadUrl));
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
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            if (_pendingInstall is { } staged)
            {
                _pendingInstall = null;
                Install(staged);
            }
        };
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
        // 러너마다 스킨 한 줄씩. 한 러너는 스킨을 하나만 입으니 다른 스킨을 고르면 바꿔 입는다.
        var items = new List<(string Id, RunnerSkin Skin, NativeMenuItem Item)>();
        foreach (var r in _store.Current.Runners)
        {
            _menu.Items.Add(new NativeMenuItemSeparator());
            string id = r.Id;
            foreach (var skin in RunnerSkins.All)
            {
                var item = new NativeMenuItem($"[개발용] {SettingsWindow.RunnerName(r)} {RunnerSkins.DisplayName(skin)} 스킨")
                {
                    ToggleType = NativeMenuItemToggleType.CheckBox,
                };
                item.Click += (_, _) => ToggleSkin(id, skin);
                _menu.Items.Add(item);
                items.Add((id, skin, item));
            }
        }
        _menu.Items.Add(new NativeMenuItemSeparator());

        void Sync()
        {
            foreach (var (id, skin, item) in items)
                item.IsChecked = RunnerSkins.Parse(_store.Current.Runners.FirstOrDefault(x => x.Id == id)?.Skin) == skin;
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
        _updateTimer.Stop();
        _tooltipTimer.Stop();
        _settingsWindow?.Close();
        _tray.IsVisible = false;
        _tray.Dispose();
        _overlay.Dispose();
    }
}
