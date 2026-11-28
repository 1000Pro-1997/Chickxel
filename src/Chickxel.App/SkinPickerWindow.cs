using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 내보낼 스킨 고르기(내보내기 창 시트 설정의 "스킨 고르기…"): 시트 설정 한 벌(공통 또는 레이어 아틀라스 따로)의
/// 넣을 레이어(끄면 자식째 <see cref="SheetOptions.ExcludedLayers"/>)와 그 레이어의 스킨 체크(끄면 <see cref="SheetOptions.ExcludedSkins"/>)를
/// 레이어 나무 그대로(위→아래, 깊이만큼 들여 써서), 그리고 스킨 프리셋(<see cref="SheetOptions.Presets"/>, 프레임 시트만)을 고른다.
/// 프레임 시트 쪽에는 레이어의 첫 스킨을 항목으로 내놓지 않는다(첫 스킨 = 지금 스킨 합성에 이미 있음). 레이어 아틀라스만 쓰는 설정은 첫 스킨도 고른다.
/// 창 안에서는 고르기만 하고, 확인을 누르면 호출한 쪽이 <see cref="ApplyTo"/>로 한 번에 반영한다(실행취소 1단계).
/// </summary>
public sealed class SkinPickerWindow : DialogBase
{
    private const int ThumbSize = 32, IndentStep = 16;

    private readonly List<(Skin Skin, CheckBox Box)> _skins = new();
    private readonly List<(SkinPreset Preset, CheckBox Box)> _presets = new();
    private readonly List<(Layer Layer, CheckBox Box)> _layers = new();
    private readonly List<WriteableBitmap> _bitmaps = new();

