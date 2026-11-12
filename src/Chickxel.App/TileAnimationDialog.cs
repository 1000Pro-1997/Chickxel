using System.Runtime.CompilerServices;
using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 움직이는 타일 편집: 그림 차례(썸네일 줄 — 고르기·앞뒤로 옮기기·복제·빼기), 타일셋의 다른 타일을 그림으로 더하기(같은 크기만),
/// 한 장 시간, 오른쪽 확대 재생 미리보기(3×3 이어 보기). 결과는 <see cref="TileDef.WithFrames"/>(그림이 1장이면 멈춘 타일).
/// </summary>
public sealed class TileAnimationDialog : DialogBase
{
    /// <summary>미리보기 그림 칸 한 변(DIP).</summary>
    private const double PreviewSize = 208;

    private readonly TileDef _tile;
    private readonly List<PixelImage> _frames;
    private readonly ConditionalWeakTable<PixelImage, WriteableBitmap> _bitmaps = new();
    private readonly FrameStrip _strip;
    private readonly ScrollViewer _stripScroll;
    private readonly Button _earlier, _later, _duplicate, _remove, _add, _play;
    private readonly TextBlock _selectedText, _cycleText, _pickText, _previewText;
    private readonly NumericUpDown _ms;
    private readonly TilePaletteView _palette;
    private readonly CheckBox _tiled;
    private readonly PreviewView _preview;
    private readonly DispatcherTimer _timer = new();
    // 고른 그림(고치기 단추가 다루는 것), 재생 중 보이는 그림
    private int _selected, _playing;

