using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chickxel.Core.IO;

/// <summary>저장한 대화의 한 줄 종류(화면에 보인 대로 다시 그리는 데 씀).</summary>
public enum ChatItemKind
{
    /// <summary>사용자 메시지(글 + 첨부 미리보기).</summary>
    User,
    /// <summary>AI 답 글.</summary>
    Assistant,
    /// <summary>AI가 부른 도구 한 줄(<see cref="ChatItem.Error"/> = 실패).</summary>
    Tool,
    /// <summary>안내 한 줄(흐린 글).</summary>
    Notice,
    /// <summary>오류 한 줄.</summary>
    Error,
    /// <summary>차례가 끝난 뒤의 캔버스 모습(작은 그림 하나).</summary>
    Snapshot,
    /// <summary>차례 끝 비용·시간 줄.</summary>
    Footer,
    /// <summary>AI 그리기(그림 생성 → 픽셀 변환) 결과: 결과 그림, 결과 글, 다시 열 기록(<see cref="ChatItem.Job"/>).</summary>
    AiDraw,
    /// <summary>그림 생성에 실제로 보낸 프롬프트(접힌 요약과 펼친 본문).</summary>
    AiDrawPrompt,
}

/// <summary>저장한 대화의 한 줄.</summary>
public sealed class ChatItem
{
    public ChatItemKind Kind { get; set; }
    public string Text { get; set; } = "";
    /// <summary>작은 PNG 그림(base64): 사용자 첨부의 미리보기, <see cref="ChatItemKind.Snapshot"/>의 캔버스 모습. 없으면 null.</summary>
    public List<string>? Images { get; set; }
    /// <summary>사용자가 붙인 그림 수(미리보기를 남기지 못한 것 포함).</summary>
    public int ImageCount { get; set; }
    /// <summary><see cref="ChatItemKind.Tool"/>: 도구가 실패함.</summary>
    public bool Error { get; set; }
    /// <summary><see cref="ChatItemKind.AiDraw"/>: 생성 기록 id(<c>Chickxel.Core.AiDraw.AiDrawJobStore</c> — "변환 설정 보기"로 다시 엶). 없으면 null.</summary>
    public string? Job { get; set; }
    /// <summary><see cref="ChatItemKind.AiDrawPrompt"/>: 접힌 줄에 보일 사용자 질문 요약.</summary>
    public string? PromptSummary { get; set; }
}

/// <summary>
/// 저장한 대화 하나: 제목(첫 메시지), AI(<see cref="Provider"/>), 모델(null = CLI 기본값), CLI 세션 ID(이어 가기용),
/// 세션을 시작한 작업 폴더(Claude는 이어 갈 때 같은 폴더여야 함, 쓰지 않는 CLI는 null), 즐겨찾기, 다시 그릴 대화 줄.
/// </summary>
public sealed class SavedChat
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Model { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? SessionId { get; set; }
    public string? SessionDirectory { get; set; }
    /// <summary>
    /// 마지막 차례를 지우거나 고쳐서 CLI 세션을 버렸음: 다음 메시지는 새 CLI 세션으로 보내고 남은 대화 정리(<see cref="ChatRecap"/>)를 앞에 붙인다.
    /// 그 메시지로 새 세션 ID를 얻으면 꺼진다.
    /// </summary>
    public bool NeedsRecap { get; set; }
    public bool Favorite { get; set; }
    public List<ChatItem> Items { get; set; } = new();

    public ChatSummary Summary() => new(Id, Title, Model, UpdatedUtc, Favorite);
}

/// <summary>대화 목록 한 칸(목록 파일 <c>index.json</c>에 담음).</summary>
public sealed record ChatSummary(string Id, string Title, string? Model, DateTime UpdatedUtc, bool Favorite);

/// <summary>
/// AI 채팅 대화 보관소(<c>&lt;앱 설정 폴더&gt;/Chickxel/Chats</c>). AI마다 폴더 하나(<c>Chats/&lt;AI id&gt;/</c>, 세션 ID가 AI마다 다르므로 목록도 따로):
/// 대화마다 <c>&lt;id&gt;.json</c>(<see cref="SavedChat"/>) + 목록 <c>index.json</c>(<see cref="ChatSummary"/>, 깨지거나 없으면 대화 파일로 다시 만듦).
/// 파일은 임시 파일에 쓴 뒤 바꿔치기한다. AI마다 <see cref="MaxChats"/>개까지 두고, 넘치면 즐겨찾기가 아닌 가장 오래된 대화부터 지운다
/// (즐겨찾기와 방금 저장한 대화는 지우지 않으므로 넘을 수 있음). 한 스레드(UI)에서만 쓴다.
/// </summary>
public sealed class ChatHistoryStore(string root)
{
    public const int MaxChats = 20;
    public const int TitleLength = 40;
    public const string IndexName = "index.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Root { get; } = root;

    /// <summary>AI id·대화 id로 쓸 수 있는 이름(영문 소문자·숫자·'-'·'_'만, 경로가 밖으로 새지 않게).</summary>
    public static bool IsValidName(string? name)
        => name is { Length: > 0 and <= 64 } && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');

    /// <summary>새 대화 id.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    private string DirectoryOf(string provider)
        => IsValidName(provider) ? Path.Combine(Root, provider) : throw new ArgumentException($"Invalid provider id: {provider}", nameof(provider));

    private string FileOf(string provider, string id)
        => IsValidName(id) ? Path.Combine(DirectoryOf(provider), id + ".json") : throw new ArgumentException($"Invalid chat id: {id}", nameof(id));

