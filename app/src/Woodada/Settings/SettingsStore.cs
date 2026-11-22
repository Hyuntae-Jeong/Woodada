using System.Text.Json;
using System.Text.Json.Serialization;

namespace Woodada.Settings;

/// <summary>%APPDATA%\Woodada\settings.json 읽기/쓰기.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string FilePath { get; }
    public AppSettings Current { get; private set; }

    /// <summary>설정이 바뀌면 오버레이가 다음 프레임부터 반영한다.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 새 폴더에 설정이 없을 때 한 번 가져올 폴더. 이름을 바꾸기 전 버전들이 쓰던 곳이고,
    /// 개발용 빌드는 처음 띄울 때 설치된 앱의 설정을 가져와 같은 모습으로 시작한다.
    /// </summary>
    private static readonly string[] LegacyFolders = AppInfo.IsDev
        ? new[] { "Woodada", "Udada", "TaskbarRunner" }
        : new[] { "Udada", "TaskbarRunner" };

    public SettingsStore()
    {
        FilePath = Path.Combine(AppInfo.DataFolder, "settings.json");
        MigrateLegacy(Platform.PlatformServices.Current.DataRoot);
        Current = Load();
    }

    private void MigrateLegacy(string appData)
    {
        if (File.Exists(FilePath) || File.Exists(SafeFile.BackupPath(FilePath))) return;
        foreach (var folder in LegacyFolders)
        {
            var legacy = Path.Combine(appData, folder, "settings.json");
            if (!File.Exists(legacy)) continue;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // 옛 파일은 지우지 않고 복사만 한다. 옛 버전으로 돌아가도 설정이 남아 있게.
                File.Copy(legacy, FilePath);
            }
            catch (Exception)
            {
                // 복사에 실패하면 기본값으로 시작한다.
            }
            return;
        }
    }

    private AppSettings Load()
    {
        // 파일이 깨졌으면 .bak을, 그것도 없으면 기본값으로 뜬다. 상주 앱이 시작조차 못 하는 것보단 낫다.
        var loadedSettings = SafeFile.Load(FilePath, json => JsonSerializer.Deserialize<AppSettings>(json, Options));
        bool loaded = loadedSettings is not null;
        var settings = loadedSettings ?? AppSettings.CreateDefault();
        // 새 설치는 컴퓨터 화면 언어를 따른다(한국어면 한국어, 그 밖엔 영어).
        // 설정 파일이 없거나 깨졌어도 달린 기록이 있으면 예전부터 쓰던 사용자다. 갑자기 영어로 바뀌지 않게 한국어로 둔다.
        if (!loaded)
            settings.Language = File.Exists(Path.Combine(AppInfo.DataFolder, "stats.json"))
                ? Loc.KoreanId
                : Loc.SystemDefaultId();
        settings.Normalize();
        return settings;
    }

    public void Update(AppSettings settings)
    {
        settings.Normalize();
        Current = settings;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        try
        {
            SafeFile.WriteAllText(FilePath, Serialize(Current));
        }
        catch (Exception)
        {
            // OneDrive 동기화 잠금 등으로 실패해도 메모리 상 설정은 유지된다.
        }
    }

    public static string Serialize(AppSettings s) => JsonSerializer.Serialize(s, Options);

    public static AppSettings Deserialize(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.CreateDefault();
}
