// 충격파 고리(ShockwaveRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정·그라데이션이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "충격파 고리",
  description: "중심에서 픽셀 원 고리가 퍼져 나갑니다.",
  translations: {
    ko: {
      name: "충격파 고리",
      description: "중심에서 픽셀 원 고리가 퍼져 나갑니다.",
      labels: {
        center: "중심", "center.Middle": "가운데", "center.Pivot": "피벗", "center.BottomCenter": "아래 가운데", "center.Custom": "직접 (X, Y)",
        centerX: "직접 중심 (원본 왼쪽 위 기준)", centerY: "직접 중심 Y",
        radius: "최대 반지름", thickness: "두께", color: "색", fade: "사라짐 (퍼지며 옅어짐)", behind: "그림 뒤 (아래에)",
      },
    },
    en: {
      name: "Shockwave ring",
      description: "A pixel circle ring spreads out from the center.",
      labels: {
        center: "Center", "center.Middle": "Center", "center.Pivot": "Pivot", "center.BottomCenter": "Bottom center", "center.Custom": "Custom (X, Y)",
        centerX: "Custom center (from the source's top-left)", centerY: "Custom center Y",
        radius: "Max radius", thickness: "Thickness", color: "Color", fade: "Fade out (fades as it spreads)", behind: "Behind image (below)",
      },
    },
  },
  animated: true,
  easing: "EaseOut",
  previewChick: "original",
  // 사방으로 최대 반지름만큼(피벗·직접 중심은 +1 — 중심이 그림 끝에 걸쳐도)
  padding: (s) => {
    const r = s.radius + (s.center === "Pivot" || s.center === "Custom" ? 1 : 0);
    return [r, r, r, r];
  },
  settings: [
    {
      key: "center", label: "중심", type: "choice", default: "Middle",
      choices: [
        { value: "Middle", label: "가운데" }, { value: "Pivot", label: "피벗" },
        { value: "BottomCenter", label: "아래 가운데" }, { value: "Custom", label: "직접 (X, Y)" },
      ],
    },
    { key: "centerX", label: "직접 중심 (원본 왼쪽 위 기준)", type: "int", min: -16384, max: 16384, default: 0, widget: "point", pair: "centerY" },
    { key: "centerY", label: "직접 중심 Y", type: "int", min: -16384, max: 16384, default: 0 },
    { key: "radius", label: "최대 반지름", type: "int", min: 1, max: 128, default: 16 },
    { key: "thickness", label: "두께", type: "int", min: 1, max: 8, default: 1 },
    { key: "color", label: "색", type: "color", default: "#ffffff" },
    { key: "fade", label: "사라짐 (퍼지며 옅어짐)", type: "bool", default: true },
    { key: "behind", label: "그림 뒤 (아래에)", type: "bool", default: false },
  ],
  render(img, ctx, s) {
    // 반지름은 빠르기 곡선대로, 옅어짐은 반복만 넣은 시간대로
    const r = Math.round(s.radius * Math.min(Math.max(ctx.progress, 0), 1));
    if (r < 1) return;
    const fade = s.fade ? 1 - ctx.phase : 1;
    if (fade <= 0) return;
    const gradient = ctx.hasGradient("color"), solid = s.color;
    if (!gradient && solid.a === 0) return;
    const w = img.width, h = img.height, d = img.data;

    // 고리 중심 픽셀: 가운데·아래 가운데는 보이는 그림 범위 기준(비면 원본)
    let cx = 0, cy = 0;
    if (s.center === "Custom") {
      cx = ctx.source.x + s.centerX;
      cy = ctx.source.y + s.centerY;
    } else if (s.center === "Pivot") {
      cx = Math.floor(ctx.pivot.x);
      cy = Math.floor(ctx.pivot.y);
    } else {
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
      if (right <= left) {
        left = ctx.source.x;
        top = ctx.source.y;
        right = left + ctx.source.w;
        bottom = top + ctx.source.h;
      }
      cx = left + Math.trunc((right - left) / 2);
      cy = s.center === "BottomCenter" ? bottom - 1 : top + Math.trunc((bottom - top) / 2);
    }

    // 고리가 닿는 칸(중심 둘레 반지름 + 1의 정사각형)만 표시해 둔다
    const left = cx - r - 1, top = cy - r - 1, side = 2 * r + 3;
    const ring = new Uint8Array(side * side);
    const plot = (x, y) => {
      if (x >= 0 && x < w && y >= 0 && y < h) ring[(y - top) * side + x - left] = 1;
    };
    if (s.thickness === 1) {
      // 중점 원 그리기: 8분원마다 한 칸씩 — 빈틈·두 겹 없는 픽셀 원
      let x = r, y = 0, err = 1 - r;
      while (x >= y) {
        plot(cx + x, cy + y);
        plot(cx + y, cy + x);
        plot(cx - y, cy + x);
        plot(cx - x, cy + y);
        plot(cx - x, cy - y);
        plot(cx - y, cy - x);
        plot(cx + y, cy - x);
        plot(cx + x, cy - y);
        y++;
        if (err < 0) err += 2 * y + 1;
        else {
          x--;
          err += 2 * (y - x) + 1;
        }
      }
    } else {
      const outer = (r + 0.5) * (r + 0.5), innerR = r - s.thickness + 0.5, inner = innerR > 0 ? innerR * innerR : -1;
      for (let y = cy - r - 1; y <= cy + r + 1; y++) {
        for (let x = cx - r - 1; x <= cx + r + 1; x++) {
          const dx = x - cx, dy = y - cy, dd = dx * dx + dy * dy;
          if (dd > inner && dd <= outer) plot(x, y);
        }
      }
    }

    // Color32.BlendOver(float 계산)를 그대로: 아래 (br, bg, bb, ba) 위에 (tr, tg, tb, ta)를 겹쳐 d의 칸 i에 쓴다
    const fr = (v) => Math.fround(v);
    const roundEven = (v) => {
      const q = Math.round(v);
      return q - v === 0.5 && q % 2 !== 0 ? q - 1 : q;
    };
    const channel = (sc, dc, sa, da, inv, oa) => Math.min(Math.max(roundEven(fr(fr(fr(sc * sa) + fr(fr(dc * da) * inv)) / oa)), 0), 255);
    const blend = (i, br, bg, bb, ba, tr, tg, tb, ta) => {
      if (ta === 255 || ba === 0) {
        d[i] = tr;
        d[i + 1] = tg;
        d[i + 2] = tb;
        d[i + 3] = ta;
      } else if (ta === 0) {
        d[i] = br;
        d[i + 1] = bg;
        d[i + 2] = bb;
        d[i + 3] = ba;
      } else {
        const sa = fr(ta / 255), da = fr(ba / 255), inv = fr(1 - sa), oa = fr(sa + fr(da * inv));
        d[i] = channel(tr, br, sa, da, inv, oa);
        d[i + 1] = channel(tg, bg, sa, da, inv, oa);
        d[i + 2] = channel(tb, bb, sa, da, inv, oa);
        d[i + 3] = Math.min(Math.max(roundEven(fr(oa * 255)), 0), 255);
      }
    };

    const x0 = Math.max(0, left), x1 = Math.min(w, left + side), y0 = Math.max(0, top), y1 = Math.min(h, top + side);
    for (let y = y0; y < y1; y++) {
      for (let x = x0; x < x1; x++) {
        if (ring[(y - top) * side + x - left] === 0) continue;
        // 그라데이션은 고리 중심 둘레 반지름 사각형 기준
        let cr = solid.r, cg = solid.g, cb = solid.b, ca = solid.a;
        if (gradient) {
          const sampled = ctx.color("color", x, y, { x: cx - r, y: cy - r, w: 2 * r + 1, h: 2 * r + 1 });
          cr = sampled.r;
          cg = sampled.g;
          cb = sampled.b;
          ca = sampled.a;
        }
        const alpha = Math.round(ca * fade);
        if (alpha === 0) continue;
        const i = (y * w + x) * 4;
        if (s.behind) blend(i, cr, cg, cb, alpha, d[i], d[i + 1], d[i + 2], d[i + 3]);
        else blend(i, d[i], d[i + 1], d[i + 2], d[i + 3], cr, cg, cb, alpha);
      }
    }
  },
});
