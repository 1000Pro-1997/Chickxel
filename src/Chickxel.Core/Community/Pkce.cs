using System.Security.Cryptography;
using System.Text;

namespace Chickxel.Core.Community;

/// <summary>
/// 앱 로그인의 PKCE(RFC 7636, S256): 비밀 verifier(무작위 32바이트의 base64url, 43자)와 서버에 먼저 보내는 challenge
/// (verifier ASCII의 SHA-256을 base64url), 로그인 왕복을 묶는 state.
/// </summary>
public static class Pkce
{
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>로그인 왕복 state(무작위 24바이트의 base64url, 32자 — 서버 규칙 <c>[A-Za-z0-9_-]</c> 128자 이하).</summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(24));

    /// <summary>덧붙임(<c>=</c>) 없는 base64url.</summary>
    public static string Base64Url(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
