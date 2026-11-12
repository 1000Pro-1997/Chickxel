using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>시트 그림에서 타일 가져오기 설정: 시작 여백, 칸 사이 간격, 빈 칸·같은 타일 빼기, 들어갈 층.</summary>
public sealed class TileImportDialog : DialogBase
{
    private readonly NumericUpDown _offX, _offY, _gapX, _gapY;
    private readonly CheckBox _skipEmpty, _skipDup;
    private readonly ComboBox _floor;
    private readonly Tileset _ts;

    private TileImportDialog(Tileset ts, Size sheet) : base(UiStrings.TileImport_Title, UiStrings.TileImport_Ok)
    {
        _ts = ts;
        _offX = Ui.Num(0, 4096, 0, 56).Unit("px");
        _offY = Ui.Num(0, 4096, 0, 56).Unit("px");
        _gapX = Ui.Num(0, 4096, 0, 56).Unit("px");
        _gapY = Ui.Num(0, 4096, 0, 56).Unit("px");
        _skipEmpty = Ui.Check(UiStrings.TileImport_SkipEmpty, true);
        _skipDup = Ui.Check(UiStrings.TileImport_SkipDuplicates, true);
        _floor = Ui.Combo(150, ts.Floors.Select(f => f.Name).ToArray());
        if (ts.Floors.Length > 0) _floor.SelectedIndex = 0;
        Body.Children.Add(Ui.Label(string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.TileImport_Summary, sheet.Width, sheet.Height, ts.CellWidth, ts.CellHeight)));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.TileImport_Offset), _offX, _offY));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.TileImport_Gap), _gapX, _gapY));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.TileOptions_Floor), _floor));
        Body.Children.Add(_skipEmpty);
        Body.Children.Add(_skipDup);
    }

    /// <summary>설정대로 가져온 타일셋과 더한 수. 취소하면 null.</summary>
    public static (Tileset Tileset, int Added)? Run(Visual? owner, Tileset ts, PixelImage sheet)
    {
        var dlg = new TileImportDialog(ts, sheet.Size);
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        int floor = dlg._floor.SelectedIndex >= 0 && dlg._floor.SelectedIndex < ts.Floors.Length ? ts.Floors[dlg._floor.SelectedIndex].Id : 0;
        return TilesetEdit.ImportSheet(ts, sheet, (int)dlg._offX.NumValue(), (int)dlg._offY.NumValue(), (int)dlg._gapX.NumValue(), (int)dlg._gapY.NumValue(),
            dlg._skipEmpty.On(), dlg._skipDup.On(), floor);
    }
}
