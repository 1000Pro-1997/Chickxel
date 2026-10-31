using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>팔레트 색으로 변환할 범위.</summary>
public enum PaletteConvertScope
{
    /// <summary>현재 레이어의 현재 프레임(선택 영역이 있으면 그 안만).</summary>
    CurrentCel,
    /// <summary>현재 레이어의 모든 프레임(부위면 모든 스킨 셀).</summary>
    CurrentLayer,
    /// <summary>모든 레이어·프레임(스킨 셀 포함).</summary>
    All,
}

/// <summary>
/// 팔레트 색으로 변환: 대상(현재 셀 / 현재 레이어 모든 프레임 / 모든 레이어·프레임)과 디더링(없음 / Bayer 4×4)을 고르고,
/// 현재 프레임의 전·후 미리보기와 대상 셀의 색 수(전 → 후)를 보인다. 잠긴 레이어는 건너뛰고, 실행취소 한 단계다.
/// 색 고르기는 <see cref="PaletteRemap"/>(redmean 가중 RGB 최근접, 알파 유지, 완전 투명은 그대로).
/// </summary>
public sealed class PaletteConvertDialog : DialogBase
{
    private static readonly string Label = UiStrings.PaletteConvert_Title;

    private readonly EditorContext _ctx;
    private readonly SpriteProject _project;
    private readonly PaletteRemap _remap;
    private readonly RadioButton _cel, _layer, _all, _noDither, _bayer;
    private readonly ImageView _before, _after;
    private readonly TextBlock _count;
    /// <summary>디더링마다 셀 → 바꾼 사본(바뀐 것이 없으면 null). 미리보기·색 수 계산과 실제 변환이 같이 쓴다.</summary>
    private readonly Dictionary<PaletteDither, Dictionary<PixelImage, PixelImage?>> _mapped = new();

    private PaletteConvertDialog(EditorContext ctx, SpriteProject project, PaletteRemap remap, string paletteName) : base($"{Label} — {paletteName}", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_52F9376144)
    {
        _ctx = ctx;
        _project = project;
        _remap = remap;
        _cel = Radio("scope", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_4BF144BF9A);
        _layer = Radio("scope", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_982BB7EF41);
        _all = Radio("scope", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_28DF63893B);
        _noDither = Radio("dither", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_541E54A55D);
        _bayer = Radio("dither", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_BDEC5A51EB);
        _cel.IsEnabled = ctx.CanEdit;
        _layer.IsEnabled = ctx.CurrentLayerObject is { IsGroup: false } && !project.IsLayerLocked(ctx.CurrentLayer);
        (_cel.IsEnabled ? _cel : _layer.IsEnabled ? _layer : _all).IsChecked = true;
        _noDither.IsChecked = true;

        _before = new ImageView(UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_8D277CDF45);
        _after = new ImageView(UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_487B63AF2E);
        _count = new TextBlock { Foreground = AppTheme.TextBrush, Margin = new Thickness(2, 4, 2, 0) };

        Body.Children.Add(Ui.Label(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_8CE25638B7,paletteName,remap.Count)));
        Body.Children.Add(Ui.Label(UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_2AE3FF9667, bold: true));
        Body.Children.Add(_cel);
        Body.Children.Add(_layer);
        Body.Children.Add(_all);
        Body.Children.Add(Ui.Label(UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_3E84BC3FF1, bold: true));
        Body.Children.Add(_noDither);
        Body.Children.Add(_bayer);
        Body.Children.Add(Ui.Row(_before, _after));
        Body.Children.Add(_count);
        Body.Children.Add(Ui.Label(UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_0988821852));

