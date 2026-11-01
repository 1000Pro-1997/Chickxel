effect({
  name: "새 효과",
  language: "ko",
  settings: [
    { key: "strength", label: "세기", type: "int", min: 0, max: 100, default: 50 },
  ],
  render(img, ctx, s) {
    const d = img.data, k = s.strength / 100;
    for (let i = 0; i < d.length; i += 4) {
      if (d[i + 3] === 0) continue; // 투명한 픽셀은 건너뜀
      d[i] = lerp(d[i], 255, k); // 빨강 쪽으로
    }
  },
});
