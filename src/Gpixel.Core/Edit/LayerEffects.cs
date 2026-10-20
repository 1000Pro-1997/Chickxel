using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>효과가 그림 밖으로 넓히는 여백(px, 왼쪽·위·오른쪽·아래).</summary>
public readonly record struct EffectPadding(int Left, int Top, int Right, int Bottom)
{
    public bool IsNone => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}

/// <summary>
/// 레이어 효과(<see cref="LayerEffect"/>) 그리기. 효과 목록은 앞에서부터 차례로 적용되고(앞 효과의 결과가 다음 효과의 입력), 꺼진 효과는 건너뛴다.
/// 모양은 알파가 0이 아닌 픽셀이다. 바깥 외곽선·그림자는 그림 밖으로 번지므로 <see cref="Render(PixelImage, IReadOnlyList{LayerEffect})"/>는 그만큼(<see cref="Padding(IReadOnlyList{LayerEffect})"/>)
/// 넓힌 이미지와 원래 그림의 왼쪽 위가 놓인 자리를 돌려준다. 이미지 밖은 투명으로 본다.
/// 그리기 함수는 받은 목록을 프레임 범위(<see cref="LayerEffect.Frames"/>)와 관계없이 그린다 — 프레임 하나를 그릴 때는 부르는 쪽이
/// <see cref="At"/>으로 그 프레임에 입힐 효과만 골라 넘긴다.
/// </summary>
public static class LayerEffects
{
    /// <summary>켜진 효과가 하나라도 있는지(어느 프레임이든).</summary>
    public static bool AnyEnabled(IReadOnlyList<LayerEffect> effects)
    {
        for (int i = 0; i < effects.Count; i++)
            if (effects[i].Enabled) return true;
        return false;
    }

    /// <summary>프레임 <paramref name="frameIndex"/>에 입혀질(켜져 있고 범위 안인) 효과가 하나라도 있는지.</summary>
    public static bool AnyActive(IReadOnlyList<LayerEffect> effects, int frameIndex)
    {
        for (int i = 0; i < effects.Count; i++)
            if (effects[i].Enabled && effects[i].AppliesTo(frameIndex)) return true;
        return false;
    }

