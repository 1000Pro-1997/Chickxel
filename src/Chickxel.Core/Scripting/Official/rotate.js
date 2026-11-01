// 회전(RotateRender)을 JS로 옮긴 공식 스크립트 효과. 같은 설정이면 C# 결과와 픽셀 단위로 같아야 한다.
// RotSprite 방식: 보이는 그림을 Scale2x(EPX)로 k번 키우고(한 변 128px 이하 3번, 256px 이하 2번, 512px 이하 1번) 결과 픽셀마다 가운데 점을
// 거꾸로 돌려 키운 그림에서 최근접으로 집는다. 90°의 배수는 키우지 않는다(손실 없음).
effect({
  name: "회전",
  language: "ko",
  description: "그림을 돌립니다(RotSprite — 새 색·반투명 없이).",
  translations: {
    ko: {
      name: "회전",
      description: "그림을 돌립니다(RotSprite — 새 색·반투명 없이).",
      labels: {
        angle: "각도 (음수 = 반시계 방향, 360 = 한 바퀴)", anchor: "기준점",
        "anchor.BottomCenter": "아래 가운데 (발 고정)", "anchor.Middle": "가운데", "anchor.Pivot": "피벗",
      },
    },
    en: {
      name: "Rotate",
      description: "Rotates the image (RotSprite — no new colors or translucency).",
      labels: {
        angle: "Angle (negative = counterclockwise, 360 = full turn)", anchor: "Pivot",
        "anchor.BottomCenter": "Bottom center (feet fixed)", "anchor.Middle": "Center", "anchor.Pivot": "Pivot",
      },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "original",
  settings: [
    { key: "angle", label: "각도 (음수 = 반시계 방향, 360 = 한 바퀴)", type: "int", min: -1440, max: 1440, default: 360, widget: "angle" },
    {
      key: "anchor", label: "기준점", type: "choice", default: "Pivot",
      choices: [{ value: "BottomCenter", label: "아래 가운데 (발 고정)" }, { value: "Middle", label: "가운데" }, { value: "Pivot", label: "피벗" }],
    },
  ],
  // 지금까지 넓힌 크기를 기준점 둘레로 돌린 그림이 닿을 수 있는 만큼(가운데: 가로 ⌈높이/2⌉·세로 ⌈너비/2⌉, 아래 가운데: 가로 높이·위 ⌈너비/2⌉·
  // 아래 √((너비/2)² + 높이²), 피벗: 사방 대각선 길이 — 각 +1). 각도가 0이면 없음
  padding(s, width, height) {
    if (s.angle === 0) return [0, 0, 0, 0];
    const w2 = width / 2;
    if (s.anchor === "Middle") {
      const x = Math.ceil(height / 2) + 1, y = Math.ceil(w2) + 1;
      return [x, y, x, y];
    }
    if (s.anchor === "BottomCenter") {
      const x = height + 1;
      return [x, Math.ceil(w2) + 1, x, Math.ceil(Math.sqrt(w2 * w2 + height * height)) + 1];
    }
    const d = Math.ceil(Math.sqrt(width * width + height * height)) + 1;
    return [d, d, d, d];
  },
  render(img, ctx, s) {
    const deg = s.angle * Math.min(Math.max(ctx.progress, 0), 1);
    const turn = deg % 360;
    if (Math.abs(turn) < 1e-9) return; // 0° · 한 바퀴 = 원래 그림
    const w = img.width, h = img.height, d = img.data;

    // 보이는 픽셀을 감싸는 범위(오른쪽·아래는 끝 다음 칸)
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
    const boundsW = right - left, boundsH = bottom - top;

    let ax = ctx.pivot.x, ay = ctx.pivot.y;
    if (s.anchor === "BottomCenter") { ax = left + boundsW / 2; ay = bottom; }
    else if (s.anchor === "Middle") { ax = left + boundsW / 2; ay = top + boundsH / 2; }

    const quarter = turn / 90;
    const exact = Math.abs(quarter - Math.round(quarter)) < 1e-9;
    let cos = 0, sin = 0;
    if (exact) {
      const q = ((Math.round(quarter) % 4) + 4) % 4;
      cos = q === 0 ? 1 : q === 2 ? -1 : 0;
      sin = q === 1 ? 1 : q === 3 ? -1 : 0;
    } else {
      const r = deg * Math.PI / 180;
      cos = Math.cos(r);
      sin = Math.sin(r);
    }

    // 범위 둘레 1px(Scale2x 이웃 투명)까지 잘라 키운다. 색은 RGBA를 한 정수로(알파 0은 모두 같은 투명 0)
    const cropX = left - 1, cropY = top - 1, cropW = boundsW + 2, cropH = boundsH + 2;
    const longest = Math.max(boundsW, boundsH);
    const levels = exact ? 0 : longest <= 128 ? 3 : longest <= 256 ? 2 : longest <= 512 ? 1 : 0;
    const scale = 1 << levels;
    const bigW = cropW * scale, bigH = cropH * scale;
    // 키운 그림 두 장을 한 배열에 이어 두고 단계마다 읽는 쪽(from)·쓰는 쪽(into)을 바꾼다
    const size = bigW * bigH;
    const big = new Int32Array(levels > 0 ? size * 2 : size);
    let from = 0, into = size;
    for (let cy = 0; cy < cropH; cy++) {
      const y = cropY + cy;
      if (y < 0 || y >= h) continue;
      for (let cx = 0; cx < cropW; cx++) {
        const x = cropX + cx;
        if (x < 0 || x >= w) continue;
        const i = (y * w + x) * 4;
        if (d[i + 3] !== 0) big[cy * cropW + cx] = (d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3];
      }
    }

    // Scale2x(EPX): 픽셀 하나를 2×2로, 이웃 모양을 따라 모서리를 이웃 색으로(새 색 없음)
    for (let k = 0, sw = cropW, sh = cropH; k < levels; k++, sw *= 2, sh *= 2) {
      const dw = sw * 2;
      for (let y = 0; y < sh; y++) {
        for (let x = 0; x < sw; x++) {
          const at = from + y * sw + x;
          const e = big[at];
          const b = y > 0 ? big[at - sw] : 0;
          const dd = x > 0 ? big[at - 1] : 0;
          const f = x < sw - 1 ? big[at + 1] : 0;
          const hh = y < sh - 1 ? big[at + sw] : 0;
          let e0 = e, e1 = e, e2 = e, e3 = e;
          if (b !== hh && dd !== f) {
            if (dd === b) e0 = dd;
            if (b === f) e1 = f;
            if (dd === hh) e2 = dd;
            if (hh === f) e3 = f;
          }
          const o = into + y * 2 * dw + x * 2;
          big[o] = e0;
          big[o + 1] = e1;
          big[o + dw] = e2;
          big[o + dw + 1] = e3;
        }
      }
      const swap = from;
      from = into;
      into = swap;
    }

    for (let i = 0, end = d.length; i < end; i += 4) {
      d[i] = 0;
      d[i + 1] = 0;
      d[i + 2] = 0;
      d[i + 3] = 0;
    }
    // 돌린 그림이 닿는 자리(잘라 낸 범위 네 모서리를 돌린 사각형, 한 칸 여유)
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (let corner = 0; corner < 4; corner++) {
      const dx = (corner % 2 === 0 ? cropX : cropX + cropW) - ax, dy = (corner < 2 ? cropY : cropY + cropH) - ay;
      const rx = ax + cos * dx - sin * dy, ry = ay + sin * dx + cos * dy;
      minX = Math.min(minX, rx);
      maxX = Math.max(maxX, rx);
      minY = Math.min(minY, ry);
      maxY = Math.max(maxY, ry);
    }
    const x0 = Math.max(0, Math.floor(minX) - 1), x1 = Math.min(w, Math.ceil(maxX) + 1);
    const y0 = Math.max(0, Math.floor(minY) - 1), y1 = Math.min(h, Math.ceil(maxY) + 1);
    for (let y = y0; y < y1; y++) {
      const dy = y + 0.5 - ay;
      for (let x = x0; x < x1; x++) {
        const dx = x + 0.5 - ax;
        // 거꾸로 돌린 자리(원본 좌표) → 키운 그림 칸
        const sx = ax + cos * dx + sin * dy, sy = ay - sin * dx + cos * dy;
        const ix = Math.floor((sx - cropX) * scale), iy = Math.floor((sy - cropY) * scale);
        if (ix < 0 || ix >= bigW || iy < 0 || iy >= bigH) continue;
        const c = big[from + iy * bigW + ix];
        if (c === 0) continue;
        const o = (y * w + x) * 4;
        d[o] = c >>> 24;
        d[o + 1] = (c >>> 16) & 255;
        d[o + 2] = (c >>> 8) & 255;
        d[o + 3] = c & 255;
      }
    }
  },
});
