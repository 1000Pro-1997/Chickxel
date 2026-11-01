// 녹아내리기(MeltRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "녹아내리기",
  description: "픽셀이 열마다 다른 빠르기로 아래로 흘러내립니다.",
  translations: {
    ko: {
      name: "녹아내리기",
      description: "픽셀이 열마다 다른 빠르기로 아래로 흘러내립니다.",
      labels: { distance: "거리", irregularity: "불규칙함", stretch: "늘어짐 (흘러내린 자국)", fade: "사라짐", seed: "시드" },
    },
    en: {
      name: "Melt",
      description: "Pixels drip down with each column at a different speed.",
      labels: { distance: "Distance", irregularity: "Irregularity", stretch: "Drip (leaves trails)", fade: "Vanish", seed: "Seed" },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "original",
  settings: [
    { key: "distance", label: "거리", type: "int", min: 1, max: 128, default: 12 },
    { key: "irregularity", label: "불규칙함", type: "int", min: 0, max: 100, default: 60 },
    { key: "stretch", label: "늘어짐 (흘러내린 자국)", type: "bool", default: true },
    { key: "fade", label: "사라짐", type: "bool", default: true },
    { key: "seed", label: "시드", type: "int", min: 0, max: 9999, default: 1 },
  ],
  // 아래로 거리만큼 넓힌다
  padding(s) {
    return [0, 0, 0, s.distance];
  },
  render(img, ctx, s) {
    const u = ctx.progress;
    if (u <= 0) return;
    const w = img.width, h = img.height, d = img.data;
    // 보이는 그림 범위
    let bl = w, bt = h, br = 0, bb = 0;
    for (let y = 0; y < h; y++) {
      const row = y * w * 4 + 3;
      let first = 0;
      while (first < w && d[row + first * 4] === 0) first++;
      if (first === w) continue;
      let last = w - 1;
      while (d[row + last * 4] === 0) last--;
      if (first < bl) bl = first;
      if (last + 1 > br) br = last + 1;
      if (y < bt) bt = y;
      bb = y + 1;
    }
    if (br <= bl) return;

    const src = d.slice();
    for (let i = 0; i < d.length; i++) d[i] = 0;
    const irregular = s.irregularity / 100, seed = s.seed, stretch = s.stretch;
    for (let x = bl; x < br; x++) {
      // 열마다(원본 기준 열 — 여백이 달라도 같은 무작위) 늦게 시작·빠르기, 그 열의 진행 q, 내려감 = 거리 × 빠르기 × q²
      const column = x - ctx.source.x;
      const delay = irregular * 0.5 * ctx.hash(column, 0, seed, 21);
      const speed = 1 - irregular * 0.5 * ctx.hash(column, 0, seed, 22);
      const q = Math.min(1, Math.max(0, (u - delay) / (1 - delay)));
      // 사라짐: q가 0.65를 넘으면서 알파가 줄어 q = 1에서 투명
      const fade = s.fade ? Math.min(1, Math.max(0, (1 - q) / (1 - 0.65))) : 1;
      if (fade <= 0) continue;
      const fall = s.distance * speed * q * q;
      let top = -1, bottom = -1;
      for (let y = bt; y < bb; y++)
        if (src[(y * w + x) * 4 + 3] !== 0) {
          if (top < 0) top = y;
          bottom = y;
        }
      if (top < 0) continue;
      const span = bottom - top;
      // 늘어짐이면 열 아래쪽 픽셀일수록 더 내려간다(위 40% → 아래 100%). 반올림은 양수라 Math.round(= AwayFromZero)
      const target = (y) => y + Math.round(fall * (stretch && span > 0 ? 0.4 + 0.6 * (y - top) / span : 1));
      for (let y = top; y <= bottom; y++) {
        const j = (y * w + x) * 4;
        let a = src[j + 3];
        if (a === 0) continue;
        if (fade < 1) {
          a = Math.round(a * fade);
          if (a === 0) continue;
        }
        const ty = target(y);
        // 늘어짐: 바로 아래 픽셀도 그림이면 그 픽셀이 내려간 자리까지 이 색으로 이어 흐른 자국을 남긴다
        const end = stretch && y < bottom && src[j + w * 4 + 3] !== 0 ? target(y + 1) : ty + 1;
        for (let yy = ty; yy < end && yy < h; yy++) {
          const i = (yy * w + x) * 4;
          d[i] = src[j];
          d[i + 1] = src[j + 1];
          d[i + 2] = src[j + 2];
          d[i + 3] = a;
        }
      }
    }
  },
});
