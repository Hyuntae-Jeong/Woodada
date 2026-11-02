using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Woodada.Update;

/// <summary>
/// macOS 자동 업데이트. Windows와 같은 세 단계로 Woodada.app 번들을 통째로 바꿔 끼운다.
/// 1. 지금 앱이 릴리즈의 auto-update-mac-&lt;arch&gt;.zip을 임시 폴더에 받아 풀고 검증한 뒤,
///    그 새 앱을 설치 모드로 띄우고 정상 종료한다(<see cref="StartMacInstaller"/>).
/// 2. 설치 모드의 새 앱이 지금 앱이 끝나길 기다렸다가 자기 번들을 설치 위치 옆에 복사하고,
///    이름 바꾸기 두 번으로 바꿔치기한다(<see cref="RunMacInstaller"/>).
/// 3. 설치된 앱을 다시 띄운다. 다시 뜬 앱이 임시 폴더와 이전 번들을 지운다(<see cref="CleanUpMacStaged"/>).
///
/// 설치 모드와 재실행은 꼭 open으로 띄운다. 로그인 항목(LaunchAgent)으로 뜬 앱이 끝나면 launchd가
/// 그 앱이 띄운 자식 프로세스까지 정리하므로, Process.Start로 띄운 설치 모드는 교체 도중에 죽을 수 있다.
///
/// Apple 개발자 서명이 없어도 된다. 앱이 직접 내려받은 파일에는 격리(quarantine) 표시가 붙지 않아
/// 새 앱을 열 때 "확인되지 않은 개발자" 경고가 다시 뜨지 않는다. 혹시 붙어 있으면 지운다.
/// </summary>
internal static partial class Updater
{
    private const string MacAppName = "Woodada.app";

