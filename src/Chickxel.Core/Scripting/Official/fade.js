// 페이드 인·아웃(FadeRender)을 JS로 옮긴 공식 스크립트 효과. 같은 설정이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "페이드 인·아웃",
  language: "ko",
  description: "프레임 범위 동안 그림 전체의 투명도를 점차 높이거나 낮춥니다.",
  translations: {
    ko: {
      name: "페이드 인·아웃",
      description: "프레임 범위 동안 그림 전체의 투명도를 점차 높이거나 낮춥니다.",
      labels: { fadeOut: "페이드 아웃" },
    },
    en: {
      name: "Fade in · out",
      description: "Gradually raises or lowers the opacity of the whole image over the frame range.",
      labels: { fadeOut: "Fade out" },
    },
  },
  animated: true,
  easing: "Linear",
  previewChick: "original",
  settings: [
    { key: "fadeOut", label: "페이드 아웃", type: "bool", default: false },
  ],
  render(img, ctx, s) {
    // 보이는 정도: 페이드 인은 진행 그대로, 페이드 아웃은 1 − 진행
    const visibility = s.fadeOut ? 1 - ctx.progress : ctx.progress;
    if (visibility >= 1) return;
    const d = img.data;
    if (visibility <= 0) {
      for (let i = 0; i < d.length; i++) d[i] = 0;
      return;
    }
    for (let i = 0; i < d.length; i += 4) {
      const a = d[i + 3];
      if (a === 0) continue;
      // C#의 AwayFromZero 반올림: 양수에서는 Math.round(.5는 위로)와 같다. 0이 되면 완전 투명(RGB도 0)
      const alpha = Math.round(a * visibility);
      if (alpha === 0) {
        d[i] = 0;
        d[i + 1] = 0;
        d[i + 2] = 0;
      }
      d[i + 3] = alpha;
    }
  },
});
