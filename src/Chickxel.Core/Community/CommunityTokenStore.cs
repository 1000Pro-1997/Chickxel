using System.Runtime.InteropServices;
using System.Text;
using Chickxel.Core.Assets;

namespace Chickxel.Core.Community;

/// <summary>로그인 세션 토큰을 두는 곳(앱은 <see cref="CommunityTokenStore"/>, test는 메모리).</summary>
public interface ICommunityTokenStore
{
    /// <summary>저장한 토큰. 없거나 읽지 못하면 null.</summary>
    string? Load();

    void Save(string token);

    void Clear();
}

/// <summary>
/// 세션 토큰 파일 하나(앱 설정 폴더의 자기 파일). Windows는 DPAPI(<c>CryptProtectData</c>, 현재 사용자 범위)로 암호화해 쓰고,
/// 그 밖의 OS는 사용자만 읽고 쓰는 권한(0600)으로 쓴다. 토큰은 기록(로그)에 남기지 않는다.
/// </summary>
public sealed class CommunityTokenStore(string path) : ICommunityTokenStore
{
    /// <summary>DPAPI 추가 엔트로피(다른 앱이 같은 사용자 권한으로 같은 바이트를 우연히 풀어 쓰지 않게).</summary>
    private static readonly byte[] Entropy = "Chickxel.CommunitySession.v1"u8.ToArray();

    public string Path { get; } = path;

    public string? Load()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            byte[] data = File.ReadAllBytes(Path);
            if (data.Length is 0 or > 4096) return null;
            if (OperatingSystem.IsWindows()) data = Dpapi.Unprotect(data);
            string token = Encoding.UTF8.GetString(data).Trim();
            return token.Length is > 0 and <= 512 && token.All(c => c is > ' ' and < (char)127) ? token : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicFailure)
        {
            return null;
        }
    }

    public void Save(string token)
    {
        byte[] data = Encoding.UTF8.GetBytes(token);
        if (OperatingSystem.IsWindows()) data = Dpapi.Protect(data);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        if (!OperatingSystem.IsWindows())
        {
            // 내용을 쓰기 전에 권한부터 좁힌다(잠깐이라도 다른 사용자가 읽을 수 있는 파일이 없게).
            using (File.Create(Path)) { }
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.WriteAllBytes(Path, data);
            return;
        }
        AtomicFile.Write(Path, data);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>DPAPI가 풀지 못함(다른 사용자·다른 컴퓨터에서 옮겨 온 파일 등).</summary>
    private sealed class CryptographicFailure(string message) : Exception(message);

    /// <summary>Windows DPAPI(crypt32) 현재 사용자 범위, UI 없음.</summary>
    private static class Dpapi
    {
        private const int UiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob input, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static byte[] Protect(byte[] data) => Run(data, protect: true);

        public static byte[] Unprotect(byte[] data) => Run(data, protect: false);

        private static byte[] Run(byte[] data, bool protect)
        {
            var input = Copy(data);
            var entropy = Copy(Entropy);
            var output = default(DataBlob);
            try
            {
                bool ok = protect
                    ? CryptProtectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
                if (!ok) throw new CryptographicFailure(Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture));
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                Free(input);
                Free(entropy);
                if (output.Data != IntPtr.Zero)
                {
                    // 풀린 토큰이 해제된 메모리에 남지 않게 지운다.
                    for (int i = 0; i < output.Size; i++) Marshal.WriteByte(output.Data, i, 0);
                    LocalFree(output.Data);
                }
            }
        }

        private static DataBlob Copy(byte[] data)
        {
            var blob = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
            Marshal.Copy(data, 0, blob.Data, data.Length);
            return blob;
        }

        private static void Free(DataBlob blob)
        {
            if (blob.Data != IntPtr.Zero) Marshal.FreeHGlobal(blob.Data);
        }
    }
}
