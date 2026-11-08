using System.Security.Cryptography;
using System.Text;

namespace Chickxel.Core.IO;

/// <summary>
/// 프로젝트 파일에 적는 "이 컴퓨터의 경로"(내보내기 폴더·내보내기 이름)를 이 설치만의 열쇠로 암호화한다 — 파일을 남에게 줘도 내 폴더 이름(사용자 이름 등)이
/// 드러나지 않게. 열쇠는 처음 암호화할 때 무작위 32바이트로 만들어 <paramref name="keyPath"/>(앱 설정 폴더)에 둔다(<see cref="LocalSecretFile"/> — Windows는
/// DPAPI, 그 밖은 0600). 값은 <c>chickxel-enc:v1:</c> + base64(12바이트 nonce + AES-GCM 암호문 + 16바이트 태그)이고, 다른 컴퓨터·다른 설치(열쇠가 다름)에서
/// 풀지 못하면 값이 없던 것으로 본다(기본값 — 프로젝트 파일 옆에 내보냄). 이 값들은 잃어도 되는 설정이라 그렇게 해도 된다.
/// nonce는 값의 HMAC(열쇠)라 같은 값은 늘 같은 글이 된다(다시 저장해도 파일 바이트가 그대로 — 버전 관리에서 바뀐 줄로 보이지 않게).
/// 프로젝트 파일은 앱이 정한 <see cref="Current"/>를 쓴다(없으면 — 시험·도구 — 암호화하지 않고 그대로).
/// </summary>
public sealed class LocalPathCipher(string keyPath)
{
    public const string Prefix = "chickxel-enc:v1:";
    private const int KeyBytes = 32, NonceBytes = 12, TagBytes = 16;
    private static readonly byte[] Entropy = "Chickxel.LocalPathKey.v1"u8.ToArray();
    private readonly object _gate = new();
    private byte[]? _key;

    /// <summary>프로젝트 파일 읽기·쓰기가 쓰는 이 설치의 암호(앱 시작 때 정함). null이면 경로를 그대로 쓴다.</summary>
    public static LocalPathCipher? Current { get; set; }

    /// <summary>암호화한 값인지(<see cref="Prefix"/>로 시작).</summary>
    public static bool IsProtected(string? value) => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary><see cref="Current"/>로 암호화(없으면 그대로).</summary>
    internal static string? ProtectCurrent(string? value) => Current is { } c ? c.Protect(value) : value;

    /// <summary><see cref="Current"/>로 풀기. 암호화하지 않은 값은 그대로, 풀지 못하면(<see cref="Current"/>가 없을 때 포함) null.</summary>
    internal static string? UnprotectCurrent(string? value) => !IsProtected(value) ? value : Current?.Unprotect(value);

    /// <summary>값을 암호화한다. 비었으면 그대로, 이미 암호화했으면 그대로, 열쇠를 쓸 수 없으면(만들어 저장하지 못함) 그대로.</summary>
    public string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || IsProtected(value) || Key(create: true) is not { } key) return value;
        var plain = Encoding.UTF8.GetBytes(value);
        var output = new byte[NonceBytes + plain.Length + TagBytes];
        var nonce = output.AsSpan(0, NonceBytes);
        HMACSHA256.HashData(key, plain).AsSpan(0, NonceBytes).CopyTo(nonce);
        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceBytes, plain.Length), output.AsSpan(NonceBytes + plain.Length, TagBytes));
        return Prefix + Convert.ToBase64String(output);
    }

    /// <summary>값을 푼다. 암호화하지 않은 값(예전 파일)은 그대로, 풀지 못하면(다른 설치의 열쇠·손상) null.</summary>
    public string? Unprotect(string? value)
    {
        if (!IsProtected(value)) return value;
        if (Key(create: false) is not { } key) return null;
        byte[] data;
        try
        {
            data = Convert.FromBase64String(value![Prefix.Length..]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (data.Length < NonceBytes + TagBytes) return null;
        int length = data.Length - NonceBytes - TagBytes;
        var plain = new byte[length];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(data.AsSpan(0, NonceBytes), data.AsSpan(NonceBytes, length), data.AsSpan(NonceBytes + length, TagBytes), plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>이 설치의 열쇠(읽어 둔 것, 없으면 파일에서, <paramref name="create"/>면 새로 만들어 저장). 쓸 수 없으면 null.</summary>
    private byte[]? Key(bool create)
    {
        lock (_gate)
        {
            if (_key is not null) return _key;
            if (LocalSecretFile.Read(keyPath, Entropy, 1024) is { Length: KeyBytes } stored) return _key = stored;
            if (!create) return null;
            var key = RandomNumberGenerator.GetBytes(KeyBytes);
            try
            {
                LocalSecretFile.Write(keyPath, key, Entropy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 열쇠를 남기지 못하면 다음 실행에서 풀 수 없으므로 암호화하지 않는다.
                return null;
            }
            return _key = key;
        }
    }
}
