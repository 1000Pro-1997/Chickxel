using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 내보낼 스킨 고르기(내보내기 설정 창의 "스킨 고르기…"): 시트 설정 한 벌(공통 또는 부위 아틀라스 따로)의
/// 스킨 체크(끄면 <see cref="SheetOptions.ExcludedSkins"/>), 착장 프리셋(<see cref="SheetOptions.Outfits"/>, 프레임 시트만),
/// 넣을 최상위 레이어(끄면 <see cref="SheetOptions.ExcludedLayers"/>), 물려받은 칸 굽기(<see cref="SheetOptions.BakeInheritedSkins"/>).
/// 창 안에서는 고르기만 하고, 확인을 누르면 호출한 쪽이 <see cref="ApplyTo"/>로 한 번에 반영한다(실행취소 1단계).
/// </summary>
public sealed class SkinPickerWindow : DialogBase
{
    private const int ThumbSize = 32;

    private readonly List<(Skin Skin, CheckBox Box)> _skins = new();
    private readonly List<(Outfit Outfit, CheckBox Box)> _outfits = new();
    private readonly List<(Layer Layer, CheckBox Box)> _layers = new();
    private readonly CheckBox? _bake;
    private readonly List<WriteableBitmap> _bitmaps = new();

    /// <param name="project">프로젝트(그룹·스킨·착장·레이어 목록과 썸네일).</param>
    /// <param name="options">처음 체크 상태를 읽을 시트 설정.</param>
    /// <param name="partsOnly">부위 아틀라스만 쓰는 설정(착장 프리셋 칸을 숨김).</param>
    /// <param name="showBake">물려받은 칸 굽기를 보일지(부위 아틀라스가 이 설정을 쓰고 스킨이 둘 이상인 그룹이 있을 때).</param>
    /// <param name="frame">썸네일로 그릴 프레임 번호.</param>
    public SkinPickerWindow(SpriteProject project, SheetOptions options, bool partsOnly, bool showBake, int frame)
        : base(partsOnly ? UiStrings.S_SkinPickerWindow_SkinPickerWindow_CDBDEDBE4D : UiStrings.S_SkinPickerWindow_SkinPickerWindow_30B8F1EF08)
    {
        frame = Math.Clamp(frame, 0, Math.Max(0, project.Frames.Count - 1));
        var content = new StackPanel { Spacing = 2, Width = 460 };

        var intro = Note(partsOnly
            ? UiStrings.S_SkinPickerWindow_SkinPickerWindow_5049C75A28
            : UiStrings.S_SkinPickerWindow_SkinPickerWindow_09AC71786E);
        content.Children.Add(intro);
        if (options.Skins == SkinSource.Current)
        {
            var current = Note(UiStrings.S_SkinPickerWindow_SkinPickerWindow_A307A08B56);
            current.Foreground = AppTheme.WarningBrush;
            content.Children.Add(current);
        }

        var groups = project.Layers.Where(l => l.IsGroup && project.ChildCount(l) > 0).Reverse().ToList();
        bool anyChoice = groups.Count > 0 || (!partsOnly && project.Outfits.Count > 0);
        if (anyChoice)
        {
            var all = Ui.Button(UiStrings.S_SkinPickerWindow_SkinPickerWindow_EB479F1895, (_, _) => SetAll(true));
            var none = Ui.Button(UiStrings.S_SkinPickerWindow_SkinPickerWindow_19FA73CDFF, (_, _) => SetAll(false));
            all.Tip(UiStrings.S_SkinPickerWindow_SkinPickerWindow_6D5AE7FB88);
            none.Tip(UiStrings.S_SkinPickerWindow_SkinPickerWindow_FF0C503AFE);
            content.Children.Add(Ui.Row(all, none));
        }

        if (groups.Count > 0)
        {
            content.Children.Add(Heading(UiStrings.S_SkinPickerWindow_SkinPickerWindow_FFE48329B5));
            foreach (var group in groups)
            {
                var name = Ui.Label(options.ExcludedLayers.Contains(group) ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_SkinPickerWindow_SkinPickerWindow_E3EF38CCC4,group.Name) : group.Name, bold: true);
                name.TextWrapping = TextWrapping.Wrap;
                content.Children.Add(name);
                var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 2) };
                var skins = group.Skins;
                for (int i = 0; i < skins.Count; i++)
                {
                    var skin = skins[i];
                    var box = SkinBox(ImageUtil.MakeThumbnail(project.FlattenGroup(group, frame, skin), ThumbSize), i == 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_SkinPickerWindow_SkinPickerWindow_20A6248C03,skin.Name) : skin.Name,
                        options, skin, partOnly: false);
                    box.Tip(i == 0
                        ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_SkinPickerWindow_SkinPickerWindow_11E58C3A27,group.Name)
                        : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_SkinPickerWindow_SkinPickerWindow_91FAFEFE55,group.Name,skin.Name));
                    wrap.Children.Add(box);
                }
                content.Children.Add(wrap);
                // 부위 전용(개별) 스킨: 그 부위 이름 아래 들여 써서(부위는 타임라인처럼 위→아래)
                foreach (var part in project.ChildrenOf(group).Reverse())
                {
                    var own = part.OwnSkins;
                    if (own.Count == 0) continue;
                    var partName = Ui.Label(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_SkinPickerWindow_SkinPickerWindow_4B4DDD65E9,part.Name));
                    partName.Foreground = AppTheme.DimTextBrush;
                    partName.Margin = new Thickness(20, 0, 0, 0);
                    content.Children.Add(partName);
                    var partWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(28, 0, 0, 2) };
                    var f = project.Frames[frame];
                    foreach (var skin in own)
                    {
                        var cel = project.GetSkinCel(part, f, skin);
                        var thumb = ImageUtil.MakeThumbnail(cel ?? new PixelImage(Math.Max(1, part.PartSize.Width), Math.Max(1, part.PartSize.Height)), ThumbSize);
                        var box = SkinBox(thumb, skin.Name, options, skin, partOnly: true);
                        box.Tip(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_SkinPickerWindow_SkinPickerWindow_6068D0001B,part.Name,skin.Name,part.Name,skin.Name,skin.Name));
                        partWrap.Children.Add(box);
                    }
                    content.Children.Add(partWrap);
                }
            }
        }

        if (!partsOnly && project.Outfits.Count > 0)
        {
            content.Children.Add(Heading(UiStrings.S_SkinPickerWindow_SkinPickerWindow_E73FD24EBA));
            content.Children.Add(Note(UiStrings.S_SkinPickerWindow_SkinPickerWindow_F5BB7790A1));
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 2) };
            foreach (var outfit in project.Outfits)
            {
                var box = Ui.Check(outfit.Name, options.Outfits.Contains(outfit));
                box.Margin = new Thickness(0, 1, 10, 1);
                _outfits.Add((outfit, box));
                wrap.Children.Add(box);
            }
            content.Children.Add(wrap);
        }
        else if (!partsOnly && groups.Count > 0)
            content.Children.Add(Note(UiStrings.S_SkinPickerWindow_SkinPickerWindow_FC0997D1E7));

        var tops = project.Layers.Where(l => !l.IsPart).Reverse().ToList();
        if (tops.Count > 0)
        {
            content.Children.Add(Heading(UiStrings.S_SkinPickerWindow_SkinPickerWindow_76ACD5089E));
            content.Children.Add(Note(partsOnly
                ? UiStrings.S_SkinPickerWindow_SkinPickerWindow_CB68D989C3
                : UiStrings.S_SkinPickerWindow_SkinPickerWindow_8D6FC7397A));
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 2) };
            foreach (var layer in tops)
            {
                var text = (layer.IsGroup ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_SkinPickerWindow_SkinPickerWindow_8923EA543C,layer.Name) : layer.Name) + (layer.Visible ? "" : UiStrings.S_SkinPickerWindow_SkinPickerWindow_5EBB32EC59);
                var box = Ui.Check(text, !options.ExcludedLayers.Contains(layer));
                box.Margin = new Thickness(0, 1, 10, 1);
                if (!layer.Visible) box.Tip(UiStrings.S_SkinPickerWindow_SkinPickerWindow_A3D6B146CF);
                _layers.Add((layer, box));
                wrap.Children.Add(box);
            }
            content.Children.Add(wrap);
        }

        if (showBake)
        {
            content.Children.Add(Heading(UiStrings.S_SkinPickerWindow_SkinPickerWindow_275C0DD513));
            _bake = Ui.Check(UiStrings.S_SkinPickerWindow_SkinPickerWindow_D6CC3CF8C8, options.BakeInheritedSkins);
            _bake.Tip(UiStrings.S_SkinPickerWindow_SkinPickerWindow_F0212A544D +
                      UiStrings.S_SkinPickerWindow_SkinPickerWindow_7865C277F0 +
                      UiStrings.S_SkinPickerWindow_SkinPickerWindow_945A308427);
            content.Children.Add(_bake);
        }

        if (!anyChoice && tops.Count == 0 && !showBake)
            content.Children.Add(Note(UiStrings.S_SkinPickerWindow_SkinPickerWindow_3CDC91DE0A));

        Body.Children.Add(new ScrollViewer
        {
            Content = content,
            MaxHeight = 520,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        Closed += (_, _) =>
        {
            foreach (var b in _bitmaps) b.Dispose();
            _bitmaps.Clear();
        };
    }

    /// <summary>썸네일 + 이름(+ 부위 전용이면 "개별" 표시) 체크 상자 하나(<see cref="_skins"/>에 더함).</summary>
    private CheckBox SkinBox(WriteableBitmap thumb, string title, SheetOptions options, Skin skin, bool partOnly)
    {
        _bitmaps.Add(thumb);
        var image = new Image { Source = thumb, Width = ThumbSize, Height = ThumbSize };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
        var text = new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 140,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { image, text } };
        if (partOnly) row.Children.Add(Badge());
        var box = new CheckBox
        {
            Content = row,
            IsChecked = !options.ExcludedSkins.Contains(skin),
            Margin = new Thickness(0, 1, 10, 1),
        };
        _skins.Add((skin, box));
        return box;
    }

    /// <summary>부위 전용(개별) 스킨 표시("개별" 작은 띠).</summary>
    public static Border Badge() => new()
    {
        Background = AppTheme.AccentBrush,
        CornerRadius = new CornerRadius(2),
        Padding = new Thickness(3, 0),
        Margin = new Thickness(4, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = UiStrings.S_SkinPickerWindow_Badge_42A5136FFC, FontSize = AppTheme.SmallFontSize - 1, Foreground = Brushes.White },
    };

    /// <summary>체크한 스킨 수(부위 전용 스킨 포함) + 체크한 착장 프리셋 수(<paramref name="countOutfits"/>면) — "스킨 고르기… (N개)"의 N.</summary>
    public static int SelectedCount(SpriteProject project, SheetOptions options, bool countOutfits)
    {
        int n = project.Layers.Where(l => l.IsGroup && project.ChildCount(l) > 0).Sum(g => g.AllSkins.Count(s => !options.ExcludedSkins.Contains(s)));
        if (countOutfits) n += project.Outfits.Count(options.Outfits.Contains);
        return n;
    }

    /// <summary>고른 것이 <paramref name="options"/>와 다른지.</summary>
    public bool Differs(SheetOptions options)
        => !SameSet(Excluded(options), options.ExcludedSkins) || !SameSet(Outfits(options), options.Outfits)
           || !SameSet(ExcludedLayers(options), options.ExcludedLayers) || (_bake is not null && _bake.On() != options.BakeInheritedSkins);

    /// <summary>고른 것을 <paramref name="options"/>에 넣는다(이 창에 없던 항목 — 지운 그룹의 스킨 등 — 은 그대로 둔다).</summary>
    public void ApplyTo(SheetOptions options)
    {
        options.ExcludedSkins = Excluded(options);
        options.Outfits = Outfits(options);
        options.ExcludedLayers = ExcludedLayers(options);
        if (_bake is not null) options.BakeInheritedSkins = _bake.On();
    }

    private IReadOnlyList<Skin> Excluded(SheetOptions o)
        => [.. o.ExcludedSkins.Where(s => !_skins.Any(x => ReferenceEquals(x.Skin, s))), .. _skins.Where(x => !x.Box.On()).Select(x => x.Skin)];

    private IReadOnlyList<Outfit> Outfits(SheetOptions o)
        => [.. o.Outfits.Where(s => !_outfits.Any(x => ReferenceEquals(x.Outfit, s))), .. _outfits.Where(x => x.Box.On()).Select(x => x.Outfit)];

    private IReadOnlyList<Layer> ExcludedLayers(SheetOptions o)
        => [.. o.ExcludedLayers.Where(s => !_layers.Any(x => ReferenceEquals(x.Layer, s))), .. _layers.Where(x => !x.Box.On()).Select(x => x.Layer)];

    private static bool SameSet<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) where T : class
        => a.Count == b.Count && a.All(x => b.Contains(x));

    private void SetAll(bool on)
    {
        foreach (var (_, box) in _skins) box.IsChecked = on;
        foreach (var (_, box) in _outfits) box.IsChecked = on;
    }

    private static TextBlock Heading(string text)
    {
        var t = Ui.Label(text, bold: true);
        t.Margin = new Thickness(3, 10, 3, 2);
        t.FontSize = 13;
        return t;
    }

    private static TextBlock Note(string text)
    {
        var t = Ui.Label(text);
        t.Foreground = AppTheme.DimTextBrush;
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }
}
