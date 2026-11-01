// 색 바꾸기(PaletteSwapRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "색 바꾸기",
  language: "ko",
  description: "고른 색을 다른 색으로 바꿉니다(팔레트 스왑 — 같은 그림의 색만 다른 버전).",
  translations: {
    ko: {
      name: "색 바꾸기",
      description: "고른 색을 다른 색으로 바꿉니다(팔레트 스왑 — 같은 그림의 색만 다른 버전).",
      labels: { pairs: "원본 → 대상", tolerance: "허용 차이" },
    },
    en: {
      name: "Palette swap",
      description: "Swaps chosen colors for other colors (palette swap — the same image in different colors).",
      labels: { pairs: "Source → target", tolerance: "Tolerance" },
    },
  },
  animated: false,
  previewChick: "original",
  settings: [
    { key: "pairs", label: "원본 → 대상", type: "colorPairs", min: 0, max: 64, default: [] },
    { key: "tolerance", label: "허용 차이", type: "int", min: 0, max: 255, default: 0 },
  ],
  render(img, ctx, s) {
    const n = s.pairs.length;
    if (n === 0) return;
    const tol = s.tolerance, d = img.data, w = img.width, h = img.height;
    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const i = (y * w + x) * 4;
        const a = d[i + 3];
        if (a === 0) continue;
        const r = d[i], g = d[i + 1], b = d[i + 2];
        // 위의 쌍부터 처음 맞는 쌍 하나만(바꾼 색을 다시 바꾸지 않음)
        for (let j = 0; j < n; j++) {
          const from = ctx.pairColor("pairs", j, "from", x, y);
          // 그라데이션에서 고른 알파 0 = 그 자리는 맞추지 않음(단색 알파는 보지 않음)
          if (from.a === 0 && ctx.hasGradient("pairs", j, "from")) continue;
          if (Math.abs(r - from.r) > tol || Math.abs(g - from.g) > tol || Math.abs(b - from.b) > tol) continue;
          const to = ctx.pairColor("pairs", j, "to", x, y);
          if (ctx.hasGradient("pairs", j, "to")) {
            // 그라데이션: 고른 알파만큼 RGB를 섞는다(픽셀 알파는 그대로)
            const k = to.a;
            if (k > 0) {
              d[i] = Math.trunc((r * (255 - k) + to.r * k + 127) / 255);
              d[i + 1] = Math.trunc((g * (255 - k) + to.g * k + 127) / 255);
              d[i + 2] = Math.trunc((b * (255 - k) + to.b * k + 127) / 255);
            }
          } else {
            // 단색: RGB = 대상, 알파 = 픽셀 알파 × 대상 알파 / 255(대상 알파 0 = 지우기)
            d[i] = to.r;
            d[i + 1] = to.g;
            d[i + 2] = to.b;
            d[i + 3] = Math.trunc((a * to.a + 127) / 255);
          }
          break;
        }
      }
    }
  },
});
