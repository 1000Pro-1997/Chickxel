// 홀로그램(HologramRender)을 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
// 차례: 글리치 조각 밀기 → 보이는 픽셀마다 색(명암 그대로) → 밝은 띠 또는 주사선 → RGB 분리(글리치 프레임) → 알파 × 투명도 × 깜빡임 → 테두리 빛.
effect({
  name: "홀로그램",
  description: "한 색의 투사 영상처럼 물들이고 주사선·밝은 띠·깜빡임·글리치를 더합니다(SF 홀로그램).",
  translations: {
    ko: {
      name: "홀로그램",
      description: "한 색의 투사 영상처럼 물들이고 주사선·밝은 띠·깜빡임·글리치를 더합니다(SF 홀로그램).",
      labels: {
        color: "색", tintStrength: "색 세기", scanGap: "주사선 간격", scanStrength: "주사선 세기",
        scanStyle: "주사선 방식", "scanStyle.Darken": "어둡게", "scanStyle.Fade": "투명하게", scanScroll: "흐르기",
        band: "밝은 띠", bandSize: "띠 두께", bandStrength: "띠 세기", opacity: "투명도", dither: "디더 (반투명 대신 Bayer 무늬)",
        flickerRate: "깜빡임 빈도", flickerStrength: "깜빡임 세기", glitchRate: "글리치 빈도", glitchShift: "최대 밀림",
        rgbSplit: "RGB 분리 (빨강·파랑 1px 어긋남)", edgeGlow: "테두리 빛", edgeOpacity: "테두리 빛 세기", seed: "시드",
      },
    },
    en: {
      name: "Hologram",
      description: "Tints like a single-color projection and adds scanlines, bright bands, blink and glitch (sci-fi hologram).",
      labels: {
        color: "Color", tintStrength: "Color strength", scanGap: "Scanline spacing", scanStrength: "Scanline strength",
        scanStyle: "Scanline mode", "scanStyle.Darken": "Darken", "scanStyle.Fade": "Transparent", scanScroll: "Scroll",
        band: "Bright band", bandSize: "Band thickness", bandStrength: "Band strength", opacity: "Opacity", dither: "Dither (Bayer pattern instead of translucency)",
        flickerRate: "Blink frequency", flickerStrength: "Blink strength", glitchRate: "Glitch frequency", glitchShift: "Max shift",
        rgbSplit: "RGB split (red and blue offset by 1px)", edgeGlow: "Edge glow", edgeOpacity: "Edge glow strength", seed: "Seed",
      },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "original",
  // 글리치 조각이 좌우로 최대 밀림만큼, 테두리 빛이면 사방으로 1px 더
  padding: (s) => {
    const edge = s.edgeGlow ? 1 : 0, reach = s.glitchRate > 0 ? s.glitchShift : 0;
    return [reach + edge, edge, reach + edge, edge];
  },
  settings: [
    { key: "color", label: "색", type: "color", default: "#50dcff" },
    { key: "tintStrength", label: "색 세기", type: "int", min: 0, max: 100, default: 85 },
    { key: "scanGap", label: "주사선 간격", type: "int", min: 2, max: 6, default: 2 },
    { key: "scanStrength", label: "주사선 세기", type: "int", min: 0, max: 100, default: 40 },
    {
      key: "scanStyle", label: "주사선 방식", type: "choice", default: "Darken",
      choices: [{ value: "Darken", label: "어둡게" }, { value: "Fade", label: "투명하게" }],
    },
    { key: "scanScroll", label: "흐르기", type: "bool", default: true },
    { key: "band", label: "밝은 띠", type: "bool", default: true },
    { key: "bandSize", label: "띠 두께", type: "int", min: 1, max: 16, default: 2 },
    { key: "bandStrength", label: "띠 세기", type: "int", min: 0, max: 100, default: 60 },
    { key: "opacity", label: "투명도", type: "int", min: 0, max: 100, default: 75 },
    { key: "dither", label: "디더 (반투명 대신 Bayer 무늬)", type: "bool", default: false },
    { key: "flickerRate", label: "깜빡임 빈도", type: "int", min: 0, max: 100, default: 15 },
    { key: "flickerStrength", label: "깜빡임 세기", type: "int", min: 0, max: 100, default: 40 },
    { key: "glitchRate", label: "글리치 빈도", type: "int", min: 0, max: 100, default: 20 },
    { key: "glitchShift", label: "최대 밀림", type: "int", min: 1, max: 8, default: 2 },
    { key: "rgbSplit", label: "RGB 분리 (빨강·파랑 1px 어긋남)", type: "bool", default: true },
    { key: "edgeGlow", label: "테두리 빛", type: "bool", default: true },
    { key: "edgeOpacity", label: "테두리 빛 세기", type: "int", min: 0, max: 100, default: 50 },
    { key: "seed", label: "시드", type: "int", min: 0, max: 9999, default: 2 },
  ],
  render(img, ctx, s) {
    const w = img.width, h = img.height, d = img.data;

    // 보이는 픽셀을 감싸는 범위(글리치 전 — 띠·글리치 조각·그라데이션 기준)
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
    const height = bottom - top, step = ctx.step, seed = s.seed, sx = ctx.source.x, sy = ctx.source.y;

    // 글리치: 범위 안 프레임 번호와 시드로 정한 1~3개 가로 조각을 좌우로 민다(원래 그림에서 읽음 — 겹치면 뒤 조각이 이김)
    const glitch = s.glitchRate > 0 && ctx.hash(step, 0, seed, 43) < s.glitchRate / 100;
    const pieces = glitch ? 1 + Math.trunc(ctx.hash(step, 0, seed, 44) * 3) : 0;
    if (pieces > 0) {
      let first = 2147483647, last = -2147483648;
      for (let j = 0; j < pieces; j++) {
        const at = top + Math.trunc(ctx.hash(step, 1 + j * 4, seed, 44) * height);
        const rows = 1 + Math.trunc(ctx.hash(step, 2 + j * 4, seed, 44) * 3);
        first = Math.min(first, at);
        last = Math.max(last, Math.min(bottom, at + rows));
      }
      if (last > first) {
        const keep = new Uint8Array((last - first) * w * 4);
        for (let i = 0; i < keep.length; i++) keep[i] = d[first * w * 4 + i];
        for (let j = 0; j < pieces; j++) {
          const at = top + Math.trunc(ctx.hash(step, 1 + j * 4, seed, 44) * height);
          const rows = 1 + Math.trunc(ctx.hash(step, 2 + j * 4, seed, 44) * 3);
          const distance = 1 + Math.trunc(ctx.hash(step, 3 + j * 4, seed, 44) * s.glitchShift);
          const shift = ctx.hash(step, 4 + j * 4, seed, 44) < 0.5 ? -distance : distance;
          const end = Math.min(bottom, at + rows);
          for (let y = at; y < end; y++) {
            const row = y * w * 4, from = (y - first) * w * 4;
            for (let x = 0; x < w; x++) {
              const ox = x - shift, i = row + x * 4;
              if (ox >= 0 && ox < w) {
                const k = from + ox * 4;
                d[i] = keep[k];
                d[i + 1] = keep[k + 1];
                d[i + 2] = keep[k + 2];
                d[i + 3] = keep[k + 3];
              } else {
                d[i] = 0;
                d[i + 1] = 0;
                d[i + 2] = 0;
                d[i + 3] = 0;
              }
            }
          }
        }
      }
    }

    // 깜빡임: 깜빡이는 프레임이면 1 − 세기 × (0.5~1)
    let flicker = 1;
    if (s.flickerRate !== 0 && s.flickerStrength !== 0 && ctx.hash(step, 0, seed, 41) < s.flickerRate / 100)
      flicker = 1 - s.flickerStrength / 100 * (0.5 + 0.5 * ctx.hash(step, 1, seed, 41));

    const u = ctx.progress;
    const tint = Math.trunc((s.tintStrength * 255 + 50) / 100), band = s.band ? Math.trunc((s.bandStrength * 255 + 50) / 100) : 0, scan = s.scanStrength;
    const solid = s.color, gradient = ctx.hasGradient("color");
    // 단색 띠 빛 = 색과 흰색 가운데(Mix 128)
    const lightR = Math.trunc((solid.r * 127 + 255 * 128 + 127) / 255);
    const lightG = Math.trunc((solid.g * 127 + 255 * 128 + 127) / 255);
    const lightB = Math.trunc((solid.b * 127 + 255 * 128 + 127) / 255);
    const gap = s.scanGap, offset = s.scanScroll ? Math.floor(Math.min(Math.max(u, 0), 1) * gap) % gap : 0;
    // 띠 위 끝: 진행 0 = 보이는 그림 바로 위(두께만큼), 1 = 바로 아래 (C# AwayFromZero 반올림)
    const bandPos = top - s.bandSize + (bottom - top + s.bandSize) * u;
    const bandTop = bandPos < 0 ? -Math.round(-bandPos) : Math.round(bandPos);
    const opacity = s.opacity / 100 * flicker;
    const split = s.rgbSplit && pieces > 0, darken = s.scanStyle === "Darken", dither = s.dither;

    // 모양(글리치 뒤 보이는 픽셀)과 한 줄의 색(RGB 분리가 같은 줄 이웃의 결과를 봄)
    const shape = new Uint8Array(w * h);
    for (let i = 0; i < w * h; i++) shape[i] = d[i * 4 + 3] !== 0 ? 1 : 0;
    const line = new Uint8Array(w * 3);

    // 홀로그램 색: 밝기 l에서 목표 = 색 × (0.3 + 0.7 × l/192)(l ≤ 192), 그 위는 흰색 쪽으로 0.6 × (l − 192)/63
    const toward = (k, l) => (l <= 192 ? Math.round(k * (0.3 + 0.7 * l / 192)) : Math.round(k + (255 - k) * 0.6 * (l - 192) / 63));
    const mix = (v, c, amount) => Math.trunc((v * (255 - amount) + c * amount + 127) / 255);
    const screen = (v, c, amount) => v + Math.trunc(((255 - Math.trunc((255 - v) * (255 - c) / 255) - v) * amount + 127) / 255);
    // 알파 × keep(디더면 4×4 Bayer 칸만 알파 그대로, 나머지는 투명)를 d의 칸 i에 쓴다
    const put = (i, r, g, b, a, keep, bx, by) => {
      let out = a;
      if (keep < 1) {
        if (dither) out = keep * 16 > ctx.bayer4(bx, by) + 0.5 - 1e-9 ? a : 0;
        else out = Math.round(a * keep);
      }
      if (out === 0) {
        d[i] = 0;
        d[i + 1] = 0;
        d[i + 2] = 0;
        d[i + 3] = 0;
      } else {
        d[i] = r;
        d[i + 1] = g;
        d[i + 2] = b;
        d[i + 3] = out;
      }
    };

    for (let y = top; y < bottom; y++) {
      const inBand = band > 0 && y >= bandTop && y < bandTop + s.bandSize;
      const scanRow = !inBand && scan > 0 && ((y - sy - offset) % gap + gap) % gap === 0;
      const keep = opacity * (scanRow && !darken ? 1 - scan / 100 : 1);
      const row = y * w;
      for (let x = 0; x < w; x++) {
        if (shape[row + x] === 0) continue;
        const i = (row + x) * 4;
        let pr = d[i], pg = d[i + 1], pb = d[i + 2];
        let cr = solid.r, cg = solid.g, cb = solid.b, ca = 255;
        if (gradient) {
          const sampled = ctx.color("color", x, y);
          cr = sampled.r;
          cg = sampled.g;
          cb = sampled.b;
          ca = sampled.a;
        }
        const tintAmount = gradient ? Math.trunc((tint * ca + 127) / 255) : tint;
        if (tintAmount > 0) {
          const l = 0.299 * pr + 0.587 * pg + 0.114 * pb;
          pr = mix(pr, toward(cr, l), tintAmount);
          pg = mix(pg, toward(cg, l), tintAmount);
          pb = mix(pb, toward(cb, l), tintAmount);
        }
        if (inBand) {
          let lr = lightR, lg = lightG, lb = lightB, bandAmount = band;
          if (gradient) {
            lr = Math.trunc((cr * 127 + 255 * 128 + 127) / 255);
            lg = Math.trunc((cg * 127 + 255 * 128 + 127) / 255);
            lb = Math.trunc((cb * 127 + 255 * 128 + 127) / 255);
            bandAmount = Math.trunc((band * ca + 127) / 255);
          }
          pr = screen(pr, lr, bandAmount);
          pg = screen(pg, lg, bandAmount);
          pb = screen(pb, lb, bandAmount);
        } else if (scanRow && darken) {
          const rest = 100 - scan;
          pr = Math.trunc((pr * rest + 50) / 100);
          pg = Math.trunc((pg * rest + 50) / 100);
          pb = Math.trunc((pb * rest + 50) / 100);
        }
        line[x * 3] = pr;
        line[x * 3 + 1] = pg;
        line[x * 3 + 2] = pb;
      }
      for (let x = 0; x < w; x++) {
        if (shape[row + x] === 0) continue;
        let r = line[x * 3], b = line[x * 3 + 2];
        if (split) {
          r = x > 0 && shape[row + x - 1] !== 0 ? line[(x - 1) * 3] : 0;
          b = x + 1 < w && shape[row + x + 1] !== 0 ? line[(x + 1) * 3 + 2] : 0;
        }
        const i = (row + x) * 4;
        put(i, r, line[x * 3 + 1], b, d[i + 3], keep, x - sx, y - sy);
      }
    }

    // 테두리 빛: 모양 밖 4방향 이웃 1px에 색(알파 255 × 세기, 디더면 무늬 칸만 불투명)
    if (s.edgeGlow && s.edgeOpacity > 0) {
      const edgeKeep = opacity * s.edgeOpacity / 100;
      for (let y = Math.max(0, top - 1); y < Math.min(h, bottom + 1); y++) {
        for (let x = 0; x < w; x++) {
          const at = y * w + x;
          if (shape[at] !== 0) continue;
          if (!(x > 0 && shape[at - 1] !== 0 || x + 1 < w && shape[at + 1] !== 0
            || y > 0 && shape[at - w] !== 0 || y + 1 < h && shape[at + w] !== 0)) continue;
          let cr = solid.r, cg = solid.g, cb = solid.b, keep = edgeKeep;
          if (gradient) {
            const sampled = ctx.color("color", x, y);
            cr = sampled.r;
            cg = sampled.g;
            cb = sampled.b;
            keep = edgeKeep * (sampled.a / 255);
          }
          const i = at * 4;
          // 모양 밖은 투명이라 보이는 결과만 그대로 놓는다
          let out = 255;
          if (keep < 1) out = dither ? (keep * 16 > ctx.bayer4(x - sx, y - sy) + 0.5 - 1e-9 ? 255 : 0) : Math.round(255 * keep);
          if (out !== 0) {
            d[i] = cr;
            d[i + 1] = cg;
            d[i + 2] = cb;
            d[i + 3] = out;
          }
        }
      }
    }
  },
});
