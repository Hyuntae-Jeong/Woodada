using System.Text;

namespace Woodada;

/// <summary>
/// 설정·기록 파일을 갑자기 꺼지거나 잠깐 잠겨 있어도 잃지 않게 읽고 쓴다.
/// 쓸 때는 임시 파일을 디스크까지 내려쓴 뒤 바꿔 끼우고, 바뀌기 전 파일을 .bak으로 남긴다.
/// 읽을 때는 잠겨 있으면 잠시 기다려 다시 읽고, 깨졌으면 .broken으로 옮겨 두고 .bak을 읽는다.
/// </summary>
internal static class SafeFile
{
    private const int ReadAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    public static string BackupPath(string path) => path + ".bak";
    public static string BrokenPath(string path) => path + ".broken";

    /// <summary>
    /// 디스크에 내려쓰지 않고 바꿔 끼우면, 그 직후 전원이 나갔을 때 파일 크기만 맞고 내용이 0(NUL)으로 채워질 수 있다.
    /// 실패하면 예외를 그대로 던지니 부르는 쪽이 다음에 다시 쓴다.
    /// </summary>
    public static void WriteAllText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path))
            File.Replace(tmp, path, BackupPath(path), ignoreMetadataErrors: true);
        else
            File.Move(tmp, path);
    }

    /// <summary>
    /// 파일을 읽어 parse로 바꾼다. 본 파일을 못 쓰면 .bak을 읽는다. 둘 다 없거나 못 쓰면 null.
    /// parse가 null을 돌려주거나 예외를 던지면 깨진 파일로 본다.
    /// </summary>
    public static T? Load<T>(string path, Func<string, T?> parse) where T : class
    {
        if (File.Exists(path))
        {
            if (TryRead(path, parse, out var value, out bool corrupt)) return value;
            // 깨진 본 파일을 그대로 두면 다음 저장 때 .bak 자리로 밀려나 멀쩡한 .bak을 덮는다. 먼저 치워 둔다.
            if (corrupt) MoveAside(path);
        }
        var backup = BackupPath(path);
        if (File.Exists(backup) && TryRead(backup, parse, out var restored, out _)) return restored;
        return null;
    }

    private static bool TryRead<T>(string path, Func<string, T?> parse, out T? value, out bool corrupt) where T : class
    {
        value = null;
        corrupt = false;
        string text;
        try
        {
            text = ReadWithRetry(path);
        }
        catch (Exception)
        {
            // 끝내 못 읽었다(백신·동기화 프로그램이 오래 잡고 있는 등). 파일은 멀쩡할 수 있으니 건드리지 않는다.
            return false;
        }
        try
        {
            value = parse(text);
        }
        catch (Exception)
        {
            value = null;
        }
        corrupt = value is null;
        return !corrupt;
    }

    private static string ReadWithRetry(string path)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < ReadAttempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }

    private static void MoveAside(string path)
    {
        try { File.Move(path, BrokenPath(path), overwrite: true); }
        catch (Exception)
        {
            try { File.Copy(path, BrokenPath(path), overwrite: true); }
            catch (Exception) { }
        }
    }
}
