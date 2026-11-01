// 색조 보정(ColorAdjustRender)을 JS로 옮긴 공식 스크립트 효과. 같은 설정이면 C# 결과와 픽셀 단위로 같아야 한다.
effect({
  name: "색조 보정",
  description: "흑백·세피아·색조 이동·밝기·대비·채도로 색을 바꿉니다(그림은 그대로).",
  translations: {
    ko: {
      name: "색조 보정",
      description: "흑백·세피아·색조 이동·밝기·대비·채도로 색을 바꿉니다(그림은 그대로).",
      labels: {
        mode: "방식", "mode.Grayscale": "흑백", "mode.Sepia": "세피아", "mode.HueShift": "색조 이동", "mode.BrightnessContrast": "밝기·대비",
        "mode.Saturation": "채도", amount: "양", hue: "색상", brightness: "밝기", contrast: "대비", saturation: "채도",
      },
    },
    en: {
      name: "Color adjustment",
      description: "Changes colors with grayscale, sepia, hue shift, brightness, contrast, and saturation (image unchanged).",
      labels: {
        mode: "Style", "mode.Grayscale": "Grayscale", "mode.Sepia": "Sepia", "mode.HueShift": "Hue shift", "mode.BrightnessContrast": "Brightness/contrast",
        "mode.Saturation": "Saturation", amount: "Amount", hue: "Hue", brightness: "Brightness", contrast: "Contrast", saturation: "Saturation",
      },
    },
  },
  animated: false,
  previewChick: "palette",
  settings: [
    {
      key: "mode", label: "방식", type: "choice", default: "Grayscale",
      choices: [
        { value: "Grayscale", label: "흑백" }, { value: "Sepia", label: "세피아" }, { value: "HueShift", label: "색조 이동" },
        { value: "BrightnessContrast", label: "밝기·대비" }, { value: "Saturation", label: "채도" },
      ],
    },
    { key: "amount", label: "양", type: "int", min: 0, max: 100, default: 100 },
    { key: "hue", label: "색상", type: "int", min: -180, max: 180, default: 30 },
    { key: "brightness", label: "밝기", type: "int", min: -100, max: 100, default: 0 },
    { key: "contrast", label: "대비", type: "int", min: -100, max: 100, default: 0 },
    { key: "saturation", label: "채도", type: "int", min: -100, max: 100, default: 50 },
  ],
  render(img, ctx, s) {
    // 방식마다 쓰는 값만 쓴다. 바뀌지 않는 값이면 그대로
    const gray = s.mode === "Grayscale", sepia = s.mode === "Sepia", hueShift = s.mode === "HueShift";
    const brightnessContrast = s.mode === "BrightnessContrast";
    if ((gray || sepia) && s.amount <= 0) return;
    if (hueShift && s.hue % 360 === 0) return;
    if (brightnessContrast && s.brightness === 0 && s.contrast === 0) return;
    if (!gray && !sepia && !hueShift && !brightnessContrast && s.saturation === 0) return;

    // 흑백·세피아 쪽으로 섞는 양(C# 정수 나눗셈 = 버림, 모두 0 이상)
    const amount = Math.trunc((s.amount * 255 + 50) / 100);
    const mix = (v, c) => Math.trunc((v * (255 - amount) + c * amount + 127) / 255);
    // C#의 AwayFromZero 반올림 뒤 0~255: 음수 쪽 차이는 0으로 잘려 사라진다
    const clamp = (v) => Math.min(255, Math.max(0, Math.round(v)));
    const contrast = 1 + s.contrast / 100, add = s.brightness * 255 / 100, saturation = 1 + s.saturation / 100;
    const d = img.data;
    for (let i = 0; i < d.length; i += 4) {
      if (d[i + 3] === 0) continue;
      const r = d[i], g = d[i + 1], b = d[i + 2];
      if (gray) {
        const l = clamp(0.299 * r + 0.587 * g + 0.114 * b);
        d[i] = mix(r, l);
        d[i + 1] = mix(g, l);
        d[i + 2] = mix(b, l);
      } else if (sepia) {
        d[i] = mix(r, clamp(0.393 * r + 0.769 * g + 0.189 * b));
        d[i + 1] = mix(g, clamp(0.349 * r + 0.686 * g + 0.168 * b));
        d[i + 2] = mix(b, clamp(0.272 * r + 0.534 * g + 0.131 * b));
      } else if (hueShift) {
        // Color32.ToHsv/FromHsv와 같은 float 계산
        const hsv = rgbToHsv(r, g, b);
        const c = hsvToRgb(hsv.h + s.hue, hsv.s, hsv.v);
        d[i] = c.r;
        d[i + 1] = c.g;
        d[i + 2] = c.b;
      } else if (brightnessContrast) {
        d[i] = clamp((r - 128) * contrast + 128 + add);
        d[i + 1] = clamp((g - 128) * contrast + 128 + add);
        d[i + 2] = clamp((b - 128) * contrast + 128 + add);
      } else {
        const l = 0.299 * r + 0.587 * g + 0.114 * b;
        d[i] = clamp(l + (r - l) * saturation);
        d[i + 1] = clamp(l + (g - l) * saturation);
        d[i + 2] = clamp(l + (b - l) * saturation);
      }
    }
  },
});
