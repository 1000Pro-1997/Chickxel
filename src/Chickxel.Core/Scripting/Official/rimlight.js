// 가장자리 빛·그늘(RimLightRender)을 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "가장자리 빛·그늘",
  language: "ko",
  description: "빛이 오는 쪽 가장자리를 밝게, 반대쪽을 어둡게 덧칠해 입체감을 줍니다.",
  translations: {
    ko: {
      name: "가장자리 빛·그늘",
      description: "빛이 오는 쪽 가장자리를 밝게, 반대쪽을 어둡게 덧칠해 입체감을 줍니다.",
      labels: {
        direction: "빛이 오는 쪽", size: "범위", light: "빛 색", shade: "그늘 색",
        "direction.TopLeft": "왼쪽 위", "direction.Top": "위", "direction.TopRight": "오른쪽 위", "direction.Left": "왼쪽",
        "direction.Right": "오른쪽", "direction.BottomLeft": "왼쪽 아래", "direction.Bottom": "아래", "direction.BottomRight": "오른쪽 아래",
      },
    },
    en: {
      name: "Edge light · shade",
      description: "Adds depth by brightening the edge facing the light and darkening the opposite side.",
      labels: {
        direction: "Light direction", size: "Range", light: "Light color", shade: "Shade color",
        "direction.TopLeft": "Top left", "direction.Top": "Up", "direction.TopRight": "Top right", "direction.Left": "Left",
        "direction.Right": "Right", "direction.BottomLeft": "Bottom left", "direction.Bottom": "Down", "direction.BottomRight": "Bottom right",
      },
    },
  },
  animated: false,
  previewChick: "original",
  settings: [
    {
      key: "direction", label: "빛이 오는 쪽", type: "choice", default: "TopLeft",
      choices: [
        { value: "TopLeft", label: "왼쪽 위" }, { value: "Top", label: "위" }, { value: "TopRight", label: "오른쪽 위" },
        { value: "Left", label: "왼쪽" }, { value: "Right", label: "오른쪽" },
        { value: "BottomLeft", label: "왼쪽 아래" }, { value: "Bottom", label: "아래" }, { value: "BottomRight", label: "오른쪽 아래" },
      ],
    },
    { key: "size", label: "범위", type: "int", min: 1, max: 16, default: 1 },
    { key: "light", label: "빛 색", type: "color", default: "#ffffffa0" },
    { key: "shade", label: "그늘 색", type: "color", default: "#00000060" },
  ],
  render(img, ctx, s) {
    const w = img.width, h = img.height, d = img.data, size = s.size;
    // 빛이 오는 쪽 (vx, vy) — 각각 −1, 0, 1(왼쪽 위 원점)
    const dir = s.direction;
    const vx = dir === "TopLeft" || dir === "Left" || dir === "BottomLeft" ? -1
      : dir === "Top" || dir === "Bottom" ? 0 : 1;
    const vy = dir === "TopLeft" || dir === "Top" || dir === "TopRight" ? -1
      : dir === "Left" || dir === "Right" ? 0 : 1;
    // 색 알파 0 = 그쪽은 칠하지 않음(그라데이션이면 픽셀마다 고른 알파가 세기)
    const lit = s.light.a !== 0 || ctx.hasGradient("light");
    const dark = s.shade.a !== 0 || ctx.hasGradient("shade");
    if (!lit && !dark) return;
    // (x, y)에서 (dx, dy) 쪽으로 size칸 안에 모양(알파가 0이 아닌 픽셀 — RGB만 바꾸므로 알파는 끝까지 원래 값) 밖(이미지 밖 포함)이
    // 있는지 — 대각선이면 두 축 중 하나라도
    const rim = (x, y, dx, dy) => {
      for (let t = 1; t <= size; t++) {
        const nx = x + dx * t, ny = y + dy * t;
        if (dx !== 0 && (nx < 0 || nx >= w || d[(y * w + nx) * 4 + 3] === 0)) return true;
        if (dy !== 0 && (ny < 0 || ny >= h || d[(ny * w + x) * 4 + 3] === 0)) return true;
      }
      return false;
    };
    const mix = (v, c, k) => Math.trunc((v * (255 - k) + c * k + 127) / 255);

    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const i = (y * w + x) * 4;
        if (d[i + 3] === 0) continue;
        let r = 0, g = 0, b = 0, k = 0;
        if (lit && rim(x, y, vx, vy)) {
          const c = ctx.color("light", x, y);
          r = c.r;
          g = c.g;
          b = c.b;
          k = c.a;
        } else if (dark && rim(x, y, -vx, -vy)) {
          const c = ctx.color("shade", x, y);
          r = c.r;
          g = c.g;
          b = c.b;
          k = c.a;
        }
        if (k === 0) continue;
        d[i] = mix(d[i], r, k);
        d[i + 1] = mix(d[i + 1], g, k);
        d[i + 2] = mix(d[i + 2], b, k);
      }
    }
  },
});
