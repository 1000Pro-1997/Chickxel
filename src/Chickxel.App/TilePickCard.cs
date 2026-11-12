using System.Runtime.CompilerServices;
using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>타일 화면 도우미: 타일 그림 비트맵(그림마다 한 번), 칸 묶음을 칸 안에 맞춰 그리기, 자동 타일 세트 대표 타일·쓰인 타일, 무리 색.</summary>
internal static class TileVisuals
{
    private static readonly ConditionalWeakTable<PixelImage, WriteableBitmap> Bitmaps = new();

    // 무리 표시 색(이름으로 정해짐 — 같은 무리는 같은 색)
    private static readonly IImmutableSolidColorBrush[] GroupBrushes =
    [
        AppTheme.Brush(Color.FromRgb(110, 190, 90)),
        AppTheme.Brush(Color.FromRgb(90, 160, 235)),
        AppTheme.Brush(Color.FromRgb(225, 170, 70)),
        AppTheme.Brush(Color.FromRgb(200, 110, 210)),
        AppTheme.Brush(Color.FromRgb(80, 200, 190)),
        AppTheme.Brush(Color.FromRgb(230, 110, 100)),
        AppTheme.Brush(Color.FromRgb(170, 150, 110)),
        AppTheme.Brush(Color.FromRgb(160, 170, 240)),
    ];

    public static WriteableBitmap Bitmap(PixelImage image) => Bitmaps.GetValue(image, ImageUtil.ToBitmap);

    /// <summary>무리 이름의 표시 색(빈 이름이면 null).</summary>
    public static IBrush? GroupBrush(string group)
    {
        if (group.Length == 0) return null;
        uint h = 2166136261;
        foreach (char c in group) h = (h ^ c) * 16777619;
        return GroupBrushes[h % (uint)GroupBrushes.Length];
    }

    /// <summary>세트의 대표 칸: 사방이 이어진 안쪽 모양에 놓일 타일(타일 있는 규칙이 없으면 null).</summary>
    public static TileCell? Representative(Tileset ts, AutoTileSet set)
        => AutoTiler.Pick(set, 255, default) is { } p && ts.Find(p.Tile) is not null ? new TileCell(p.Tile, ts.Allowed(p.Transform)) : null;

    /// <summary>세트 규칙들에 넣은 타일 Id.</summary>
    public static HashSet<int> TilesOf(AutoTileSet set)
    {
        var ids = new HashSet<int>();
        foreach (var rule in set.Rules)
            if (rule.HasTiles)
                foreach (var c in rule.Tiles) ids.Add(c.Tile);
        return ids;
    }

    /// <summary>표시용 타일 이름(이름이 없으면 "타일 Id").</summary>
    public static string TileName(TileDef def) => def.Name.Length > 0 ? def.Name : UiStrings.FormatTilePick_TileId(id: def.Id);

    /// <summary>칸 묶음(칸 자리, 칸)을 <paramref name="area"/> 안 가운데에 비율대로 맞춰 그린다(픽셀 그대로, 움직이는 타일은 시각 <paramref name="timeMs"/>의 그림).</summary>
    public static void DrawCells(DrawingContext g, Tileset ts, IReadOnlyList<(Point At, TileCell Cell)> cells, Rect area, long timeMs)
    {
        Rectangle union = Rectangle.Empty;
        foreach (var (at, cell) in cells)
            if (ts.Placement(at, cell, timeMs) is { } p) union = union.IsEmpty ? p.Rect : Rectangle.Union(union, p.Rect);
        if (union.Width <= 0 || union.Height <= 0) return;
        double scale = Math.Min(area.Width / union.Width, area.Height / union.Height);
        double ox = area.X + (area.Width - union.Width * scale) / 2 - union.X * scale;
        double oy = area.Y + (area.Height - union.Height * scale) / 2 - union.Y * scale;
        using (ImageUtil.Pixelated(g))
            foreach (var (at, cell) in cells)
            {
                if (ts.Placement(at, cell, timeMs) is not { } p) continue;
                var bmp = Bitmap(p.Image);
                g.DrawImage(bmp, new Rect(0, 0, p.Image.Width, p.Image.Height),
                    new Rect(ox + p.Rect.X * scale, oy + p.Rect.Y * scale, p.Rect.Width * scale, p.Rect.Height * scale));
            }
    }

    /// <summary>칸 묶음에 움직이는 타일이 있는지.</summary>
    public static bool AnyAnimated(Tileset ts, IReadOnlyList<(Point At, TileCell Cell)> cells)
    {
        foreach (var (_, cell) in cells)
            if (ts.Find(cell.Tile) is { IsAnimated: true }) return true;
        return false;
    }
}

/// <summary>칸 묶음 작은 그림(체커 바탕 + 테두리). 비었으면 <see cref="Icon"/>을 가운데 그린다. 시각은 <see cref="TimeMs"/>.</summary>
internal sealed class TileThumb : Control
{
    private static readonly IPen FramePen = AppTheme.Pen(AppTheme.BorderColor);

