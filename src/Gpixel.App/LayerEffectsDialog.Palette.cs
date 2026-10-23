using Gpixel.Core.Edit;
using Gpixel.Core.Model;
using Gpixel.App.Localization;
using Gpixel.Core.IO;

namespace Gpixel.App;

/// <summary>
/// 효과 창 왼쪽 끝의 효과 종류 칸: 종류마다 미리보기 그림(지금 프레임 그림에 기본 설정으로 입힌 모습 — 움직이는 효과는 돌려 보임) + 이름 상자.
/// 두 번 누르거나 효과 목록으로 끌어 놓으면 "추가 범위"로 추가한다(끌어 놓으면 놓은 자리에).
/// </summary>
public sealed partial class LayerEffectsDialog
{
    private const int TileWidth = 88, TileHeight = 96, ThumbSteps = 12, ThumbHold = 4, ThumbMaxSize = 128;

    private readonly List<EffectTile> _tiles = new();
    private readonly List<EffectPreset> _presets = new();
    private readonly WrapPanel _customWrap = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _customEmpty = Ui.Label("");
    private readonly DispatcherTimer _thumbTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private PixelImage _thumbSource = SampleSprite();
    private int _thumbTick;
    private EffectTile? _pressedTile;
    private Avalonia.Point _pressPoint;
    private bool _tileDragging;
    private Button? _savePreset;

    private static string PresetPath => Path.Combine(Platform.AppDataDirectory, "Gpixel", EffectPresets.FileName);

