// 내려받기(첫 화면 index.html, 다운로드 안내 download.html).
// - 첫 화면 다운로드 단추: 접속한 OS를 알면 그 파일을 받는 download.html?os=… 로(모르면 아래 다운로드 구역 그대로).
// - 터미널 설치(.install): 접속한 OS의 탭을 먼저 고른다. 명령 줄의 복사 단추(.cmd-copy).
// - download.html: ?os=windows|mac-arm64|mac-x64|linux-x64|linux-arm64 이면 그 파일을 바로 받고 그 OS 안내만 보인다. 주소에서 os를 지워
//   새로 고침·뒤로 가기·언어 바꾸기로 다시 받지 않게 한다. os가 없으면 받지 않고 접속한 OS 안내만 보인다.
// - Linux: Android(이름에 Linux가 들어감)는 거르고, CPU는 navigator.platform(x86_64·aarch64)으로 본다. 모르는 CPU는 고르지 않는다.
//   Chromium은 platform을 x86_64로 줄여 보여 줄 수 있어 userAgentData의 architecture로 한 번 더 고친다.
(function () {
  var BASE = 'https://github.com/1000Pro-1997/Chickxel/releases/latest/download/';
  var FILES = {
    windows: 'Chickxel-windows-x64.zip', 'mac-arm64': 'Chickxel-macos-arm64.zip', 'mac-x64': 'Chickxel-macos-x64.zip',
    'linux-x64': 'Chickxel-linux-x64.zip', 'linux-arm64': 'Chickxel-linux-arm64.zip'
  };
  var ua = navigator.platform || navigator.userAgent;
  function linuxFile(cpu) {
    if (/x86_64|amd64|x64/i.test(cpu)) return 'linux-x64';
    if (/aarch64|arm64|armv8/i.test(cpu)) return 'linux-arm64';
    return null;
  }
  var android = /Android/i.test(navigator.userAgent);
  var linux = !android && /Linux/i.test(ua);
  var detected = /Mac/i.test(ua) ? 'mac-arm64' : /Win/i.test(ua) ? 'windows' : linux ? linuxFile(ua) : null;
  function family(os) { return os ? os.split('-')[0] : ''; }
  // 파일은 몰라도(Linux의 다른 CPU) 안내·설치 탭은 그 OS로.
  var detectedFamily = family(detected) || (linux ? 'linux' : '');
  var refined = [];
  if (linux && navigator.userAgentData && navigator.userAgentData.getHighEntropyValues) {
    navigator.userAgentData.getHighEntropyValues(['architecture', 'bitness']).then(function (v) {
      var os = v.bitness !== '64' ? null : v.architecture === 'x86' ? 'linux-x64' : v.architecture === 'arm' ? 'linux-arm64' : null;
      if (!os || os === detected) return;
      detected = os;
      refined.forEach(function (f) { f(); });
    }, function () {});
  }

  var main = document.getElementById('main-download');
  function pointMain() { if (main && detected) main.href = 'download.html?os=' + detected; }
  pointMain();
  refined.push(pointMain);

  document.querySelectorAll('.install').forEach(function (box) {
    var tabs = box.querySelectorAll('[role="tab"]');
    function show(f) {
      tabs.forEach(function (t) { t.setAttribute('aria-selected', String(t.dataset.os === f)); });
      box.querySelectorAll('[data-pane]').forEach(function (el) { el.hidden = el.dataset.pane !== f; });
    }
    tabs.forEach(function (t) { t.addEventListener('click', function () { show(t.dataset.os); }); });
    show(detectedFamily || 'windows');
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
  function showOs(os, fam) {
    document.body.dataset.os = fam || family(os);
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
    showOs(detected, detectedFamily);
    refined.push(function () { if (starting.hidden) showOs(detected, detectedFamily); });
  }
})();
