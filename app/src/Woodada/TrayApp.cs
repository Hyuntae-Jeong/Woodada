using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Woodada.Platform;
using Woodada.Platform.Mac;
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
    private readonly NativeMenuItem _settingsItem;
    private readonly NativeMenuItem _startupItem;
    private readonly NativeMenuItem _quitItem;
    private readonly DispatcherTimer _tooltipTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _updateCts = new();
    private SettingsWindow? _settingsWindow;

    /// <summary>메뉴 막대 아이콘에 지금 그려 둔 생선 수 글자. null이면 고양이만 있는 기본 아이콘이다.</summary>
    private string? _menuBarCount;

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
    private string _downloadVersion = "";
    private Version? _notifiedVersion;

    public TrayApp(IClassicDesktopStyleApplicationLifetime lifetime, bool openSettings, bool updated, bool checkForUpdate)
    {
        _lifetime = lifetime;
        Loc.Use(_store.Current.Language);
        TryRun(_platform.MigrateLegacyStartup);
        ApplyStartupDefault();
        _rewards = new RewardStore(_stats);
        _rewards.GrantStartingItems(_store.Current);
        if (_rewards.WithoutUnowned(_store.Current) is { } owned) _store.Update(owned);
        _overlay = new OverlayController(_store, _stats);

        _settingsItem = new NativeMenuItem();
        _settingsItem.Click += (_, _) => ShowSettings();
        _startupItem = new NativeMenuItem
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = SafeIsStartupEnabled(),
        };
        _startupItem.Click += (_, _) => ToggleStartup();
        _quitItem = new NativeMenuItem();
        _quitItem.Click += (_, _) => _lifetime.Shutdown();
        _menu.Items.Add(_settingsItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(_quitItem);
        LabelMenu();
        _store.Changed += (_, _) => OnLanguageMaybeChanged();

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
        UpdateMenuBarCount();
        _store.Changed += (_, _) => UpdateMenuBarCount();
        _rewards.Changed += (_, _) => UpdateMenuBarCount(); // 생선을 쓰면 1초를 기다리지 않고 바로 줄인다
        // Windows에서 아이콘을 왼쪽 클릭하면 설정을 연다. macOS는 누르면 메뉴가 열린다.
        _tray.Clicked += (_, _) => ShowSettings();

        _tooltipTimer.Tick += (_, _) =>
        {
            _tray.ToolTipText = $"{AppInfo.Tag}CPU {_overlay.Sampler.Cpu:0}% · Memory {_overlay.Sampler.Memory:0}%";
            UpdateMenuBarCount();
        };
        _tooltipTimer.Start();

        // 로그오프·종료 때는 앱이 정상 종료 경로를 못 탈 수 있으니 기록부터 저장한다.
        _lifetime.ShutdownRequested += (_, _) => _overlay.SaveStats();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _stats.Save();

        if (openSettings) ShowSettings();
        if (_stats.WasReset || _rewards.WasReset) Dispatcher.UIThread.Post(ShowResetNotice);
        if (updated) Toast.Show("Woodada", Loc.T($"v{Updater.Current.ToString(3)}(으)로 업데이트했어요.", $"Updated to v{Updater.Current.ToString(3)}."));
        // 로컬 빌드(0.0.0)는 업데이트하지 않는다.
        if (Updater.IsReleaseBuild)
        {
            _updateTimer.Tick += (_, _) => _ = RunUpdateCheckAsync();
            Updater.DeleteLegacyCheckRecord();
            // 켜질 때 바로 한 번 확인하고, 그 뒤로 켜져 있는 동안 6시간마다 확인한다.
            // 설치에 실패해 다시 뜬 실행(checkForUpdate=false)은 같은 업데이트를 또 받지 않도록 6시간을 꽉 채워 기다린다.
            if (checkForUpdate) _ = RunUpdateCheckAsync();
            else ScheduleUpdateCheck(Updater.CheckInterval);
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
        _downloadVersion = version;
        LabelMenu();

        if (release.Version == _notifiedVersion) return;
        _notifiedVersion = release.Version;
        Toast.Show(Loc.T("새 버전이 나왔어요", "A new version is out"),
            Loc.T($"Woodada v{version}을 받을 수 있어요. 눌러서 다운로드 페이지를 열어요.", $"Woodada v{version} is ready. Click to open the download page."),
            () => OpenUrl(_downloadUrl));
    }

    /// <summary>앱 밖에서 고친 기록을 처음 상태로 돌렸다고 알린다. 금방 사라지는 알림 대신 확인을 눌러야 닫히는 창으로 띄운다.</summary>
    private void ShowResetNotice()
    {
        var (ko, en) = (_stats.WasReset, _rewards.WasReset) switch
        {
            (true, true) => ("달린 기록과 생선, 잠금 해제 기록", "your running record, fish and unlocks"),
            (true, false) => ("달린 기록", "your running record"),
            _ => ("생선과 잠금 해제 기록", "your fish and unlocks"),
        };
        Dialogs.ShowNotice(
            Loc.T("기록을 초기화했어요", "Your records were reset"),
            Loc.T($"저장된 기록이 앱 밖에서 바뀐 것을 발견해서 {ko}을 처음 상태로 돌렸어요.\n\n원래 파일은 기록 폴더에 .tampered를 붙여 남겨 두었어요.",
                  $"Woodada found that its saved records had been changed outside the app, so it reset {en}.\n\nThe original file was kept in the data folder with .tampered added to its name."));
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

    private void ShowSettings(SettingsWindow.Page page = SettingsWindow.Page.Cats, PixelPoint? position = null)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.ShowPage(page);
            _settingsWindow.Activate();
            return;
        }
        var window = new SettingsWindow(_store, _stats, _rewards, _overlay.Sampler);
        _settingsWindow = window;
        window.ShowPage(page);
        if (position is { } p)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = p;
        }
        window.Closed += (_, _) =>
        {
            if (_settingsWindow != window) return; // 언어를 바꿔 다시 연 창이 이어받았다
            _settingsWindow = null;
            if (_pendingInstall is { } staged)
            {
                _pendingInstall = null;
                Install(staged);
            }
        };
        window.Show();
        window.Activate();
    }

    /// <summary>
    /// 일반 탭에서 켜 두면(macOS만) 메뉴 막대 아이콘 옆에 생선 수를 그린다. 1초마다 불리지만
    /// 보이는 글자가 바뀔 때만 그림을 다시 만든다(12.3k처럼 줄인 뒤로는 100마리마다 한 번).
    /// </summary>
    private void UpdateMenuBarCount()
    {
        if (!OperatingSystem.IsMacOS()) return;
        string? text = _store.Current.ShowFishInMenuBar ? MenuBarIcon.FormatCount(_rewards.Fish) : null;
        if (text == _menuBarCount) return;
        _menuBarCount = text;
        try
        {
            _tray.Icon = text is null ? AppIcon.LoadMenuBarTemplate() : MenuBarIcon.Render(text);
            // 숫자를 붙이면 그림이 넓어지니 항목 폭을 그림에 맞춘다. 끄면 원래 정사각형 폭으로 돌린다.
            if (!MacStatusItem.SetFitsImage(text is not null))
                Updater.Log("메뉴 막대 항목을 찾지 못해 폭을 바꾸지 못했습니다");
        }
        catch (Exception ex)
        {
            // 그리기에 실패해도 앱은 기본 아이콘으로 계속 돈다.
            Updater.Log($"메뉴 막대 생선 수를 그리지 못했습니다: {ex.Message}");
        }
    }

    /// <summary>메뉴 글자를 지금 언어로 맞춘다.</summary>
    private void LabelMenu()
    {
        _settingsItem.Header = Loc.T("설정…", "Settings…");
        _startupItem.Header = _platform.StartupMenuLabel;
        _quitItem.Header = Loc.T("종료", "Quit");
        if (_downloadItem is not null)
            _downloadItem.Header = Loc.T($"새 버전 v{_downloadVersion} 받기…", $"Get v{_downloadVersion}…");
    }

    /// <summary>
    /// 일반 탭에서 언어를 바꾸면 메뉴 글자를 바꾸고, 설정 창은 같은 자리·같은 탭(일반)에서 새 언어로 다시 연다.
    /// 창의 글자는 만들 때 정해지는 게 많아서, 하나씩 바꾸기보다 새로 여는 편이 빠짐없다.
    /// 설정 창의 이벤트 처리 도중이라 닫기는 그 처리가 끝난 뒤로 미룬다.
    /// </summary>
    private void OnLanguageMaybeChanged()
    {
        if (!Loc.Use(_store.Current.Language)) return;
        LabelMenu();
        if (_settingsWindow is not { } old) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_settingsWindow != old) return;
            var position = old.Position;
            _settingsWindow = null;
            ShowSettings(SettingsWindow.Page.General, position);
            old.Close();
        });
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