    /// <summary>
    /// 프레임 <paramref name="frameIndex"/>가 범위 안인 효과만(순서 그대로, 꺼진 효과도 남음 — 그리기가 건너뜀).
    /// 모두 범위 안이면 <paramref name="effects"/> 그대로(복사 없음).
    /// </summary>
    public static IReadOnlyList<LayerEffect> At(IReadOnlyList<LayerEffect> effects, int frameIndex)
    {
        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i].AppliesTo(frameIndex)) continue;
            var list = new List<LayerEffect>(effects.Count - 1);
            for (int k = 0; k < i; k++) list.Add(effects[k]);
            for (int k = i + 1; k < effects.Count; k++)
                if (effects[k].AppliesTo(frameIndex)) list.Add(effects[k]);
            return list;
        }
        return effects;
    }

    /// <summary>
    /// 켜진 효과들이 그림 밖으로 넓힐 수 있는 최대 여백(차례대로 쌓임: 외곽선 뒤 그림자면 외곽선까지 그림자가 진다).
    /// 프레임 범위는 보지 않으므로 어느 프레임의 효과 묶음 여백보다도 작지 않다(효과를 빼면 여백은 줄기만 함) — 모든 프레임 같은 크기로 넓힐 때 쓴다.
    /// </summary>
    public static EffectPadding Padding(IReadOnlyList<LayerEffect> effects)
    {
        int l = 0, t = 0, r = 0, b = 0;
        foreach (var e in effects)
        {
            if (!e.Enabled) continue;
            switch (e)
            {
                case OutlineEffect { Inside: false } o:
                    l += o.Thickness;
                    t += o.Thickness;
                    r += o.Thickness;
                    b += o.Thickness;
                    break;
                case ShadowEffect s:
                    l = Math.Max(l, l - s.OffsetX);
                    r = Math.Max(r, r + s.OffsetX);
                    t = Math.Max(t, t - s.OffsetY);
                    b = Math.Max(b, b + s.OffsetY);
                    break;
            }
        }
        return new EffectPadding(l, t, r, b);
    }

    /// <summary>
    /// <paramref name="width"/>×<paramref name="height"/> 그림의 여백. 넓힌 크기가 <see cref="PixelImage.MaxSize"/>를 넘는 축은 여백 없이(그 축은 이미지 끝에서 잘림).
    /// </summary>
    public static EffectPadding Padding(IReadOnlyList<LayerEffect> effects, int width, int height)
    {
        var p = Padding(effects);
        if (width + p.Left + p.Right > PixelImage.MaxSize) p = p with { Left = 0, Right = 0 };
        if (height + p.Top + p.Bottom > PixelImage.MaxSize) p = p with { Top = 0, Bottom = 0 };
        return p;
    }

    /// <summary>
    /// 효과를 입힌 그림. 켜진 효과가 없으면 <paramref name="source"/> 그대로(복사 없음, 위치 0,0).
    /// 있으면 여백만큼 넓힌 새 이미지와, 원래 그림 왼쪽 위를 기준으로 그 이미지의 왼쪽 위 위치(여백만큼 음수)를 돌려준다.
    /// </summary>
    public static (PixelImage Image, int OffsetX, int OffsetY) Render(PixelImage source, IReadOnlyList<LayerEffect> effects)
        => Render(source, effects, Padding(effects, source.Width, source.Height));

    /// <summary>
    /// 여백을 정해 두고 효과를 입힌 그림(<paramref name="padding"/>은 <paramref name="effects"/>의 여백 이상 — 프레임마다 효과가 달라도 같은 크기로).
    /// 여백이 없고 켜진 효과도 없으면 <paramref name="source"/> 그대로(복사 없음, 위치 0,0).
    /// </summary>
    public static (PixelImage Image, int OffsetX, int OffsetY) Render(PixelImage source, IReadOnlyList<LayerEffect> effects, EffectPadding padding)
    {
        if (padding.IsNone && !AnyEnabled(effects)) return (source, 0, 0);
        var image = new PixelImage(source.Width + padding.Left + padding.Right, source.Height + padding.Top + padding.Bottom);
        image.Blit(source, padding.Left, padding.Top);
        Apply(image, effects);
        return (image, -padding.Left, -padding.Top);
    }

    /// <summary>효과를 입힌 그림을 원래 크기로 자른 것(밖으로 번진 부분은 버림). 켜진 효과가 없으면 <paramref name="source"/> 그대로.</summary>
    public static PixelImage RenderClipped(PixelImage source, IReadOnlyList<LayerEffect> effects)
    {
        if (!AnyEnabled(effects)) return source;
        var (image, x, y) = Render(source, effects);
        return image.Crop(new Rectangle(-x, -y, source.Width, source.Height));
    }

    /// <summary>켜진 효과를 차례로 <paramref name="image"/>에 바로 입힌다(크기는 그대로 — 번질 자리는 부르는 쪽이 미리 넓혀 둔다).</summary>
    public static void Apply(PixelImage image, IReadOnlyList<LayerEffect> effects)
    {
        foreach (var e in effects)
        {
            if (!e.Enabled) continue;
            switch (e)
            {
                case OutlineEffect o: Outline(image, o); break;
                case ShadowEffect s: Shadow(image, s); break;
                case RimLightEffect r: Rim(image, r); break;
                case ColorOverlayEffect c: Overlay(image, c); break;
            }
        }
    }

    // ───────────── 효과 ─────────────

    private static void Outline(PixelImage image, OutlineEffect o)
    {
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        var shape = Mask(px);
        var grown = shape;
        if (o.Inside)
        {
            for (int k = 0; k < o.Thickness; k++) grown = Erode(grown, w, h, o.Diagonal);
            for (int i = 0; i < px.Length; i++)
                if (shape[i] && !grown[i]) px[i] = Mix(px[i], o.Color, o.Color.A);
        }
        else
        {
            for (int k = 0; k < o.Thickness; k++) grown = Dilate(grown, w, h, o.Diagonal);
            for (int i = 0; i < px.Length; i++)
                if (grown[i] && !shape[i]) px[i] = o.Color;
        }
    }

    private static void Shadow(PixelImage image, ShadowEffect s)
    {
        int w = image.Width, h = image.Height, dx = s.OffsetX, dy = s.OffsetY;
        var px = image.Pixels;
        var src = (Color32[])px.Clone();
        for (int y = 0; y < h; y++)
        {
            int sy = y - dy;
            if ((uint)sy >= (uint)h) continue;
            for (int x = 0; x < w; x++)
            {
                int sx = x - dx;
                if ((uint)sx >= (uint)w) continue;
                byte a = src[sy * w + sx].A;
                if (a == 0) continue;
                var shadow = s.Color.WithAlpha((byte)((s.Color.A * a + 127) / 255));
                int i = y * w + x;
                px[i] = Color32.BlendOver(shadow, src[i]);
            }
        }
    }

    private static void Rim(PixelImage image, RimLightEffect r)
    {
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        var shape = Mask(px);
        var (vx, vy) = r.Vector;
        bool Solid(int x, int y) => (uint)x < (uint)w && (uint)y < (uint)h && shape[y * w + x];
        bool Edge(int x, int y, int dx, int dy) => (dx != 0 && !Solid(x + dx, y)) || (dy != 0 && !Solid(x, y + dy));
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!shape[i]) continue;
                if (r.Light.A != 0 && Edge(x, y, vx, vy)) px[i] = Mix(px[i], r.Light, r.Light.A);
                else if (r.Shade.A != 0 && Edge(x, y, -vx, -vy)) px[i] = Mix(px[i], r.Shade, r.Shade.A);
            }
    }

    private static void Overlay(PixelImage image, ColorOverlayEffect c)
    {
        int amount = (c.Amount * 255 + 50) / 100;
        if (amount == 0) return;
        var px = image.Pixels;
        for (int i = 0; i < px.Length; i++)
            if (px[i].A != 0) px[i] = Mix(px[i], c.Color, amount);
    }

    // ───────────── 도우미 ─────────────

    /// <summary>알파가 0이 아닌 픽셀.</summary>
    private static bool[] Mask(Color32[] px)
    {
        var mask = new bool[px.Length];
        for (int i = 0; i < px.Length; i++) mask[i] = px[i].A != 0;
        return mask;
    }

    /// <summary>한 칸 넓히기(4방향 = 십자, 8방향 = 모서리 포함).</summary>
    private static bool[] Dilate(bool[] src, int w, int h, bool diagonal)
    {
        var dst = new bool[src.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                dst[i] = src[i] || Any(src, w, h, x, y, diagonal);
            }
        return dst;
    }

    /// <summary>한 칸 깎기: 이웃(4/8방향) 중 하나라도 비었거나 이미지 밖이면 빠진다.</summary>
    private static bool[] Erode(bool[] src, int w, int h, bool diagonal)
    {
        var dst = new bool[src.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                dst[i] = src[i] && All(src, w, h, x, y, diagonal);
            }
        return dst;
    }

    private static readonly (int X, int Y)[] Cross = [(-1, 0), (1, 0), (0, -1), (0, 1)];
    private static readonly (int X, int Y)[] Square = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    private static bool Any(bool[] m, int w, int h, int x, int y, bool diagonal)
    {
        foreach (var (dx, dy) in diagonal ? Square : Cross)
        {
            int nx = x + dx, ny = y + dy;
            if ((uint)nx < (uint)w && (uint)ny < (uint)h && m[ny * w + nx]) return true;
        }
        return false;
    }

    private static bool All(bool[] m, int w, int h, int x, int y, bool diagonal)
    {
        foreach (var (dx, dy) in diagonal ? Square : Cross)
        {
            int nx = x + dx, ny = y + dy;
            if ((uint)nx >= (uint)w || (uint)ny >= (uint)h || !m[ny * w + nx]) return false;
        }
        return true;
    }

    /// <summary><paramref name="dst"/>의 RGB를 <paramref name="color"/> 쪽으로 <paramref name="amount"/>/255만큼(알파는 그대로).</summary>
    private static Color32 Mix(Color32 dst, Color32 color, int amount)
    {
        if (amount <= 0) return dst;
        int keep = 255 - amount;
        byte M(byte d, byte c) => (byte)((d * keep + c * amount + 127) / 255);
        return new Color32(M(dst.R, color.R), M(dst.G, color.G), M(dst.B, color.B), dst.A);
    }
}
