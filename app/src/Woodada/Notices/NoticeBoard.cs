using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using Woodada.Update;

namespace Woodada.Notices;

/// <summary>공지 한 개. 글자는 지금 언어로 이미 골라 둔 것이다.</summary>
internal sealed record Notice(string Id, DateOnly Date, string Title, string Body, NoticeLink? Link);

/// <summary>공지 아래 버튼. 모집 글의 "연락하기"처럼 글마다 다른 곳으로 보낸다. Icon은 "threads"·"github" 같은 이름(UI/BrandIcons).</summary>
internal sealed record NoticeLink(string Label, string Url, string? Icon);

/// <summary>
/// 설정 창 공지 탭에 보여 줄 공지들. 원본은 홈페이지의 docs/notices.json이다.
/// 앱을 새로 내지 않고 공지를 올리려고 홈페이지에서 받아 오고, 받은 파일은 데이터 폴더에 남겨 둔다(꺼져 있어도 보이게).
/// 받지 못하면 남겨 둔 파일, 그것도 없으면 빌드 때 exe에 넣은 같은 파일을 쓴다.
/// 남겨 둔 파일과 exe 안 파일 중에는 "updated" 날짜가 늦은 쪽을 쓴다. 인터넷이 끊긴 채 업데이트돼도 새 공지가 보이게.
/// </summary>
internal static class NoticeBoard
{
    private const string RemoteUrl = "https://hyuntae-jeong.github.io/Woodada/notices.json";

    /// <summary>설정 창을 열 때마다 받으면 여닫을 때마다 요청이 간다. 한 번 받으면 이만큼은 다시 받지 않는다.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    private static string CachePath => Path.Combine(AppInfo.DataFolder, "notices.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly HttpClient Http = CreateHttpClient();

    private static NoticeFile? _file;
    private static DateTime _lastFetched = DateTime.MinValue;
    private static bool _fetching;

    /// <summary>새 공지를 받아 목록이 바뀌었다. UI 스레드에서 알린다.</summary>
    public static event EventHandler? Changed;

    /// <summary>최신 글이 위로 오게 정렬한 공지. 기한("until")이 지난 글은 뺀다.</summary>
    public static IReadOnlyList<Notice> Current
    {
        get
        {
            var file = _file ??= LoadLocal();
            var today = DateOnly.FromDateTime(DateTime.Now);
            return (file?.Notices ?? new())
                .Where(n => !string.IsNullOrWhiteSpace(n.Id) && (n.Until is not { } until || until >= today))
                .OrderByDescending(n => n.Date)
                .Select(n => new Notice(n.Id!, n.Date, Pick(n.Title), Pick(n.Body),
                    n.Link is { Url: { } url } link && IsWebUrl(url) ? new NoticeLink(LinkLabel(link.Label), url, link.Icon) : null))
                .Where(n => n.Title.Length > 0)
                .ToList();
        }
    }

    /// <summary>
    /// 홈페이지에서 공지를 다시 받는다. 실패해도 지금 목록을 그대로 둔다.
    /// 받은 내용이 지금과 다를 때만 <see cref="Changed"/>를 낸다.
    /// </summary>
    public static async Task RefreshAsync()
    {
        if (_fetching || DateTime.UtcNow - _lastFetched < RefreshInterval) return;
        _fetching = true;
        try
        {
            string json = await Http.GetStringAsync(RemoteUrl).ConfigureAwait(false);
            var remote = Parse(json) ?? throw new InvalidDataException("notices.json을 읽을 수 없습니다");
            _lastFetched = DateTime.UtcNow;
            string before = _file is null ? "" : JsonSerializer.Serialize(_file, Options);
            if (JsonSerializer.Serialize(remote, Options) == before) return;
            _file = remote;
            SaveCache(json);
            Dispatcher.UIThread.Post(() => Changed?.Invoke(null, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            // 공지는 없어도 앱이 도는 데 지장이 없다. 남겨 둔 목록을 그대로 보여 준다.
            Updater.Log($"공지를 받지 못했습니다: {ex.Message}");
        }
        finally
        {
            _fetching = false;
        }
    }

    private static NoticeFile? LoadLocal()
    {
        NoticeFile? cached = null;
        try
        {
            if (File.Exists(CachePath)) cached = Parse(File.ReadAllText(CachePath));
        }
        catch (Exception)
        {
            // 깨졌으면 exe 안 파일을 쓴다. 다음에 받으면 덮어쓴다.
        }
        var bundled = LoadBundled();
        if (cached is null) return bundled;
        if (bundled is null) return cached;
        return bundled.Updated > cached.Updated ? bundled : cached;
    }

    private static NoticeFile? LoadBundled()
    {
        try
        {
            using var stream = typeof(NoticeBoard).Assembly.GetManifestResourceStream("notices.json");
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static NoticeFile? Parse(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<NoticeFile>(json, Options);
            return file?.Notices is null ? null : file;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void SaveCache(string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, json);
        }
        catch (Exception)
        {
            // 못 남겨도 이번 실행에서는 받은 목록을 보여 준다.
        }
    }

    /// <summary>지금 언어 글. 그 언어가 비어 있으면 다른 언어라도 보여 준다(영어만 쓴 공지 등).</summary>
    private static string Pick(LocalizedText? text)
    {
        string ko = text?.Ko?.Trim() ?? "", en = text?.En?.Trim() ?? "";
        string preferred = Loc.T(ko, en);
        return preferred.Length > 0 ? preferred : ko.Length > 0 ? ko : en;
    }

    private static string LinkLabel(LocalizedText? text) => Pick(text) is { Length: > 0 } label ? label : Loc.T("자세히 보기", "Learn more");

    /// <summary>원격 파일이 열게 하는 주소는 웹 페이지만. file:이나 실행 파일 경로가 셸로 열리지 않게.</summary>
    private static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Woodada", Updater.Current.ToString(3)));
        return client;
    }

    // ───────── notices.json 모양 ─────────

    private sealed class NoticeFile
    {
        /// <summary>파일을 고친 날. 남겨 둔 파일과 exe 안 파일 중 어느 쪽이 새것인지 가른다.</summary>
        public DateOnly Updated { get; set; }
        public List<NoticeEntry>? Notices { get; set; }
    }

    private sealed class NoticeEntry
    {
        /// <summary>읽었는지 기억하는 열쇠. 한 번 올린 글의 id는 바꾸지 않는다(바꾸면 다시 NEW가 뜬다).</summary>
        public string? Id { get; set; }
        public DateOnly Date { get; set; }
        /// <summary>이 날까지만 보여 준다. 모집 마감처럼 끝이 있는 글에 쓴다. 없으면 계속 보인다.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateOnly? Until { get; set; }
        public LocalizedText? Title { get; set; }
        public LocalizedText? Body { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LinkEntry? Link { get; set; }
    }

    private sealed class LinkEntry
    {
        public LocalizedText? Label { get; set; }
        public string? Url { get; set; }
        /// <summary>버튼 글자 앞 아이콘 이름("threads", "github"). 앱이 모르는 이름이면 글자만 보인다.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Icon { get; set; }
    }

    private sealed class LocalizedText
    {
        public string? Ko { get; set; }
        public string? En { get; set; }
    }
}
