using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>
/// 에셋 관리자의 모양 펜 거르기: 내 에셋 = 모양 펜 세트 모음의 내 세트(<see cref="ShapeSetActions.Own"/> — 직접 만든 세트와 예전 기본 세트에서 남은 세트,
/// 기본 세트 다섯 가지는 빼고)와 이름이 맞는 내 세트가 없는 떠 둔 사본(<see cref="ShapeSetActions.Snapshot"/>), 받은 에셋 = 상점·패키지 파일에서 온 세트(도구 설정의
/// 받은 세트와 같음). 내 세트 상세는 모양 격자·이름 바꾸기·캔버스에서 편집·내보내기(세트 파일·에셋 패키지)·상점에 올리기·지우기, 도구 막대는 캔버스에서 새 세트와
/// 기본 세트 되돌리기. 세트 모음을 고치면 <see cref="EditorContext.ShapeSetsChanged"/>로 목록과 도구 설정이 같이 바뀐다.
/// </summary>
public sealed partial class AssetManagerWindow
{
    /// <summary>세트 모음을 가진 메인 창(모양 펜 거르기의 내 세트 — 없으면 받은 세트만 보임).</summary>
    private readonly MainForm? _main;
    private readonly EditorContext? _shapeCtx;
    private readonly Button _restoreEffects, _restoreSets, _newSetFromCanvas;

    /// <summary>목록 한 줄의 고를 키(내 모양 펜 세트).</summary>
    private static string ShapeSetKey(string id) => "shape:" + id;

    /// <summary>창을 띄운 <paramref name="owner"/>부터 위로 올라가 찾은 메인 창(없으면 앱의 메인 창).</summary>
    private static MainForm? MainOf(Window owner)
    {
        for (Window? window = owner; window is not null; window = window.Owner as Window)
            if (window is MainForm main) return main;
        return MainForm.Current;
    }

    /// <summary>
    /// 도구 설정의 "세트 관리…": 설치됨 탭의 모양 펜 거르기로 열고 <paramref name="setId"/>(내 세트 또는 받은 세트 — 받은 세트의 이름표 = 에셋 ID)를 고른다.
    /// 기본 세트는 목록에 없어 첫 줄을 고른다.
    /// </summary>
    public static void OpenShapeSets(Window owner, string? setId)
    {
        AppAssets.ShowProblems(owner);
        var main = MainOf(owner);
        var window = new AssetManagerWindow(AssetKinds.ShapeSet, null, AssetManagerTab.Installed, main);
        if (setId is not null)
        {
            window._selectKey = main?.ShapeSetContext.ShapeSets.Find(setId) is { } own && !ShapeSetActions.IsBuiltIn(own) ? ShapeSetKey(setId) : AssetKey(setId);
            window.Refresh();
        }
        window.ShowDialog(owner);
    }

    private bool IsShapeSetFilter => _filter.SelectedIndex == FilterIndex(AssetKinds.ShapeSet);

    private StampSet? SelectedSet => (_list.SelectedItem as ListBoxItem)?.Tag as StampSet;

    /// <summary>도구 막대: 모양 펜 거르기면 "기본 효과 되돌리기" 대신 "캔버스에서 새 세트"·"기본 세트 되돌리기".</summary>
    private void ShowToolbarFor()
    {
        bool shapes = IsShapeSetFilter;
        _restoreEffects.IsVisible = !shapes;
        _restoreSets.IsVisible = _newSetFromCanvas.IsVisible = shapes && _shapeCtx is not null;
    }

    /// <summary>
    /// 내 에셋으로 보이는 에셋인지: 내가 만든(<see cref="AssetEntry.IsOwnedBy"/>) 것. 단 상점·패키지 파일에서 온 모양 펜 세트는 내가 올린 것이라도
    /// 받은 세트다(도구 설정에서 읽기 전용 — 고치려면 내 세트로 복사).
    /// </summary>
    private static bool IsMineEntry(AssetEntry entry, string? handle)
        => entry.IsOwnedBy(handle) && !(entry.Kind == AssetKinds.ShapeSet && entry.Source != AssetSources.Local);

