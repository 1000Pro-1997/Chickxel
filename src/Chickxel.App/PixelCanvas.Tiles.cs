using System.Runtime.CompilerServices;
using Chickxel.App.Localization;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

// 타일맵 레이어 화면: 타일 도구 입력(찍기·지우기·사각형·채우기·스포이드·칸 선택·자동 타일), 칸 격자,
// 찍을 것 미리보기(붓 그림·자동 타일은 찍으면 붙을 실제 모양), 잠긴·숨긴 층 경고, 칸 정보(칸 좌표·층·그 칸의 타일).
public sealed partial class PixelCanvas
{
    private static readonly IPen TileGridPen = AppTheme.Pen(Color.FromArgb(70, 255, 255, 255), 1);
    private static readonly IPen TileCursorPen = AppTheme.Pen(AppTheme.AccentColor, 1.5);
    private static readonly IPen TileErasePen = AppTheme.Pen(Color.FromArgb(230, 255, 90, 90), 1.5);
    private static readonly IPen TileSelectionPen = AppTheme.Pen(Color.FromRgb(90, 170, 255), 1.5, Dash);
    private static readonly IBrush TileRectBrush = new SolidColorBrush(Color.FromArgb(50, 90, 170, 255));
    private static readonly IPen TileWarnPen = AppTheme.Pen(AppTheme.WarningColor, 2, Dash);
    private static readonly IBrush TileHudBrush = new SolidColorBrush(Color.FromArgb(215, 22, 22, 26));

    /// <summary>칸 정보 글과 지금 찍을 것을 다시 만들지 않게 담아 두는 열쇠(맵·타일셋·칸·붓 상태).</summary>
    private readonly record struct TileHudKey(TilemapData Data, Tileset Tileset, Point Cell, TileTool Tool, TileStamp? Stamp, int AutoSet, bool Auto, int? Floor, TileTransform Transform, bool Random);

    /// <summary>마우스가 가리키는 칸(타일맵 화면).</summary>
    private Point? _tileHover;
    private Point _tileDragStart, _tileDragCell;
    private bool _tileErase;
    private TileTool _tileDragTool;
    /// <summary>타일 그림 → 화면 비트맵(그림은 만든 뒤 바뀌지 않으므로 그림마다 한 번).</summary>
    private readonly ConditionalWeakTable<PixelImage, WriteableBitmap> _tileBitmaps = new();

    private WriteableBitmap TileBitmap(PixelImage image) => _tileBitmaps.GetValue(image, ImageUtil.ToBitmap);

    // 자동 타일 미리보기: (맵, 세트, 타일셋, 칸)에 찍으면 바뀔 칸들(층 아래 → 위)
    private (TileMap Map, AutoTileSet Set, Tileset Tileset, Point Cell, List<(Point At, TileCell Cell)> Changes)? _autoGhost;
    // 칸 정보: 열쇠, 지금 찍을 것, 글, 경고 글
    private (TileHudKey Key, TilePick Pick, string Text, string? Warning)? _tileHud;

    /// <summary>타일맵 화면에서 누름: 끌기를 시작했으면 true(포인터를 잡음).</summary>
    private bool BeginTileDrag(MouseButton button, Point px, KeyModifiers mods)
    {
        if (_ctx.TileCellAt(px) is not { } cell)
        {
            Message?.Invoke(this, UiStrings.Tile_NoTileset);
            return false;
        }
        var tool = (mods & KeyModifiers.Alt) != 0 ? TileTool.Picker : _ctx.TileTool;
        _tileErase = button == MouseButton.Right;
        _tileDragTool = tool;
        _tileDragStart = _tileDragCell = cell;
        switch (tool)
        {
            case TileTool.Picker:
                if (!_ctx.TilePick(cell)) Message?.Invoke(this, UiStrings.Tile_NothingToPick);
                return false;
            case TileTool.Fill:
                if (!_tileErase && !_ctx.UsesAutoTileSource && _ctx.TileStamp is null) { Message?.Invoke(this, UiStrings.Tile_PickTileFirst); return false; }
                _ctx.TileFill(cell, _tileErase);
                return false;
            case TileTool.Brush:
            case TileTool.Eraser:
            case TileTool.AutoTile:
                if (tool == TileTool.Brush && !_tileErase && _ctx.TileStamp is null) { Message?.Invoke(this, UiStrings.Tile_PickTileFirst); return false; }
                if (tool == TileTool.AutoTile && _ctx.CurrentAutoTileSet is null) { Message?.Invoke(this, UiStrings.Tile_PickAutoTileFirst); return false; }
                if (_ctx.BeginTileStroke(cell) is { } why) { Message?.Invoke(this, why); return false; }
                _ctx.TileStrokeAt(cell, _tileErase);
                _drag = DragMode.Tile;
                return true;
            case TileTool.Rect:
                if (!_tileErase && !_ctx.UsesAutoTileSource && _ctx.TileStamp is null) { Message?.Invoke(this, UiStrings.Tile_PickTileFirst); return false; }
                _drag = DragMode.Tile;
                InvalidateVisual();
                return true;
            case TileTool.Select:
                if (_tileErase) { _ctx.TileSelection = null; return false; }
                _drag = DragMode.Tile;
                InvalidateVisual();
                return true;
        }
        return false;
    }

