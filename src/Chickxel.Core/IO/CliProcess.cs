using System.Text;

namespace Chickxel.Core.IO;

/// <summary>실제로 띄울 프로그램: 실행 파일, 앞에 붙일 인자(예: node의 스크립트 경로), 더할 환경 변수.</summary>
public sealed record CliLaunchTarget(string FileName, IReadOnlyList<string> PrefixArgs, IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// AI CLI 실행 도우미(순수 계산 — 파일 확인만): npm이 만든 <c>.cmd</c> 셰임 대신 그 안의 실제 프로그램 찾기, Windows 명령줄 길이 재기,
/// 자식 프로세스 출력 해독(UTF-8이 아니면 콘솔 코드 페이지).
/// </summary>
public static class CliProcess
{
    /// <summary>cmd.exe가 받는 명령줄 최대 길이(8191자)에서 셰임이 덧붙이는 node·스크립트 경로 몫을 뺀 한도.</summary>
    public const int CmdLimit = 7800;
    /// <summary>CreateProcess 명령줄 최대 길이(32767자, 끝 문자 포함)에서 여유를 뺀 한도.</summary>
    public const int ProcessLimit = 32000;

    /// <summary><c>.cmd</c>·<c>.bat</c>(cmd.exe를 거쳐 실행 — 8191자 한도, 인자가 cmd 규칙으로 다시 해석됨)인지.</summary>
    public static bool IsBatch(string path)
        => path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    /// <summary>Windows에서 <paramref name="file"/>과 <paramref name="args"/>로 만들어질 명령줄 길이(.NET <c>ArgumentList</c>의 따옴표·역슬래시 규칙).</summary>
    public static int CommandLineLength(string file, IEnumerable<string> args)
    {
        int length = QuotedLength(file);
        foreach (var a in args) length += 1 + QuotedLength(a);
        return length;
    }

    /// <summary>인자 하나가 명령줄에서 차지하는 길이: 공백·따옴표가 없으면 그대로, 있으면 따옴표로 감싸고 따옴표 앞 역슬래시를 두 배로.</summary>
    private static int QuotedLength(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0) return arg.Length;
        int length = 2, slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') length += slashes * 2 + 2;
            else length += slashes + 1;
            slashes = 0;
        }
        return length + slashes * 2;
    }

    /// <summary>
    /// npm 전역 설치의 Codex 셰임(<c>…\npm\codex.cmd</c>)이면 그 패키지가 실제로 띄우는 네이티브 <c>codex.exe</c>(<c>@openai/codex</c>의 플랫폼 패키지
    /// <c>@openai/codex-win32-&lt;arch&gt;/vendor/&lt;target&gt;/bin/codex.exe</c>)와 <c>codex.js</c>가 넣는 환경 변수를 돌려준다(cmd.exe 8191자 한도를 피함).
    /// 네이티브 파일이 없고 <c>codex.js</c>만 있으면 node로 그 스크립트를 띄운다(<paramref name="node"/> = 셰임 옆 node.exe 또는 PATH의 node, 없으면 null).
    /// npm 셰임이 아니면 null.
    /// </summary>
    public static CliLaunchTarget? ResolveCodexShim(string shimPath, System.Runtime.InteropServices.Architecture arch, string? node)
    {
        if (!IsBatch(shimPath) || !Path.GetFileNameWithoutExtension(shimPath).Equals("codex", StringComparison.OrdinalIgnoreCase)) return null;
        var dir = Path.GetDirectoryName(Path.GetFullPath(shimPath));
        if (dir is null) return null;
        var package = Path.Combine(dir, "node_modules", "@openai", "codex");
        var script = Path.Combine(package, "bin", "codex.js");
        if (!File.Exists(script)) return null;
        var env = new Dictionary<string, string>
        {
            ["CODEX_MANAGED_PACKAGE_ROOT"] = package,
            ["CODEX_MANAGED_BY_NPM"] = "1",
        };
        var (platform, target) = arch == System.Runtime.InteropServices.Architecture.Arm64
            ? ("codex-win32-arm64", "aarch64-pc-windows-msvc")
            : ("codex-win32-x64", "x86_64-pc-windows-msvc");
        string[] candidates =
        [
            Path.Combine(package, "node_modules", "@openai", platform, "vendor", target, "bin", "codex.exe"),
            Path.Combine(dir, "node_modules", "@openai", platform, "vendor", target, "bin", "codex.exe"),
            Path.Combine(package, "vendor", target, "bin", "codex.exe"),
        ];
        foreach (var exe in candidates)
            if (File.Exists(exe)) return new CliLaunchTarget(exe, [], env);
        var localNode = Path.Combine(dir, "node.exe");
        var nodePath = File.Exists(localNode) ? localNode : node;
        return nodePath is null ? null : new CliLaunchTarget(nodePath, [script], new Dictionary<string, string>());
    }

    /// <summary>
    /// 자식 프로세스 출력 한 줄(바이트)을 글로: UTF-8로 맞으면 UTF-8, 아니면 <paramref name="fallback"/>(Windows 콘솔 코드 페이지 — 예: cmd.exe의
    /// "명령줄이 너무 깁니다"는 CP949), 그것도 없으면 깨진 글자를 바꾼 UTF-8.
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes, Encoding? fallback)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return (fallback ?? Encoding.UTF8).GetString(bytes);
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// 출력 바이트를 줄 단위로 나눠(<c>\n</c>, 끝의 <c>\r</c>는 뗌) 줄마다 <see cref="Decode"/>한다. <paramref name="pending"/>은 아직 줄 끝이 오지 않은
    /// 바이트(다음 호출에 이어 붙임, <paramref name="final"/>이면 마지막 줄로 내보냄).
    /// </summary>
    public static void SplitLines(ReadOnlySpan<byte> chunk, List<byte> pending, bool final, Encoding? fallback, Action<string> onLine)
    {
        foreach (var b in chunk)
        {
            if (b != (byte)'\n')
            {
                pending.Add(b);
                continue;
            }
            Emit();
        }
        if (final && pending.Count > 0) Emit();

        void Emit()
        {
            int n = pending.Count;
            if (n > 0 && pending[n - 1] == (byte)'\r') n--;
            var bytes = n == 0 ? [] : pending.GetRange(0, n).ToArray();
            pending.Clear();
            onLine(Decode(bytes, fallback));
        }
    }
}
