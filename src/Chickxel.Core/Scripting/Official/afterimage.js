// 잔상(AfterimageRender)을 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
// 미루는 효과(deferred): 이 레이어의 다른 효과를 모두 그린 뒤에 그리고, 앞 프레임은 다른 효과를 입힌 그림(ctx.renderedFrameSource)을 쓴다.
effect({
  name: "잔상",
  description: "앞 프레임 그림을 뒤에 점점 옅게 남겨 빠르게 움직이는 느낌을 줍니다.",
  translations: {
    ko: {
      name: "잔상",
      description: "앞 프레임 그림을 뒤에 점점 옅게 남겨 빠르게 움직이는 느낌을 줍니다.",
      labels: { count: "개수", interval: "간격 (프레임)", opacity: "불투명도 (가장 가까운 잔상)", tint: "색 입히기", tintColor: "입힐 색" },
    },
    en: {
      name: "Afterimage",
      description: "Leaves fading copies of previous frames behind for a sense of fast motion.",
      labels: { count: "Count", interval: "Spacing (frames)", opacity: "Opacity (nearest afterimage)", tint: "Tint", tintColor: "Tint color" },
    },
  },
  animated: true,
  easing: "Linear",
  deferred: true,
  previewChick: "original",
  motion: false,
  settings: [
    { key: "count", label: "개수", type: "int", min: 1, max: 8, default: 3 },
    { key: "interval", label: "간격 (프레임)", type: "int", min: 1, max: 8, default: 1 },
    { key: "opacity", label: "불투명도 (가장 가까운 잔상)", type: "int", min: 1, max: 100, default: 60 },
    { key: "tint", label: "색 입히기", type: "bool", default: false },
    { key: "tintColor", label: "입힐 색", type: "color", default: "#5096ff" },
  ],
  render(img, ctx, s) {
    const w = img.width, h = img.height, d = img.data, src = ctx.source;
    const layer = img.blank(w, h);
    let drawn = false;
    // 먼 잔상부터 깔고 가까운 잔상을 위에(범위 안 프레임만)
    for (let k = s.count; k >= 1; k--) {
      const back = k * s.interval;
      if (back > ctx.step) continue;
      // k번째 잔상의 불투명도: 불투명도 × (개수 − k + 1) / 개수
      const opacity = Math.min(255, Math.trunc((Math.trunc(s.opacity * 255 * (s.count - k + 1) / s.count) + 50) / 100));
      if (opacity === 0) continue;
      const rendered = ctx.renderedFrameSource(ctx.frame - back);
      if (!rendered) continue;
      drawn = true;
      // 받은 그림(새 사본)에 색·불투명도를 입혀 잔상 층에 겹친다. 층 밖 칸은 blendOver가 잘라 낸다
      const ghost = rendered.image, gw = ghost.width, gh = ghost.height, gd = ghost.data;
      const ox = src.x + rendered.offsetX, oy = src.y + rendered.offsetY;
      for (let gy = 0; gy < gh; gy++) {
        const y = gy + oy;
        for (let gx = 0; gx < gw; gx++) {
          const i = (gy * gw + gx) * 4;
          if (gd[i + 3] === 0) continue;
          const x = gx + ox;
          if (y < 0 || y >= h || x < 0 || x >= w) continue;
          if (s.tint) {
            // 색 알파 = 물들이는 세기(그라데이션은 원본 범위 기준)
            const t = ctx.color("tintColor", x, y, src);
            if (t.a > 0) {
              gd[i] = Math.trunc((gd[i] * (255 - t.a) + t.r * t.a + 127) / 255);
              gd[i + 1] = Math.trunc((gd[i + 1] * (255 - t.a) + t.g * t.a + 127) / 255);
              gd[i + 2] = Math.trunc((gd[i + 2] * (255 - t.a) + t.b * t.a + 127) / 255);
            }
          }
          gd[i + 3] = Math.trunc((gd[i + 3] * opacity + 127) / 255);
        }
      }
      layer.blendOver(ghost, ox, oy);
    }
    if (!drawn) return;
    // 지금 그림을 잔상 위에: 잔상이 있는 칸만 바꾼다
    const ld = layer.data, under = new Uint8Array(w * h);
    for (let p = 0; p < w * h; p++) under[p] = ld[p * 4 + 3];
    layer.blendOver(img, 0, 0);
    for (let p = 0; p < w * h; p++) {
      if (under[p] === 0) continue;
      const i = p * 4;
      d[i] = ld[i];
      d[i + 1] = ld[i + 1];
      d[i + 2] = ld[i + 2];
      d[i + 3] = ld[i + 3];
    }
  },
});
