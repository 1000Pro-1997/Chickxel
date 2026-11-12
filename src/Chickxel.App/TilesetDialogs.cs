using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 타일맵 레이어가 쓸 타일셋 고르기: 기본 타일셋(앱에 든 것 — 사본), 새 빈 타일셋(이름·칸 크기), 프로젝트에 있는 타일셋, 타일셋 파일 열기.
/// </summary>
public sealed class TilesetChoiceDialog : DialogBase
{
    private readonly RadioButton _builtIn, _new, _existing;
    private readonly ComboBox _builtIns;
    private readonly TextBox _name;
    private readonly NumericUpDown _cellW, _cellH;
    private readonly ComboBox _list;
    private readonly IReadOnlyList<Tileset> _tilesets;
    private Tileset? _opened;

    private TilesetChoiceDialog(IReadOnlyList<Tileset> tilesets, string title) : base(title)
    {
        _tilesets = tilesets;
        _builtIn = new RadioButton { Content = UiStrings.TilesetChoice_BuiltIn, GroupName = "tileset", IsChecked = true, Margin = new Thickness(3) };
        _builtIns = Ui.Combo(220, BuiltInTilesets.All.Select(e => e.Name).ToArray());
        _builtIns.SelectedIndex = 0;
        _builtIns.SelectionChanged += (_, _) => _builtIn.IsChecked = true;
        _new = new RadioButton { Content = UiStrings.TilesetChoice_New, GroupName = "tileset", Margin = new Thickness(3) };
        _name = Ui.Text(UiStrings.TilesetChoice_DefaultName, 200);
        _cellW = Ui.Num(1, Tileset.MaxCellSize, 16, 60).Presets(8, 16, 24, 32, 48, 64).Unit("px");
        _cellH = Ui.Num(1, Tileset.MaxCellSize, 16, 60).Presets(8, 16, 24, 32, 48, 64).Unit("px");
        _existing = new RadioButton { Content = UiStrings.TilesetChoice_Existing, GroupName = "tileset", Margin = new Thickness(3), IsEnabled = tilesets.Count > 0 };
        _list = Ui.Combo(220, tilesets.Select(t => t.Name.Length > 0 ? t.Name : UiStrings.TilesetChoice_Unnamed).ToArray());
        _list.IsEnabled = tilesets.Count > 0;
        if (tilesets.Count > 0) _list.SelectedIndex = 0;
        _list.SelectionChanged += (_, _) => _existing.IsChecked = true;
        var open = Ui.Button(VectorIcon.FolderOpen, UiStrings.TilesetChoice_OpenFile, (_, _) => OpenFile());
        Body.Children.Add(_builtIn);
        Body.Children.Add(Ui.Row(_builtIns, Ui.InfoBadge(UiStrings.TilesetChoice_BuiltInInfo)));
        Body.Children.Add(_new);
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.TilesetChoice_Name), _name));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.TilesetChoice_CellSize), _cellW, Ui.Label("×"), _cellH, Ui.InfoBadge(UiStrings.TilesetChoice_CellSizeInfo)));
        Body.Children.Add(_existing);
        Body.Children.Add(Ui.Row(_list));
        Body.Children.Add(Ui.Row(open));
    }

    private void OpenFile()
    {
        var path = Ui.OpenFile(this, UiStrings.TilesetChoice_OpenFile, null, new FileFilter(UiStrings.Tileset_FileFilter, "*" + Tileset.FileExtension));
        if (path is null) return;
        try
        {
            _opened = TilesetIO.Load(path);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
    }

    private Tileset? Result()
    {
        if (_opened is not null) return _opened;
        if (_existing.IsChecked == true && _list.SelectedIndex >= 0) return _tilesets[_list.SelectedIndex];
        if (_builtIn.IsChecked == true && _builtIns.SelectedIndex >= 0) return BuiltInTilesets.All[_builtIns.SelectedIndex].Create();
        var name = string.IsNullOrWhiteSpace(_name.Text) ? UiStrings.TilesetChoice_DefaultName : _name.Text.Trim();
        return Tileset.Create(name, (int)_cellW.NumValue(), (int)_cellH.NumValue());
    }

    /// <summary>고른 타일셋(파일에서 연 것이면 그 파일 경로를 가짐). 취소하면 null.</summary>
    public static Tileset? Run(Visual? owner, SpriteProject project, string title)
    {
        var dlg = new TilesetChoiceDialog(project.Tilesets.Values.OrderBy(t => t.Name, StringComparer.CurrentCulture).ToList(), title);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Result() : null;
    }
}

/// <summary>타일셋 설정: 이름, 칸 크기(타일이 없을 때만 바꿀 수 있음).</summary>
public sealed class TilesetSettingsDialog : DialogBase
{
    private readonly TextBox _name;
    private readonly NumericUpDown _cellW, _cellH;

    private TilesetSettingsDialog(Tileset ts) : base(UiStrings.TilesetSettings_Title)
    {
        _name = Ui.Text(ts.Name, 200);
        _cellW = Ui.Num(1, Tileset.MaxCellSize, ts.CellWidth, 60).Unit("px");
        _cellH = Ui.Num(1, Tileset.MaxCellSize, ts.CellHeight, 60).Unit("px");
        _cellW.IsEnabled = _cellH.IsEnabled = ts.Tiles.Length == 0;
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.TilesetChoice_Name), _name));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.TilesetChoice_CellSize), _cellW, Ui.Label("×"), _cellH,
            Ui.InfoBadge(ts.Tiles.Length == 0 ? UiStrings.TilesetChoice_CellSizeInfo : UiStrings.TilesetSettings_CellLocked)));
    }

    public static Tileset? Run(Visual? owner, Tileset ts)
    {
        var dlg = new TilesetSettingsDialog(ts);
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        var name = string.IsNullOrWhiteSpace(dlg._name.Text) ? ts.Name : dlg._name.Text.Trim();
        return ts with { Name = name, CellWidth = (int)dlg._cellW.NumValue(), CellHeight = (int)dlg._cellH.NumValue() };
    }
}

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
