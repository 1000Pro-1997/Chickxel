using Gpixel.Core.Edit;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>
/// 효과 창 왼쪽 끝의 효과 종류 칸: 종류마다 미리보기 그림(지금 프레임 그림에 기본 설정으로 입힌 모습 — 움직이는 효과는 돌려 보임) + 이름 상자.
/// 두 번 누르거나 효과 목록으로 끌어 놓으면 "추가 범위"로 추가한다(끌어 놓으면 놓은 자리에).
/// </summary>
public sealed partial class LayerEffectsDialog
{
    private const int TileWidth = 88, TileHeight = 96, ThumbSteps = 12, ThumbHold = 4, ThumbMaxSize = 128;

    private readonly List<EffectTile> _tiles = new();
    private readonly DispatcherTimer _thumbTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private PixelImage _thumbSource = SampleSprite();
    private int _thumbTick;
    private EffectTile? _pressedTile;
    private Avalonia.Point _pressPoint;
    private bool _tileDragging;

    private Control BuildPalette(string? motionTip)
    {
        _thumbSource = ThumbSource();
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        for (int k = 0; k < KindTable.Length; k++)
        {
            var tile = new EffectTile(k, Kinds[k], IsAnimated(k) ? ThumbSteps : 1) { Width = TileWidth, Height = TileHeight, Margin = new Thickness(2) };
            ToolTip.SetTip(tile, $"{Kinds[k]}\n{DescriptionOf(k)}" + (tile.Animated ? UiStrings.S_LayerEffectsDialog_Palette_BuildPalette_164E2EA098 : "")
                                 + (tile.Animated && motionTip is not null ? " " + motionTip : ""));
            tile.Show(0, RenderThumb);
            AttachTileInput(tile);
            _tiles.Add(tile);
            wrap.Children.Add(tile);
        }
        _thumbTimer.Tick += (_, _) =>
        {
            // 움직이는 효과: 0 → 끝까지 돌리고 끝 모습을 잠깐 보여 준 뒤 처음부터.
            _thumbTick = (_thumbTick + 1) % (ThumbSteps + ThumbHold);
            int step = Math.Min(_thumbTick, ThumbSteps - 1);
            foreach (var tile in _tiles)
                if (tile.Animated) tile.Show(step, RenderThumb);
        };
        _thumbTimer.Start();
        int width = (TileWidth + 4) * 2 + 18; // 두 줄 + 세로 스크롤바
        var scroll = new ScrollViewer
        {
            Content = wrap,
            Width = width,
            Height = 560,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        return Ui.Stack(Ui.Row(Ui.Label(UiStrings.S_LayerEffectsDialog_Palette_BuildPalette_F3EEB77504, bold: true), Ui.InfoBadge(UiStrings.S_LayerEffectsDialog_Palette_BuildPalette_938E8B22DD)), scroll);
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

    /// <summary>종류 <paramref name="kind"/>의 새 효과(기본 설정)를 미리보기 원본에 입힌 그림 — 움직이는 효과는 <see cref="ThumbSteps"/>칸 중 <paramref name="step"/>번째.</summary>
    private PixelImage RenderThumb(int kind, int step)
    {
        IReadOnlyList<LayerEffect> list = [Create(kind)];
        var src = _thumbSource;
        Func<int, PixelImage?>? frames = null;
        if (list[0] is AfterimageEffect)
        {
            // 잔상은 앞 프레임과 달라야 보이므로 원본이 한 칸씩 오른쪽으로 가는 것처럼 만든다.
            var moving = _thumbSource;
            frames = f => Shifted(moving, f);
            src = Shifted(moving, step);
        }
        var animated = LayerEffects.Animate(list, step, ThumbSteps, null, frames);
        return LayerEffects.Render(src, animated, LayerEffects.Padding(list, src.Width, src.Height)).Image;
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
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                Add(tile.Kind, _effects.Count);
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
            if (at is { } i) Add(tile.Kind, i);
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

        public EffectTile(int kind, string name, int frames)
        {
            Kind = kind;
            Title = name;
            _frames = new WriteableBitmap?[frames];
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public int Kind { get; }
        public string Title { get; }
        public bool Animated => _frames.Length > 1;

        /// <summary><paramref name="step"/>번째 모습을 보인다(처음 볼 때 한 번만 그림).</summary>
        public void Show(int step, Func<int, int, PixelImage> render)
        {
            step = Math.Clamp(step, 0, _frames.Length - 1);
            _frames[step] ??= ImageUtil.ToBitmap(render(Kind, step));
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
