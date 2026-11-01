// 늘었다 줄기(SquashRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "늘었다 줄기",
  description: "젤리처럼 가로·세로로 출렁이다 원래 모양으로 돌아옵니다.",
  translations: {
    ko: {
      name: "늘었다 줄기",
      description: "젤리처럼 가로·세로로 출렁이다 원래 모양으로 돌아옵니다.",
      labels: {
        widthAmount: "가로 세기", heightAmount: "세로 세기", bounces: "튕김",
        anchor: "기준점", "anchor.BottomCenter": "아래 가운데 (발 고정)", "anchor.Middle": "가운데", "anchor.Pivot": "피벗",
        stretchFirst: "위로 먼저 (길쭉 → 납작)",
      },
    },
    en: {
      name: "Squash & stretch",
      description: "Wobbles horizontally and vertically like jelly, then returns to its original shape.",
      labels: {
        widthAmount: "Horizontal strength", heightAmount: "Vertical strength", bounces: "Bounce",
        anchor: "Anchor", "anchor.BottomCenter": "Bottom center (feet fixed)", "anchor.Middle": "Center", "anchor.Pivot": "Pivot",
        stretchFirst: "Upward first (tall → flat)",
      },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "original",
  settings: [
    { key: "widthAmount", label: "가로 세기", type: "int", min: 0, max: 50, default: 20 },
    { key: "heightAmount", label: "세로 세기", type: "int", min: 0, max: 50, default: 20 },
    { key: "bounces", label: "튕김", type: "int", min: 1, max: 3, default: 2 },
    {
      key: "anchor", label: "기준점", type: "choice", default: "BottomCenter",
      choices: [{ value: "BottomCenter", label: "아래 가운데 (발 고정)" }, { value: "Middle", label: "가운데" }, { value: "Pivot", label: "피벗" }],
    },
    { key: "stretchFirst", label: "위로 먼저 (길쭉 → 납작)", type: "bool", default: false },
  ],
  // 지금까지 넓힌 크기 × 축별 세기만큼 넓힌다 — 기준점에서 그림 끝까지는 많아야 지금 크기(기준점이 그림 가운데면 절반)다
  padding(s, width, height) {
    const sx = s.widthAmount / 100, sy = s.heightAmount / 100;
    const half = sx === 0 ? 0 : Math.ceil(width * sx / 2) + 1, full = sx === 0 ? 0 : Math.ceil(width * sx) + 1;
    const halfH = sy === 0 ? 0 : Math.ceil(height * sy / 2) + 1, fullH = sy === 0 ? 0 : Math.ceil(height * sy) + 1;
    if (s.anchor === "BottomCenter") return [half, fullH, half, 0];
    if (s.anchor === "Middle") return [half, halfH, half, halfH];
    return [full, fullH, full, fullH];
  },
  render(img, ctx, s) {
    // 출렁임 f: sin(2π·튕김·u)·e^(−2.5u)를 첫 봉우리(0~1을 1000칸으로 잰 최댓값)가 1이 되게 나눈 값. u = 0, 1에서 0
    const u = ctx.progress, bounces = s.bounces;
    if (u <= 0 || u >= 1) return;
    let peak = -Infinity;
    for (let i = 0; i <= 1000; i++) {
      const v = Math.sin(2 * Math.PI * bounces * i / 1000) * Math.exp(-2.5 * i / 1000);
      if (v > peak) peak = v;
    }
    let f = Math.min(1, Math.max(-1, Math.sin(2 * Math.PI * bounces * u) * Math.exp(-2.5 * u) / peak));
    if (s.stretchFirst) f = -f;
    if (Math.abs(f) < 1e-9) return;

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

    const sx = 1 + s.widthAmount / 100 * f, sy = 1 - s.heightAmount / 100 * f;
    let ax = bl + (br - bl) / 2, ay = bb;
    if (s.anchor === "Middle") ay = bt + (bb - bt) / 2;
    else if (s.anchor === "Pivot") {
      ax = ctx.pivot.x;
      ay = ctx.pivot.y;
    }
    const src = d.slice();
    for (let i = 0; i < d.length; i++) d[i] = 0;
    // 결과가 놓일 수 있는 자리만(기준점에서 그림 끝까지 × 배율, 한 칸 여유). 최근접: 결과 픽셀 가운데를 거꾸로 옮긴 원본 칸
    const x0 = Math.max(0, Math.floor(ax + (bl - ax) * sx) - 1), x1 = Math.min(w, Math.ceil(ax + (br - ax) * sx) + 1);
    const y0 = Math.max(0, Math.floor(ay + (bt - ay) * sy) - 1), y1 = Math.min(h, Math.ceil(ay + (bb - ay) * sy) + 1);
    for (let y = y0; y < y1; y++) {
      const iy = Math.floor(ay + (y + 0.5 - ay) / sy);
      if (iy < bt || iy >= bb) continue;
      for (let x = x0; x < x1; x++) {
        const ix = Math.floor(ax + (x + 0.5 - ax) / sx);
        if (ix < bl || ix >= br) continue;
        const i = (y * w + x) * 4, j = (iy * w + ix) * 4;
        d[i] = src[j];
        d[i + 1] = src[j + 1];
        d[i + 2] = src[j + 2];
        d[i + 3] = src[j + 3];
      }
    }
  },
});