        foreach (var r in new[] { _cel, _layer, _all, _noDither, _bayer })
            // 같은 묶음의 다른 단추가 아직 꺼지기 전에 불리므로, 한 묶음의 바뀜이 다 끝난 뒤에 미리보기를 고친다.
            r.IsCheckedChanged += (_, _) => { if (r.On()) Dispatcher.UIThread.Post(UpdatePreview); };
        UpdatePreview();
    }

    public PaletteConvertScope Scope => _cel.On() ? PaletteConvertScope.CurrentCel : _layer.On() ? PaletteConvertScope.CurrentLayer : PaletteConvertScope.All;
    public PaletteDither Dither => _bayer.On() ? PaletteDither.Bayer4 : PaletteDither.None;

    /// <summary>
    /// 팔레트 색으로 변환 대화상자를 띄우고 고른 대로 바꾼다. 상태 표시줄에 보일 문구를 돌려준다(취소했거나 프로젝트가 없으면 null).
    /// 팔레트에 투명이 아닌 색이 없으면 알리고 null.
    /// </summary>
    public static string? Run(EditorContext ctx, Visual? owner, IReadOnlyList<Color32> palette, string paletteName)
    {
        if (ctx.Project is not { } p) return null;
        PaletteRemap remap;
        try
        {
            remap = new PaletteRemap(palette);
        }
        catch (ArgumentException)
        {
            Ui.Info(owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PaletteConvertDialog_Run_6433CC1607,paletteName), Label);
            return null;
        }
        ctx.CommitFloating();
        var dlg = new PaletteConvertDialog(ctx, p, remap, paletteName);
        if (dlg.ShowDialog(owner) != DialogResult.OK || ctx.Project != p) return null;
        return dlg.Apply();
    }

    private string Apply()
    {
        var dither = Dither;
        if (Scope == PaletteConvertScope.CurrentCel)
        {
            if (!_ctx.CanEdit) return UiStrings.S_PaletteConvertDialog_Apply_2B4635834B;
            _ctx.BeginStroke();
            int n = _remap.Apply(_ctx.EditImage!, dither, SelectionClip);
            _ctx.NotifyPixels();
            _ctx.CommitStroke(Label);
            return n > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PaletteConvertDialog_Apply_7FEFC3F8B0,Label,n) : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PaletteConvertDialog_Apply_2EF2CA7E96,Label);
        }
        int cels = 0;
        var target = Target();
        _ctx.Structural(() => cels = _project.MapCels(Label, target, cel => Mapped(cel, dither)));
        return cels > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PaletteConvertDialog_Apply_4B82D58C1B,Label,cels) : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PaletteConvertDialog_Apply_2EF2CA7E96,Label);
    }

    private PixelMask? SelectionClip => _ctx.Selection.HasSelection ? _ctx.Selection.Region : null;

    /// <summary>범위에 드는 레이어·프레임(잠긴 레이어 제외 — <see cref="SpriteProject.MapCels"/>도 건너뜀).</summary>
    private Func<Layer, Frame, bool> Target()
    {
        var current = _ctx.CurrentLayerObject;
        var frame = _project.Frames[_ctx.CurrentFrame];
        bool all = Scope == PaletteConvertScope.All, cel = Scope == PaletteConvertScope.CurrentCel;
        return (l, f) => !(l.Locked || (l.Parent?.Locked ?? false))
            && (all || (ReferenceEquals(l, current) && (!cel || ReferenceEquals(f, frame))));
    }

    /// <summary>셀을 바꾼 사본(바뀐 것이 없으면 null, 기억해 둠).</summary>
    private PixelImage? Mapped(PixelImage cel, PaletteDither dither)
    {
        if (!_mapped.TryGetValue(dither, out var cache)) _mapped[dither] = cache = new(ReferenceEqualityComparer.Instance);
        if (!cache.TryGetValue(cel, out var result)) cache[cel] = result = _remap.Remapped(cel, dither);
        return result;
    }

    private void UpdatePreview()
    {
        var dither = Dither;
        int frame = _ctx.CurrentFrame;
        var current = _ctx.CurrentLayerObject;
        var target = Target();
        var frameObj = _project.Frames[frame];
        bool cel = Scope == PaletteConvertScope.CurrentCel;
        var clip = SelectionClip;

        // 현재 셀: 선택 영역 안만 바꾼 사본(실제 변환과 같은 좌표·디더 무늬).
        PixelImage CelMap(Layer layer, PixelImage image)
        {
            if (!target(layer, frameObj)) return image;
            if (!cel || clip is null) return Mapped(image, dither) ?? image;
            var copy = image.Clone();
            _remap.Apply(copy, dither, clip);
            return copy;
        }

        _before.SetImage(_project.Flatten(frame));
        var after = new PixelImage(_project.Width, _project.Height);
        _project.FlattenInto(after, frame, celMap: CelMap);
        _after.SetImage(after);

        // 대상 셀들의 색 수(전 → 후).
        var before = new HashSet<uint>();
        var afterSet = new HashSet<uint>();
        void Count(PixelImage image, HashSet<uint> set)
        {
            foreach (var c in image.Pixels)
                if (c.A != 0) set.Add(c.Argb);
        }
        if (cel)
        {
            if (_ctx.EditImage is { } img && current is not null)
            {
                Count(img, before);
                Count(CelMap(current, img), afterSet);
            }
        }
        else
            foreach (var (layer, f, image) in _project.StoredCels)
            {
                if (!target(layer, f)) continue;
                Count(image, before);
                Count(Mapped(image, dither) ?? image, afterSet);
            }
        _count.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PaletteConvertDialog_UpdatePreview_716EED2D13,before.Count,afterSet.Count);
    }

    private static RadioButton Radio(string group, string text)
        => new() { GroupName = group, Content = text, Foreground = AppTheme.TextBrush };

    /// <summary>체커 위에 이미지를 정수 배율로 맞춰 보이는 미리보기 칸(제목 포함).</summary>
    private sealed class ImageView : Control
    {
        private readonly string _caption;
        private WriteableBitmap? _bitmap;

        public ImageView(string caption)
        {
            _caption = caption;
            Width = 240;
            Height = 240;
            Margin = new Thickness(2, 6, 6, 0);
        }

        public void SetImage(PixelImage image)
        {
            _bitmap?.Dispose();
            _bitmap = ImageUtil.ToBitmap(image);
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            var bounds = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.BackBrush, bounds);
            AppTheme.DrawTextIn(g, _caption, new Rect(4, 0, bounds.Width - 8, 18), AppTheme.DimTextBrush, AppTheme.SmallFontSize);
            if (_bitmap is { } bmp)
            {
                var size = bmp.PixelSize;
                var dest = ImageUtil.DrawFitted(g, bmp, new Rectangle(0, 0, size.Width, size.Height), new Rect(6, 20, bounds.Width - 12, bounds.Height - 26), checker: true);
                g.DrawRectangle(null, AppTheme.Pen(AppTheme.BorderColor), dest.Inflate(0.5));
            }
        }
    }
}