    /// <summary>첫 메시지로 만든 제목: 줄바꿈·겹친 공백을 한 칸으로, 앞뒤 공백을 떼고 <paramref name="max"/>자가 넘으면 자르고 "…".</summary>
    public static string MakeTitle(string text, int max = TitleLength)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var s = string.Join(' ', words);
        return s.Length <= max ? s : s[..max].TrimEnd() + "…";
    }

    /// <summary>목록 순서: 즐겨찾기 먼저, 그 안에서와 나머지는 최근에 고친 것부터.</summary>
    public static List<ChatSummary> Order(IEnumerable<ChatSummary> chats)
        => [.. chats.OrderByDescending(c => c.Favorite).ThenByDescending(c => c.UpdatedUtc).ThenBy(c => c.Id, StringComparer.Ordinal)];

    /// <summary>
    /// 지울 대화: 모두 <paramref name="max"/>개를 넘으면 즐겨찾기가 아닌 가장 오래된(마지막으로 고친 때) 것부터 넘지 않을 때까지.
    /// 즐겨찾기와 <paramref name="keepId"/>(방금 저장한 대화)는 지우지 않는다.
    /// </summary>
    public static List<ChatSummary> SelectEvict(IEnumerable<ChatSummary> chats, int max, string? keepId)
    {
        var list = chats.ToList();
        var evict = new List<ChatSummary>();
        int count = list.Count;
        foreach (var c in list.Where(c => !c.Favorite && c.Id != keepId).OrderBy(c => c.UpdatedUtc).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            if (count <= max) break;
            evict.Add(c);
            count--;
        }
        return evict;
    }

    /// <summary><paramref name="provider"/>의 대화 목록(<see cref="Order"/> 순서, 파일이 없는 항목은 뺌).</summary>
    public List<ChatSummary> List(string provider)
    {
        var dir = DirectoryOf(provider);
        if (!Directory.Exists(dir)) return [];
        var index = ReadIndex(provider);
        if (index is null)
        {
            index = Rebuild(provider);
            TryWriteIndex(provider, index);
        }
        return Order(index.Where(c => IsValidName(c.Id) && File.Exists(FileOf(provider, c.Id))));
    }

    /// <summary>대화 하나를 읽는다. 없거나 깨졌으면 null.</summary>
    public SavedChat? Load(string provider, string id)
    {
        if (!IsValidName(id)) return null;
        try
        {
            var file = FileOf(provider, id);
            if (!File.Exists(file)) return null;
            var chat = JsonSerializer.Deserialize<SavedChat>(File.ReadAllText(file), Options);
            if (chat is null || chat.Id != id) return null;
            chat.Provider = provider;
            return chat;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 대화를 쓰고(새 것이면 목록에 더함) 넘친 대화를 지운다(<see cref="SelectEvict"/>). 지운 대화 id 목록.
    /// 쓰지 못하면 예외(IOException 등).
    /// </summary>
    public List<string> Save(SavedChat chat)
    {
        var provider = chat.Provider;
        var file = FileOf(provider, chat.Id);
        Directory.CreateDirectory(DirectoryOf(provider));
        UndoHistoryIO.WriteAtomic(file, JsonSerializer.SerializeToUtf8Bytes(chat, Options));
        var list = List(provider).Where(c => c.Id != chat.Id).ToList();
        list.Add(chat.Summary());
        var evicted = SelectEvict(list, MaxChats, chat.Id);
        foreach (var e in evicted)
        {
            TryDeleteFile(FileOf(provider, e.Id));
            list.Remove(e);
        }
        WriteIndex(provider, Order(list));
        return [.. evicted.Select(e => e.Id)];
    }

    /// <summary>즐겨찾기를 켜고 끈다. 대화가 없으면 false.</summary>
    public bool SetFavorite(string provider, string id, bool favorite)
    {
        if (Load(provider, id) is not { } chat) return false;
        if (chat.Favorite == favorite) return true;
        chat.Favorite = favorite;
        UndoHistoryIO.WriteAtomic(FileOf(provider, id), JsonSerializer.SerializeToUtf8Bytes(chat, Options));
        var list = List(provider).Select(c => c.Id == id ? c with { Favorite = favorite } : c);
        WriteIndex(provider, Order(list));
        return true;
    }

    /// <summary>대화를 지운다. 지웠으면 true.</summary>
    public bool Delete(string provider, string id)
    {
        if (!IsValidName(id)) return false;
        bool removed = TryDeleteFile(FileOf(provider, id));
        var dir = DirectoryOf(provider);
        if (Directory.Exists(dir)) TryWriteIndex(provider, List(provider));
        return removed;
    }

    private List<ChatSummary>? ReadIndex(string provider)
    {
        try
        {
            var file = Path.Combine(DirectoryOf(provider), IndexName);
            return File.Exists(file) ? JsonSerializer.Deserialize<List<ChatSummary>>(File.ReadAllText(file), Options) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>목록 파일이 없거나 깨졌을 때: 대화 파일을 모두 읽어 목록을 다시 만든다.</summary>
    private List<ChatSummary> Rebuild(string provider)
    {
        var list = new List<ChatSummary>();
        foreach (var file in Directory.EnumerateFiles(DirectoryOf(provider), "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (Path.GetFileName(file) == IndexName || !IsValidName(id)) continue;
            if (Load(provider, id) is { } chat) list.Add(chat.Summary());
        }
        return list;
    }

    private void WriteIndex(string provider, List<ChatSummary> list)
        => UndoHistoryIO.WriteAtomic(Path.Combine(DirectoryOf(provider), IndexName), JsonSerializer.SerializeToUtf8Bytes(list, Options));

    private void TryWriteIndex(string provider, List<ChatSummary> list)
    {
        try
        {
            WriteIndex(provider, list);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 목록은 다음에 다시 만든다.
        }
    }

    private static bool TryDeleteFile(string file)
    {
        try
        {
            if (!File.Exists(file)) return false;
            File.Delete(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
