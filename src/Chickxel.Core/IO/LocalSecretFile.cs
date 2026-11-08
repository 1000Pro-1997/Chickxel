using System.Runtime.InteropServices;

namespace Chickxel.Core.IO;

/// <summary>
/// 이 컴퓨터의 이 사용자만 읽을 작은 비밀 파일(앱 설정 폴더의 계정 파일·경로 열쇠). Windows는 DPAPI(<c>CryptProtectData</c>, 현재 사용자 범위)로
/// 암호화해 쓰고, 그 밖의 OS는 사용자만 읽고 쓰는 권한(0600)으로 쓴다.
/// </summary>
internal static class LocalSecretFile
{
    /// <summary>파일 내용(풀어서). 없거나 비었거나 <paramref name="maxBytes"/>보다 크거나 읽거나 풀 수 없으면 null.</summary>
    /// <param name="entropy">DPAPI 추가 엔트로피(파일 종류마다 다르게 — 다른 앱·다른 파일의 바이트를 우연히 풀어 쓰지 않게).</param>
    public static byte[]? Read(string path, byte[] entropy, int maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return null;
            byte[] data = File.ReadAllBytes(path);
            if (data.Length == 0 || data.Length > maxBytes) return null;
            return OperatingSystem.IsWindows() ? Dpapi.Unprotect(data, entropy) : data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicFailure)
        {
            return null;
        }
    }

    /// <summary>파일에 쓴다(폴더가 없으면 만듦). 쓰지 못하면 <see cref="IOException"/>·<see cref="UnauthorizedAccessException"/>.</summary>
    public static void Write(string path, byte[] data, byte[] entropy)
    {
        if (OperatingSystem.IsWindows()) data = Dpapi.Protect(data, entropy);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!OperatingSystem.IsWindows())
        {
            // 내용을 쓰기 전에 권한부터 좁힌다(잠깐이라도 다른 사용자가 읽을 수 있는 파일이 없게).
            using (File.Create(path)) { }
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.WriteAllBytes(path, data);
            return;
        }
        AtomicFile.Write(path, data);
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

        public static byte[] Protect(byte[] data, byte[] entropy) => Run(data, entropy, protect: true);

        public static byte[] Unprotect(byte[] data, byte[] entropy) => Run(data, entropy, protect: false);

        private static byte[] Run(byte[] data, byte[] entropyBytes, bool protect)
        {
            var input = Copy(data);
            var entropy = Copy(entropyBytes);
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
                    // 풀린 비밀이 해제된 메모리에 남지 않게 지운다.
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