    public Tileset? Tileset { get; set; }

    public IReadOnlyList<(Point At, TileCell Cell)> Cells { get; set; } = [];

    /// <summary>그릴 칸이 없을 때 보일 아이콘(없으면 빈 칸).</summary>
    public VectorIcon? Icon { get; set; }

    public long TimeMs { get; set; }

    /// <summary>움직이는 타일이 들어 있는지(시계가 가면 다시 그림).</summary>
    public bool IsAnimated => Tileset is { } ts && TileVisuals.AnyAnimated(ts, Cells);

    public TileThumb(double size)
    {
        Width = size;
        Height = size;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public override void Render(DrawingContext g)
    {
        var area = new Rect(Bounds.Size);
        AppTheme.DrawChecker(g, area, Math.Max(3, Bounds.Width / 6));
        if (Tileset is { } ts && Cells.Count > 0) TileVisuals.DrawCells(g, ts, Cells, area.Deflate(2), TimeMs);
        else if (Icon is { } icon)
        {
            g.FillRectangle(AppTheme.InputBrush, area);
            VectorIcons.Draw(g, icon, area, Math.Round(Bounds.Width * 0.55), AppTheme.TextBrush);
        }
        g.DrawRectangle(null, FramePen, area.Deflate(0.5));
    }
}

/// <summary>
/// '지금 찍을 것' 카드: 타일 도구가 다음에 찍을 것(타일·스탬프 묶음·자동 타일 세트·지우개)의 그림, 이름, 종류, 들어갈 층(잠김·숨김이면 경고 색).
/// 타일셋 패널 위와 도구 설정의 타일 묶음에 놓는다. 타일 상태·프로젝트·레이어가 바뀌면 스스로 고친다.
/// </summary>
internal sealed class TilePickCard : Border
{
    private static readonly IBrush ActiveBorder = AppTheme.Brush(Color.FromArgb(160, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));

    private readonly EditorContext _ctx;
    private readonly TileThumb _thumb;
    private readonly TextBlock _name, _kind, _floor;
    private readonly IconView _floorIcon;
    private readonly DockPanel _floorRow;

