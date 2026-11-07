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
/// 팔레트 색으로 변환: 대상(현재 셀 / 현재 레이어 모든 프레임 / 모든 레이어·프레임), 색 고르는 규칙(<see cref="PaletteMatchRule"/> — 가장 가까운 색이면 색 거리도),
/// 디더링(없음 / Bayer 4×4 — 규칙이 고른 두 색을 섞음)을 고르고, 현재 프레임의 전·후 미리보기와 대상 셀의 색 수(전 → 후)를 보인다.
/// 잠긴 레이어는 건너뛰고, 실행취소 한 단계다. 색 고르기는 <see cref="PaletteRemap"/>(알파 유지, 완전 투명은 그대로) — 순서 규칙은 대상 셀 전체의 색을 함께 줄 세운다.
/// 규칙과 색 거리는 앱 설정(<see cref="AppSettings.PaletteConvertRule"/>·<see cref="AppSettings.PaletteConvertDistance"/>)에 기억한다.
/// </summary>
public sealed class PaletteConvertDialog : DialogBase
{
    private static readonly string Label = UiStrings.PaletteConvert_Title;
    private static readonly PaletteMatchRule[] Rules =
        [PaletteMatchRule.Nearest, PaletteMatchRule.BrightnessOrder, PaletteMatchRule.PaletteOrder, PaletteMatchRule.HueFirst, PaletteMatchRule.LightnessOnly];
    private static readonly PaletteMatchDistance[] Distances = [PaletteMatchDistance.Rgb, PaletteMatchDistance.OkLab, PaletteMatchDistance.Ciede2000];

    private readonly EditorContext _ctx;
    private readonly SpriteProject _project;
    private readonly IReadOnlyList<Color32> _palette;
    private readonly RadioButton _cel, _layer, _all, _noDither, _bayer;
    /// <summary>규칙 단추(<see cref="Rules"/>와 같은 차례).</summary>
    private readonly RadioButton[] _rules;
    private readonly ComboBox _distance;
    private readonly ImageView _before, _after;
    private readonly TextBlock _count;
    /// <summary>지금 고른 대상·규칙·색 거리의 색 고르기(고른 것이 바뀌면 새로 만듦).</summary>
    private PaletteRemap _remap;
    /// <summary>지금 고른 것(규칙·대상·디더링)으로 셀 → 바꾼 사본(바뀐 것이 없으면 null). 미리보기·색 수 계산과 실제 변환이 같이 쓰고, 고른 것이 바뀌면 비운다.</summary>
    private readonly Dictionary<PixelImage, PixelImage?> _mapped = new(ReferenceEqualityComparer.Instance);

