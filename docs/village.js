// 병아리 후원 마을(village.html — 위 막대의 마을 단추로 온다).
// 네세스(Necesse)·팅커랜드(Tinkerlands)처럼 위에서 비스듬히 내려다본(3/4) 숲속 픽셀 마을: 서 있는 것은 윗면과 함께 남쪽 앞면이 보이고,
// 집은 지붕 없이 통나무 벽(벽 윗면 + 북쪽 벽의 안쪽 앞면 + 남쪽 벽의 바깥 앞면)과 나무 바닥·가구가 보인다. 북쪽 언덕(절벽)에서 강이
// 폭포로 떨어져 마을 서쪽을 지나 남서쪽 호수로 흐르고, 집 앞에서 돌길이 갈라진다. 울타리 친 밭마다 커피를 사 주고 이름을 남긴 사람의
// 병아리(앱 마스코트) 한 마리가 살며, 물을 주고 집에 들르고 마을 곳곳을 걸어 다닌다. 마을 밖은 아직 개척하지 않은 숲(멀수록 안개).
// 땅(그림자 포함)은 한 번 그려 두고, 나무·벽·가구·울타리·작물·키 큰 풀·병아리처럼 서 있는 것은 발 자리(아래 끝)가 위인 것부터 그려
// 앞뒤가 맞게 겹친다. 끌어서 옮기고 휠·두 손가락·단추로 확대하며, 주민 이름을 누르면 따라간다.
// 공개 주민 목록에는 인증된 후원자가 선택한 병아리와 작물도 들어온다.
(async function (script) {
  var T = 16;
  // 온 세상(타일)과 그 안의 마을 자리(전체 보기·안개 기준)
  var WC = 56, WR = 36, VC = 18, VR = 12, COLS = 20, ROWS = 12;
  var WW = WC * T, WH = WR * T, VX = VC * T, VY = VR * T, W = COLS * T, H = ROWS * T;

  // ---------------------------------------------------------------- 도구
  function hash(a, b) { var h = (a * 374761393 + b * 668265263) | 0; h = (h ^ (h >>> 13)) * 1274126177; return ((h ^ (h >>> 16)) >>> 0) / 4294967296; }
  function canvas(w, h) { var c = document.createElement('canvas'); c.width = w; c.height = h; return c; }
  function distance(c, r) {
    var dx = Math.max(VC - c, 0, c - (VC + COLS - 1)), dy = Math.max(VR - r, 0, r - (VR + ROWS - 1));
    return Math.sqrt(dx * dx + dy * dy);
  }

  // ---------------------------------------------------------------- Chickxel 타일맵과 오브젝트 프로젝트의 내보내기 결과.
  // scene.json: 시트 칸 [x, y, w, h], 배치 [칸 번호, 세상 x, 세상 y, 발 기준 y].
  var atlasImg = new Image(), ground = new Image(), art, objects, houseDoor, chickSheet;
  function sceneObject(id, x, y, base) {
    var rect = art.sprites[id];
    return { rect: rect, x: x, y: y, base: base, w: rect[2], h: rect[3] };
  }
  function paintSprite(ctx, rect, x, y) {
    ctx.drawImage(atlasImg, rect[0], rect[1], rect[2], rect[3], x, y, rect[2], rect[3]);
  }

  var HOUSE = { x0: VX + 5 * T, x1: VX + 12 * T, y0: VY + 1 * T + 2, y1: VY + 5 * T + 14 };
  var WALL = 8, FACE = 14, DOOR_X = (HOUSE.x0 + HOUSE.x1) / 2;
  var DOOR_IN = HOUSE.y1 - FACE - 6, DOOR_OUT = HOUSE.y1 + 6, DOOR_SWING = 0.4;

  // ---------------------------------------------------------------- 마을 배치(세상 픽셀)
  function X(c) { return VX + c * T; }
  function Y(r) { return VY + r * T; }
  // 밭 왼쪽 위는 타일맵에서 받는다. 큰길 위·아래의 두 울타리는 각자 입구를 쓴다.
  var NE_GATE = { x: X(15) + 12, inY: Y(5) - 4 };
  var SE_GATE = { x: X(15) + 8, inY: Y(7) + 20 };
  var PLOTS;
  var CROPS = ['carrot', 'cabbage', 'wheat', 'tomato', 'pumpkin', 'sunflower'];
  // 작물(3/4로 선 것): 이랑마다 네 포기, 포기마다 발 자리가 달라 병아리와 앞뒤가 맞는다
  var cropLayer = [];
  function plantCrops(x, y, crop) {
    for (var r = 0; r < 4; r++)
      for (var c = 0; c < 4; c++) {
        var cx = x + 4 + c * 8, foot = y + 5 + r * 8;
        cropLayer.push(sceneObject(art.crops[crop][(c + r) % 2], cx - 3, foot - 12, foot));
      }
  }

  // ---------------------------------------------------------------- 움직이는 것
  // 물결은 실제 물 표면에만 그린다. 강에서 흘러오던 물결은 호수에 닿을 때 흐려진다.
  var lakeTiles = [], fallTiles = [], fallRows = new Uint8Array(WR);
  var riverRows = new Float32Array(WR).fill(-1), riverEnd = 0;
  var waterSurface = new Uint8Array(WW * WH);
  function prepareWater() {
    var counts = new Uint16Array(WR);
    art.water.forEach(function (entry) {
      var x = entry[0], y = entry[1], kind = entry[2], row = Math.floor(y / T);
      if (row < 0 || row >= WR) return;
      if (kind === 1 || kind === 3) {
        if (!counts[row]++) riverRows[row] = 0;
        riverRows[row] += x + T / 2;
        riverEnd = Math.max(riverEnd, y + T);
      }
      if (kind === 2) lakeTiles.push(x, y);
      if (kind === 3) { fallTiles.push(x, y); fallRows[row] = 1; }
    });
    for (var row = 0; row < WR; row++) if (counts[row]) riverRows[row] /= counts[row];
    var ctx = canvas(WW, WH).getContext('2d', { willReadFrequently: true });
    ctx.drawImage(ground, 0, 0);
    var rgba = ctx.getImageData(0, 0, WW, WH).data;
    // 바닥과 시설을 합친 내보내기에서 실제로 드러난 물빛에만 파문을 얹는다.
    var colors = new Set([0x3f78b8, 0x3b71ae, 0x2f5f99, 0x6ea7dc, 0xe3f4ff, 0x5f9bd4, 0xa0d0f2]);
    for (var i = 0; i < rgba.length; i += 4)
      waterSurface[i / 4] = colors.has(rgba[i] * 65536 + rgba[i + 1] * 256 + rgba[i + 2]);
  }
  function waterLine(ctx, x, y, w) {
    if (y < 0 || y >= WH) return;
    var end = Math.min(WW, x + w), start = -1, row = y * WW;
    for (var i = Math.max(0, x); i < end; i++) {
      if (waterSurface[row + i]) { if (start < 0) start = i; }
      else if (start >= 0) { ctx.fillRect(start, y, i - start, 1); start = -1; }
    }
    if (start >= 0) ctx.fillRect(start, y, end - start, 1);
  }
  function riverCenter(y) {
    var center = riverRows[y >> 4], pos = y / T - 0.5, row = Math.floor(pos);
    var a = riverRows[row], b = riverRows[row + 1];
    return a >= 0 && b >= 0 ? a + (b - a) * (pos - row) : center;
  }
  function drawWater(ctx, t, view) {
    ctx.fillStyle = '#6ea7dc';
    for (var i = 0; riverEnd && i < 27; i++) {
      var y = Math.floor((t / 85 + i * riverEnd / 27) % riverEnd);
      if (y + 2 < view.y0 || y > view.y1 || fallRows[y >> 4] || riverRows[y >> 4] < 0) continue;
      var x = Math.floor(riverCenter(y) - 10 + hash(i, 49) * 20);
      if (x + 6 < view.x0 || x > view.x1) continue;
      ctx.globalAlpha = 0.65 * Math.min(1, (riverEnd - y) / (3 * T));
      var w = 3 + (i % 4);
      waterLine(ctx, x, y, w);
      if (i % 3 === 0) waterLine(ctx, x + 1, y + 2, w - 2);
    }
    for (var k = 0; k < lakeTiles.length; k += 2) {
      var lx = lakeTiles[k], ly = lakeTiles[k + 1];
      if (lx + T < view.x0 || lx > view.x1 || ly + T < view.y0 || ly > view.y1) continue;
      var phase = (t / 2400 + hash(lx, ly)) % 1;
      ctx.globalAlpha = 0.55 * Math.sin(phase * Math.PI);
      if (hash(lx, ly + 9) > 0.4)
        waterLine(ctx, lx + 3 + Math.floor(phase * 4), ly + 4 + Math.floor(hash(ly, lx) * 8), 4 + Math.floor(hash(lx, 18) * 3));
    }
    ctx.globalAlpha = 1;
    ctx.fillStyle = '#e3f4ff';
    for (var f = 0; f < fallTiles.length; f += 2) {
      var fx = fallTiles[f], fy = fallTiles[f + 1];
      if (fx + T < view.x0 || fx > view.x1 || fy + T + 7 < view.y0 || fy > view.y1) continue;
      for (var j = 0; j < 4; j++) {
        var col = 1 + Math.floor(hash(fx, j) * 14), off = Math.floor((t / 40 + j * 5) % T);
        for (var n = 0; n < 4 && off + n < T; n++) waterLine(ctx, fx + col, fy + off + n, 1);
      }
      for (var b = 0; b < 5; b++) {
        var bx = fx + Math.floor(hash(f, b) * 14), by = fy + T + Math.floor((t / 120 + b * 3) % 6);
        waterLine(ctx, bx, by, 2);
      }
    }
  }
  // 나비 세 마리(마을 위)
  function butterflies(ctx, t) {
    [[X(6), Y(8), '#ffe36b', 0], [X(14), Y(6), '#ffb3c1', 2], [X(3), Y(3), '#ffffff', 4]].forEach(function (b) {
      var a = t / 2200 + b[3], x = Math.round(b[0] + Math.sin(a) * 30), y = Math.round(b[1] + Math.sin(a * 2.3) * 12);
      var open = Math.floor((t + b[3] * 100) / 150) % 2;
      ctx.fillStyle = '#1c2a14'; ctx.fillRect(x, y, 1, 2);
      ctx.fillStyle = b[2];
      if (open) { ctx.fillRect(x - 2, y - 1, 2, 2); ctx.fillRect(x + 1, y - 1, 2, 2); } else { ctx.fillRect(x - 1, y, 1, 2); ctx.fillRect(x + 1, y, 1, 2); }
    });
  }

  // 병아리 시트: 아래·오른쪽·위·왼쪽 순으로 한 줄씩, 대기·걷기 네 칸·눈 깜빡임 순.
  var SOUTH = 0, EAST = 1, NORTH = 2, WEST = 3;
  var CHICK_W = 16, CHICK_H = 14, CHICK_SPEED = 14;
  function nameSeed(s) { var h = 7; for (var i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0; return h >>> 0; }

  // 길 위 자리(세상 px): 집 안 · 집 앞 · 갈림길 · 다리 건너 · 동쪽 길 · 계단 위 · 남쪽 길. 갈래 자리는 큰길(PATH_ROW)의 갈림점을 거쳐 간다
  var ROW_Y = Y(6) + 9;
  var SPOTS = {
    home: { x: HOUSE.x0 + WALL + 26, y: HOUSE.y0 + WALL + FACE + 32, via: DOOR_X, door: true },
    door: { x: DOOR_X, y: DOOR_OUT, via: DOOR_X },
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
    function crossDoor(from, to) {
      go(DOOR_X, from, DOOR_SWING);
      pts[pts.length - 1].opensDoor = true;
      go(DOOR_X, to);
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
        if (s.door) {
          crossDoor(DOOR_OUT, DOOR_IN);
          go(s.x, s.y, 4 + hash(seed, 7) * 3);
          crossDoor(DOOR_IN, DOOR_OUT);
        } else go(s.x, s.y, 2.5 + hash(seed, s.x) * 2);
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
    var seg = r.segs[i], a = seg.a, prev = r.segs[(i + r.segs.length - 1) % r.segs.length];
    var age = s - seg.start, elapsed = age - a.pause;
    // 문 앞에서 열릴 때까지 기다리고, 완전히 통과한 뒤 다음 구간에서 닫는다.
    var door = a.opensDoor ? Math.min(1, age / DOOR_SWING) : prev.a.opensDoor ? Math.max(0, 1 - age / DOOR_SWING) : 0;
    if (elapsed < 0) {
      var face = a.water ? (a.water < 0 ? NORTH : SOUTH) : a.opensDoor ? seg.face : prev.face;
      var blink = !still && (t + r.offset * 1000) % 3200 < 120;
      return { x: a.x, y: a.y, face: face, pose: blink ? 5 : 0, water: a.water, door: door };
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
    return { x: a.x + seg.dx * f, y: a.y + seg.dy * f, face: seg.face, pose: pose, water: 0, door: door };
  }
  function drawChick(ctx, p, t) {
    var x = p.x, y = p.y;
    ctx.fillStyle = 'rgba(12,28,8,0.4)';
    ctx.fillRect(x - 5, y - 1, 10, 2);
    ctx.fillRect(x - 4, y, 8, 1);
    // 세상 픽셀로 위치를 반올림하지 않는다. 화면에 확대할 때에도 그림 자체는 보간하지 않는다.
    ctx.drawImage(atlasImg, chickSheet[0] + p.pose * CHICK_W, chickSheet[1] + p.face * CHICK_H, CHICK_W, CHICK_H,
      x - CHICK_W / 2, y - CHICK_H, CHICK_W, CHICK_H);
    if (p.water) {
      var cx = x + 4, cy = y + (p.water < 0 ? -10 : -6);
      paintSprite(ctx, art.sprites[art.can], cx, cy);
      var d = Math.floor(t / 120) % 4;
      ctx.fillStyle = '#bfe6ff';
      ctx.fillRect(cx + 8, cy + 2 + p.water * d, 1, 1);
      ctx.fillRect(cx + 10, cy + 2 + p.water * ((d + 2) % 4), 1, 1);
    }
  }

  // ---------------------------------------------------------------- 그려 두기
  var screen = document.getElementById('world');
  if (!screen) return;
  try {
    atlasImg.src = new URL(script.dataset.atlas, document.baseURI).href;
    ground.src = new URL(script.dataset.ground, document.baseURI).href;
    var assets = await Promise.all([
      fetch(new URL(script.dataset.scene, document.baseURI)).then(function (response) {
        if (!response.ok) throw Error('Could not load village scene.');
        return response.json();
      }),
      atlasImg.decode(), ground.decode()
    ]);
    art = assets[0];
    if (atlasImg.naturalWidth !== art.size[0] || atlasImg.naturalHeight !== art.size[1])
      throw Error('Village sprite sheet dimensions do not match the scene.');
    if (ground.naturalWidth !== WW || ground.naturalHeight !== WH)
      throw Error('Village tilemap dimensions do not match the world.');
    objects = art.objects.map(function (o) { return sceneObject(o[0], o[1], o[2], o[3]); });
    houseDoor = objects[art.door.object];
    chickSheet = art.sprites[art.chicks];
    if (houseDoor) {
      var shift = Math.floor(houseDoor.base) - HOUSE.y1;
      DOOR_X = houseDoor.x + houseDoor.w / 2;
      DOOR_IN += shift;
      DOOR_OUT += shift;
      SPOTS.home.via = SPOTS.door.via = SPOTS.door.x = DOOR_X;
      SPOTS.door.y = DOOR_OUT;
    }
    PLOTS = art.fields.map(function (p) {
      var south = p[1] > ROW_Y;
      return { x: p[0], y: p[1], frontY: p[1] + (south ? -4 : 40), gate: south ? SE_GATE : NE_GATE };
    });
    prepareWater();
  } catch (error) {
    document.getElementById('village-art-error').hidden = false;
    console.error(error);
    return;
  }
  var namesHost = document.getElementById('world-names');
  var sctx = screen.getContext('2d');
  // 그릴 순서: 서 있는 것 + 작물, 발 자리 순
  var scene = [];
  function sortScene() { scene = objects.concat(cropLayer); scene.sort(function (a, b) { return a.base - b.base || a.x - b.x; }); }
  sortScene();
  // 안개: 마을에서 멀수록 짙게(4px 덩어리로 살짝 얼룩지게) — 서 있는 것까지 다 그린 뒤 덮는다
  var fog = canvas(WW, WH);
  var fogCtx = fog.getContext('2d');
  for (var fy = 0; fy < WH; fy += 4)
    for (var fx = 0; fx < WW; fx += 4) {
      var dd = distance(fx / T, fy / T);
      if (dd < 1.5) continue;
      var al = Math.pow(Math.min(1, (dd - 1.5) / 9), 0.8) * 0.78 + (hash(fx, fy) - 0.5) * 0.06;
      fogCtx.fillStyle = 'rgba(16,26,34,' + Math.max(0, al).toFixed(3) + ')';
      fogCtx.fillRect(fx, fy, 4, 4);
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
    cropLayer.length = 0;
    PLOTS.forEach(function (p, k) {
      var owner = residents[k];
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
    var door = 0, at = residents.map(function (r) {
      var p = residentPos(r, t);
      door = Math.max(door, p.door); // 겹쳐 드나들 때는 아직 통과 중인 병아리 앞에서 문이 닫히지 않는다.
      return p;
    });
    if (houseDoor) {
      var doorFrames = art.door.frames;
      houseDoor.rect = art.sprites[doorFrames[Math.round(door * (doorFrames.length - 1))]];
    }
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
    PLOTS.forEach(function (p, i) {
      paintSprite(sctx, art.sprites[art.soils[residents[i] ? 1 : 0]], p.x - 1, p.y - 1);
    });
    drawWater(sctx, t, view);
    // 서 있는 것과 병아리: 발 자리가 위인 것부터(같은 줄이면 병아리가 앞)
    var chicks = at.slice().sort(function (p, q) { return p.y - q.y; });
    var ci = 0;
    for (var i = 0; i < scene.length; i++) {
      var o = scene[i];
      while (ci < chicks.length && chicks[ci].y + 0.5 <= o.base) { drawChick(sctx, chicks[ci++], t); }
      if (o.x + o.w < view.x0 || o.x > view.x1 || o.y + o.h < view.y0 || o.y > view.y1) continue;
      paintSprite(sctx, o.rect, o.x, o.y);
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
})(document.currentScript);
