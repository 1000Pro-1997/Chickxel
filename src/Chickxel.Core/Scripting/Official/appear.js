// 나타나기·사라지기(AppearRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "나타나기 / 사라지기",
  description: "디더·흩어짐·쓸기·투명도로 그림이 나타나거나 사라집니다.",
  translations: {
    ko: {
      name: "나타나기 / 사라지기",
      description: "디더·흩어짐·쓸기·투명도로 그림이 나타나거나 사라집니다.",
      labels: {
        disappear: "사라지기",
        style: "방식", "style.Fade": "투명도", "style.Dither": "디더 (4×4 Bayer)", "style.Wipe": "쓸기", "style.Scatter": "픽셀 흩어짐",
        from: "쓸기 시작", "from.Top": "위", "from.Bottom": "아래", "from.Left": "왼쪽", "from.Right": "오른쪽",
        edge: "가장자리", seed: "시드",
      },
    },
    en: {
      name: "Appear / disappear",
      description: "The image appears or disappears via dither, scatter, wipe or opacity.",
      labels: {
        disappear: "Disappear",
        style: "Style", "style.Fade": "Opacity", "style.Dither": "Dither (4×4 Bayer)", "style.Wipe": "Wipe", "style.Scatter": "Pixel scatter",
        from: "Wipe from", "from.Top": "Top", "from.Bottom": "Bottom", "from.Left": "Left", "from.Right": "Right",
        edge: "Edge", seed: "Seed",
      },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "original",
  settings: [
    { key: "disappear", label: "사라지기", type: "bool", default: false },
    {
      key: "style", label: "방식", type: "choice", default: "Dither",
      choices: [
        { value: "Fade", label: "투명도" }, { value: "Dither", label: "디더 (4×4 Bayer)" },
        { value: "Wipe", label: "쓸기" }, { value: "Scatter", label: "픽셀 흩어짐" },
      ],
    },
    {
      key: "from", label: "쓸기 시작", type: "choice", default: "Top",
      choices: [{ value: "Top", label: "위" }, { value: "Bottom", label: "아래" }, { value: "Left", label: "왼쪽" }, { value: "Right", label: "오른쪽" }],
    },
    { key: "edge", label: "가장자리", type: "int", min: 0, max: 2, default: 1 },
    { key: "seed", label: "시드", type: "int", min: 0, max: 9999, default: 1 },
  ],
  render(img, ctx, s) {
    // 보임: 나타나기는 진행 그대로, 사라지기는 1 − 진행
    const v = Math.min(1, Math.max(0, s.disappear ? 1 - ctx.progress : ctx.progress));
    if (v >= 1) return;
    const w = img.width, h = img.height, d = img.data;
    if (v <= 0) {
      for (let i = 0; i < d.length; i++) d[i] = 0;
      return;
    }
    // 무늬·무작위는 원본 왼쪽 위 기준 자리(여백과 관계없음)
    const left = ctx.source.x, top = ctx.source.y;

    if (s.style === "Fade") {
      for (let i = 0; i < d.length; i += 4) {
        const a = d[i + 3];
        if (a === 0) continue;
        // C#의 AwayFromZero 반올림(양수라 Math.round와 같음). 0이 되면 완전 투명
        const alpha = Math.round(a * v);
        if (alpha === 0) {
          d[i] = 0;
          d[i + 1] = 0;
          d[i + 2] = 0;
        }
        d[i + 3] = alpha;
      }
      return;
    }

    if (s.style === "Dither") {
      // 4×4 Bayer 문턱 (b + ½)/16이 보임 이상인 칸은 지운다(투명한 칸도)
      for (let y = 0; y < h; y++)
        for (let x = 0; x < w; x++)
          if ((ctx.bayer4(x - left, y - top) + 0.5) / 16 >= v) {
            const i = (y * w + x) * 4;
            d[i] = 0;
            d[i + 1] = 0;
            d[i + 2] = 0;
            d[i + 3] = 0;
          }
      return;
    }

    if (s.style === "Scatter") {
      const seed = s.seed;
      for (let y = 0; y < h; y++)
        for (let x = 0; x < w; x++) {
          const i = (y * w + x) * 4;
          if (d[i + 3] !== 0 && ctx.hash(x - left, y - top, seed, 31) >= v) {
            d[i] = 0;
            d[i + 1] = 0;
            d[i + 2] = 0;
            d[i + 3] = 0;
          }
        }
      return;
    }

    // 쓸기: 보이는 그림 범위
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
    // 시작 쪽부터 잰 자리 p, 선 c = v × (길이 + 가장자리). p + ½ < c − 가장자리면 보임, 가장자리 안이면 2×2 디더, 그 밖은 지움
    const vertical = s.from === "Top" || s.from === "Bottom";
    const length = vertical ? bb - bt : br - bl, edge = s.edge;
    const c = v * (length + edge);
    for (let y = bt; y < bb; y++)
      for (let x = bl; x < br; x++) {
        const i = (y * w + x) * 4;
        if (d[i + 3] === 0) continue;
        let p = 0;
        if (s.from === "Top") p = y - bt;
        else if (s.from === "Bottom") p = bb - 1 - y;
        else if (s.from === "Left") p = x - bl;
        else p = br - 1 - x;
        if (s.disappear) p = length - 1 - p;
        const inside = c - (p + 0.5); // 선에서 안쪽으로 얼마나(px)
        if (inside > edge) continue;
        if (inside > 0) {
          // 가장자리 1px = 50%, 2px = 선 쪽 25%·안쪽 75%
          const level = edge === 1 ? 0.5 : inside > 1 ? 0.75 : 0.25;
          if (ctx.bayer2(x - left, y - top) + 0.5 < level * 4) continue;
        }
        d[i] = 0;
        d[i + 1] = 0;
        d[i + 2] = 0;
        d[i + 3] = 0;
      }
  },
});
