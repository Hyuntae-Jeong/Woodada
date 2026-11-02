using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Woodada.Update;

/// <summary>
/// GitHub 최신 릴리즈를 확인하고, 더 새 버전이 있으면 묻지 않고 바꿔 끼운 뒤 다시 실행한다.
/// 확인은 켜질 때 한 번, 그 뒤로 켜져 있는 동안 <see cref="CheckInterval"/>(6시간)마다 한 번 한다(TrayApp이 정한다).
/// macOS의 .app 교체는 Updater.Mac.cs에 있다. 바꿔 끼울 수 없는 곳(.dmg 안, 쓰기 권한 없는 폴더)에서 돌면
/// 알림만 띄운다(<see cref="FindNewerReleaseAsync"/>).
///
/// Windows 교체는 세 단계로 한다.
/// 1. 지금 앱이 새 exe를 임시 폴더에 받아 검증하고, 그 exe를 설치 모드(<see cref="InstallArg"/>)로 띄운 뒤 정상 종료한다.
/// 2. 설치 모드의 새 exe가 지금 앱이 끝나길 기다렸다가 자기 자신을 설치 위치에 복사한다(<see cref="TryRunInstaller"/>).
/// 3. 설치된 exe를 다시 띄우고 설치 모드는 끝난다. 다시 뜬 앱이 임시 폴더의 exe를 지운다(<see cref="CleanUpStaged"/>).
///
/// 실행 중인 exe 파일은 이름을 바꾸는 것조차 하지 않는다. self-contained 단일 exe는 .NET 라이브러리를 필요할 때마다
/// exe 경로에서 읽어 오므로, 실행 중에 그 경로의 파일이 바뀌면 다음 라이브러리를 못 찾고 FileNotFoundException으로 죽는다.
/// </summary>
internal static partial class Updater
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Hyuntae-Jeong/Woodada/releases/latest";

    /// <summary>CI가 릴리즈에 올리는 파일. .github/workflows/release.yml과 이름이 같아야 한다.</summary>
    private const string ExeName = "Woodada.exe";

    /// <summary>임시 폴더의 새 exe를 설치 모드로 띄우는 인자. 뒤에 설치할 경로가 온다.</summary>
    private const string InstallArg = "--install-update";

    /// <summary>설치 모드에 넘기는 인자. 이 PID의 프로세스(이전 앱)가 끝난 뒤에 exe를 바꾼다.</summary>
    private const string WaitPidArg = "--wait-pid";

    /// <summary>설치를 마치고 다시 띄운 앱에 넘기는 인자. 업데이트됐다는 알림을 한 번 띄운다.</summary>
    public const string UpdatedArg = "--updated";

    /// <summary>
    /// 설치에 실패해 원래 exe를 다시 띄울 때 넘기는 인자. 켜지자마자 업데이트를 확인하지 않는다.
    /// 그러지 않으면 같은 업데이트를 받아 또 실패하고 다시 뜨기를 끝없이 되풀이한다.
    /// 주기 확인은 그대로 하지만, 실패한 그 확인이 기록돼 있어 다음 주기까지는 다시 받지 않는다.
    /// </summary>
    public const string SkipUpdateArg = "--skip-update";

    /// <summary>로그인 직후에는 네트워크가 아직 안 붙어 있을 수 있어 몇 번 더 시도한다.</summary>
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60) };

    /// <summary>
    /// CI가 태그(vX.Y.Z)에서 박아 넣은 버전. 로컬 빌드는 csproj 기본값 0.0.0이다.
    /// 정적 필드는 적힌 순서대로 초기화되므로, 이 값을 쓰는 <see cref="Http"/>보다 위에 있어야 한다.
    /// </summary>
    public static Version Current { get; } = Normalize(typeof(Updater).Assembly.GetName().Version);

    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly string WorkDir = Path.Combine(Path.GetTempPath(), "Woodada-update");

    private static readonly string LogPath = Path.Combine(AppInfo.DataFolder, "update.log");

    /// <summary>새 릴리즈를 확인하는 주기.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>로컬 빌드(0.0.0)나 dotnet run은 릴리즈로 덮어쓰면 안 되므로 업데이트하지 않는다.</summary>
    public static bool IsReleaseBuild => Current > new Version(0, 0, 0);

    /// <summary>예전 버전이 "마지막 확인 시각"을 남기던 파일. 이제 켜질 때마다 확인하므로 쓰지 않고, 남아 있으면 지운다.</summary>
    private static readonly string LegacyLastCheckPath = Path.Combine(AppInfo.DataFolder, "update-check.txt");

    public static void DeleteLegacyCheckRecord()
    {
        try
        {
            if (File.Exists(LegacyLastCheckPath)) File.Delete(LegacyLastCheckPath);
        }
        catch (Exception)
        {
            // 못 지워도 아무 데서도 읽지 않으니 그만이다.
        }
    }

    /// <summary>
    /// 내려받아 검증까지 마친 새 앱. 아직 설치 위치는 건드리지 않은 상태.
    /// Location은 Windows에서는 새 exe, macOS에서는 새 Woodada.app 번들이다.
    /// </summary>
    public sealed record Staged(Version Version, string Location);

    /// <summary>새 릴리즈. 바꿔 끼울 수 없을 때 이 정보로 알림만 띄운다.</summary>
    public sealed record Release(Version Version, string PageUrl);

    /// <summary>
    /// 이 실행 위치에서 앱을 스스로 바꿔 끼울 수 있는지. false면 <see cref="FindNewerReleaseAsync"/>로 알림만 띄운다.
    /// macOS는 안 되는 이유를 update.log에 남긴다.
    /// </summary>
    public static bool CanInstallHere()
    {
        if (!IsReleaseBuild) return false;
        if (OperatingSystem.IsWindows()) return IsInstalledExe();
        return OperatingSystem.IsMacOS() && MacInstallTarget() is not null;
    }

    /// <summary>
    /// 새 버전이 있으면 임시 폴더에 내려받아 검증해 둔다. 없거나 실패하면 null.
    /// 실패해도 앱은 지금 버전 그대로 계속 돌아야 하므로 예외를 밖으로 던지지 않는다.
    /// </summary>
    public static Task<Staged?> PrepareAsync(CancellationToken ct)
    {
        if (!CanInstallHere()) return Task.FromResult<Staged?>(null);
        return WithRetriesAsync<Staged>(OperatingSystem.IsWindows() ? PrepareOnceAsync : PrepareMacOnceAsync, ct);
    }

    /// <summary>
    /// 더 새 릴리즈가 있는지만 본다. 바꿔 끼울 수 없는 곳에서 돌 때(macOS의 .dmg 안 등) 사용자가 릴리즈 페이지에서 받게 한다.
    /// </summary>
    public static Task<Release?> FindNewerReleaseAsync(CancellationToken ct)
    {
        if (!IsReleaseBuild) return Task.FromResult<Release?>(null);
        return WithRetriesAsync(async token =>
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestReleaseApi, token).ConfigureAwait(false));
            var release = doc.RootElement;
            if (ParseNewer(release) is not { } latest) return null;
            string page = release.TryGetProperty("html_url", out var url) ? url.GetString() ?? ReleasesPage : ReleasesPage;
            return new Release(latest, page);
        }, ct);
    }

    private const string ReleasesPage = "https://github.com/Hyuntae-Jeong/Woodada/releases/latest";

    /// <summary>once를 실행하고, 서버에 닿지 못했으면 조금 뒤 다시 해 본다.</summary>
    private static async Task<T?> WithRetriesAsync<T>(Func<CancellationToken, Task<T?>> once, CancellationToken ct) where T : class
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await once(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return null;
            }
            // 상태 코드가 없으면 서버에 닿지도 못한 것이다. 404(릴리즈 없음)·403(요청 한도) 같은 응답은 다시 해도 같다.
            catch (HttpRequestException ex) when (ex.StatusCode is null && attempt < RetryDelays.Length)
            {
                Log($"연결 실패, 다시 시도합니다: {ex.Message}");
                try { await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return null; }
            }
            catch (Exception ex)
            {
                Log($"업데이트 확인 실패: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>릴리즈 태그가 지금보다 새 버전이면 그 버전. 아니면 null.</summary>
    private static Version? ParseNewer(JsonElement release)
    {
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var parsed))
        {
            Log($"태그를 버전으로 읽을 수 없습니다: {tag}");
            return null;
        }
        var latest = Normalize(parsed);
        if (latest <= Current)
        {
            Log("최신 버전입니다");
            return null;
        }
        return latest;
    }

    private static async Task<Staged?> PrepareOnceAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestReleaseApi, ct).ConfigureAwait(false));
        var release = doc.RootElement;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (ParseNewer(release) is not { } latest) return null;

        var found = FindAsset(release, ExeName);

        Directory.CreateDirectory(WorkDir);
        string stagedExe = Path.Combine(WorkDir, ExeName);
        await DownloadAsync(found, stagedExe, ct).ConfigureAwait(false);

        // 내려받은 exe가 정말 그 버전인지 확인한다. 엉뚱한 파일이면 같은 업데이트를 매번 되풀이하게 된다.
        var info = FileVersionInfo.GetVersionInfo(stagedExe);
        var stagedVersion = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        if (stagedVersion != latest)
            throw new InvalidDataException($"{ExeName} 버전(v{stagedVersion})이 태그({tag})와 다릅니다");

        return new Staged(latest, stagedExe);
    }

    /// <summary>
    /// 릴리즈에 올라간 파일 중 이름이 같은 것.
    /// 없으면 릴리즈는 만들어졌는데 CI가 아직 파일을 올리는 중일 수 있으므로, 실패로 던져 확인 기록을 남기지 않는다.
    /// </summary>
    private static JsonElement FindAsset(JsonElement release, string name)
    {
        foreach (var a in release.GetProperty("assets").EnumerateArray())
        {
            if (a.GetProperty("name").GetString() == name) return a;
        }
        throw new InvalidDataException($"{release.GetProperty("tag_name").GetString()} 릴리즈에 {name}이 없습니다");
    }

    /// <summary>릴리즈 파일을 path에 내려받고 온전한지 확인한다.</summary>
    private static async Task DownloadAsync(JsonElement asset, string path, CancellationToken ct)
    {
        string url = asset.GetProperty("browser_download_url").GetString()!;
        await using (var download = await Http.GetStreamAsync(url, ct).ConfigureAwait(false))
        await using (var file = File.Create(path))
        {
            await download.CopyToAsync(file, ct).ConfigureAwait(false);
        }
        Verify(path, asset);
    }

    /// <summary>API가 알려준 크기와 SHA-256으로 내려받은 파일이 온전한지 확인한다.</summary>
    private static void Verify(string path, JsonElement asset)
    {
        long expectedSize = asset.GetProperty("size").GetInt64();
        long actualSize = new FileInfo(path).Length;
        if (actualSize != expectedSize)
            throw new InvalidDataException($"내려받은 크기가 다릅니다 ({actualSize:N0} / {expectedSize:N0} bytes)");

        // digest는 "sha256:<hex>" 형식. 예전 릴리즈 등 값이 없으면 크기 확인으로 만족한다.
        if (asset.TryGetProperty("digest", out var digest) &&
            digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(path);
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!actual.Equals(d["sha256:".Length..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("내려받은 파일의 SHA-256이 다릅니다");
        }
    }

    /// <summary>
    /// 내려받아 둔 새 exe를 설치 모드로 띄운다. 성공하면 true이고, 호출한 쪽은 바로 종료해야 한다.
    /// 설치 모드는 이 프로세스가 끝나야 exe를 바꾸기 시작한다.
    /// </summary>
    public static bool StartInstaller(Staged staged)
    {
        if (OperatingSystem.IsMacOS()) return StartMacInstaller(staged);

        var start = new ProcessStartInfo(staged.Location)
        {
            UseShellExecute = false,
            WorkingDirectory = WorkDir,
        };
        start.ArgumentList.Add(InstallArg);
        start.ArgumentList.Add(Environment.ProcessPath!);
        start.ArgumentList.Add(WaitPidArg);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        try
        {
            Process.Start(start);
        }
        catch (Exception ex)
        {
            Log($"→ v{staged.Version.ToString(3)} 설치 단계를 띄우지 못했습니다: {ex.Message}");
            return false;
        }
        Log($"→ v{staged.Version.ToString(3)} 설치를 시작합니다");
        return true;
    }

    /// <summary>
    /// 설치 모드로 실행됐으면 exe를 바꿔 끼우고 앱을 다시 띄운 뒤 true를 돌려준다. 호출한 쪽은 그대로 끝내면 된다.
    /// 설치에 실패해도 원래 exe는 다시 띄운다. 업데이트 때문에 고양이가 사라지면 안 된다.
    /// </summary>
    public static bool TryRunInstaller(string[] args)
    {
        int i = Array.IndexOf(args, InstallArg);
        if (i < 0 || i + 1 >= args.Length) return false;
        string target = args[i + 1];

        if (OperatingSystem.IsMacOS())
        {
            RunMacInstaller(target, args);
            return true;
        }

        WaitForPreviousApp(args);

        // 새 이름으로 다 쓴 다음 한 번에 바꿔치기한다. 복사가 중간에 끊겨도 원래 exe는 멀쩡하다.
        // 이전 앱이 막 끝난 직후에는 파일이 잠깐 더 잠겨 있을 수 있어(백신 검사 등) 몇 번 다시 시도한다.
        string temp = target + ".new";
        bool installed = false;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(Environment.ProcessPath!, temp, overwrite: true);
                File.Move(temp, target, overwrite: true);
                installed = true;
                break;
            }
            catch (Exception ex)
            {
                if (attempt == 10)
                {
                    // 쓰기 권한이 없는 폴더(Program Files 등)에 두었으면 여기까지 온다.
                    Log($"설치 실패, 이전 버전을 다시 실행합니다: {ex.Message}");
                    break;
                }
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }
        TryDelete(temp);

        var start = new ProcessStartInfo(target)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(target)!,
        };
        start.ArgumentList.Add(installed ? UpdatedArg : SkipUpdateArg);
        try
        {
            Process.Start(start);
            if (installed) Log("설치 완료, 다시 실행합니다");
        }
        catch (Exception ex)
        {
            Log($"설치 후 앱을 다시 띄우지 못했습니다: {ex.Message}");
        }
        return true;
    }

    /// <summary>이전 앱이 끝나길 기다린다. 30초가 지나도 안 끝나면 false.</summary>
    private static bool WaitForPreviousApp(string[] args)
    {
        int i = Array.IndexOf(args, WaitPidArg);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out int pid)) return true;
        try
        {
            using var previous = Process.GetProcessById(pid);
            return previous.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException)
        {
            return true; // 이미 끝났다.
        }
    }

    /// <summary>
    /// 업데이트 직후 실행에서, 임시 폴더에 받아 두었던 설치용 exe를 지운다.
    /// 설치 모드가 아직 끝나는 중일 수 있으므로 시작을 막지 않고 백그라운드에서 몇 번 다시 시도한다.
    /// 그래도 안 되면 다음 업데이트가 같은 자리에 덮어쓴다.
    /// </summary>
    public static void CleanUpStaged(string[] args)
    {
        if (OperatingSystem.IsMacOS())
        {
            CleanUpMacStaged(args);
            return;
        }

        string staged = Path.Combine(WorkDir, ExeName);
        if (!File.Exists(staged)) return;

        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    File.Delete(staged);
                    return;
                }
                catch (Exception)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
        });
    }

    /// <summary>
    /// 릴리즈에서 받은 exe로 실행 중일 때만 업데이트한다.
    /// dotnet run이나 빌드 폴더의 apphost는 0.0.0이라 여기까지 오지 않지만, 이름이 다른 exe도 막아 둔다.
    /// </summary>
    private static bool IsInstalledExe() =>
        Environment.ProcessPath is { } path &&
        Path.GetFileName(path).Equals(ExeName, StringComparison.OrdinalIgnoreCase);

    private static Version Normalize(Version? v) =>
        v is null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        // GitHub API는 User-Agent가 없으면 요청을 거절한다.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Woodada", Current.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception) { }
    }

    /// <summary>
    /// 사용자에게 묻지 않고 조용히 도는 기능이라, 언제 업데이트됐고 왜 안 됐는지는 파일로만 남긴다.
    /// 업데이트 기록이 재실행 뒤 로그에 덮이지 않도록 이어 쓰고, 커지면 새로 시작한다.
    /// </summary>
    private const long MaxLogBytes = 64 * 1024;

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} v{Current.ToString(3)} {message}{Environment.NewLine}";
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxLogBytes) File.WriteAllText(LogPath, line);
            else File.AppendAllText(LogPath, line);
        }
        catch (Exception)
        {
            // 로그를 못 남겨도 업데이트 흐름에는 영향이 없다.
        }
    }
}