    private TileAnimationDialog(Tileset ts, TileDef tile)
        : base(UiStrings.FormatTileAnim_Title(name: tile.Name.Length > 0 ? tile.Name : UiStrings.FormatTileAnim_TileNumber(id: tile.Id)))
    {
        _tile = tile;
        _frames = Enumerable.Range(0, tile.FrameCount).Select(tile.FrameImage).ToList();

        // ── 왼쪽: 그림 차례 ──
        _strip = new FrameStrip(this);
        _stripScroll = new ScrollViewer
        {
            Content = _strip,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Width = 400,
        };
        _earlier = Ui.IconButton(VectorIcon.ChevronLeft, UiStrings.TileAnim_MoveEarlier, (_, _) => MoveSelected(-1));
        _later = Ui.IconButton(VectorIcon.ChevronRight, UiStrings.TileAnim_MoveLater, (_, _) => MoveSelected(1));
        _duplicate = Ui.Button(VectorIcon.Copy, UiStrings.TileAnim_Duplicate, (_, _) => DuplicateSelected());
        _remove = Ui.Button(VectorIcon.Trash, UiStrings.TileAnim_Remove, (_, _) => RemoveSelected());
        _selectedText = Ui.Label("");
        _selectedText.Foreground = AppTheme.AccentBrush;
        _ms = Ui.Num(TileDef.MinFrameMs, 10_000, tile.FrameMs, 70).Presets(50, 80, 100, 120, 150, 200, 250, 300, 500).Unit("ms");
        _ms.ValueChanged += (_, _) => Refresh();
        _cycleText = Ui.Label("");
        _cycleText.Foreground = AppTheme.DimTextBrush;
        var framesBox = Ui.Group(UiStrings.TileAnim_Frames, Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.FormatTileAnim_Size(width: tile.Image.Width, height: tile.Image.Height)), Ui.InfoBadge(UiStrings.TileAnim_FramesInfo)),
            _stripScroll,
            Ui.Row(_earlier, _later, _duplicate, _remove, _selectedText),
            Ui.Row(Ui.Label(UiStrings.TileAnim_FrameMs), _ms, _cycleText)));

        // ── 왼쪽 아래: 타일셋에서 그림 더하기 ──
        _palette = new TilePaletteView { Tileset = ts, SlotSize = 28, MinColumns = 8 };
        _palette.SelectionChanged += _ => UpdatePick();
        _palette.TileActivated += _ => AddPicked();
        var paletteScroll = new ScrollViewer
        {
            Content = _palette,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 200,
            Width = 400,
        };
        _add = Ui.Button(VectorIcon.Plus, UiStrings.TileAnim_Add, (_, _) => AddPicked());
        _pickText = Ui.Label("");
        _pickText.TextWrapping = TextWrapping.Wrap;
        _pickText.Width = 400;
        var addBox = Ui.Group(UiStrings.TileAnim_AddFromTileset, Ui.Stack(
            paletteScroll,
            Ui.Row(_add, Ui.InfoBadge(UiStrings.TileAnim_AddInfo)),
            _pickText));

        // ── 오른쪽: 재생 미리보기 ──
        _preview = new PreviewView(this) { Width = PreviewSize, Height = PreviewSize };
        _play = Ui.IconButton(VectorIcon.Play, UiStrings.TileAnim_Play, (_, _) => SetPlaying(!_timer.IsEnabled));
        _tiled = Ui.Check(UiStrings.TileAnim_Tiled, true);
        _tiled.IsCheckedChanged += (_, _) => _preview.InvalidateVisual();
        _previewText = Ui.Label("");
        var previewBox = Ui.Group(UiStrings.TileAnim_Preview, Ui.Stack(
            new Border { BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), Child = _preview, HorizontalAlignment = HorizontalAlignment.Left },
            Ui.Row(_play, _previewText),
            _tiled));

        var left = new StackPanel { Spacing = 8, Children = { framesBox, addBox } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,Auto") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(previewBox, 2);
        previewBox.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(left);
        grid.Children.Add(previewBox);
        Body.Children.Add(grid);

        _timer.Tick += (_, _) =>
        {
            _playing = (_playing + 1) % _frames.Count;
            ShowPlayback();
        };
        Refresh();
        UpdatePick();
        // 움직이는 타일이면 열자마자 돈다
        SetPlaying(_frames.Count > 1);
    }

    /// <summary>타일 <paramref name="tile"/>의 그림들을 고친 타일(취소하면 null).</summary>
    public static TileDef? Run(Visual owner, Tileset ts, TileDef tile)
    {
        var dlg = new TileAnimationDialog(ts, tile);
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        return tile.WithFrames(dlg._frames, (int)dlg._ms.NumValue());
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private int FrameMs => (int)_ms.NumValue();

    /// <summary>미리보기에 보이는 그림: 재생 중이면 도는 그림, 멈췄으면 고른 그림.</summary>
    private int ShownFrame => _timer.IsEnabled ? _playing : _selected;

    private WriteableBitmap BitmapOf(PixelImage image) => _bitmaps.GetValue(image, ImageUtil.ToBitmap);

    // ───────────── 그림 차례 고치기 ─────────────

    /// <summary>그림 <paramref name="index"/>를 고른다(재생은 멈춰 미리보기가 고른 그림을 보인다).</summary>
    private void Select(int index)
    {
        _selected = Math.Clamp(index, 0, _frames.Count - 1);
        SetPlaying(false);
        Refresh();
        ScrollToSelected();
    }

    private void MoveSelected(int delta)
    {
        int to = _selected + delta;
        if (to < 0 || to >= _frames.Count) return;
        (_frames[_selected], _frames[to]) = (_frames[to], _frames[_selected]);
        _selected = to;
        Refresh();
        ScrollToSelected();
    }

    private void DuplicateSelected()
    {
        if (_frames.Count >= TileDef.MaxFrames) return;
        _frames.Insert(_selected + 1, _frames[_selected].Clone());
        _selected++;
        Refresh();
        ScrollToSelected();
    }

    private void RemoveSelected()
    {
        if (_frames.Count <= 1) return;
        _frames.RemoveAt(_selected);
        _selected = Math.Min(_selected, _frames.Count - 1);
        _playing %= _frames.Count;
        if (_frames.Count < 2) SetPlaying(false);
        Refresh();
    }

    /// <summary>타일 목록에서 고른 타일들 가운데 크기가 같은 것의 그림들(움직이는 타일은 그 그림 모두, 목록 차례).</summary>
    private List<PixelImage> PickedImages(out int wrongSize)
    {
        var picked = _palette.SelectedTiles;
        var images = new List<PixelImage>();
        wrongSize = 0;
        foreach (var t in picked)
        {
            if (t.Image.Width != _tile.Image.Width || t.Image.Height != _tile.Image.Height)
            {
                wrongSize++;
                continue;
            }
            for (int i = 0; i < t.FrameCount; i++) images.Add(t.FrameImage(i));
        }
        return images;
    }

    /// <summary>고른 타일들의 그림을 고른 그림 뒤에 넣는다(같은 크기만, 최대 장 수까지).</summary>
    private void AddPicked()
    {
        var images = PickedImages(out _);
        if (images.Count == 0 || _frames.Count + images.Count > TileDef.MaxFrames) return;
        _frames.InsertRange(_selected + 1, images.Select(i => i.Clone()));
        _selected += images.Count;
        Refresh();
        ScrollToSelected();
        UpdatePick();
    }

    /// <summary>고른 타일에 맞춰 '그림으로 더하기' 단추와 안내 글(몇 장이 들어갈지, 안 되면 까닭)을 맞춘다.</summary>
    private void UpdatePick()
    {
        var images = PickedImages(out int wrongSize);
        int tiles = _palette.SelectedTiles.Count;
        string text;
        bool ok = false, warn = false;
        if (tiles == 0) text = UiStrings.TileAnim_PickNone;
        else if (images.Count == 0)
        {
            text = UiStrings.FormatTileAnim_PickWrongSize(width: _tile.Image.Width, height: _tile.Image.Height);
            warn = true;
        }
        else if (_frames.Count + images.Count > TileDef.MaxFrames)
        {
            text = UiStrings.FormatTileAnim_TooMany(max: TileDef.MaxFrames);
            warn = true;
        }
        else
        {
            text = UiStrings.FormatTileAnim_PickCount(count: tiles - wrongSize, frames: images.Count)
                   + (wrongSize > 0 ? " · " + UiStrings.FormatTileAnim_PickSkipped(count: wrongSize) : "");
            ok = true;
            warn = wrongSize > 0;
        }
        _add.IsEnabled = ok;
        _pickText.Text = text;
        _pickText.Foreground = warn ? AppTheme.WarningBrush : ok ? AppTheme.AccentBrush : AppTheme.DimTextBrush;
    }

    /// <summary>그림 차례·고른 그림이 바뀜: 줄·단추·글·미리보기를 맞춘다.</summary>
    private void Refresh()
    {
        int n = _frames.Count;
        _selected = Math.Clamp(_selected, 0, n - 1);
        _earlier.IsEnabled = _selected > 0;
        _later.IsEnabled = _selected < n - 1;
        _duplicate.IsEnabled = n < TileDef.MaxFrames;
        _remove.IsEnabled = n > 1;
        _play.IsEnabled = n > 1;
        _selectedText.Text = UiStrings.FormatTileAnim_Selected(index: _selected + 1, count: n);
        _cycleText.Text = n > 1 ? UiStrings.FormatTileAnim_Cycle(ms: (long)FrameMs * n) : UiStrings.TileAnim_Still;
        _timer.Interval = TimeSpan.FromMilliseconds(FrameMs);
        _strip.InvalidateMeasure();
        _strip.InvalidateVisual();
        UpdatePick();
        ShowPlayback();
    }

    /// <summary>재생 미리보기·줄의 재생 표시·그림 번호 글을 맞춘다.</summary>
    private void ShowPlayback()
    {
        _preview.InvalidateVisual();
        _strip.InvalidateVisual();
        _previewText.Text = UiStrings.FormatTileAnim_PreviewFrame(index: ShownFrame + 1, count: _frames.Count);
    }

    private void SetPlaying(bool on)
    {
        on &= _frames.Count > 1;
        if (on)
        {
            _playing = _selected;
            _timer.Start();
        }
        else _timer.Stop();
        _play.Content = Ui.Icon(on ? VectorIcon.Pause : VectorIcon.Play, 14);
        _play.Tip(on ? UiStrings.TileAnim_Pause : UiStrings.TileAnim_Play);
        ShowPlayback();
    }

    private void ScrollToSelected()
    {
        double x = FrameStrip.Gap + _selected * (FrameStrip.Slot + FrameStrip.Gap);
        var offset = _stripScroll.Offset;
        double view = _stripScroll.Viewport.Width;
        if (view <= 0) return;
        if (x < offset.X) _stripScroll.Offset = offset.WithX(x - FrameStrip.Gap);
        else if (x + FrameStrip.Slot > offset.X + view) _stripScroll.Offset = offset.WithX(x + FrameStrip.Slot + FrameStrip.Gap - view);
    }

    /// <summary>그림을 칸 <paramref name="area"/> 안에 가운데로(정수배로 들어가면 정수배) 그린다.</summary>
    private void DrawFitted(DrawingContext g, PixelImage image, Rect area)
    {
        double scale = Math.Min(area.Width / image.Width, area.Height / image.Height);
        if (scale >= 1) scale = Math.Floor(scale);
        double w = image.Width * scale, h = image.Height * scale;
        g.DrawImage(BitmapOf(image), new Rect(0, 0, image.Width, image.Height), new Rect(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
    }

    // ───────────── 그림 차례 줄 ─────────────

    /// <summary>그림 차례 썸네일 줄: 누르면 고르고, 고른 그림은 강조 테두리, 재생 중 보이는 그림은 아래 막대로 표시.</summary>
    private sealed class FrameStrip(TileAnimationDialog owner) : Control
    {
        public const double Slot = 56, Gap = 4;
        private const double NumberHeight = 16;
        private static readonly IPen SelectedPen = AppTheme.Pen(AppTheme.AccentColor, 2);
        private static readonly IPen SlotPen = AppTheme.Pen(AppTheme.BorderColor);

        protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
            => new(Gap + owner._frames.Count * (Slot + Gap), Gap + Slot + NumberHeight + Gap);

        private Rect SlotRect(int i) => new(Gap + i * (Slot + Gap), Gap, Slot, Slot);

        public override void Render(DrawingContext g)
        {
            g.FillRectangle(AppTheme.BackBrush, new Rect(Bounds.Size));
            bool playing = owner._timer.IsEnabled;
            for (int i = 0; i < owner._frames.Count; i++)
            {
                var r = SlotRect(i);
                AppTheme.DrawChecker(g, r, 4);
                using (ImageUtil.Pixelated(g))
                    owner.DrawFitted(g, owner._frames[i], r.Deflate(2));
                bool selected = i == owner._selected;
                g.DrawRectangle(null, selected ? SelectedPen : SlotPen, selected ? r.Deflate(1) : r.Deflate(0.5));
                var number = new Rect(r.X, r.Bottom, r.Width, NumberHeight);
                if (playing && i == owner._playing) g.FillRectangle(AppTheme.AccentBrush, new Rect(r.X + 4, r.Bottom + 2, r.Width - 8, 3));
                AppTheme.DrawTextIn(g, (i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), number.Translate(new Vector(0, 3)),
                    selected ? AppTheme.AccentBrush : AppTheme.DimTextBrush, AppTheme.SmallFontSize, bold: selected, center: true);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var p = e.GetPosition(this);
            int i = (int)Math.Floor((p.X - Gap / 2) / (Slot + Gap));
            if (i < 0 || i >= owner._frames.Count) return;
            e.Handled = true;
            owner.Select(i);
        }
    }

    // ───────────── 재생 미리보기 ─────────────

    /// <summary>확대 미리보기: 보이는 그림 하나를 크게, 또는 3×3으로 이어 붙여(이음매 확인).</summary>
    private sealed class PreviewView(TileAnimationDialog owner) : Control
    {
        public override void Render(DrawingContext g)
        {
            var area = new Rect(Bounds.Size);
            AppTheme.DrawChecker(g, area, 8);
            var image = owner._frames[Math.Clamp(owner.ShownFrame, 0, owner._frames.Count - 1)];
            using (ImageUtil.Pixelated(g))
            {
                if (owner._tiled.IsChecked != true)
                {
                    owner.DrawFitted(g, image, area.Deflate(4));
                    return;
                }
                double scale = Math.Min((area.Width - 8) / (3.0 * image.Width), (area.Height - 8) / (3.0 * image.Height));
                if (scale >= 1) scale = Math.Floor(scale);
                double w = image.Width * scale, h = image.Height * scale;
                double x0 = (area.Width - 3 * w) / 2, y0 = (area.Height - 3 * h) / 2;
                var bmp = owner.BitmapOf(image);
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                        g.DrawImage(bmp, new Rect(0, 0, image.Width, image.Height), new Rect(x0 + x * w, y0 + y * h, w, h));
            }
        }
    }
}
