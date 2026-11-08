using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;

namespace Chickxel.Core.Community;

/// <summary>저장한 계정 하나: 세션 토큰과 마지막으로 받은 내 정보(계정 전환 목록에 보임; 모르면 null — 예전 한 계정 파일에서 옮긴 토큰).</summary>
public sealed record CommunityAccount(string Token, CommunityUser? User);

/// <summary>
/// 저장한 계정 목록(로그인한 차례)과 지금 쓰는 계정 번호(<see cref="Active"/>, 쓰는 계정이 없으면 -1).
/// 파일 내용은 <c>{ "active": 0, "accounts": [{ "token": "…", "user": { User JSON } }] }</c>.
/// 예전 한 계정 파일(토큰 글자만)은 그 토큰 하나를 쓰는 목록으로 읽는다.
/// </summary>
public sealed record CommunityAccounts(IReadOnlyList<CommunityAccount> List, int Active)
{
    public static readonly CommunityAccounts Empty = new([], -1);

    /// <summary>지금 쓰는 계정. 없으면 null.</summary>
    public CommunityAccount? Current => Active >= 0 && Active < List.Count ? List[Active] : null;

    public string ToJson()
    {
        var accounts = new JsonArray();
        foreach (var account in List)
        {
            var item = new JsonObject { ["token"] = account.Token };
            if (account.User is { } user) item["user"] = user.ToJson();
            accounts.Add(item);
        }
        return new JsonObject { ["active"] = Active, ["accounts"] = accounts }.ToJsonString();
    }

    /// <summary>파일 내용을 읽는다. 알아볼 수 없는 내용·잘못된 토큰은 버린다(로그인하지 않은 것으로 봄).</summary>
    public static CommunityAccounts Parse(string text)
    {
        text = text.Trim();
        if (!text.StartsWith('{')) return IsValidToken(text) ? new([new CommunityAccount(text, null)], 0) : Empty;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject o || o["accounts"] is not JsonArray items) return Empty;
            int saved = o["active"] is JsonValue a && a.TryGetValue(out int index) ? index : -1;
            int active = -1;
            var list = new List<CommunityAccount>();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is not JsonObject item || CommunityJson.String(item["token"]) is not { } token || !IsValidToken(token)) continue;
                CommunityUser? user = null;
                if (item["user"] is JsonObject known)
                {
                    try
                    {
                        user = CommunityUser.Read(known);
                    }
                    catch (CommunityException) { }
                }
                // 버린 칸이 있어도 같은 계정을 가리키게 번호를 새 목록 기준으로 바꾼다.
                if (i == saved) active = list.Count;
                list.Add(new CommunityAccount(token, user));
            }
            return new CommunityAccounts(list, active);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>서버가 주는 세션 토큰 모양: 512자 이하, 공백·제어 문자 없는 ASCII.</summary>
    public static bool IsValidToken(string? token) => token is { Length: > 0 and <= 512 } && token.All(c => c is > ' ' and < (char)127);
}

/// <summary>로그인한 계정들을 두는 곳(앱은 <see cref="CommunityAccountStore"/>, test는 메모리).</summary>
public interface ICommunityAccountStore
{
    /// <summary>저장한 계정들. 없거나 읽지 못하면 <see cref="CommunityAccounts.Empty"/>.</summary>
    CommunityAccounts Load();

    /// <summary>계정들을 저장한다(빈 목록이면 저장한 것을 지움).</summary>
    void Save(CommunityAccounts accounts);
}

/// <summary>
/// 계정 파일 하나(앱 설정 폴더의 자기 파일). Windows는 DPAPI(현재 사용자 범위)로 암호화해 쓰고, 그 밖의 OS는 사용자만 읽고 쓰는 권한(0600)으로 쓴다
/// (<see cref="LocalSecretFile"/>). 토큰은 기록(로그)에 남기지 않는다.
/// </summary>
public sealed class CommunityAccountStore(string path) : ICommunityAccountStore
{
    /// <summary>DPAPI 추가 엔트로피(다른 앱이 같은 사용자 권한으로 같은 바이트를 우연히 풀어 쓰지 않게).</summary>
    private static readonly byte[] Entropy = "Chickxel.CommunitySession.v1"u8.ToArray();

    public string Path { get; } = path;

    /// <summary>읽을 파일 크기 한도(계정 수십 개도 넉넉히 들어감).</summary>
    private const int MaxFileBytes = 256 * 1024;

    public CommunityAccounts Load()
        => LocalSecretFile.Read(Path, Entropy, MaxFileBytes) is { } data ? CommunityAccounts.Parse(Encoding.UTF8.GetString(data)) : CommunityAccounts.Empty;

    public void Save(CommunityAccounts accounts)
    {
        if (accounts.List.Count == 0)
        {
            Delete();
            return;
        }
        LocalSecretFile.Write(Path, Encoding.UTF8.GetBytes(accounts.ToJson()), Entropy);
    }

    private void Delete()
    {
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