    private void DragTile(Point px)
    {
        if (_ctx.TileCellAt(px) is not { } cell || cell == _tileDragCell) return;
        if (_tileDragTool is TileTool.Brush or TileTool.Eraser or TileTool.AutoTile)
            foreach (var c in Raster.Line(_tileDragCell, cell).Skip(1)) _ctx.TileStrokeAt(c, _tileErase);
        _tileDragCell = cell;
        _tileHover = cell;
        InvalidateVisual();
    }

    private void EndTileDrag()
    {
        switch (_tileDragTool)
        {
            case TileTool.Brush:
            case TileTool.Eraser:
            case TileTool.AutoTile:
                _ctx.EndTileStroke();
                break;
            case TileTool.Rect:
                _ctx.TileRect(TileDragRect, _tileErase);
                break;
            case TileTool.Select:
                _ctx.TileSelection = _tileDragCell == _tileDragStart && _ctx.TileSelection is { } old && old.Contains(_tileDragStart) ? null : TileDragRect;
                break;
        }
        InvalidateVisual();
    }

    /// <summary>끌기 시작 칸과 지금 칸을 감싼 칸 사각형.</summary>
    private Rectangle TileDragRect => Rectangle.FromLTRB(Math.Min(_tileDragStart.X, _tileDragCell.X), Math.Min(_tileDragStart.Y, _tileDragCell.Y),
        Math.Max(_tileDragStart.X, _tileDragCell.X) + 1, Math.Max(_tileDragStart.Y, _tileDragCell.Y) + 1);

    /// <summary>타일 끌기 중 반대쪽 버튼: 찍던 것을 버린다.</summary>
    private void CancelTileDrag()
    {
        _drag = DragMode.None;
        if (_tileDragTool is TileTool.Brush or TileTool.Eraser or TileTool.AutoTile) _ctx.CancelTileStroke();
        Message?.Invoke(this, UiStrings.S_PixelCanvas_CancelPixelStroke_53972D288A);
        InvalidateVisual();
    }

    /// <summary>마우스가 가리키는 칸이 바뀌면 다시 그린다(붓 미리보기).</summary>
    private void UpdateTileHover(Point px)
    {
        var cell = _ctx.TileCellAt(px);
        if (cell == _tileHover) return;
        _tileHover = cell;
        InvalidateVisual();
    }

    // ───────────── 그리기 ─────────────

    /// <summary>타일맵 화면 위에 칸 격자·칸 선택·끄는 사각형·찍을 것 미리보기·칸 정보를 그린다.</summary>
    private void DrawTileOverlay(DrawingContext g)
    {
        if (_ctx.CurrentTiles is not { } data || _ctx.CurrentTileset is not { } ts || _peekPrevious) return;
        if (_ctx.ShowTileCells) DrawTileCells(g, data, ts);
        if (_ctx.TileSelection is { } sel) g.DrawRectangle(null, TileSelectionPen, ToRect(ImageToScreen(data.PixelRect(ts, sel))));
        bool dragging = _drag == DragMode.Tile;
        // 마우스가 캔버스를 떠나면 미리보기도 감춘다(끄는 중은 그대로)
        if (_tileHover is not { } cell || !dragging && !IsPointerOver) return;
        var hud = TileHud(data, ts, cell);
        if (dragging && _tileDragTool is TileTool.Rect or TileTool.Select)
        {
            var r = ToRect(ImageToScreen(data.PixelRect(ts, TileDragRect)));
            g.FillRectangle(TileRectBrush, r);
            g.DrawRectangle(null, _tileErase ? TileErasePen : TileCursorPen, r);
        }
        else DrawTileCursor(g, data, ts, cell, hud.Pick);
        DrawTileHud(g, hud.Text, hud.Warning, hud.Pick.FloorState);
    }

