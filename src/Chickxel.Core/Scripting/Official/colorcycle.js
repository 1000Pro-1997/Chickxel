// 색 순환(ColorCycleRender)을 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "색 순환",
  description: "정한 색들을 차례로 돌려 바꿉니다(물·불·네온처럼 흐르는 색).",
  translations: {
    ko: {
      name: "색 순환",
      description: "정한 색들을 차례로 돌려 바꿉니다(물·불·네온처럼 흐르는 색).",
      labels: { colors: "색", steps: "칸 수", reverse: "거꾸로" },
    },
    en: {
      name: "Color cycle",
      description: "Cycles through chosen colors in turn (flowing colors like water, fire, neon).",
      labels: { colors: "Color", steps: "Steps", reverse: "Reverse" },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "original",
  settings: [
    { key: "colors", label: "색", type: "colorList", min: 2, max: 8, alpha: false, default: ["#2850c8", "#4696f0", "#aae6ff"] },
    { key: "steps", label: "칸 수", type: "int", min: 1, max: 64, default: 3 },
    { key: "reverse", label: "거꾸로", type: "bool", default: false },
  ],
  render(img, ctx, s) {
    const n = s.colors.length;
    if (n < 2) return;
    // 넘어간 칸 수 k = ⌊진행 × 칸 수⌋ mod 색 개수(거꾸로면 −k)
    let k = Math.floor(Math.min(Math.max(ctx.progress, 0), 1) * s.steps + 1e-9) % n;
    if (s.reverse) k = (n - k) % n;
    if (k === 0) return;
    const d = img.data, w = img.width, h = img.height;
    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const i = (y * w + x) * 4;
        if (d[i + 3] === 0) continue;
        const r = d[i], g = d[i + 1], b = d[i + 2];
        // 목록 앞의 색부터 처음 맞는 색 하나만(같은 색이 두 번 있으면 앞의 것)
        for (let j = 0; j < n; j++) {
          const match = ctx.colorAt("colors", j, x, y);
          // 그라데이션에서 고른 알파 0 = 그 자리는 맞추지 않음
          if (match.a === 0 && ctx.hasGradient("colors", j)) continue;
          if (r !== match.r || g !== match.g || b !== match.b) continue;
          const at = (j + k) % n;
          const target = ctx.colorAt("colors", at, x, y);
          if (ctx.hasGradient("colors", at)) {
            // 그라데이션: 고른 알파만큼 RGB를 섞는다(픽셀 알파는 그대로)
            const t = target.a;
            if (t > 0) {
              d[i] = Math.trunc((r * (255 - t) + target.r * t + 127) / 255);
              d[i + 1] = Math.trunc((g * (255 - t) + target.g * t + 127) / 255);
              d[i + 2] = Math.trunc((b * (255 - t) + target.b * t + 127) / 255);
            }
          } else {
            d[i] = target.r;
            d[i + 1] = target.g;
            d[i + 2] = target.b;
          }
          break;
        }
      }
    }
  },
});
