// 병아리 후원 마을(village.html — 위 막대의 마을 단추로 온다).
// 네세스(Necesse)·팅커랜드(Tinkerlands)처럼 위에서 비스듬히 내려다본(3/4) 숲속 픽셀 마을: 서 있는 것은 윗면과 함께 남쪽 앞면이 보이고,
// 집은 지붕 없이 통나무 벽(벽 윗면 + 북쪽 벽의 안쪽 앞면 + 남쪽 벽의 바깥 앞면)과 나무 바닥·가구가 보인다. 북쪽 언덕(절벽)에서 강이
// 폭포로 떨어져 마을 서쪽을 지나 남서쪽 호수로 흐르고, 집 앞에서 돌길이 갈라진다. 울타리 친 밭마다 커피를 사 주고 이름을 남긴 사람의
// 병아리(앱 마스코트) 한 마리가 살며, 물을 주고 집에 들르고 마을 곳곳을 걸어 다닌다. 마을 밖은 아직 개척하지 않은 숲(멀수록 안개).
// 땅(그림자 포함)은 한 번 그려 두고, 나무·벽·가구·울타리·작물·키 큰 풀·병아리처럼 서 있는 것은 발 자리(아래 끝)가 위인 것부터 그려
// 앞뒤가 맞게 겹친다. 끌어서 옮기고 휠·두 손가락·단추로 확대하며, 주민 이름을 누르면 따라간다.
// 공개 주민 목록에는 인증된 후원자가 선택한 병아리와 작물도 들어온다.
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
  function draw(w, h, fn) { var cv = canvas(w, h); g = cv.getContext('2d'); fn(); return cv; }
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

  // 빛은 왼쪽 위에서, 그림자는 오른쪽 아래로. 색은 네세스·팅커랜드 화면에서 골라 맞춤(짙은 올리브 풀, 누런 키 큰 풀, 회색 돌길)
  var C = {
    grass: '#4a7d2b', grass2: '#467727', grassDark: '#3b6a22', grassShade: '#325c1c', grassLight: '#5e9234', grassRim: '#79ab3e',
    blade: '#8fae37', bladeTip: '#b9cc4e', bladeDark: '#5f8a2a',
    dirt: '#8a5a32', dirtDark: '#734a28', dirtLight: '#a06c3e', dirtShadow: '#5e3b1f',
    stone: '#8a8590', stone2: '#7f7a86', stoneLight: '#a5a0ab', stoneGap: '#5a5660', stoneEdge: '#6a6572',
    cliff: '#8a5532', cliffDark: '#6e4126', cliffStreak: '#7a492b', cliffLight: '#a5683b', cliffFoot: '#5a321c', cliffLine: '#3a2112',
    water: '#3f78b8', water2: '#3b71ae', waterDark: '#2f5f99', waterLight: '#6ea7dc', foam: '#e3f4ff', fall: '#5f9bd4', fallLight: '#a0d0f2',
    wood: '#9a6634', woodDark: '#6e4422', woodLight: '#c08a4a', woodLine: '#2e1d0e',
    ink: '#1c2a14', shadow: 'rgba(12,28,8,0.32)'
  };

  // ---------------------------------------------------------------- 지도(타일 종류)
  // G 풀 · P 길(마을 안은 돌길, 밖은 흙길) · W 물 · C 절벽 · F 폭포 · S 돌계단 · B 다리(물 위)
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
    if (c === PATH_COL && r >= VR + 6 && r <= VR + 18) return true;
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
  function inVillage(c, r) { return c >= VC && c < VC + COLS && r >= VR && r < VR + ROWS; }
  // 풀이 길·물로 삐죽 넘어오는 길이(2~4px, 2px마다 다름)
  function jag(u, seed) { return 2 + Math.floor(hash(u >> 1, seed) * 3); }

  // ---------------------------------------------------------------- 땅(픽셀 단위로 한 번)
  function groundPixel(x, y) {
    var c = x >> 4, r = y >> 4, lx = x & 15, ly = y & 15, t = tile(c, r);
    var up = tile(c, r - 1), down = tile(c, r + 1), left = tile(c - 1, r), right = tile(c + 1, r);
    if (t === 'P') {
      var d = distance(c, r);
      // 마을 밖 길은 멀수록 풀에 덮인다(4px 덩어리)
      if (d > 0.5 && hash(x >> 2, y >> 2) > 1 - d * 0.16) return grassPixel(x, y, lx, ly, up);
      var e = edgeGrass(x, y, lx, ly, up, down, left, right, function (n) { return !walk(n) && n !== 'B'; });
      if (e) return e;
      return inVillage(c, r) ? cobble(x, y) : dirtPixel(x, y);
    }
    if (t === 'W' || t === 'B') {
      var e2 = edgeWater(x, y, lx, ly, up, down, left, right);
      if (e2) return e2;
      return smooth(x / 16, y / 16, 3, 2) > 0.6 ? C.water2 : C.water;
    }
    if (t === 'C') {
      var j = jag(x, 91);
      if (ly < j - 1) return C.grass;
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
      return s === 0 ? C.stoneLight : s === 3 ? C.stoneEdge : C.stone;
    }
    return grassPixel(x, y, lx, ly, up);
  }
  function grassPixel(x, y, lx, ly, up) {
    // 절벽 바로 아래는 그늘
    if ((up === 'C' || up === 'S') && ly < 4) return ly < 2 ? C.grassShade : C.grassDark;
    var n = hash(x >> 1, y >> 1);
    if (n > 0.96) return C.grassLight;
    if (n < 0.05) return C.grassDark;
    var p = smooth(x / 16, y / 16, 2.5, 7);
    return p > 0.62 ? C.grass2 : p < 0.3 ? '#4d822d' : C.grass;
  }
  function dirtPixel(x, y) {
    var n = hash(x >> 1, y >> 1);
    return n > 0.93 ? C.dirtLight : n < 0.08 ? C.dirtDark : C.dirt;
  }
  // 돌길: 엇갈려 깐 넓적한 돌(8×5), 돌 사이 틈, 돌마다 윗줄이 밝고 아랫줄이 어둡다
  function cobble(x, y) {
    var row = Math.floor(y / 5), off = (row % 2) * 4 + Math.floor(hash(row, 3) * 3), col = Math.floor((x + off) / 8);
    var sx = (x + off) % 8, sy = y % 5;
    if (sx === 0 || sy === 0) return C.stoneGap;
    var h = hash(col, row);
    if (sy === 1 && sx > 1 && sx < 7) return C.stoneLight;
    if (sy === 4) return C.stoneEdge;
    return h > 0.6 ? C.stone2 : h < 0.15 ? '#96919c' : C.stone;
  }
  // 길 칸: 풀 이웃 쪽 가장자리에 삐죽한 풀 테두리(위쪽 풀이면 그 아래 길에 그늘)
  function edgeGrass(x, y, lx, ly, up, down, left, right, grassy) {
    var j;
    if (grassy(up)) { j = jag(x, y >> 4); if (ly < j - 1) return C.grass; if (ly === j - 1) return C.grassRim; if (ly === j) return C.grassShade; if (ly === j + 1) return C.stoneGap; }
    if (grassy(down)) { j = jag(x, (y >> 4) + 50); var d = 15 - ly; if (d < j - 1) return C.grass; if (d === j - 1) return C.grassRim; if (d === j) return C.grassShade; }
    if (grassy(left)) { j = jag(y, (x >> 4) + 90); if (lx < j - 1) return C.grass; if (lx === j - 1) return C.grassRim; if (lx === j) return C.grassShade; }
    if (grassy(right)) { j = jag(y, (x >> 4) + 130); var d2 = 15 - lx; if (d2 < j - 1) return C.grass; if (d2 === j - 1) return C.grassRim; if (d2 === j) return C.grassShade; }
    return null;
  }
  // 물 칸: 땅 이웃 쪽에 풀 테두리와 흙 둑(앞면), 짙은 물 그늘(위쪽 땅이면 둑이 보이고 그늘이 넓다)
  function edgeWater(x, y, lx, ly, up, down, left, right) {
    var j;
    if (!wet(up)) {
      j = jag(x, (y >> 4) + 7);
      if (ly < j - 1) return C.grass; if (ly === j - 1) return C.grassRim;
      if (ly < j + 3) return ly === j ? C.cliffLine : C.cliffDark;
      if (ly === j + 3) return C.cliffLine;
      if (ly < j + 7) return C.waterDark;
    }
    if (up === 'F' && ly < 4) return hash(x >> 1, y) > 0.4 ? C.foam : C.waterLight;
    if (!wet(left)) { j = jag(y, (x >> 4) + 11); if (lx < j - 2) return C.grass; if (lx === j - 2) return C.ink; if (lx < j + 1) return C.waterDark; }
    if (!wet(right)) { j = jag(y, (x >> 4) + 13); var d = 15 - lx; if (d < j - 2) return C.grass; if (d === j - 2) return C.ink; if (d < j + 1) return C.waterDark; }
    if (!wet(down)) { j = jag(x, (y >> 4) + 17); var d2 = 15 - ly; if (d2 < j - 2) return C.grass; if (d2 === j - 2) return C.ink; }
    return null;
  }

  // ---------------------------------------------------------------- 서 있는 것(발 자리 = 아래 끝 기준으로 앞뒤 정렬)
  var objects = [];
  // 그림 한 장(작은 캔버스)을 세상 (x, y)(왼쪽 위)에 두고, 발 자리 base(세상 y)로 정렬
  function place(img, x, y, base, list) { (list || objects).push({ img: img, x: Math.round(x), y: Math.round(y), base: base, w: img.width, h: img.height }); }
  // 땅에 깔리는 그림자(오른쪽 아래로 조금 밀림) — 땅 그림에 바로 칠한다
  var shadows = [];
  function shadowEllipse(cx, cy, rx, ry) { shadows.push(function (ctx) { ctx.beginPath(); ctx.ellipse(cx + 3, cy + 1, rx, ry, 0, 0, Math.PI * 2); ctx.fill(); }); }
  function shadowRect(x, y, w, h) { shadows.push(function (ctx) { ctx.fillRect(x, y, w, h); }); }
  // 그린 모양 둘레에 1px 테두리
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

  // 잎 뭉치 수관: 모양(타원 + 둘레 혹) 안을 작은 잎 뭉치(반지름 ~3)로 위에서 아래로 채운다. 뭉치마다 왼쪽 위가 밝고 오른쪽 아래가 어둡고,
  // 수관 전체로는 위·왼쪽이 밝고 아래·오른쪽이 어둡다(네세스 나무처럼 오돌토돌한 잎 덩어리)
  function canopy(w, h, cx, cy, rx, ry, pal, seed, bumps) {
    var mask = new Uint8Array(w * h), bs = [];
    for (var k = 0; k < bumps; k++) {
      var a = (k / bumps) * Math.PI * 2 + seed * 0.7;
      bs.push([cx + Math.cos(a) * rx * 0.86, cy + Math.sin(a) * ry * 0.86, Math.min(rx, ry) * (0.3 + hash(seed, k) * 0.1)]);
    }
    for (var y = 0; y < h; y++)
      for (var x = 0; x < w; x++) {
        var dx = (x + 0.5 - cx) / rx, dy = (y + 0.5 - cy) / ry, inside = dx * dx + dy * dy <= 1;
        for (var b = 0; !inside && b < bs.length; b++) inside = Math.hypot(x + 0.5 - bs[b][0], y + 0.5 - bs[b][1]) <= bs[b][2];
        if (inside) mask[y * w + x] = 1;
      }
    function level(x, y) {
      var tv = (y - (cy - ry)) / (2 * ry), sv = (x - cx) / rx;
      var s = 0.62 - tv * 0.9 - sv * 0.25 + (hash(x + seed, y) - 0.5) * 0.12;
      return s > 0.42 ? 3 : s > 0.12 ? 2 : s > -0.2 ? 1 : 0;
    }
    for (var y2 = 0; y2 < h; y2++)
      for (var x2 = 0; x2 < w; x2++) if (mask[y2 * w + x2]) px(x2, y2, 1, 1, pal[Math.max(0, level(x2, y2) - 1)]);
    var cl = [];
    for (var qy = cy - ry - 2; qy < cy + ry + 3; qy += 4)
      for (var qx = cx - rx - 2; qx < cx + rx + 3; qx += 4) {
        var jx = qx + (hash(qx + seed, qy) - 0.5) * 3, jy = qy + (hash(qy, qx + seed) - 0.5) * 3;
        var ix = Math.round(jx), iy = Math.round(jy);
        if (ix >= 0 && iy >= 0 && ix < w && iy < h && mask[iy * w + ix]) cl.push([jx, jy]);
      }
    cl.sort(function (p, q) { return p[1] - q[1]; });
    cl.forEach(function (q) {
      var L = level(q[0], q[1]), rad = 2.7;
      for (var yy = Math.floor(q[1] - rad); yy <= q[1] + rad; yy++)
        for (var xx = Math.floor(q[0] - rad); xx <= q[0] + rad; xx++) {
          if (xx < 0 || yy < 0 || xx >= w || yy >= h || !mask[yy * w + xx]) continue;
          var ddx = xx + 0.5 - q[0], ddy = yy + 0.5 - q[1];
          if (ddx * ddx + ddy * ddy > rad * rad) continue;
          var loc = (-ddx * 0.6 - ddy * 0.8) / rad;
          var lv = loc > 0.5 ? L + 1 : loc < -0.45 ? L - 1 : L;
          px(xx, yy, 1, 1, pal[Math.max(0, Math.min(4, lv))]);
        }
    });
    // 아래쪽 가장자리는 한 겹 더 어둡게(수관 아래 그늘)
    for (var y3 = 1; y3 < h; y3++)
      for (var x3 = 0; x3 < w; x3++)
        if (mask[(y3 - 1) * w + x3] && !mask[y3 * w + x3]) px(x3, y3 - 1, 1, 1, pal[0]);
  }
  var OAK = ['#1f4419', '#2c5d20', '#3f7d28', '#5a9c33', '#7fbf45'];
  var OAK_DARK = ['#1b3c17', '#27531d', '#356f25', '#4c8a2e', '#6aa83b'];
  var POPLAR = ['#2a5a1c', '#3d7d24', '#57a02e', '#79c03a', '#a6dc52'];
  var AUTUMN = ['#7a3a12', '#a85418', '#d17a22', '#eca23a', '#ffd05e'];
  var treeCache = {};
  // 활엽수: 굵은 줄기와 땅으로 뻗은 뿌리 위에 큰 잎 뭉치 수관
  function oak(rad, seed) {
    var key = 'o' + rad + '-' + (seed % 5);
    if (treeCache[key]) return treeCache[key];
    var rx = rad, ry = Math.round(rad * 0.86), w = rx * 2 + 8, top = 3, cy = top + ry, foot = cy + ry + 9, h = foot + 2, cx = w / 2;
    var pal = seed % 4 === 3 ? OAK_DARK : OAK;
    var cv = draw(w, h, function () {
      // 뿌리 · 줄기(수관 아래로 보이는 부분)
      var tx = Math.round(cx);
      px(tx - 4, cy + ry - 6, 8, foot - (cy + ry - 6), C.woodLine);
      px(tx - 3, cy + ry - 6, 6, foot - (cy + ry - 6) - 1, '#5a3a20'); px(tx - 3, cy + ry - 6, 2, foot - (cy + ry - 6) - 1, '#7a5230');
      px(tx - 8, foot - 3, 5, 2, C.woodLine); px(tx - 7, foot - 3, 3, 1, '#5a3a20');
      px(tx + 3, foot - 3, 5, 2, C.woodLine); px(tx + 4, foot - 3, 3, 1, '#4a2e18');
      px(tx - 1, foot - 1, 3, 1, C.woodLine);
      canopy(w, cy + ry + 3, cx, cy, rx, ry, pal, seed, 9);
    });
    outline(cv, '#10240c');
    return (treeCache[key] = { img: cv, foot: foot, w: w });
  }
  // 포플러(팅커랜드의 길쭉한 나무): 키 큰 타원 수관, 가는 줄기
  function poplar(size, seed, pal) {
    var key = 'q' + size + '-' + (seed % 4) + (pal === AUTUMN ? 'a' : '');
    if (treeCache[key]) return treeCache[key];
    var rx = size, ry = Math.round(size * 1.9), w = rx * 2 + 6, cy = ry + 2, foot = cy + ry + 6, h = foot + 2, cx = w / 2;
    var cv = draw(w, h, function () {
      var tx = Math.round(cx);
      px(tx - 2, cy + ry - 4, 4, foot - (cy + ry - 4), C.woodLine); px(tx - 1, cy + ry - 4, 2, foot - (cy + ry - 4) - 1, '#6a4426');
      canopy(w, cy + ry + 2, cx, cy, rx, ry, pal || POPLAR, seed, 7);
    });
    outline(cv, '#10240c');
    return (treeCache[key] = { img: cv, foot: foot, w: w });
  }
  // 전나무: 톱니 같은 층 넷, 층마다 잎 결
  var PINE = ['#12301e', '#1d4a2c', '#2b6a3c', '#3f8a4a', '#62ad5c'];
  function pine(size, seed) {
    var key = 'p' + size + '-' + (seed % 4);
    if (treeCache[key]) return treeCache[key];
    var w = size * 2 + 6, tierH = Math.round(size * 0.95), tiers = 4, step = Math.round(size * 0.62);
    var foot = tierH + step * (tiers - 1) + 8, h = foot + 2, cx = Math.floor(w / 2);
    var cv = draw(w, h, function () {
      px(cx - 2, foot - 7, 4, 7, C.woodLine); px(cx - 1, foot - 7, 2, 6, '#5a3a20');
      for (var k = 0; k < tiers; k++) {
        var bottom = foot - 5 - k * step, half = Math.round(size * (1 - k * 0.2));
        for (var y = 0; y < tierH; y++) {
          var hw = Math.round(half * (y + 1) / tierH), yy = bottom - tierH + y;
          for (var x = -hw; x <= hw; x++) {
            if (y === tierH - 1 && ((x + k) & 3) === 0) continue;
            var s = -x / (half + 1) * 0.8 - (y / tierH) * 0.5 + 0.35 + (hash(x + seed, yy) - 0.5) * 0.3;
            var lv = s > 0.55 ? 4 : s > 0.2 ? 3 : s > -0.15 ? 2 : s > -0.45 ? 1 : 0;
            if ((x + yy) % 3 === 0 && lv > 1 && y > 1) lv--;
            px(cx + x, yy, 1, 1, PINE[lv]);
          }
        }
      }
    });
    outline(cv, '#0a1f12');
    return (treeCache[key] = { img: cv, foot: foot, w: w });
  }
  function addTree(t, x, foot) {
    place(t.img, x - t.img.width / 2, foot - t.foot, foot);
    shadowEllipse(x, foot - 1, t.w * 0.42, 4);
  }
  function addOak(x, foot, rad, seed) { addTree(oak(rad, seed), x, foot); }
  function addPine(x, foot, size, seed) { addTree(pine(size, seed), x, foot); }
  function addPoplar(x, foot, size, seed, pal) { addTree(poplar(size, seed, pal), x, foot); }
  // 덤불(열매 달린 것도): 낮게 뭉친 잎
  function addBush(x, foot, seed, berries) {
    var key = 'b' + (seed % 3) + (berries ? berries : '');
    var b = treeCache[key];
    if (!b) {
      var cv = draw(18, 14, function () {
        canopy(18, 14, 9, 7, 8, 6, OAK, seed, 6);
        if (berries) [[5, 6], [10, 4], [12, 8], [7, 9]].forEach(function (p) { px(p[0], p[1], 2, 2, berries); px(p[0], p[1], 1, 1, '#ffffff'); });
      });
      outline(cv, '#10240c');
      b = treeCache[key] = { img: cv };
    }
    place(b.img, x - 9, foot - 13, foot);
    shadowEllipse(x, foot - 1, 8, 3);
  }
  // 키 큰 풀(네세스의 누런 풀 무더기): 잎 서너 가닥이 위로 솟고 끝이 휜다
  var tallGrass = [];
  for (var tg = 0; tg < 6; tg++) {
    tallGrass.push(draw(9, 12, function () {
      for (var k = 0; k < 4; k++) {
        var bx = 1 + k * 2, hgt = 6 + Math.floor(hash(tg, k) * 5), bend = hash(k, tg) > 0.5 ? 1 : -1;
        for (var y = 0; y < hgt; y++) {
          var xx = bx + (y > hgt - 3 ? bend : 0);
          px(xx, 11 - y, 1, 1, y > hgt - 3 ? C.bladeTip : k % 2 ? C.bladeDark : C.blade);
        }
      }
    }));
  }
  function addTallGrass(x, foot, seed) { place(tallGrass[seed % tallGrass.length], x - 4, foot - 11, foot); }
  // 돌(3/4): 윗면이 밝고 남쪽 앞면이 어둡다
  function addRock(x, foot, big) {
    var w = big ? 18 : 11, h = big ? 13 : 8;
    var cv = draw(w, h, function () {
      for (var y = 0; y < h; y++)
        for (var xx = 0; xx < w; xx++) {
          var dx = (xx + 0.5 - w / 2) / (w / 2), dy = (y + 0.5 - h * 0.55) / (h * 0.55);
          if (dx * dx + dy * dy > 1) continue;
          var front = y > h * 0.55, s = -dx * 0.5 - dy * 0.6 + (hash(xx + x, y) - 0.5) * 0.2;
          px(xx, y, 1, 1, front ? (s > -0.3 ? '#6f7480' : '#575c67') : s > 0.45 ? '#c3c8d0' : s > 0 ? '#a2a7b1' : '#8a8f99');
        }
      if (big) { px(w * 0.55, 2, 1, 4, '#6f7480'); px(3, 2, 4, 2, '#5f9a3a'); px(4, 1, 2, 1, '#79b54a'); }
    });
    outline(cv, '#2a2d34');
    place(cv, x - w / 2, foot - h, foot);
    shadowEllipse(x, foot - 1, w * 0.5, 2.5);
  }
  function addStump(x, foot) {
    var cv = draw(14, 12, function () {
      px(1, 4, 12, 7, '#5a3a20'); px(1, 4, 3, 7, '#7a5230'); px(10, 4, 3, 7, '#4a2e18');
      px(0, 9, 3, 2, '#5a3a20'); px(11, 9, 3, 2, '#4a2e18');
      px(1, 1, 12, 4, '#c8955c'); px(3, 2, 8, 2, '#a8703f'); px(5, 2, 4, 1, '#c8955c');
    });
    outline(cv, C.woodLine);
    place(cv, x - 7, foot - 11, foot);
    shadowEllipse(x, foot - 1, 7, 2.5);
  }
  // 해바라기(네세스 마을 꽃): 긴 줄기와 잎, 노란 꽃잎과 갈색 씨
  function addSunflower(x, foot) {
    var cv = draw(11, 20, function () {
      px(5, 8, 1, 12, '#3f7a28'); px(2, 12, 3, 2, '#4f8f2c'); px(6, 14, 3, 2, '#4f8f2c');
      [[4, 0], [3, 1], [2, 3], [3, 5], [4, 6], [6, 6], [7, 5], [8, 3], [7, 1], [6, 0]].forEach(function (p) { px(p[0], p[1], 2, 2, '#f2c12e'); });
      px(4, 2, 3, 4, '#6a3a12'); px(4, 2, 1, 1, '#8a5420');
    });
    outline(cv, '#2a1c08');
    place(cv, x - 5, foot - 20, foot);
  }
  function addLupin(x, foot, color) {
    var cv = draw(5, 11, function () {
      px(2, 4, 1, 7, '#3f7a28');
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
  // 쇠 가로등(네세스): 검은 기둥 위 등불, 둘레에 따뜻한 빛
  function addLamp(x, foot) {
    var cv = draw(26, 32, function () {
      g.fillStyle = 'rgba(255,190,90,0.16)'; g.beginPath(); g.arc(13, 8, 12, 0, Math.PI * 2); g.fill();
      px(11, 12, 4, 17, '#22252c'); px(12, 12, 1, 17, '#4a4f5a');
      px(9, 28, 8, 3, '#22252c'); px(10, 28, 6, 1, '#5a606c');
      px(9, 2, 8, 10, '#22252c'); px(10, 4, 6, 6, '#ffb84a'); px(11, 5, 3, 3, '#fff0b8');
      px(8, 1, 10, 2, '#2c3038'); px(10, 0, 6, 1, '#4a4f5a'); px(12, 12, 2, 1, '#ffb84a');
    });
    place(cv, x - 13, foot - 31, foot);
    shadowEllipse(x, foot - 1, 4, 2);
  }
  function addSign(x, foot) {
    var cv = draw(26, 24, function () {
      px(11, 4, 4, 18, C.woodLine); px(12, 4, 2, 18, '#8a5530');
      px(4, 3, 18, 7, C.woodLine); px(5, 4, 15, 5, C.woodLight); px(20, 5, 2, 3, C.woodLight); px(22, 6, 1, 1, C.woodLine); px(6, 6, 12, 1, '#a8703f'); px(5, 8, 15, 1, C.woodDark);
      px(2, 11, 18, 7, C.woodLine); px(5, 12, 14, 5, C.wood); px(3, 13, 2, 3, C.wood); px(2, 14, 1, 1, C.woodLine); px(7, 14, 11, 1, '#8a5530'); px(5, 16, 14, 1, C.woodDark);
    });
    place(cv, x - 13, foot - 22, foot);
    shadowEllipse(x, foot - 1, 4, 2);
  }
  // 상자·통(3/4): 윗면 밝게, 앞면 어둡게
  function crateImg(produce) {
    return draw(16, 15, function () {
      px(0, 2, 16, 13, C.woodLine);
      px(1, 3, 14, 4, C.woodLight); px(1, 7, 14, 7, C.woodDark); px(1, 7, 14, 1, '#5a361a'); px(1, 10, 14, 1, '#5a361a'); px(4, 8, 1, 6, '#5a361a'); px(11, 8, 1, 6, '#5a361a');
      if (produce) for (var k = 0; k < 5; k++) { px(2 + k * 2.6, 1 + (k % 2), 3, 3, produce); px(2 + k * 2.6, 1 + (k % 2), 1, 1, '#fff6d6'); }
    });
  }
  function addCrate(x, foot, produce) { place(crateImg(produce), x - 8, foot - 15, foot); shadowRect(x - 6, foot - 2, 16, 3); }
  function addBarrel(x, foot) {
    var cv = draw(12, 17, function () {
      px(0, 3, 12, 14, C.woodLine); px(1, 4, 10, 12, '#7a4c26'); px(1, 4, 3, 12, '#9a6234'); px(9, 4, 2, 12, '#5a361a');
      px(1, 6, 10, 1, '#3e424c'); px(1, 13, 10, 1, '#3e424c');
      px(0, 0, 12, 5, C.woodLine); px(1, 1, 10, 3, '#b07a44'); px(3, 2, 6, 1, '#8a5a30');
    });
    place(cv, x - 6, foot - 17, foot);
    shadowEllipse(x, foot - 1, 6, 2);
  }
  function addLog(x, foot, len) {
    var cv = draw(len + 2, 12, function () {
      px(0, 1, len + 2, 10, C.woodLine); px(1, 2, len, 4, '#9a5a30'); px(1, 2, len, 1, '#b06a3a'); px(1, 6, len, 4, '#6e3f22');
      for (var k = 6; k < len - 4; k += 9) px(1 + k, 3, 4, 1, '#7a4426');
      px(len - 4, 2, 5, 8, '#c8955c'); px(len - 3, 4, 3, 3, '#a8703f');
    });
    place(cv, x, foot - 11, foot);
    shadowRect(x + 2, foot - 1, len, 3);
  }
  // 울타리(네세스): 굵은 기둥(앞면이 보이는 높이 13px, 윗면 밝게) 사이에 가로대 둘. 세로 줄은 기둥이 촘촘하고 가로대가 세로로 보인다
  function post(x, y) { px(x, y, 5, 13, C.woodLine); px(x + 1, y + 1, 3, 11, '#7a4c26'); px(x + 1, y + 1, 3, 2, '#c8955c'); px(x + 1, y + 3, 1, 9, '#9a6234'); }
  function fenceH(x, y, len, gap) {
    var cv = draw(len + 5, 14, function () {
      for (var s = 0; s < len; s++) {
        if (gap && s > gap[0] && s < gap[1]) continue;
        px(s + 2, 3, 1, 3, C.woodLine); px(s + 2, 4, 1, 1, '#9a6234');
        px(s + 2, 8, 1, 3, C.woodLine); px(s + 2, 9, 1, 1, '#9a6234');
      }
      for (var k = 0; k <= len; k += 10) { if (gap && k > gap[0] - 4 && k < gap[1]) continue; post(k, 0); }
      if (gap) { post(gap[0] - 3, 0); post(gap[1], 0); }
    });
    place(cv, x - 2, y - 12, y);
    shadowRect(x, y - 1, len + 4, 3);
  }
  function fenceV(x, y, len) {
    var cv = draw(7, len + 14, function () {
      px(2, 2, 3, len + 4, C.woodLine); px(3, 2, 1, len + 4, '#9a6234');
      for (var k = 0; k <= len; k += 10) post(1, k);
    });
    place(cv, x - 3, y - 12, y + len);
    shadowRect(x + 2, y, 4, len);
  }
  function fenceBox(x0, y0, x1, y1, gapTop, gapBottom) {
    fenceH(x0, y0, x1 - x0, gapTop);
    fenceV(x0 + 2, y0 + 2, y1 - y0 - 2);
    fenceV(x1 + 2, y0 + 2, y1 - y0 - 2);
    fenceH(x0, y1, x1 - x0, gapBottom);
  }

  // ---------------------------------------------------------------- 집(네세스처럼 지붕 없는 통나무 집)
  // 벽 윗면(8px: 통나무 결 + 금빛 테두리), 북쪽 벽은 안쪽 앞면(14px 세로 통나무)이 보이고, 남쪽 벽은 바깥 앞면이 보인다. 바닥은 세로 판자.
  var WALL = 8, FACE = 14;
  function wallTop(w, h, vertical) {
    return draw(w, h, function () {
      px(0, 0, w, h, '#1e1208');
      px(1, 1, w - 2, h - 2, '#c08a32');
      px(2, 2, w - 4, h - 4, '#4a2a12');
      if (vertical) for (var x = 3; x < w - 2; x += 2) px(x, 2, 1, h - 4, '#5e3818');
      else for (var y = 3; y < h - 2; y += 2) px(2, y, w - 4, 1, '#5e3818');
    });
  }
  function wallFace(w, door) {
    return draw(w, FACE, function () {
      for (var x = 0; x < w; x++) {
        if (door && x >= door[0] && x < door[1]) continue;
        var k = x % 4;
        px(x, 0, 1, FACE, k === 0 ? '#2a170a' : k === 1 ? '#7a4a20' : '#5a3416');
        if (k !== 0 && hash(x >> 2, 3) > 0.5) px(x, 5 + ((x >> 2) % 3) * 2, 1, 2, '#3e220e');
      }
      px(0, 0, w, 2, '#2a170a');
      px(0, FACE - 1, w, 1, '#1e1208');
      if (door) {
        px(door[0] - 2, 0, 2, FACE, '#1e1208'); px(door[1], 0, 2, FACE, '#1e1208');
      }
    });
  }
  var HOUSE = { x0: VX + 5 * T, x1: VX + 12 * T, y0: VY + 1 * T + 2, y1: VY + 5 * T + 14 };
  function addHouse() {
    var x0 = HOUSE.x0, x1 = HOUSE.x1, y0 = HOUSE.y0, y1 = HOUSE.y1, w = x1 - x0;
    var doorX = (x0 + x1) / 2 - 8, door = [doorX - x0, doorX - x0 + 16];
    // 북쪽 벽: 윗면 + 안쪽 앞면(바닥 위에 서 있으니 앞면 아래 끝이 발 자리)
    place(wallTop(w, WALL, false), x0, y0, y0 + WALL);
    place(wallFace(w - 2 * WALL, null), x0 + WALL, y0 + WALL, y0 + WALL + FACE);
    // 서·동 벽 윗면(남쪽 벽 윗면까지)
    var sideH = y1 - FACE - WALL - y0;
    place(wallTop(WALL, sideH, true), x0, y0 + WALL, y1 - FACE - WALL + 1);
    place(wallTop(WALL, sideH, true), x1 - WALL, y0 + WALL, y1 - FACE - WALL + 1);
    // 남쪽 벽: 윗면 + 바깥 앞면(문 자리는 비움)
    var southL = wallTop(door[0], WALL, false), southR = wallTop(w - door[1], WALL, false), southFace = wallFace(w, door);
    var south = draw(w, WALL + FACE, function () {
      g.drawImage(southL, 0, 0);
      g.drawImage(southR, door[1], 0);
      g.drawImage(southFace, 0, WALL);
    });
    place(south, x0, y1 - FACE - WALL, y1);
    // 문(열린 문짝이 문틀 옆 벽에 붙어 있음)과 문 앞 깔개
    var leaf = draw(6, FACE + 2, function () { px(0, 0, 6, FACE + 2, C.woodLine); px(1, 1, 4, FACE, '#8a5a2c'); px(1, 1, 1, FACE, '#b07a44'); px(3, 8, 1, 2, '#ffd23f'); });
    place(leaf, x0 + door[1] + 2, y1 - FACE - 2, y1 + 0.5);
    // 바깥 그림자(동쪽 벽 옆, 남쪽 벽 아래)
    shadowRect(x1, y0 + 6, 6, y1 - y0 - 4);
    shadowRect(x0 + 4, y1, w + 2, 4);
    // 가구(3/4): 침대 · 탁자와 의자 · 난로 · 책장 · 상자 · 화분 · 촛대
    var inX0 = x0 + WALL, inY0 = y0 + WALL + FACE;
    furniture(bed(), inX0 + 4, inY0 - 6);
    furniture(bookshelf(), inX0 + 26, y0 + WALL + 1);
    furniture(stove(), x1 - WALL - 22, y0 + WALL + 2);
    furniture(chest(), x1 - WALL - 18, inY0 + 22);
    furniture(table(), inX0 + 40, inY0 + 10);
    furniture(chair(false), inX0 + 44, inY0 + 2);
    furniture(chair(false), inX0 + 54, inY0 + 2);
    furniture(chair(true), inX0 + 44, inY0 + 22);
    furniture(plant(), inX0 + 2, inY0 + 26);
    furniture(candle(), x1 - WALL - 34, y0 + WALL + 4);
  }
  function furniture(img, x, y) { place(img, x, y, y + img.height); }
  function bed() {
    return draw(16, 26, function () {
      px(0, 0, 16, 26, C.woodLine);
      px(1, 1, 14, 5, '#9a6234'); px(1, 1, 14, 1, '#c08a4a');
      px(2, 6, 12, 5, '#efe6d2'); px(2, 10, 12, 1, '#c9bfa8');
      px(1, 11, 14, 11, '#b8363a'); px(1, 11, 14, 2, '#d8545a'); px(1, 17, 14, 1, '#8a2428');
      px(1, 22, 14, 3, '#7a4c26'); px(1, 22, 14, 1, '#9a6234');
    });
  }
  function table() {
    return draw(24, 14, function () {
      px(0, 0, 24, 10, C.woodLine); px(1, 1, 22, 6, '#c8955c'); px(2, 2, 10, 1, '#e0b07a'); px(1, 7, 22, 2, '#7a4c26');
      px(2, 10, 2, 4, C.woodLine); px(20, 10, 2, 4, C.woodLine);
      px(8, 2, 4, 3, '#efe6d2'); px(9, 3, 2, 1, '#d8463a'); px(15, 2, 3, 3, '#7ec3ea');
    });
  }
  function chair(back) {
    return draw(8, 12, function () {
      if (back) { px(0, 0, 8, 12, C.woodLine); px(1, 1, 6, 5, '#9a6234'); px(1, 6, 6, 3, '#c8955c'); px(1, 9, 1, 3, C.woodLine); px(6, 9, 1, 3, C.woodLine); }
      else { px(0, 4, 8, 8, C.woodLine); px(1, 5, 6, 3, '#c8955c'); px(1, 8, 6, 2, '#7a4c26'); px(1, 10, 1, 2, C.woodLine); px(6, 10, 1, 2, C.woodLine); }
    });
  }
  function stove() {
    return draw(18, 20, function () {
      px(12, 0, 3, 8, '#22252c'); px(13, 0, 1, 8, '#4a4f5a');
      px(0, 6, 18, 14, '#1a1c22'); px(1, 7, 16, 4, '#4a4f5a'); px(1, 11, 16, 8, '#2c3038');
      px(5, 13, 8, 4, '#1a1c22'); px(6, 14, 6, 2, '#ff7a2a'); px(7, 14, 3, 1, '#ffd35c');
    });
  }
  function bookshelf() {
    return draw(18, 20, function () {
      px(0, 0, 18, 20, C.woodLine); px(1, 1, 16, 18, '#6e4422');
      var colors = ['#b8363a', '#3f78b8', '#4f8f2c', '#e0a030', '#8a4ab0', '#efe6d2'];
      [2, 9].forEach(function (sy, row) {
        for (var k = 0; k < 7; k++) px(2 + k * 2, sy + (k % 3 === 0 ? 1 : 0), 2, 6 - (k % 3 === 0 ? 1 : 0), colors[(k + row * 2) % colors.length]);
        px(1, sy + 6, 16, 1, '#9a6234');
      });
      px(1, 16, 16, 3, '#5a3416');
    });
  }
  function chest() {
    return draw(16, 13, function () {
      px(0, 0, 16, 13, C.woodLine); px(1, 1, 14, 4, '#b07a44'); px(1, 1, 14, 1, '#d8a066'); px(1, 5, 14, 7, '#7a4c26');
      px(1, 5, 14, 1, '#3e2410'); px(0, 3, 16, 1, '#c08a32'); px(0, 9, 16, 1, '#c08a32'); px(7, 5, 2, 3, '#ffd23f');
    });
  }
  function plant() {
    return draw(10, 14, function () {
      px(2, 9, 6, 5, '#2e1d0e'); px(3, 9, 4, 4, '#c0643a'); px(3, 9, 4, 1, '#e08a5a');
      px(1, 3, 3, 5, '#3f7d28'); px(6, 2, 3, 6, '#3f7d28'); px(3, 0, 4, 8, '#5a9c33'); px(4, 1, 1, 3, '#7fbf45');
    });
  }
  function candle() {
    return draw(8, 12, function () {
      px(3, 4, 2, 7, '#efe6d2'); px(2, 10, 4, 2, '#c08a32'); px(3, 1, 2, 3, '#ffb84a'); px(3, 2, 1, 1, '#fff0b8');
      g.fillStyle = 'rgba(255,190,90,0.2)'; g.fillRect(0, 0, 8, 8);
    });
  }

  // ---------------------------------------------------------------- 마을 배치(세상 픽셀)
  function X(c) { return VX + c * T; }
  function Y(r) { return VY + r * T; }
  // 밭(32×32 이랑, 둑이 있어 앞면이 보임)과 병아리가 물 주는 자리(front), 드나드는 문(gate 안·밖)
  var NE = { x0: X(13), y0: Y(1) + 6, x1: X(18) + 6, y1: Y(5) + 4 };
  var SE = { x0: X(11), y0: Y(7) + 12, x1: X(20) - 4, y1: Y(11) + 12 };
  var NE_GATE = { x: X(15) + 12, inY: NE.y1 - 8, outY: Y(6) + 9 };
  var SE_GATE = { x: X(15) + 8, inY: SE.y0 + 8, outY: Y(6) + 9 };
  var PLOTS = [
    { x: SE.x0 + 10, y: SE.y0 + 24, frontY: SE.y0 + 20, gate: SE_GATE },
    { x: NE.x0 + 8, y: NE.y0 + 6, frontY: NE.y0 + 46, gate: NE_GATE },
    { x: SE.x0 + 54, y: SE.y0 + 24, frontY: SE.y0 + 20, gate: SE_GATE },
    { x: NE.x0 + 52, y: NE.y0 + 6, frontY: NE.y0 + 46, gate: NE_GATE },
    { x: SE.x0 + 98, y: SE.y0 + 24, frontY: SE.y0 + 20, gate: SE_GATE }
  ];
  var CROPS = ['carrot', 'cabbage', 'wheat', 'tomato', 'pumpkin', 'sunflower'];
  var HAY = { x0: X(4) + 4, y0: Y(7) + 12, x1: X(7) + 6, y1: Y(10) + 10 };

  function buildObjects() {
    addHouse();
    fenceBox(NE.x0, NE.y0, NE.x1, NE.y1, null, [NE_GATE.x - NE.x0 - 8, NE_GATE.x - NE.x0 + 8]);
    fenceBox(SE.x0, SE.y0, SE.x1, SE.y1, [SE_GATE.x - SE.x0 - 8, SE_GATE.x - SE.x0 + 8], null);
    fenceBox(HAY.x0, HAY.y0, HAY.x1, HAY.y1, null, null);
    // 밀밭: 키 큰 밀 줄(앞뒤가 맞게 줄마다 한 장)
    for (var hy = HAY.y0 + 10; hy < HAY.y1 - 1; hy += 6) {
      var row = draw(HAY.x1 - HAY.x0 - 6, 10, function () {
        for (var k = 0; k < HAY.x1 - HAY.x0 - 8; k += 2) {
          var hgt = 7 + Math.floor(hash(k, hy) * 3);
          px(k, 10 - hgt, 1, hgt, k % 4 ? '#c9952f' : '#b07f22'); px(k, 10 - hgt, 2, 2, '#f6d26a');
        }
      });
      place(row, HAY.x0 + 4, hy - 10, hy);
    }
    // 다리 난간(위 줄은 병아리 뒤, 아래 줄은 앞)
    var bx0 = X(2) - 6, blen = 2 * T + 8;
    fenceH(bx0, Y(6) + 3, blen, null);
    fenceH(bx0, Y(7) + 2, blen, null);
    // 집 앞과 갈림길
    addBarrel(X(4) + 12, Y(6) - 1); addCrate(X(13) - 6, Y(6) - 1, '#ff8a2a');
    addLamp(X(7) + 2, Y(6) + 2); addLamp(X(10) - 2, Y(6) + 2);
    addSign(X(10) + 4, Y(7) + 13);
    addCrate(X(18) + 14, Y(1) + 12, '#ff4b3a'); addCrate(X(18) + 14, Y(2) + 14, '#ffd35c');
    addSunflower(X(4) + 8, Y(3)); addSunflower(X(12) + 6, Y(2) + 4); addSunflower(X(12) + 8, Y(4) + 6);
    addBush(X(18) + 10, Y(5) + 4, 1, '#3a5ad8'); addBush(X(9) + 12, Y(11) + 14, 2, '#d8463a'); addBush(X(0) + 10, Y(7) - 1, 0);
    // 마을 안 나무 · 돌 · 꽃 · 버섯 · 통나무
    addPine(X(4) + 6, Y(5) + 4, 9, 1); addPoplar(X(10) + 8, Y(10) + 8, 6, 2); addPoplar(X(20) - 2, Y(6) - 2, 6, 5, AUTUMN);
    addOak(X(0) + 10, Y(3) + 8, 15, 3); addOak(X(1), Y(10) + 10, 17, 4);
    addRock(X(1) + 8, Y(8) + 8, true); addRock(X(19) + 4, Y(11) + 12, false); addRock(X(4) + 4, Y(1) + 10, false);
    addLupin(X(0) + 12, Y(5) + 10, '#a46ad8'); addLupin(X(16) + 6, Y(6) - 1, '#c88ae8'); addLupin(X(20) + 4, Y(9), '#a46ad8');
    addMushroom(X(9) + 14, Y(9) + 4); addMushroom(X(17) + 10, Y(6) - 2);
    addLog(X(4) + 2, Y(11) + 14, 40);
    addStump(X(20) + 8, Y(8) + 8);
    addReeds(X(1) + 6, Y(12) + 2);
    // 마을 안 키 큰 풀(길·밭·집에서 떨어진 풀밭에 듬성듬성)
    for (var r = VR; r < VR + ROWS; r++)
      for (var c = VC; c < VC + COLS; c++) {
        if (tile(c, r) !== 'G' || hash(c * 7, r * 3) < 0.55) continue;
        var gx = c * T + 3 + Math.floor(hash(c, r * 5) * 10), gy = r * T + 8 + Math.floor(hash(r, c * 5) * 7);
        if (busy(gx, gy)) continue;
        addTallGrass(gx, gy, c + r);
      }
    buildWild();
  }
  // 집·울타리 안·길 옆처럼 키 큰 풀을 두지 않을 자리
  function busy(x, y) {
    function inside(b, pad) { return x > b.x0 - pad && x < b.x1 + pad && y > b.y0 - pad && y < b.y1 + pad; }
    if (inside(HOUSE, 6) || inside(NE, 6) || inside(SE, 6) || inside(HAY, 6)) return true;
    var c = x >> 4, r = y >> 4;
    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++) if (tile(c + dx, r + dy) !== 'G') return true;
    return false;
  }

  // 마을 밖 숲: 마을에서 멀수록, 무늬 값이 클수록 나무가 빽빽(길·물·절벽 옆은 비움). 활엽수 · 전나무 · 포플러가 무늬별로 모이고,
  // 사이사이 키 큰 풀 · 덤불 · 돌 · 그루터기 · 버섯 · 꽃
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
          var kind = smooth(c, r, 6, 9);
          if (kind > 0.62) addPine(x, foot, 8 + Math.floor(hash(c, r) * 3), c * 7 + r);
          else if (kind < 0.3) addPoplar(x, foot, 5 + Math.floor(hash(r, c) * 2), c + r * 3, hash(c, r * 9) > 0.93 ? AUTUMN : null);
          else addOak(x, foot, 13 + Math.floor(hash(r, c) * 6), c * 3 + r * 5);
          continue;
        }
        var f = hash(c + 11, r + 7);
        if (clear) { if (f > 0.93) addRock(x, foot, false); continue; }
        if (f > 0.4 && f < 0.62) addTallGrass(x, foot, c * 3 + r);
        else if (f > 0.96) addBush(x, foot, c + r, hash(c, r) > 0.6 ? '#3a5ad8' : null);
        else if (f > 0.93) addRock(x, foot, f > 0.95);
        else if (f < 0.03) addStump(x, foot);
        else if (f < 0.05) addMushroom(x, foot);
        else if (f > 0.7 && f < 0.72) addLupin(x, foot, '#a46ad8');
      }
    // 호숫가 갈대
    for (var rr = VR + 11; rr < WR; rr++)
      for (var cc = VC - 10; cc < VC + 6; cc++)
        if (tile(cc, rr) === 'G' && wet(tile(cc, rr + 1)) && hash(cc, rr * 3) > 0.55) addReeds(cc * T + 8, rr * T + 15);
  }

  // 땅 위에 바로 그리는 것(납작한 것): 풀 결, 들꽃, 연잎, 다리 판자, 집 바닥·깔개, 밭 이랑
  function decorateGround() {
    for (var r = 0; r < WR; r++)
      for (var c = 0; c < WC; c++) {
        var t = tile(c, r), x = c * T, y = r * T;
        if (t === 'G') {
          for (var k = 0; k < 2; k++) {
            var hx = Math.floor(hash(c * 3 + k, r) * 12) + 1, hy = Math.floor(hash(r * 5 + k, c) * 11) + 3;
            if (tile(c, r - 1) === 'C' && hy < 6) continue;
            px(x + hx, y + hy, 1, 2, C.grassDark); px(x + hx + 2, y + hy, 1, 2, C.grassDark); px(x + hx + 1, y + hy - 1, 1, 2, C.grassLight);
          }
          var f = hash(c + 21, r + 3);
          if (f > 0.93) { px(x + 6, y + 6, 3, 3, '#ffffff'); px(x + 7, y + 7, 1, 1, '#ffd35c'); }
          else if (f < 0.03) { px(x + 9, y + 9, 2, 2, '#ffd35c'); px(x + 4, y + 4, 2, 2, '#ff9fb0'); }
        }
        if (t === 'W' && isLake(c, r) && !isRiver(c, r) && hash(c * 5, r) > 0.84) {
          var lx2 = x + 3 + Math.floor(hash(c, r) * 6), ly2 = y + 4 + Math.floor(hash(r, c) * 6);
          px(lx2, ly2, 7, 4, '#24501d'); px(lx2 + 1, ly2, 5, 3, '#4f9f3a'); px(lx2 + 3, ly2 + 1, 1, 2, '#24501d');
          if (hash(c, r + 3) > 0.6) { px(lx2 + 2, ly2 - 2, 3, 2, '#ffb3c1'); px(lx2 + 3, ly2 - 3, 1, 1, '#fff'); }
        }
      }
    // 다리 판자(물 위, 양쪽 둑에 걸침)와 그 앞면(두께)
    var bx0 = X(2) - 6, bw = 2 * T + 12, by0 = Y(6) + 1;
    px(bx0, by0, bw, 15, C.woodLine);
    for (var b = 0; b < bw - 2; b += 4) { px(bx0 + 1 + b, by0 + 1, 3, 12, b % 8 ? '#8a5a2c' : C.wood); px(bx0 + 1 + b, by0 + 1, 1, 12, C.woodLight); px(bx0 + 2 + b, by0 + 3, 1, 1, C.woodLine); px(bx0 + 2 + b, by0 + 10, 1, 1, C.woodLine); }
    px(bx0 + 1, by0 + 13, bw - 2, 3, '#4a2e16'); px(bx0, by0 + 16, bw, 3, 'rgba(10,30,60,0.45)');
    // 집 바닥: 세로 판자, 판자 끝이 엇갈리고 못 자국(문 자리는 남쪽 벽 앞면 끝까지)
    var fx0 = HOUSE.x0 + WALL, fx1 = HOUSE.x1 - WALL, fy0 = HOUSE.y0 + WALL, fy1 = HOUSE.y1 - FACE, dx0 = (HOUSE.x0 + HOUSE.x1) / 2 - 8;
    for (var yy = fy0; yy < HOUSE.y1; yy++)
      for (var xx = fx0; xx < fx1; xx++) {
        if (yy >= fy1 && (xx < dx0 || xx >= dx0 + 16)) continue;
        var pk = Math.floor((xx - fx0) / 4), lxp = (xx - fx0) % 4, seam = (yy - fy0 + pk * 7) % 20;
        var col = lxp === 0 ? '#4a2a10' : seam === 0 ? '#4a2a10' : pk % 2 ? '#7a4a1e' : '#6e4218';
        if (lxp === 1 && seam !== 0) col = '#8a5626';
        if (seam === 2 && lxp === 2) col = '#a8763a';
        px(xx, yy, 1, 1, col);
      }
    // 문 앞 돌계단 · 방 가운데 깔개
    px(HOUSE.x0 + (HOUSE.x1 - HOUSE.x0) / 2 - 9, HOUSE.y1, 18, 4, C.stoneEdge); px(HOUSE.x0 + (HOUSE.x1 - HOUSE.x0) / 2 - 8, HOUSE.y1, 16, 2, C.stoneLight);
    var rugX = fx0 + 36, rugY = fy0 + FACE + 6;
    px(rugX, rugY, 36, 26, '#5a2a4a'); px(rugX + 1, rugY + 1, 34, 24, '#8a3a5a'); px(rugX + 3, rugY + 3, 30, 20, '#b8546a'); px(rugX + 5, rugY + 5, 26, 16, '#8a3a5a');
    PLOTS.forEach(function (p) { soil(p.x, p.y, false); });
    // 그림자(서 있는 것들이 모은 것)
    g.fillStyle = C.shadow;
    shadows.forEach(function (s) { s(g); });
  }
  // 밭 이랑: 둑(윗면 · 앞면)으로 둘러싸인 갈아 둔 흙, 물 주면 짙다
  function soil(x, y, wetSoil) {
    px(x - 1, y - 1, 34, 36, '#2a1a0c');
    px(x, y, 32, 32, wetSoil ? '#4a2e18' : '#6a4426');
    for (var r = 0; r < 4; r++) { px(x + 1, y + 2 + r * 8, 30, 3, wetSoil ? '#5e3a1e' : '#7e5230'); px(x + 1, y + 2 + r * 8, 30, 1, wetSoil ? '#6e4626' : '#946238'); }
    for (var k = 0; k < 16; k++) px(x + 1 + k * 2, y + 32, 1, 1, '#8a6a3a');
    px(x, y + 32, 32, 2, '#3e2814');
  }
  // 작물(3/4로 선 것): 이랑마다 네 포기, 포기마다 발 자리가 달라 병아리와 앞뒤가 맞는다
  var cropLayer = [];
  function plantCrops(x, y, crop) {
    for (var r = 0; r < 4; r++)
      for (var c = 0; c < 4; c++) {
        var cx = x + 4 + c * 8, foot = y + 5 + r * 8;
        place(cropImg(crop, (c + r) % 2), cx - 3, foot - 12, foot, cropLayer);
      }
  }
  var cropCache = {};
  function cropImg(crop, alt) {
    var key = crop + alt;
    if (cropCache[key]) return cropCache[key];
    var cv = draw(7, 12, function () {
      if (crop === 'carrot') { px(1, 4, 1, 6, '#4f9f3a'); px(3, 2, 1, 8, '#62b244'); px(5, 4, 1, 6, '#4f9f3a'); px(2, 6, 3, 1, '#62b244'); px(2, 10, 3, 2, '#ff8a2a'); px(2, 10, 1, 1, '#ffb05a'); }
      else if (crop === 'cabbage') { px(0, 6, 7, 6, '#24501d'); px(1, 5, 5, 6, '#6ab54a'); px(2, 6, 3, 3, '#a6dc7a'); px(1, 10, 5, 1, '#3f7d28'); }
      else if (crop === 'wheat') { for (var k = 0; k < 3; k++) { px(1 + k * 2, 2 + k % 2, 1, 10, '#b07f22'); px(1 + k * 2, 1 + k % 2, 2, 3, '#f2c94e'); } }
      else if (crop === 'tomato') { px(3, 0, 1, 12, '#7a4c26'); px(1, 2, 2, 6, '#3f8f45'); px(4, 3, 2, 6, '#3f8f45'); px(1, 5, 2, 2, '#e8463a'); px(4, 7, 2, 2, '#e8463a'); px(1, 5, 1, 1, '#ff9a8a'); }
      else if (crop === 'pumpkin') { if (alt) { px(0, 6, 7, 6, '#7a3a0a'); px(1, 6, 5, 5, '#f08a24'); px(1, 6, 2, 2, '#ffb45a'); px(3, 6, 1, 5, '#c86a14'); px(3, 4, 1, 2, '#3f7d28'); } else { px(1, 7, 5, 4, '#3f8f45'); px(2, 6, 3, 2, '#62b244'); } }
      else { px(3, 4, 1, 8, '#3f7a28'); px(1, 7, 2, 2, '#4f8f2c'); px(4, 8, 2, 2, '#4f8f2c'); px(1, 0, 5, 5, '#f2c12e'); px(2, 1, 3, 3, '#6a3a12'); }
    });
    return (cropCache[key] = cv);
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
        if (hash(c + 3, r) > 0.65) { ctx.fillStyle = '#5a92cc'; ctx.fillRect(x + 9 - m, y + 11, 3, 1); }
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

  // ---------------------------------------------------------------- 병아리(기본 마스코트의 앞·오른쪽·뒤, 왼쪽은 옆모습을 뒤집음)
  var SOUTH = 0, EAST = 1, NORTH = 2, WEST = 3;
  var CHICK_W = 16, CHICK_H = 14, CHICK_FRAMES = 6, CHICK_SPEED = 14;
  var CHICK_COLORS = {
    k: '#5a3d00', y: '#ffd84a', s: '#eab436', h: '#ffe77e',
    e: '#3a2600', p: '#ffab8a', o: '#ff8a2a'
  };
  var CHICK_BODIES = [
    ['......kk......', '...kkkkkkkk...', '..kyhhhhyyyk..', '.kyyyyyyyyyyk.',
     '.kyyeyyyyeyyk.', '.kpyyyooyyypk.', '.kyyyyyyyyyyk.', 'kyyyyyyyyyyyyk',
     'ksyyyyyyyyyysk', 'ksyyyyyyyyyysk', '.kssssssssssk.', '..kkkkkkkkkk..'],
    ['......kk......', '....kkkkkkk...', '...kyhhhyyyk..', '...kyyyyyyyyk.',
     '...kyyyyyeyyk.', '.kkyyyyyyyypko', 'kyyyyyyyyyyyko', '.kyyssyyyyyyk.',
     '..kyyssyyyyk..', '..kssyyyyyyk..', '...kssssssk...', '....kkkkkk....'],
    ['......kk......', '...kkkkkkkk...', '..kyyhhyyyyk..', '.kyyyyyyyyyyk.',
     '.kyyyyyyyyyyk.', '.kyyyyyyyyyyk.', '.kyyyyyyyyyyk.', 'ksyyyyyyyyyysk',
     'kssyyyssyyyssk', 'kyyyyssssyyyyk', '.kssssssssssk.', '..kkkkkkkkkk..']
  ];
  // 두 발의 X 이동과 들림: 대기, 오른발 딛기, 교차, 왼발 딛기, 교차.
  var CHICK_STEPS = [[0, 0, 0, 0], [1, 0, -1, 1], [0, 1, 0, 0], [-1, 1, 1, 0], [0, 0, 0, 1]];
  function chickSheet() {
    return draw(CHICK_W * CHICK_FRAMES, CHICK_H * 4, function () {
      var blink = Object.assign({}, CHICK_COLORS, { e: '#b97c26' });
      for (var face = SOUTH; face <= WEST; face++) {
        var side = face === EAST || face === WEST, y = face * CHICK_H;
        for (var f = 0; f < CHICK_FRAMES; f++) {
          var x = f * CHICK_W, step = CHICK_STEPS[f === 5 ? 0 : f], lift = f === 2 || f === 4 ? 1 : 0;
          if (face === WEST) { g.save(); g.translate(x * 2 + CHICK_W, 0); g.scale(-1, 1); }
          px(x + (side ? 7 : 10) + step[0], y + 13 - step[1], 2, 1, '#d96b20');
          px(x + (side ? 6 : 4) + step[2], y + 13 - step[3], 2, 1, CHICK_COLORS.o);
          sprite(x + 1, y + 1 - lift, CHICK_BODIES[side ? EAST : face], f === 5 ? blink : CHICK_COLORS);
          if (face === WEST) g.restore();
        }
      }
    });
  }
  var chickFrames = chickSheet();
  var wateringCan = draw(8, 5, function () {
    sprite(0, 0, ['...kkk..', '.kkbbbk.', 'kbbbbbbk', '.kbbbbbk', '..kkkkk.'], { k: C.ink, b: '#7fb8d8' });
  });
  function nameSeed(s) { var h = 7; for (var i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0; return h >>> 0; }

  // 길 위 자리(세상 px): 집 안 · 집 앞 · 갈림길 · 다리 건너 · 동쪽 길 · 계단 위 · 남쪽 길. 갈래 자리는 큰길(PATH_ROW)의 갈림점을 거쳐 간다
  var ROW_Y = Y(6) + 9, DOOR_X = (HOUSE.x0 + HOUSE.x1) / 2;
  var SPOTS = {
    home: { x: HOUSE.x0 + WALL + 26, y: HOUSE.y0 + WALL + FACE + 32, via: DOOR_X, door: true },
    door: { x: DOOR_X, y: HOUSE.y1 + 6, via: DOOR_X },
    cross: { x: X(8) + 8, y: ROW_Y },
    west: { x: X(0) + 6, y: ROW_Y },
    east: { x: X(20) + 4, y: ROW_Y },
    hill: { x: X(19) + 8, y: Y(-2) + 8, via: X(19) + 8 },
    south: { x: X(8) + 8, y: Y(11) + 6, via: X(8) + 8 }
  };
  var SPOT_NAMES = ['home', 'door', 'west', 'east', 'hill', 'south', 'home'];
  // 주민 한 마리의 하루(한 바퀴): 밭에서 물 주기 → 문 → 큰길 → 들를 곳 둘(집 안이면 문으로 들어가 잠깐 쉼) → 다시 밭. 밭이 없으면 큰길과 들를 곳만
  function buildRoute(r, index) {
    var seed = nameSeed(r.name) + index * 97, pts = [];
    function go(x, y, pause, water) {
      var prev = pts[pts.length - 1];
      if (prev && prev.x === x && prev.y === y) {
        prev.pause += pause || 0;
        prev.water = water || prev.water;
      } else pts.push({ x: x, y: y, pause: pause || 0, water: water || 0 });
    }
    function toRow(x) { go(x, ROW_Y); }
    var visits = [];
    for (var k = 0; visits.length < 2 && k < 12; k++) {
      var s = SPOT_NAMES[Math.floor(hash(seed, k) * SPOT_NAMES.length)];
      if (visits.indexOf(s) < 0) visits.push(s);
    }
    if (r.plot) {
      var p = r.plot, fx = p.x + 16, water = p.y < p.frontY ? -1 : 1;
      go(fx - 8, p.frontY, 3.5, water);
      go(fx + 8, p.frontY, 3, water);
      go(p.gate.x, p.gate.inY);
      toRow(p.gate.x);
    } else go(SPOTS.cross.x, ROW_Y, 1);
    visits.forEach(function (name) {
      var s = SPOTS[name];
      if (s.via !== undefined) {
        toRow(s.via);
        if (s.door) { go(DOOR_X, HOUSE.y1 + 6); go(DOOR_X, HOUSE.y1 - FACE - 6); go(s.x, s.y, 4 + hash(seed, 7) * 3); go(DOOR_X, HOUSE.y1 - FACE - 6); go(DOOR_X, HOUSE.y1 + 6); }
        else go(s.x, s.y, 2.5 + hash(seed, s.x) * 2);
        toRow(s.via);
      } else go(s.x, s.y, 1.5 + hash(seed, s.y) * 2);
    });
    if (r.plot) { toRow(r.plot.gate.x); go(r.plot.gate.x, r.plot.gate.inY); }
    var first = pts[0], last = pts[pts.length - 1];
    if (first.x === last.x && first.y === last.y) {
      first.pause += last.pause;
      first.water = first.water || last.water;
      pts.pop();
    }
    // 구간을 이어 걸으며, 실제로 쉬는 지점 앞뒤에서만 속도를 줄이거나 높인다.
    var segs = [], total = 0, walked = 0;
    for (var i = 0; i < pts.length; i++) {
      var a = pts[i], b = pts[(i + 1) % pts.length], dx = b.x - a.x, dy = b.y - a.y;
      var length = Math.hypot(dx, dy);
      var face = Math.abs(dx) > Math.abs(dy) ? (dx < 0 ? WEST : EAST) : (dy < 0 ? NORTH : SOUTH);
      var travel = length / CHICK_SPEED, ramp = Math.min(0.16, travel);
      var startRamp = a.pause ? ramp : 0, stopRamp = b.pause ? ramp : 0;
      var walk = travel + (startRamp + stopRamp) / 2;
      segs.push({ a: a, dx: dx, dy: dy, length: length, face: face, walk: walk,
        startRamp: startRamp, stopRamp: stopRamp, start: total, walked: walked });
      total += a.pause + walk;
      walked += length;
    }
    r.segs = segs;
    r.period = total;
    r.offset = hash(seed, 3) * total;
  }
  function residentPos(r, t) {
    var s = (t / 1000 + r.offset) % r.period, i = 0;
    while (i < r.segs.length - 1 && s >= r.segs[i + 1].start) i++;
    var seg = r.segs[i], a = seg.a, elapsed = s - seg.start - a.pause;
    if (elapsed < 0) {
      var face = a.water ? (a.water < 0 ? NORTH : SOUTH) : r.segs[(i + r.segs.length - 1) % r.segs.length].face;
      var blink = !still && (t + r.offset * 1000) % 3200 < 120;
      return { x: a.x, y: a.y, face: face, pose: blink ? 5 : 0, water: a.water };
    }
    // 가속·감속 동안 덜 걸은 거리를 빼고, 실제 걸은 거리로 발동작을 고른다.
    var travel = elapsed;
    if (seg.startRamp) {
      var up = Math.min(elapsed, seg.startRamp);
      travel -= up * (1 - up / (2 * seg.startRamp));
    }
    if (seg.stopRamp) {
      var down = Math.max(0, elapsed - (seg.walk - seg.stopRamp));
      travel -= down * down / (2 * seg.stopRamp);
    }
    var distance = Math.min(seg.length, travel * CHICK_SPEED), f = distance / seg.length;
    var pose = still ? 0 : 1 + Math.floor((seg.walked + distance) / 2) % 4;
    return { x: a.x + seg.dx * f, y: a.y + seg.dy * f, face: seg.face, pose: pose, water: 0 };
  }
  function drawChick(ctx, p, t) {
    var x = p.x, y = p.y;
    ctx.fillStyle = 'rgba(12,28,8,0.4)';
    ctx.fillRect(x - 5, y - 1, 10, 2);
    ctx.fillRect(x - 4, y, 8, 1);
    // 세상 픽셀로 위치를 반올림하지 않는다. 화면에 확대할 때에도 그림 자체는 보간하지 않는다.
    ctx.drawImage(chickFrames, p.pose * CHICK_W, p.face * CHICK_H, CHICK_W, CHICK_H,
      x - CHICK_W / 2, y - CHICK_H, CHICK_W, CHICK_H);
    if (p.water) {
      var cx = x + 4, cy = y + (p.water < 0 ? -10 : -6);
      ctx.drawImage(wateringCan, cx, cy);
      var d = Math.floor(t / 120) % 4;
      ctx.fillStyle = '#bfe6ff';
      ctx.fillRect(cx + 8, cy + 2 + p.water * d, 1, 1);
      ctx.fillRect(cx + 10, cy + 2 + p.water * ((d + 2) % 4), 1, 1);
    }
  }

  // ---------------------------------------------------------------- 그려 두기
  var screen = document.getElementById('world');
  if (!screen) return;
  var namesHost = document.getElementById('world-names');
  var sctx = screen.getContext('2d');
  var ground = canvas(WW, WH), gctx = ground.getContext('2d');
  buildObjects();
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
  // 그릴 순서: 서 있는 것 + 작물, 발 자리 순
  var scene = [];
  function sortScene() { scene = objects.concat(cropLayer); scene.sort(function (a, b) { return a.base - b.base || a.x - b.x; }); }
  sortScene();
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
  var still = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var last = 0, framePending = false;
  function requestDraw() {
    if (framePending) return;
    framePending = true;
    requestAnimationFrame(frame);
  }

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
    requestDraw();
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

  function fit() {
    follow = -1;
    goal = { x: VX + W / 2, y: VY + H / 2, z: fitZoom() };
    markActive();
    requestDraw();
  }
  function zoomAt(factor, sx, sy) {
    var wx = cam.x + (sx - vw / 2) / cam.z, wy = cam.y + (sy - vh / 2) / cam.z;
    cam.z = Math.min(8, Math.max(minZoom(), cam.z * factor));
    if (follow < 0) { cam.x = wx - (sx - vw / 2) / cam.z; cam.y = wy - (sy - vh / 2) / cam.z; }
    goal = null;
    clamp();
    requestDraw();
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
      requestDraw();
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
  function showResidents(entries) {
    if (follow >= 0) fit();
    residents = entries.map(function (entry, k) {
      var name = entry.name;
      var crop = CROPS.includes(entry.crop) ? entry.crop : CROPS[k % CROPS.length];
      var r = { name: name, plot: PLOTS[k] || null, crop: crop };
      buildRoute(r, k);
      return r;
    });
    // 밭: 주인 있는 밭은 물 준 흙에 작물, 나머지는 갈아만 둔 흙
    g = gctx;
    cropLayer.length = 0;
    PLOTS.forEach(function (p, k) {
      var owner = residents[k];
      soil(p.x, p.y, !!owner);
      if (owner) plantCrops(p.x, p.y, owner.crop);
    });
    sortScene();
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
        requestDraw();
      });
      li.appendChild(b);
      list.appendChild(li);
      r.label = document.createElement('span');
      r.label.className = 'village-name';
      r.label.textContent = r.name;
      namesHost.appendChild(r.label);
    });
    document.getElementById('resident-count').textContent = String(entries.length);
    document.getElementById('residents-empty').hidden = entries.length > 0;
    markActive();
    filter();
    requestDraw();
  }
  async function loadResidents() {
    try {
      var response = await fetch('/api/village/residents', { headers: { Accept: 'application/json' } });
      if (!response.ok) throw Error('Could not load village residents.');
      showResidents((await response.json()).residents);
      document.getElementById('residents-error').hidden = true;
    } catch (e) {
      document.getElementById('residents-error').hidden = false;
    }
  }
  loadResidents();
  document.addEventListener('chickxel:residents-changed', loadResidents);

  function frame(now) {
    framePending = false;
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
      var a = still ? 1 : 1 - Math.exp(-rate * dt), az = still ? 1 : 1 - Math.exp(-2.2 * dt);
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
    var chicks = at.slice().sort(function (p, q) { return p.y - q.y; });
    var ci = 0;
    for (var i = 0; i < scene.length; i++) {
      var o = scene[i];
      while (ci < chicks.length && chicks[ci].y + 0.5 <= o.base) { drawChick(sctx, chicks[ci++], t); }
      if (o.x + o.w < view.x0 || o.x > view.x1 || o.y + o.h < view.y0 || o.y > view.y1) continue;
      sctx.drawImage(o.img, o.x, o.y);
    }
    for (; ci < chicks.length; ci++) drawChick(sctx, chicks[ci], t);
    butterflies(sctx, t);
    sctx.drawImage(fog, 0, 0);
    // 이름표(HTML): 병아리 머리 위
    residents.forEach(function (r, i) {
      var q = at[i];
      r.label.style.transform = 'translate(' + ((q.x - cam.x) * cam.z + vw / 2) + 'px,' + ((q.y - CHICK_H - 2 - cam.y) * cam.z + vh / 2) + 'px) translate(-50%,-100%)';
      r.label.classList.toggle('active', i === follow);
    });
    if (!still) requestDraw();
  }
})();
