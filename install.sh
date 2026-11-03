#!/bin/sh
# Chickxel 설치(macOS 터미널): curl -fsSL https://raw.githubusercontent.com/1000Pro-1997/Chickxel/main/install.sh | sh
# Mac 칩에 맞는 최신 릴리스 zip을 받아 /Applications/Chickxel.app에 두고 실행한다. 받기·풀기가 끝난 뒤에만 옛 앱을 바꾼다.
# curl로 받은 파일에는 격리 표시가 붙지 않지만 혹시 붙어 있으면 지운다(Gatekeeper 경고가 뜨지 않게). 이후 업데이트는 앱이 스스로 한다.
set -eu
[ "$(uname -s)" = Darwin ] || { echo "This installer is for macOS." >&2; exit 1; }
# Rosetta로 연 터미널은 uname -m이 x86_64라서 칩은 sysctl로 본다.
if [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = 1 ]; then arch=arm64; else arch=x64; fi
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
echo "Downloading Chickxel ($arch)..."
curl -fL --progress-bar -o "$tmp/Chickxel.zip" "https://github.com/1000Pro-1997/Chickxel/releases/latest/download/Chickxel-macos-$arch.zip"
ditto -x -k "$tmp/Chickxel.zip" "$tmp/app"
rm -rf /Applications/Chickxel.app
ditto "$tmp/app/Chickxel.app" /Applications/Chickxel.app
xattr -dr com.apple.quarantine /Applications/Chickxel.app 2>/dev/null || true
echo "Chickxel installed: /Applications/Chickxel.app"
open /Applications/Chickxel.app
