using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Woodada.Storage;

/// <summary>기록 파일을 열어 본 결과.</summary>
public enum SealState
{
    /// <summary>파일이 없다.</summary>
    Missing,
    /// <summary>JSON으로 읽을 수 없다(쓰다 끊겼거나 손으로 고치다 망가뜨렸다).</summary>
    Broken,
    /// <summary>
    /// 확인할 수는 없지만 기한 안이라 믿어 주는 파일. 서명을 넣기 전 버전이 쓴 파일이거나,
    /// 지금은 쓰지 않는(잃어버렸거나 새어 나간) 열쇠로 서명한 파일이다. 받아 주면 바로 지금 열쇠로 다시 서명한다.
    /// </summary>
    Legacy,
    /// <summary>이 빌드의 열쇠로 서명했고 내용이 그대로다.</summary>
    Sealed,
    /// <summary>서명이 맞지 않는다. 앱 밖에서 고쳤거나, 다른 열쇠를 쓰는 빌드가 썼다.</summary>
    Tampered,
}

/// <summary>
/// stats.json·rewards.json에 HMAC 서명을 붙이고 확인한다. 내용은 그대로 읽을 수 있게 두고
/// (숨기는 게 아니라 고친 걸 알아채는 게 목적이다) 옆에 서명을 단다.
///
/// 파일 모양: { "v": 1, "key": 1, "sig": "...", "data": { 원래 내용 } }
/// 서명은 data 글자 그대로에 건다. 읽은 객체를 다시 직렬화해 비교하면 .NET 버전에 따라 숫자 표기가 달라져
/// 정상 파일이 틀렸다고 나올 수 있다. 서명을 .sig 파일로 따로 두지 않는 것도 같은 이유다.
/// 두 파일 사이에서 앱이 꺼지면 짝이 어긋나 정상 사용자가 걸린다.
///
/// 정식 릴리즈는 GitHub Secret(WOODADA_SIGNING_KEY)으로 받은 열쇠를 빌드 때 넣는다(Woodada.csproj).
/// 열쇠가 없는 빌드(개발용, 소스를 받아 직접 만든 빌드)는 소스에 적힌 개발용 열쇠를 쓰고,
/// 정식 기록을 덮어쓰지 않게 다른 폴더를 쓴다(AppInfo.DataFolder).
/// 실행 파일을 뜯어보면 열쇠를 꺼낼 수 있으니 완벽한 방어는 아니다. 메모장으로 숫자만 바꾸는 것을 막는 정도다.
///
/// ── 열쇠를 잃어버렸거나 새어 나갔을 때 ──
/// 그냥 새 열쇠로 릴리즈하면 모든 사용자의 파일이 "서명이 틀린 파일"이 되어 한꺼번에 초기화된다. 대신:
///   1. 새 열쇠를 만들어 GitHub Secret과 app/signing.key를 바꾼다.
///   2. 아래 <see cref="OfficialKeyId"/>를 1 올리고, <see cref="OfficialKeyFingerprint"/>를 새 열쇠의 지문으로 바꾼다.
///      지문: base64 -d app/signing.key | sha256sum
///   3. 옛 번호를 <see cref="RetiredKeys"/>에 넣고, 사용자들이 업데이트할 넉넉한 기한(6개월 이상)을 적는다.
///      기한 안에는 옛 번호 파일을 확인 없이 한 번 믿어 주고 새 열쇠로 다시 서명한다.
///      잃어버린 열쇠는 확인할 수 없고, 새어 나간 열쇠는 확인해 봐야 의미가 없으니 둘 다 믿어 주기만 한다.
/// </summary>
internal static class DataSeal
{
    /// <summary>
    /// 서명을 넣기 전 버전이 쓴 파일을 이때까지만 받아 준다. 기한 없이 받으면 서명 부분만 지우고
    /// 옛 모양으로 돌려놓는 식으로 언제든 빠져나갈 수 있다.
    /// 그동안 한 번도 새 버전을 켜지 않은 사용자는 이 날짜 뒤에 기록이 초기화된다.
    /// </summary>
    public static readonly DateTime LegacyAcceptedUntil = new(2027, 5, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>지금 정식 열쇠의 번호. 열쇠를 바꿀 때마다 1씩 올린다(위 설명 참고).</summary>
    private const int OfficialKeyId = 1;

    /// <summary>
    /// 지금 정식 열쇠의 지문(열쇠 바이트의 SHA-256). 공개해도 열쇠를 알아낼 수 없다.
    /// 빌드에 들어온 열쇠의 지문이 이와 다르면 정식 열쇠로 쓰지 않는다. Secret을 실수로 다른 값으로 바꿔도
    /// 모든 사용자의 기록이 "서명이 틀린 파일"로 초기화되는 일을 막는다. 릴리즈 워크플로도 같은 값을 확인한다.
    /// </summary>
    public const string OfficialKeyFingerprint = "96c8272e9ffabf6766c30704eead91e493e3b54621b25b8371e9bc1d32c4dce2";

    /// <summary>
    /// 이제 쓰지 않는 정식 열쇠 번호 → 그 번호로 서명한 파일을 믿어 주는 기한(UTC).
    /// 예: { 1, new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
    /// </summary>
    private static readonly Dictionary<int, DateTime> RetiredKeys = new()
    {
    };

    private const int FormatVersion = 1;
    private const string MetadataKey = "WoodadaSigningKey";

    // 비밀이 아니다. 개발용·직접 빌드끼리만 쓰고, 정식 빌드는 이 번호(0)의 파일을 받지 않는다.
    private const int DevKeyId = 0;
    private static readonly byte[] DevKey = SHA256.HashData("Woodada dev build - not a secret"u8);

    private static readonly byte[]? OfficialKey = ReadOfficialKey();

    /// <summary>정식 열쇠를 넣어 빌드했는지. 아니면 기록을 다른 폴더에 둔다.</summary>
    public static bool HasOfficialKey => OfficialKey is not null;

    private static int KeyId => HasOfficialKey ? OfficialKeyId : DevKeyId;
    private static byte[] Key => OfficialKey ?? DevKey;

    /// <summary>열쇠 바이트의 지문(소문자 16진수 SHA-256).</summary>
    public static string Fingerprint(byte[] key) => Convert.ToHexStringLower(SHA256.HashData(key));

    /// <summary>data(JSON 글자)에 서명을 붙인 파일 내용.</summary>
    /// <param name="purpose">파일 종류("stats" …). 서명에 섞어서 한 파일 내용을 다른 파일에 옮겨 붙여도 통과하지 않게 한다.</param>
    public static string Seal(string purpose, string dataJson) =>
        "{\n" +
        $"  \"v\": {FormatVersion},\n" +
        $"  \"key\": {KeyId},\n" +
        $"  \"sig\": \"{Sign(purpose, dataJson)}\",\n" +
        $"  \"data\": {dataJson}\n" +
        "}\n";

    /// <summary>
    /// 파일을 열어 상태와 내용(data 글자, 옛 파일이면 파일 전체)을 돌려준다.
    /// 확인할 수 없는 파일은 기한 안이면 Legacy, 지났으면 Tampered다.
    /// 읽기 자체에 실패하면(잠금 등) 예외를 그대로 던진다.
    /// </summary>
    public static (SealState State, string? Json) Open(string purpose, string path)
    {
        if (!File.Exists(path)) return (SealState.Missing, null);
        string text = File.ReadAllText(path);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch (JsonException) { return (SealState.Broken, null); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (SealState.Broken, null);
            bool hasSig = root.TryGetProperty("sig", out var sig);
            bool hasData = root.TryGetProperty("data", out var data);
            if (!hasSig && !hasData) return (Within(LegacyAcceptedUntil), text);
            if (!hasSig || !hasData) return (SealState.Tampered, null);

            // GetRawText는 파일에 적힌 글자를 그대로 잘라 준다. 서명할 때 넣은 글자와 똑같다.
            string json = data.GetRawText();
            if (!root.TryGetProperty("key", out var key) || !key.TryGetInt32(out int keyId)) return (SealState.Tampered, null);
            if (keyId == KeyId)
            {
                bool ok = sig.ValueKind == JsonValueKind.String && Verify(purpose, json, sig.GetString());
                return (ok ? SealState.Sealed : SealState.Tampered, json);
            }
            // 정식 빌드만 옛 정식 열쇠를 믿어 준다. 개발용 열쇠(0)는 소스에 있어 누구나 서명할 수 있으니 받지 않는다.
            if (HasOfficialKey && RetiredKeys.TryGetValue(keyId, out var until)) return (Within(until), json);
            return (SealState.Tampered, null);
        }
    }

    private static SealState Within(DateTime until) => DateTime.UtcNow < until ? SealState.Legacy : SealState.Tampered;

    /// <summary>
    /// 확인할 수 없는 파일(Legacy)을 받아 줄지. 다른 기록 파일이 이미 지금 열쇠로 서명된 모양이면
    /// 이 컴퓨터는 이미 넘어온 것이라, 이 파일만 옛 모양인 건 누군가 서명을 지우거나 번호를 바꾼 것이다.
    /// </summary>
    public static bool AcceptLegacy(SealState otherFile) => otherFile is not (SealState.Sealed or SealState.Tampered);

    /// <summary>다른 기록 파일의 상태만 본다. 읽다 실패하면 없는 것으로 친다.</summary>
    public static SealState Peek(string purpose, string path)
    {
        try { return Open(purpose, path).State; }
        catch (Exception) { return SealState.Missing; }
    }

    /// <summary>믿지 못할 파일을 덮어쓰기 전에 옆에 남겨 둔다. 오탐이면 되살릴 수 있게.</summary>
    public static void Quarantine(string path, string suffix)
    {
        try { File.Copy(path, path + suffix, overwrite: true); }
        catch (Exception) { }
    }

    private static string Sign(string purpose, string dataJson) =>
        Convert.ToBase64String(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(purpose + "\n" + dataJson)));

    private static bool Verify(string purpose, string dataJson, string? signature)
    {
        if (signature is null) return false;
        var given = new byte[32];
        if (!Convert.TryFromBase64String(signature, given, out int written) || written != given.Length) return false;
        var expected = HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(purpose + "\n" + dataJson));
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }

    /// <summary>
    /// Woodada.csproj가 Release 빌드에 넣는 열쇠(base64, 32바이트 이상). 개발용 빌드는 늘 개발용 열쇠를 쓴다.
    /// 지문이 <see cref="OfficialKeyFingerprint"/>와 다르면 정식 열쇠로 치지 않는다. 그러면 이 빌드는
    /// 기록을 Woodada-unsigned에 따로 두어서, 잘못 넣은 열쇠가 사용자 기록을 초기화하지 못한다.
    /// </summary>
    private static byte[]? ReadOfficialKey()
    {
        if (AppInfo.IsDev) return null;
        string? value = typeof(DataSeal).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == MetadataKey)?.Value;
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var key = Convert.FromBase64String(value.Trim());
            return key.Length >= 32 && Fingerprint(key) == OfficialKeyFingerprint ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
