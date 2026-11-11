using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Chickxel.App;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace GuideShots;

/// <summary>
/// 선택 도구 쪽(tools-select)의 합치기(Shift·Alt)·커서·스마트 선택 방식·미리보기 그림. 번호 붙은 그림은 <c>tools-select</c> 샷(①~④)에 이어
/// ⑤부터, 비교 그림(<c>tools-select-compare-*</c>)은 번호 없이 왼쪽부터 나란히 놓는다(가이드 그림 설명에 "왼쪽부터: …").
/// 캔버스는 8배로 키우고 피벗 표시를 끄며, 예시 그림 위쪽(머리 ~ 가슴, 0,0 ~ 49,62)만 자른다.
/// </summary>
internal static class SelectShots
{
    // 합치기 비교의 두 사각형(프레임 픽셀, 왼쪽 위 ~ 오른쪽 아래 칸): A = 머리, B = 얼굴 오른쪽 ~ 가슴으로 비스듬히 겹침.
    private static readonly (int X, int Y, int R, int B) RectA = (7, 3, 34, 34), RectB = (20, 22, 44, 56);
    // 스마트 선택을 누르거나 마우스를 올리는 곳: 머리카락(밝은 회색) · 얼굴 피부. 허용 오차 10%면 머리카락의 흰 반짝임·그림자와 피부는 따로다.
    private const double HairX = 22.5, HairY = 18.5, SkinX = 26.5, SkinY = 40.5;
    private const int Tolerance = 10, Zoom = 8;

    private static MainForm Open(ShotKit k)
    {
        App.Settings.RightWidth = 330;
        var m = ToolFileHelp.Open(k, ToolFileHelp.Sample(k), 110);
        ShotKit.Call(m, "SetPixelGrid", false);
        ShotKit.Call(m, "SetShowPivot", false);
        var canvas = ToolFileHelp.Canvas(m);
        canvas.SetZoom(Zoom);
        ShotKit.Set(canvas, "_offset", new System.Drawing.Point(24, 24));
        canvas.InvalidateVisual();
        k.Settle(300);
        return m;
    }

    /// <summary>자르는 곳: 예시 그림 위쪽(머리 ~ 가슴) + 둘레 1px.</summary>
    private static Rect Area(MainForm m) => Px(m, -1, -1, 51, 64);

    private static Rect Px(MainForm m, double x, double y, double w, double h)
        => new(ToolFileHelp.At(m, x, y), ToolFileHelp.At(m, x + w, y + h));

    /// <summary>마우스를 그림 밖(캔버스 빈 곳)으로 — 미리보기·커서 자리 표시가 남지 않게.</summary>
    private static void Away(ShotKit k, MainForm m)
    {
        m.MouseMove(ToolFileHelp.At(m, 52, 64));
        k.Settle(300);
    }

    private static void Hover(ShotKit k, MainForm m, double x, double y, RawInputModifiers mods)
    {
        m.MouseMove(ToolFileHelp.At(m, x, y), mods);
        k.Settle(400);
    }

    private static void DragRect(ShotKit k, MainForm m, (int X, int Y, int R, int B) r, RawInputModifiers mods)
        => k.Drag(m, ToolFileHelp.At(m, r.X + 0.5, r.Y + 0.5), ToolFileHelp.At(m, r.R + 0.5, r.B + 0.5), mods: mods);

    private static void Wand(ShotKit k, MainForm m, WandMode mode, bool preview)
    {
        var ctx = ToolFileHelp.Ctx(m);
        ToolFileHelp.Tool(k, "스마트 선택");
        ctx.FillTolerance = Tolerance;
        ctx.WandMode = mode;
        ctx.WandPreview = preview;
    }

