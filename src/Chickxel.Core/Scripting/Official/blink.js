// 깜빡이기(BlinkRender)를 JS로 옮긴 공식 스크립트 효과. 같은 설정이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "깜빡이기",
  description: "보였다 숨었다를 되풀이합니다(무적 시간 표시 등).",
  translations: {
    ko: {
      name: "깜빡이기",
      description: "보였다 숨었다를 되풀이합니다(무적 시간 표시 등).",
      labels: {
        timing: "간격", "timing.Frames": "프레임 수로", "timing.Count": "범위 안 횟수로", onFrames: "보임 프레임", offFrames: "숨김 프레임",
        count: "횟수", hide: "숨김 방식", "hide.Transparent": "완전 투명", "hide.Translucent": "반투명", "hide.Dither": "디더 50%", opacity: "불투명도",
      },
    },
    en: {
      name: "Blink",
      description: "Repeatedly shows and hides (e.g. to indicate invincibility frames).",
      labels: {
        timing: "Interval", "timing.Frames": "By frame count", "timing.Count": "By count in range", onFrames: "Visible frames", offFrames: "Hidden frames",
        count: "Count", hide: "Hide style", "hide.Transparent": "Fully transparent", "hide.Translucent": "Translucent", "hide.Dither": "Dither 50%", opacity: "Opacity",
      },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "ninja",
  // 숨는 프레임은 범위 안 프레임 번호로만 정한다(빠르기 곡선·반복을 쓰지 않음)
  motion: false,
  settings: [
    {
      key: "timing", label: "간격", type: "choice", default: "Frames",
      choices: [{ value: "Frames", label: "프레임 수로" }, { value: "Count", label: "범위 안 횟수로" }],
    },
    { key: "onFrames", label: "보임 프레임", type: "int", min: 1, max: 16, default: 1 },
    { key: "offFrames", label: "숨김 프레임", type: "int", min: 1, max: 16, default: 1 },
    { key: "count", label: "횟수", type: "int", min: 1, max: 16, default: 4 },
    {
      key: "hide", label: "숨김 방식", type: "choice", default: "Transparent",
      choices: [{ value: "Transparent", label: "완전 투명" }, { value: "Translucent", label: "반투명" }, { value: "Dither", label: "디더 50%" }],
    },
    { key: "opacity", label: "불투명도", type: "int", min: 0, max: 100, default: 40 },
  ],
  render(img, ctx, s) {
    // 프레임 수로: 범위 안 몇 번째 프레임 s에서 s mod (보임 + 숨김) ≥ 보임이면 숨김.
    // 횟수로: 범위의 프레임 n개를 2 × 횟수 칸으로 나눠 ⌊s × 2 × 횟수 / n⌋이 홀수면 숨김(빠르기 곡선·반복과 무관)
    const hidden = s.timing === "Frames"
      ? ctx.step % (s.onFrames + s.offFrames) >= s.onFrames
      : (Math.trunc(ctx.step * 2 * s.count / (ctx.span + 1)) & 1) === 1;
    if (!hidden) return;
    const w = img.width, h = img.height, d = img.data;
    if (s.hide === "Transparent") {
      for (let i = 0; i < d.length; i++) d[i] = 0;
    } else if (s.hide === "Translucent") {
      for (let i = 0; i < d.length; i += 4) {
        const a = d[i + 3];
        if (a === 0) continue;
        const alpha = Math.trunc((a * s.opacity + 50) / 100);
        if (alpha === 0) {
          d[i] = 0;
          d[i + 1] = 0;
          d[i + 2] = 0;
        }
        d[i + 3] = alpha;
      }
    } else {
      // 바둑판 칸만 남긴다. 원본 왼쪽 위 기준이라 앞 효과가 여백을 넓혀도 무늬가 그대로
      for (let y = 0; y < h; y++) {
        const offset = y - ctx.source.y - ctx.source.x;
        for (let x = 0; x < w; x++) {
          if (((x + offset) & 1) === 0) continue;
          const i = (y * w + x) * 4;
          d[i] = 0;
          d[i + 1] = 0;
          d[i + 2] = 0;
          d[i + 3] = 0;
        }
      }
    }
  },
});
