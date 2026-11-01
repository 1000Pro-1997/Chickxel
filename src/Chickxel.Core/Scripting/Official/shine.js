// 빛 지나가기(ShineRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "빛 지나가기",
  language: "ko",
  description: "빛 띠가 그림을 한쪽에서 반대쪽으로 훑고 지나갑니다(반짝임).",
  translations: {
    ko: {
      name: "빛 지나가기",
      description: "빛 띠가 그림을 한쪽에서 반대쪽으로 훑고 지나갑니다(반짝임).",
      labels: { angle: "각도", width: "폭", strength: "세기", color: "색", softness: "부드러움" },
    },
    en: {
      name: "Light sweep",
      description: "A band of light sweeps across the image from one side to the other (shine).",
      labels: { angle: "Angle", width: "Width", strength: "Strength", color: "Color", softness: "Softness" },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "original",
  settings: [
    { key: "angle", label: "각도", type: "int", min: 0, max: 359, default: 45, widget: "angle" },
    { key: "width", label: "폭", type: "int", min: 1, max: 32, default: 3 },
    { key: "strength", label: "세기", type: "int", min: 0, max: 100, default: 80 },
    { key: "color", label: "색", type: "color", default: "#ffffff" },
    { key: "softness", label: "부드러움", type: "int", min: 0, max: 2, default: 0 },
  ],
  render(img, ctx, s) {
    const amount = Math.trunc((s.strength * 255 + 50) / 100);
    if (amount === 0) return;
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

    // 단색은 알파를 쓰지 않는다. 그라데이션이면 픽셀마다 고른 색의 알파가 세기를 줄인다
    const solid = s.color, gradient = ctx.hasGradient("color");
    const angle = s.angle * Math.PI / 180, dx = Math.cos(angle), dy = -Math.sin(angle); // y는 아래로
    const along = (x, y) => x * dx + y * dy;
    const l = left + 0.5, r = right - 0.5, t = top + 0.5, b = bottom - 0.5;
    const s0 = Math.min(Math.min(along(l, t), along(r, t)), Math.min(along(l, b), along(r, b)));
    const s1 = Math.max(Math.max(along(l, t), along(r, t)), Math.max(along(l, b), along(r, b)));
    const half = s.width / 2, reach = half + s.softness;
    // 띠 가운데: 진행 0 = 첫 픽셀보다 (반폭 + 부드러움 + 1) 앞, 1 = 마지막 픽셀보다 그만큼 뒤
    const from = s0 - reach - 1, to = s1 + reach + 1, c = from + (to - from) * ctx.progress;
    const steps = s.softness + 1;
    // 스크린(밝게 더하기)을 k/255만큼. 정수 나눗셈은 C#처럼 버림
    const screen = (v, light, k) => {
      const full = 255 - Math.trunc((255 - v) * (255 - light) / 255);
      return v + Math.trunc(((full - v) * k + 127) / 255);
    };

    for (let y = top; y < bottom; y++) {
      for (let x = left; x < right; x++) {
        const i = (y * w + x) * 4;
        if (d[i + 3] === 0) continue;
        const dist = Math.abs(along(x + 0.5, y + 0.5) - c);
        if (dist > reach) continue;
        let k = dist <= half ? amount : Math.trunc(amount * (steps - Math.ceil(dist - half)) / steps);
        if (k > 0) {
          let cr = solid.r, cg = solid.g, cb = solid.b;
          if (gradient) {
            const sampled = ctx.color("color", x, y);
            cr = sampled.r;
            cg = sampled.g;
            cb = sampled.b;
            k = Math.trunc((k * sampled.a + 127) / 255);
          }
          d[i] = screen(d[i], cr, k);
          d[i + 1] = screen(d[i + 1], cg, k);
          d[i + 2] = screen(d[i + 2], cb, k);
        }
      }
    }
  },
});
