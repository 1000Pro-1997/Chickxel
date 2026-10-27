#!/bin/sh
# 개발용 창 관리(macOS / Linux, dev.ps1과 같은 동작): ./dev.sh start | restart | stop(close) | status
#   start   Debug 빌드 후 실행(이미 떠 있으면 다시 띄우지 않음)
#   restart 창을 정상 종료한 뒤 다시 빌드·실행
#   stop    창을 정상 종료(close도 같음). 저장하지 않은 문서가 있으면 앱이 물어보며, 강제 종료하지 않는다.
#   status  이 저장소의 Debug Gpixel이 떠 있는지
# 이 저장소의 Debug 실행 파일만 찾으며 배포판(Gpixel.app)은 건드리지 않는다. 앱 출력은 .dev/gpixel.log에 남는다.
root=$(cd "$(dirname "$0")" && pwd -P) || exit 1
project="$root/src/Gpixel.App/Gpixel.App.csproj"
exe="$root/src/Gpixel.App/bin/Debug/net10.0/Gpixel.App"
log="$root/.dev/gpixel.log"

if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "$HOME/.dotnet/dotnet" ]; then PATH="$HOME/.dotnet:$PATH"; export DOTNET_ROOT="$HOME/.dotnet"
  else echo ".NET 10 SDK가 필요합니다: https://dotnet.microsoft.com/download" >&2; exit 1; fi
fi

# 이 실행 파일로 떠 있는 프로세스 번호들(실행 파일 경로가 정확히 같은 것만).
pids() {
  ps -axo pid=,comm= | awk -v exe="$exe" '{ pid = $1; $1 = ""; sub(/^ +/, ""); if ($0 == exe) print pid }'
}

stop_all() {
  for pid in $(pids); do
    kill -TERM "$pid" 2>/dev/null || continue
    i=0
    while kill -0 "$pid" 2>/dev/null; do
      i=$((i + 1))
      if [ "$i" -gt 100 ]; then
        echo "PID $pid: 아직 실행 중입니다(저장할지 묻는 중일 수 있음). 앱에서 마친 뒤 다시 하세요." >&2
        return 1
      fi
      sleep 0.1
    done
    echo "Gpixel PID $pid 종료."
  done
}

start() {
  dotnet build "$project" -c Debug --nologo -v q || { echo "빌드 실패: Gpixel을 띄우지 않았습니다." >&2; return 1; }
  mkdir -p "$root/.dev"
  (cd "$root" && nohup "$exe" >"$log" 2>&1 &)
  sleep 1.5
  pid=$(pids | head -n 1)
  if [ -z "$pid" ]; then
    echo "Gpixel이 시작하자마자 끝났습니다. 로그: $log" >&2
    tail -n 20 "$log" >&2
    return 1
  fi
  echo "로컬 Debug Gpixel PID $pid 실행."
}

case "${1:-start}" in
  status)
    running=$(pids)
    if [ -z "$running" ]; then echo "로컬 Debug Gpixel이 실행 중이 아닙니다."
    else for pid in $running; do echo "로컬 Debug Gpixel PID $pid 실행 중."; done; fi ;;
  stop|close) stop_all ;;
  restart) stop_all && start ;;
  start)
    if [ -n "$(pids)" ]; then echo "로컬 Debug Gpixel이 이미 실행 중입니다. 다시 빌드하려면 restart."; exit 0; fi
    start ;;
  *) echo "사용법: gpixel start | restart | stop(close) | status" >&2; exit 2 ;;
esac
