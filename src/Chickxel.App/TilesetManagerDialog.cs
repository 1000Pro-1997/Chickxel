using Chickxel.App.Localization;
using Chickxel.Core.Tiles;

namespace Chickxel.App;

/// <summary>
/// 타일셋 관리 창(닫기만): 프로젝트가 담은 타일셋마다 그림·이름(고치면 바로 바뀜)·칸 크기·타일 수와 쓰는 곳(지금 레이어에 열림 · 다른 레이어가 씀 · 쓰지 않음),
/// 타일셋 파일로 저장·지우기(어느 레이어도 쓰지 않을 때만). 아래에서 새 빈 타일셋(이름·칸 크기)을 만들거나 타일셋 파일을 프로젝트로 가져온다.
/// 모두 실행취소 한 단계씩이다. Enter는 이름 칸 확정에만 쓴다.
/// </summary>
public sealed class TilesetManagerDialog : DialogBase
{
    private readonly EditorContext _ctx;
    private readonly StackPanel _list = new() { Spacing = 4 };
    private readonly TextBox _newName;
    private readonly NumericUpDown _cellW, _cellH;

    private TilesetManagerDialog(EditorContext ctx) : base(UiStrings.TilesetManager_Title)
    {
        _ctx = ctx;
        CloseOnly();
        Body.Children.Add(new ScrollViewer
        {
            Content = _list,
            MaxHeight = 420,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        var head = Ui.Label(UiStrings.TilesetChoice_New, bold: true);
        head.Margin = new Thickness(3, 10, 3, 2);
        Body.Children.Add(head);
        _newName = Ui.Text(UiStrings.TilesetChoice_DefaultName, 160);
        var cell = ctx.CurrentTileset is { } cur ? new Size(cur.CellWidth, cur.CellHeight) : new Size(16, 16);
        _cellW = Ui.Num(1, Tileset.MaxCellSize, cell.Width, 60).Presets(8, 16, 24, 32, 48, 64).Unit("px");
        _cellH = Ui.Num(1, Tileset.MaxCellSize, cell.Height, 60).Presets(8, 16, 24, 32, 48, 64).Unit("px");
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.TilesetChoice_Name), _newName));
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.TilesetChoice_CellSize), _cellW, Ui.Label("×"), _cellH, Ui.InfoBadge(UiStrings.TilesetChoice_CellSizeInfo),
            Ui.Button(VectorIcon.Plus, UiStrings.TilesetChoice_Create, (_, _) => CreateEmpty())));
        Body.Children.Add(Ui.Row(Ui.Button(VectorIcon.FolderOpen, UiStrings.TilesetManager_ImportFile, (_, _) => ImportFile())));
        BuildList();
    }

    private void BuildList()
    {
        _list.Children.Clear();
        if (_ctx.Project is not { } p) return;
        var open = _ctx.CurrentTiles?.TilesetKeys ?? [];
        foreach (var ts in p.AllTilesets.OrderBy(t => t.Name, StringComparer.CurrentCulture))
        {
            string key = ts.Key;
            var name = Ui.Text(ts.Name, 180);
            void Rename()
            {
                var text = name.Text?.Trim() ?? "";
                if (text.Length > 0 && _ctx.Project?.Tilesets.GetValueOrDefault(key) is { } now && text != now.Name)
                    _ctx.EditTileset(key, UiStrings.TilesetSettings_Title, t => t with { Name = text });
            }
            name.LostFocus += (_, _) => Rename();
            name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(); };
            string use = open.Contains(key) ? UiStrings.TilesetChoice_Open : p.IsTilesetUsed(key) ? UiStrings.TilesetManager_UsedElsewhere : UiStrings.TilesetManager_Unused;
            var info = new TextBlock
            {
                Text = UiStrings.FormatTilesetChoice_Summary(w: ts.CellWidth, h: ts.CellHeight, tiles: ts.Tiles.Length, sets: ts.AutoTiles.Length) + " · " + use,
                Foreground = AppTheme.DimTextBrush,
                FontSize = AppTheme.SmallFontSize,
                Margin = new Thickness(3, 0),
            };
            var save = Ui.IconButton(VectorIcon.Save, UiStrings.Tileset_SaveFile, (_, _) => SaveFile(key), size: 14);
            var delete = Ui.IconButton(VectorIcon.Trash, UiStrings.TilesetManager_Delete, (_, _) => Delete(key), size: 14);
            delete.IsEnabled = !p.IsTilesetUsed(key);
            if (!delete.IsEnabled) ToolTip.SetTip(delete, UiStrings.TilesetManager_DeleteUsedTip);
            var row = new DockPanel();
            var buttons = Ui.Row(save, delete);
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);
            row.Children.Add(Ui.Row(new TilesetStrip(ts) { Margin = new Thickness(0, 0, 6, 0) }, new StackPanel { Spacing = 1, Children = { name, info } }));
            _list.Children.Add(new Border
            {
                Child = row,
                Width = 520,
                Padding = new Thickness(6, 4),
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
            });
        }
        if (_list.Children.Count == 0) _list.Children.Add(Ui.Label(UiStrings.TilesetManager_None));
    }

    private void CreateEmpty()
    {
        if (_ctx.Project is not { } p) return;
        var name = string.IsNullOrWhiteSpace(_newName.Text) ? UiStrings.TilesetChoice_DefaultName : _newName.Text.Trim();
        var ts = Tileset.Create(name, (int)_cellW.NumValue(), (int)_cellH.NumValue());
        _ctx.Structural(() => p.AddTileset(ts));
        BuildList();
    }

    private void ImportFile()
    {
        if (_ctx.Project is not { } p) return;
        var path = Ui.OpenFile(this, UiStrings.TilesetManager_ImportFile, null, new FileFilter(UiStrings.Tileset_FileFilter, "*" + Tileset.FileExtension));
        if (path is null) return;
        try
        {
            var ts = TilesetIO.Load(path);
            _ctx.Structural(() => p.AddTileset(ts));
            p.MarkTilesetSynced(ts);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
        BuildList();
    }

    private void SaveFile(string key)
    {
        if (_ctx.Project is not { } p || !p.Tilesets.TryGetValue(key, out var ts)) return;
        var dir = ts.SourcePath is { } sp ? Path.GetDirectoryName(sp) : p.Path is { } pp ? Path.GetDirectoryName(pp) : null;
        var path = Ui.SaveFile(this, UiStrings.Tileset_SaveFile, dir, (ts.Name.Length > 0 ? ts.Name : "tileset") + Tileset.FileExtension, Tileset.FileExtension,
            new FileFilter(UiStrings.Tileset_FileFilter, "*" + Tileset.FileExtension));
        if (path is null) return;
        var full = Path.GetFullPath(path);
        try
        {
            if (!string.Equals(ts.SourcePath, full, StringComparison.Ordinal)) _ctx.EditTileset(key, UiStrings.Tileset_SaveFile, t => t with { SourcePath = full });
            var saved = p.Tilesets[key];
            TilesetIO.Save(saved, full);
            p.MarkTilesetSynced(saved);
            Ui.Info(this, string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.Tileset_SavedMessage, Path.GetFileName(full)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, ex.Message);
        }
        BuildList();
    }

    private void Delete(string key)
    {
        if (_ctx.Project is not { } p || !p.Tilesets.TryGetValue(key, out var ts)) return;
        if (Ui.Ask(this, UiStrings.FormatTilesetManager_DeleteAsk(name: ts.Name), UiStrings.TilesetManager_Delete, MessageButtons.OKCancel, MessageIcon.Question) != DialogResult.OK) return;
        _ctx.Structural(() => p.RemoveTileset(key));
        BuildList();
    }

    /// <summary>타일셋 관리 창을 띄운다.</summary>
    public static void Run(Visual? owner, EditorContext ctx)
    {
        if (ctx.Project is null) return;
        new TilesetManagerDialog(ctx).ShowDialog(owner);
    }
}
