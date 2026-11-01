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
