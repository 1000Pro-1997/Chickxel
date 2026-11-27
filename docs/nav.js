// 첫 화면 다운로드 구역의 "커피를 사 주신 분들": 목록을 브라우저에 한 시간 동안 둔다.
// 위 막대의 커피 단추는 그 마을을 새 창으로 여는 링크다.
(function () {
  var KEY = 'chickxel.supporters';
  var HOUR = 60 * 60 * 1000;
  var waiting = null;
  function load(done) {
    var saved = null;
    try { saved = JSON.parse(localStorage.getItem(KEY) || 'null'); } catch (e) { saved = null; }
    if (saved && Array.isArray(saved.names)) done(saved.names);
    if (saved && Date.now() - saved.at < HOUR) return;
    if (!waiting) {
      waiting = fetch('https://chickxel.com/api/supporters', { headers: { Accept: 'application/json' } })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (body) {
          if (!body || !Array.isArray(body.supporters)) return null;
          var names = body.supporters.map(function (s) { return String(s.name || ''); }).filter(function (n) { return n.length > 0; });
          try { localStorage.setItem(KEY, JSON.stringify({ at: Date.now(), names: names })); } catch (e) { /* 저장 못 하면 다음에 다시 받음 */ }
          return names;
        })
        .catch(function () { return null; });
    }
    var pending = waiting;
    pending.then(function (names) {
      if (names) done(names);
      else if (waiting === pending) waiting = null;
    });
  }
  var section = document.querySelector('.supporters');
  if (!section) return;
  var ICON = '<svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M10 2v2"/><path d="M14 2v2"/><path d="M16 8a1 1 0 0 1 1 1v8a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4V9a1 1 0 0 1 1-1h14a4 4 0 1 1 0 8h-1"/><path d="M6 2v2"/></svg>';
  var list = section.querySelector('.supporter-list');
  load(function (names) {
    list.textContent = '';
    names.forEach(function (name) {
      var li = document.createElement('li');
      li.innerHTML = ICON;
      li.appendChild(document.createTextNode(name));
      list.appendChild(li);
    });
    section.hidden = names.length === 0;
  });
})();

// 위 막대의 계정 메뉴(에셋 사이트 chickxel.com/static/site.js의 머리글과 같은 모양).
// 열고 닫기, 언어 바꾸기(고른 언어를 기억하고 같은 쪽의 그 언어 페이지로),
// chickxel.com에서 열렸으면 로그인 상태(프로필·내 계정·관리·로그아웃)를 보인다. github.io에서는 세션을 볼 수 없어 로그인 단추만.
(function () {
  var box = document.getElementById('account-menu');
  if (!box) return;
  var btn = box.querySelector('.account-btn');
  var menu = box.querySelector('.menu');

  function setOpen(open) {
    menu.hidden = !open;
    btn.setAttribute('aria-expanded', String(open));
  }
  btn.addEventListener('click', function () { setOpen(menu.hidden); });
  document.addEventListener('click', function (e) { if (!box.contains(e.target)) setOpen(false); });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && !menu.hidden) { setOpen(false); btn.focus(); }
  });

  var lang = document.documentElement.lang;
  menu.querySelectorAll('[data-lang]').forEach(function (a) {
    if (a.dataset.lang === lang) a.setAttribute('aria-current', 'true');
    a.addEventListener('click', function (e) {
      try { localStorage.setItem('chickxel-lang', a.dataset.lang); } catch (err) {}
      if (a.dataset.lang === lang) { e.preventDefault(); setOpen(false); return; }
      a.href = a.href.split('#')[0] + location.hash;
    });
  });

  if (location.hostname !== 'chickxel.com') return;
  menu.querySelector('[data-when="out"]').href = '/login?return=' + encodeURIComponent(location.pathname);
  fetch('/api/me', { credentials: 'same-origin', headers: { Accept: 'application/json' } })
    .then(function (res) { return res.ok ? res.json() : null; })
    .then(function (me) { if (me) showUser(me); })
    .catch(function () { /* 로그인 단추 그대로 */ });

  function avatarInto(host, url) {
    if (typeof url !== 'string' || !(/^https:\/\//i.test(url) || /^\/(?![\/\\])/.test(url))) return;
    var img = document.createElement('img');
    img.src = url;
    img.alt = '';
    img.referrerPolicy = 'no-referrer';
    img.addEventListener('error', function () { img.remove(); });
    host.textContent = '';
    host.appendChild(img);
  }

  function showUser(me) {
    menu.querySelectorAll('[data-when="in"]').forEach(function (el) { el.hidden = false; });
    menu.querySelector('[data-when="out"]').hidden = true;
    menu.querySelector('[data-when="admin"]').hidden = !me.isAdmin;
    var head = menu.querySelector('.menu-head');
    head.querySelector('strong').textContent = me.name;
    var headIcon = btn.querySelector('.account-icon').cloneNode(true);
    head.querySelector('.account-icon').replaceWith(headIcon);
    avatarInto(headIcon, me.avatarUrl);
    head.querySelector(':scope > div > span').textContent = '@' + me.handle;
    avatarInto(btn.querySelector('.account-icon'), me.avatarUrl);
    menu.querySelector('[data-logout]').addEventListener('click', function () {
      fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin', headers: { 'X-Requested-With': 'chickxel' } })
        .then(function () { location.reload(); });
    });
  }
})();
