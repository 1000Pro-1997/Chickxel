// 병아리 후원 마을 창(위 막대의 커피 단추로 열림 — 홈페이지 nav.js와 에셋 사이트 site.js가 같이 씀).
// 위에서 내려다본 픽셀 농촌 마을: 연못과 흙길, 오두막, 나무 사이로 밭이 있고, 커피를 사 주고 이름을 남긴 사람마다 밭 한 뙈기와
// 병아리(앱 마스코트) 한 마리가 산다. 병아리는 물뿌리개로 밭에 물을 주고, 이름표는 HTML 글자로 그 위에. 밭보다 사람이 많으면
// 나머지 병아리는 길을 거닌다. 후원자 목록(https://chickxel.com/api/supporters)은 브라우저에 한 시간 동안 둔다(localStorage 'chickxel.supporters').
(function () {
  var KEY = 'chickxel.supporters';
  var HOUR = 60 * 60 * 1000;
  var T = 16, COLS = 20, ROWS = 12, W = COLS * T, H = ROWS * T;

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
    // 받지 못했으면 다음에 열 때 다시 받는다
    var pending = waiting;
    pending.then(function (names) {
      if (names) done(names);
      else if (waiting === pending) waiting = null;
    });
  }

  // ---------------------------------------------------------------- 그리기 도구(낮은 해상도 캔버스, 픽셀 단위)
  var g;
  var INK = '#1b2b26';
  function px(x, y, w, h, c) { g.fillStyle = c; g.fillRect(Math.round(x), Math.round(y), w, h); }
  function sprite(x, y, rows, colors) {
    for (var r = 0; r < rows.length; r++)
      for (var c = 0; c < rows[r].length; c++) {
        var k = rows[r][c];
        if (colors[k]) px(x + c, y + r, 1, 1, colors[k]);
      }
  }
  // 테두리 있는 둥근 덩어리(나무 수관·덤불): 가운데 (cx, cy), 반지름 r
  function blob(cx, cy, r, fill, light, dark) {
    for (var y = -r - 1; y <= r + 1; y++)
      for (var x = -r - 1; x <= r + 1; x++) {
        var d = x * x + y * y;
        if (d <= r * r) px(cx + x, cy + y, 1, 1, (x + y < -r * 0.6) ? light : (x + y > r * 0.7 ? dark : fill));
        else if (d <= (r + 1) * (r + 1)) px(cx + x, cy + y, 1, 1, INK);
      }
  }
  // 위치마다 같은 값을 내는 의사 난수(타일 무늬가 프레임마다 바뀌지 않게)
  function hash(a, b) { var h = (a * 374761393 + b * 668265263) | 0; h = (h ^ (h >>> 13)) * 1274126177; return ((h ^ (h >>> 16)) >>> 0) / 4294967296; }

  // ---------------------------------------------------------------- 지도
  function isWater(c, r) { return (c <= 5 && r >= 8) || (c <= 4 && r === 7) || (c === 6 && r >= 9) || (c === 7 && r >= 10); }
  function isPath(c, r) { return (r === 5 && c >= 2) || (c === 10 && r !== 5) || (c === 2 && r >= 3 && r <= 5); }
  // 밭 자리(2×2 타일, 잘 보이는 자리 먼저)
  var PLOTS = [[12, 1], [15, 1], [12, 7], [15, 7], [6, 1], [6, 7], [18, 1], [18, 7], [12, 10], [15, 10], [18, 10]];
  var CROPS = ['carrot', 'cabbage', 'wheat', 'tomato', 'pumpkin', 'sprout'];

  function drawGround(t) {
    for (var r = 0; r < ROWS; r++)
      for (var c = 0; c < COLS; c++) {
        var x = c * T, y = r * T;
        if (isWater(c, r)) {
          px(x, y, T, T, '#2c5d71');
          // 물결(천천히 흐름)
          var s = Math.floor(t / 700 + hash(c, r) * 6) % 6;
          if (hash(c, r + 9) > 0.35) px(x + 3 + s, y + 5 + (c % 3) * 3, 4, 1, '#4b8aa1');
          if (hash(c + 3, r) > 0.6) px(x + 9 - s, y + 11, 3, 1, '#3f7a91');
          continue;
        }
        if (isPath(c, r)) {
          px(x, y, T, T, '#d8b479');
          if (hash(c, r) > 0.5) px(x + 4 + Math.floor(hash(r, c) * 8), y + 5, 2, 1, '#b88f59');
          if (hash(c, r + 4) > 0.6) px(x + 10, y + 10, 1, 1, '#a57d4b');
          // 풀과 맞닿은 가장자리
          if (!isPath(c, r - 1)) px(x, y, T, 1, '#a9844f');
          if (!isPath(c, r + 1)) px(x, y + T - 1, T, 1, '#a9844f');
          if (!isPath(c - 1, r)) px(x, y, 1, T, '#a9844f');
          if (!isPath(c + 1, r)) px(x + T - 1, y, 1, T, '#a9844f');
          continue;
        }
        // 차분한 풀색 바둑판 + 짙은 풀 무더기(부드러운 힐링 색감)
        px(x, y, T, T, (c + r) % 2 ? '#4f9148' : '#4b8b45');
        if (hash(c + 5, r + 2) > 0.7) { px(x + 2, y + 3, 9, 6, '#447f40'); px(x + 4, y + 2, 5, 8, '#447f40'); }
        for (var k = 0; k < 4; k++) {
          var hx = Math.floor(hash(c * 3 + k, r) * 14), hy = Math.floor(hash(r * 5 + k, c) * 13);
          px(x + hx, y + hy, 1, 2, '#6aac5a'); px(x + hx + 1, y + hy + 1, 1, 1, '#3b7238');
        }
        var f = hash(c + 11, r + 7);
        if (f > 0.84) { px(x + 6, y + 7, 1, 1, f > 0.93 ? '#ffe36b' : '#ffb3c1'); px(x + 5, y + 7, 1, 1, '#fff6d6'); px(x + 6, y + 8, 1, 1, '#2f5e2d'); }
      }
    // 물가: 땅과 맞닿은 물 쪽에 어두운 테두리와 그림자
    for (r = 0; r < ROWS; r++)
      for (c = 0; c < COLS; c++) {
        if (!isWater(c, r)) continue;
        x = c * T; y = r * T;
        if (!isWater(c, r - 1) && r > 0) { px(x, y, T, 3, '#214a3c'); px(x, y, T, 1, INK); }
        if (!isWater(c + 1, r) && c < COLS - 1) { px(x + T - 2, y, 2, T, '#214a3c'); px(x + T - 1, y, 1, T, INK); }
        if (!isWater(c - 1, r) && c > 0) { px(x, y, 1, T, INK); }
      }
  }

  function lily(x, y) { px(x, y, 5, 3, '#5fbf6a'); px(x + 1, y - 1, 3, 1, '#5fbf6a'); px(x + 2, y + 1, 2, 1, '#2c5d71'); px(x, y + 3, 5, 1, '#3c8d4a'); }
  function reeds(x, y, t) {
    for (var k = 0; k < 3; k++) {
      var sway = Math.sin(t / 900 + x + k) > 0.5 ? 1 : 0;
      px(x + k * 2 + sway, y - 6, 1, 6, '#4c8f3f');
      px(x + k * 2 + sway, y - 8, 1, 3, '#9b5a2e');
    }
  }
  function rock(x, y) {
    sprite(x, y, ['.kkkk..', 'kllggk.', 'klgggdk', 'kgggddk', '.kkkkk.'], { k: INK, l: '#b7bcc4', g: '#8e949e', d: '#6c717b' });
  }
  function tree(x, y) {
    px(x - 2, y - 2, 5, 6, INK); px(x - 1, y - 2, 3, 6, '#7a4c2a'); px(x - 1, y + 3, 3, 1, '#5a3820');
    px(x - 6, y + 3, 12, 2, 'rgba(20,40,30,0.25)');
    blob(x, y - 9, 8, '#3f8f45', '#63b45b', '#2f6f37');
  }
  function bush(x, y) { blob(x, y, 4, '#479b4a', '#6cc061', '#357a39'); }

  // 오두막(3×3 타일): 빨간 지붕, 크림색 벽, 문과 창, 굴뚝 연기
  function cottage(x, y, t) {
    px(x + 1, y + 37, 44, 3, 'rgba(20,40,30,0.3)');
    px(x + 3, y + 18, 40, 20, INK); px(x + 4, y + 19, 38, 18, '#ecd8ab'); px(x + 4, y + 34, 38, 3, '#d4bb88');
    px(x + 19, y + 25, 8, 12, INK); px(x + 20, y + 26, 6, 11, '#8a5530'); px(x + 24, y + 31, 1, 1, '#ffd23f');
    [[8, 23], [31, 23]].forEach(function (w) { px(x + w[0], y + w[1], 8, 7, INK); px(x + w[0] + 1, y + w[1] + 1, 6, 5, '#8fd3ff'); px(x + w[0] + 1, y + w[1] + 1, 2, 2, '#d8f1ff'); px(x + w[0] + 4, y + w[1] + 1, 1, 5, INK); });
    // 지붕: 처마가 벽보다 넓은 사다리꼴, 줄무늬 기와
    for (var r = 0; r < 20; r++) {
      var inset = Math.floor((19 - r) / 3);
      px(x + inset, y + r, 46 - inset * 2, 1, r === 0 ? INK : r % 4 === 0 ? '#9f3434' : '#c8453f');
      px(x + inset, y + r, 1, 1, INK); px(x + 45 - inset, y + r, 1, 1, INK);
    }
    px(x, y + 19, 46, 1, INK); px(x + 2, y + 18, 42, 1, '#e86a5a');
    px(x + 33, y - 6, 6, 9, INK); px(x + 34, y - 5, 4, 8, '#9a8f86');
    var s = Math.floor(t / 500) % 4;
    px(x + 35 + (s % 2), y - 9 - s, 2, 2, 'rgba(255,255,255,0.8)'); px(x + 34 - (s % 2), y - 13 - s, 3, 2, 'rgba(255,255,255,0.55)');
  }
  function well(x, y) {
    px(x, y + 6, 14, 9, INK); px(x + 1, y + 7, 12, 7, '#9aa2ad'); px(x + 3, y + 8, 8, 4, '#2c5d71'); px(x + 1, y + 12, 12, 2, '#7d8590');
    px(x + 1, y - 4, 1, 11, INK); px(x + 12, y - 4, 1, 11, INK);
    px(x - 1, y - 7, 16, 4, INK); px(x, y - 6, 14, 2, '#c8453f');
  }
  function bench(x, y) {
    px(x, y, 22, 7, INK); px(x + 1, y + 1, 20, 2, '#b07a45'); px(x + 1, y + 4, 20, 2, '#9a6538');
    px(x + 2, y + 7, 2, 3, INK); px(x + 18, y + 7, 2, 3, INK);
  }
  function fenceRow(x, y, w) {
    for (var k = 0; k <= w; k += 6) { px(x + k, y - 6, 3, 8, INK); px(x + k + 1, y - 5, 1, 6, '#c9935a'); }
    px(x, y - 4, w + 3, 2, INK); px(x + 1, y - 4, w + 1, 1, '#d9a56a');
  }
  // 연못으로 뻗은 나무 선착장(세로 판자)
  function dock(x, y) {
    px(x - 1, y - 1, 16, 30, INK);
    for (var k = 0; k < 7; k++) { px(x, y + k * 4, 14, 3, k % 2 ? '#b07a45' : '#c18a52'); px(x + 4 + (k % 3) * 3, y + k * 4 + 1, 1, 1, '#7a4c2a'); }
    px(x - 2, y + 26, 3, 5, INK); px(x + 13, y + 26, 3, 5, INK);
  }
  function lilyFlower(x, y) { lily(x, y); px(x + 1, y - 2, 3, 2, '#ffb3c1'); px(x + 2, y - 3, 1, 1, '#fff'); }
  function crate(x, y) {
    px(x, y, 10, 9, INK); px(x + 1, y + 1, 8, 7, '#c18a52'); px(x + 1, y + 4, 8, 1, '#8a5a2b'); px(x + 4, y + 1, 1, 7, '#8a5a2b');
  }
  function barrel(x, y) {
    px(x, y, 9, 11, INK); px(x + 1, y + 1, 7, 9, '#a9703f'); px(x + 1, y + 3, 7, 1, '#6b4428'); px(x + 1, y + 7, 7, 1, '#6b4428'); px(x + 2, y + 1, 1, 9, '#c58a55');
  }
  function hay(x, y) {
    px(x, y, 14, 10, INK); px(x + 1, y + 1, 12, 8, '#e2bd5a'); px(x + 1, y + 6, 12, 2, '#c99b33');
    px(x + 3, y + 2, 1, 4, '#f3d98b'); px(x + 8, y + 3, 1, 3, '#f3d98b');
  }
  function mailbox(x, y) {
    px(x + 3, y + 4, 2, 8, INK); px(x, y, 8, 6, INK); px(x + 1, y + 1, 6, 4, '#5b8fd1'); px(x + 7, y - 2, 1, 3, '#ff4b3a');
  }
  function sign(x, y) {
    px(x + 5, y + 5, 2, 8, INK); px(x, y, 12, 7, INK); px(x + 1, y + 1, 10, 5, '#d9a56a'); px(x + 3, y + 3, 6, 1, '#8a5a2b');
  }
  // 화단(오두막 앞): 흙 띠에 작은 꽃
  function flowerBed(x, y) {
    px(x, y, 38, 6, INK); px(x + 1, y + 1, 36, 4, '#7a4c2a');
    var colors = ['#ff9fb0', '#ffe36b', '#c78bff', '#ffffff', '#ff8a5b'];
    for (var k = 0; k < 9; k++) { px(x + 2 + k * 4, y + 1, 2, 2, colors[k % colors.length]); px(x + 3 + k * 4, y + 3, 1, 1, '#3f8f45'); }
  }
  // 나비(연못과 밭 사이를 팔랑팔랑)
  function butterfly(x, y, t, c) {
    var open = Math.floor(t / 150) % 2;
    px(x, y, 1, 2, INK);
    if (open) { px(x - 2, y - 1, 2, 2, c); px(x + 1, y - 1, 2, 2, c); } else { px(x - 1, y, 1, 2, c); px(x + 1, y, 1, 2, c); }
  }

  // 밭(2×2 타일, 32×32): 갈아 둔 흙과 작물 3×4포기. 물을 준 밭은 흙이 짙다.
  function plot(x, y, crop, wet, t) {
    px(x, y, 32, 32, INK);
    px(x + 1, y + 1, 30, 30, wet ? '#6e452c' : '#8a5a3a');
    for (var r = 0; r < 4; r++) px(x + 1, y + 4 + r * 8, 30, 1, wet ? '#563521' : '#6e452c');
    for (r = 0; r < 3; r++)
      for (var c = 0; c < 4; c++) {
        var cx = x + 5 + c * 7, cy = y + 7 + r * 9;
        var sway = Math.sin(t / 700 + c + r * 2) > 0.6 ? 1 : 0;
        if (crop === 'carrot') { px(cx, cy - 3, 1, 4, '#5fbf4a'); px(cx + 2, cy - 3, 1, 4, '#5fbf4a'); px(cx + 1, cy - 4 + sway, 1, 4, '#7fd85f'); px(cx, cy + 1, 3, 2, '#ff8a2a'); }
        else if (crop === 'cabbage') { px(cx - 1, cy - 2, 5, 4, '#2f6f37'); px(cx, cy - 2, 3, 3, '#8ed36c'); px(cx + 1, cy - 1, 1, 1, '#c6f0a6'); }
        else if (crop === 'wheat') { px(cx + sway, cy - 5, 1, 7, '#c99b33'); px(cx + 2, cy - 4, 1, 6, '#c99b33'); px(cx + sway - 1, cy - 6, 2, 2, '#ffd65c'); px(cx + 2, cy - 5, 2, 2, '#ffd65c'); }
        else if (crop === 'tomato') { px(cx, cy - 4, 3, 6, '#3f8f45'); px(cx - 1, cy - 2, 1, 1, '#ff4b3a'); px(cx + 3, cy, 1, 2, '#ff4b3a'); px(cx + 1, cy - 1, 1, 1, '#ff6b5a'); }
        else if (crop === 'pumpkin') { if ((r + c) % 2 === 0) { px(cx - 1, cy - 1, 5, 4, INK); px(cx, cy, 3, 2, '#ff9a2e'); px(cx + 1, cy - 2, 1, 1, '#3f8f45'); } else px(cx, cy - 1, 2, 2, '#5fbf4a'); }
        else { px(cx, cy - 1 - sway, 1, 2, '#7fd85f'); px(cx + 1, cy - 2, 1, 1, '#7fd85f'); }
      }
  }

  // 마스코트 병아리(앱 아이콘의 그 병아리)와 물뿌리개
  var CHICK = ['...oo...', '..oooo..', '.oyyyyo.', 'oyeyyeyo', 'opybbypo', 'oyyyyyyo', '.oyyyyo.', '..f..f..'];
  var CHICK_BLINK = CHICK.slice(); CHICK_BLINK[3] = 'oyoyyoyo';
  var CHICK_COLORS = { o: '#6b4410', y: '#ffd23f', e: '#2b1d0e', b: '#ff8a2a', p: '#ff9fb0', f: '#ff8a2a' };
  var CAN = ['..kkk...', '.kbbbkk.', 'kbbbbbbk', 'kbbbbbk.', '.kkkkk..'];
  function chick(x, y, blink) {
    px(x + 1, y + 8, 6, 1, 'rgba(20,40,30,0.3)');
    sprite(x, y, blink ? CHICK_BLINK : CHICK, CHICK_COLORS);
  }

  // ---------------------------------------------------------------- 한 마을(캔버스 + 이름표)
  function Village(host, names) {
    var canvas = document.createElement('canvas');
    canvas.width = W; canvas.height = H;
    canvas.className = 'village-canvas';
    canvas.setAttribute('role', 'img');
    canvas.setAttribute('aria-label', names.join(', '));
    host.textContent = '';
    host.appendChild(canvas);
    var ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = false;
    var scale = 1;

    var residents = names.map(function (name, k) {
      var label = document.createElement('span');
      label.className = 'village-name';
      label.textContent = name;
      host.appendChild(label);
      return { label: label, plot: PLOTS[k] || null, crop: CROPS[k % CROPS.length], phase: k * 1.9 };
    });

    function place(label, x, y) {
      label.style.left = x * scale + 'px';
      label.style.top = y * scale + 'px';
    }

    function frame(t) {
      g = ctx;
      drawGround(t);
      // 연못: 나무 선착장, 연잎(꽃 핀 것도), 갈대, 돌, 징검돌
      dock(44, 110);
      lily(18, 150); lily(52, 176); lily(30, 130); lily(72, 186); lilyFlower(82, 158); lilyFlower(12, 172);
      reeds(78, 128, t); reeds(86, 146, t); reeds(100, 166, t); reeds(4, 118, t); reeds(66, 120, t);
      rock(60, 160); rock(92, 132); rock(108, 180);
      cottage(2, 2, t);
      flowerBed(4, 44);
      crate(52, 34); crate(58, 40); barrel(50, 44);
      mailbox(40, 52);
      well(60, 60);
      tree(140, 24); tree(132, 60); tree(290, 72); tree(212, 72); tree(10, 80); tree(146, 176); tree(306, 146);
      bush(120, 70); bush(306, 40); bush(196, 138); bush(250, 142); bush(116, 140); bush(176, 178);
      bench(178, 66);
      fenceRow(98, 12, 34); fenceRow(98, 108, 34);
      hay(270, 178); hay(286, 172);
      sign(120, 100);

      var items = [];
      residents.forEach(function (r) {
        var bob = Math.sin(t / 300 + r.phase) > 0.85 ? 1 : 0;
        var blink = Math.sin(t / 1100 + r.phase * 3) > 0.985;
        if (r.plot) {
          var x = r.plot[0] * T, y = r.plot[1] * T;
          var watering = Math.sin(t / 1600 + r.phase) > 0.2;
          plot(x, y, r.crop, true, t);
          items.push({ y: y + 32, draw: function () {
            var cx = x + 30, cy = y + 22;
            chick(cx, cy - bob, blink);
            // 물뿌리개를 기울이면 물방울이 밭으로 떨어짐
            sprite(cx - 7, cy + 2 - (watering ? 1 : 0), CAN, { k: INK, b: '#7fb8d8' });
            if (watering) {
              var d = Math.floor(t / 120) % 4;
              px(cx - 9, cy + 3 + d, 1, 1, '#bfe6ff'); px(cx - 11, cy + 5 + ((d + 2) % 4), 1, 1, '#bfe6ff');
            }
            place(r.label, cx + 4, cy - 2 - bob);
          } });
        } else {
          // 밭이 모자라면 큰길(가로)을 천천히 오감
          var span = 13 * T, s = ((t / 40 + r.phase * 200) % (span * 2));
          var wx = 3 * T + (s < span ? s : span * 2 - s), wy = 5 * T + 4;
          items.push({ y: wy + 8, draw: function () {
            chick(wx, wy - (Math.floor(t / 200) % 2), blink);
            place(r.label, wx + 4, wy - 2);
          } });
        }
      });
      items.sort(function (a, b) { return a.y - b.y; });
      items.forEach(function (it) { it.draw(); });
      // 나비 세 마리
      [[90, 90, '#ffe36b', 0], [230, 120, '#ffb3c1', 2], [170, 30, '#ffffff', 4]].forEach(function (b) {
        var a = t / 2200 + b[3];
        butterfly(b[0] + Math.sin(a) * 26, b[1] + Math.sin(a * 2.3) * 10, t + b[3] * 100, b[2]);
      });
    }

    var running = false;
    function loop(t) {
      if (!host.isConnected || host.offsetParent === null) { running = false; return; }
      frame(t);
      requestAnimationFrame(loop);
    }
    this.resize = function (s) {
      scale = s;
      canvas.style.width = W * s + 'px';
      canvas.style.height = H * s + 'px';
      host.style.width = W * s + 'px';
      host.style.height = H * s + 'px';
      // 창 너비를 마을 그림에 맞춘다(아래 감사 글이 그 너비로 줄바꿈)
      var box = host.closest('.village-dialog');
      if (box) box.style.width = W * s + 40 + 'px';
    };
    this.start = function () {
      if (running) return;
      if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) { frame(0); return; }
      running = true;
      requestAnimationFrame(loop);
    };
  }

  // ---------------------------------------------------------------- 창
  var dialog = null;
  // 화면에 맞는 정수 배율(2~4)
  function fitScale() {
    var byW = Math.floor((window.innerWidth - 64) / W), byH = Math.floor((window.innerHeight - 230) / H);
    return Math.max(1, Math.min(4, byW, byH));
  }

  // text: { title, thanks, buy, empty, close } — 부르는 쪽이 화면 언어로 준다.
  function open(text) {
    if (!dialog) dialog = build(text);
    document.body.appendChild(dialog.root);
    dialog.root.hidden = false;
    document.documentElement.classList.add('village-open');
    var resize = function () { if (dialog.village) dialog.village.resize(fitScale()); };
    window.addEventListener('resize', resize);
    dialog.cleanup = function () { window.removeEventListener('resize', resize); };
    dialog.close.focus();
    load(function (names) {
      var key = names.join('\n');
      if (!dialog.village || dialog.key !== key) {
        dialog.village = new Village(dialog.map, names);
        dialog.key = key;
      }
      dialog.empty.hidden = names.length > 0;
      dialog.village.resize(fitScale());
      dialog.village.start();
    });
  }

  function closeDialog() {
    if (!dialog || dialog.root.hidden) return;
    dialog.root.hidden = true;
    document.documentElement.classList.remove('village-open');
    if (dialog.cleanup) dialog.cleanup();
    if (dialog.opener) dialog.opener.focus();
  }

  // 창 모양(홈페이지와 에셋 사이트가 같이 쓰도록 여기 한곳에)
  var CSS = [
    '.village-backdrop{position:fixed;inset:0;z-index:100;display:grid;place-items:center;padding:16px;background:rgba(8,10,14,.72);backdrop-filter:blur(3px)}',
    '.village-backdrop[hidden]{display:none}',
    'html.village-open{overflow:hidden}',
    '.village-dialog{max-width:100%;max-height:100%;overflow:auto;padding:16px 18px 18px;background:#15161b;border:1px solid #2c2e36;border-radius:18px;box-shadow:0 24px 60px rgba(0,0,0,.5);color:#e9eaee}',
    '.village-head{display:flex;align-items:center;gap:12px;margin:0 0 12px}',
    '.village-head h2{flex:1;margin:0;font-size:18px;color:#fff}',
    '.village-close{display:inline-grid;place-items:center;width:34px;height:34px;padding:0;border-radius:50%;border:1px solid #2c2e36;background:#1d1f25;color:#c6c9d2;cursor:pointer}',
    '.village-close:hover{background:#262930}',
    '.village-close .icon{width:18px;height:18px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round}',
    '.village-stage{position:relative;border-radius:10px;overflow:hidden;border:2px solid #1b2b26}',
    '.chick-village{position:relative}',
    '.village-canvas{display:block;image-rendering:pixelated}',
    '.village-name{position:absolute;transform:translate(-50%,-100%);padding:1px 8px;border-radius:999px;background:rgba(27,43,38,.82);border:1px solid rgba(255,255,255,.18);color:#fff;font-size:12px;font-weight:700;line-height:1.5;white-space:nowrap;pointer-events:none}',
    '.village-empty{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);margin:0;padding:8px 14px;border-radius:999px;background:rgba(27,43,38,.85);color:#fff;font-size:14px;font-weight:700;white-space:nowrap}',
    '.village-empty[hidden]{display:none}',
    '.village-foot{display:flex;align-items:center;gap:16px;margin-top:14px}',
    '.village-foot p{flex:1;margin:0;color:#9599a6;font-size:13.5px;line-height:1.55}',
    '.village-foot .btn{flex:none;display:inline-flex;align-items:center;gap:8px;padding:11px 18px;border-radius:999px;background:#ff813f;border:1px solid #ff813f;color:#fff;font-weight:700;font-size:14.5px;text-decoration:none;white-space:nowrap}',
    '.village-foot .btn:hover{background:#e96f2f;border-color:#e96f2f}',
    '.village-foot .btn .icon{width:18px;height:18px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round}',
    '@media (max-width:640px){.village-foot{flex-direction:column;align-items:stretch}.village-foot .btn{justify-content:center}}'
  ].join('\n');

  function build(text) {
    function el(tag, cls, content) {
      var e = document.createElement(tag);
      if (cls) e.className = cls;
      if (content) e.textContent = content;
      return e;
    }
    if (!document.getElementById('village-style')) {
      var style = el('style');
      style.id = 'village-style';
      style.textContent = CSS;
      document.head.appendChild(style);
    }
    var root = el('div', 'village-backdrop');
    root.hidden = true;
    var box = el('div', 'village-dialog');
    box.setAttribute('role', 'dialog');
    box.setAttribute('aria-modal', 'true');
    box.setAttribute('aria-label', text.title);
    var head = el('div', 'village-head');
    head.appendChild(el('h2', null, text.title));
    var close = el('button', 'village-close');
    close.type = 'button';
    close.title = text.close;
    close.setAttribute('aria-label', text.close);
    close.innerHTML = '<svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>';
    head.appendChild(close);
    var map = el('div', 'chick-village');
    var empty = el('p', 'village-empty', text.empty);
    empty.hidden = true;
    var foot = el('div', 'village-foot');
    foot.appendChild(el('p', null, text.thanks));
    var buy = el('a', 'btn support');
    buy.href = 'https://buymeacoffee.com/chickxel';
    buy.target = '_blank';
    buy.rel = 'noopener';
    buy.innerHTML = '<svg class="icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M10 2v2"/><path d="M14 2v2"/><path d="M16 8a1 1 0 0 1 1 1v8a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4V9a1 1 0 0 1 1-1h14a4 4 0 1 1 0 8h-1"/><path d="M6 2v2"/></svg>';
    buy.appendChild(el('span', null, text.buy));
    foot.appendChild(buy);
    var stage = el('div', 'village-stage');
    stage.appendChild(map);
    stage.appendChild(empty);
    box.appendChild(head);
    box.appendChild(stage);
    box.appendChild(foot);
    root.appendChild(box);
    close.addEventListener('click', closeDialog);
    root.addEventListener('click', function (e) { if (e.target === root) closeDialog(); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeDialog(); });
    return { root: root, map: map, empty: empty, close: close, village: null, key: null, cleanup: null, opener: null };
  }

  window.ChickxelVillage = {
    load: load,
    open: function (text, opener) {
      open(text);
      dialog.opener = opener || null;
    }
  };
})();
