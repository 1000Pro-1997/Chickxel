// 병아리 후원 마을(village.html — 위 막대의 마을 단추로 온다).
// 위에서 내려다본 숲속 픽셀 마을: 북쪽 언덕(절벽) 아래로 강이 폭포로 떨어져 마을 서쪽을 지나 남서쪽 호수로 흐르고, 가운데 집 앞에서
// 흙길이 갈라진다. 울타리 친 밭마다 커피를 사 주고 이름을 남긴 사람의 병아리(앱 마스코트) 한 마리가 살며, 밭에 물을 주고 마을 곳곳을
// 걸어 다닌다. 마을 밖은 아직 개척하지 않은 숲(멀수록 안개). 땅은 한 번 그려 두고, 나무·집·울타리·병아리처럼 서 있는 것은
// 발 자리(아래 끝)가 위인 것부터 그려 앞뒤가 맞게 겹친다. 끌어서 옮기고 휠·두 손가락·단추로 확대하며, 주민 이름을 누르면 따라간다.
// 후원자 목록은 nav.js의 ChickxelSupporters.load(한 시간 동안 브라우저에 둠).
(function () {
  var T = 16;
  // 온 세상(타일)과 그 안의 마을 자리(전체 보기·안개 기준)
  var WC = 56, WR = 36, VC = 18, VR = 12, COLS = 20, ROWS = 12;
  var WW = WC * T, WH = WR * T, VX = VC * T, VY = VR * T, W = COLS * T, H = ROWS * T;

  // ---------------------------------------------------------------- 도구
  var g;
  function px(x, y, w, h, c) { g.fillStyle = c; g.fillRect(Math.round(x), Math.round(y), w, h); }
  function hash(a, b) { var h = (a * 374761393 + b * 668265263) | 0; h = (h ^ (h >>> 13)) * 1274126177; return ((h ^ (h >>> 16)) >>> 0) / 4294967296; }
  function smooth(x, y, cell, seed) {
    var gx = Math.floor(x / cell), gy = Math.floor(y / cell), fx = x / cell - gx, fy = y / cell - gy;
    function v(a, b) { return hash(a * 7 + seed, b * 13 + seed * 3); }
    var sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
    var top = v(gx, gy) + (v(gx + 1, gy) - v(gx, gy)) * sx, bottom = v(gx, gy + 1) + (v(gx + 1, gy + 1) - v(gx, gy + 1)) * sx;
    return top + (bottom - top) * sy;
  }
  function canvas(w, h) { var c = document.createElement('canvas'); c.width = w; c.height = h; return c; }
  var rgbCache = {};
  function rgb(hex) {
    var v = rgbCache[hex];
    if (!v) { var n = parseInt(hex.slice(1), 16); v = rgbCache[hex] = [n >> 16, (n >> 8) & 255, n & 255]; }
    return v;
  }
  function sprite(x, y, rows, colors) {
    for (var r = 0; r < rows.length; r++)
      for (var c = 0; c < rows[r].length; c++) if (colors[rows[r][c]]) px(x + c, y + r, 1, 1, colors[rows[r][c]]);
  }

  // 빛은 왼쪽 위에서. 색은 참고한 숲 마을 그림들에서 골라 맞춤
  var C = {
    grass: '#5a9c3c', grass2: '#55953a', grassLight: '#79bb48', grassRim: '#98d35c', grassDark: '#447f33', grassShade: '#3d7430',
    tuft: '#3f7a31', tuftTip: '#6fb244',
    dirt: '#a8693d', dirtDark: '#8f5631', dirtLight: '#c0844f', dirtShadow: '#7a4627',
    cliff: '#9c5d36', cliffDark: '#7d4628', cliffStreak: '#874f2d', cliffLight: '#b8743f', cliffFoot: '#6a3a22', cliffLine: '#4a2a18',
    water: '#4a86c4', water2: '#4580bd', waterDark: '#3a6ea8', waterLight: '#7bb6e6', foam: '#e3f4ff', fall: '#6aa6dc', fallLight: '#a6d6f6',
    wood: '#a86a3a', woodDark: '#7a4a28', woodLight: '#c98a52', woodLine: '#4a2c18',
    ink: '#24361f'
  };

  // ---------------------------------------------------------------- 지도(타일 종류)
  // G 풀 · P 흙길 · W 물 · C 절벽 · F 폭포 · S 돌계단 · B 다리(물 위)
  var CLIFF = VR, PATH_ROW = VR + 6, PATH_COL = VC + 8, STAIR_COL = VC + 19;
  function riverCols(r) {
    // 북쪽 언덕에서는 조금 굽이치고, 마을에서는 곧게(VC+2, VC+3), 마을 아래에서는 호수 쪽(남서)으로 휜다
    var cx = VC + 3;
    if (r < VR) cx += Math.round(Math.sin(r * 0.5) * 1.3);
    else if (r > VR + 11) cx -= Math.min(4, r - (VR + 11));
    return [cx - 1, cx];
  }
  function isLake(c, r) {
    var dx = (c - (VC - 1)) / 7.5, dy = (r - (VR + 16)) / 4.2;
    return dx * dx + dy * dy + (smooth(c, r, 3, 5) - 0.5) * 0.6 < 1;
  }
  function isRiver(c, r) { var rc = riverCols(r); return r <= VR + 15 && (c === rc[0] || c === rc[1]); }
  function isPathTile(c, r) {
    if (r === PATH_ROW && c >= VC - 8 && c <= VC + 27) return true;
    if (c === PATH_COL && r >= VR + 5 && r <= VR + 18) return true;
    if (c === STAIR_COL && r >= VR - 6 && r <= PATH_ROW) return true;
    return false;
  }
  var TT = new Array(WC * WR);
  for (var r0 = 0; r0 < WR; r0++)
    for (var c0 = 0; c0 < WC; c0++) {
      var t = 'G';
      var water = isRiver(c0, r0) || isLake(c0, r0);
      if (r0 === CLIFF) t = water ? 'F' : c0 === STAIR_COL ? 'S' : 'C';
      else if (water) t = isPathTile(c0, r0) && r0 === PATH_ROW ? 'B' : 'W';
      else if (isPathTile(c0, r0)) t = 'P';
      TT[r0 * WC + c0] = t;
    }
  function tile(c, r) { return c < 0 || r < 0 || c >= WC || r >= WR ? 'G' : TT[r * WC + c]; }
  function wet(t) { return t === 'W' || t === 'B' || t === 'F'; }
  function walk(t) { return t === 'P' || t === 'S'; }
  // 마을 가장자리에서 (c, r)까지의 거리(타일, 마을 안이면 0)
  function distance(c, r) {
    var dx = Math.max(VC - c, 0, c - (VC + COLS - 1)), dy = Math.max(VR - r, 0, r - (VR + ROWS - 1));
    return Math.sqrt(dx * dx + dy * dy);
  }
  // 풀이 흙·물로 삐죽 넘어오는 길이(2~4px, 2px마다 다름)
  function jag(u, seed) { return 2 + Math.floor(hash(u >> 1, seed) * 3); }

  // ---------------------------------------------------------------- 땅(픽셀 단위로 한 번)
  function groundPixel(x, y) {
    var c = x >> 4, r = y >> 4, lx = x & 15, ly = y & 15, t = tile(c, r);
    var up = tile(c, r - 1), down = tile(c, r + 1), left = tile(c - 1, r), right = tile(c + 1, r);
    if (t === 'P') {
      // 마을 밖 길은 멀수록 풀에 덮인다(4px 덩어리)
      var d = distance(c, r);
      if (d > 0.5 && hash(x >> 2, y >> 2) > 1 - d * 0.16) return grassPixel(x, y, c, r, lx, ly, up);
      var e = edgeGrass(x, y, lx, ly, up, down, left, right, function (n) { return !walk(n) && n !== 'B'; });
      if (e) return e;
      var n = hash(x >> 1, y >> 1);
      return n > 0.93 ? C.dirtLight : n < 0.07 ? C.dirtDark : hash(x >> 3, y >> 2) > 0.8 ? '#a26539' : C.dirt;
    }
    if (t === 'W' || t === 'B') {
      var e2 = edgeWater(x, y, lx, ly, up, down, left, right);
      if (e2) return e2;
      return smooth(x / 16, y / 16, 3, 2) > 0.6 ? C.water2 : C.water;
    }
    if (t === 'C') {
      var j = jag(x, 91);
      if (ly < j - 1) return C.grassLight;
      if (ly === j - 1) return C.grassRim;
      if (ly === j) return C.cliffLine;
      if (ly <= j + 2) return C.cliffDark;
      if (ly >= 14) return ly === 15 ? C.cliffLine : C.cliffFoot;
      if (hash(x >> 1, 17) > 0.78) return C.cliffStreak;
      if (hash(x, y) > 0.96) return C.cliffLight;
      return C.cliff;
    }
    if (t === 'F') {
      if (ly < 2) return C.foam;
      return hash(x, 5) > 0.6 ? C.fallLight : C.fall;
    }
    if (t === 'S') {
      if (lx < 2 || lx > 13) return lx === 0 || lx === 15 ? C.cliffLine : C.cliffFoot;
      var s = ly % 4;
      return s === 0 ? '#c99460' : s === 3 ? C.dirtShadow : '#ad7447';
    }
    return grassPixel(x, y, c, r, lx, ly, up);
  }
  function grassPixel(x, y, c, r, lx, ly, up) {
    // 절벽 바로 아래는 그늘
    if ((up === 'C' || up === 'S') && ly < 4) return ly < 2 ? C.grassShade : C.grassDark;
    var n = hash(x >> 1, y >> 1);
    if (n > 0.95) return C.grassLight;
    if (n < 0.04) return C.grassDark;
    return smooth(x / 16, y / 16, 2.5, 7) > 0.58 ? C.grass2 : C.grass;
  }
  // 흙길 칸: 풀 이웃 쪽 가장자리에 삐죽한 풀 테두리(위쪽 풀이면 그 아래 흙에 그늘)
  function edgeGrass(x, y, lx, ly, up, down, left, right, grassy) {
    var j;
    if (grassy(up)) { j = jag(x, y >> 4); if (ly < j - 1) return C.grass; if (ly === j - 1) return C.grassLight; if (ly === j) return C.dirtShadow; if (ly === j + 1) return C.dirtDark; }
    if (grassy(down)) { j = jag(x, (y >> 4) + 50); var d = 15 - ly; if (d < j - 1) return C.grass; if (d === j - 1) return C.grassRim; }
    if (grassy(left)) { j = jag(y, (x >> 4) + 90); if (lx < j - 1) return C.grass; if (lx === j - 1) return C.grassLight; if (lx === j) return C.dirtShadow; }
    if (grassy(right)) { j = jag(y, (x >> 4) + 130); var d2 = 15 - lx; if (d2 < j - 1) return C.grass; if (d2 === j - 1) return C.grassLight; if (d2 === j) return C.dirtShadow; }
    return null;
  }
  // 물 칸: 땅 이웃 쪽에 풀 테두리와 짙은 물 그늘(위쪽 땅이면 그늘이 넓다)
  function edgeWater(x, y, lx, ly, up, down, left, right) {
    var j;
    if (!wet(up)) { j = jag(x, (y >> 4) + 7); if (ly < j - 1) return C.grassLight; if (ly === j - 1) return C.ink; if (ly < j + 4) return C.waterDark; }
    if (up === 'F' && ly < 4) return hash(x >> 1, y) > 0.4 ? C.foam : C.waterLight;
    if (!wet(left)) { j = jag(y, (x >> 4) + 11); if (lx < j - 2) return C.grass; if (lx === j - 2) return C.ink; if (lx < j + 1) return C.waterDark; }
    if (!wet(right)) { j = jag(y, (x >> 4) + 13); var d = 15 - lx; if (d < j - 2) return C.grass; if (d === j - 2) return C.ink; if (d < j + 1) return C.waterDark; }
    if (!wet(down)) { j = jag(x, (y >> 4) + 17); var d2 = 15 - ly; if (d2 < j - 2) return C.grass; if (d2 === j - 2) return C.ink; }
    return null;
  }

  // ---------------------------------------------------------------- 서 있는 것(발 자리 = 아래 끝 기준으로 앞뒤 정렬)
  var objects = [];
  // 그림 한 장(작은 캔버스)을 세상 (x, y)(왼쪽 위)에 두고, 발 자리 base(세상 y)로 정렬
  function place(img, x, y, base) { objects.push({ img: img, x: Math.round(x), y: Math.round(y), base: base, w: img.width, h: img.height }); }
  // 그린 모양 둘레에 1px 테두리(나무·덤불)
  function outline(cv, color) {
    var ctx = cv.getContext('2d'), w = cv.width, h = cv.height, data = ctx.getImageData(0, 0, w, h), a = data.data;
    var edge = [], o = rgb(color);
    for (var y = 0; y < h; y++)
      for (var x = 0; x < w; x++) {
        if (a[(y * w + x) * 4 + 3] > 0) continue;
        var near = (x > 0 && a[(y * w + x - 1) * 4 + 3] > 200) || (x < w - 1 && a[(y * w + x + 1) * 4 + 3] > 200)
          || (y > 0 && a[((y - 1) * w + x) * 4 + 3] > 200) || (y < h - 1 && a[((y + 1) * w + x) * 4 + 3] > 200);
        if (near) edge.push(x, y);
      }
    for (var k = 0; k < edge.length; k += 2) {
      var i = (edge[k + 1] * w + edge[k]) * 4;
      a[i] = o[0]; a[i + 1] = o[1]; a[i + 2] = o[2]; a[i + 3] = 255;
    }
    ctx.putImageData(data, 0, 0);
  }
  // 둥근 덩어리 하나(빛: 왼쪽 위) — 나무 수관과 덤불은 이것을 위에서 아래로 겹쳐 쌓는다
  function lump(cx, cy, rad, pal, seed) {
    for (var y = -rad; y <= rad; y++)
      for (var x = -rad; x <= rad; x++) {
        var d = Math.sqrt(x * x + y * y);
        if (d > rad + 0.3) continue;
        var s = (-x * 0.55 - y * 0.85) / rad + (hash(cx + x + seed, cy + y) - 0.5) * 0.25;
        var col = s > 0.62 && d < rad * 0.75 ? pal[3] : s > 0.2 ? pal[2] : s > -0.45 ? pal[1] : pal[0];
        if (d > rad - 1 && s < -0.1) col = pal[0];
        px(cx + x, cy + y, 1, 1, col);
      }
  }
  var ROUND = ['#2f6a2b', '#4a8f36', '#64ab42', '#8acb57'];
  var ROUND_DARK = ['#2a5d29', '#3f7f33', '#57993d', '#79bb4c'];
  var treeCache = {};
  // 둥근 활엽수(수관 반지름 rad): 뭉게뭉게한 수관, 아래로 뿌리와 짧은 줄기, 바닥 그림자
  function roundTree(rad, seed) {
    var key = 'r' + rad + '-' + (seed % 5);
    if (treeCache[key]) return treeCache[key];
    var w = rad * 2 + 8, h = rad * 2 + 14, cv = canvas(w, h), cx = w / 2;
    g = cv.getContext('2d');
    var pal = seed % 3 === 0 ? ROUND_DARK : ROUND;
    // 그림자 · 뿌리 · 줄기
    px(cx - rad + 2, h - 5, rad * 2 - 4, 3, 'rgba(20,45,20,0.35)');
    px(cx - rad + 4, h - 4, rad * 2 - 8, 2, 'rgba(20,45,20,0.25)');
    px(cx - 4, h - 10, 8, 7, C.woodLine); px(cx - 3, h - 10, 6, 6, '#6b4026'); px(cx - 3, h - 10, 2, 6, '#85512e');
    px(cx - 6, h - 5, 3, 2, C.woodLine); px(cx + 3, h - 5, 3, 2, C.woodLine); px(cx - 5, h - 5, 2, 1, '#6b4026'); px(cx + 3, h - 5, 2, 1, '#6b4026');
    // 수관: 아래 둘레 덩어리 → 가운데 → 위 순서로(위가 앞에 보이게)
    var top = rad + 2, cy = top + 1, lumps = [];
    var n = 7 + seed % 3;
    for (var k = 0; k < n; k++) {
      var a = (k / n) * Math.PI * 2 + seed;
      lumps.push([cx + Math.cos(a) * rad * 0.55, cy + Math.sin(a) * rad * 0.5 + 2, rad * 0.48]);
    }
    lumps.push([cx - rad * 0.2, cy - rad * 0.15, rad * 0.55]);
    lumps.push([cx + rad * 0.25, cy - rad * 0.3, rad * 0.45]);
    lumps.push([cx - rad * 0.1, cy - rad * 0.55, rad * 0.42]);
    lumps.sort(function (p, q) { return q[1] - p[1]; });
    lumps.forEach(function (l, i) { lump(Math.round(l[0]), Math.round(l[1]), Math.round(l[2]), pal, seed * 31 + i); });
    outline(cv, '#1f3a1d');
    var out = { img: cv, foot: h - 4 };
    treeCache[key] = out;
    return out;
  }
  // 전나무: 톱니 같은 층 셋, 왼쪽이 밝다
  var PINE = ['#1f5a35', '#2f7f45', '#46a052', '#73c862'];
  function pineTree(size, seed) {
    var key = 'p' + size + '-' + (seed % 4);
    if (treeCache[key]) return treeCache[key];
    var w = size * 2 + 6, h = size * 3 + 12, cv = canvas(w, h), cx = Math.floor(w / 2);
    g = cv.getContext('2d');
    px(cx - size + 3, h - 5, size * 2 - 6, 3, 'rgba(20,45,20,0.35)');
    px(cx - 3, h - 11, 6, 8, C.woodLine); px(cx - 2, h - 11, 4, 7, '#6b4026');
    var tiers = 3, tierH = Math.round(size * 1.05);
    for (var k = 0; k < tiers; k++) {
      var bottom = h - 9 - k * Math.round(size * 0.78), half = Math.round(size * (1 - k * 0.24));
      for (var y = 0; y < tierH; y++) {
        var hw = Math.round(half * (y + 1) / tierH);
        var yy = bottom - tierH + y;
        for (var x = -hw; x <= hw; x++) {
          var saw = y === tierH - 1 && ((x + k) & 3) === 0;
          if (saw) continue;
          var s = -x / (half + 1) + (hash(x + seed, yy) - 0.5) * 0.3;
          var col = x === 0 && y > 2 ? PINE[1] : s > 0.45 ? PINE[3] : s > 0 ? PINE[2] : s > -0.5 ? PINE[1] : PINE[0];
          px(cx + x, yy, 1, 1, col);
        }
      }
    }
    outline(cv, '#173a22');
    var out = { img: cv, foot: h - 4 };
    treeCache[key] = out;
    return out;
  }
  function addRoundTree(x, foot, rad, seed) { var t = roundTree(rad, seed); place(t.img, x - t.img.width / 2, foot - t.foot, foot); }
  function addPine(x, foot, size, seed) { var t = pineTree(size, seed); place(t.img, x - t.img.width / 2, foot - t.foot, foot); }
  // 덤불(네 잎 클로버처럼 낮게 뭉친 것)
  function bush(seed) {
    var key = 'b' + (seed % 3);
    if (treeCache[key]) return treeCache[key];
    var cv = canvas(16, 14);
    g = cv.getContext('2d');
    px(2, 11, 12, 2, 'rgba(20,45,20,0.3)');
    lump(5, 8, 4, ROUND, seed); lump(11, 8, 4, ROUND, seed + 1); lump(8, 5, 4, ROUND, seed + 2);
    outline(cv, '#1f3a1d');
    return (treeCache[key] = { img: cv, foot: 12 });
  }
  function addBush(x, foot, seed) { var b = bush(seed); place(b.img, x - 8, foot - b.foot, foot); }
  function draw(w, h, fn) { var cv = canvas(w, h); g = cv.getContext('2d'); fn(); return cv; }
  var ROCK = { k: '#33373f', l: '#b9bec7', g: '#8d929c', d: '#666b75', m: '#6fa046' };
  function addRock(x, foot, big) {
    var rows = big
      ? ['....kkkkkk....', '..kkllllggkk..', '.kllllgggggdk.', 'kllgggggggdddk', 'klgggggggddddk', 'kgggggggdddddk', '.kgggddddddddk', '..kkkkkkkkkkk.']
      : ['..kkkk..', '.kllggk.', 'klgggddk', 'kggdddk.', '.kkkkk..'];
    var cv = draw(rows[0].length + 2, rows.length + 2, function () {
      px(1, rows.length - 1, rows[0].length, 2, 'rgba(20,45,20,0.3)');
      sprite(1, 0, rows, ROCK);
    });
    place(cv, x - cv.width / 2, foot - rows.length, foot);
  }
  function addStump(x, foot) {
    var cv = draw(14, 12, function () {
      px(1, 9, 12, 2, 'rgba(20,45,20,0.3)');
      px(1, 3, 12, 7, C.woodLine); px(2, 4, 10, 5, '#7a4a28'); px(2, 4, 3, 5, '#8f5a33');
      px(1, 1, 12, 4, C.woodLine); px(2, 1, 10, 3, '#c8955c'); px(4, 2, 6, 1, '#a8703f');
    });
    place(cv, x - 7, foot - 10, foot);
  }
  function addLupin(x, foot, color) {
    var cv = draw(5, 11, function () {
      px(2, 4, 1, 7, '#3f7a31');
      for (var k = 0; k < 4; k++) { px(1, k * 2, 3, 2, color); px(2, k * 2, 1, 1, '#f1d4ff'); }
    });
    place(cv, x - 2, foot - 11, foot);
  }
  function addMushroom(x, foot) {
    var cv = draw(7, 7, function () {
      px(2, 4, 3, 3, '#efe3cf'); px(0, 1, 7, 3, '#5a1e1a'); px(1, 0, 5, 3, '#d8463a'); px(2, 1, 1, 1, '#fff'); px(4, 2, 1, 1, '#fff');
    });
    place(cv, x - 3, foot - 7, foot);
  }
  function addReeds(x, foot) {
    var cv = draw(7, 12, function () {
      for (var k = 0; k < 3; k++) { px(1 + k * 2, 3 + k % 2, 1, 9 - k % 2, '#4c8f3f'); px(1 + k * 2, 1 + k % 2, 1, 3, '#8a5530'); }
    });
    place(cv, x - 3, foot - 12, foot);
  }
  function addLamp(x, foot) {
    var cv = draw(26, 30, function () {
      g.fillStyle = 'rgba(255,190,90,0.18)'; g.beginPath(); g.arc(13, 8, 12, 0, Math.PI * 2); g.fill();
      px(9, 26, 8, 3, 'rgba(20,45,20,0.35)');
      px(11, 10, 4, 18, '#2c3038'); px(12, 10, 2, 18, '#5a5f6a');
      px(9, 26, 8, 3, '#2c3038'); px(10, 26, 6, 2, '#6a707c');
      px(8, 2, 10, 10, '#2c3038'); px(9, 4, 8, 6, '#ffb84a'); px(10, 5, 4, 3, '#ffe9a8'); px(8, 0, 10, 3, '#3c414c'); px(10, 0, 6, 1, '#6a707c');
    });
    place(cv, x - 13, foot - 29, foot);
  }
  function addSign(x, foot) {
    var cv = draw(26, 24, function () {
      px(10, 20, 8, 3, 'rgba(20,45,20,0.35)');
      px(11, 4, 4, 18, C.woodLine); px(12, 4, 2, 18, '#8a5530');
      // 오른쪽 화살 판 · 왼쪽 화살 판
      px(4, 3, 18, 7, C.woodLine); px(5, 4, 15, 5, C.woodLight); px(20, 5, 2, 3, C.woodLight); px(22, 6, 1, 1, C.woodLine); px(6, 6, 12, 1, '#a8703f');
      px(2, 11, 18, 7, C.woodLine); px(5, 12, 14, 5, C.wood); px(3, 13, 2, 3, C.wood); px(2, 14, 1, 1, C.woodLine); px(7, 14, 11, 1, '#8a5530');
    });
    place(cv, x - 13, foot - 22, foot);
  }
  function addMailbox(x, foot) {
    var cv = draw(10, 16, function () {
      px(4, 6, 2, 10, C.woodLine);
      px(0, 0, 10, 7, '#23344a'); px(1, 1, 8, 5, '#4f86c7'); px(1, 1, 8, 1, '#7fb0e6'); px(8, -1, 1, 4, '#e8463a');
    });
    place(cv, x - 5, foot - 16, foot);
  }
  function addCrate(x, foot, produce) {
    var cv = draw(16, 15, function () {
      px(1, 12, 14, 2, 'rgba(20,45,20,0.35)');
      px(0, 3, 16, 11, C.woodLine); px(1, 4, 14, 9, C.wood); px(1, 8, 14, 1, C.woodDark); px(1, 4, 14, 1, C.woodLight);
      if (produce) for (var k = 0; k < 5; k++) { px(2 + k * 3, 1 + (k % 2), 3, 3, produce); px(2 + k * 3, 1 + (k % 2), 1, 1, '#fff6d6'); }
    });
    place(cv, x - 8, foot - 14, foot);
  }
  function addBarrel(x, foot) {
    var cv = draw(12, 16, function () {
      px(1, 13, 10, 2, 'rgba(20,45,20,0.35)');
      px(0, 1, 12, 14, C.woodLine); px(1, 2, 10, 12, '#9a5f33'); px(1, 2, 3, 12, '#b0703d');
      px(1, 4, 10, 1, '#4f5560'); px(1, 11, 10, 1, '#4f5560'); px(1, 0, 10, 3, C.woodLine); px(2, 1, 8, 1, '#c8955c');
    });
    place(cv, x - 6, foot - 15, foot);
  }
  function addLog(x, foot, len) {
    var cv = draw(len + 2, 12, function () {
      px(1, 9, len, 3, 'rgba(20,45,20,0.35)');
      px(0, 1, len + 2, 9, C.woodLine); px(1, 2, len, 7, '#9a5a30'); px(1, 2, len, 2, '#b06a3a'); px(1, 7, len, 1, '#7a4426');
      for (var k = 6; k < len - 4; k += 9) px(1 + k, 4, 4, 1, '#7a4426');
      px(len - 4, 2, 5, 7, '#c8955c'); px(len - 3, 4, 3, 3, '#a8703f');
    });
    place(cv, x, foot - 10, foot);
  }
  // 울타리: 가로 줄(기둥 8px마다, 가로대 둘), 세로 줄(기둥만 촘촘히, 가로대는 세로로)
  function fenceH(x, y, len, gap) {
    var cv = draw(len + 4, 14, function () {
      px(0, 10, len + 4, 2, 'rgba(20,45,20,0.25)');
      for (var k = 0; k <= len; k += 8) {
        if (gap && k > gap[0] && k < gap[1]) continue;
        px(k, 0, 4, 11, C.woodLine); px(k + 1, 1, 2, 9, C.woodLight); px(k + 1, 1, 1, 9, '#e0aa6c');
      }
      for (var s = 0; s < len; s++) {
        if (gap && s > gap[0] && s < gap[1]) continue;
        px(s + 2, 3, 1, 3, C.woodLine); px(s + 2, 4, 1, 1, C.wood);
        px(s + 2, 7, 1, 2, C.woodLine);
      }
    });
    place(cv, x - 2, y - 11, y);
  }
  function fenceV(x, y, len) {
    var cv = draw(6, len + 12, function () {
      for (var k = 0; k <= len; k += 8) { px(1, k, 4, 11, C.woodLine); px(2, k + 1, 2, 9, C.woodLight); }
      px(2, 3, 2, len, C.woodLine); px(2, 3, 1, len, C.wood);
    });
    place(cv, x - 3, y - 11, y + len);
  }
  function fenceBox(x0, y0, x1, y1, gapTop, gapBottom) {
    fenceH(x0, y0, x1 - x0, gapTop);
    fenceH(x0, y1, x1 - x0, gapBottom);
    fenceV(x0 + 2, y0 + 2, y1 - y0 - 2);
    fenceV(x1 + 2, y0 + 2, y1 - y0 - 2);
  }
  // 집(참고 그림의 나무 골조 오두막): 짙은 초록 너와 지붕, 가운데 용마루, 크림색 벽에 나무 골조, 둥근 문과 창
  function addHouse(cx, foot) {
    var w = 84, h = 76;
    var cv = draw(w, h, function () {
      px(4, h - 4, w - 8, 4, 'rgba(20,45,20,0.35)');
      var wallTop = 40;
      // 벽
      px(6, wallTop, w - 12, h - wallTop - 3, C.woodLine);
      px(7, wallTop + 1, w - 14, h - wallTop - 5, '#f1dcb0');
      px(7, wallTop + 1, w - 14, 2, '#d8bf8e');
      [7, 40, w - 10].forEach(function (bx) { px(bx, wallTop + 1, 3, h - wallTop - 5, '#8a5530'); px(bx, wallTop + 1, 1, h - wallTop - 5, '#a86a3a'); });
      px(7, wallTop + 14, w - 14, 3, '#8a5530');
      for (var k = 0; k < 10; k++) { px(11 + k * 2.6, wallTop + 4 + k, 3, 1, '#8a5530'); px(w - 14 - k * 2.6, wallTop + 4 + k, 3, 1, '#8a5530'); }
      // 돌 기단
      px(6, h - 7, w - 12, 4, '#2e3138'); for (var s = 0; s < 9; s++) px(7 + s * 8, h - 6, 7, 2, s % 2 ? '#8d929c' : '#a7acb5');
      // 창(왼쪽) · 문(가운데, 둥근 윗변) · 창(오른쪽)
      [[14, wallTop + 18], [56, wallTop + 18]].forEach(function (p) {
        px(p[0], p[1], 14, 12, C.woodLine); px(p[0] + 1, p[1] + 1, 12, 10, '#7ec3ea'); px(p[0] + 1, p[1] + 1, 4, 3, '#d6f0ff');
        px(p[0] + 6, p[1] + 1, 1, 10, C.woodLine); px(p[0] + 1, p[1] + 5, 12, 1, C.woodLine);
        px(p[0] - 1, p[1] + 12, 16, 3, '#7a4a28'); px(p[0], p[1] + 11, 3, 2, '#e85a6a'); px(p[0] + 5, p[1] + 11, 3, 2, '#ffd35c'); px(p[0] + 10, p[1] + 11, 3, 2, '#e85a6a');
      });
      px(33, wallTop + 16, 18, h - wallTop - 19, C.woodLine);
      px(35, wallTop + 14, 14, 3, C.woodLine);
      px(34, wallTop + 17, 16, h - wallTop - 21, '#8a5530'); px(36, wallTop + 15, 12, 3, '#8a5530');
      for (var p2 = 0; p2 < 4; p2++) px(36 + p2 * 4, wallTop + 17, 1, h - wallTop - 21, '#6b4026');
      px(46, wallTop + 26, 2, 2, '#ffd23f');
      // 지붕: 위가 좁은 사다리꼴, 줄마다 너와 이음매, 왼쪽 밝게, 가운데 용마루
      for (var y = 0; y < wallTop + 4; y++) {
        var inset = Math.round((wallTop + 3 - y) * 0.22);
        var x0 = inset, x1 = w - inset;
        for (var x = x0; x < x1; x++) {
          var left = x < w / 2;
          var col = left ? '#3f8a6c' : '#2d6a55';
          if ((y % 5) === 4) col = left ? '#2d6a55' : '#215242';
          else if (((x + (Math.floor(y / 5) % 2) * 3) % 6) === 0) col = left ? '#347a60' : '#255c4a';
          if (left && (y % 5) === 0 && hash(x, y) > 0.5) col = '#56a383';
          if (x === x0 || x === x1 - 1 || y === 0) col = '#173a30';
          px(x, y, 1, 1, col);
        }
      }
      px(0, wallTop + 3, w, 2, '#173a30');
      px(w / 2 - 3, 0, 6, wallTop + 4, '#5a3420'); px(w / 2 - 2, 0, 4, wallTop + 3, '#9a6236'); px(w / 2 - 2, 0, 1, wallTop + 3, '#c08453');
      px(w / 2 - 5, wallTop + 1, 10, 4, '#5a3420'); px(w / 2 - 4, wallTop + 2, 8, 2, '#9a6236');
    });
    place(cv, cx - w / 2, foot - h + 1, foot);
  }

  // ---------------------------------------------------------------- 마을 배치(세상 픽셀)
  function X(c) { return VX + c * T; }
  function Y(r) { return VY + r * T; }
  // 밭 자리(32×32, 잘 보이는 자리 먼저)와 울타리 안 병아리가 물 주는 자리(front), 드나드는 문(gate 안·밖)
  var NE = { x0: X(11), y0: Y(1) + 6, x1: X(17) + 4, y1: Y(5) + 2 };
  var SE = { x0: X(11), y0: Y(7) + 10, x1: X(20) - 4, y1: Y(11) + 12 };
  var NE_GATE = { x: X(14) + 2, inY: NE.y1 - 7, outY: Y(6) + 9 };
  var SE_GATE = { x: X(15) + 8, inY: SE.y0 + 8, outY: Y(6) + 9 };
  var PLOTS = [
    { x: SE.x0 + 10, y: SE.y0 + 22, frontY: SE.y0 + 18, gate: SE_GATE },
    { x: NE.x0 + 12, y: NE.y0 + 6, frontY: NE.y0 + 44, gate: NE_GATE },
    { x: SE.x0 + 54, y: SE.y0 + 22, frontY: SE.y0 + 18, gate: SE_GATE },
    { x: NE.x0 + 52, y: NE.y0 + 6, frontY: NE.y0 + 44, gate: NE_GATE },
    { x: SE.x0 + 98, y: SE.y0 + 22, frontY: SE.y0 + 18, gate: SE_GATE }
  ];
  var CROPS = ['carrot', 'cabbage', 'wheat', 'tomato', 'pumpkin', 'sprout'];
  var HAY = { x0: X(4) + 4, y0: Y(7) + 12, x1: X(7) + 10, y1: Y(10) + 10 };

  function buildObjects() {
    addHouse(X(8) + 8, Y(5));
    fenceBox(NE.x0, NE.y0, NE.x1, NE.y1, null, [NE_GATE.x - NE.x0 - 10, NE_GATE.x - NE.x0 + 10]);
    fenceBox(SE.x0, SE.y0, SE.x1, SE.y1, [SE_GATE.x - SE.x0 - 10, SE_GATE.x - SE.x0 + 10], null);
    fenceBox(HAY.x0, HAY.y0, HAY.x1, HAY.y1, null, null);
    // 다리 난간(위 줄은 병아리 뒤, 아래 줄은 앞)
    var bx0 = X(2) - 6, blen = 2 * T + 8;
    fenceH(bx0, Y(6) + 3, blen, null);
    fenceH(bx0, Y(7) + 1, blen, null);
    // 집 앞과 갈림길
    addCrate(X(6) + 2, Y(5) + 6, '#ff8a2a'); addCrate(X(6) + 14, Y(5) + 7, '#8ed36c'); addBarrel(X(5) + 6, Y(5) + 5);
    addMailbox(X(7) + 12, Y(5) + 6);
    addLamp(X(10) + 8, Y(6) + 1);
    addSign(X(10) + 2, Y(7) + 12);
    addCrate(X(18) + 4, Y(1) + 12, '#ff4b3a'); addCrate(X(18) + 4, Y(2) + 12, '#ffd35c');
    addBush(X(18) + 6, Y(5) + 2, 1); addBush(X(9) + 12, Y(11) + 14, 2);
    // 마을 안 나무 · 돌 · 꽃 · 버섯 · 통나무
    addPine(X(4) + 10, Y(4) + 14, 9, 1); addPine(X(10) + 14, Y(10) + 6, 10, 2);
    addRoundTree(X(0) + 10, Y(3) + 8, 14, 3); addRoundTree(X(1), Y(10) + 10, 16, 4);
    addRock(X(1) + 8, Y(8) + 6, true); addRock(X(19) + 4, Y(11) + 10, false); addRock(X(5) + 2, Y(1) + 6, false); addRock(X(12) + 8, Y(5) + 14, false);
    addLupin(X(0) + 12, Y(5) + 10, '#a46ad8'); addLupin(X(4) + 4, Y(6) - 1, '#a46ad8'); addLupin(X(16) + 6, Y(6) - 1, '#c88ae8'); addLupin(X(20) + 2, Y(9), '#a46ad8');
    addMushroom(X(5) + 4, Y(2) + 12); addMushroom(X(9) + 14, Y(9) + 4); addMushroom(X(17) + 10, Y(6) - 2);
    addLog(X(4) + 2, Y(11) + 12, 40);
    addStump(X(20) + 6, Y(8) + 6);
    addReeds(X(1) + 6, Y(12) + 2);
    buildWild();
    objects.sort(function (a, b) { return a.base - b.base || a.x - b.x; });
  }

  // 마을 밖 숲: 마을에서 멀수록, 무늬 값이 클수록 나무가 빽빽(길·물·절벽 옆은 비움). 사이사이 돌·그루터기·버섯·꽃
  function buildWild() {
    for (var r = 0; r < WR + 2; r++)
      for (var c = -1; c < WC + 1; c++) {
        if (c >= VC - 1 && c <= VC + COLS && r >= VR && r <= VR + ROWS) continue;
        var t = tile(c, r), d = distance(c, r);
        if (t !== 'G') continue;
        var clear = false;
        for (var dy = -1; dy <= 1 && !clear; dy++)
          for (var dx = -1; dx <= 1; dx++) if (tile(c + dx, r + dy) !== 'G' && !(dy === -1 && tile(c + dx, r + dy) === 'C')) { clear = true; break; }
        if (tile(c, r + 1) === 'C') clear = true;
        var x = c * T + 4 + Math.floor(hash(c, r + 1) * 8), foot = r * T + 12 + Math.floor(hash(c + 2, r) * 4);
        var dense = smooth(c, r, 4, 1) + Math.min(d, 8) * 0.05;
        if (!clear && d >= 1.2 && dense > 0.6 && hash(c + 3, r + 5) > 0.3) {
          if (smooth(c, r, 6, 9) > 0.6) addPine(x, foot, 9 + Math.floor(hash(c, r) * 3), c * 7 + r);
          else addRoundTree(x, foot, 13 + Math.floor(hash(r, c) * 6), c * 3 + r * 5);
          continue;
        }
        var f = hash(c + 11, r + 7);
        if (clear && !wet(t)) { if (f > 0.93) addRock(x, foot, false); continue; }
        if (f > 0.96) addBush(x, foot, c + r);
        else if (f > 0.93) addRock(x, foot, f > 0.95);
        else if (f < 0.03) addStump(x, foot);
        else if (f < 0.05) addMushroom(x, foot);
        else if (f > 0.5 && f < 0.52) addLupin(x, foot, '#a46ad8');
      }
    // 호숫가 갈대
    for (var rr = VR + 11; rr < WR; rr++)
      for (var cc = VC - 10; cc < VC + 6; cc++)
        if (tile(cc, rr) === 'G' && wet(tile(cc, rr + 1)) && hash(cc, rr * 3) > 0.55) addReeds(cc * T + 8, rr * T + 15);
  }

  // 땅 위에 바로 그리는 것(납작한 것): 풀 무더기, 들꽃, 연잎, 다리 판자, 밀밭, 빈 밭
  function decorateGround() {
    for (var r = 0; r < WR; r++)
      for (var c = 0; c < WC; c++) {
        var t = tile(c, r), x = c * T, y = r * T;
        if (t === 'G') {
          for (var k = 0; k < 3; k++) {
            var hx = Math.floor(hash(c * 3 + k, r) * 12) + 1, hy = Math.floor(hash(r * 5 + k, c) * 11) + 3;
            if (tile(c, r - 1) === 'C' && hy < 6) continue;
            px(x + hx, y + hy, 1, 2, C.tuft); px(x + hx + 2, y + hy, 1, 2, C.tuft); px(x + hx + 1, y + hy + 1, 1, 2, C.tuft);
            px(x + hx + 1, y + hy - 1, 1, 1, C.tuftTip);
          }
          var f = hash(c + 21, r + 3);
          if (f > 0.9) { px(x + 6, y + 6, 3, 3, '#ffffff'); px(x + 7, y + 7, 1, 1, '#ffd35c'); px(x + 7, y + 9, 1, 1, C.tuft); }
          else if (f < 0.05) { px(x + 9, y + 9, 2, 2, '#ffd35c'); px(x + 4, y + 4, 2, 2, '#ff9fb0'); }
        }
        if (t === 'W' && isLake(c, r) && !isRiver(c, r) && hash(c * 5, r) > 0.86) {
          var lx2 = x + 3 + Math.floor(hash(c, r) * 6), ly2 = y + 4 + Math.floor(hash(r, c) * 6);
          px(lx2, ly2, 7, 4, '#2f6a2b'); px(lx2 + 1, ly2, 5, 3, '#5fbf6a'); px(lx2 + 3, ly2 + 1, 1, 2, '#2f6a2b');
          if (hash(c, r + 3) > 0.6) { px(lx2 + 2, ly2 - 2, 3, 2, '#ffb3c1'); px(lx2 + 3, ly2 - 3, 1, 1, '#fff'); }
        }
      }
    // 다리 판자(물 위, 양쪽 둑에 조금 걸침)
    var bx0 = X(2) - 6, bw = 2 * T + 12, by0 = Y(6) + 1;
    px(bx0, by0, bw, 15, C.woodLine);
    for (var b = 0; b < bw - 2; b += 4) { px(bx0 + 1 + b, by0 + 1, 3, 13, b % 8 ? '#9a5f33' : C.wood); px(bx0 + 1 + b, by0 + 1, 1, 13, C.woodLight); px(bx0 + 2 + b, by0 + 3, 1, 1, C.woodLine); px(bx0 + 2 + b, by0 + 11, 1, 1, C.woodLine); }
    px(bx0, by0 + 15, bw, 2, 'rgba(20,40,60,0.45)');
    // 밀밭(울타리 안을 금빛 이삭 줄로)
    for (var hy = HAY.y0 + 3; hy < HAY.y1 - 1; hy += 5)
      for (var hx = HAY.x0 + 5; hx < HAY.x1 - 2; hx += 4) {
        px(hx, hy, 4, 5, '#c9952f'); px(hx, hy, 3, 4, '#e8bf4f'); px(hx + 1, hy, 1, 2, '#f6dc86');
      }
    PLOTS.forEach(function (p) { soil(p.x, p.y, false); });
  }
  function soil(x, y, wetSoil) {
    px(x, y, 32, 32, C.woodLine);
    px(x + 1, y + 1, 30, 30, wetSoil ? '#6a3f24' : '#85532f');
    for (var r = 0; r < 4; r++) { px(x + 1, y + 4 + r * 8, 30, 2, wetSoil ? '#55311c' : '#6e4426'); px(x + 1, y + 3 + r * 8, 30, 1, wetSoil ? '#7a4a2a' : '#9a6538'); }
  }
  // 밭 작물(3줄 × 4포기)
  function crops(x, y, crop) {
    for (var r = 0; r < 3; r++)
      for (var c = 0; c < 4; c++) {
        var cx = x + 5 + c * 7, cy = y + 8 + r * 9;
        if (crop === 'carrot') { px(cx, cy - 3, 1, 4, '#5fbf4a'); px(cx + 2, cy - 3, 1, 4, '#5fbf4a'); px(cx + 1, cy - 4, 1, 4, '#7fd85f'); px(cx, cy + 1, 3, 2, '#ff8a2a'); }
        else if (crop === 'cabbage') { px(cx - 1, cy - 2, 5, 5, '#2f6f37'); px(cx, cy - 2, 3, 4, '#8ed36c'); px(cx + 1, cy - 1, 1, 2, '#c6f0a6'); }
        else if (crop === 'wheat') { px(cx, cy - 5, 1, 7, '#c99b33'); px(cx + 2, cy - 4, 1, 6, '#c99b33'); px(cx - 1, cy - 6, 2, 2, '#ffd65c'); px(cx + 2, cy - 5, 2, 2, '#ffd65c'); }
        else if (crop === 'tomato') { px(cx, cy - 4, 3, 6, '#3f8f45'); px(cx - 1, cy - 2, 2, 2, '#ff4b3a'); px(cx + 3, cy, 1, 2, '#ff4b3a'); px(cx + 1, cy - 1, 1, 1, '#ff6b5a'); }
        else if (crop === 'pumpkin') { if ((r + c) % 2 === 0) { px(cx - 1, cy - 1, 5, 4, C.woodLine); px(cx, cy, 3, 2, '#ff9a2e'); px(cx + 1, cy - 2, 1, 1, '#3f8f45'); } else px(cx, cy - 1, 2, 2, '#5fbf4a'); }
        else { px(cx, cy - 2, 1, 3, '#7fd85f'); px(cx + 1, cy - 3, 1, 2, '#7fd85f'); px(cx - 1, cy - 1, 1, 1, '#5fbf4a'); }
      }
  }

  // ---------------------------------------------------------------- 움직이는 것
  // 물결: 강은 아래로 흐르고 호수는 옆으로 천천히 반짝인다. 폭포는 줄무늬가 떨어지고 밑에 물보라
  var waterTiles = [], fallTiles = [];
  for (var r1 = 0; r1 < WR; r1++)
    for (var c1 = 0; c1 < WC; c1++) {
      var t1 = tile(c1, r1);
      if (t1 === 'W') waterTiles.push(c1, r1, isRiver(c1, r1) ? 1 : 0);
      else if (t1 === 'F') fallTiles.push(c1, r1);
    }
  function drawWater(ctx, t, view) {
    for (var k = 0; k < waterTiles.length; k += 3) {
      var c = waterTiles[k], r = waterTiles[k + 1], x = c * T, y = r * T;
      if (x + T < view.x0 || x > view.x1 || y + T < view.y0 || y > view.y1) continue;
      ctx.fillStyle = C.waterLight;
      if (waterTiles[k + 2]) {
        var s = (t / 90 + hash(c, r) * 16) % 16;
        if (hash(c, r + 9) > 0.3) { ctx.fillRect(x + 3 + Math.floor(hash(c, r) * 6), y + Math.floor(s), 1, 3); ctx.fillRect(x + 5 + Math.floor(hash(c, r) * 6), y + Math.floor(s) + 1, 1, 2); }
        if (hash(c + 4, r) > 0.5) ctx.fillRect(x + 10, y + Math.floor((s + 8) % 16), 1, 3);
      } else {
        var m = Math.floor(t / 700 + hash(c, r) * 6) % 6;
        if (hash(c, r + 9) > 0.45) ctx.fillRect(x + 3 + m, y + 5 + (c % 3) * 3, 4, 1);
        if (hash(c + 3, r) > 0.65) { ctx.fillStyle = '#6aa3d8'; ctx.fillRect(x + 9 - m, y + 11, 3, 1); }
      }
    }
    for (var f = 0; f < fallTiles.length; f += 2) {
      var fx = fallTiles[f] * T, fy = fallTiles[f + 1] * T;
      ctx.fillStyle = C.foam;
      for (var i = 0; i < 4; i++) {
        var col = Math.floor(hash(fallTiles[f], i) * 14) + 1, off = (t / 40 + i * 5) % 16;
        ctx.fillRect(fx + col, fy + Math.floor(off), 1, 4);
      }
      for (var b = 0; b < 5; b++) {
        var bx = fx + Math.floor(hash(f, b) * 14), by = fy + 16 + Math.floor(((t / 120 + b * 3) % 6));
        ctx.fillStyle = b % 2 ? C.foam : C.waterLight;
        ctx.fillRect(bx, by, 2, 1);
      }
    }
  }
  // 나비 세 마리(마을 위)
  function butterflies(ctx, t) {
    [[X(6), Y(8), '#ffe36b', 0], [X(14), Y(6), '#ffb3c1', 2], [X(3), Y(3), '#ffffff', 4]].forEach(function (b) {
      var a = t / 2200 + b[3], x = Math.round(b[0] + Math.sin(a) * 30), y = Math.round(b[1] + Math.sin(a * 2.3) * 12);
      var open = Math.floor((t + b[3] * 100) / 150) % 2;
      ctx.fillStyle = C.ink; ctx.fillRect(x, y, 1, 2);
      ctx.fillStyle = b[2];
      if (open) { ctx.fillRect(x - 2, y - 1, 2, 2); ctx.fillRect(x + 1, y - 1, 2, 2); } else { ctx.fillRect(x - 1, y, 1, 2); ctx.fillRect(x + 1, y, 1, 2); }
    });
  }

  // ---------------------------------------------------------------- 병아리(앱 마스코트 그림)
  var SCRIPT_BASE = document.currentScript ? new URL('.', document.currentScript.src).href : '';
  var MASCOTS = ['original', 'knight', 'wizard', 'pirate', 'viking', 'ninja', 'king', 'astronaut', 'archer', 'healer', 'gamer', 'painter', 'designer', 'hatch'];
  var mascotImages = {};
  function mascot(name) {
    if (!mascotImages[name]) { var img = new Image(); img.src = SCRIPT_BASE + 'assets/mascot/chick-' + name + '.png'; mascotImages[name] = img; }
    return mascotImages[name];
  }
  var CAN = ['..kkk...', '.kbbbkk.', 'kbbbbbbk', 'kbbbbbk.', '.kkkkk..'];
  function nameSeed(s) { var h = 7; for (var i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0; return h >>> 0; }

  // 길 위 자리(세상 px): 집 앞 · 갈림길 · 다리 건너 · 동쪽 길 · 계단 위 · 남쪽 길. 갈래 자리는 큰길(PATH_ROW)의 갈림점을 거쳐 간다
  var ROW_Y = Y(6) + 9;
  var SPOTS = {
    door: { x: X(8) + 8, y: Y(5) + 6, via: X(8) + 8 },
    cross: { x: X(8) + 8, y: ROW_Y },
    west: { x: X(0) + 6, y: ROW_Y },
    east: { x: X(20) + 4, y: ROW_Y },
    hill: { x: X(19) + 8, y: Y(-2) + 8, via: X(19) + 8 },
    south: { x: X(8) + 8, y: Y(11) + 6, via: X(8) + 8 },
    lamp: { x: X(6) + 8, y: ROW_Y }
  };
  var SPOT_NAMES = ['door', 'west', 'east', 'hill', 'south', 'lamp', 'cross'];
  // 주민 한 마리의 하루(한 바퀴): 밭에서 물 주기 → 문 → 큰길 → 들를 곳 둘 → 다시 밭. 밭이 없으면 큰길과 들를 곳만
  function buildRoute(r, index) {
    var seed = nameSeed(r.name) + index * 97, pts = [];
    function go(x, y, pause, water) { pts.push({ x: x, y: y, pause: pause || 0, water: !!water }); }
    function toRow(x) { go(x, ROW_Y); }
    var visits = [];
    for (var k = 0; visits.length < 2 && k < 10; k++) {
      var s = SPOT_NAMES[Math.floor(hash(seed, k) * SPOT_NAMES.length)];
      if (visits.indexOf(s) < 0) visits.push(s);
    }
    if (r.plot) {
      var p = r.plot, fx = p.x + 16;
      go(fx - 8, p.frontY, 3.5, true);
      go(fx + 8, p.frontY, 3, true);
      go(p.gate.x, p.gate.inY);
      toRow(p.gate.x);
    } else go(SPOTS.cross.x, ROW_Y, 1);
    visits.forEach(function (name) {
      var s = SPOTS[name];
      if (s.via !== undefined) { toRow(s.via); go(s.x, s.y, 2.5 + hash(seed, s.x) * 2); toRow(s.via); }
      else go(s.x, s.y, 1.5 + hash(seed, s.y) * 2);
    });
    if (r.plot) { toRow(r.plot.gate.x); go(r.plot.gate.x, r.plot.gate.inY); }
    // 구간(걷기 + 멈춤)과 한 바퀴 길이
    var segs = [], total = 0, speed = 14;
    for (var i = 0; i < pts.length; i++) {
      var a = pts[i], b = pts[(i + 1) % pts.length], dur = Math.max(0.01, Math.hypot(b.x - a.x, b.y - a.y) / speed);
      segs.push({ a: a, b: b, walk: dur, pause: a.pause, start: total });
      total += a.pause + dur;
    }
    r.segs = segs;
    r.period = total;
    r.offset = hash(seed, 3) * total;
  }
  function residentPos(r, t) {
    var s = ((t / 1000 + r.offset) % r.period + r.period) % r.period;
    for (var i = 0; i < r.segs.length; i++) {
      var g2 = r.segs[i];
      if (s >= g2.start + g2.pause + g2.walk && i < r.segs.length - 1) continue;
      var into = s - g2.start;
      if (into < g2.pause) {
        var hop = !g2.a.water && Math.sin(t / 260 + r.offset) > 0.9 ? 1 : 0;
        return { x: g2.a.x, y: g2.a.y, bob: hop, watering: g2.a.water, left: g2.b.x < g2.a.x };
      }
      var f = Math.min(1, (into - g2.pause) / g2.walk);
      var e = f * f * (3 - 2 * f);
      return { x: g2.a.x + (g2.b.x - g2.a.x) * e, y: g2.a.y + (g2.b.y - g2.a.y) * e, bob: Math.floor(t / 160) % 2, watering: false, left: g2.b.x < g2.a.x };
    }
    return { x: r.segs[0].a.x, y: r.segs[0].a.y, bob: 0, watering: false, left: false };
  }
  function drawChick(ctx, r, p, t) {
    var img = r.img, x = Math.round(p.x), y = Math.round(p.y);
    ctx.fillStyle = 'rgba(20,45,20,0.35)';
    ctx.fillRect(x - 5, y - 1, 10, 2);
    if (!img.complete || !img.naturalWidth) return;
    var w = img.naturalWidth, h = img.naturalHeight, top = y - h - p.bob;
    if (p.left) { ctx.save(); ctx.translate(x * 2, 0); ctx.scale(-1, 1); }
    ctx.drawImage(img, x - Math.floor(w / 2), top);
    if (p.left) ctx.restore();
    if (p.watering) {
      var cx = x - Math.floor(w / 2) - 7, cy = top + 5;
      for (var rr = 0; rr < CAN.length; rr++)
        for (var cc = 0; cc < CAN[rr].length; cc++) {
          var k = CAN[rr][cc];
          if (k === '.') continue;
          ctx.fillStyle = k === 'k' ? C.ink : '#7fb8d8';
          ctx.fillRect(cx + cc, cy + rr, 1, 1);
        }
      var d = Math.floor(t / 120) % 4;
      ctx.fillStyle = '#bfe6ff';
      ctx.fillRect(cx - 1, cy + 4 + d, 1, 1); ctx.fillRect(cx - 3, cy + 3 + ((d + 2) % 4), 1, 1);
    }
  }

  // ---------------------------------------------------------------- 그려 두기
  var screen = document.getElementById('world');
  if (!screen) return;
  var namesHost = document.getElementById('world-names');
  var sctx = screen.getContext('2d');
  var ground = canvas(WW, WH), gctx = ground.getContext('2d');
  (function paintGround() {
    var img = gctx.createImageData(WW, WH), a = img.data;
    for (var y = 0; y < WH; y++)
      for (var x = 0; x < WW; x++) {
        var col = rgb(groundPixel(x, y)), i = (y * WW + x) * 4;
        a[i] = col[0]; a[i + 1] = col[1]; a[i + 2] = col[2]; a[i + 3] = 255;
      }
    gctx.putImageData(img, 0, 0);
    g = gctx;
    decorateGround();
  })();
  buildObjects();
  // 안개: 마을에서 멀수록 짙게(4px 덩어리로 살짝 얼룩지게) — 서 있는 것까지 다 그린 뒤 덮는다
  var fog = canvas(WW, WH);
  g = fog.getContext('2d');
  for (var fy = 0; fy < WH; fy += 4)
    for (var fx = 0; fx < WW; fx += 4) {
      var dd = distance(fx / T, fy / T);
      if (dd < 1.5) continue;
      var al = Math.pow(Math.min(1, (dd - 1.5) / 9), 0.8) * 0.78 + (hash(fx, fy) - 0.5) * 0.06;
      px(fx, fy, 4, 4, 'rgba(16,26,34,' + Math.max(0, al).toFixed(3) + ')');
    }

  var residents = [];
  var cam = { x: VX + W / 2, y: VY + H / 2, z: 3 }, goal = null, follow = -1;
  var vw = 0, vh = 0, dpr = 1;

  function minZoom() { return Math.max(vw / WW, vh / WH); }
  // 마을 전체 보기: 넓은 화면은 양옆 소개·주민 칸을 비켜서(전체 화면은 칸이 없으니 가운데 가득)
  function fitZoom() {
    var side = fullElement() ? 40 : vw > 900 ? 640 : 40;
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
    if (follow < 0) fit();
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
      var r = { name: name, plot: PLOTS[k] || null, crop: CROPS[k % CROPS.length], img: mascot(MASCOTS[nameSeed(name) % MASCOTS.length]) };
      buildRoute(r, k);
      return r;
    });
    // 밭: 주인 있는 밭은 물 준 흙에 작물, 나머지는 갈아만 둔 흙
    g = gctx;
    PLOTS.forEach(function (p, k) {
      var owner = residents[k];
      soil(p.x, p.y, !!owner);
      if (owner) crops(p.x, p.y, owner.crop);
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
    var at = residents.map(function (r) { return residentPos(r, t); });
    // 카메라: 따라가는 병아리 쪽으로(가까이), 또는 '전체 보기' 쪽으로 — 지수 감속으로 부드럽게(발 자리를 겨눠 콩콩 뛰어도 흔들리지 않게)
    var target = goal, rate = 4;
    if (follow >= 0 && at[follow]) {
      target = { x: at[follow].x, y: at[follow].y - 6, z: Math.max(cam.z, 5) };
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
    var hw = vw / 2 / cam.z, hh = vh / 2 / cam.z;
    var view = { x0: cam.x - hw - 2, x1: cam.x + hw + 2, y0: cam.y - hh - 2, y1: cam.y + hh + 2 };
    sctx.drawImage(ground, 0, 0);
    drawWater(sctx, t, view);
    // 서 있는 것과 병아리: 발 자리가 위인 것부터(같은 줄이면 병아리가 앞)
    var chicks = residents.map(function (r, i) { return { chick: r, at: at[i], base: at[i].y + 0.5 }; });
    chicks.sort(function (p, q) { return p.base - q.base; });
    var ci = 0;
    for (var i = 0; i < objects.length; i++) {
      var o = objects[i];
      while (ci < chicks.length && chicks[ci].base <= o.base) { drawChick(sctx, chicks[ci].chick, chicks[ci].at, t); ci++; }
      if (o.x + o.w < view.x0 || o.x > view.x1 || o.y + o.h < view.y0 || o.y > view.y1) continue;
      sctx.drawImage(o.img, o.x, o.y);
    }
    for (; ci < chicks.length; ci++) drawChick(sctx, chicks[ci].chick, chicks[ci].at, t);
    butterflies(sctx, t);
    sctx.drawImage(fog, 0, 0);
    // 이름표(HTML): 병아리 머리 위
    residents.forEach(function (r, i) {
      var q = at[i], h = r.img.naturalHeight || 13;
      r.label.style.transform = 'translate(' + ((q.x - cam.x) * cam.z + vw / 2) + 'px,' + ((q.y - h - 3 - q.bob - cam.y) * cam.z + vh / 2) + 'px) translate(-50%,-100%)';
      r.label.classList.toggle('active', i === follow);
    });
    if (!still) requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
  if (still) window.addEventListener('pointerup', function () { requestAnimationFrame(frame); });
})();
