// 그림자(ShadowRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "그림자",
  language: "ko",
  description: "그림 모양 그대로의 그림자를 비스듬히 옮겨 뒤에 깝니다.",
  translations: {
    ko: {
      name: "그림자",
      description: "그림 모양 그대로의 그림자를 비스듬히 옮겨 뒤에 깝니다.",
      labels: {
        color: "색", offsetX: "X", offsetY: "Y",
        dither: "디더", "dither.None": "없음 (반투명)", "dither.Light25": "25%", "dither.Half50": "50% (바둑판)", "dither.Dense75": "75%",
      },
    },
    en: {
      name: "Shadow",
      description: "Casts a copy of the image shape diagonally behind it as a shadow.",
      labels: {
        color: "Color", offsetX: "X", offsetY: "Y",
        dither: "Dither", "dither.None": "None (semi-transparent)", "dither.Light25": "25%", "dither.Half50": "50% (checkerboard)", "dither.Dense75": "75%",
      },
    },
  },
  animated: false,
  previewChick: "original",
  settings: [
    { key: "color", label: "색", type: "color", default: "#00000080" },
    { key: "offsetX", label: "X", type: "int", min: -16, max: 16, default: 1, widget: "point", pair: "offsetY" },
    { key: "offsetY", label: "Y", type: "int", min: -16, max: 16, default: 1 },
    {
      key: "dither", label: "디더", type: "choice", default: "None",
      choices: [
        { value: "None", label: "없음 (반투명)" }, { value: "Light25", label: "25%" },
        { value: "Half50", label: "50% (바둑판)" }, { value: "Dense75", label: "75%" },
      ],
    },
  ],
  // 옮긴 쪽으로 옮긴 만큼 넓힌다
  padding(s) {
    return [Math.max(0, -s.offsetX), Math.max(0, -s.offsetY), Math.max(0, s.offsetX), Math.max(0, s.offsetY)];
  },
  render(img, ctx, s) {
    // MathF.Round(짝수 쪽 반올림) 뒤 0~255로
    const roundByte = (v) => {
      const f = Math.floor(v), frac = v - f;
      return Math.min(255, Math.max(0, frac > 0.5 || frac === 0.5 && f % 2 === 1 ? f + 1 : f));
    };
    // (그림 채널 × 그림 알파 + 그림자 채널 × 그림자 알파 × (1 − 그림 알파)) / 합친 알파, 모두 단정도
    const mix = (over, under, fa, fb, rest, oa) =>
      roundByte(Math.fround(Math.fround(Math.fround(over * fa) + Math.fround(Math.fround(under * fb) * rest)) / oa));
    const w = img.width, h = img.height, d = img.data;
    const dx = s.offsetX, dy = s.offsetY;
    // 디더 밀도: 4×4 Bayer 문턱이 이보다 작은 칸에만 깐다(0 = 디더 없음, 색 알파로 반투명)
    const density = s.dither === "Light25" ? 4 : s.dither === "Half50" ? 8 : s.dither === "Dense75" ? 12 : 0;
    const gradient = ctx.hasGradient("color");
    const solid = s.color;
    const left = ctx.source.x, top = ctx.source.y;
    // 그림자가 놓일 수 있는 줄·칸만(옮긴 원본이 이미지 안인 곳)
    const x0 = Math.max(0, dx), x1 = Math.min(w, w + dx), y0 = Math.max(0, dy), y1 = Math.min(h, h + dy);
    // 제자리에서 그린다: 옮긴 쪽 끝부터 돌아서 읽을 원본 칸((x − dx, y − dy))은 아직 고치지 않은 칸이다
    for (let ky = 0; ky < y1 - y0; ky++) {
      const y = dy > 0 ? y1 - 1 - ky : y0 + ky;
      for (let kx = 0; kx < x1 - x0; kx++) {
        const x = dy === 0 && dx > 0 ? x1 - 1 - kx : x0 + kx;
        const a = d[((y - dy) * w + x - dx) * 4 + 3];
        if (a === 0) continue;
        let r = 0, g = 0, b = 0, sa = 0;
        if (density === 0) {
          let ca = solid.a;
          r = solid.r;
          g = solid.g;
          b = solid.b;
          if (gradient) {
            const sampled = ctx.color("color", x, y);
            r = sampled.r;
            g = sampled.g;
            b = sampled.b;
            ca = sampled.a;
          }
          sa = Math.trunc((ca * a + 127) / 255);
        } else if (ctx.bayer4(x - left, y - top) < density) {
          // 디더: 단색은 불투명, 그라데이션은 고른 색 그대로
          r = solid.r;
          g = solid.g;
          b = solid.b;
          sa = 255;
          if (gradient) {
            const sampled = ctx.color("color", x, y);
            r = sampled.r;
            g = sampled.g;
            b = sampled.b;
            sa = sampled.a;
          }
        } else continue;

        // 그림이 그림자 위에: Color32.BlendOver(그림자, 그림) — float(단정도) 계산과 MathF.Round(짝수 쪽 반올림)까지 같게
        const i = (y * w + x) * 4;
        const ua = d[i + 3];
        if (ua === 255 || sa === 0) continue;
        if (ua === 0) {
          d[i] = r;
          d[i + 1] = g;
          d[i + 2] = b;
          d[i + 3] = sa;
          continue;
        }
        const fa = Math.fround(ua / 255), fb = Math.fround(sa / 255), rest = Math.fround(1 - fa);
        const oa = Math.fround(fa + Math.fround(fb * rest));
        d[i] = mix(d[i], r, fa, fb, rest, oa);
        d[i + 1] = mix(d[i + 1], g, fa, fb, rest, oa);
        d[i + 2] = mix(d[i + 2], b, fa, fb, rest, oa);
        d[i + 3] = roundByte(Math.fround(oa * 255));
      }
    }
  },
});