    /// <summary>
    /// 사각 선택 합치기: 머리 사각형(A)을 고른 뒤 B를 Shift(더하기) / Alt(빼기) / Shift+Alt(겹친 곳만)로 끈 결과(개미 줄).
    /// </summary>
    [Shot("tools-select-compare-combine")]
    public static void Combine(ShotKit k)
    {
        var m = Open(k);
        ToolFileHelp.Tool(k, "사각 선택");
        var shots = new List<(Bitmap, Rect)>();
        foreach (var mods in new[] { RawInputModifiers.Shift, RawInputModifiers.Alt, RawInputModifiers.Shift | RawInputModifiers.Alt })
        {
            k.Command("편집/선택 해제");
            DragRect(k, m, RectA, RawInputModifiers.None);
            DragRect(k, m, RectB, mods);
            Away(k, m);
            shots.Add((k.Capture(m), Area(m)));
        }
        GuideScreenUiCompareShots.Compose(k, k.Id, shots);
    }

    /// <summary>선택 도구 커서 네 가지(앱이 쓰는 그림 그대로, 8배): 바꾸기 · 더하기(Shift) · 빼기(Alt) · 겹친 곳만(Shift+Alt).</summary>
    [Shot("tools-select-compare-cursor")]
    public static void Cursors(ShotKit k)
    {
        const int scale = 8, check = 8, margin = 4, pad = 16, gap = 24;
        var back = new Color32(32, 32, 36);
        var checkA = new Color32(206, 206, 212);
        var checkB = new Color32(234, 234, 238);
        var images = Enum.GetValues<SelectionCombine>()
            .Select(mode => (PixelImage)typeof(PixelCanvas).GetMethod("SelectCursorImage",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [mode])!)
            .ToList();
        int cw = (images[0].Width + margin * 2) * scale, ch = (images[0].Height + margin * 2) * scale;
        var sheet = new PixelImage(pad * 2 + images.Count * cw + (images.Count - 1) * gap, pad * 2 + ch);
        sheet.Clear(back);
        for (int i = 0; i < images.Count; i++)
        {
            var cell = new PixelImage(cw, ch);
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                    cell.Pixels[y * cw + x] = ((x / check) + (y / check)) % 2 == 1 ? checkA : checkB;
            var src = images[i];
            var big = new PixelImage(src.Width * scale, src.Height * scale);
            for (int y = 0; y < big.Height; y++)
                for (int x = 0; x < big.Width; x++)
                    big.Pixels[y * big.Width + x] = src.Pixels[y / scale * src.Width + x / scale];
            cell.Composite(big, margin * scale, margin * scale);
            sheet.Blit(cell, pad + i * (cw + gap), pad);
        }
        var file = Path.Combine(ShotKit.OutDir, k.Id + ".png");
        Directory.CreateDirectory(ShotKit.OutDir);
        using (var bmp = ImageUtil.ToBitmap(sheet)) bmp.Save(file);
        k.Saved.Add(Path.GetRelativePath(ShotKit.Root, file));
    }

    /// <summary>스마트 선택 도구 설정: ⑤ 선택 방식(목록을 펼친 채) · ⑥ 미리보기와 한도.</summary>
    [Shot("tools-select-4")]
    public static void WandOptions(ShotKit k)
    {
        var m = Open(k);
        Wand(k, m, WandMode.Tolerance, preview: true);
        var options = ToolFileHelp.Options(m);
        var modeRow = ShotKit.Get<Control>(options, "_wandModeRow");
        var previewRow = ShotKit.Get<Control>(options, "_wandPreviewRow");
        var combo = ShotKit.Get<ComboBox>(options, "_wandMode");
        var box = ShotKit.Bounds(ShotKit.Get<Control>(options, "_fillBox"), m);
        combo.IsDropDownOpen = true;
        k.Settle(400);
        k.Save(m, crop: box.Inflate(10), marks: [Lead(ShotKit.Bounds(modeRow, m)), Lead(ShotKit.Bounds(previewRow, m))], firstNumber: 5);
        combo.IsDropDownOpen = false;
        k.Settle(200);
    }

    /// <summary>번호 동그라미가 첫 글자를 가리지 않게 왼쪽을 조금 늘린다.</summary>
    private static Rect Lead(Rect r) => new(r.X - 12, r.Y - 1, r.Width + 12, r.Height + 2);

