// 반사(ReflectionRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "반사",
  language: "ko",
  description: "그림을 뒤집어 아래·위·옆에 점점 옅게 비춥니다(물·바닥·벽 반사).",
  translations: {
    ko: {
      name: "반사",
      description: "그림을 뒤집어 아래·위·옆에 점점 옅게 비춥니다(물·바닥·벽 반사).",
      labels: {
        direction: "방향", "direction.Down": "아래 (바닥·물)", "direction.Up": "위 (천장)", "direction.Left": "왼쪽 (벽)", "direction.Right": "오른쪽 (벽)",
        gap: "간격", height: "높이(옆이면 너비) %", opacityStart: "시작 불투명도", opacityEnd: "끝 불투명도",
        ditherFade: "디더로 흐리기 (반투명 대신 무늬)", tint: "색 입히기", wave: "물결",
      },
    },
    en: {
      name: "Reflection",
      description: "Flips the image and reflects it below, above or to a side, fading gradually (water/floor/wall reflection).",
      labels: {
        direction: "Direction", "direction.Down": "Down (floor/water)", "direction.Up": "Up (ceiling)", "direction.Left": "Left (wall)", "direction.Right": "Right (wall)",
        gap: "Gap", height: "Height (width for sides) %", opacityStart: "Start opacity", opacityEnd: "End opacity",
        ditherFade: "Fade with dithering (pattern instead of semi-transparency)", tint: "Tint", wave: "Ripple",
      },
    },
  },
  animated: false,
  previewChick: "original",
  // 클리핑되면 기준 효과의 픽셀만 뒤집고, 비친 픽셀은 기준 영역 밖에도 남는다
  clipsBySource: true,
  settings: [
    {
      key: "direction", label: "방향", type: "choice", default: "Down",
      choices: [
        { value: "Down", label: "아래 (바닥·물)" }, { value: "Up", label: "위 (천장)" },
        { value: "Left", label: "왼쪽 (벽)" }, { value: "Right", label: "오른쪽 (벽)" },
      ],
    },
    { key: "gap", label: "간격", type: "int", min: 0, max: 32, default: 0 },
    { key: "height", label: "높이(옆이면 너비) %", type: "int", min: 1, max: 100, default: 50 },
    { key: "opacityStart", label: "시작 불투명도", type: "int", min: 0, max: 100, default: 50 },
    { key: "opacityEnd", label: "끝 불투명도", type: "int", min: 0, max: 100, default: 0 },
    { key: "ditherFade", label: "디더로 흐리기 (반투명 대신 무늬)", type: "bool", default: false },
    { key: "tint", label: "색 입히기", type: "color", default: "#4682E600" },
    { key: "wave", label: "물결", type: "int", min: 0, max: 3, default: 0 },
  ],
  // 비치는 쪽으로 간격 + 지금까지 넓힌 높이(옆이면 너비)의 길이 %, 물결 폭만큼 양옆(옆이면 위아래)
  padding(s, width, height) {
    const horizontal = s.direction === "Left" || s.direction === "Right";
    const length = horizontal ? width : height;
    const reach = s.gap + (length <= 0 ? 0 : Math.max(1, Math.trunc((length * s.height + 99) / 100)));
    if (s.direction === "Up") return [s.wave, reach, s.wave, 0];
    if (s.direction === "Left") return [reach, s.wave, 0, s.wave];
    if (s.direction === "Right") return [0, s.wave, reach, s.wave];
    return [s.wave, 0, s.wave, reach];
  },
  render(img, ctx, s) {
    const w = img.width, h = img.height, d = img.data;

    // 보이는 픽셀을 감싸는 범위(오른쪽·아래는 끝 다음 칸)
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

    const up = s.direction === "Up", toLeft = s.direction === "Left", toRight = s.direction === "Right";
    const horizontal = toLeft || toRight;
    const length = horizontal ? right - left : bottom - top;
    const rows = Math.max(1, Math.trunc((length * s.height + 99) / 100));
    const gap = s.gap, wave = s.wave, dither = s.ditherFade;
    // 색 입히기 그라데이션 범위: 보이는 범위를 비치는 쪽으로 간격 + 비칠 줄 수만큼 늘린 사각형
    let bx = left, by = top, bw = right - left, bh = bottom - top;
    if (up) { by = top - gap - rows; bh = bottom - by; }
    else if (toLeft) { bx = left - gap - rows; bw = right - bx; }
    else if (toRight) bw += gap + rows;
    else bh += gap + rows;
    const gradient = ctx.hasGradient("tint"), solid = s.tint;
    const sx = ctx.source.x, sy = ctx.source.y;
    const alongStart = horizontal ? top : left, alongEnd = horizontal ? bottom : right;
    const limit = horizontal ? w : h;
    // C# MidpointRounding.AwayFromZero
    const roundAway = (v) => {
      const whole = Math.trunc(v);
      return Math.abs(v - whole) >= 0.5 ? whole + Math.sign(v) : whole;
    };

    // 줄(옆이면 열) j: 그림 끝에서 j번째 줄을 간격 너머 j번째 줄에 비춘다. 비친 줄은 모두 보이는 범위 밖이고 칸마다 한 번만 쓰므로
    // 쓰는 자리는 늘 투명하다(C#의 BlendOver(비친 색, 투명) = 비친 색)
    for (let j = 0; j < rows; j++) {
      let from = bottom - 1 - j, to = bottom + gap + j;
      if (up) { from = top + j; to = top - 1 - gap - j; }
      else if (toLeft) { from = left + j; to = left - 1 - gap - j; }
      else if (toRight) { from = right - 1 - j; to = right + gap + j; }
      if (to < 0 || to >= limit) continue;
      const opacity = (rows === 1 ? s.opacityStart : s.opacityStart + (s.opacityEnd - s.opacityStart) * j / (rows - 1)) / 100;
      if (opacity <= 0) continue;
      const shift = wave === 0 ? 0 : roundAway(wave * Math.sin(2 * Math.PI * j / 6));
      for (let k = alongStart; k < alongEnd; k++) {
        const i = horizontal ? (k * w + from) * 4 : (from * w + k) * 4;
        let a = d[i + 3];
        if (a === 0) continue;
        const tx = horizontal ? to : k + shift, ty = horizontal ? k + shift : to;
        if (tx < 0 || tx >= w || ty < 0 || ty >= h) continue;
        if (dither) {
          if (!(opacity * 16 > ctx.bayer4(tx - sx, ty - sy) + 0.5 - 1e-9)) continue;
        } else {
          a = roundAway(a * opacity);
          if (a === 0) continue;
        }
        let r = d[i], g = d[i + 1], b = d[i + 2];
        let tr = solid.r, tg = solid.g, tb = solid.b, ta = solid.a;
        if (gradient) {
          const sampled = ctx.color("tint", tx, ty, { x: bx, y: by, w: bw, h: bh });
          tr = sampled.r;
          tg = sampled.g;
          tb = sampled.b;
          ta = sampled.a;
        }
        if (ta !== 0) {
          const keep = 255 - ta;
          r = Math.trunc((r * keep + tr * ta + 127) / 255);
          g = Math.trunc((g * keep + tg * ta + 127) / 255);
          b = Math.trunc((b * keep + tb * ta + 127) / 255);
        }
        const o = (ty * w + tx) * 4;
        d[o] = r;
        d[o + 1] = g;
        d[o + 2] = b;
        d[o + 3] = a;
      }
    }
  },
});
