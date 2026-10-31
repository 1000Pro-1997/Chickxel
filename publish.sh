#!/bin/sh
# 배포 파일 만들기(macOS에서 실행): .NET이 없는 PC에서도 바로 실행되는 자체 포함(self-contained) 앱.
#   dist/Chickxel-windows-x64.zip  → 압축 풀고 Chickxel.exe 더블클릭
#   dist/Chickxel-macos-arm64.zip  → Apple Silicon(M1~) Mac용 Chickxel.app
#   dist/Chickxel-macos-x64.zip    → Intel Mac용 Chickxel.app
#   dist/SHA256SUMS.txt          → zip들의 SHA-256(앱의 자동 업데이트가 이것으로 검증한다)
# 사용: ./publish.sh [버전]   (예: ./publish.sh 1.2.0, 기본 1.0.0)
# Linux에서도 Windows zip은 만들 수 있다(.app 서명은 macOS에서만).
set -eu
cd "$(dirname "$0")"

VERSION="${1:-${VERSION:-1.0.0}}"
PROJECT=src/Chickxel.App/Chickxel.App.csproj
DIST=dist
WORK="$DIST/.work"

if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "$HOME/.dotnet/dotnet" ]; then PATH="$HOME/.dotnet:$PATH"; export PATH
  else echo ".NET 10 SDK가 필요합니다: https://dotnet.microsoft.com/download" >&2; exit 1; fi
fi

rm -rf "$DIST"
mkdir -p "$WORK"

publish() { # $1 = RID, $2 = 출력 폴더, 나머지 = 추가 속성
  rid="$1"; out="$2"; shift 2
  echo "== $rid"
  dotnet publish "$PROJECT" -c Release -r "$rid" --self-contained true \
    -p:Version="$VERSION" -p:DebugType=none -p:DebugSymbols=false -o "$out" "$@"
}

# ── Windows: exe 하나(실행할 때 필요한 네이티브 라이브러리도 안에 묶음)
publish win-x64 "$WORK/win-x64" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
mkdir -p "$WORK/Chickxel-windows-x64"
cp "$WORK/win-x64/Chickxel.App.exe" "$WORK/Chickxel-windows-x64/Chickxel.exe"
(cd "$WORK" && zip -qr "../Chickxel-windows-x64.zip" "Chickxel-windows-x64")

# ── macOS: Chickxel.app 묶음(Apple Silicon / Intel)
if [ "$(uname -s)" = "Darwin" ]; then
  for arch in arm64 x64; do
    publish "osx-$arch" "$WORK/osx-$arch"
    app="$WORK/mac-$arch/Chickxel.app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -R "$WORK/osx-$arch/." "$app/Contents/MacOS/"
    sed "s/@VERSION@/$VERSION/g" packaging/macos/Info.plist > "$app/Contents/Info.plist"
    cp src/Chickxel.App/Assets/AppIcon/Chickxel.icns "$app/Contents/Resources/Chickxel.icns"
    chmod +x "$app/Contents/MacOS/Chickxel.App"
    # 임시(ad-hoc) 서명: Apple Silicon은 서명 없는 앱을 실행하지 않는다.
    codesign --force --deep --sign - "$app"
    ditto -c -k --keepParent "$app" "$DIST/Chickxel-macos-$arch.zip"
  done
else
  echo "macOS가 아니라서 .app은 건너뜁니다."
fi

# ── 체크섬: 앱의 자동 업데이트는 이 파일이 없는 릴리스는 자동으로 설치하지 않는다.
(cd "$DIST" && if command -v shasum >/dev/null 2>&1; then shasum -a 256 *.zip; else sha256sum *.zip; fi > SHA256SUMS.txt)

rm -rf "$WORK"
echo
echo "완료 (버전 $VERSION):"
ls -lh "$DIST"