    /// <summary>머리카락을 고른 뒤 얼굴 피부에 마우스를 올리고 Shift를 누른 상태: ⑦ 지금 선택(개미 줄) · ⑧ 누르면 더해질 칸(파랑).</summary>
    [Shot("tools-select-5")]
    public static void WandPreview(ShotKit k)
    {
        var m = Open(k);
        var ctx = ToolFileHelp.Ctx(m);
        Wand(k, m, WandMode.Tolerance, preview: true);
        k.Click(m, ToolFileHelp.At(m, HairX, HairY));
        var b = ctx.Selection.Bounds;
        var selected = Px(m, b.X, b.Y, b.Width, b.Height);
        Hover(k, m, SkinX, SkinY, RawInputModifiers.Shift);
        var added = PreviewBounds(m, added: true);
        k.Save(m, crop: Area(m), marks: [selected, added.Inflate(4)], firstNumber: 7);
    }

    /// <summary>
    /// 미리보기와 합치기: 머리카락을 고른 채 마우스를 올린 상태 — 키 없이 피부(피부가 파랗게 새로 선택, 머리카락이 빨갛게 빠짐) /
    /// Shift로 피부(피부만 파랗게 더해짐) / Alt로 머리카락(머리카락이 빨갛게 빠짐). 머리카락이 300칸을 넘어 한도를 1000으로.
    /// </summary>
    [Shot("tools-select-compare-preview")]
    public static void PreviewCombine(ShotKit k)
    {
        var m = Open(k);
        var ctx = ToolFileHelp.Ctx(m);
        Wand(k, m, WandMode.Tolerance, preview: true);
        ctx.WandPreviewLimit = 1000;
        k.Click(m, ToolFileHelp.At(m, HairX, HairY));
        var shots = new List<(Bitmap, Rect)>();
        foreach (var (x, y, mods) in new[] { (SkinX, SkinY, RawInputModifiers.None), (SkinX, SkinY, RawInputModifiers.Shift), (HairX, HairY, RawInputModifiers.Alt) })
        {
            Away(k, m);
            Hover(k, m, x, y, mods);
            PreviewBounds(m, added: mods != RawInputModifiers.Alt);
            shots.Add((k.Capture(m), Area(m)));
        }
        GuideScreenUiCompareShots.Compose(k, k.Id, shots);
    }

    /// <summary>
    /// 스마트 선택 방식 세 가지: 선택이 없을 때 같은 곳(머리카락)에 마우스를 올린 미리보기(파랑 = 누르면 고를 칸) —
    /// 허용 오차 · 바깥 1칸 더하기 · 맞닿은 색까지. 넓은 영역도 보이게 한도를 최대로.
    /// </summary>
    [Shot("tools-select-compare-wand")]
    public static void WandModes(ShotKit k)
    {
        var m = Open(k);
        var ctx = ToolFileHelp.Ctx(m);
        var shots = new List<(Bitmap, Rect)>();
        foreach (var mode in new[] { WandMode.Tolerance, WandMode.Grow, WandMode.Touching })
        {
            Wand(k, m, mode, preview: true);
            ctx.WandPreviewLimit = EditorContext.MaxWandPreviewLimit;
            Away(k, m);
            Hover(k, m, HairX, HairY, RawInputModifiers.None);
            PreviewBounds(m, added: true);
            shots.Add((k.Capture(m), Area(m)));
        }
        GuideScreenUiCompareShots.Compose(k, k.Id, shots);
    }

    /// <summary>캔버스가 그리고 있는 스마트 선택 미리보기(더해질 칸 또는 빠질 칸)를 감싼 창 좌표 사각형. 비어 있으면 실패(샷이 뜻대로 안 찍힘).</summary>
    private static Rect PreviewBounds(MainForm m, bool added)
    {
        var pv = ShotKit.Get<object?>(ToolFileHelp.Canvas(m), "_wandPreview") ?? throw new InvalidOperationException("no wand preview");
        var spans = (List<System.Drawing.Rectangle>)pv.GetType().GetField(added ? "Item3" : "Item4")!.GetValue(pv)!;
        if (spans.Count == 0) throw new InvalidOperationException("wand preview is empty");
        var u = spans.Aggregate(System.Drawing.Rectangle.Union);
        return Px(m, u.X, u.Y, u.Width, u.Height);
    }
}
