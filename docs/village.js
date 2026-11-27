// 병아리 후원 마을(village.html — 위 막대의 커피 단추가 새 창으로 연다).
// 위에서 내려다본 픽셀 농촌 마을이 가운데 있고, 그 둘레는 아직 개척하지 않은 숲(안개가 낄수록 멀다). 커피를 사 주고 이름을 남긴
// 사람마다 밭 한 뙈기와 병아리(앱 마스코트) 한 마리가 산다. 병아리는 밭 앞을 오가며 물뿌리개로 물을 주고, 밭보다 사람이 많으면
// 나머지는 큰길을 거닌다. 끌어서 옮기고 휠·두 손가락·단추로 확대하며, 오른쪽 위 주민 이름을 누르면 그 병아리로 다가가 따라간다.
// 후원자 목록은 nav.js의 ChickxelSupporters.load(한 시간 동안 브라우저에 둠).
(function () {
  var T = 16, COLS = 20, ROWS = 12, W = COLS * T, H = ROWS * T;
  // 온 세상(타일)과 그 안의 마을 자리
  var WC = 56, WR = 36, VC = 18, VR = 12, WW = WC * T, WH = WR * T, VX = VC * T, VY = VR * T;

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

  // ---------------------------------------------------------------- 마을(살아 움직이는 부분, 320×192)
  function drawVillage(t, residents) {
    drawGround(t);
    // 연못: 나무 선착장, 연잎(꽃 핀 것도), 갈대, 돌
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
      var p = residentPos(r, t);
      if (r.plot) plot(r.plot[0] * T, r.plot[1] * T, r.crop, true, t);
      items.push({ y: p.y + 8, draw: function () {
        chick(p.x, p.y - p.bob, p.blink);
        if (p.watering) {
          // 물뿌리개를 밭 쪽(위)으로 들고, 물방울이 밭 가장자리로 떨어짐
          sprite(p.x - 6, p.y - 5, CAN, { k: INK, b: '#7fb8d8' });
          var d = Math.floor(t / 120) % 4;
          px(p.x - 7, p.y - 6 + d, 1, 1, '#bfe6ff'); px(p.x - 9, p.y - 7 + ((d + 2) % 4), 1, 1, '#bfe6ff');
        }
      } });
    });
    items.sort(function (a, b) { return a.y - b.y; });
    items.forEach(function (it) { it.draw(); });
    // 나비 세 마리
    [[90, 90, '#ffe36b', 0], [230, 120, '#ffb3c1', 2], [170, 30, '#ffffff', 4]].forEach(function (b) {
      var a = t / 2200 + b[3];
      butterfly(b[0] + Math.sin(a) * 26, b[1] + Math.sin(a * 2.3) * 10, t + b[3] * 100, b[2]);
    });
  }

  // 주민 병아리의 자리(마을 안 픽셀): 밭 아래 가장자리를 천천히 오가다 멈춰서 물을 준다. 밭이 없으면 큰길을 오간다.
  function residentPos(r, t) {
    var bob = Math.sin(t / 300 + r.phase) > 0.85 ? 1 : 0;
    var blink = Math.sin(t / 1100 + r.phase * 3) > 0.985;
    if (r.plot) {
      var x0 = r.plot[0] * T + 2, x1 = r.plot[0] * T + 24, y = r.plot[1] * T + 33;
      var s = (t / 1000 + r.phase * 3) % 14, x, watering = false;
      // 걸음은 천천히 출발해 천천히 멈춘다(병아리도, 따라가는 카메라도 덜 덜컹이게)
      var ease = function (f) { return f * f * (3 - 2 * f); };
      if (s < 4) x = x0 + (x1 - x0) * ease(s / 4);
      else if (s < 7) { x = x1; watering = true; }
      else if (s < 11) x = x1 - (x1 - x0) * ease((s - 7) / 4);
      else { x = x0; watering = s > 12; }
      var walking = !watering && (s < 4 || (s >= 7 && s < 11));
      return { x: x, y: y - (walking && Math.floor(t / 180) % 2 ? 1 : 0), ground: y, bob: walking ? 0 : bob, blink: blink, watering: watering };
    }
    var span = 13 * T, w = (t / 40 + r.phase * 200) % (span * 2), gy = 5 * T + 4;
    return { x: 3 * T + (w < span ? w : span * 2 - w), y: gy - (Math.floor(t / 200) % 2), ground: gy, bob: 0, blink: blink, watering: false };
  }

  // ---------------------------------------------------------------- 개척하지 않은 숲(한 번만 그려 둠)
  function smooth(x, y, cell, seed) {
    var gx = Math.floor(x / cell), gy = Math.floor(y / cell), fx = x / cell - gx, fy = y / cell - gy;
    function v(a, b) { return hash(a * 7 + seed, b * 13 + seed * 3); }
    var sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
    var top = v(gx, gy) + (v(gx + 1, gy) - v(gx, gy)) * sx, bottom = v(gx, gy + 1) + (v(gx + 1, gy + 1) - v(gx, gy + 1)) * sx;
    return top + (bottom - top) * sy;
  }
  // 마을 가장자리에서 (c, r) 타일까지의 거리(타일, 마을 안이면 0)
  function distance(c, r) {
    var dx = Math.max(VC - c, 0, c - (VC + COLS - 1)), dy = Math.max(VR - r, 0, r - (VR + ROWS - 1));
    return Math.sqrt(dx * dx + dy * dy);
  }
  // 마을 밖 물: 마을 연못이 왼쪽 아래로 이어진 호수
  function wildWater(c, r) {
    var dx = (c - (VC - 1)) / 7.5, dy = (r - (VR + 13)) / 4.5;
    return dx * dx + dy * dy + (smooth(c, r, 3, 5) - 0.5) * 0.6 < 1;
  }
  // 마을 길을 숲 쪽으로 조금 잇고, 멀어질수록 풀에 덮인다(값 = 마을에서 몇 칸째, 길이 아니면 -1)
  function wildPath(c, r) {
    if (r === VR + 5 && c >= VC + COLS && c < VC + COLS + 5) return c - (VC + COLS);
    if (c === VC + 10 && r < VR && r >= VR - 5) return VR - 1 - r;
    if (c === VC + 10 && r >= VR + ROWS && r < VR + ROWS + 5) return r - (VR + ROWS);
    return -1;
  }

  function drawWild() {
    var trees = [];
    for (var r = 0; r < WR; r++)
      for (var c = 0; c < WC; c++) {
        if (c >= VC && c < VC + COLS && r >= VR && r < VR + ROWS) continue;
        var x = c * T, y = r * T, d = distance(c, r);
        if (wildWater(c, r)) {
          px(x, y, T, T, '#2c5d71');
          if (hash(c, r + 9) > 0.5) px(x + 3 + Math.floor(hash(c, r) * 6), y + 6, 4, 1, '#3f7a91');
          // 물가 테두리: 위가 땅일 때만(마을 연못과 이어지는 곳은 그대로 물)
          var inVillageAbove = c >= VC && c < VC + COLS && r - 1 >= VR && r - 1 < VR + ROWS;
          var waterAbove = inVillageAbove ? isWater(c - VC, r - 1 - VR) : wildWater(c, r - 1);
          if (!waterAbove) { px(x, y, T, 3, '#214a3c'); px(x, y, T, 1, INK); }
          continue;
        }
        // 마을 둘레 한두 칸은 마을 풀색, 그 밖은 짙고 거친 들풀
        var near = d < 1.5;
        px(x, y, T, T, near ? ((c + r) % 2 ? '#4b8b45' : '#4f9148') : ((c + r) % 2 ? '#3d7339' : '#3a6e37'));
        for (var k = 0; k < (near ? 4 : 7); k++) {
          var hx = Math.floor(hash(c * 3 + k, r) * 14), hy = Math.floor(hash(r * 5 + k, c) * 12);
          px(x + hx, y + hy, 1, 3, near ? '#6aac5a' : '#5b9a4f'); px(x + hx + 1, y + hy + 1, 1, 2, '#2f5e2d');
        }
        var p = wildPath(c, r);
        if (p >= 0) {
          // 덮인 정도: 멀수록 흙이 듬성듬성
          for (var b = 0; b < 16; b++) {
            var bx = (b % 4) * 4, by = Math.floor(b / 4) * 4;
            if (hash(c * 4 + b, r) > p * 0.22) px(x + bx, y + by, 4, 4, (b + r) % 2 ? '#c9a46b' : '#bf9a62');
          }
          continue;
        }
        var f = hash(c + 11, r + 7);
        if (f > 0.9) px(x + 6, y + 7, 1, 1, f > 0.95 ? '#e8d36b' : '#d79fb0');
        if (f < 0.05) rock(x + 4, y + 6);
        if (f > 0.5 && f < 0.53) { px(x + 5, y + 9, 3, 2, '#e14b3c'); px(x + 6, y + 11, 1, 2, '#e8dccb'); }
        // 숲: 마을에서 멀수록, 무늬 값이 클수록 나무가 빽빽
        if (d >= 2 && smooth(c, r, 4, 1) + Math.min(d, 8) * 0.04 > 0.62 && hash(c + 3, r + 5) > 0.35) trees.push([x + 6 + Math.floor(hash(c, r + 1) * 4), y + 10]);
        else if (d >= 2 && hash(c + 7, r + 1) > 0.86) trees.push([x + 8, y + 12, true]);
      }
    trees.sort(function (a, b) { return a[1] - b[1]; });
    trees.forEach(function (tr) {
      if (tr[2]) bush(tr[0], tr[1]);
      else { px(tr[0] - 2, tr[1] - 2, 5, 6, INK); px(tr[0] - 1, tr[1] - 2, 3, 6, '#5c3a22'); blob(tr[0], tr[1] - 9, 8, '#2f6f37', '#4b9547', '#245a2c'); }
    });
    // 개척 예정 말뚝: 마을 길 끝마다
    sign((VC + COLS + 4) * T + 2, (VR + 5) * T - 4);
    sign((VC + 10) * T + 18, (VR - 5) * T + 2);
    sign((VC + 10) * T + 18, (VR + ROWS + 4) * T + 2);
    // 안개: 마을에서 멀수록 짙게, 4픽셀 덩어리로 살짝 얼룩지게
    for (var fy = 0; fy < WH; fy += 4)
      for (var fx = 0; fx < WW; fx += 4) {
        var dd = distance(fx / T, fy / T);
        if (dd < 1.5) continue;
        var a = Math.pow(Math.min(1, (dd - 1.5) / 9), 0.8) * 0.78 + (hash(fx, fy) - 0.5) * 0.06;
        px(fx, fy, 4, 4, 'rgba(16,26,34,' + Math.max(0, a).toFixed(3) + ')');
      }
  }

  // ---------------------------------------------------------------- 화면(카메라 · 확대 · 따라가기)
  var screen = document.getElementById('world');
  if (!screen) return;
  var namesHost = document.getElementById('world-names');
  var sctx = screen.getContext('2d');
  var world = document.createElement('canvas');
  world.width = WW; world.height = WH;
  g = world.getContext('2d');
  drawWild();
  // 마을 자리에도 바닥을 깔아 둔다(확대 배율이 정수가 아닐 때 마을 그림 가장자리 틈으로 빈칸이 보이지 않게)
  g.save(); g.translate(VX, VY); drawGround(0); g.restore();
  var live = document.createElement('canvas');
  live.width = W; live.height = H;
  var lctx = live.getContext('2d');

  var residents = [];
  var cam = { x: VX + W / 2, y: VY + H / 2, z: 3 }, goal = null, follow = -1;
  var vw = 0, vh = 0, dpr = 1;

  function minZoom() { return Math.max(vw / WW, vh / WH); }
  // 마을 전체 보기: 넓은 화면은 양옆 소개·주민 칸을 비켜서
  function fitZoom() {
    var side = vw > 900 ? 640 : 40;
    return Math.min(8, Math.max(minZoom(), Math.min((vw - side) / W, (vh - 80) / H)));
  }
  function clamp() {
    cam.z = Math.min(8, Math.max(minZoom(), cam.z));
    var hw = vw / 2 / cam.z, hh = vh / 2 / cam.z;
    cam.x = Math.min(WW - hw, Math.max(hw, cam.x));
    cam.y = Math.min(WH - hh, Math.max(hh, cam.y));
  }
  // 캔버스는 위 막대 아래 칸(CSS) — 그 크기와 화면 안 위치로 맞춘다
  var offX = 0, offY = 0;
  function resize() {
    dpr = window.devicePixelRatio || 1;
    var rect = screen.getBoundingClientRect();
    vw = rect.width; vh = rect.height; offX = rect.left; offY = rect.top;
    screen.width = Math.round(vw * dpr); screen.height = Math.round(vh * dpr);
    clamp();
  }
  window.addEventListener('resize', resize);
  resize();
  // 전체 화면: 위 막대·페이지는 빼고 마을 칸(#stage)만 화면 가득. 지원하지 않는 브라우저(아이폰 등)는 단추를 숨긴다
  var stage = document.getElementById('stage');
  var fullBtn = document.getElementById('zoom-full');
  var canFull = !!(stage.requestFullscreen || stage.webkitRequestFullscreen);
  fullBtn.hidden = !canFull;
  function fullElement() { return document.fullscreenElement || document.webkitFullscreenElement || null; }
  fullBtn.addEventListener('click', function () {
    if (fullElement()) (document.exitFullscreen || document.webkitExitFullscreen).call(document);
    else (stage.requestFullscreen || stage.webkitRequestFullscreen).call(stage);
  });
  function onFull() {
    fullBtn.setAttribute('aria-pressed', String(fullElement() === stage));
    resize();
  }
  document.addEventListener('fullscreenchange', onFull);
  document.addEventListener('webkitfullscreenchange', onFull);
  cam.z = fitZoom(); clamp();

  function fit() { follow = -1; goal = { x: VX + W / 2, y: VY + H / 2, z: fitZoom() }; markActive(); }
  function zoomAt(factor, sx, sy) {
    var wx = cam.x + (sx - vw / 2) / cam.z, wy = cam.y + (sy - vh / 2) / cam.z;
    cam.z = Math.min(8, Math.max(minZoom(), cam.z * factor));
    if (follow < 0) { cam.x = wx - (sx - vw / 2) / cam.z; cam.y = wy - (sy - vh / 2) / cam.z; }
    goal = null;
    clamp();
  }

  // 끌기 · 두 손가락 확대
  var pointers = new Map(), pinch = 0;
  screen.addEventListener('pointerdown', function (e) {
    screen.setPointerCapture(e.pointerId);
    pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
    if (pointers.size === 2) { var p = Array.from(pointers.values()); pinch = Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y); }
    screen.classList.add('dragging');
  });
  screen.addEventListener('pointermove', function (e) {
    var last = pointers.get(e.pointerId);
    if (!last) return;
    if (pointers.size === 1 && Math.hypot(e.clientX - last.x, e.clientY - last.y) > 0) {
      follow = -1; goal = null; markActive();
      cam.x -= (e.clientX - last.x) / cam.z; cam.y -= (e.clientY - last.y) / cam.z;
      clamp();
    }
    pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
    if (pointers.size === 2) {
      var p = Array.from(pointers.values()), d = Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y);
      if (pinch > 0) zoomAt(d / pinch, (p[0].x + p[1].x) / 2 - offX, (p[0].y + p[1].y) / 2 - offY);
      pinch = d;
    }
  });
  function release(e) { pointers.delete(e.pointerId); pinch = 0; if (pointers.size === 0) screen.classList.remove('dragging'); }
  screen.addEventListener('pointerup', release);
  screen.addEventListener('pointercancel', release);
  screen.addEventListener('wheel', function (e) {
    e.preventDefault();
    zoomAt(Math.pow(1.0015, -e.deltaY), e.clientX - offX, e.clientY - offY);
  }, { passive: false });
  document.getElementById('zoom-in').addEventListener('click', function () { zoomAt(1.4, vw / 2, vh / 2); });
  document.getElementById('zoom-out').addEventListener('click', function () { zoomAt(1 / 1.4, vw / 2, vh / 2); });
  document.getElementById('zoom-fit').addEventListener('click', fit);

  // 주민 목록(접기·펴기, 이름 찾기): 누르면 그 병아리로 다가가 따라간다(다시 누르면 마을 전체)
  var list = document.getElementById('resident-list');
  var search = document.getElementById('resident-search');
  var panel = document.getElementById('residents');
  var toggle = document.getElementById('residents-toggle');
  var body = document.getElementById('residents-body');
  // 기본은 접힘 — 펼쳐 두면 그다음에도 펼친 채로
  function setFolded(folded) {
    body.hidden = folded;
    panel.classList.toggle('folded', folded);
    toggle.setAttribute('aria-expanded', String(!folded));
    try { localStorage.setItem('chickxel.village.open', folded ? '0' : '1'); } catch (e) { /* 기억 못 해도 됨 */ }
  }
  toggle.addEventListener('click', function () { setFolded(!body.hidden); });
  try { if (localStorage.getItem('chickxel.village.open') === '1') setFolded(false); } catch (e) { /* 기본은 접힘 */ }
  function filter() {
    var q = search.value.trim().toLowerCase(), shown = 0;
    [].forEach.call(list.children, function (li, i) {
      var hit = !q || residents[i].name.toLowerCase().indexOf(q) >= 0;
      li.hidden = !hit;
      if (hit) shown++;
    });
    document.getElementById('residents-none').hidden = shown > 0 || residents.length === 0;
  }
  search.addEventListener('input', filter);
  // 찾기 칸에서 Enter: 처음 맞는 주민을 따라간다
  search.addEventListener('keydown', function (e) {
    if (e.key !== 'Enter') return;
    var first = [].find.call(list.children, function (li) { return !li.hidden; });
    if (first) first.firstChild.click();
  });
  function markActive() {
    [].forEach.call(list.children, function (li, i) { li.firstChild.setAttribute('aria-pressed', String(i === follow)); });
  }
  function showResidents(names) {
    residents = names.map(function (name, k) {
      return { name: name, plot: PLOTS[k] || null, crop: CROPS[k % CROPS.length], phase: k * 1.9 };
    });
    list.textContent = '';
    namesHost.textContent = '';
    residents.forEach(function (r, i) {
      var li = document.createElement('li');
      var b = document.createElement('button');
      b.type = 'button';
      b.textContent = r.name;
      b.addEventListener('click', function () {
        if (follow === i) { fit(); return; }
        follow = i; goal = null; markActive();
      });
      li.appendChild(b);
      list.appendChild(li);
      r.label = document.createElement('span');
      r.label.className = 'village-name';
      r.label.textContent = r.name;
      namesHost.appendChild(r.label);
    });
    document.getElementById('resident-count').textContent = String(names.length);
    document.getElementById('residents-empty').hidden = names.length > 0;
    if (follow >= residents.length) follow = -1;
    markActive();
    filter();
  }
  (window.ChickxelSupporters ? window.ChickxelSupporters.load : function (done) { done([]); })(showResidents);

  var still = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var last = 0;
  function frame(now) {
    var t = still ? 0 : now;
    // 지난 그림에서 흐른 시간(초): 화면 주사율과 상관없이 같은 빠르기로 다가가게
    var dt = last ? Math.min(0.1, (now - last) / 1000) : 0;
    last = now;
    g = lctx;
    drawVillage(t, residents);
    // 카메라: 따라가는 병아리 쪽으로(가까이), 또는 '전체 보기' 쪽으로 — 지수 감속으로 부드럽게.
    // 따라갈 때는 콩콩 뛰는 높이를 빼고 발 자리를 겨눈다(화면이 흔들리지 않게).
    var target = goal, rate = 4;
    if (follow >= 0 && residents[follow]) {
      var p = residentPos(residents[follow], t);
      target = { x: VX + p.x + 4, y: VY + p.ground + 2, z: Math.max(cam.z, 5) };
      rate = 2.6;
    }
    if (target) {
      var a = 1 - Math.exp(-rate * dt), az = 1 - Math.exp(-2.2 * dt);
      cam.x += (target.x - cam.x) * a; cam.y += (target.y - cam.y) * a; cam.z += (target.z - cam.z) * az;
      if (goal && Math.abs(goal.z - cam.z) < 0.01 && Math.hypot(goal.x - cam.x, goal.y - cam.y) < 0.5) goal = null;
      clamp();
    }
    var k = cam.z * dpr;
    sctx.setTransform(1, 0, 0, 1, 0, 0);
    sctx.fillStyle = '#101a22';
    sctx.fillRect(0, 0, screen.width, screen.height);
    sctx.imageSmoothingEnabled = false;
    sctx.setTransform(k, 0, 0, k, (vw / 2 - cam.x * cam.z) * dpr, (vh / 2 - cam.y * cam.z) * dpr);
    sctx.drawImage(world, 0, 0);
    sctx.drawImage(live, VX, VY);
    // 이름표(HTML): 병아리 머리 위
    residents.forEach(function (r, i) {
      var q = residentPos(r, t);
      r.label.style.transform = 'translate(' + ((VX + q.x + 4 - cam.x) * cam.z + vw / 2) + 'px,' + ((VY + q.y - 2 - q.bob - cam.y) * cam.z + vh / 2) + 'px) translate(-50%,-100%)';
      r.label.classList.toggle('active', i === follow);
    });
    if (!still) requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
  if (still) window.addEventListener('pointerup', function () { requestAnimationFrame(frame); });
})();
