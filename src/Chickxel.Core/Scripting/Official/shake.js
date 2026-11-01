// 흔들기(ShakeRender의 ShakeEffect 부분)를 JS로 옮긴 공식 스크립트 효과. 같은 설정이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "흔들기",
  language: "ko",
  description: "그림 전체가 픽셀 단위로 덜덜 떱니다.",
  translations: {
    ko: {
      name: "흔들기",
      description: "그림 전체가 픽셀 단위로 덜덜 떱니다.",
      labels: {
        axis: "방향", "axis.Horizontal": "가로", "axis.Vertical": "세로", "axis.Both": "둘 다", amount: "세기", count: "횟수",
        decay: "줄어듦 (끝으로 갈수록 약하게)", regular: "규칙적 (사인)", seed: "시드",
      },
    },
    en: {
      name: "Shake",
      description: "The whole image jitters pixel by pixel.",
      labels: {
        axis: "Direction", "axis.Horizontal": "Horizontal", "axis.Vertical": "Vertical", "axis.Both": "Both", amount: "Strength", count: "Count",
        decay: "Decay (weaker toward the end)", regular: "Regular (sine)", seed: "Seed",
      },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "viking",
  settings: [
    {
      key: "axis", label: "방향", type: "choice", default: "Horizontal",
      choices: [{ value: "Horizontal", label: "가로" }, { value: "Vertical", label: "세로" }, { value: "Both", label: "둘 다" }],
    },
    { key: "amount", label: "세기", type: "int", min: 1, max: 32, default: 2 },
    { key: "count", label: "횟수", type: "int", min: 1, max: 16, default: 4 },
    { key: "decay", label: "줄어듦 (끝으로 갈수록 약하게)", type: "bool", default: true },
    { key: "regular", label: "규칙적 (사인)", type: "bool", default: false },
    { key: "seed", label: "시드", type: "int", min: 0, max: 9999, default: 1 },
  ],
  // 흔드는 축으로 세기만큼 넓힌다
  padding(s) {
    const x = s.axis === "Vertical" ? 0 : s.amount, y = s.axis === "Horizontal" ? 0 : s.amount;
    return [x, y, x, y];
  },
  render(img, ctx, s) {
    // 시작(진행 0)·끝(진행 1)은 제자리
    const u = ctx.progress;
    if (u <= 0 || u >= 1) return;
    const a = s.amount * (s.decay ? 1 - u : 1);
    const both = s.axis === "Both";
    let wx = 0, wy = 0;
    if (s.regular) {
      // 가로 sin(2π·횟수·u), 둘 다면 세로 sin(4π·횟수·u)(8자)
      wx = Math.sin(2 * Math.PI * s.count * u);
      wy = both ? Math.sin(4 * Math.PI * s.count * u) : wx;
    } else {
      // 한 번에 두 번씩 바뀌는 (시드, 칸 번호) 무작위 −1~1(용도 7)
      const k = Math.floor(u * s.count * 2);
      wx = ctx.hash(k, 0, s.seed, 7) * 2 - 1;
      wy = both ? ctx.hash(k, 1, s.seed, 7) * 2 - 1 : wx;
    }
    // C#의 AwayFromZero 반올림 뒤 −세기~세기
    const vx = wx * a, vy = wy * a;
    const rx = Math.min(s.amount, Math.max(-s.amount, vx < 0 ? -Math.round(-vx) : Math.round(vx)));
    const ry = Math.min(s.amount, Math.max(-s.amount, vy < 0 ? -Math.round(-vy) : Math.round(vy)));
    const dx = s.axis === "Vertical" ? 0 : rx, dy = s.axis === "Horizontal" ? 0 : ry;
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