    private Control BuildPalette(string? motionTip)
    {
        _thumbSource = ThumbSource();
        var builtInWrap = new WrapPanel { Orientation = Orientation.Horizontal };
        for (int kind = 0; kind < KindTable.Length; kind++)
        {
            var effects = new LayerEffect[] { Create(kind) };
            var tile = new EffectTile(KindTable[kind].Name, effects);
            ToolTip.SetTip(tile, $"{Kinds[kind]}\n{DescriptionOf(kind)}" + (tile.Animated ? UiStrings.S_LayerEffectsDialog_Palette_BuildPalette_164E2EA098 : "")
                                 + (tile.Animated && motionTip is not null ? " " + motionTip : ""));
            AddPaletteTile(tile, builtInWrap);
        }

        string? loadError = null;
        try
        {
            _presets.AddRange(EffectPresets.Load(PresetPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            loadError = string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.EffectPreset_LoadError, ex.Message);
        }

        _savePreset = Ui.Button(VectorIcon.Layers, UiStrings.EffectPreset_SaveGroup, (_, _) => SavePresetGroup());
        _savePreset.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(_savePreset, UiStrings.EffectPreset_SaveGroupTip);
        _customEmpty.Text = loadError ?? UiStrings.EffectPreset_Empty;
        _customEmpty.TextWrapping = TextWrapping.Wrap;
        _customEmpty.MaxWidth = (TileWidth + 4) * 2;
        RebuildCustomPalette();

        _thumbTimer.Tick += (_, _) =>
        {
            // 움직이는 효과: 0 → 끝까지 돌리고 끝 모습을 잠깐 보여 준 뒤 처음부터.
            _thumbTick = (_thumbTick + 1) % (ThumbSteps + ThumbHold);
            int step = Math.Min(_thumbTick, ThumbSteps - 1);
            foreach (var tile in _tiles)
                if (tile.Animated) tile.Show(step, RenderThumb);
        };
        _thumbTimer.Start();

        int width = (TileWidth + 4) * 2 + 18;
        ScrollViewer Scroll(Control content, double height) => new()
        {
            Content = content,
            Width = width,
            Height = height,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var custom = Ui.Stack(_savePreset, Ui.Label(UiStrings.EffectPreset_CustomHelp), _customEmpty, Scroll(_customWrap, 470));
        custom.Margin = new Thickness(0);
        var tabs = new TabControl { Width = width, Height = 590 };
        tabs.Items.Add(new TabItem { Header = UiStrings.EffectPreset_BuiltInTab, Content = Scroll(builtInWrap, 540) });
        tabs.Items.Add(new TabItem { Header = UiStrings.EffectPreset_CustomTab, Content = custom });
        return Ui.Stack(Ui.Row(Ui.Label(UiStrings.S_LayerEffectsDialog_Palette_BuildPalette_F3EEB77504, bold: true), Ui.InfoBadge(UiStrings.S_LayerEffectsDialog_Palette_BuildPalette_938E8B22DD)), tabs);
    }

    private void AddPaletteTile(EffectTile tile, Panel panel)
    {
        tile.Width = TileWidth;
        tile.Height = TileHeight;
        tile.Margin = new Thickness(2);
        tile.Show(0, RenderThumb);
        AttachTileInput(tile);
        if (tile.Preset is { } preset)
            PopupMenus.Attach(tile, () => PresetMenu(preset));
        _tiles.Add(tile);
        panel.Children.Add(tile);
    }

    private void RebuildCustomPalette()
    {
        foreach (var tile in _tiles.Where(tile => tile.Preset is not null).ToArray())
        {
            tile.Dispose();
            _tiles.Remove(tile);
        }
        _customWrap.Children.Clear();
        foreach (var preset in _presets)
        {
            var tile = new EffectTile(preset.Name, preset.Effects, preset);
            ToolTip.SetTip(tile, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.EffectPreset_TileTip, preset.Effects.Count));
            AddPaletteTile(tile, _customWrap);
        }
        _customEmpty.IsVisible = _presets.Count == 0;
    }

    private IEnumerable<object> PresetMenu(EffectPreset preset)
    {
        MenuItem Item(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => action();
            return item;
        }
        return
        [
            Item(UiStrings.EffectPreset_Rename, () => RenamePreset(preset)),
            Item(UiStrings.EffectPreset_Delete, () => DeletePreset(preset)),
        ];
    }

    private void SavePresetGroup()
    {
        if (_effects.Count == 0) return;
        var name = InputDialog.Ask(this, UiStrings.EffectPreset_SaveTitle, UiStrings.EffectPreset_NamePrompt,
            _effects[0].DisplayName, _presets.Select(preset => preset.Name))?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var preset = EffectPreset.Capture(name, _effects);
        var next = _presets.ToList();
        int duplicate = next.FindIndex(item => item.Name == name);
        if (duplicate >= 0)
        {
            if (Ui.Ask(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,
                    UiStrings.EffectPreset_OverwriteQuestion, name), UiStrings.EffectPreset_SaveTitle,
                    MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
            next[duplicate] = preset;
        }
        else next.Add(preset);
        PersistPresets(next);
    }

    private void RenamePreset(EffectPreset preset)
    {
        if (!_presets.Contains(preset)) return;
        var name = InputDialog.Ask(this, UiStrings.EffectPreset_RenameTitle, UiStrings.EffectPreset_NamePrompt,
            preset.Name, _presets.Select(item => item.Name))?.Trim();
        if (string.IsNullOrEmpty(name) || name == preset.Name) return;
        if (_presets.Any(item => item.Name == name))
        {
            Ui.Warn(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.EffectPreset_DuplicateName, name), UiStrings.EffectPreset_RenameTitle);
            return;
        }
        var next = _presets.ToList();
        next[next.IndexOf(preset)] = EffectPreset.Capture(name, preset.Effects);
        PersistPresets(next);
    }

    private void DeletePreset(EffectPreset preset)
    {
        if (!_presets.Contains(preset)
            || Ui.Ask(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,
                    UiStrings.EffectPreset_DeleteQuestion, preset.Name), UiStrings.EffectPreset_Delete,
                MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        var next = _presets.ToList();
        next.Remove(preset);
        PersistPresets(next);
    }

    private bool PersistPresets(IReadOnlyList<EffectPreset> next)
    {
        try
        {
            EffectPresets.Save(PresetPath, next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.EffectPreset_SaveError, ex.Message));
            return false;
        }
        _presets.Clear();
        _presets.AddRange(next);
        _customEmpty.Text = UiStrings.EffectPreset_Empty;
        RebuildCustomPalette();
        return true;
    }

    /// <summary>미리보기 원본: 지금 프레임 그림(투명 여백을 잘라 냄). 비었거나 너무 크면 작은 견본 그림.</summary>
    private PixelImage ThumbSource()
    {
        if (SourceAt(_frame) is not { } src) return SampleSprite();
        var bounds = src.GetOpaqueBounds();
        if (bounds.IsEmpty || bounds.Width > ThumbMaxSize || bounds.Height > ThumbMaxSize) return SampleSprite();
        return src.Crop(bounds);
    }

    /// <summary>16×16 공 모양 견본(밝은 면·몸·그늘 세 색).</summary>
    private static PixelImage SampleSprite()
    {
        var img = new PixelImage(16, 16);
        var body = new Color32(90, 140, 230);
        var shade = new Color32(50, 80, 170);
        var light = new Color32(175, 205, 255);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                double dx = x - 7.5, dy = y - 7.5, d2 = dx * dx + dy * dy;
                if (d2 > 56) continue;
                img[x, y] = dx + dy > 5 ? shade : dx + dy < -6 ? light : body;
            }
        return img;
    }

    /// <summary>효과 묶음을 미리보기 원본에 입힌 그림 — 움직이는 효과는 <see cref="ThumbSteps"/>칸 중 <paramref name="step"/>번째.</summary>
    private PixelImage RenderThumb(IReadOnlyList<LayerEffect> template, int step)
    {
        var src = _thumbSource;
        Func<int, PixelImage?>? frames = null;
        if (template.Any(effect => effect is AfterimageEffect))
        {
            // 잔상은 앞 프레임과 달라야 보이므로 원본이 한 칸씩 오른쪽으로 가는 것처럼 만든다.
            var moving = _thumbSource;
            frames = frame => Shifted(moving, frame);
            src = Shifted(moving, step);
        }
        var animated = LayerEffects.Animate(template, step, ThumbSteps, null, frames);
        return LayerEffects.Render(src, animated, LayerEffects.Padding(template, src.Width, src.Height)).Image;
    }

