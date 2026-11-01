// 둥실둥실(ShakeRender의 BobEffect 부분)을 JS로 옮긴 공식 스크립트 효과. 같은 설정이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "둥실둥실",
  language: "ko",
  description: "위아래로 부드럽게 떠오르고 가라앉습니다(대기 동작).",
  translations: {
    ko: {
      name: "둥실둥실",
      description: "위아래로 부드럽게 떠오르고 가라앉습니다(대기 동작).",
      labels: { height: "높이", cycles: "횟수", axis: "방향", "axis.Vertical": "위아래", "axis.Horizontal": "좌우" },
    },
    en: {
      name: "Bob",
      description: "Gently floats up and down (idle motion).",
      labels: { height: "Height", cycles: "Count", axis: "Direction", "axis.Vertical": "Up/down", "axis.Horizontal": "Left/right" },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "astronaut",
  settings: [
    { key: "height", label: "높이", type: "int", min: 1, max: 32, default: 2 },
    { key: "cycles", label: "횟수", type: "int", min: 1, max: 8, default: 1 },
    {
      key: "axis", label: "방향", type: "choice", default: "Vertical",
      choices: [{ value: "Vertical", label: "위아래" }, { value: "Horizontal", label: "좌우" }],
    },
  ],
  // 오르내리는 축으로 높이만큼 넓힌다
  padding(s) {
    return s.axis === "Horizontal" ? [s.height, 0, s.height, 0] : [0, s.height, 0, s.height];
  },
  render(img, ctx, s) {
    // 옮김 = round(높이 × sin(2π × 횟수 × 진행))px(AwayFromZero), 시작·끝은 제자리. 위아래면 위로 먼저
    const u = ctx.progress;
    const wave = u <= 0 || u >= 1 ? 0 : Math.sin(2 * Math.PI * s.cycles * u);
    const v = s.height * wave;
    const r = v < 0 ? -Math.round(-v) : Math.round(v);
    const shift = Math.min(s.height, Math.max(-s.height, r));
    const horizontal = s.axis === "Horizontal";
    const dx = horizontal ? shift : 0, dy = horizontal ? 0 : -shift;
    if (dx === 0 && dy === 0) return;

    // 그림을 (dx, dy)px 옮긴다(밖으로 나간 부분은 잘리고 빈 자리는 투명). 아직 읽지 않은 칸을 덮지 않는 차례로 제자리에서
    const w = img.width, h = img.height, d = img.data;
    if (Math.abs(dx) >= w || Math.abs(dy) >= h) {
      for (let i = 0; i < d.length; i++) d[i] = 0;
      return;
    }
    for (let k = 0; k < h; k++) {
      const y = dy > 0 ? h - 1 - k : k, sy = y - dy, row = y * w * 4;
      if (sy < 0 || sy >= h) {
        for (let i = row; i < row + w * 4; i++) d[i] = 0;
        continue;
      }
      const from = sy * w * 4;
      if (dx > 0) {
        for (let x = w - 1; x >= 0; x--) {
          const o = row + x * 4, sx = x - dx;
          if (sx >= 0) {
            const p = from + sx * 4;
            d[o] = d[p];
            d[o + 1] = d[p + 1];
            d[o + 2] = d[p + 2];
            d[o + 3] = d[p + 3];
          } else {
            d[o] = 0;
            d[o + 1] = 0;
            d[o + 2] = 0;
            d[o + 3] = 0;
          }
        }
      } else {
        for (let x = 0; x < w; x++) {
          const o = row + x * 4, sx = x - dx;
          if (sx < w) {
            const p = from + sx * 4;
            d[o] = d[p];
            d[o + 1] = d[p + 1];
            d[o + 2] = d[p + 2];
            d[o + 3] = d[p + 3];
          } else {
            d[o] = 0;
            d[o + 1] = 0;
            d[o + 2] = 0;
            d[o + 3] = 0;
          }
        }
      }
    }
  },
});
