// 병아리 후원 마을(위 막대의 커피 단추를 누르면 펼쳐짐 — 홈페이지 nav.js와 에셋 사이트 site.js가 같이 씀).
// 대각선(아이소메트릭) 타일 섬 한가운데 감사 기념비가 있고, 커피를 사 주고 이름을 남긴 사람마다 집 한 채와 병아리 한 마리가 산다
// (이름표는 HTML 글자로 그 병아리 위에). 집 자리보다 사람이 많으면 나머지 병아리는 광장에서 논다.
// 후원자 목록(https://chickxel.com/api/supporters)은 브라우저에 한 시간 동안 두고(localStorage 'chickxel.supporters') 그 뒤에 다시 받는다.
(function () {
  var KEY = 'chickxel.supporters';
  var HOUR = 60 * 60 * 1000;
  var W = 196, H = 128, SCALE = 2, OX = 90, OY = 28;
  var N = 11, C = 5; // 섬 크기(타일 N×N)와 가운데 칸

  // ---------------------------------------------------------------- 후원자 목록
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
    // 받지 못했으면 다음에 펼칠 때 다시 받는다
    var pending = waiting;
    pending.then(function (names) {
      if (names) done(names);
      else if (waiting === pending) waiting = null;
    });
  }

  // ---------------------------------------------------------------- 그리기 도구(낮은 해상도 캔버스, 픽셀 단위)
  var g;
  function px(x, y, w, h, c) { g.fillStyle = c; g.fillRect(Math.round(x), Math.round(y), w, h); }
  function iso(i, j) { return { x: OX + (i - j) * 8, y: OY + (i + j) * 4 }; }
  // 마름모 타일 한 칸(왼쪽 위 모서리 x, y — 16×8)
  function diamond(x, y, c) {
    for (var dy = 0; dy < 8; dy++) {
      var half = dy < 4 ? (dy + 1) * 2 : (8 - dy) * 2;
      px(x + 8 - half, y + dy, half * 2, 1, c);
    }
  }
  function sprite(x, y, rows, colors) {
    for (var r = 0; r < rows.length; r++)
      for (var c = 0; c < rows[r].length; c++) {
        var k = rows[r][c];
        if (colors[k]) px(x + c, y + r, 1, 1, colors[k]);
      }
  }

  // 마스코트 병아리(앱 아이콘의 그 병아리: 동그란 노란 몸, 주황 부리, 분홍 볼, 머리 깃털)
  var CHICK = [
    '...oo...',
    '..oooo..',
    '.oyyyyo.',
    'oyeyyeyo',
    'opybbypo',
    'oyyyyyyo',
    '.oyyyyo.',
    '..f..f..'
  ];
  var CHICK_BLINK = CHICK.slice();
  CHICK_BLINK[3] = 'oyoyyoyo';
  var CHICK_COLORS = { o: '#8a5a14', y: '#ffd23f', e: '#2b1d0e', b: '#ff8a2a', p: '#ff9fb0', f: '#ff8a2a' };

  // 집: 바닥 마름모(x, y) 위에 벽 높이 h, 뾰족 지붕. 벽 색은 집마다 조금씩.
  var WALLS = [['#fff1c9', '#f3d99a'], ['#ffe0e6', '#f2bcc8'], ['#e3f3ff', '#bcd9ef'], ['#e9ffe0', '#c4e8b5']];
  var ROOFS = [['#ff8a5b', '#e0623a'], ['#ffb347', '#e58f1f'], ['#c78bff', '#9f62e0'], ['#5fc4ff', '#2f9de0']];
  function house(x, y, kind) {
    var h = 9, wall = WALLS[kind % WALLS.length], roof = ROOFS[kind % ROOFS.length];
    for (var c = 0; c < 16; c++) {
      var d = Math.min(c, 15 - c) / 2;
      var bottom = y + 4 + d;
      px(x + c, bottom - h, 1, h + 1, c < 8 ? wall[1] : wall[0]);
    }
    // 창문(왼쪽 벽), 문(오른쪽 벽)
    px(x + 3, y + 1, 2, 2, '#7fc8ff'); px(x + 3, y + 1, 1, 1, '#d6f0ff');
    px(x + 11, y + 2, 2, 4, '#a0622a'); px(x + 12, y + 4, 1, 1, '#ffd23f');
    // 지붕: 꼭대기(x+8, y-h-6)에서 양옆 모서리로
    for (c = 0; c < 16; c++) {
      var top = y - h - 6 + Math.abs(c - 7.5) * 1.25;
      var under = y - h + 4 + Math.min(c, 15 - c) / 2 + 1;
      px(x + c, top, 1, Math.max(1, under - top), c < 8 ? roof[1] : roof[0]);
    }
    px(x + 7, y - h - 7, 2, 2, roof[0]);
    // 굴뚝
    px(x + 11, y - h - 4, 2, 4, '#9a8f86'); px(x + 11, y - h - 5, 2, 1, '#bdb3aa');
  }

  function tree(x, y) {
    px(x + 7, y - 2, 2, 5, '#8a5a2b');
    var blob = ['..gggg..', '.gGGggg.', 'gGGgggdg', 'gGggggdg', 'gggggddg', '.gggddg.', '..gddd..'];
    sprite(x + 4, y - 9, blob, { g: '#6fcf5f', G: '#a8ec8c', d: '#4aa845' });
  }
  function flower(x, y, c) { px(x, y, 1, 1, c); px(x, y + 1, 1, 1, '#4aa845'); }

  // 감사 기념비: 두 단 받침돌 위 둥근 비석, 금빛 하트, 꼭대기에 김 나는 커피잔
  function monument(x, y, t) {
    px(x + 1, y + 1, 14, 3, '#a7a3b5'); px(x + 2, y, 12, 1, '#c9c5d6');
    px(x + 3, y - 2, 10, 2, '#b8b4c6'); px(x + 3, y - 3, 10, 1, '#d4d0e0');
    px(x + 4, y - 21, 8, 18, '#dcd8e8');
    px(x + 4, y - 21, 1, 18, '#f1eff8');
    px(x + 11, y - 21, 1, 18, '#b3afc2');
    px(x + 5, y - 22, 6, 1, '#dcd8e8'); px(x + 6, y - 23, 4, 1, '#dcd8e8');
    sprite(x + 5, y - 17, ['.hh.hh.', 'hHhhhhh', 'hhhhhhh', '.hhhhh.', '..hhh..', '...h...'], { h: '#ffcc33', H: '#fff2b0' });
    px(x + 5, y - 9, 6, 1, '#a9a5b9'); px(x + 5, y - 7, 6, 1, '#a9a5b9'); px(x + 6, y - 5, 4, 1, '#a9a5b9');
    // 커피잔(꼭대기)
    sprite(x + 4, y - 29, ['.wwwww..', 'wcccccww', 'wcccccw.w', '.wwwww.', '..www...'], { w: '#ffffff', c: '#8a5a2b' });
    var s = Math.floor(t / 400) % 3;
    px(x + 6 + (s === 1 ? 1 : 0), y - 32 - s, 1, 2, 'rgba(255,255,255,0.85)');
    px(x + 9 - (s === 2 ? 1 : 0), y - 33 + (s === 0 ? 1 : 0), 1, 2, 'rgba(255,255,255,0.7)');
  }

  // ---------------------------------------------------------------- 마을 배치
  // 집 자리(먼저 들어온 사람부터, 잘 보이는 자리 먼저): 길(i = C, j = C)과 광장(가운데 3×3)을 피해 섬 위에 고르게.
  var SLOTS = [[8, 2], [2, 8], [8, 8], [2, 2], [2, 5.5], [8, 5.5], [5.5, 2], [5.5, 8], [0, 0.5], [10, 9.5], [0.5, 10], [9.5, 0], [0, 3.5], [3.5, 0], [10, 6.5], [6.5, 10]];
  var TREES = [[0, 2], [2, 0], [10, 2], [2, 10], [10, 8], [8, 10], [0, 8], [8, 0], [4, 1], [1, 4], [9, 6], [6, 9], [10, 0], [0, 10]];

  function isPath(i, j) { return i === C || j === C; }
  function isPlaza(i, j) { return Math.abs(i - C) <= 1 && Math.abs(j - C) <= 1; }

  function drawGround(t) {
    // 섬 옆면(앞쪽 두 변): 타일 아래 모서리를 따라 흙 두께를 내리긋는다
    for (var k = 0; k < N; k++) {
      var a = iso(N - 1, k), b = iso(k, N - 1);
      for (var c = 0; c < 8; c++) {
        px(a.x + 8 + c, a.y + 8 - c / 2, 1, 6, '#c08a52'); px(a.x + 8 + c, a.y + 12 - c / 2, 1, 2, '#93623a');
        px(b.x + c, b.y + 4 + c / 2, 1, 6, '#a36f3d'); px(b.x + c, b.y + 8 + c / 2, 1, 2, '#7f5230');
      }
    }
    for (var i = 0; i < N; i++)
      for (var j = 0; j < N; j++) {
        var p = iso(i, j);
        var col = isPlaza(i, j) ? ((i + j) % 2 ? '#e8dcc8' : '#f2e8d6')
          : isPath(i, j) ? ((i + j) % 2 ? '#e6c98f' : '#efd6a2')
          : ((i * 7 + j * 3) % 5 === 0 ? '#8fdc72' : (i + j) % 2 ? '#7ed36a' : '#86d870');
        diamond(p.x, p.y, col);
      }
    // 꽃(살랑살랑)
    var flowers = [[1, 2, '#ff9fb0'], [9, 3, '#fff'], [3, 9, '#ffd23f'], [7, 1, '#c78bff'], [0, 5, '#ff9fb0'], [10, 4, '#fff'], [3, 1, '#ffd23f'], [7, 7, '#ff9fb0'], [1, 7, '#c78bff']];
    flowers.forEach(function (f) {
      var p = iso(f[0], f[1]);
      flower(p.x + 6, p.y + 3 + (Math.sin(t / 600 + f[0]) > 0.6 ? -1 : 0), f[2]);
      flower(p.x + 10, p.y + 4, f[2]);
    });
  }

  function cloud(x, y) {
    px(x + 2, y, 8, 1, '#ffffff'); px(x, y + 1, 14, 2, '#ffffff'); px(x + 4, y - 1, 4, 1, '#ffffff');
    px(x + 1, y + 3, 12, 1, '#e7eefc');
  }

  // ---------------------------------------------------------------- 한 마을(캔버스 + 이름표)
  function Village(host, names) {
    var canvas = document.createElement('canvas');
    canvas.width = W; canvas.height = H;
    canvas.style.width = W * SCALE + 'px'; canvas.style.height = H * SCALE + 'px';
    canvas.className = 'village-canvas';
    canvas.setAttribute('role', 'img');
    canvas.setAttribute('aria-label', names.join(', '));
    host.textContent = '';
    host.appendChild(canvas);
    var ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = false;

    // 주민: 앞 SLOTS.length명은 집 앞, 나머지는 광장을 돌아다님. 이름 없는 아기 병아리 둘은 늘 광장에서 논다(빈 마을도 심심하지 않게).
    var residents = names.map(function (name, k) {
      var slot = SLOTS[k];
      var label = document.createElement('span');
      label.className = 'village-name';
      label.textContent = name;
      host.appendChild(label);
      return {
        name: name, label: label, home: slot || null, kind: k,
        phase: k * 1.7, wander: slot ? null : { a: k * 2.3 }
      };
    }).concat([0, 1].map(function (k) {
      return { name: '', label: null, home: null, kind: 0, phase: 0.8 + k * 2.9, wander: { a: k * Math.PI } };
    }));
    // 위쪽 하늘에 고정한 감사 띠(기념비 이름표가 집 이름표와 겹치지 않게)
    var thanks = document.createElement('span');
    thanks.className = 'village-name village-thanks';
    thanks.textContent = document.documentElement.lang === 'ko' ? '커피 고맙습니다!' : 'Thanks for the coffee!';
    thanks.style.left = W * SCALE / 2 + 'px';
    thanks.style.top = '30px';
    host.appendChild(thanks);

    function frame(t) {
      g = ctx;
      g.clearRect(0, 0, W, H);
      var drift = (t / 90) % (W + 40);
      cloud(drift - 30, 6); cloud(((drift + 110) % (W + 40)) - 30, 14);
      drawGround(t);

      // 깊이(i + j) 순서로 나무·집·기념비·병아리
      var items = [];
      TREES.forEach(function (tr) { items.push({ d: tr[0] + tr[1], draw: function () { var p = iso(tr[0], tr[1]); tree(p.x, p.y + 4); } }); });
      items.push({ d: C * 2, draw: function () { var p = iso(C, C); monument(p.x, p.y + 3, t); } });
      residents.forEach(function (r) {
        var hop = Math.max(0, Math.sin(t / 260 + r.phase)) > 0.92 ? 2 : Math.sin(t / 260 + r.phase) > 0.7 ? 1 : 0;
        var blink = Math.sin(t / 900 + r.phase * 3) > 0.985;
        if (r.home) {
          var hi = r.home[0], hj = r.home[1];
          items.push({ d: hi + hj, draw: function () { var p = iso(hi, hj); house(p.x, p.y, r.kind); } });
          items.push({ d: hi + hj + 0.6, draw: function () {
            var p = iso(hi, hj);
            sprite(p.x + 10, p.y + 3 - hop, blink ? CHICK_BLINK : CHICK, CHICK_COLORS);
            place(r.label, p.x + 14, p.y - 1 - hop);
          } });
        } else {
          // 광장 둘레를 천천히 돎
          var a = r.wander.a + t / 4000;
          var wi = C + Math.cos(a) * 1.4, wj = C + Math.sin(a) * 1.4;
          items.push({ d: wi + wj + 0.5, draw: function () {
            var p = iso(wi, wj);
            sprite(p.x + 4, p.y - 2 - hop, blink ? CHICK_BLINK : CHICK, CHICK_COLORS);
            place(r.label, p.x + 8, p.y - 6 - hop);
          } });
        }
      });
      items.sort(function (a, b) { return a.d - b.d; });
      items.forEach(function (it) { it.draw(); });
    }

    function place(label, x, y) {
      if (!label) return;
      label.style.left = x * SCALE + 'px';
      label.style.top = y * SCALE + 'px';
    }

    var running = false;
    function loop(t) {
      // 펼침이 닫히면(host가 화면에서 빠지면) 멈춘다
      if (!host.isConnected || host.offsetParent === null) { running = false; return; }
      frame(t);
      requestAnimationFrame(loop);
    }
    this.start = function () {
      if (running) return;
      running = true;
      if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) { frame(0); running = false; return; }
      requestAnimationFrame(loop);
    };
  }

  // host: 마을을 그릴 칸, emptyEl: 아무도 없을 때 보일 안내(없어도 됨). 펼칠 때마다 부른다(처음엔 목록을 받고, 그다음엔 움직임만 다시 켬).
  function mount(host, emptyEl) {
    load(function (names) {
      var key = names.join('\n');
      if (!host.village || host.villageKey !== key) {
        host.village = new Village(host, names);
        host.villageKey = key;
      }
      if (emptyEl) emptyEl.hidden = names.length > 0;
      host.village.start();
    });
  }

  window.ChickxelVillage = { mount: mount, load: load };
})();
