// 색 덮기(ColorOverlayRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "색 덮기",
  language: "ko",
  description: "보이는 픽셀을 한 색이나 공간 그라데이션으로 물들입니다(100% = 실루엣).",
  translations: {
    ko: {
      name: "색 덮기",
      description: "보이는 픽셀을 한 색이나 공간 그라데이션으로 물들입니다(100% = 실루엣).",
      labels: { color: "색", amount: "양", blend: "섞기", "blend.Normal": "덮기", "blend.Multiply": "곱하기", "blend.Screen": "스크린" },
    },
    en: {
      name: "Color overlay",
      description: "Tints visible pixels with a single color or a spatial gradient (100% = silhouette).",
      labels: { color: "Color", amount: "Amount", blend: "Blend", "blend.Normal": "Overlay", "blend.Multiply": "Multiply", "blend.Screen": "Screen" },
    },
  },
  animated: false,
  previewChick: "original",
  settings: [
    { key: "color", label: "색", type: "color", default: "#ffffff" },
    { key: "amount", label: "양", type: "int", min: 0, max: 100, default: 100 },
    {
      key: "blend", label: "섞기", type: "choice", default: "Normal",
      choices: [{ value: "Normal", label: "덮기" }, { value: "Multiply", label: "곱하기" }, { value: "Screen", label: "스크린" }],
    },
  ],
  render(img, ctx, s) {
    const amount = Math.trunc((s.amount * 255 + 50) / 100);
    if (amount === 0) return;
    const w = img.width, h = img.height, d = img.data;
    const solid = s.color, gradient = ctx.hasGradient("color");
    const multiply = s.blend === "Multiply", screen = s.blend === "Screen";
    // C# 정수 나눗셈(버림). 섞는 값은 모두 0 이상이라 Math.trunc와 같다
    const mix = (v, c, k) => Math.trunc((v * (255 - k) + c * k + 127) / 255);
    const lighten = (v, c, k) => {
      const full = 255 - Math.trunc((255 - v) * (255 - c) / 255);
      return v + Math.trunc(((full - v) * k + 127) / 255);
    };

    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const i = (y * w + x) * 4;
        if (d[i + 3] === 0) continue;
        let cr = solid.r, cg = solid.g, cb = solid.b, ca = solid.a;
        if (gradient) {
          const sampled = ctx.color("color", x, y);
          cr = sampled.r;
          cg = sampled.g;
          cb = sampled.b;
          ca = sampled.a;
        }
        // 색 알파가 세기를 줄인다(단색도 같음)
        const k = Math.trunc((amount * ca + 127) / 255);
        if (k === 0) continue;
        if (screen) {
          d[i] = lighten(d[i], cr, k);
          d[i + 1] = lighten(d[i + 1], cg, k);
          d[i + 2] = lighten(d[i + 2], cb, k);
        } else if (multiply) {
          d[i] = mix(d[i], Math.trunc((d[i] * cr + 127) / 255), k);
          d[i + 1] = mix(d[i + 1], Math.trunc((d[i + 1] * cg + 127) / 255), k);
          d[i + 2] = mix(d[i + 2], Math.trunc((d[i + 2] * cb + 127) / 255), k);
        } else {
          d[i] = mix(d[i], cr, k);
          d[i + 1] = mix(d[i + 1], cg, k);
          d[i + 2] = mix(d[i + 2], cb, k);
        }
      }
    }
  },
});
