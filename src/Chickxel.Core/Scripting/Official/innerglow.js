// 안쪽 빛(InnerGlowRender)을 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "안쪽 빛",
  description: "가장자리에서 안쪽으로 색을 번지듯 섞어 빛나는 테두리를 만듭니다.",
  translations: {
    ko: {
      name: "안쪽 빛",
      description: "가장자리에서 안쪽으로 색을 번지듯 섞어 빛나는 테두리를 만듭니다.",
      labels: {
        color: "색", size: "두께", strength: "세기", fade: "안쪽으로 갈수록 약하게 (계단식)", blend: "섞기",
        "blend.Normal": "덮기", "blend.Multiply": "곱하기", "blend.Screen": "스크린",
      },
    },
    en: {
      name: "Inner glow",
      description: "Makes a glowing edge by blending color inward from the edges.",
      labels: {
        color: "Color", size: "Thickness", strength: "Strength", fade: "Weaker toward the inside (stepped)", blend: "Blend",
        "blend.Normal": "Overlay", "blend.Multiply": "Multiply", "blend.Screen": "Screen",
      },
    },
  },
  animated: false,
  previewChick: "original",
  settings: [
    { key: "color", label: "색", type: "color", default: "#fff0a0" },
    { key: "size", label: "두께", type: "int", min: 1, max: 8, default: 2 },
    { key: "strength", label: "세기", type: "int", min: 0, max: 100, default: 70 },
    { key: "fade", label: "안쪽으로 갈수록 약하게 (계단식)", type: "bool", default: true },
    {
      key: "blend", label: "섞기", type: "choice", default: "Screen",
      choices: [{ value: "Normal", label: "덮기" }, { value: "Multiply", label: "곱하기" }, { value: "Screen", label: "스크린" }],
    },
  ],
  render(img, ctx, s) {
    const amount = Math.trunc((s.strength * 255 + 50) / 100);
    if (amount === 0) return;
    const w = img.width, h = img.height, d = img.data, n = s.size;
    const solid = s.color, gradient = ctx.hasGradient("color");
    const multiply = s.blend === "Multiply", screen = s.blend === "Screen";
    // C# 정수 나눗셈(버림). 섞는 값은 모두 0 이상이라 Math.trunc와 같다
    const mix = (v, c, k) => Math.trunc((v * (255 - k) + c * k + 127) / 255);
    const lighten = (v, c, k) => {
      const full = 255 - Math.trunc((255 - v) * (255 - c) / 255);
      return v + Math.trunc(((full - v) * k + 127) / 255);
    };

    for (let y = 0; y < h; y++) {
      // 왼쪽 이웃의 거리(0 = 비었음, n + 1 = n보다 멂). 거리는 이웃끼리 1까지만 다르므로 이웃 거리 − 1 고리부터 보면 된다
      let prev = 0;
      for (let x = 0; x < w; x++) {
        const i = (y * w + x) * 4;
        if (d[i + 3] === 0) {
          prev = 0;
          continue;
        }
        // 8방향으로 한 칸씩 깎을 때 k번째에 빠지는 픽셀 = 가장 가까운 빈 칸(이미지 밖 포함)까지 체비쇼프 거리가 k.
        // 고리 r이 이미지 밖에 닿으면 k = r, 아니면 고리의 알파를 본다(RGB만 바꾸므로 알파는 끝까지 원래 값)
        let k = 0;
        for (let r = Math.max(1, prev - 1); r <= n && k === 0; r++) {
          if (x < r || y < r || x + r >= w || y + r >= h) {
            k = r;
            break;
          }
          const top = i - (r * w + r) * 4 + 3, bottom = i + (r * w - r) * 4 + 3, left = top + r * w * 4, right = left + r * 8;
          for (let t = 0; t <= r * 2; t++) {
            if (d[top + t * 4] === 0 || d[bottom + t * 4] === 0 || d[left + (t - r) * w * 4] === 0 || d[right + (t - r) * w * 4] === 0) {
              k = r;
              break;
            }
          }
        }
        prev = k === 0 ? n + 1 : k;
        if (k === 0) continue;
        const a = s.fade ? Math.trunc(amount * (n - k + 1) / n) : amount;
        let cr = solid.r, cg = solid.g, cb = solid.b, strength = a;
        if (gradient) {
          // 그라데이션 알파는 세기에 곱한다(단색 알파는 쓰지 않음)
          const sampled = ctx.color("color", x, y);
          cr = sampled.r;
          cg = sampled.g;
          cb = sampled.b;
          strength = Math.trunc((a * sampled.a + 127) / 255);
        }
        if (strength === 0) continue;
        if (screen) {
          d[i] = lighten(d[i], cr, strength);
          d[i + 1] = lighten(d[i + 1], cg, strength);
          d[i + 2] = lighten(d[i + 2], cb, strength);
        } else if (multiply) {
          d[i] = mix(d[i], Math.trunc((d[i] * cr + 127) / 255), strength);
          d[i + 1] = mix(d[i + 1], Math.trunc((d[i + 1] * cg + 127) / 255), strength);
          d[i + 2] = mix(d[i + 2], Math.trunc((d[i + 2] * cb + 127) / 255), strength);
        } else {
          d[i] = mix(d[i], cr, strength);
          d[i + 1] = mix(d[i + 1], cg, strength);
          d[i + 2] = mix(d[i + 2], cb, strength);
        }
      }
    }
  },
});