    public TilePickCard(EditorContext ctx, double thumbSize = 40)
    {
        _ctx = ctx;
        _thumb = new TileThumb(thumbSize) { Margin = new Thickness(0, 0, 8, 0) };
        var title = new TextBlock { Text = UiStrings.TilePick_Title, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize };
        _name = new TextBlock { FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis };
        _kind = new TextBlock { Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextTrimming = TextTrimming.CharacterEllipsis };
        _floor = new TextBlock { FontSize = AppTheme.SmallFontSize, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _floorIcon = new IconView(VectorIcon.Layers) { Width = 11, Height = 11, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        _floorRow = new DockPanel();
        DockPanel.SetDock(_floorIcon, Dock.Left);
        _floorRow.Children.Add(_floorIcon);
        _floorRow.Children.Add(_floor);
        ToolTip.SetTip(_floorRow, UiStrings.TilePick_TargetFloorTip);
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, _name, _kind, _floorRow } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(text, 1);
        grid.Children.Add(_thumb);
        grid.Children.Add(text);
        Child = grid;
        Background = AppTheme.InputBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(6, 5);
        Margin = new Thickness(2, 4, 2, 2);

        ctx.TileStateChanged += (_, _) => Refresh();
        ctx.CurrentChanged += (_, _) => Refresh();
        ctx.ProjectReplaced += (_, _) => Refresh();
        ctx.ProjectChanged += (_, _) => Refresh();
        ctx.TileClockTick += () =>
        {
            if (!IsEffectivelyVisible || !_thumb.IsAnimated) return;
            _thumb.TimeMs = ctx.TileClockMs;
            _thumb.InvalidateVisual();
        };
        Refresh();
    }

    public void Refresh()
    {
        if (!_ctx.IsTilemapSelected || _ctx.CurrentTileset is not { } ts)
        {
            IsVisible = false;
            return;
        }
        IsVisible = true;
        var pick = TilePick.Describe(_ctx, ts);
        _thumb.Tileset = ts;
        _thumb.Cells = pick.Cells;
        _thumb.Icon = pick.Icon;
        _thumb.TimeMs = _ctx.TileClockMs;
        _thumb.InvalidateVisual();
        _name.Text = pick.Name;
        _name.Foreground = pick.Chosen ? AppTheme.TextBrush : AppTheme.DimTextBrush;
        _kind.Text = pick.Kind;
        _kind.TextWrapping = pick.Chosen ? TextWrapping.NoWrap : TextWrapping.Wrap;
        _floor.Text = pick.FloorState switch
        {
            TileFloorState.Locked => pick.Floor + " · " + UiStrings.TilePick_Locked,
            TileFloorState.Hidden => pick.Floor + " · " + UiStrings.TilePick_Hidden,
            _ => pick.Floor,
        };
        _floorRow.IsVisible = pick.Floor.Length > 0;
        var warn = pick.FloorState != TileFloorState.Normal;
        _floor.Foreground = warn ? AppTheme.WarningBrush : AppTheme.TextBrush;
        _floorIcon.Foreground = warn ? AppTheme.WarningBrush : AppTheme.DimTextBrush;
        _floorIcon.Icon = pick.FloorState switch
        {
            TileFloorState.Locked => VectorIcon.Lock,
            TileFloorState.Hidden => VectorIcon.EyeOff,
            _ => VectorIcon.Layers,
        };
        BorderBrush = warn ? AppTheme.WarningBrush : pick.Chosen ? ActiveBorder : AppTheme.BorderBrush;
    }
}

/// <summary>찍을 층의 상태(잠기면 찍히지 않고, 숨기면 찍어도 안 보임).</summary>
internal enum TileFloorState { Normal, Hidden, Locked }

/// <summary>지금 찍을 것 설명: 이름, 종류, 층 글, 층 상태, 들어갈 층 Id들, 그림(칸 묶음 또는 아이콘).</summary>
internal sealed record TilePick(
    bool Chosen, string Name, string Kind, string Floor, TileFloorState FloorState, IReadOnlyList<int> Floors,
    IReadOnlyList<(Point At, TileCell Cell)> Cells, VectorIcon? Icon)
{
    /// <summary>지금 도구·붓·자동 타일 세트·층 고정으로 다음에 찍을 것.</summary>
    public static TilePick Describe(EditorContext ctx, Tileset ts)
    {
        var data = ctx.CurrentTiles;
        var tool = ctx.TileTool;
        if (tool == TileTool.Eraser)
        {
            int[] floors = ctx.TileFloor is { } f ? [ts.ValidFloor(f)] : [];
            return new TilePick(true, UiStrings.TilePick_Eraser, UiStrings.TilePick_EraserKind,
                floors.Length > 0 ? FloorText(ts, floors) : UiStrings.TilePick_FloorTopVisible, StateOf(data, floors), floors, [], VectorIcon.Eraser);
        }
        if (ctx.UsesAutoTileSource && ctx.CurrentAutoTileSet is { } set)
        {
            int[] floors = [set.Floor];
            var kind = UiStrings.FormatTilePick_AutoKind(count: set.Rules.Length);
            if (set.Group.Length > 0) kind += " · " + UiStrings.FormatTilePick_Group(name: set.Group);
            var rep = TileVisuals.Representative(ts, set);
            return new TilePick(true, set.Name.Length > 0 ? set.Name : UiStrings.TilesetChoice_Unnamed, kind, FloorText(ts, floors), StateOf(data, floors), floors,
                rep is { } c ? [(default, c)] : [], VectorIcon.Grid3x3);
        }
        if (tool == TileTool.AutoTile)
            return new TilePick(false, UiStrings.TilePick_Nothing, UiStrings.TilePick_NoSetHint, "", TileFloorState.Normal, [], [], VectorIcon.Grid3x3);
        if (ctx.EffectiveStamp is { Cells.Count: > 0 } stamp)
        {
            var cells = stamp.Cells.Select(c => (new Point(c.Dx, c.Dy), c.Cell)).ToList();
            var box = stamp.Bounds(ts);
            string name, kind;
            if (stamp.Cells.Count == 1 && ts.Find(stamp.Cells[0].Cell.Tile) is { } def)
            {
                name = TileVisuals.TileName(def);
                kind = UiStrings.FormatTilePick_SingleKind(w: box.Width, h: box.Height);
                if (def.IsAnimated) kind += " · " + UiStrings.FormatTilePick_Frames(count: def.FrameCount);
            }
            else
            {
                name = UiStrings.FormatTilePick_Tiles(count: stamp.Cells.Count);
                kind = ctx.TileRandom ? UiStrings.TilePick_RandomKind : UiStrings.FormatTilePick_StampKind(w: box.Width, h: box.Height);
            }
            if (ctx.TileTransform != TileTransform.None) kind += " · " + UiStrings.TilePick_Turned;
            var floors = stamp.Cells.Select(c => ts.ValidFloor(ctx.TileFloor ?? c.Floor ?? ts.Find(c.Cell.Tile)?.Floor ?? 0)).Distinct().ToList();
            return new TilePick(true, name, kind, FloorText(ts, floors), StateOf(data, floors), floors, cells, null);
        }
        return new TilePick(false, UiStrings.TilePick_Nothing, UiStrings.TilePick_NothingHint, "", TileFloorState.Normal, [], [], VectorIcon.Square);
    }

    private static string FloorText(Tileset ts, IReadOnlyList<int> floors)
        => UiStrings.FormatTilePick_Floor(name: string.Join(", ", floors.Select(f => ts.FindFloor(f)?.Name ?? "?")));

    private static TileFloorState StateOf(TilemapData? data, IReadOnlyList<int> floors)
    {
        if (data is null) return TileFloorState.Normal;
        if (floors.Any(data.IsFloorLocked)) return TileFloorState.Locked;
        if (floors.Any(f => !data.ShowsFloor(f))) return TileFloorState.Hidden;
        return TileFloorState.Normal;
    }
}
