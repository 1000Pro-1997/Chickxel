#!/bin/sh
# 어디서든 chickxel start | restart | stop(close) | status를 쓰게 ~/.local/bin/chickxel을 만들어 이 저장소의 dev.sh에 연결한다(macOS / Linux).
# 저장소를 옮기면 다시 실행한다. 이 스크립트가 만든 것이 아닌 같은 이름의 명령은 덮어쓰지 않는다.
set -e
root=$(cd "$(dirname "$0")/.." && pwd -P)
bin="$HOME/.local/bin"
launcher="$bin/chickxel"
marker="# Managed by Chickxel tools/install-dev-command.sh"

mkdir -p "$bin"
if [ -e "$launcher" ] && ! grep -qF "$marker" "$launcher"; then
  echo "기존 명령을 덮어쓰지 않습니다: $launcher" >&2
  exit 1
fi
cat >"$launcher" <<EOF
#!/bin/sh
$marker
exec "$root/dev.sh" "\$@"
EOF
chmod +x "$launcher" "$root/dev.sh"

case ":$PATH:" in
  *":$bin:"*) ;;
  *)
    rc="$HOME/.zshrc"
    [ "$(basename "${SHELL:-}")" = "bash" ] && rc="$HOME/.bashrc"
    echo "export PATH=\"\$HOME/.local/bin:\$PATH\"" >>"$rc"
    echo "$rc 에 ~/.local/bin을 PATH로 더했습니다. 새 터미널에서 chickxel을 쓰세요." ;;
esac
echo "chickxel start / restart / stop(close) / status 설치: $launcher"
