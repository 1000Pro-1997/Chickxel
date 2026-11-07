#!/bin/sh
# Chickxel 설치(macOS·Linux 터미널): curl -fsSL https://raw.githubusercontent.com/1000Pro-1997/Chickxel/main/install.sh | sh
# macOS: Mac 칩에 맞는 최신 릴리스 zip을 받아 /Applications/Chickxel.app에 두고 실행한다.
#   curl로 받은 파일에는 격리 표시가 붙지 않지만 혹시 붙어 있으면 지운다(Gatekeeper 경고가 뜨지 않게).
# Linux: CPU에 맞는 zip을 받아 ~/.local/share/Chickxel/Chickxel에 두고, 프로그램 메뉴(.desktop)와 아이콘을 등록한 뒤 실행한다.
#   사용자 폴더라 sudo가 필요 없고, 앱이 이 파일을 스스로 업데이트한다.
# 받기·풀기가 끝난 뒤에만 옛 앱을 바꾼다. 이후 업데이트는 앱이 스스로 한다.
set -eu
BASE=https://github.com/1000Pro-1997/Chickxel/releases/latest/download
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

install_mac() {
  # Rosetta로 연 터미널은 uname -m이 x86_64라서 칩은 sysctl로 본다.
  if [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = 1 ]; then arch=arm64; else arch=x64; fi
  echo "Downloading Chickxel ($arch)..."
  curl -fL --progress-bar -o "$tmp/Chickxel.zip" "$BASE/Chickxel-macos-$arch.zip"
  ditto -x -k "$tmp/Chickxel.zip" "$tmp/app"
  rm -rf /Applications/Chickxel.app
  ditto "$tmp/app/Chickxel.app" /Applications/Chickxel.app
  xattr -dr com.apple.quarantine /Applications/Chickxel.app 2>/dev/null || true
  echo "Chickxel installed: /Applications/Chickxel.app"
  open /Applications/Chickxel.app
}

# zip 풀기: unzip이 없는 배포판도 있어 bsdtar, python3 순으로 찾는다.
unzip_to() {
  if command -v unzip >/dev/null 2>&1; then unzip -q "$1" -d "$2"
  elif command -v bsdtar >/dev/null 2>&1; then mkdir -p "$2" && bsdtar -xf "$1" -C "$2"
  elif command -v python3 >/dev/null 2>&1; then python3 -m zipfile -e "$1" "$2"
  else echo "Install unzip first (for example: sudo apt install unzip)." >&2; exit 1
  fi
}

install_linux() {
  case "$(uname -m)" in
    x86_64 | amd64) arch=x64 ;;
    aarch64 | arm64) arch=arm64 ;;
    *) echo "Chickxel for Linux supports x64 and ARM64 only (this CPU: $(uname -m))." >&2; exit 1 ;;
  esac
  data="${XDG_DATA_HOME:-$HOME/.local/share}"
  dir="$data/Chickxel"
  exe="$dir/Chickxel"
  icon="$data/icons/hicolor/256x256/apps/chickxel.png"
  desktop="$data/applications/chickxel.desktop"
  echo "Downloading Chickxel (linux-$arch)..."
  curl -fL --progress-bar -o "$tmp/Chickxel.zip" "$BASE/Chickxel-linux-$arch.zip"
  unzip_to "$tmp/Chickxel.zip" "$tmp/app"
  pkg="$tmp/app/Chickxel-linux-$arch"
  [ -f "$pkg/Chickxel" ] || { echo "The download has no Chickxel executable." >&2; exit 1; }
  mkdir -p "$dir" "$(dirname "$icon")" "$(dirname "$desktop")"
  # 새 이름으로 복사한 뒤 바꿔 넣는다(실행 중인 옛 앱이 있어도 된다).
  cp "$pkg/Chickxel" "$exe.new"
  chmod 755 "$exe.new"
  mv -f "$exe.new" "$exe"
  cp "$pkg/chickxel.png" "$icon"
  cat >"$desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Chickxel
Comment=Pixel art editor for games
Exec="$exe"
Icon=$icon
Terminal=false
Categories=Graphics;2DGraphics;RasterGraphics;
EOF
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$(dirname "$desktop")" 2>/dev/null || true
  echo "Chickxel installed: $exe"
  if [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
    if command -v setsid >/dev/null 2>&1; then setsid "$exe" </dev/null >/dev/null 2>&1 &
    else nohup "$exe" </dev/null >/dev/null 2>&1 &
    fi
  else
    echo "Open Chickxel from the applications menu."
  fi
}

case "$(uname -s)" in
  Darwin) install_mac ;;
  Linux) install_linux ;;
  *) echo "This installer is for macOS and Linux." >&2; exit 1 ;;
esac