    /// <summary>마우스 아래 칸: 지우개·스포이드·칸 선택은 칸 테두리, 자동 타일은 찍으면 붙을 모양, 붓은 붓 그림(반투명). 잠긴·숨긴 층이면 경고 색.</summary>
    private void DrawTileCursor(DrawingContext g, TilemapData data, Tileset ts, Point cell, TilePick pick)
    {
        bool dragging = _drag == DragMode.Tile;
        bool alt = !dragging && (_mods & KeyModifiers.Alt) != 0;
        var tool = dragging ? _tileDragTool : alt ? TileTool.Picker : _ctx.TileTool;
        bool erasing = dragging ? _tileErase : tool == TileTool.Eraser;
        bool locked = pick.FloorState == TileFloorState.Locked || _ctx.TileEditBlocker() is not null;
        bool warn = locked || pick.FloorState == TileFloorState.Hidden;
        var one = ToRect(ImageToScreen(data.PixelRect(ts, new Rectangle(cell, new Size(1, 1)))));
        if (erasing || tool is TileTool.Picker or TileTool.Select)
        {
            // 지우기는 고정한 층(자동 타일 붓이면 세트의 층)이 잠겼을 때만 막힘
            int? eraseFloor = tool == TileTool.AutoTile ? _ctx.CurrentAutoTileSet?.Floor : _ctx.TileFloor;
            bool eraseLocked = erasing && (_ctx.TileEditBlocker() is not null || eraseFloor is { } ef && data.IsFloorLocked(ef));
            g.DrawRectangle(null, eraseLocked ? TileWarnPen : erasing ? TileErasePen : TileCursorPen, one);
            if (eraseLocked) DrawTileWarnIcon(g, one, TileFloorState.Locked, true);
            return;
        }
        // 화면 시각: 지금 프레임의 타일 시각(프로젝트 시계 빼고) + 화면 시계 — 캔버스 합성과 같은 그림
        long time = _ctx.Project is { } tp ? tp.TileTimeMs(_ctx.CurrentFrame) - tp.TileClockMs + _ctx.TileClockMs : 0;
        if (tool == TileTool.AutoTile || tool is TileTool.Rect or TileTool.Fill && _ctx.UsesAutoTileSource)
        {
            if (!locked && _ctx.CurrentAutoTileSet is { } set)
            {
                var changes = AutoGhost(ts, data, set, cell);
                if (changes.Count > 0)
                    using (ImageUtil.Pixelated(g))
                    using (g.PushOpacity(0.8))
                        foreach (var (at, c) in changes) DrawTileGhost(g, data, ts, at, c, time);
            }
            g.DrawRectangle(null, warn ? TileWarnPen : TileCursorPen, one);
            if (warn) DrawTileWarnIcon(g, one, pick.FloorState, locked);
            return;
        }
        if (_ctx.EffectiveStamp is not { } stamp)
        {
            g.DrawRectangle(null, TileCursorPen, one);
            return;
        }
        // 무작위 찍기는 칸 하나만 보인다(어느 타일일지 모름).
        if (_ctx.TileRandom && stamp.Cells.Count > 1)
        {
            g.DrawRectangle(null, warn ? TileWarnPen : TileCursorPen, one);
            if (warn) DrawTileWarnIcon(g, one, pick.FloorState, locked);
            return;
        }
        if (!locked)
            using (ImageUtil.Pixelated(g))
            using (g.PushOpacity(0.65))
                foreach (var c in stamp.Cells) DrawTileGhost(g, data, ts, new Point(cell.X + c.Dx, cell.Y + c.Dy), c.Cell, time);
        var box = stamp.Bounds(ts);
        box.Offset(cell);
        var boxRect = ToRect(ImageToScreen(data.PixelRect(ts, box)));
        g.DrawRectangle(null, warn ? TileWarnPen : TileCursorPen, boxRect);
        if (warn) DrawTileWarnIcon(g, boxRect, pick.FloorState, locked);
    }

