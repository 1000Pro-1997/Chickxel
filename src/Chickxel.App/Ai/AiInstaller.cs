using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Chickxel.App.Localization;

namespace Chickxel.App.Ai;

/// <summary>사용자가 승인한 뒤 보이는 터미널에서 공식 설치 명령을 실행하고 로그인까지 이어 준다.</summary>
internal static class AiInstaller
{
    internal static string Preview(AiProvider provider)
    {
        if (provider.Id == "claude")
            return OperatingSystem.IsWindows()
                ? UiStrings.S_AiInstaller_Preview_A6B5B3079B
                : UiStrings.S_AiInstaller_Preview_91DCED206B;
        if (OperatingSystem.IsWindows())
            return UiStrings.S_AiInstaller_Preview_AD9D1D8075;
        return UiStrings.S_AiInstaller_Preview_425F99142F;
    }

    internal static bool OpenTerminal(AiProvider provider, out string error)
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
            File.WriteAllText(script, OperatingSystem.IsWindows() ? WindowsScript(provider).Replace("\n", "\r\n") : MacScript(provider), new UTF8Encoding(false));
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
    private static string WindowsScript(AiProvider provider)
    {
        var script = UiStrings.ResourceManager.GetString(
            provider.Id == "claude" ? "S_AiInstaller_WindowsScript_CF3206EDC2" : "S_AiInstaller_WindowsScript_60DF0BBF87",
            System.Globalization.CultureInfo.InvariantCulture)!;
        return script.Replace("echo __LOGIN_COMPLETE__", BatchMessage(UiStrings.AiInstaller_LoginComplete))
            .Replace("echo __LOGIN_FAILED__", BatchMessage(UiStrings.AiInstaller_LoginFailed));
    }

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

    private static string MacScript(AiProvider provider)
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
        return "#!/bin/bash\nexport PATH=\"$HOME/.local/bin:$HOME/.npm-global/bin:/opt/homebrew/bin:/usr/local/bin:$PATH\"\n" +
               "fail() { printf '%s\\n' " + ShellQuote(UiStrings.AiInstaller_LoginFailed) + "; exit 1; }\n" +
               setup + "\nprintf '%s\\n' " + ShellQuote(UiStrings.AiInstaller_LoginComplete) + "\n";
    }
}
