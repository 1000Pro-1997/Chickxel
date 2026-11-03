// 내려받기(첫 화면 index.html, 다운로드 안내 download.html).
// - 첫 화면 다운로드 단추: 접속한 OS를 알면 그 파일을 받는 download.html?os=… 로(모르면 아래 다운로드 구역 그대로).
// - 터미널 설치(.install): 접속한 OS의 탭을 먼저 고른다. 명령 줄의 복사 단추(.cmd-copy).
// - download.html: ?os=windows|mac-arm64|mac-x64 이면 그 파일을 바로 받고 그 OS 안내만 보인다. 주소에서 os를 지워
//   새로 고침·뒤로 가기·언어 바꾸기로 다시 받지 않게 한다. os가 없으면 받지 않고 접속한 OS 안내만 보인다.
(function () {
  var BASE = 'https://github.com/1000Pro-1997/Chickxel/releases/latest/download/';
  var FILES = { windows: 'Chickxel-windows-x64.zip', 'mac-arm64': 'Chickxel-macos-arm64.zip', 'mac-x64': 'Chickxel-macos-x64.zip' };
  var ua = navigator.platform || navigator.userAgent;
  var detected = /Mac/i.test(ua) ? 'mac-arm64' : /Win/i.test(ua) ? 'windows' : null;
  function family(os) { return os ? os.split('-')[0] : ''; }

  var main = document.getElementById('main-download');
  if (main && detected) main.href = 'download.html?os=' + detected;

  document.querySelectorAll('.install').forEach(function (box) {
    var tabs = box.querySelectorAll('[role="tab"]');
    function show(f) {
      tabs.forEach(function (t) { t.setAttribute('aria-selected', String(t.dataset.os === f)); });
      box.querySelectorAll('[data-pane]').forEach(function (el) { el.hidden = el.dataset.pane !== f; });
    }
    tabs.forEach(function (t) { t.addEventListener('click', function () { show(t.dataset.os); }); });
    show(family(detected) || 'windows');
  });

  document.querySelectorAll('.cmd-copy').forEach(function (btn) {
    btn.addEventListener('click', function () {
      if (!navigator.clipboard) return;
      navigator.clipboard.writeText(btn.parentNode.querySelector('code').textContent).then(function () {
        btn.classList.add('copied');
        setTimeout(function () { btn.classList.remove('copied'); }, 1600);
      });
    });
  });

  var starting = document.getElementById('dl-start');
  if (!starting) return;
  var links = document.querySelectorAll('.dl-files [data-os]');
  function showOs(os) {
    document.body.dataset.os = family(os);
    links.forEach(function (a) { a.classList.toggle('primary', a.dataset.os === os); });
  }
  function start(os) {
    var url = BASE + FILES[os];
    document.getElementById('dl-file').textContent = FILES[os];
    document.getElementById('dl-retry').href = url;
    document.getElementById('dl-pick').hidden = true;
    starting.hidden = false;
    showOs(os);
    // GitHub이 첨부 파일로 주므로 이 쪽은 그대로 남는다.
    location.href = url;
  }
  links.forEach(function (a) {
    a.addEventListener('click', function (e) { e.preventDefault(); start(a.dataset.os); });
  });
  var asked = new URLSearchParams(location.search).get('os');
  if (FILES[asked]) {
    history.replaceState(null, '', location.pathname + location.hash);
    start(asked);
  } else {
    showOs(detected);
  }
})();