    private static PixelImage Shifted(PixelImage src, int frame)
    {
        var img = new PixelImage(src.Width + ThumbSteps, src.Height);
        img.Blit(src, Math.Clamp(frame, 0, ThumbSteps - 1), 0);
        return img;
    }

    // ───────────── 두 번 누르기·끌어 놓기 ─────────────

    private void AttachTileInput(EffectTile tile)
    {
        tile.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2)
            {
                Add(tile.Effects, _effects.Count);
                return;
            }
            _pressedTile = tile;
            _pressPoint = e.GetPosition(this);
            _tileDragging = false;
            e.Pointer.Capture(tile);
        };
        tile.PointerMoved += (_, e) =>
        {
            if (_pressedTile != tile) return;
            if (!_tileDragging && ReorderCore.Exceeded(_pressPoint, e.GetPosition(this))) _tileDragging = true;
            if (!_tileDragging) return;
            bool over = DropIndex(e) is not null;
            tile.Cursor = new Cursor(over ? StandardCursorType.DragCopy : StandardCursorType.No);
            ShowDropTarget(over);
        };
        tile.PointerReleased += (_, e) =>
        {
            if (_pressedTile != tile) return;
            int? at = _tileDragging ? DropIndex(e) : null;
            EndTileDrag();
            e.Pointer.Capture(null);
            if (at is { } i) Add(tile.Effects, i);
        };
        tile.PointerCaptureLost += (_, _) =>
        {
            if (_pressedTile == tile) EndTileDrag();
        };
    }

    private void EndTileDrag()
    {
        if (_pressedTile is { } tile) tile.Cursor = new Cursor(StandardCursorType.Hand);
        _pressedTile = null;
        _tileDragging = false;
        ShowDropTarget(false);
    }

    /// <summary>포인터가 효과 목록 위면 넣을 자리(그 위치보다 위에 있는 줄 수), 아니면 null.</summary>
    private int? DropIndex(PointerEventArgs e)
    {
        var p = e.GetPosition(_list);
        if (!new Rect(_list.Bounds.Size).Contains(p)) return null;
        int at = 0;
        foreach (var item in _list.Items.OfType<ListBoxItem>())
            if (item.TranslatePoint(new Avalonia.Point(0, item.Bounds.Height / 2), _list) is { } mid && mid.Y < p.Y) at++;
        return at;
    }

    private void ShowDropTarget(bool on)
    {
        if (on)
        {
            _list.BorderBrush = AppTheme.AccentBrush;
            _list.BorderThickness = new Thickness(2);
        }
        else
        {
            _list.ClearValue(BorderBrushProperty);
            _list.ClearValue(BorderThicknessProperty);
        }
    }

    private void DisposePalette()
    {
        _thumbTimer.Stop();
        foreach (var tile in _tiles) tile.Dispose();
    }

    /// <summary>효과 종류 상자: 위 = 미리보기(같은 정수 배율, 작으면 맞춤 축소), 아래 = 이름.</summary>
    private sealed class EffectTile : Control, IDisposable
    {
        private readonly WriteableBitmap?[] _frames;
        private int _shown;
        private bool _hover;

        public EffectTile(string name, IReadOnlyList<LayerEffect> effects, EffectPreset? preset = null)
        {
            Title = name;
            Effects = effects;
            Preset = preset;
            _frames = new WriteableBitmap?[effects.Any(effect => effect is AnimatedEffect) ? ThumbSteps : 1];
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public string Title { get; }
        public IReadOnlyList<LayerEffect> Effects { get; }
        public EffectPreset? Preset { get; }
        public bool Animated => _frames.Length > 1;
        /// <summary><paramref name="step"/>번째 모습을 보인다(처음 볼 때 한 번만 그림).</summary>
        public void Show(int step, Func<IReadOnlyList<LayerEffect>, int, PixelImage> render)
        {
            step = Math.Clamp(step, 0, _frames.Length - 1);
            _frames[step] ??= ImageUtil.ToBitmap(render(Effects, step));
            if (_shown == step) return;
            _shown = step;
            InvalidateVisual();
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            _hover = true;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hover = false;
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            var b = new Rect(Bounds.Size);
            g.FillRectangle(_hover ? AppTheme.HoverBrush : AppTheme.PanelBrush, b);
            g.DrawRectangle(new Pen(AppTheme.BorderBrush, 1), b.Deflate(0.5));
            if (_frames[_shown] is { } bmp)
            {
                var area = new Rect(5, 5, b.Width - 10, b.Height - 28);
                var src = new Rectangle(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
                double fit = Math.Min(area.Width / src.Width, area.Height / src.Height);
                ImageUtil.DrawFitted(g, bmp, src, area, fit >= 1 ? (int)Math.Floor(fit) : 0, checker: true);
            }
            AppTheme.DrawTextIn(g, Title, new Rect(2, b.Height - 21, b.Width - 4, 18), AppTheme.TextBrush, AppTheme.SmallFontSize, center: true);
        }

        public void Dispose()
        {
            for (int i = 0; i < _frames.Length; i++)
            {
                _frames[i]?.Dispose();
                _frames[i] = null;
            }
        }
    }
}
