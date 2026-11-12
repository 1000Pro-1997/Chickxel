using Chickxel.App.Localization;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

// 타일맵 레이어 모드: 타일맵 레이어를 고르면 도구 막대(타일 도구)·왼쪽 패널(타일셋)·도구 설정·픽셀 전용 메뉴가 바뀐다.
public sealed partial class MainForm
{
    /// <summary>타일 도구 막대 단추: 도구, 아이콘 파일, 같은 단축키를 쓰는 픽셀 도구(그 도구 명령의 키 = 이 도구의 키).</summary>
    private static readonly (TileTool Tool, string Icon, ToolKind Key)[] TileToolDefs =
    [
        (TileTool.Brush, "tile_brush", ToolKind.Pencil),
        (TileTool.AutoTile, "autotile", ToolKind.GradientPencil),
        (TileTool.Eraser, "eraser", ToolKind.Eraser),
        (TileTool.Rect, "tile_rect", ToolKind.Shape),
        (TileTool.Fill, "fill", ToolKind.Fill),
        (TileTool.Picker, "picker", ToolKind.Picker),
        (TileTool.Select, "tile_select", ToolKind.Select),
    ];

    /// <summary>타일맵 모드에서 픽셀 도구 명령(단축키)이 고를 타일 도구. 없으면 그 도구는 쓸 수 없다.</summary>
    private static TileTool? TileToolFor(ToolKind tool) => tool switch
    {
        ToolKind.Pencil or ToolKind.ShapePen => TileTool.Brush,
        ToolKind.GradientPencil => TileTool.AutoTile,
        ToolKind.Eraser => TileTool.Eraser,
        ToolKind.Shape => TileTool.Rect,
        ToolKind.Fill or ToolKind.Gradient => TileTool.Fill,
        ToolKind.Picker => TileTool.Picker,
        ToolKind.Select or ToolKind.EllipseSelect or ToolKind.Lasso or ToolKind.MagicWand => TileTool.Select,
        _ => null,
    };

    private TilesetPanel _tilesetPanel = null!;
    private StackPanel _tileToolBar = null!;
    private readonly Dictionary<TileTool, ToggleButton> _tileButtons = new();
    /// <summary>타일맵 레이어에서는 쓸 수 없는 픽셀 전용 명령(메뉴 항목).</summary>
    private readonly List<MenuItem> _pixelItems = new();
    private bool _tileModeShown;

    /// <summary>픽셀 그림에만 쓰는 명령: 타일맵 레이어를 고르면 흐려지고, 단축키로 불러도 알림만 보인다.</summary>
    private MenuItem PixelDocCmd(string id, string text, Action action, string? keyText = null, KeyScope scope = KeyScope.Global, params (Key, KeyModifiers)[] keys)
    {
        var mi = DocCmd(id, text, () =>
        {
            if (_ctx.IsTilemapSelected) ShowMessage(UiStrings.Tile_PixelOnly);
            else action();
        }, keyText, scope, keys);
        _pixelItems.Add(mi);
        return mi;
    }

    /// <summary>도구 명령(메뉴·단축키): 타일맵 레이어면 같은 키의 타일 도구로.</summary>
    private void PickTool(ToolKind tool)
    {
        if (_ctx.IsTilemapSelected)
        {
            if (TileToolFor(tool) is { } tile) _ctx.TileTool = tile;
            else ShowMessage(string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tile_ToolUnavailable, ToolName(tool)));
            UpdateToolUi();
            return;
        }
        _ctx.Tool = tool;
        UpdateToolUi();
    }

    private void TurnTileStamp(TileTransform step)
    {
        _ctx.TurnStamp(step);
        ShowMessage(UiStrings.Tile_StampTurned);
    }

    /// <summary>칸 선택: 캔버스를 덮는 칸과 맵 전체.</summary>
    private void SelectAllTiles()
    {
        if (_ctx.Project is not { } p || _ctx.CurrentTiles is not { } data || _ctx.CurrentTileset is not { } ts) return;
        var a = data.CellAt(ts, 0, 0);
        var b = data.CellAt(ts, p.Width - 1, p.Height - 1);
        var r = Rectangle.FromLTRB(a.X, a.Y, b.X + 1, b.Y + 1);
        var used = data.Map.CellBounds(ts);
        if (!used.IsEmpty) r = Rectangle.Union(r, used);
        _ctx.TileTool = TileTool.Select;
        _ctx.TileSelection = r;
        UpdateToolUi();
    }

    /// <summary>도구 막대의 타일 도구 단추(처음엔 숨김).</summary>
    private StackPanel BuildTileToolBar()
    {
        _tileToolBar = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, IsVisible = false };
        foreach (var (tool, icon, _) in TileToolDefs)
        {
            var t = tool;
            var b = ToolBarButton(Icons.ForTile(icon), ToolOptionsPanel.TileToolName(tool));
            b.Click += (_, _) => { _ctx.TileTool = t; UpdateToolUi(); _canvas.Focus(); };
            _tileButtons[tool] = b;
            _tileToolBar.Children.Add(b);
        }
        return _tileToolBar;
    }

    /// <summary>타일 도구 단추 풍선: 이름 (단축키) + 쓰는 법.</summary>
    private void RefreshTileToolTips()
    {
        foreach (var (tool, _, key) in TileToolDefs)
            if (_tileButtons.TryGetValue(tool, out var b))
            {
                var keyText = ToolKeyText(key);
                var name = ToolOptionsPanel.TileToolName(tool);
                b.Tip((keyText.Length > 0 ? $"{name} ({keyText})" : name) + "\n" + ToolOptionsPanel.TileToolHint(tool));
            }
    }

    /// <summary>
    /// 현재 레이어에 맞춰 화면을 바꾼다: 타일맵 레이어면 도구 막대 = 타일 도구, 왼쪽 패널 = 타일셋, 픽셀 전용 메뉴는 흐림. 그 밖은 원래대로.
    /// </summary>
    private void UpdateTileMode()
    {
        bool tile = _ctx.IsTilemapSelected;
        foreach (var mi in _pixelItems) mi.IsEnabled = _ctx.HasProject && !tile;
        if (tile == _tileModeShown) return;
        _tileModeShown = tile;
        if (tile) _ctx.CommitFloating();
        foreach (var slot in _toolSlots) slot.IsVisible = !tile;
        _tileToolBar.IsVisible = tile;
        _colors.IsVisible = !tile;
        _tilesetPanel.IsVisible = tile;
        if (tile) RefreshTileToolTips();
    }

    /// <summary>도구 이름·안내 줄과 단추 눌림(타일맵 모드).</summary>
    private bool UpdateTileToolUi()
    {
        if (!_ctx.IsTilemapSelected) return false;
        var tool = _ctx.TileTool;
        foreach (var (t, b) in _tileButtons) b.IsChecked = t == tool;
        var key = TileToolDefs.First(d => d.Tool == tool).Key;
        var name = ToolOptionsPanel.TileToolName(tool);
        _cbToolName.Text = Ui.Shortcut(ToolKeyText(key) is { Length: > 0 } k ? $"{name} ({k})" : name);
        _cbHint.Text = Ui.Shortcut(ToolOptionsPanel.TileToolHint(tool));
        foreach (var (t, mi) in _toolMenuItems) mi.IsChecked = TileToolFor(t) == tool && TileToolDefs.Any(d => d.Key == t);
        return true;
    }
}
