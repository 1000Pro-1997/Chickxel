using System.Runtime.CompilerServices;
using Avalonia.Controls.Templates;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 도구 설정 "모양 펜" 묶음: 세트 고르기(첫 모양 썸네일 + 이름 — 내 세트, 그다음 받은 세트)·새 세트·이름 바꾸기·지우기·기본 세트 되돌리기·세트 관리(상점·설치된 세트·
/// 상점에 올리기·내보내기·가져오기·내 세트로 복사 — <see cref="ShapeSetManageDialog"/>), 세트의 모양 칸(누르면 다음에 찍을 모양, 우클릭 = 지우기)·모양 더하기(선택 영역·지금 셀·PNG)·지우기
/// (지우기 단추는 늘 빨강), 순서(무작위·차례로·선택된 것 — 세트마다), 끌기 간격(1 이상)·최적화 간격, 지금 색으로 물들이기.
/// 받은 세트(상점·패키지 파일)는 읽기 전용이라 고치는 단추가 꺼지고 복사 안내가 보인다.
/// 세트를 고치면 <see cref="EditorContext.NotifyShapeSetsEdited"/>로 알리고 메인 창이 파일에 저장한다.
/// </summary>
public sealed partial class ToolOptionsPanel
{
    /// <summary>모양 칸 한 변(px).</summary>
    private const int ShapeTile = 30;

    private readonly ComboBox _shapePenSets = new()
    {
        Width = 170,
        Margin = new Thickness(3),
        Focusable = false,
    };
    /// <summary>새 세트·이름 바꾸기·세트 지우기·기본 세트 되돌리기·세트 관리 단추 줄(좁으면 다음 줄로).</summary>
    private WrapPanel _shapePenSetButtons = null!;
    /// <summary>내 세트여야 쓰는 단추(이름 바꾸기·지우기 — 받은 세트는 읽기 전용).</summary>
    private Button[] _shapePenMineOnly = [];
    /// <summary>세트 관리 창을 여는 단추.</summary>
    private Button _shapePenManage = null!;
    private Button _shapePenAddShape = null!;
    /// <summary>받은 세트를 골랐을 때의 안내(고치려면 복사).</summary>
    private TextBlock _shapePenReadOnly = null!;
    /// <summary>세트의 모양 칸(<see cref="ShapeTile"/> 단추 — 고른 것 = 다음에 찍을 모양).</summary>
    private readonly WrapPanel _shapePenShapes = new() { Orientation = Orientation.Horizontal };
    /// <summary>모양 더하기·지우기 단추 줄.</summary>
    private StackPanel _shapePenShapeButtons = null!;
    /// <summary>무작위 / 차례로 / 선택된 것(<see cref="StampOrder"/> 순서). 클릭해도 캔버스 포커스를 빼앗지 않는다.</summary>
    private readonly TabStrip _shapePenOrder = new(TabStripKind.Segmented) { Focusable = false, IsTabStop = false, Margin = default };
    private NumericUpDown _shapePenSpacing = null!;
    /// <summary>간격 줄(최적화 간격을 켜면 흐려짐).</summary>
    private StackPanel _shapePenSpacingRow = null!;
    private CheckBox _shapePenOptimized = null!, _shapePenTint = null!;
    private Button _shapePenDeleteShape = null!;
    /// <summary>모양 칸을 만든 세트와 그때의 모양들(바뀌었을 때만 칸을 다시 만든다).</summary>
    private (StampSet? Set, PixelImage[] Shapes) _shapeTilesShown = (null, []);

    /// <summary>모양 펜 묶음을 만든다(생성자에서 한 번).</summary>
    private Border BuildShapePen()
    {
        _shapePenSets.ItemTemplate = new FuncDataTemplate<StampSet>((set, _) => set is null ? null : SetItem(set));
        _shapePenSets.Tip(UiStrings.ShapePen_SetTip);
        _shapePenSets.SelectionChanged += (_, _) =>
        {
            if (!_updating && _shapePenSets.SelectedItem is StampSet set) _ctx.ShapePenSetId = set.Id;
        };
        var newSet = Ui.IconButton(VectorIcon.Plus, UiStrings.ShapePen_NewSetTip, (_, _) => NewShapeSet(), size: 13);
        var rename = Ui.IconButton(VectorIcon.Pencil, UiStrings.ShapePen_RenameSetTip, (_, _) => RenameShapeSet(), size: 13);
        var deleteSet = Danger(Ui.IconButton(VectorIcon.Trash, UiStrings.ShapePen_DeleteSetTip, (_, _) => DeleteShapeSet(), size: 13));
        var restore = Ui.IconButton(VectorIcon.Undo2, UiStrings.ShapePen_RestoreDefaultsTip, (_, _) => RestoreShapeSets(), size: 13);
        _shapePenManage = Ui.Button(VectorIcon.Package, UiStrings.ShapePen_ManageSets, (_, _) => ManageShapeSets()).Tip(UiStrings.ShapePen_ManageSetsTip);
        foreach (var b in new[] { newSet, rename, deleteSet, restore, _shapePenManage }) b.Focusable = false;
        _shapePenMineOnly = [rename, deleteSet];
        _shapePenSetButtons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 2, LineSpacing = 2 };
        _shapePenSetButtons.Children.AddRange([newSet, rename, deleteSet, restore, _shapePenManage]);
        _shapePenReadOnly = new TextBlock
        {
            Text = UiStrings.ShapePen_ReadOnlyNote, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize,
            Margin = new Thickness(3, 0, 3, 2), MaxWidth = 300, HorizontalAlignment = HorizontalAlignment.Left,
        };

        var add = _shapePenAddShape = Ui.Button(VectorIcon.ChevronDown, UiStrings.ShapePen_AddShape, iconAfter: true).Tip(UiStrings.ShapePen_AddShapeTip);
        add.Focusable = false;
        add.Click += (_, _) => PopupMenus.Show(add, AddShapeMenu(), Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft);
        _shapePenDeleteShape = Danger(Ui.IconButton(VectorIcon.Trash, UiStrings.ShapePen_DeleteShapeTip, (_, _) => DeleteShape(_ctx.ShapePenIndex), size: 13));
        _shapePenDeleteShape.Focusable = false;
        _shapePenShapeButtons = Ui.Row(add, _shapePenDeleteShape);

        _shapePenOrder.Add(UiStrings.ShapePen_OrderRandom, tip: UiStrings.ShapePen_OrderRandomTip);
        _shapePenOrder.Add(UiStrings.ShapePen_OrderSequential, tip: UiStrings.ShapePen_OrderSequentialTip);
        _shapePenOrder.Add(UiStrings.ShapePen_OrderSelected, tip: UiStrings.ShapePen_OrderSelectedTip);
        _shapePenOrder.SelectionChanged += (_, _) =>
        {
            if (_updating || _shapePenOrder.SelectedIndex < 0 || _ctx.ShapePenSet is not { } set || _ctx.IsInstalledShapeSet(set)) return;
            var order = (StampOrder)_shapePenOrder.SelectedIndex;
            if (set.Order == order) return;
            set.Order = order;
            _ctx.NotifyShapeSetsEdited();
        };
        _shapePenSpacing = NumBox(1, EditorContext.MaxShapePenSpacing, v => _ctx.ShapePenSpacing = v)
            .Presets(1, 2, 3, 4, 6, 8, 12, 16, 24, 32).Unit("px").RecentKey("shapepen.spacing");
        _shapePenSpacing.Tip(UiStrings.ShapePen_SpacingTip);
        _shapePenSpacingRow = Ui.Row(Ui.Label(UiStrings.ShapePen_Spacing), _shapePenSpacing, Ui.Label("px"));
        _shapePenOptimized = Check(UiStrings.ShapePen_OptimizedSpacing, UiStrings.ShapePen_OptimizedSpacingTip, v => _ctx.ShapePenOptimizedSpacing = v);
        _shapePenTint = Check(UiStrings.ShapePen_Tint, UiStrings.ShapePen_TintTip, v => _ctx.ShapePenTint = v);

        _ctx.ShapeSetsChanged += (_, _) => RefreshShapeSetList();
        _ctx.InstalledShapeSetsChanged += (_, _) => RefreshShapeSetList();
        _ctx.ShapePenChanged += (_, _) => RefreshShapePen();
        RefreshShapeSetList();
        return InfoGroup(UiStrings.ShapePen_Group, UiStrings.ShapePen_Info, Ui.Stack(
            Ui.Row(Ui.Label(UiStrings.ShapePen_Set), _shapePenSets),
            _shapePenSetButtons,
            _shapePenReadOnly,
            _shapePenShapes,
            _shapePenShapeButtons,
            Ui.Row(Ui.Label(UiStrings.ShapePen_Order), _shapePenOrder),
            _shapePenSpacingRow,
            _shapePenOptimized,
            _shapePenTint));
    }

    /// <summary>지우기 단추를 늘 빨간 바탕·흰 아이콘으로(마우스를 올려도 유지).</summary>
    private static Button Danger(Button button)
    {
        button.Background = AppTheme.UnsavedBrush;
        button.Foreground = Brushes.White;
        Ui.KeepFillOnHover(button);
        return button;
    }

    /// <summary>세트 고르기 목록의 한 줄: 첫 모양 썸네일 + 이름(받은 세트는 앞에 패키지 아이콘).</summary>
    private Control SetItem(StampSet set)
    {
        var row = Ui.Row(new StampView { Image = set.Shapes.FirstOrDefault(), Width = 18, Height = 18 }, Ui.Label(set.DisplayName));
        if (_ctx.IsInstalledShapeSet(set)) row.Children.Insert(1, Ui.Icon(VectorIcon.Package, 12));
        return row;
    }

    /// <summary>세트 목록을 다시 채운다(내 세트 모음·받은 세트가 바뀌었을 때 — 이름·첫 모양도 다시 그림).</summary>
    private void RefreshShapeSetList()
    {
        bool wasUpdating = _updating;
        _updating = true;
        try { _shapePenSets.ItemsSource = _ctx.AllShapeSets.ToList(); }
        finally { _updating = wasUpdating; }
        _shapeTilesShown = (null, []);
        RefreshShapePen();
    }

    /// <summary>고른 세트·모양 칸·순서·간격·물들이기를 보인다.</summary>
    private void RefreshShapePen()
    {
        var set = _ctx.ShapePenSet;
        bool wasUpdating = _updating;
        _updating = true;
        try
        {
            _shapePenSets.SelectedItem = set;
            _shapePenOrder.SelectedIndex = (int)(set?.Order ?? StampOrder.Random);
            _shapePenOrder.IsEnabled = set is { Shapes.Count: > 1 } && !_ctx.IsInstalledShapeSet(set);
            _shapePenSpacing.Value = _ctx.ShapePenSpacing;
            _shapePenOptimized.IsChecked = _ctx.ShapePenOptimizedSpacing;
            _shapePenSpacingRow.IsEnabled = !_ctx.ShapePenOptimizedSpacing;
            _shapePenTint.IsChecked = _ctx.ShapePenTint;
        }
        finally { _updating = wasUpdating; }
        bool installed = _ctx.IsInstalledShapeSet(set);
        foreach (var b in _shapePenMineOnly) b.IsEnabled = set is not null && !installed;
        _shapePenReadOnly.IsVisible = installed;
        _shapePenAddShape.IsEnabled = !installed;
        _shapePenDeleteShape.IsEnabled = set is { Shapes.Count: > 0 } && !installed;

        var shapes = set?.Shapes.ToArray() ?? [];
        if (!ReferenceEquals(_shapeTilesShown.Set, set) || !_shapeTilesShown.Shapes.SequenceEqual(shapes))
        {
            _shapeTilesShown = (set, shapes);
            _shapePenShapes.Children.Clear();
            for (int i = 0; i < shapes.Length; i++) _shapePenShapes.Children.Add(ShapeTileButton(shapes[i], i));
            _shapePenShapes.IsVisible = shapes.Length > 0;
        }
        int next = _ctx.ShapePenIndex;
        for (int i = 0; i < _shapePenShapes.Children.Count; i++)
            ((Button)_shapePenShapes.Children[i]).Background = i == next ? AppTheme.AccentBrush : AppTheme.InputBrush;
    }

    /// <summary>모양 칸 하나: 누르면 다음에 이 모양을 찍고, 우클릭하면 지우기 메뉴.</summary>
    private Button ShapeTileButton(PixelImage shape, int index)
    {
        var b = Ui.Button("", (_, _) => _ctx.ShapePenIndex = index);
        b.Content = new StampView { Image = shape, Width = ShapeTile - 6, Height = ShapeTile - 6 };
        b.Width = b.Height = ShapeTile;
        b.MinWidth = b.MinHeight = 0;
        b.Padding = new Thickness(2);
        b.Margin = new Thickness(1);
        b.Focusable = false;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.VerticalContentAlignment = VerticalAlignment.Center;
        b.Tip(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, "{0}×{1} · {2}", shape.Width, shape.Height, UiStrings.ShapePen_ShapeTip));
        PopupMenus.Attach(b, () => [MenuItemFor(UiStrings.ShapePen_DeleteShape, () => DeleteShape(index), enabled: !_ctx.IsInstalledShapeSet(_ctx.ShapePenSet))]);
        return b;
    }

    /// <summary>메뉴를 닫은 뒤 <paramref name="action"/>을 하는 메뉴 항목(대화상자는 메뉴가 닫힌 다음에 연다).</summary>
    private static MenuItem MenuItemFor(string text, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = text, IsEnabled = enabled };
        item.Click += (_, _) => Dispatcher.UIThread.Post(action);
        return item;
    }

    /// <summary>모양 더하기 메뉴: 선택 영역(있을 때)·지금 셀·PNG 파일.</summary>
    private IEnumerable<object> AddShapeMenu()
    {
        bool hasCel = _ctx.EditImage is not null && !_ctx.IsGroupSelected;
        return
        [
            MenuItemFor(UiStrings.ShapePen_AddFromSelection, () => AddShapes([_ctx.Selection.Copy(_ctx.EditImageOrEmpty)]), hasCel && _ctx.Selection.HasSelection),
            MenuItemFor(UiStrings.ShapePen_AddFromCel, () => AddShapes([_ctx.EditImage]), hasCel),
            new Separator(),
            MenuItemFor(UiStrings.ShapePen_AddFromPng, AddShapesFromPng),
        ];
    }

    /// <summary>PNG 파일(여러 개)을 모양으로 더한다(알파 포함, 투명한 둘레는 잘라 냄).</summary>
    private void AddShapesFromPng()
    {
        var images = new List<PixelImage?>();
        foreach (var path in Ui.OpenFiles(this, UiStrings.ShapePen_PngTitle, null, new FileFilter(UiStrings.ShapePen_PngFilter, "*.png")))
        {
            try { images.Add(PngIO.Load(path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                StatusMessage?.Invoke(this, UiStrings.FormatShapePen_PngFailed(name: Path.GetFileName(path), error: ex.Message));
            }
        }
        if (images.Count > 0) AddShapes(images);
    }

    /// <summary>
    /// 그림들을 고른 세트에 모양으로 더한다(투명한 둘레를 잘라 냄 — 세트가 없으면 새 세트). 투명뿐이거나 너무 크면 건너뛰고 알린다.
    /// 마지막으로 더한 모양을 다음 모양으로 고른다.
    /// </summary>
    private void AddShapes(IEnumerable<PixelImage?> images)
    {
        var shapes = new List<PixelImage>();
        string? problem = null;
        foreach (var image in images)
        {
            if (image is null || StampLibrary.Trim(image) is not { } shape) problem = UiStrings.ShapePen_EmptyContent;
            else if (StampLibrary.TooBig(shape))
                problem = UiStrings.FormatShapePen_TooBig(width: shape.Width, height: shape.Height, max: StampLibrary.MaxShapeSize);
            else shapes.Add(shape);
        }
        if (shapes.Count > 0)
        {
            if (_ctx.ShapePenSet is not { } set || _ctx.IsInstalledShapeSet(set))
            {
                set = new StampSet { Name = UiStrings.FormatShapePen_NewSetName(number: 1) };
                _ctx.ShapeSets.Sets.Add(set);
                _ctx.ShapePenSetId = set.Id;
            }
            set.Shapes.AddRange(shapes);
            _ctx.ShapePenIndex = set.Shapes.Count - 1;
            _ctx.NotifyShapeSetsEdited();
            var last = shapes[^1];
            StatusMessage?.Invoke(this, UiStrings.FormatShapePen_Added(count: shapes.Count, width: last.Width, height: last.Height, set: set.DisplayName));
        }
        if (problem is not null) StatusMessage?.Invoke(this, problem);
    }

    /// <summary>고른 세트의 <paramref name="index"/>번 모양을 지운다(기본 세트 모양은 '기본 세트 되돌리기'로 다시 넣을 수 있음).</summary>
    private void DeleteShape(int index)
    {
        if (_ctx.ShapePenSet is not { } set || _ctx.IsInstalledShapeSet(set) || (uint)index >= (uint)set.Shapes.Count) return;
        set.Shapes.RemoveAt(index);
        _ctx.ShapePenIndex = Math.Min(index, Math.Max(0, set.Shapes.Count - 1));
        _ctx.NotifyShapeSetsEdited();
    }

    private void NewShapeSet()
    {
        var names = _ctx.ShapeSets.Sets.Select(s => s.DisplayName).ToHashSet();
        int n = 1;
        while (names.Contains(UiStrings.FormatShapePen_NewSetName(number: n))) n++;
        if (InputDialog.Ask(this, UiStrings.ShapePen_NewSetTip, UiStrings.ShapePen_SetNamePrompt, UiStrings.FormatShapePen_NewSetName(number: n)) is not { } name) return;
        var set = new StampSet { Name = name };
        _ctx.ShapeSets.Sets.Add(set);
        _ctx.ShapePenSetId = set.Id;
        _ctx.NotifyShapeSetsEdited();
        StatusMessage?.Invoke(this, UiStrings.ShapePen_NewSetHint);
    }

    private void RenameShapeSet()
    {
        if (_ctx.ShapePenSet is not { } set || _ctx.IsInstalledShapeSet(set)) return;
        if (InputDialog.Ask(this, UiStrings.ShapePen_RenameSetTip, UiStrings.ShapePen_SetNamePrompt, set.DisplayName) is not { } name || name == set.DisplayName) return;
        set.Name = name;
        _ctx.NotifyShapeSetsEdited();
    }

    private void DeleteShapeSet()
    {
        if (_ctx.ShapePenSet is not { } set || _ctx.IsInstalledShapeSet(set)) return;
        if (Ui.Ask(this, UiStrings.FormatShapePen_DeleteSetConfirm(name: set.DisplayName, count: set.Shapes.Count), UiStrings.ShapePen_DeleteSetTip,
                MessageButtons.OKCancel, MessageIcon.Warning, okText: UiStrings.ShapePen_DeleteSet) != DialogResult.OK) return;
        int at = _ctx.ShapeSets.Sets.IndexOf(set);
        _ctx.ShapeSets.Sets.RemoveAt(at);
        var sets = _ctx.ShapeSets.Sets;
        _ctx.ShapePenSetId = sets.Count > 0 ? sets[Math.Min(at, sets.Count - 1)].Id : null;
        _ctx.NotifyShapeSetsEdited();
    }

    private void RestoreShapeSets()
    {
        if (Ui.Ask(this, UiStrings.ShapePen_RestoreDefaultsConfirm, UiStrings.ShapePen_RestoreDefaultsTip,
                MessageButtons.OKCancel, MessageIcon.Question, okText: UiStrings.ShapePen_RestoreDefaults) != DialogResult.OK) return;
        string? current = _ctx.ShapePenSetId;
        _ctx.ShapeSets.RestoreDefaults();
        _ctx.ShapePenSetId = current;
        _ctx.NotifyShapeSetsEdited();
    }

    /// <summary>받은 세트를 고칠 수 있는 내 세트로 복사하고 그 사본을 고른다(받은 원본은 그대로).</summary>
    private void CopyShapeSet()
    {
        if (_ctx.ShapePenSet is not { } set || !_ctx.IsInstalledShapeSet(set)) return;
        if (MainForm.CopyShapeSetToMine(set) is { } copy)
            StatusMessage?.Invoke(this, UiStrings.FormatShapePen_Copied(name: copy.DisplayName));
    }

    /// <summary>
    /// 고른 내 세트를 상점에 올린다(게시 창, 로그인 필요): 세트의 지금 모습을 내 에셋(<c>shape-set</c>)으로 떠 두고 게시 창을 연다. 같은 이름으로 떠 둔 내 에셋이 있으면
    /// 그 자리를 바꿔(상점 연결은 그대로) 다시 올리면 새 버전이 된다. 모양이 없거나 너무 많으면 먼저 알린다. 기본 세트·받은 세트는 올리지 않는다(단추가 꺼짐).
    /// </summary>
    private async void PublishShapeSet()
    {
        if (_ctx.ShapePenSet is not { } set || _ctx.IsInstalledShapeSet(set) || StampBuiltIns.Ids.Contains(set.Id)
            || TopLevel.GetTopLevel(this) is not Window owner) return;
        int max = AssetPackageLimits.Default.MaxShapesPerSet;
        if (set.Shapes.Count == 0 || set.Shapes.Count > max)
        {
            Ui.Info(owner, set.Shapes.Count == 0 ? UiStrings.ShapePen_PublishEmpty : UiStrings.FormatShapePen_PublishTooMany(max: max, count: set.Shapes.Count),
                UiStrings.Publish_Title);
            return;
        }
        string? handle = AppAssets.MyHandle;
        var existing = AppAssets.Catalog.ShapeSets.FirstOrDefault(entry => entry.Source == AssetSources.Local && entry.IsOwnedBy(handle)
                                                                           && string.Equals(entry.Name, set.DisplayName, StringComparison.OrdinalIgnoreCase));
        AssetEntry? saved = null;
        if (!AppAssets.Try(owner, () => saved = existing is not null && existing.ContentHash == AssetContent.Hash(AssetKinds.ShapeSet, ShapeSetAsset.From(set))
                ? existing
                : AppAssets.Store.SaveShapeSet(existing?.Id, set), UiStrings.Assets_ActionError) || saved is null) return;
        await AssetPublishDialog.RunAsync(owner, saved.Id);
    }

    /// <summary>
    /// 세트 관리 창을 띄우고, 고른 동작을 창이 닫힌 뒤에 한다: 상점 둘러보기·설치된 세트 = 에셋 관리자(모양 펜 거르기 — 받은 세트를 고르고 있으면 그 에셋을 고름),
    /// 상점에 올리기·내보내기·가져오기·내 세트로 복사 = 같은 이름의 처리기.
    /// </summary>
    private void ManageShapeSets()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var set = _ctx.ShapePenSet;
        bool installed = _ctx.IsInstalledShapeSet(set);
        switch (ShapeSetManageDialog.Ask(owner, set, installed))
        {
            case ShapeSetManageAction.BrowseStore: AssetManagerWindow.Open(owner, AssetKinds.ShapeSet, tab: AssetManagerTab.Store); break;
            case ShapeSetManageAction.Installed: AssetManagerWindow.Open(owner, AssetKinds.ShapeSet, installed ? set!.Id : null); break;
            case ShapeSetManageAction.Publish: PublishShapeSet(); break;
            case ShapeSetManageAction.Export: ExportShapeSet(); break;
            case ShapeSetManageAction.Import: PickShapeSetFiles(); break;
            case ShapeSetManageAction.Copy: CopyShapeSet(); break;
        }
    }

    private static FileFilter ShapeSetFilter => new(UiStrings.ShapePen_SetFileType, "*" + StampLibrary.SetFileExtension);

    /// <summary>고른 세트를 세트 파일 하나(<see cref="StampLibrary.SetFileExtension"/>)로 내보낸다. 처음 파일 이름 = 보이는 이름(파일 이름에 못 쓰는 글자는 뺌).</summary>
    private void ExportShapeSet()
    {
        if (_ctx.ShapePenSet is not { } set) return;
        var suggested = ProjectFileName.BaseNameFor(set.DisplayName) + StampLibrary.SetFileExtension;
        if (Ui.SaveFile(this, UiStrings.ShapePen_ExportSet, null, suggested, StampLibrary.SetFileExtension, ShapeSetFilter) is not { } path) return;
        try
        {
            StampLibrary.SaveSet(set, path);
            StatusMessage?.Invoke(this, UiStrings.FormatShapePen_Exported(name: set.DisplayName, path: path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, UiStrings.FormatShapePen_ExportFailed(error: ex.Message));
        }
    }

    /// <summary>세트 가져오기(세트 관리 창): 세트 파일·PNG를 여러 개 골라 가져온다.</summary>
    private void PickShapeSetFiles()
    {
        var paths = Ui.OpenFiles(this, UiStrings.ShapePen_ImportSet, null,
            new FileFilter(UiStrings.ShapePen_ImportFileType, "*" + StampLibrary.SetFileExtension, "*.png"), ShapeSetFilter);
        if (paths.Count > 0) ImportShapeSets(paths);
    }

    /// <summary>
    /// 파일마다 새 세트로 끝에 더하고(덮어쓰지 않음 — 이름이 겹치면 " (2)" …, 이름표는 늘 새것) 마지막 세트를 고른다.
    /// 세트 파일은 그 세트, PNG는 투명한 둘레를 잘라 낸 모양 하나짜리 세트(이름 = 파일 이름). 읽을 수 없는 파일은 모아 오류 상자로 알린다.
    /// 창에 끌어 놓은 세트 파일도 여기로 온다(<see cref="MainForm"/>).
    /// </summary>
    internal void ImportShapeSets(IReadOnlyList<string> paths)
    {
        var imported = new List<StampSet>();
        var failed = new List<string>();
        foreach (var path in paths)
        {
            if (ReadShapeSet(path) is { } set)
            {
                _ctx.ShapeSets.AddImported(set, Path.GetFileNameWithoutExtension(path));
                imported.Add(set);
            }
            else failed.Add(Path.GetFileName(path));
        }
        if (imported.Count > 0)
        {
            _ctx.ShapePenSetId = imported[^1].Id;
            _ctx.NotifyShapeSetsEdited();
            StatusMessage?.Invoke(this, string.Join(" · ", imported.Select(s => UiStrings.FormatShapePen_Imported(name: s.DisplayName, count: s.Shapes.Count))));
        }
        if (failed.Count > 0) Ui.Error(this, UiStrings.FormatShapePen_ImportFailed(names: string.Join(", ", failed)));
    }

    /// <summary>파일 하나를 새 세트로 읽는다(세트 파일, 또는 PNG 한 장 = 모양 하나). 읽을 수 없거나 모양으로 쓸 수 없으면 null.</summary>
    private static StampSet? ReadShapeSet(string path)
    {
        try
        {
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return StampLibrary.ParseSet(File.ReadAllText(path));
            return StampLibrary.Trim(PngIO.Load(path)) is { } shape && !StampLibrary.TooBig(shape) ? new StampSet { Shapes = { shape } } : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>대상 도움말(F1): 모양 펜 묶음 안의 컨트롤이면 가이드 자리, 아니면 null.</summary>
    private HelpTarget? ShapePenHelpTarget(Control c)
    {
        const string page = "tools-shape-pen";
        bool Is(Control control) => ReferenceEquals(c, control);
        bool InRow(Control control) => Is(control) || ReferenceEquals(c, control.Parent);
        static HelpTarget At(string anchor) => new(page, Anchor: anchor);
        if (Is(_shapePenManage)) return At(UiStrings.ShapePen_ExportSet);
        if (InRow(_shapePenSets) || Is(_shapePenSetButtons) || Is(_shapePenReadOnly)) return At(UiStrings.ShapePen_Set);
        if (Is(_shapePenShapes) || c.Parent == _shapePenShapes || Is(_shapePenShapeButtons)) return At(UiStrings.ShapePen_AddShape);
        if (InRow(_shapePenOrder)) return At(UiStrings.ShapePen_Order);
        if (InRow(_shapePenSpacing)) return At(UiStrings.ShapePen_Spacing);
        if (Is(_shapePenOptimized)) return At(UiStrings.ShapePen_OptimizedSpacing);
        if (Is(_shapePenTint)) return At(UiStrings.ShapePen_Tint);
        if (Is(_shapePenBox)) return new(page);
        return null;
    }

    /// <summary>모양 하나를 칸에 맞춰(정수 배율, 체커 바탕) 그린다.</summary>
    internal sealed class StampView : Control
    {
        /// <summary>모양마다 한 번 만든 비트맵(모양 그림은 더한 뒤 바뀌지 않는다).</summary>
        private static readonly ConditionalWeakTable<PixelImage, WriteableBitmap> Bitmaps = new();

        public PixelImage? Image { get; init; }

        public override void Render(DrawingContext g)
        {
            if (Image is not { } image) return;
            var bmp = Bitmaps.GetValue(image, ImageUtil.ToBitmap);
            ImageUtil.DrawFitted(g, bmp, new Rectangle(0, 0, image.Width, image.Height), new Rect(Bounds.Size), checker: true);
        }
    }
}
