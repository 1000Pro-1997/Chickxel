using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Gpixel.App.Ai;

/// <summary>사용자가 승인한 뒤 보이는 터미널에서 공식 설치 명령을 실행하고 로그인까지 이어 준다.</summary>
internal static class AiInstaller
{
    internal static string Preview(AiProvider provider)
    {
        if (provider.Id == "claude")
            return OperatingSystem.IsWindows()
                ? "PowerShell: irm https://claude.ai/install.ps1 | iex\n설치 성공 후: claude auth login"
                : "curl -fsSL https://claude.ai/install.sh | bash\n설치 성공 후: claude auth login";
        if (OperatingSystem.IsWindows())
            return "npm이 없으면: winget으로 Node.js LTS 설치. winget이 없거나 실패하면 https://nodejs.org/dist/ 의 공식 Node.js LTS ZIP을 SHA-256 확인 후 사용자 폴더에 설치\nnpm install -g @openai/codex\n설치 성공 후: codex login";
        return "npm이 없으면: brew install node (Homebrew가 없으면 https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.3/install.sh 로 Node.js LTS 설치)\nnpm install -g --prefix ~/.npm-global @openai/codex\n설치 성공 후: codex login";
    }

    internal static bool OpenTerminal(AiProvider provider, out string error)
    {
        error = "";
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            error = "자동 설치는 Windows와 macOS에서만 지원합니다. 아래 설치 명령을 터미널에서 실행하세요.";
            return false;
        }
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "gpixel-ai");
            Directory.CreateDirectory(dir);
            // 한 번 연 설치 창의 스크립트를 다음 설치 시 덮어쓰지 않는다.
            var script = Path.Combine(dir, $"install-{provider.Id}-{Guid.NewGuid():N}" + (OperatingSystem.IsWindows() ? ".cmd" : ".command"));
            if (OperatingSystem.IsWindows() && provider.Id == "codex")
                File.WriteAllText(script + ".ps1", WindowsNodeScript, new UTF8Encoding(false));
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
                error = "Terminal을 열지 못했습니다.";
                return false;
            }
            using (var p = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/k", script },
                UseShellExecute = true,
            }))
                if (p is not null) return true;
            error = "명령 프롬프트를 열지 못했습니다.";
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string WindowsScript(AiProvider provider) => provider.Id == "claude"
        ? """
          @echo off
          chcp 65001 >nul
          powershell -NoProfile -Command "irm https://claude.ai/install.ps1 | iex"
          if errorlevel 1 goto failed
          set "PATH=%USERPROFILE%\.local\bin;%PATH%"
          where claude >nul 2>nul
          if errorlevel 1 goto failed
          call claude auth login
          if errorlevel 1 goto failed
          echo 로그인 완료. Gpixel로 돌아가세요.
          goto end
          :failed
          echo 설치 또는 로그인에 실패했습니다. 위 오류를 확인하세요.
          :end
          """
        : """
          @echo off
          chcp 65001 >nul
          where npm >nul 2>nul
          if not errorlevel 1 goto npm_ready
          where winget >nul 2>nul
          if errorlevel 1 goto install_node_direct
          winget install --id OpenJS.NodeJS.LTS --exact --source winget --accept-package-agreements --accept-source-agreements
          if errorlevel 1 goto install_node_direct
          set "PATH=%ProgramFiles%\nodejs;%PATH%"
          goto npm_ready
          :install_node_direct
          powershell -NoProfile -ExecutionPolicy Bypass -File "%~f0.ps1"
          if errorlevel 1 goto failed
          set "PATH=%LOCALAPPDATA%\Gpixel\Node;%PATH%"
          :npm_ready
          set "npm_config_prefix=%APPDATA%\npm"
          where npm >nul 2>nul
          if errorlevel 1 goto failed
          call npm install -g @openai/codex
          if errorlevel 1 goto failed
          set "PATH=%APPDATA%\npm;%PATH%"
          where codex >nul 2>nul
          if errorlevel 1 goto failed
          call codex login
          if errorlevel 1 goto failed
          echo 로그인 완료. Gpixel로 돌아가세요.
          goto end
          :failed
          echo 설치 또는 로그인에 실패했습니다. 위 오류를 확인하세요.
          :end
          """;

    /// <summary>winget이 없는 Windows에서도 관리자 권한 없이 공식 Node.js LTS와 npm을 설치한다.</summary>
    private const string WindowsNodeScript = """
        $ErrorActionPreference = 'Stop'
        $target = Join-Path $env:LOCALAPPDATA 'Gpixel\Node'
        if (Test-Path (Join-Path $target 'node.exe')) { exit 0 }
        if (Test-Path $target) { throw "Node 설치 폴더가 이미 있습니다: $target" }
        $temp = Join-Path $env:TEMP ('gpixel-node-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temp | Out-Null
        try {
            $release = Invoke-RestMethod 'https://nodejs.org/dist/index.json' | Where-Object { $_.lts -ne $false } | Select-Object -First 1
            if (!$release) { throw 'Node.js LTS 버전을 찾지 못했습니다.' }
            $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
            $name = "node-$($release.version)-win-$arch.zip"
            $url = "https://nodejs.org/dist/$($release.version)"
            $zip = Join-Path $temp $name
            Invoke-WebRequest "$url/$name" -OutFile $zip
            $sums = (Invoke-WebRequest "$url/SHASUMS256.txt").Content
            $match = [regex]::Match($sums, '(?m)^([0-9a-fA-F]{64})  ' + [regex]::Escape($name) + '\r?$')
            if (!$match.Success -or (Get-FileHash -Algorithm SHA256 $zip).Hash -ne $match.Groups[1].Value) {
                throw 'Node.js 다운로드의 SHA-256 확인에 실패했습니다.'
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
                  curl -fsSL https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.3/install.sh -o "$TMPDIR/gpixel-nvm-install.sh" || fail
                  PROFILE=/dev/null bash "$TMPDIR/gpixel-nvm-install.sh" || fail
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
               "fail() { echo '설치 또는 로그인에 실패했습니다. 위 오류를 확인하세요.'; exit 1; }\n" +
               setup + "\necho '로그인 완료. Gpixel로 돌아가세요.'\n";
    }
}
