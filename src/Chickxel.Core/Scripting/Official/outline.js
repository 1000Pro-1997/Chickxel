// 외곽선(OutlineRender)을 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "외곽선",
  description: "그림 둘레에 테두리 선을 두릅니다(바깥 = 그림 밖에, 안쪽 = 가장자리 픽셀에). 색은 지정 색 또는 맞닿은 픽셀보다 진하게·연하게(인접 픽셀 기준 명암).",
  translations: {
    ko: {
      name: "외곽선",
      description: "그림 둘레에 테두리 선을 두릅니다(바깥 = 그림 밖에, 안쪽 = 가장자리 픽셀에). 색은 지정 색 또는 맞닿은 픽셀보다 진하게·연하게(인접 픽셀 기준 명암).",
      labels: {
        color: "색", thickness: "두께", diagonal: "8방향 (모서리 포함)", inside: "안쪽 (가장자리 픽셀)", hideOriginal: "원본 지우기 (외곽선만 보기)",
        colorMode: "색 방식", "colorMode.Fixed": "지정 색", "colorMode.Shade": "인접 픽셀 기준 명암", shadeAmount: "명암 %",
      },
    },
    en: {
      name: "Outline",
      description: "Draws an outline around the image (outside = beyond the image, inside = on edge pixels). The line is a fixed color or the touching pixels' color made darker or lighter (shade from neighbors).",
      labels: {
        color: "Color", thickness: "Thickness", diagonal: "8 directions (incl. corners)", inside: "Inside (edge pixels)", hideOriginal: "Hide original (outline only)",
        colorMode: "Color mode", "colorMode.Fixed": "Fixed color", "colorMode.Shade": "Shade from neighbors", shadeAmount: "Shade %",
      },
    },
  },
  animated: false,
  previewChick: "original",
  // 원본 지우기면 지워진(투명해진) 원본 자리는 클리핑 기준 영역이 아니다
  erasesSource: "hideOriginal",
  settings: [
    { key: "color", label: "색", type: "color", default: "#000000" },
    { key: "thickness", label: "두께", type: "int", min: 1, max: 3, default: 1 },
    { key: "diagonal", label: "8방향 (모서리 포함)", type: "bool", default: false },
    { key: "inside", label: "안쪽 (가장자리 픽셀)", type: "bool", default: false },
    { key: "hideOriginal", label: "원본 지우기 (외곽선만 보기)", type: "bool", default: false },
    {
      key: "colorMode", label: "색 방식", type: "choice", default: "Fixed",
      choices: [{ value: "Fixed", label: "지정 색" }, { value: "Shade", label: "인접 픽셀 기준 명암" }],
    },
    { key: "shadeAmount", label: "명암 %", type: "int", min: -100, max: 100, default: -40 },
  ],
  // 바깥 외곽선은 사방으로 두께만큼(안쪽이면 없음)
  padding(s) {
    return s.inside ? [0, 0, 0, 0] : [s.thickness, s.thickness, s.thickness, s.thickness];
  },
  render(img, ctx, s) {
    const w = img.width, h = img.height, n = w * h, d = img.data;
    const thickness = s.thickness, diagonal = s.diagonal, hide = s.hideOriginal;
    const shade = s.colorMode === "Shade";
    const gradient = !shade && ctx.hasGradient("color"), solid = s.color;
    // 명암: OKLab 밝기를 검정(음수)·흰색(양수) 쪽으로 옮기고 색상은 그대로(C# float 계산과 같게 Math.fround)
    const amount = Math.fround(s.shadeAmount / 100);

    // 모양 단계 k(0 = 그림, k = k칸 넓힌·깎은 모양)를 한 배열에 이어 둔다
    const masks = new Uint8Array(n * (thickness + 1));
    for (let i = 0; i < n; i++) masks[i] = d[i * 4 + 3] !== 0 ? 1 : 0;
    // 바깥 명암: 그림 픽셀 색, 고리마다 새 칸은 바로 안쪽 이웃의 기준 색을 이어받는다(RGBA를 한 정수로)
    const base = new Int32Array(shade && !s.inside ? n : 0);
    if (shade && !s.inside)
      for (let i = 0; i < n; i++) base[i] = (d[i * 4] << 24) | (d[i * 4 + 1] << 16) | (d[i * 4 + 2] << 8) | d[i * 4 + 3];

    for (let k = 1; k <= thickness; k++) {
      const from = (k - 1) * n, into = k * n;
      for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
          const i = y * w + x;
          // 이웃 4/8방향: 넓히기는 하나라도 있으면, 깎기는 모두 있어야(이미지 밖은 없음)
          let any = 0, all = 1;
          for (let dir = 0; dir < (diagonal ? 8 : 4); dir++) {
            const nx = x + (dir === 0 || dir === 4 || dir === 6 ? -1 : dir === 2 || dir === 3 ? 0 : 1);
            const ny = y + (dir < 2 ? 0 : dir === 2 || dir === 4 || dir === 5 ? -1 : 1);
            if (nx >= 0 && nx < w && ny >= 0 && ny < h && masks[from + ny * w + nx] === 1) any = 1;
            else all = 0;
          }
          masks[into + i] = s.inside ? (masks[from + i] === 1 && all === 1 ? 1 : 0) : (masks[from + i] === 1 || any === 1 ? 1 : 0);
        }
      }
      if (!shade || s.inside) continue;
      // 새 고리 칸: 가로·세로 이웃(왼·오른·위·아래 순)이 있으면 그중, 없으면 대각 이웃 중 가장 많은 색(같으면 먼저 본 이웃)
      for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
          const i = y * w + x;
          if (masks[into + i] === 0 || masks[from + i] === 1) continue;
          let c0 = 0, c1 = 0, c2 = 0, c3 = 0, count = 0;
          for (let pass = 0; pass < 2 && count === 0; pass++) {
            for (let dir = pass * 4; dir < pass * 4 + 4; dir++) {
              const nx = x + (dir === 0 || dir === 4 || dir === 6 ? -1 : dir === 2 || dir === 3 ? 0 : 1);
              const ny = y + (dir < 2 ? 0 : dir === 2 || dir === 4 || dir === 5 ? -1 : 1);
              if (nx < 0 || nx >= w || ny < 0 || ny >= h || masks[from + ny * w + nx] === 0) continue;
              const c = base[ny * w + nx];
              if (count === 0) c0 = c;
              else if (count === 1) c1 = c;
              else if (count === 2) c2 = c;
              else c3 = c;
              count++;
            }
          }
          let best = c0, bestVotes = 0;
          for (let a = 0; a < count; a++) {
            const ca = a === 0 ? c0 : a === 1 ? c1 : a === 2 ? c2 : c3;
            let votes = 0;
            if (c0 === ca) votes++;
            if (count > 1 && c1 === ca) votes++;
            if (count > 2 && c2 === ca) votes++;
            if (count > 3 && c3 === ca) votes++;
            if (votes > bestVotes) {
              best = ca;
              bestVotes = votes;
            }
          }
          base[i] = best;
        }
      }
    }

    // 명암 색은 바로 앞에 찾은 색을 기억해 같은 색이 이어지면 다시 계산하지 않는다
    let memoValid = false, memoR = 0, memoG = 0, memoB = 0, memoInR = 0, memoInG = 0, memoInB = 0;
    const last = thickness * n;
    for (let i = 0; i < n; i++) {
      const o = i * 4;
      if (masks[i] === 1) {
        if (!s.inside || masks[last + i] === 1) {
          // 바깥이면 그림 전체, 안쪽이면 고리 안쪽 칸: 원본 지우기면 투명
          if (hide) {
            d[o] = 0;
            d[o + 1] = 0;
            d[o + 2] = 0;
            d[o + 3] = 0;
          }
          continue;
        }
      } else if (s.inside || masks[last + i] === 0) {
        continue;
      }
      // 칠할 칸: 안쪽이면 가장자리 고리(그림 픽셀), 바깥이면 넓힌 고리(그림 밖)
      if (shade) {
        let r = d[o], g = d[o + 1], b = d[o + 2], a = d[o + 3];
        if (!s.inside) {
          const c = base[i];
          r = c >>> 24;
          g = (c >>> 16) & 255;
          b = (c >>> 8) & 255;
          a = c & 255;
        }
        if (amount !== 0) {
          if (!memoValid || r !== memoInR || g !== memoInG || b !== memoInB) {
            const lab = rgbToOklab(r, g, b);
            let l = amount < 0 ? Math.fround(lab.l * Math.fround(1 + amount)) : Math.fround(lab.l + Math.fround(Math.fround(1 - lab.l) * amount));
            l = l < 0 ? 0 : l > 1 ? 1 : l;
            const shaded = oklabToRgb(l, lab.a, lab.b);
            memoR = shaded.r;
            memoG = shaded.g;
            memoB = shaded.b;
            memoInR = r;
            memoInG = g;
            memoInB = b;
            memoValid = true;
          }
          r = memoR;
          g = memoG;
          b = memoB;
        }
        d[o] = r;
        d[o + 1] = g;
        d[o + 2] = b;
        d[o + 3] = a;
        continue;
      }
      const x = i % w, y = (i - x) / w;
      let cr = solid.r, cg = solid.g, cb = solid.b, ca = solid.a;
      if (gradient) {
        const sampled = ctx.color("color", x, y);
        cr = sampled.r;
        cg = sampled.g;
        cb = sampled.b;
        ca = sampled.a;
      }
      if (!s.inside) {
        // 바깥 고리는 색을 그대로 놓는다(알파 포함)
        d[o] = cr;
        d[o + 1] = cg;
        d[o + 2] = cb;
        d[o + 3] = ca;
      } else if (ca > 0) {
        // 안쪽 고리는 그림 색을 색 쪽으로 알파/255만큼(알파는 그대로)
        const keep = 255 - ca;
        d[o] = Math.trunc((d[o] * keep + cr * ca + 127) / 255);
        d[o + 1] = Math.trunc((d[o + 1] * keep + cg * ca + 127) / 255);
        d[o + 2] = Math.trunc((d[o + 2] * keep + cb * ca + 127) / 255);
      }
    }
  },
});
