// 터지기(ExplodeRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "터지기",
  description: "픽셀이 조각나 바깥으로 튀어 날아가며 사라집니다.",
  translations: {
    ko: {
      name: "터지기",
      description: "픽셀이 조각나 바깥으로 튀어 날아가며 사라집니다.",
      labels: {
        distanceX: "날아가는 거리 X", distanceY: "날아가는 거리 Y",
        center: "중심", "center.Middle": "가운데", "center.Pivot": "피벗", "center.BottomCenter": "아래 가운데", "center.Custom": "직접 (X, Y)",
        centerX: "직접 중심 (원본 왼쪽 위 기준)", centerY: "직접 중심 Y",
        spreadX: "무작위 변화 X", spreadY: "무작위 변화 Y", gravity: "중력", pieceSize: "조각 크기", shrink: "끝으로 갈수록 작게",
        fade: "사라짐", fadeStart: "사라짐 시작", flashFrames: "번쩍임 프레임", flashColor: "번쩍임 색",
        keepOriginal: "원본 남기기 (복사본이 날아감)", seed: "시드",
      },
    },
    en: {
      name: "Explode",
      description: "Pixels shatter and fly outward as they disappear.",
      labels: {
        distanceX: "Fly distance X", distanceY: "Fly distance Y",
        center: "Center", "center.Middle": "Center", "center.Pivot": "Pivot", "center.BottomCenter": "Bottom center", "center.Custom": "Custom (X, Y)",
        centerX: "Custom center (from original top-left)", centerY: "Custom center Y",
        spreadX: "Random variation X", spreadY: "Random variation Y", gravity: "Gravity", pieceSize: "Fragment size", shrink: "Smaller toward the end",
        fade: "Vanish", fadeStart: "Vanish start", flashFrames: "Flash frames", flashColor: "Flash color",
        keepOriginal: "Keep original (a copy flies off)", seed: "Seed",
      },
    },
  },
  animated: true,
  easing: "EaseOut",
  previewChick: "original",
  // 가로·세로 거리만큼, 세로는 중력 방향으로 중력만큼 더
  padding: (s) => [s.distanceX, s.distanceY + Math.max(0, -s.gravity), s.distanceX, s.distanceY + Math.max(0, s.gravity)],
  settings: [
    { key: "distanceX", label: "날아가는 거리 X", type: "int", min: 0, max: 128, default: 16 },
    { key: "distanceY", label: "날아가는 거리 Y", type: "int", min: 0, max: 128, default: 16 },
    {
      key: "center", label: "중심", type: "choice", default: "Middle",
      choices: [
        { value: "Middle", label: "가운데" }, { value: "Pivot", label: "피벗" },
        { value: "BottomCenter", label: "아래 가운데" }, { value: "Custom", label: "직접 (X, Y)" },
      ],
    },
    { key: "centerX", label: "직접 중심 (원본 왼쪽 위 기준)", type: "int", min: -16384, max: 16384, default: 0, widget: "point", pair: "centerY" },
    { key: "centerY", label: "직접 중심 Y", type: "int", min: -16384, max: 16384, default: 0 },
    { key: "spreadX", label: "무작위 변화 X", type: "int", min: 0, max: 100, default: 50 },
    { key: "spreadY", label: "무작위 변화 Y", type: "int", min: 0, max: 100, default: 50 },
    { key: "gravity", label: "중력", type: "int", min: -64, max: 64, default: 6 },
    { key: "pieceSize", label: "조각 크기", type: "int", min: 1, max: 4, default: 1 },
    { key: "shrink", label: "끝으로 갈수록 작게", type: "bool", default: false },
    { key: "fade", label: "사라짐", type: "bool", default: true },
    { key: "fadeStart", label: "사라짐 시작", type: "int", min: 0, max: 100, default: 40 },
    { key: "flashFrames", label: "번쩍임 프레임", type: "int", min: 0, max: 8, default: 0 },
    { key: "flashColor", label: "번쩍임 색", type: "color", default: "#ffffff" },
    { key: "keepOriginal", label: "원본 남기기 (복사본이 날아감)", type: "bool", default: false },
    { key: "seed", label: "시드", type: "int", min: 0, max: 9999, default: 1 },
  ],
  render(img, ctx, s) {
    // 움직임은 진행(빠르기 곡선), 사라짐·작아짐은 반복만 넣은 시간
    const u = ctx.phase, t = ctx.progress;
    const f = s.flashFrames, step = ctx.step;
    const flash = f > 0 && step >= 1 && step <= f ? Math.trunc((255 * (f - step + 1) + Math.trunc(f / 2)) / f) : 0;
    if (u <= 0 && flash === 0) return; // 시작 = 원래 그림 그대로
    const fade = !s.fade ? 1 : s.fadeStart >= 100 ? (u >= 1 ? 0 : 1) : Math.min(Math.max((1 - u) / (1 - s.fadeStart / 100), 0), 1);
    if (s.distanceX === 0 && s.distanceY === 0 && s.gravity === 0 && flash === 0 && fade === 1 && (!s.shrink || s.pieceSize === 1)) return;
    const w = img.width, h = img.height, d = img.data;

    // 보이는 픽셀을 감싸는 범위
    let left = w, top = h, right = 0, bottom = 0;
    for (let y = 0; y < h; y++) {
      const row = y * w * 4 + 3;
      let first = 0;
      while (first < w && d[row + first * 4] === 0) first++;
      if (first === w) continue;
      let last = w - 1;
      while (d[row + last * 4] === 0) last--;
      if (first < left) left = first;
      if (last + 1 > right) right = last + 1;
      if (y < top) top = y;
      bottom = y + 1;
    }
    if (right <= left) return;

    // Color32.BlendOver(float 계산)를 그대로: d의 칸 i 위에 (r, g, b, a)를 겹친다
    const fr = (v) => Math.fround(v);
    const roundEven = (v) => {
      const r = Math.round(v);
      return r - v === 0.5 && r % 2 !== 0 ? r - 1 : r;
    };
    const channel = (sc, dc, sa, da, inv, oa) => Math.min(Math.max(roundEven(fr(fr(fr(sc * sa) + fr(fr(dc * da) * inv)) / oa)), 0), 255);
    const over = (i, r, g, b, a) => {
      const below = d[i + 3];
      if (a === 255 || below === 0) {
        d[i] = r;
        d[i + 1] = g;
        d[i + 2] = b;
        d[i + 3] = a;
      } else if (a !== 0) {
        const sa = fr(a / 255), da = fr(below / 255), inv = fr(1 - sa), oa = fr(sa + fr(da * inv));
        d[i] = channel(r, d[i], sa, da, inv, oa);
        d[i + 1] = channel(g, d[i + 1], sa, da, inv, oa);
        d[i + 2] = channel(b, d[i + 2], sa, da, inv, oa);
        d[i + 3] = Math.min(Math.max(roundEven(fr(oa * 255)), 0), 255);
      }
    };

    const n = s.pieceSize, src = new Uint8Array(d);
    for (let i = 0; i < d.length; i++) d[i] = 0;
    // 조각은 차례대로 겹쳐 놓는다(나중 조각이 위)
    if (fade > 0) {
      const sx = ctx.source.x, sy = ctx.source.y;
      let cx = left + (right - left) / 2, cy = top + (bottom - top) / 2;
      if (s.center === "BottomCenter") cy = bottom;
      else if (s.center === "Custom") {
        cx = sx + s.centerX + 0.5;
        cy = sy + s.centerY + 0.5;
      } else if (s.center === "Pivot") {
        cx = ctx.pivot.x;
        cy = ctx.pivot.y;
      }
      const size = s.shrink ? Math.max(1, Math.ceil(n * (1 - u) - 1e-9)) : n, inset = Math.trunc((n - size) / 2);
      const spreadX = s.spreadX / 100, spreadY = s.spreadY / 100, fall = s.gravity * u * u;
      const distanceX = s.distanceX, distanceY = s.distanceY, seed = s.seed;
      // 바깥 조각일수록 멀리(중심 조각은 거리의 40%)
      const reach = Math.max(1, Math.max(Math.max(Math.abs(left - cx), Math.abs(right - cx)), Math.max(Math.abs(top - cy), Math.abs(bottom - cy))));
      // C# Math.Round(AwayFromZero)
      const away = (v) => (v < 0 ? -Math.round(-v) : Math.round(v));
      const gradient = ctx.hasGradient("flashColor"), solid = s.flashColor;
      // 조각 격자는 원본 왼쪽 위 기준
      const gx0 = Math.floor((left - sx) / n), gx1 = Math.floor((right - 1 - sx) / n);
      const gy0 = Math.floor((top - sy) / n), gy1 = Math.floor((bottom - 1 - sy) / n);
      for (let gy = gy0; gy <= gy1; gy++) {
        for (let gx = gx0; gx <= gx1; gx++) {
          const x0 = sx + gx * n, y0 = sy + gy * n;
          const vx = x0 + n / 2 - cx, vy = y0 + n / 2 - cy;
          const angle = vx * vx + vy * vy < 1e-9 ? ctx.hash(gx, gy, seed, 0) * 2 * Math.PI : Math.atan2(vy, vx);
          const jitter = ctx.hash(gx, gy, seed, 1) * 2 - 1;
          const radial = 0.4 + 0.6 * Math.min(1, Math.sqrt(vx * vx + vy * vy) / reach);
          const speedRandom = ctx.hash(gx, gy, seed, 2);
          const angleX = angle + jitter * spreadX * Math.PI / 2;
          const travelX = distanceX * radial * (1 - spreadX * 0.6 * speedRandom) * t;
          const dx = distanceX === 0 ? 0 : away(Math.cos(angleX) * travelX);
          const angleY = spreadX === spreadY ? angleX : angle + jitter * spreadY * Math.PI / 2;
          const travelY = spreadX === spreadY && distanceX === distanceY ? travelX
            : distanceY * radial * (1 - spreadY * 0.6 * speedRandom) * t;
          const dy = distanceY === 0 ? away(fall) : away(Math.sin(angleY) * travelY + fall);
          for (let yy = y0 + inset; yy < y0 + inset + size; yy++) {
            const ty = yy + dy;
            if (yy < 0 || yy >= h || ty < 0 || ty >= h) continue;
            for (let xx = x0 + inset; xx < x0 + inset + size; xx++) {
              const tx = xx + dx;
              if (xx < 0 || xx >= w || tx < 0 || tx >= w) continue;
              const k = (yy * w + xx) * 4;
              let a = src[k + 3];
              if (a === 0) continue;
              let r = src[k], g = src[k + 1], b = src[k + 2];
              if (flash > 0) {
                // 번쩍임 색 쪽으로(그라데이션이면 고른 색의 알파가 세기를 줄인다)
                let cr = solid.r, cg = solid.g, cb = solid.b, amount = flash;
                if (gradient) {
                  const sampled = ctx.color("flashColor", xx, yy);
                  cr = sampled.r;
                  cg = sampled.g;
                  cb = sampled.b;
                  amount = Math.trunc((flash * sampled.a + 127) / 255);
                }
                if (amount > 0) {
                  const keep = 255 - amount;
                  r = Math.trunc((r * keep + cr * amount + 127) / 255);
                  g = Math.trunc((g * keep + cg * amount + 127) / 255);
                  b = Math.trunc((b * keep + cb * amount + 127) / 255);
                }
              }
              if (fade < 1) {
                a = roundEven(a * fade);
                if (a === 0) continue;
              }
              over((ty * w + tx) * 4, r, g, b, a);
            }
          }
        }
      }
    }
    // 원본 남기기: 날아간 조각 위에 원래 그림을 다시 올린다
    if (s.keepOriginal) {
      for (let i = 0; i < d.length; i += 4) {
        if (src[i + 3] !== 0) over(i, src[i], src[i + 1], src[i + 2], src[i + 3]);
      }
    }
  },
});
