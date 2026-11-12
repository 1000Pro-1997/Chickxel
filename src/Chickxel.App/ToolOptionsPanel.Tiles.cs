using Chickxel.App.Localization;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

// 도구 설정의 타일 묶음: 타일맵 레이어를 고르면 픽셀 도구 묶음 대신 보인다('지금 찍을 것' 카드, 층, 뒤집기·회전, 무작위, 칸 격자).
public sealed partial class ToolOptionsPanel
{
    private Border _tileBox = null!;
    private TextBlock _tileHint = null!;
    private TilePickCard _tilePick = null!;
    private ComboBox _tileFloor = null!;
    private ToggleButton _tileFlipX = null!, _tileFlipY = null!, _tileRotate = null!;
    private CheckBox _tileRandom = null!, _tileCells = null!;
    private Control _tileTransformRow = null!, _tileRandomRow = null!, _tileFloorRow = null!;
    /// <summary>층 칸 차례(0 = 타일 기본, 그다음 타일셋 층 Id).</summary>
    private readonly List<int?> _tileFloorIds = new();

    private Border BuildTileOptions()
    {
        _tileHint = Dim("");
        _tileHint.TextWrapping = TextWrapping.Wrap;
        _tileFloor = Ui.Combo(150);
        _tileFloor.Focusable = false;
        _tileFloor.SelectionChanged += (_, _) =>
        {
            if (_updating || _tileFloor.SelectedIndex < 0 || _tileFloor.SelectedIndex >= _tileFloorIds.Count) return;
            _ctx.TileFloor = _tileFloorIds[_tileFloor.SelectedIndex];
        };
        _tileFloor.Tip(UiStrings.TileOptions_FloorTip);
        _tileFloorRow = Ui.Row(Ui.Label(UiStrings.TileOptions_Floor), _tileFloor);
        _tileFlipX = TextToggle(VectorIcon.FlipLeftRight, UiStrings.TileOptions_FlipXTip, _ => _ctx.TileTransform ^= TileTransform.FlipX);
        _tileFlipY = TextToggle(VectorIcon.FlipUpDown, UiStrings.TileOptions_FlipYTip, _ => _ctx.TileTransform ^= TileTransform.FlipY);
        _tileRotate = TextToggle(VectorIcon.RotateCw, UiStrings.TileOptions_RotateTip, _ => _ctx.TileTransform ^= TileTransform.Rotate90);
        var reset = Ui.IconButton(VectorIcon.Undo2, UiStrings.TileOptions_ResetTip, (_, _) => _ctx.TileTransform = TileTransform.None, size: 13);
        reset.Focusable = false;
        _tileTransformRow = Ui.Row(Ui.Label(UiStrings.TileOptions_Transform), _tileFlipX, _tileFlipY, _tileRotate, reset);
        _tileRandom = Check(UiStrings.TileOptions_Random, UiStrings.TileOptions_RandomTip, v => _ctx.TileRandom = v);
        _tileRandom.Content = Ui.IconLabel(VectorIcon.Shuffle, UiStrings.TileOptions_Random, iconAfter: true);
        _tileRandomRow = _tileRandom;
        _tileCells = Check(UiStrings.TileOptions_Grid, UiStrings.TileOptions_GridTip, v => _ctx.ShowTileCells = v);
        _tilePick = new TilePickCard(_ctx, 32);
        _tileBox = Ui.Group(UiStrings.TileOptions_Title, Ui.Stack(_tilePick, _tileHint, _tileFloorRow, _tileTransformRow, _tileRandomRow, _tileCells));
        _ctx.TileStateChanged += (_, _) => { if (_tileBox.IsVisible) RefreshTiles(); };
        _ctx.CurrentChanged += (_, _) => Refresh();
        return _tileBox;
    }

    /// <summary>타일 도구 이름(제목).</summary>
    internal static string TileToolName(TileTool tool) => tool switch
    {
        TileTool.Brush => UiStrings.TileTool_Brush,
        TileTool.Eraser => UiStrings.TileTool_Eraser,
        TileTool.Rect => UiStrings.TileTool_Rect,
        TileTool.Fill => UiStrings.TileTool_Fill,
        TileTool.Picker => UiStrings.TileTool_Picker,
        TileTool.Select => UiStrings.TileTool_Select,
        _ => UiStrings.TileTool_AutoTile,
    };

    /// <summary>타일 도구 쓰는 법(한두 문장).</summary>
    internal static string TileToolHint(TileTool tool) => tool switch
    {
        TileTool.Brush => UiStrings.TileTool_BrushHint,
        TileTool.Eraser => UiStrings.TileTool_EraserHint,
        TileTool.Rect => UiStrings.TileTool_RectHint,
        TileTool.Fill => UiStrings.TileTool_FillHint,
        TileTool.Picker => UiStrings.TileTool_PickerHint,
        TileTool.Select => UiStrings.TileTool_SelectHint,
        _ => UiStrings.TileTool_AutoTileHint,
    };

    /// <summary>타일맵 화면이면 타일 묶음만 보이고 true.</summary>
    private bool RefreshTileMode()
    {
        bool tile = _ctx.IsTilemapSelected && !_ctx.IsTransforming && !_ctx.IsCanvasResizing;
        _tileBox.IsVisible = tile;
        if (!tile) return false;
        foreach (var box in new Control[] { _shapeBox, _brushBox, _pencilGradientBox, _shapePenBox, _fillBox, _gradientBox, _pickerBox, _dodgeBox, _eraserBox, _ditherBox, _textBox, _optionsBox, _mirrorBox, _frameRangeBox, _pivotBox, _sliceBox, _hint })
            box.IsVisible = false;
        _title.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_ToolOptionsPanel_Refresh_2AA5BFEE57, TileToolName(_ctx.TileTool));
        RefreshTiles();
        return true;
    }

    private void RefreshTiles()
    {
        var tool = _ctx.TileTool;
        var ts = _ctx.CurrentTileset;
        bool stamping = tool is TileTool.Brush or TileTool.Rect or TileTool.Fill;
        _tileHint.Text = TileToolHint(tool);
        _tileFloorRow.IsVisible = stamping || tool == TileTool.Eraser;
        _tileTransformRow.IsVisible = stamping;
        _tileRandomRow.IsVisible = stamping;
        _updating = true;
        try
        {
            _tileFloorIds.Clear();
            _tileFloorIds.Add(null);
            var names = new List<string> { UiStrings.TileOptions_FloorAuto };
            foreach (var f in ts?.Floors ?? [])
            {
                _tileFloorIds.Add(f.Id);
                names.Add(f.Name);
            }
            _tileFloor.ItemsSource = names;
            _tileFloor.SelectedIndex = Math.Max(0, _tileFloorIds.IndexOf(_ctx.TileFloor));
            var t = _ctx.TileTransform;
            _tileFlipX.IsChecked = (t & TileTransform.FlipX) != 0;
            _tileFlipY.IsChecked = (t & TileTransform.FlipY) != 0;
            _tileRotate.IsChecked = (t & TileTransform.Rotate90) != 0;
            _tileRotate.IsEnabled = ts?.CanRotate ?? false;
            _tileRandom.IsChecked = _ctx.TileRandom;
            _tileCells.IsChecked = _ctx.ShowTileCells;
        }
        finally
        {
            _updating = false;
        }
    }
}
