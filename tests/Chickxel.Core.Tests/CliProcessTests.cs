using System.Runtime.InteropServices;
using System.Text;
using Chickxel.Core.IO;

namespace Chickxel.Core.Tests;

public class CliProcessTests
{
    [Fact]
    public void Command_line_length_follows_quoting_rules()
    {
        Assert.Equal("codex.exe a".Length, CliProcess.CommandLineLength("codex.exe", ["a"]));
        // 공백이 있으면 따옴표로 감쌈.
        Assert.Equal("x \"a b\"".Length, CliProcess.CommandLineLength("x", ["a b"]));
        // 빈 인자는 "".
        Assert.Equal("x \"\"".Length, CliProcess.CommandLineLength("x", [""]));
        // 따옴표는 \" 로, 그 앞 역슬래시는 두 배.
        Assert.Equal("x \"a\\\"b\"".Length, CliProcess.CommandLineLength("x", ["a\"b"]));
        Assert.Equal("x \"a\\\\\\\"b\"".Length, CliProcess.CommandLineLength("x", ["a\\\"b"]));
        // 따옴표로 감싼 인자의 끝 역슬래시도 두 배.
        Assert.Equal("x \"a b\\\\\"".Length, CliProcess.CommandLineLength("x", ["a b\\"]));
        // 따옴표가 없으면 역슬래시는 그대로.
        Assert.Equal("x a\\b".Length, CliProcess.CommandLineLength("x", ["a\\b"]));
        Assert.True(CliProcess.IsBatch(@"C:\npm\codex.CMD"));
        Assert.False(CliProcess.IsBatch(@"C:\bin\codex.exe"));
    }

    [Fact]
    public void Resolve_codex_shim_prefers_native_exe()
    {
        using var dir = new TempDir();
        var shim = dir.File("codex.cmd");
        File.WriteAllText(shim, "@ECHO off");
        Assert.Null(CliProcess.ResolveCodexShim(shim, Architecture.X64, null));

        var package = Path.Combine(dir.Path, "node_modules", "@openai", "codex");
        Directory.CreateDirectory(Path.Combine(package, "bin"));
        File.WriteAllText(Path.Combine(package, "bin", "codex.js"), "");
        // 네이티브가 없고 node도 없으면 null, node가 있으면 node + codex.js.
        Assert.Null(CliProcess.ResolveCodexShim(shim, Architecture.X64, null));
        var viaNode = CliProcess.ResolveCodexShim(shim, Architecture.X64, @"C:\node\node.exe")!;
        Assert.Equal(@"C:\node\node.exe", viaNode.FileName);
        Assert.Equal([Path.Combine(package, "bin", "codex.js")], viaNode.PrefixArgs);

        var exe = Path.Combine(package, "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "");
        var native = CliProcess.ResolveCodexShim(shim, Architecture.X64, null)!;
        Assert.Equal(exe, native.FileName);
        Assert.Empty(native.PrefixArgs);
        Assert.Equal("1", native.Environment["CODEX_MANAGED_BY_NPM"]);
        Assert.Equal(package, native.Environment["CODEX_MANAGED_PACKAGE_ROOT"]);
        // arm64는 다른 플랫폼 패키지(없으면 node로).
        Assert.Null(CliProcess.ResolveCodexShim(shim, Architecture.Arm64, null));
        // 다른 이름의 셰임·exe는 건드리지 않음.
        File.WriteAllText(dir.File("claude.cmd"), "");
        Assert.Null(CliProcess.ResolveCodexShim(dir.File("claude.cmd"), Architecture.X64, null));
        Assert.Null(CliProcess.ResolveCodexShim(exe, Architecture.X64, null));
    }

    [Fact]
    public void Decode_falls_back_to_console_code_page()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        const string message = "명령줄이 너무 깁니다.";
        Assert.Equal(message, CliProcess.Decode(cp949.GetBytes(message), cp949));
        Assert.Equal(message, CliProcess.Decode(Encoding.UTF8.GetBytes(message), cp949));
        Assert.Contains('\uFFFD', CliProcess.Decode(cp949.GetBytes(message), null));
    }

    [Fact]
    public void Split_lines_handles_chunks_crlf_and_final_line()
    {
        var lines = new List<string>();
        var pending = new List<byte>();
        var bytes = Encoding.UTF8.GetBytes("첫 줄\r\n둘째\n끝");
        CliProcess.SplitLines(bytes.AsSpan(0, 5), pending, false, null, lines.Add);
        CliProcess.SplitLines(bytes.AsSpan(5), pending, false, null, lines.Add);
        Assert.Equal(["첫 줄", "둘째"], lines);
        CliProcess.SplitLines([], pending, true, null, lines.Add);
        Assert.Equal(["첫 줄", "둘째", "끝"], lines);
    }
}