    /// <summary>
    /// CI가 릴리즈에 올리는 파일(build/mac/build-app.sh). 지금 앱과 같은 CPU용을 받는다.
    /// 사용자가 릴리즈 페이지에서 .dmg 대신 받지 않도록 Woodada가 아닌 이름을 붙였다.
    /// </summary>
    private static string MacZipName =>
        $"auto-update-mac-{(RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64")}.zip";

    private static string MacStagedApp => Path.Combine(WorkDir, MacAppName);

    /// <summary>
    /// 지금 실행 중인 .app 번들 경로(…/Woodada.app). 실행 파일이 …/X.app/Contents/MacOS/ 안에 있지 않으면 null.
    /// 사용자가 번들 이름을 바꿨어도 그 이름 그대로 바꿔 끼운다.
    /// </summary>
    private static string? RunningBundle()
    {
        if (Environment.ProcessPath is not { } exe) return null;
        string? macOS = Path.GetDirectoryName(exe);
        string? contents = Path.GetDirectoryName(macOS);
        string? bundle = Path.GetDirectoryName(contents);
        if (bundle is null || Path.GetFileName(macOS) != "MacOS" || Path.GetFileName(contents) != "Contents" ||
            !bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return null;
        return bundle;
    }

    /// <summary>바꿔 끼울 번들. 이 위치에서는 바꿔 끼울 수 없으면 이유를 남기고 null.</summary>
    private static string? MacInstallTarget()
    {
        if (RunningBundle() is not { } bundle)
        {
            Log("Woodada.app 안에서 실행된 앱이 아니라 자동 업데이트를 건너뜁니다");
            return null;
        }
        if (bundle.StartsWith("/Volumes/", StringComparison.Ordinal) || bundle.Contains("/AppTranslocation/", StringComparison.Ordinal))
        {
            Log("디스크 이미지(.dmg)나 macOS가 격리한 위치에서 실행 중이라 자동 업데이트를 건너뜁니다. " +
                "Woodada를 응용 프로그램 폴더로 옮기면 자동 업데이트됩니다");
            return null;
        }
        // 일반(관리자가 아닌) 계정은 /Applications에 쓸 수 없다.
        string parent = Path.GetDirectoryName(bundle)!;
        if (!CanWriteTo(parent))
        {
            Log($"{parent}에 쓸 권한이 없어 자동 업데이트를 건너뜁니다");
            return null;
        }
        return bundle;
    }

    private static bool CanWriteTo(string folder)
    {
        string probe = Path.Combine(folder, $".woodada-write-test-{Environment.ProcessId}");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<Staged?> PrepareMacOnceAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(LatestReleaseApi, ct).ConfigureAwait(false));
        var release = doc.RootElement;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (ParseNewer(release) is not { } latest) return null;
        var found = FindAsset(release, MacZipName);

        Directory.CreateDirectory(WorkDir);
        string zip = Path.Combine(WorkDir, MacZipName);
        await DownloadAsync(found, zip, ct).ConfigureAwait(false);

        // ditto로 풀어야 실행 권한·서명이 그대로 남는다. zip 안에는 Woodada.app 하나가 들어 있다.
        DeleteDirectory(MacStagedApp);
        RunOrThrow("/usr/bin/ditto", "-x", "-k", zip, WorkDir);
        TryDelete(zip);
        if (!Directory.Exists(MacStagedApp))
            throw new InvalidDataException($"{MacZipName} 안에 {MacAppName}이 없습니다");

        // 내려받은 앱이 정말 그 버전인지 확인한다. 엉뚱한 파일이면 같은 업데이트를 매번 되풀이하게 된다.
        string dll = Path.Combine(MacStagedApp, "Contents", "MacOS", "Woodada.dll");
        var stagedVersion = Normalize(AssemblyName.GetAssemblyName(dll).Version);
        if (stagedVersion != latest)
            throw new InvalidDataException($"{MacAppName} 버전(v{stagedVersion})이 태그({tag})와 다릅니다");

        // 임시 서명이라도 서명이 깨진 앱은 애플 실리콘에서 실행되지 않는다. 바꿔 끼우기 전에 확인한다.
        RunOrThrow("/usr/bin/codesign", "--verify", "--deep", "--strict", MacStagedApp);
        Run("/usr/bin/xattr", "-dr", "com.apple.quarantine", MacStagedApp); // 표시가 없으면 실패하는데, 그래도 된다

        return new Staged(latest, MacStagedApp);
    }

    /// <summary>내려받아 둔 새 앱을 설치 모드로 띄운다. 성공하면 true이고, 호출한 쪽은 바로 종료해야 한다.</summary>
    private static bool StartMacInstaller(Staged staged)
    {
        string version = staged.Version.ToString(3);
        if (RunningBundle() is not { } target)
        {
            Log($"→ v{version} 설치할 위치를 찾지 못했습니다");
            return false;
        }
        // -n: 같은 번들 ID의 앱(지금 앱)이 떠 있어도 새로 띄운다.
        var (code, error) = Run("/usr/bin/open", "-n", staged.Location, "--args",
            InstallArg, target, WaitPidArg, Environment.ProcessId.ToString());
        if (code != 0)
        {
            Log($"→ v{version} 설치 단계를 띄우지 못했습니다: {error}");
            return false;
        }
        Log($"→ v{version} 설치를 시작합니다");
        return true;
    }

    /// <summary>
    /// 설치 모드: 이 프로세스의 번들(임시 폴더의 새 앱)로 target 번들을 바꿔 끼우고 다시 띄운다.
    /// 설치에 실패해도 원래 앱은 다시 띄운다. 업데이트 때문에 고양이가 사라지면 안 된다.
    /// </summary>
    private static void RunMacInstaller(string target, string[] args)
    {
        if (!WaitForPreviousApp(args))
        {
            // 이전 앱이 아직 돌고 있으니 번들을 건드리지도, 하나 더 띄우지도 않는다.
            Log("이전 앱이 끝나지 않아 업데이트를 그만둡니다");
            return;
        }

        // 새 번들을 설치 위치 옆에 다 복사한 다음 이름만 바꾼다. 복사가 중간에 끊겨도 원래 앱은 멀쩡하다.
        // 점으로 시작하는 이름이라 Finder에는 보이지 않는다. 같은 폴더 안이라 이름 바꾸기는 한 번에 끝난다.
        string parent = Path.GetDirectoryName(target)!;
        string name = Path.GetFileName(target);
        string incoming = Path.Combine(parent, $".{name}.new");
        string backup = Path.Combine(parent, $".{name}.old");
        bool installed = false;
        try
        {
            string source = RunningBundle() ?? throw new InvalidOperationException("설치 모드가 .app 안에서 실행되지 않았습니다");
            DeleteDirectory(incoming);
            DeleteDirectory(backup);
            RunOrThrow("/usr/bin/ditto", source, incoming);
            Directory.Move(target, backup);
            try
            {
                Directory.Move(incoming, target);
            }
            catch
            {
                Directory.Move(backup, target);
                throw;
            }
            installed = true;
        }
        catch (Exception ex)
        {
            Log($"설치 실패, 이전 버전을 다시 실행합니다: {ex.Message}");
        }
        TryDeleteDirectory(incoming);

        var (code, error) = Run("/usr/bin/open", "-n", target, "--args", installed ? UpdatedArg : SkipUpdateArg);
        if (code != 0) Log($"설치 후 앱을 다시 띄우지 못했습니다: {error}");
        else if (installed) Log("설치 완료, 다시 실행합니다");
    }

    /// <summary>
    /// 설치 모드가 다시 띄운 실행에서만, 임시 폴더의 새 앱과 바꿔 끼우기 전 번들을 지운다.
    /// 그 밖의 실행에서 지우면 설치 모드가 아직 쓰고 있는 파일을 지울 수 있다. 남아도 다음 업데이트가 덮어쓴다.
    /// </summary>
    private static void CleanUpMacStaged(string[] args)
    {
        if (!args.Contains(UpdatedArg) && !args.Contains(SkipUpdateArg)) return;
        string? bundle = RunningBundle();

        _ = Task.Run(async () =>
        {
            // 설치 모드가 마무리하고 끝날 시간을 준다.
            await Task.Delay(TimeSpan.FromSeconds(5));
            TryDeleteDirectory(MacStagedApp);
            TryDelete(Path.Combine(WorkDir, MacZipName));
            if (bundle is not null)
                TryDeleteDirectory(Path.Combine(Path.GetDirectoryName(bundle)!, $".{Path.GetFileName(bundle)}.old"));
        });
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { DeleteDirectory(path); }
        catch (Exception ex) { Log($"{path}을 지우지 못했습니다: {ex.Message}"); }
    }

    /// <summary>명령을 실행하고 끝나길 기다린다. 종료 코드와 오류 출력을 돌려준다.</summary>
    private static (int Code, string Error) Run(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill();
            return (-1, $"{Path.GetFileName(file)}이 끝나지 않습니다");
        }
        output.Wait();
        return (process.ExitCode, error.Result.Trim());
    }

    private static void RunOrThrow(string file, params string[] args)
    {
        var (code, error) = Run(file, args);
        if (code != 0) throw new IOException($"{Path.GetFileName(file)} 실패({code}): {error}");
    }
}