    /// <summary>칸 <paramref name="at"/>에 놓일 타일 그림 하나(시각 <paramref name="time"/>의 그림).</summary>
    private void DrawTileGhost(DrawingContext g, TilemapData data, Tileset ts, Point at, TileCell cell, long time)
    {
        if (ts.Placement(at, cell, time) is not { } placed) return;
        var rect = placed.Rect;
        rect.Offset(data.Origin);
        var bmp = TileBitmap(placed.Image);
        g.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), ToRect(ImageToScreen(rect)));
    }

    /// <summary>칸 테두리 오른쪽 위에 잠김(자물쇠)·숨김(감은 눈) 표시.</summary>
    private static void DrawTileWarnIcon(DrawingContext g, Rect box, TileFloorState state, bool locked)
    {
        const double size = 16;
        var chip = new Rect(box.Right - size / 2, box.Y - size / 2, size, size);
        g.FillRectangle(TileHudBrush, chip, 3);
        VectorIcons.Draw(g, locked ? VectorIcon.Lock : state == TileFloorState.Hidden ? VectorIcon.EyeOff : VectorIcon.CircleAlert, chip, 12, AppTheme.WarningBrush);
    }

    /// <summary>자동 타일 세트로 칸 <paramref name="cell"/>을 찍으면 바뀔 칸들(그 칸과 둘레, 보이는 층, 아래 층부터). 같은 맵·칸이면 다시 셈하지 않음.</summary>
    private List<(Point At, TileCell Cell)> AutoGhost(Tileset ts, TilemapData data, AutoTileSet set, Point cell)
    {
        if (_autoGhost is { } c && ReferenceEquals(c.Map, data.Map) && ReferenceEquals(c.Set, set) && ReferenceEquals(c.Tileset, ts) && c.Cell == cell) return c.Changes;
        var before = data.Map;
        var after = AutoTiler.Paint(ts, before, set, [cell]);
        var changes = new List<(Point At, TileCell Cell)>();
        if (!ReferenceEquals(after, before))
            foreach (var floor in ts.Floors)
            {
                if (!data.ShowsFloor(floor.Id)) continue;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var at = new Point(cell.X + dx, cell.Y + dy);
                        if (after.Get(floor.Id, at) is { } now && now != before.Get(floor.Id, at)) changes.Add((at, now));
                    }
            }
        _autoGhost = (before, set, ts, cell, changes);
        return changes;
    }

    /// <summary>지금 찍을 것과 칸 정보 글(칸 좌표 · 그 칸의 보이는 맨 위 타일과 층), 찍을 층이 잠기거나 숨었으면 경고 글.</summary>
    private (TilePick Pick, string Text, string? Warning) TileHud(TilemapData data, Tileset ts, Point cell)
    {
        var key = new TileHudKey(data, ts, cell, _ctx.TileTool, _ctx.TileStamp, _ctx.AutoTileSet, _ctx.UsesAutoTileSource, _ctx.TileFloor, _ctx.TileTransform, _ctx.TileRandom);
        if (_tileHud is { } h && h.Key == key) return (h.Pick, h.Text, h.Warning);
        var pick = TilePick.Describe(_ctx, ts);
        var text = UiStrings.FormatTileHud_Cell(x: cell.X, y: cell.Y) + " · " + (TileInfo(data, ts, cell) ?? UiStrings.TileHud_Empty);
        string? warning = null;
        foreach (int f in pick.Floors)
        {
            var name = ts.FindFloor(f)?.Name ?? "?";
            if (data.IsFloorLocked(f)) { warning = UiStrings.FormatTileHud_FloorLocked(floor: name); break; }
            if (!data.ShowsFloor(f)) warning ??= UiStrings.FormatTileHud_FloorHidden(floor: name);
        }
        _tileHud = (key, pick, text, warning);
        return (pick, text, warning);
    }

    /// <summary>칸에 보이는 맨 위 타일: "층 · 타일 이름"(자동 타일이면 세트 이름도). 빈 칸이면 null.</summary>
    private static string? TileInfo(TilemapData data, Tileset ts, Point cell)
    {
        foreach (int f in data.Map.FloorsTopDown(ts))
        {
            if (!data.ShowsFloor(f) || data.Map.Covering(ts, f, cell) is not { } hit) continue;
            var tile = ts.Find(hit.Cell.Tile) is { } def ? TileVisuals.TileName(def) : UiStrings.FormatTilePick_TileId(id: hit.Cell.Tile);
            if (hit.Cell.AutoTile != 0 && ts.FindAutoTile(hit.Cell.AutoTile) is { } set)
                tile = UiStrings.FormatTileHud_AutoOf(tile: tile, set: set.Name.Length > 0 ? set.Name : UiStrings.TilesetChoice_Unnamed);
            return UiStrings.FormatTileHud_Placed(floor: ts.FindFloor(f)?.Name ?? "?", tile: tile);
        }
        return null;
    }

    /// <summary>캔버스 왼쪽 아래 칸 정보 띠(경고가 있으면 그 줄을 경고 색으로 위에).</summary>
    private void DrawTileHud(DrawingContext g, string text, string? warning, TileFloorState state)
    {
        const double pad = 6, line = 20;
        double y = Bounds.Height - 8 - line;
        DrawHudLine(g, text, null, AppTheme.TextBrush, y);
        if (warning is not null) DrawHudLine(g, warning, state == TileFloorState.Hidden ? VectorIcon.EyeOff : VectorIcon.Lock, AppTheme.WarningBrush, y - line - 3);

        void DrawHudLine(DrawingContext dc, string s, VectorIcon? icon, IBrush brush, double top)
        {
            var size = AppTheme.MeasureText(s, AppTheme.SmallFontSize);
            double iconW = icon is null ? 0 : 16;
            var chip = new Rect(8, top, size.Width + pad * 2 + iconW, line);
            dc.FillRectangle(TileHudBrush, chip, 3);
            if (icon is { } ic) VectorIcons.Draw(dc, ic, new Rect(chip.X + pad - 1, chip.Y, 14, line), 12, brush);
            AppTheme.DrawText(dc, s, new Avalonia.Point(chip.X + pad + iconW, chip.Y + (line - size.Height) / 2), brush, AppTheme.SmallFontSize);
        }
    }

    /// <summary>캔버스 좌표 <paramref name="px"/> 칸의 정보 글("칸 x, y · 층 · 타일 이름") — 타일맵 레이어가 아니면 null(상태 표시줄용).</summary>
    public string? TileInfoAt(Point px)
    {
        if (_ctx.CurrentTiles is not { } data || _ctx.CurrentTileset is not { } ts || _ctx.TileCellAt(px) is not { } cell) return null;
        return UiStrings.FormatTileHud_Cell(x: cell.X, y: cell.Y) + " · " + (TileInfo(data, ts, cell) ?? UiStrings.TileHud_Empty);
    }

    /// <summary>칸 격자(보이는 곳만, 너무 촘촘하면 그리지 않음).</summary>
    private void DrawTileCells(DrawingContext g, TilemapData data, Tileset ts)
    {
        int cw = ts.CellWidth, ch = ts.CellHeight;
        if (cw * _zoom < 4 || ch * _zoom < 4) return;
        // 보이는 이미지 좌표 범위(캔버스 밖 포함)
        var a = ScreenToPixel(new Point(0, 0));
        var b = ScreenToPixel(new Point((int)Bounds.Width, (int)Bounds.Height));
        int x0 = data.Origin.X + FloorDiv(a.X - data.Origin.X, cw) * cw, y0 = data.Origin.Y + FloorDiv(a.Y - data.Origin.Y, ch) * ch;
        for (int x = x0; x <= b.X + 1; x += cw)
        {
            double sx = _offset.X + x * _zoom + 0.5;
            g.DrawLine(TileGridPen, new Avalonia.Point(sx, 0), new Avalonia.Point(sx, Bounds.Height));
        }
        for (int y = y0; y <= b.Y + 1; y += ch)
        {
            double sy = _offset.Y + y * _zoom + 0.5;
            g.DrawLine(TileGridPen, new Avalonia.Point(0, sy), new Avalonia.Point(Bounds.Width, sy));
        }
    }
}