    /// <summary>검색어에 맞는 내 세트(세트 모음 차례).</summary>
    private List<StampSet> ShownOwnSets()
    {
        if (_shapeCtx is null) return [];
        string query = _search.Text?.Trim() ?? "";
        return ShapeSetActions.Own(_shapeCtx).Where(set => query.Length == 0 || set.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    /// <summary>
    /// 모양 펜 거르기의 목록: 내 에셋(내 세트, 그다음 이름이 맞는 내 세트가 없는 떠 둔 사본), 받은 에셋(상점·패키지 파일에서 온 세트).
    /// </summary>
    private void AddShapeSetRows(List<AssetEntry> shown)
    {
        var own = ShownOwnSets();
        var ownNames = _shapeCtx is null ? [] : ShapeSetActions.Own(_shapeCtx).Select(s => s.DisplayName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var snapshots = shown.Where(entry => entry.Source == AssetSources.Local && !ownNames.Contains(entry.Name)).ToList();
        var received = shown.Where(entry => entry.Source != AssetSources.Local).ToList();
        if (own.Count + snapshots.Count > 0)
        {
            AddHeader(UiStrings.Assets_PersonalHeader);
            foreach (var set in own)
                _list.Items.Add(new ListBoxItem { Content = ShapeSetRow(set), Tag = set, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            foreach (var entry in snapshots) AddRow(entry, AssetRow(entry, mine: true));
        }
        if (received.Count > 0)
        {
            AddHeader(UiStrings.Assets_ReceivedHeader);
            foreach (var entry in received) AddRow(entry, AssetRow(entry, mine: false));
        }
    }

    /// <summary>내 세트 줄: 첫 모양 썸네일·이름, 아래에 모양 수와 올린 버전·고침 상태.</summary>
    private static Control ShapeSetRow(StampSet set)
    {
        var name = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,6,*") };
        name.Children.Add(new ToolOptionsPanel.StampView { Image = set.Shapes.FirstOrDefault(), Width = 16, Height = 16 });
        var title = new TextBlock { Text = set.DisplayName, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(title, 2);
        name.Children.Add(title);
        var snapshot = ShapeSetActions.Snapshot(set);
        string?[] parts =
        [
            F(UiStrings.Assets_ShapeCount, set.Shapes.Count),
            snapshot?.Store is { } store ? F(UiStrings.Assets_PublishedVersion, store.Version) : null,
            snapshot?.Store is not null && ShapeSetActions.ChangedSince(set, snapshot) ? UiStrings.Assets_ModifiedNote : null,
        ];
        var caption = Dim(string.Join(" · ", parts.Where(s => !string.IsNullOrEmpty(s))));
        caption.Margin = new Thickness(22, 2, 0, 0);
        return new StackPanel { Children = { name, caption } };
    }

    /// <summary>
    /// 내 세트 상세: 모양 격자, 정보 표(종류·작성자·상점 ID·올린 버전·고침·출처 — 상점 정보는 올릴 때 떠 둔 사본에서), 모양 수, 동작(캔버스에서 편집·이름 바꾸기·
    /// 내보내기·상점에 올리기·지우기).
    /// </summary>
    private void ShapeSetDetail(StampSet set)
    {
        var ctx = _shapeCtx!;
        AddTitle(set.DisplayName);
        _detail.Children.Add(new AssetPreviewView(set));
        var snapshot = ShapeSetActions.Snapshot(set);
        var store = snapshot?.Store;
        Facts(
            (UiStrings.Assets_Kind, KindText(AssetKinds.ShapeSet)),
            (UiStrings.Assets_Author, IdentityBadge.Author(null, 16, fallback: snapshot?.Author?.Name ?? UiStrings.Assets_SourceLocal)),
            (UiStrings.Assets_StoreId, store?.PackageId),
            (UiStrings.Assets_Version, store?.Version),
            (UiStrings.Assets_Status, store is not null && ShapeSetActions.ChangedSince(set, snapshot!) ? UiStrings.Assets_ModifiedFact : null),
            (UiStrings.Assets_Source, UiStrings.Assets_SourceMySets));
        _detail.Children.Add(Dim(F(UiStrings.Assets_ShapeCount, set.Shapes.Count)));

        var export = Ui.Button(VectorIcon.ChevronDown, UiStrings.Assets_ExportSetMenu, iconAfter: true).Tip(UiStrings.Assets_ExportSetTip);
        export.Click += (_, _) => PopupMenus.Show(export, ExportSetMenu(set), Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft);
        bool listed = store is not null;
        Actions(
            Ui.Button(VectorIcon.Image, UiStrings.Assets_EditOnCanvas, (_, _) => EditOnCanvas(set)).Tip(UiStrings.Assets_EditOnCanvasTip),
            Ui.Button(VectorIcon.Pencil, UiStrings.Assets_Rename, (_, _) => ShapeSetActions.Rename(this, ctx, set)),
            export,
            Ui.Button(VectorIcon.Globe, listed ? UiStrings.Assets_PublishNewVersion : UiStrings.Assets_Publish, async (_, _) => await ShapeSetActions.PublishAsync(this, ctx, set))
                .Tip(listed ? UiStrings.Assets_PublishNewVersionTip : UiStrings.Assets_PublishEntryTip),
            Ui.Button(VectorIcon.Trash, UiStrings.Assets_Delete, (_, _) => ShapeSetActions.Delete(this, ctx, set)));
        _detail.Children[^1].HelpAt(HelpPage, anchor: "My assets");
    }

    /// <summary>내 세트 내보내기 메뉴: 세트 파일(.chickxel-shapes) 또는 에셋 패키지(.chickxel-asset — 세트를 내 에셋으로 떠 두고 그것을 내보냄).</summary>
    private IEnumerable<object> ExportSetMenu(StampSet set)
    {
        MenuItem Item(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => Dispatcher.UIThread.Post(action);
            return item;
        }
        return
        [
            Item(UiStrings.Assets_ExportSetFile, () => ShapeSetActions.ExportFile(this, set)),
            Item(UiStrings.Assets_ExportSetPackage, () =>
            {
                if (ShapeSetActions.SaveSnapshot(this, set, UiStrings.Assets_ExportTitle) is { } saved) Export([saved]);
            }),
        ];
    }

    /// <summary>
    /// 도구 막대 "보이는 것 내보내기": 모양 펜 거르기면 보이는 내 세트를 내 에셋으로 떠 두고(모양이 없거나 너무 많은 세트는 뺌) 보이는 받은 세트·떠 둔 사본과 함께 묶는다.
    /// </summary>
    private void ExportShown()
    {
        if (!IsShapeSetFilter || _shapeCtx is null)
        {
            Export(Shown());
            return;
        }
        int max = AssetPackageLimits.Default.MaxShapesPerSet;
        var entries = new List<AssetEntry>();
        foreach (var set in ShownOwnSets().Where(s => s.Shapes.Count > 0 && s.Shapes.Count <= max))
        {
            if (ShapeSetActions.SaveSnapshot(this, set, UiStrings.Assets_ExportTitle) is not { } saved) return;
            entries.Add(saved);
        }
        var ids = entries.Select(e => e.Id).ToHashSet();
        entries.AddRange(_list.Items.OfType<ListBoxItem>().Select(item => item.Tag).OfType<AssetEntry>().Where(shown => !ids.Contains(shown.Id)).ToList());
        Export(entries);
    }

    /// <summary>세트를 새 문서 탭으로 열어 고친다(에셋 관리자는 모달이라 닫고 넘어감 — 세트에 저장하면 목록·도구 설정이 바뀜).</summary>
    private void EditOnCanvas(StampSet set)
    {
        if (_main is not { } main) return;
        Close();
        main.EditShapeSetOnCanvas(set.Id);
    }

    /// <summary>받은 세트를 캔버스에서 편집: 먼저 내 세트로 복사하고 그 사본을 연다(받은 원본은 그대로).</summary>
    private void EditCopyOnCanvas(StampSet received)
    {
        if (_shapeCtx is null) return;
        EditOnCanvas(ShapeSetActions.CopyToMine(_shapeCtx, received));
    }

    /// <summary>캔버스에서 새 세트: 지금 문서의 프레임마다 모양 하나로 새 내 세트를 만들고(메인 창이 이름을 물음) 목록에서 고른다.</summary>
    private void NewShapeSetFromCanvas()
    {
        if (_main is not { } main || _shapeCtx is not { } ctx) return;
        int before = ctx.ShapeSets.Sets.Count;
        main.NewShapeSetFromCanvas(this);
        if (ctx.ShapeSets.Sets.Count <= before) return;
        _selectKey = ShapeSetKey(ctx.ShapeSets.Sets[^1].Id);
        _search.Text = "";
        Refresh();
    }

    private void RestoreShapeSets()
    {
        if (_shapeCtx is not null && ShapeSetActions.RestoreDefaults(this, _shapeCtx))
            Ui.Info(this, UiStrings.Assets_RestoreSetsDone, UiStrings.ShapePen_RestoreDefaultsTip);
    }

    /// <summary>모양 펜 세트 파일·PNG를 새 내 세트로 가져오고 마지막 세트를 고른다(<see cref="ShapeSetActions.Import"/>).</summary>
    private void ImportSetFiles(IReadOnlyList<string> paths)
    {
        if (_shapeCtx is null || ShapeSetActions.Import(this, _shapeCtx, paths) is not { Count: > 0 } imported) return;
        _selectKey = ShapeSetKey(imported[^1].Id);
        _search.Text = "";
        Refresh();
    }
}