    private PaletteConvertDialog(EditorContext ctx, SpriteProject project, IReadOnlyList<Color32> palette, int paletteCount, string paletteName)
        : base($"{Label} — {paletteName}", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_52F9376144)
    {
        _ctx = ctx;
        _project = project;
        _palette = palette;
        _cel = Radio("scope", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_4BF144BF9A);
        _layer = Radio("scope", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_982BB7EF41);
        _all = Radio("scope", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_28DF63893B);
        _rules =
        [
            Radio("rule", UiStrings.PaletteConvert_Rule_Nearest),
            Radio("rule", UiStrings.PaletteConvert_Rule_BrightnessOrder),
            Radio("rule", UiStrings.PaletteConvert_Rule_PaletteOrder),
            Radio("rule", UiStrings.PaletteConvert_Rule_HueFirst),
            Radio("rule", UiStrings.PaletteConvert_Rule_LightnessOnly),
        ];
        _distance = Ui.Combo(150, UiStrings.PaletteConvert_Distance_Rgb, UiStrings.Pixelate_Pal_Distance_OkLab, UiStrings.Pixelate_Pal_Distance_Ciede2000);
        _noDither = Radio("dither", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_541E54A55D);
        _bayer = Radio("dither", UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_BDEC5A51EB);
        _cel.IsEnabled = ctx.CanEdit;
        _layer.IsEnabled = ctx.CurrentLayerObject is { IsGroup: false } && !project.IsLayerLocked(ctx.CurrentLayer);
        (_cel.IsEnabled ? _cel : _layer.IsEnabled ? _layer : _all).IsChecked = true;
        _rules[Math.Max(0, Array.IndexOf(Rules, App.Settings.PaletteConvertRule))].IsChecked = true;
        _distance.SelectedIndex = Math.Max(0, Array.IndexOf(Distances, App.Settings.PaletteConvertDistance));
        _noDither.IsChecked = true;

        _before = new ImageView(UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_8D277CDF45);
        _after = new ImageView(UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_487B63AF2E);
        _count = new TextBlock { Foreground = AppTheme.TextBrush, Margin = new Thickness(2, 4, 2, 0) };

        Body.Children.Add(Ui.Label(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_8CE25638B7,paletteName,paletteCount)));
        Body.Children.Add(Ui.Label(UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_2AE3FF9667, bold: true));
        Body.Children.Add(_cel);
        Body.Children.Add(_layer);
        Body.Children.Add(_all);
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.PaletteConvert_Rule, bold: true), Ui.InfoBadge(UiStrings.PaletteConvert_Rule_Tip)));
        Body.Children.Add(_rules[0]);
        var distanceRow = Ui.Row(Ui.Label(UiStrings.Pixelate_Pal_Distance), _distance);
        distanceRow.Margin = new Thickness(24, 0, 0, 2);
        Body.Children.Add(distanceRow);
        for (int i = 1; i < _rules.Length; i++) Body.Children.Add(_rules[i]);
        Body.Children.Add(Ui.Label(UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_3E84BC3FF1, bold: true));
        Body.Children.Add(_noDither);
        Body.Children.Add(_bayer);
        Body.Children.Add(Ui.Row(_before, _after));
        Body.Children.Add(_count);
        Body.Children.Add(Ui.Label(UiStrings.S_PaletteConvertDialog_PaletteConvertDialog_0988821852));

        _remap = BuildRemap();
        foreach (var r in new[] { _cel, _layer, _all, _noDither, _bayer }.Concat(_rules))
            // 같은 묶음의 다른 단추가 아직 꺼지기 전에 불리므로, 한 묶음의 바뀜이 다 끝난 뒤에 미리보기를 고친다.
            r.IsCheckedChanged += (_, _) => { if (r.On()) Dispatcher.UIThread.Post(Refresh); };
        _distance.SelectionChanged += (_, _) => Refresh();
        _distance.IsEnabled = Rule == PaletteMatchRule.Nearest;
        UpdatePreview();
    }

    public PaletteConvertScope Scope => _cel.On() ? PaletteConvertScope.CurrentCel : _layer.On() ? PaletteConvertScope.CurrentLayer : PaletteConvertScope.All;
    public PaletteDither Dither => _bayer.On() ? PaletteDither.Bayer4 : PaletteDither.None;
    public PaletteMatchRule Rule => Rules[Math.Max(0, Array.FindIndex(_rules, r => r.On()))];
    public PaletteMatchDistance Distance => Distances[Math.Max(0, _distance.SelectedIndex)];

    /// <summary>
    /// 팔레트 색으로 변환 대화상자를 띄우고 고른 대로 바꾼다. 상태 표시줄에 보일 문구를 돌려준다(취소했거나 프로젝트가 없으면 null).
    /// 팔레트에 투명이 아닌 색이 없으면 알리고 null.
    /// </summary>
    public static string? Run(EditorContext ctx, Visual? owner, IReadOnlyList<Color32> palette, string paletteName)
    {
        if (ctx.Project is not { } p) return null;
        int count;
        try
        {
            count = new PaletteRemap(palette).Count;
        }
        catch (ArgumentException)
        {
            Ui.Info(owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PaletteConvertDialog_Run_6433CC1607,paletteName), Label);
            return null;
        }
        ctx.CommitFloating();
        var dlg = new PaletteConvertDialog(ctx, p, palette, count, paletteName);
        if (dlg.ShowDialog(owner) != DialogResult.OK || ctx.Project != p) return null;
        App.Settings.PaletteConvertRule = dlg.Rule;
        App.Settings.PaletteConvertDistance = dlg.Distance;
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
        _ctx.Structural(() => cels = _project.MapCels(Label, target, Mapped));
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

    /// <summary>
    /// 고른 규칙·색 거리의 색 고르기. 순서 규칙(<see cref="PaletteRemap.RanksSource"/>)은 바꿀 픽셀의 색을 모두 함께 줄 세운다 —
    /// 현재 셀이면 편집 중인 그림(선택 영역이 있으면 그 안), 아니면 대상 셀 전부라 프레임마다 같은 색이 같은 팔레트 색이 된다.
    /// </summary>
    private PaletteRemap BuildRemap()
    {
        var rule = Rule;
        if (!PaletteRemap.RanksSource(rule)) return new PaletteRemap(_palette, rule, Distance);
        var source = new HashSet<Color32>();
        if (Scope == PaletteConvertScope.CurrentCel)
        {
            if (_ctx.EditImage is { } img) PaletteRemap.AddOpaqueColors(img, source, SelectionClip);
        }
        else
        {
            var target = Target();
            foreach (var (layer, f, image) in _project.StoredCels)
                if (target(layer, f)) PaletteRemap.AddOpaqueColors(image, source);
        }
        return new PaletteRemap(_palette, rule, Distance, source);
    }

    /// <summary>대상·규칙·색 거리·디더링이 바뀌면 색 고르기를 새로 만들고 미리보기를 고친다.</summary>
    private void Refresh()
    {
        _distance.IsEnabled = Rule == PaletteMatchRule.Nearest;
        _remap = BuildRemap();
        _mapped.Clear();
        UpdatePreview();
    }

    /// <summary>셀을 바꾼 사본(바뀐 것이 없으면 null, 기억해 둠).</summary>
    private PixelImage? Mapped(PixelImage cel)
    {
        if (!_mapped.TryGetValue(cel, out var result)) _mapped[cel] = result = _remap.Remapped(cel, Dither);
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
            if (!cel || clip is null) return Mapped(image) ?? image;
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
                Count(Mapped(image) ?? image, afterSet);
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
