using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>
/// 사용자가 승인한 뒤 보이는 터미널에서 공식 설치 명령을 실행한다. 설치(<c>login</c>)는 로그인까지 이어 주고, 업데이트는 로그인을 건너뛴다(로그인은 CLI가 그대로 기억함).
/// </summary>
internal static partial class AiInstaller
{
    /// <summary>터미널에서 실행할 일(확인 창에 보임). <paramref name="login"/>이 아니면 마지막 "설치 성공 후: … login" 줄을 뺀다.</summary>
    internal static string Preview(AiProvider provider, bool login = true)
    {
        var text = provider.Id == "claude"
            ? OperatingSystem.IsWindows() ? UiStrings.S_AiInstaller_Preview_A6B5B3079B : UiStrings.S_AiInstaller_Preview_91DCED206B
            : OperatingSystem.IsWindows() ? UiStrings.S_AiInstaller_Preview_AD9D1D8075 : UiStrings.S_AiInstaller_Preview_425F99142F;
        return login ? text : text[..Math.Max(0, text.TrimEnd().LastIndexOf('\n'))].TrimEnd();
    }

    /// <summary>설치·업데이트 터미널을 연다. <paramref name="login"/>이면 설치 뒤 로그인까지, 아니면 업데이트만 하고 끝낸다.</summary>
    internal static bool OpenTerminal(AiProvider provider, out string error, bool login = true)
    {
        error = "";
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            error = UiStrings.S_AiInstaller_OpenTerminal_A5D29502C5;
            return false;
        }
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "chickxel-ai");
            Directory.CreateDirectory(dir);
            // 한 번 연 설치 창의 스크립트를 다음 설치 시 덮어쓰지 않는다.
            var script = Path.Combine(dir, $"install-{provider.Id}-{Guid.NewGuid():N}" + (OperatingSystem.IsWindows() ? ".cmd" : ".command"));
            if (OperatingSystem.IsWindows() && provider.Id == "codex")
                File.WriteAllText(script + ".ps1", WindowsNodeScript
                    .Replace("__NODE_FOLDER_EXISTS__", PowerShellLiteral(UiStrings.AiInstaller_NodeFolderExists).Replace("{path}", "' + $target + '"))
                    .Replace("__NODE_LTS_MISSING__", PowerShellLiteral(UiStrings.AiInstaller_NodeLtsMissing))
                    .Replace("__NODE_HASH_FAILED__", PowerShellLiteral(UiStrings.AiInstaller_NodeHashFailed)), new UTF8Encoding(false));
            File.WriteAllText(script, OperatingSystem.IsWindows() ? WindowsScript(provider, login).Replace("\n", "\r\n") : MacScript(provider, login), new UTF8Encoding(false));
            if (OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                using var p = Process.Start(new ProcessStartInfo("open")
                {
                    ArgumentList = { "-a", "Terminal", script },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (p is not null && p.WaitForExit(5000) && p.ExitCode == 0) return true;
                error = UiStrings.S_AiInstaller_OpenTerminal_91B288994C;
                return false;
            }
            using (var p = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/k", script },
                UseShellExecute = true,
            }))
                if (p is not null) return true;
            error = UiStrings.S_AiInstaller_OpenTerminal_85390FEF5C;
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string PowerShellLiteral(string text) => "'" + text.Replace("'", "''") + "'";
    private static string ShellQuote(string text) => "'" + text.Replace("'", "'\\''") + "'";

    private static string BatchMessage(string text)
    {
        var command = "Write-Output " + PowerShellLiteral(text);
        return "powershell -NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
    }

    // 배치 명령은 번역하면 실행이 깨지므로 중립 리소스에서 읽고, 사용자 안내만 안전한 명령으로 바꾼다.
    // PSModulePath는 비운다: PowerShell 7(pwsh)에서 띄운 Chickxel이면 그 모듈 경로가 물려 와 Windows PowerShell 5.1이 Get-FileHash 같은
    // 기본 명령을 못 찾고(Claude 설치 스크립트·Node 확인이 실패) — 비우면 5.1이 제 기본 경로를 쓴다.
    // 내려받는 동안 창이 비어 보이지 않게 먼저 시작 안내를 보이고, 업데이트(login 아님)는 로그인 줄을 빼고 업데이트 안내로 끝낸다.
    private static string WindowsScript(AiProvider provider, bool login)
    {
        var script = UiStrings.ResourceManager.GetString(
            provider.Id == "claude" ? "S_AiInstaller_WindowsScript_CF3206EDC2" : "S_AiInstaller_WindowsScript_60DF0BBF87",
            System.Globalization.CultureInfo.InvariantCulture)!;
        if (!login) script = LoginLines().Replace(script, "");
        var start = "chcp 65001 >nul\n";
        script = script.Replace(start, start + "set \"PSModulePath=\"\n" + BatchMessage(UiStrings.FormatAiInstaller_Starting(product: provider.ProductName)) + "\n");
        return script.Replace("echo __LOGIN_COMPLETE__", BatchMessage(login ? UiStrings.AiInstaller_LoginComplete : UiStrings.AiInstaller_UpdateComplete))
            .Replace("echo __LOGIN_FAILED__", BatchMessage(login ? UiStrings.AiInstaller_LoginFailed : UiStrings.AiInstaller_UpdateFailed));
    }

    /// <summary>Windows 설치 스크립트의 로그인 두 줄(로그인 명령과 실패 검사).</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^call (?:claude auth login|codex login)\r?\nif errorlevel 1 goto failed\r?\n", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex LoginLines();

    /// <summary>winget이 없는 Windows에서도 관리자 권한 없이 공식 Node.js LTS와 npm을 설치한다.</summary>
    private const string WindowsNodeScript = """
        $ErrorActionPreference = 'Stop'
        $target = Join-Path $env:LOCALAPPDATA 'Chickxel\Node'
        if (Test-Path (Join-Path $target 'node.exe')) { exit 0 }
        if (Test-Path $target) { throw __NODE_FOLDER_EXISTS__ }
        $temp = Join-Path $env:TEMP ('chickxel-node-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temp | Out-Null
        try {
            $release = Invoke-RestMethod 'https://nodejs.org/dist/index.json' | Where-Object { $_.lts -ne $false } | Select-Object -First 1
            if (!$release) { throw __NODE_LTS_MISSING__ }
            $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
            $name = "node-$($release.version)-win-$arch.zip"
            $url = "https://nodejs.org/dist/$($release.version)"
            $zip = Join-Path $temp $name
            Invoke-WebRequest "$url/$name" -OutFile $zip
            $sums = (Invoke-WebRequest "$url/SHASUMS256.txt").Content
            $match = [regex]::Match($sums, '(?m)^([0-9a-fA-F]{64})  ' + [regex]::Escape($name) + '\r?$')
            if (!$match.Success -or (Get-FileHash -Algorithm SHA256 $zip).Hash -ne $match.Groups[1].Value) {
                throw __NODE_HASH_FAILED__
            }
            $unpack = Join-Path $temp 'unpack'
            Expand-Archive -LiteralPath $zip -DestinationPath $unpack
            New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
            Move-Item -LiteralPath (Join-Path $unpack "node-$($release.version)-win-$arch") -Destination $target
        } finally {
            Remove-Item -LiteralPath $temp -Recurse -Force
        }
        """;

    private static string MacScript(AiProvider provider, bool login)
    {
        var setup = provider.Id == "claude"
            ? """
              curl -fsSL https://claude.ai/install.sh | bash || fail
              command -v claude >/dev/null 2>&1 || fail
              claude auth login || fail
              """
            : """
              if ! command -v npm >/dev/null 2>&1; then
                if command -v brew >/dev/null 2>&1; then
                  brew install node || fail
                else
                  export NVM_DIR="$HOME/.nvm"
                  curl -fsSL https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.3/install.sh -o "$TMPDIR/chickxel-nvm-install.sh" || fail
                  PROFILE=/dev/null bash "$TMPDIR/chickxel-nvm-install.sh" || fail
                  . "$NVM_DIR/nvm.sh" || fail
                  nvm install --lts || fail
                fi
              fi
              command -v npm >/dev/null 2>&1 || fail
              npm install -g --prefix "$HOME/.npm-global" @openai/codex || fail
              export PATH="$HOME/.npm-global/bin:$PATH"
              command -v codex >/dev/null 2>&1 || fail
              codex login || fail
              """;
        // 업데이트는 로그인 줄을 빼고 업데이트 안내로 끝낸다.
        if (!login) setup = string.Join('\n', setup.Split('\n').Where(l => !l.TrimStart().StartsWith("claude auth login", StringComparison.Ordinal) && !l.TrimStart().StartsWith("codex login", StringComparison.Ordinal)));
        return "#!/bin/bash\nexport PATH=\"$HOME/.local/bin:$HOME/.npm-global/bin:/opt/homebrew/bin:/usr/local/bin:$PATH\"\n" +
               "fail() { printf '%s\\n' " + ShellQuote(login ? UiStrings.AiInstaller_LoginFailed : UiStrings.AiInstaller_UpdateFailed) + "; exit 1; }\n" +
               "printf '%s\\n' " + ShellQuote(UiStrings.FormatAiInstaller_Starting(product: provider.ProductName)) + "\n" +
               setup + "\nprintf '%s\\n' " + ShellQuote(login ? UiStrings.AiInstaller_LoginComplete : UiStrings.AiInstaller_UpdateComplete) + "\n";
    }
}
