// 첫 화면 다운로드 구역의 "커피를 사 주신 분들"(.supporters): 서버 /api/supporters의 이름 목록을 커피 아이콘과 함께 보인다.
// 받은 목록은 브라우저에 한 시간 동안 두고(localStorage) 그 뒤에 다시 받는다. 아무도 없거나 받지 못하면 구역을 숨긴 채로 둔다.
(function () {
  var box = document.querySelector('.supporters');
  if (!box) return;
  var list = box.querySelector('.supporter-list');
  var KEY = 'chickxel.supporters';
  var HOUR = 60 * 60 * 1000;
  var ICON = '<svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M10 2v2"/><path d="M14 2v2"/><path d="M16 8a1 1 0 0 1 1 1v8a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4V9a1 1 0 0 1 1-1h14a4 4 0 1 1 0 8h-1"/><path d="M6 2v2"/></svg>';

  function show(names) {
    list.textContent = '';
    names.forEach(function (name) {
      var li = document.createElement('li');
      li.innerHTML = ICON;
      li.appendChild(document.createTextNode(name));
      list.appendChild(li);
    });
    box.hidden = names.length === 0;
  }

  var saved = null;
  try { saved = JSON.parse(localStorage.getItem(KEY) || 'null'); } catch (e) { saved = null; }
  if (saved && Array.isArray(saved.names)) show(saved.names);
  if (saved && Date.now() - saved.at < HOUR) return;

  fetch('/api/supporters', { headers: { Accept: 'application/json' } })
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (body) {
      if (!body || !Array.isArray(body.supporters)) return;
      var names = body.supporters.map(function (s) { return String(s.name || ''); }).filter(function (n) { return n.length > 0; });
      try { localStorage.setItem(KEY, JSON.stringify({ at: Date.now(), names: names })); } catch (e) { /* 저장 못 하면 다음에 다시 받음 */ }
      show(names);
    })
    .catch(function () { /* 받지 못하면 그대로 */ });
})();
