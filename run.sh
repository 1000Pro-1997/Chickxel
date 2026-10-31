#!/bin/sh
# 개발용: 소스에서 바로 빌드해 실행(macOS / Linux). .NET 없이 쓰려면 publish.sh로 만든 Chickxel.app을 쓴다.
cd "$(dirname "$0")" || exit 1
if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "$HOME/.dotnet/dotnet" ]; then PATH="$HOME/.dotnet:$PATH"; export DOTNET_ROOT="$HOME/.dotnet"
  else echo ".NET 10 SDK가 필요합니다: https://dotnet.microsoft.com/download" >&2; exit 1; fi
fi
exec dotnet run --project src/Chickxel.App/Chickxel.App.csproj -c Release -- "$@"