    /// <param name="project">프로젝트(레이어 나무·스킨·프리셋 목록과 썸네일).</param>
    /// <param name="options">처음 체크 상태를 읽을 시트 설정.</param>
    /// <param name="partsOnly">레이어 아틀라스만 쓰는 설정(자리 있는 레이어만, 첫 스킨도 고름, 스킨 프리셋 칸을 숨김).</param>
    /// <param name="frame">썸네일로 그릴 프레임 번호.</param>
    public SkinPickerWindow(SpriteProject project, SheetOptions options, bool partsOnly, int frame)
        : base(partsOnly ? UiStrings.S_SkinPickerWindow_SkinPickerWindow_CDBDEDBE4D : UiStrings.S_SkinPickerWindow_SkinPickerWindow_30B8F1EF08)
    {
        frame = Math.Clamp(frame, 0, Math.Max(0, project.Frames.Count - 1));
        // 대상 도움말(F1): 창 = "스킨 내보내기"의 스킨 고르기 창 그림, 스킨 칸 ⑥·스킨 프리셋 칸 ⑦(같은 문단),
        // 레이어 칸 = 내보내기 창 공통 설정의 스킨 고르기… 항목(합칠 레이어를 고른다는 설명)
        const string page = "parts-skin-export";
        this.HelpAt(page, shot: "parts-skin-export-2");
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

        // 레이어 아틀라스만 쓰는 설정이면 자리 있는 레이어만(맨 위 일반 레이어·타임라인은 아틀라스에 들지 않음).
        var layers = Enumerable.Range(0, project.Layers.Count).Reverse().Select(i => project.Layers[i]).Where(l => !partsOnly || l.IsPart).ToList();
        bool anySkin = layers.Any(l => SkinChoices(l, partsOnly).Count > 0);
        bool anyChoice = anySkin || (!partsOnly && project.SkinPresets.Count > 0);
        if (anyChoice)
        {
            var all = Ui.Button(UiStrings.S_SkinPickerWindow_SkinPickerWindow_EB479F1895, (_, _) => SetAll(true));
            var none = Ui.Button(UiStrings.S_SkinPickerWindow_SkinPickerWindow_19FA73CDFF, (_, _) => SetAll(false));
            all.Tip(UiStrings.S_SkinPickerWindow_SkinPickerWindow_6D5AE7FB88);
            none.Tip(UiStrings.S_SkinPickerWindow_SkinPickerWindow_FF0C503AFE);
            content.Children.Add(Ui.Row(all, none));
        }

        if (layers.Count > 0)
        {
            content.Children.Add(Heading(UiStrings.S_SkinPickerWindow_SkinPickerWindow_76ACD5089E).HelpAt(page, anchor: "⑥"));
            content.Children.Add(Note(partsOnly
                ? UiStrings.S_SkinPickerWindow_SkinPickerWindow_CB68D989C3
                : UiStrings.S_SkinPickerWindow_SkinPickerWindow_8D6FC7397A).HelpAt("screen-export-sheet", anchor: "Choose skins…"));
            foreach (var layer in layers)
            {
                double indent = 8 + project.DepthOf(layer) * IndentStep;
                bool visible = project.IsLayerVisible(layer);
                var box = Ui.Check(layer.Name + (visible ? "" : UiStrings.S_SkinPickerWindow_SkinPickerWindow_5EBB32EC59), !options.ExcludedLayers.Contains(layer));
                box.FontWeight = FontWeight.Bold;
                box.Margin = new Thickness(indent, 4, 0, 0);
                if (!visible) box.Tip(UiStrings.S_SkinPickerWindow_SkinPickerWindow_A3D6B146CF);
                _layers.Add((layer, box));
                content.Children.Add(box.HelpAt("screen-export-sheet", anchor: "Choose skins…"));

                var choices = SkinChoices(layer, partsOnly);
                if (choices.Count == 0) continue;
                var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(indent + 20, 0, 0, 2) }.HelpAt(page, anchor: "⑥");
                int index = project.IndexOf(layer);
                foreach (var skin in choices)
                {
                    bool first = ReferenceEquals(skin, layer.BaseSkin);
                    // 타임라인 칸·스킨 설정 카드와 같은 썸네일(효과 없이, 그 레이어 자리로 자름)
                    var picture = project.RenderCelThumbnail(index, frame, skin) ?? new PixelImage(1, 1);
                    var skinBox = SkinBox(ImageUtil.MakeThumbnail(picture, ThumbSize),
                        first ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_SkinPickerWindow_SkinPickerWindow_20A6248C03, skin.Name) : skin.Name, options, skin);
                    skinBox.Tip(first
                        ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_SkinPickerWindow_SkinPickerWindow_11E58C3A27, layer.Name)
                        : string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_SkinPickerWindow_SkinPickerWindow_91FAFEFE55, layer.Name, skin.Name));
                    wrap.Children.Add(skinBox);
                }
                content.Children.Add(wrap);
            }
        }

        if (!partsOnly && project.SkinPresets.Count > 0)
        {
            content.Children.Add(Heading(UiStrings.S_SkinPickerWindow_SkinPickerWindow_E73FD24EBA).HelpAt(page, anchor: "⑦"));
            content.Children.Add(Note(UiStrings.S_SkinPickerWindow_SkinPickerWindow_F5BB7790A1).HelpAt(page, anchor: "⑦"));
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 2) }.HelpAt(page, anchor: "⑦");
            foreach (var preset in project.SkinPresets)
            {
                var box = Ui.Check(preset.Name, options.Presets.Contains(preset));
                box.Margin = new Thickness(0, 1, 10, 1);
                _presets.Add((preset, box));
                wrap.Children.Add(box);
            }
            content.Children.Add(wrap);
        }
        else if (!partsOnly && anySkin)
            content.Children.Add(Note(UiStrings.S_SkinPickerWindow_SkinPickerWindow_FC0997D1E7));

        if (layers.Count == 0)
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

    /// <summary>
    /// 레이어에서 고를 수 있는 스킨: 스킨이 둘 이상일 때만 — 레이어 아틀라스만 쓰는 설정이면 모든 스킨, 아니면 첫 스킨이 아닌 스킨(프레임 시트 항목).
    /// </summary>
    private static IReadOnlyList<Skin> SkinChoices(Layer layer, bool partsOnly)
    {
        var skins = layer.Skins;
        if (skins.Count < 2) return [];
        return partsOnly ? skins : skins.Skip(1).ToList();
    }

    /// <summary>썸네일 + 이름 체크 상자 하나(<see cref="_skins"/>에 더함).</summary>
    private CheckBox SkinBox(WriteableBitmap thumb, string title, SheetOptions options, Skin skin)
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
        var box = new CheckBox
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { image, text } },
            IsChecked = !options.ExcludedSkins.Contains(skin),
            Margin = new Thickness(0, 1, 10, 1),
        };
        _skins.Add((skin, box));
        return box;
    }

    /// <summary>
    /// 체크한 스킨 수 + (<paramref name="partsOnly"/>가 아니면) 체크한 스킨 프리셋 수 — "스킨 고르기… (N개)"의 N.
    /// 스킨은 창과 같은 기준(레이어 아틀라스만이면 자리 있는 레이어의 모든 스킨, 아니면 첫 스킨이 아닌 스킨).
    /// </summary>
    public static int SelectedCount(SpriteProject project, SheetOptions options, bool partsOnly)
    {
        int n = project.Layers.Where(l => !partsOnly || l.IsPart).Sum(l => SkinChoices(l, partsOnly).Count(s => !options.ExcludedSkins.Contains(s)));
        if (!partsOnly) n += project.SkinPresets.Count(options.Presets.Contains);
        return n;
    }

    /// <summary>고른 것이 <paramref name="options"/>와 다른지.</summary>
    public bool Differs(SheetOptions options)
        => !SameSet(Excluded(options), options.ExcludedSkins) || !SameSet(Presets(options), options.Presets)
           || !SameSet(ExcludedLayers(options), options.ExcludedLayers);

    /// <summary>고른 것을 <paramref name="options"/>에 넣는다(이 창에 없던 항목 — 지운 레이어의 스킨 등 — 은 그대로 둔다).</summary>
    public void ApplyTo(SheetOptions options)
    {
        options.ExcludedSkins = Excluded(options);
        options.Presets = Presets(options);
        options.ExcludedLayers = ExcludedLayers(options);
    }

    private IReadOnlyList<Skin> Excluded(SheetOptions o)
        => [.. o.ExcludedSkins.Where(s => !_skins.Any(x => ReferenceEquals(x.Skin, s))), .. _skins.Where(x => !x.Box.On()).Select(x => x.Skin)];

    private IReadOnlyList<SkinPreset> Presets(SheetOptions o)
        => [.. o.Presets.Where(s => !_presets.Any(x => ReferenceEquals(x.Preset, s))), .. _presets.Where(x => x.Box.On()).Select(x => x.Preset)];

    private IReadOnlyList<Layer> ExcludedLayers(SheetOptions o)
        => [.. o.ExcludedLayers.Where(s => !_layers.Any(x => ReferenceEquals(x.Layer, s))), .. _layers.Where(x => !x.Box.On()).Select(x => x.Layer)];

    private static bool SameSet<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) where T : class
        => a.Count == b.Count && a.All(x => b.Contains(x));

    private void SetAll(bool on)
    {
        foreach (var (_, box) in _skins) box.IsChecked = on;
        foreach (var (_, box) in _presets) box.IsChecked = on;
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
