// 피격 번쩍임(HitFlashRender)을 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "피격 번쩍임",
  language: "ko",
  description: "맞았을 때처럼 그림이 한 색으로 번쩍입니다.",
  translations: {
    ko: {
      name: "피격 번쩍임",
      description: "맞았을 때처럼 그림이 한 색으로 번쩍입니다.",
      labels: { color: "색", strength: "세기", count: "횟수", shape: "모양", "shape.Solid": "꽉 채움 (켜졌다 꺼짐)", "shape.Fade": "점점 엷어짐" },
    },
    en: {
      name: "Hit flash",
      description: "The image flashes a single color, as if hit.",
      labels: { color: "Color", strength: "Strength", count: "Count", shape: "Shape", "shape.Solid": "Solid (on, then off)", "shape.Fade": "Fade out gradually" },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "knight",
  settings: [
    { key: "color", label: "색", type: "color", default: "#ffffff" },
    { key: "strength", label: "세기", type: "int", min: 0, max: 100, default: 100 },
    { key: "count", label: "횟수", type: "int", min: 1, max: 8, default: 2 },
    {
      key: "shape", label: "모양", type: "choice", default: "Solid",
      choices: [{ value: "Solid", label: "꽉 채움 (켜졌다 꺼짐)" }, { value: "Fade", label: "점점 엷어짐" }],
    },
  ],
  render(img, ctx, s) {
    // 한 번 안의 위치 f = frac(진행 × 횟수): 꽉 채움은 앞쪽 절반만, 점점 엷어짐은 1 − f. 끝(진행 1)은 늘 원래 그림
    const u = ctx.progress;
    if (u >= 1 || s.strength === 0) return;
    const p = Math.max(0, u) * s.count, f = p - Math.floor(p);
    const k = s.shape === "Solid" ? (f < 0.5 - 1e-9 ? 1 : 0) : 1 - f;
    // C#의 AwayFromZero 반올림: 0 이상에서는 Math.round와 같다
    const amount = Math.round(k * s.strength / 100 * 255);
    if (amount <= 0) return;

    const w = img.width, h = img.height, d = img.data;
    // 단색은 알파를 쓰지 않는다. 그라데이션이면 픽셀마다 고른 색의 알파가 세기를 줄인다
    const solid = s.color, gradient = ctx.hasGradient("color");
    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const i = (y * w + x) * 4;
        if (d[i + 3] === 0) continue;
        let cr = solid.r, cg = solid.g, cb = solid.b, strength = amount;
        if (gradient) {
          const sampled = ctx.color("color", x, y);
          cr = sampled.r;
          cg = sampled.g;
          cb = sampled.b;
          strength = Math.trunc((amount * sampled.a + 127) / 255);
        }
        if (strength <= 0) continue;
        const keep = 255 - strength;
        d[i] = Math.trunc((d[i] * keep + cr * strength + 127) / 255);
        d[i + 1] = Math.trunc((d[i + 1] * keep + cg * strength + 127) / 255);
        d[i + 2] = Math.trunc((d[i + 2] * keep + cb * strength + 127) / 255);
      }
    }
  },
});
