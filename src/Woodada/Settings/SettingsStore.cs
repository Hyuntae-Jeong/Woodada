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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        FilePath = Path.Combine(AppInfo.DataFolder, "settings.json");
        MigrateLegacy(appData);
        Current = Load();
    }

    private void MigrateLegacy(string appData)
    {
        if (File.Exists(FilePath)) return;
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
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? Deserialize(File.ReadAllText(FilePath))
                : AppSettings.CreateDefault();
        }
        catch (Exception)
        {
            // 파일이 깨졌으면 기본값으로 뜬다. 상주 앱이 시작조차 못 하는 것보단 낫다.
            settings = AppSettings.CreateDefault();
        }
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
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // 쓰는 도중 꺼져도 기존 파일이 반쯤 잘리지 않도록 임시 파일에 쓰고 교체한다.
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Serialize(Current));
            File.Move(tmp, FilePath, overwrite: true);
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
