using Chickxel.App.Localization;
using Chickxel.Core.Assets;

namespace Chickxel.App;

/// <summary>에셋 관리자의 탭(탭 칸 차례와 같음).</summary>
public enum AssetManagerTab
{
    Installed,
    Store,
    Updates,
}

/// <summary>
/// 에셋 관리자(계획 8장): 설치됨·상점·업데이트 탭. 설치됨 탭은 왼쪽에 에셋 목록(내 에셋 — <see cref="AppAssets.IsMine"/> — 과 받은 에셋),
/// 오른쪽에 고른 에셋의 상세(미리보기, 작성자, 라이선스, 상점 ID·버전, 출처)와 동작(즐겨찾기, 고치기, 복사, 내보내기, 올리기, 삭제 …)을 둔다.
/// 종류 거르기(효과·그라데이션)와 이름 검색은 메모리 카탈로그에서 한다. 상점·업데이트는 온라인 카탈로그(<c>.Store</c>)를 쓴다.
/// 모달이며, 효과 창·그라데이션 프리셋 창에서도 종류 거르기와 처음 탭을 정해 연다. 저장소가 바뀌면(<see cref="AppAssets.Changed"/>) 목록을 다시 만든다.
/// </summary>
public sealed partial class AssetManagerWindow : DialogBase
{
    private readonly ListBox _list = NewList();
    private readonly StackPanel _detail = new() { Spacing = 6 };
    private readonly TabStrip _filter;
    private readonly TextBox _search;
    private readonly TabControl _tabs = new();
    private readonly Action _onChanged;
    /// <summary>다시 만든 뒤 고를 항목(<see cref="AssetKey"/>).</summary>
    private string? _selectKey;
    private bool _refreshing;

    /// <summary>목록 한 줄의 고를 키.</summary>
    private static string AssetKey(string id) => "asset:" + id;

    /// <summary>열려 있는 에셋 관리자(모달이라 하나뿐). 없으면 null — 홈페이지 링크·패키지 파일이 열려 있는 창으로 가게.</summary>
    private static AssetManagerWindow? s_current;

    /// <summary>
    /// 에셋 관리자를 연다. <paramref name="kind"/>는 처음 종류 거르기(<see cref="AssetKinds"/>, null = 고를 에셋의 종류 — 설치됨·상점 탭이 같이 씀),
    /// <paramref name="selectAssetId"/>는 처음 고를 에셋, <paramref name="tab"/>은 처음 보일 탭(상점·업데이트면 온라인 목록을 바로 받는다).
    /// </summary>
    public static void Open(Window owner, string? kind = null, string? selectAssetId = null, AssetManagerTab tab = AssetManagerTab.Installed)
    {
        AppAssets.ShowProblems(owner);
        new AssetManagerWindow(kind, selectAssetId, tab).ShowDialog(owner);
    }

    /// <summary>
    /// 홈페이지 "Chickxel에서 열기": 상점 탭에서 온라인 목록을 새로 받아 패키지 <paramref name="packageId"/>를 고른다(거르기·검색은 비움).
    /// 설치는 사용자가 상세의 설치 단추로 한다. 에셋 관리자가 이미 열려 있으면 그 창에서 보인다.
    /// </summary>
    public static void ShowRemotePackage(Window owner, string packageId)
    {
        if (s_current is { } open)
        {
            open.ShowRemotePackage(packageId);
            open.Activate();
            return;
        }
        AppAssets.ShowProblems(owner);
        var window = new AssetManagerWindow(null, null, AssetManagerTab.Store);
        window.ShowRemotePackage(packageId);
        window.ShowDialog(owner);
    }

    /// <summary>
    /// 패키지 파일 더블클릭: 에셋 관리자 설치됨 탭에서 <paramref name="path"/>의 에셋을 가져오고(가져오기와 같음 — <see cref="AppAssets.InstallFile"/>)
    /// 처음 들어간 에셋을 고른다. 에셋 관리자가 이미 열려 있으면 그 창에서 한다.
    /// </summary>
    public static void InstallPackageFile(Window owner, string path)
    {
        if (s_current is { } open)
        {
            open.Activate();
            open.InstallFile(path);
            return;
        }
        AppAssets.ShowProblems(owner);
        var window = new AssetManagerWindow(null, null, AssetManagerTab.Installed);
        // 창이 뜬 뒤에 가져와 묻는 창·오류 상자가 에셋 관리자 위에 뜨게 한다.
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => window.InstallFile(path));
        window.ShowDialog(owner);
    }

    private AssetManagerWindow(string? kind, string? selectAssetId, AssetManagerTab tab) : base(UiStrings.Assets_Title)
    {
        s_current = this;
        CloseOnly();
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        Width = 980;
        Height = 660;
        MinWidth = 720;
        MinHeight = 460;
        _selectKey = selectAssetId is null ? null : AssetKey(selectAssetId);

        _filter = new TabStrip(TabStripKind.Segmented, UiStrings.Assets_FilterEffects, UiStrings.Assets_FilterGradients)
        {
            // 처음 종류: 준 종류, 없으면 고를 에셋의 종류, 그것도 없으면 효과.
            SelectedIndex = FilterIndex(kind ?? (selectAssetId is null ? null : AppAssets.Catalog.Find(selectAssetId)?.Kind)),
        };
        _filter.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
        _search = new TextBox { Width = 180, Watermark = UiStrings.Assets_Search };
        _search.TextChanged += (_, _) => Refresh();
        var import = Ui.Button(VectorIcon.Download, UiStrings.Assets_Import, (_, _) => Import()).Tip(UiStrings.Assets_ImportTip);
        var export = Ui.Button(VectorIcon.Upload, UiStrings.Assets_ExportShown, (_, _) => Export(Shown())).Tip(UiStrings.Assets_ExportShownTip);
        var publish = Ui.Button(VectorIcon.Globe, UiStrings.Assets_Publish, (_, _) => PublishSelected()).Tip(UiStrings.Assets_PublishTip);
        var restore = Ui.Button(VectorIcon.Undo2, UiStrings.Assets_RestoreOfficial, (sender, _) => RestoreOfficialEffects((Control)sender!))
            .Tip(UiStrings.Assets_RestoreOfficialTip);
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        toolbar.Children.AddRange([
            _filter.HelpAt(HelpPage, anchor: "Filter by Effects"), _search.HelpAt(HelpPage, anchor: "Filter by Effects"),
            import.HelpAt(HelpPage, anchor: "Import from File"), export.HelpAt(HelpPage, anchor: "Import from File"),
            publish.HelpAt(HelpPage, anchor: "Import from File"), restore.HelpAt(HelpPage, anchor: "Import from File"),
        ]);
        foreach (var child in toolbar.Children) child.Margin = new Thickness(0, 0, 8, 0);

        _list.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _selectKey = Selected is { } entry ? AssetKey(entry.Id) : null;
            ShowDetail();
        };
        _list.HelpAt(HelpPage, anchor: "The list shows My assets");
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("340,*"), Children = { _list, DetailScroller(_detail).HelpAt(HelpPage, anchor: "Shows the selected asset's preview") } };
        var installed = new DockPanel().HelpAt(HelpPage, anchor: "Installed tab");
        DockPanel.SetDock(toolbar, Dock.Top);
        installed.Children.Add(toolbar);
        installed.Children.Add(split);

        // 탭 머리(①)는 탭 칸 설명으로, 탭 안은 각 탭 절로.
        _updatesTab = new TabItem { Header = UiStrings.Assets_TabUpdates, Content = UpdatesTab() }.HelpAt(HelpPage, anchor: "There are Installed");
        _tabs.Items.Add(new TabItem { Header = UiStrings.Assets_TabInstalled, Content = installed }.HelpAt(HelpPage, anchor: "There are Installed"));
        _tabs.Items.Add(new TabItem { Header = UiStrings.Assets_TabStore, Content = StoreTab(_filter.SelectedIndex) }.HelpAt(HelpPage, anchor: "There are Installed"));
        _tabs.Items.Add(_updatesTab);
        this.HelpAt(HelpPage);
        // 온라인 목록은 상점·업데이트 탭을 처음 열 때 받는다(관리자를 열기만 해서는 요청하지 않음).
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex > 0 && !_remoteStarted) LoadRemote();
        };
        _tabs.SelectedIndex = (int)tab;
        if (tab != AssetManagerTab.Installed && !_remoteStarted) LoadRemote();

        var root = (DockPanel)Content!;
        root.Children.Remove(Body);
        root.Children.Add(_tabs);

        _onChanged = () => Dispatcher.UIThread.Post(() =>
        {
            Refresh();
            RefreshRemoteViews();
        });
        AppAssets.Changed += _onChanged;
        Closed += (_, _) =>
        {
            AppAssets.Changed -= _onChanged;
            _closing.Cancel();
            if (s_current == this) s_current = null;
        };
        Refresh();
    }

    /// <summary>
    /// 기본 효과 되돌리기: 지운 공식 효과를 다시 넣고, 낮은 버전(고치지 않은 것)은 앱에 든 버전으로 올린다(<see cref="AppAssets.SyncBundledAsync"/> —
    /// 패키지를 만들고 넣는 동안 단추를 끄고 백그라운드에서, 목록은 <see cref="AppAssets.Changed"/>로 다시 만들어진다).
    /// </summary>
    private async void RestoreOfficialEffects(Control button)
    {
        button.IsEnabled = false;
        OfficialEffectPackages.SyncResult result;
        try
        {
            result = await AppAssets.SyncBundledAsync(restore: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!_closing.IsCancellationRequested) Ui.Error(this, F(UiStrings.Assets_RestoreOfficialError, ex.Message));
            return;
        }
        finally
        {
            button.IsEnabled = true;
        }
        if (_closing.IsCancellationRequested) return;
        if (result.Errors.Count > 0)
            Ui.Warn(this, F(UiStrings.Assets_RestoreOfficialError, string.Join(Environment.NewLine, result.Errors.Select(e => $"{e.PackageId}: {e.Error}"))),
                UiStrings.Assets_RestoreOfficial);
        else
            Ui.Info(this, result.Changed
                ? F(UiStrings.Assets_RestoreOfficialDone, result.Installed.Count, result.Updated.Count)
                : UiStrings.Assets_RestoreOfficialNothing, UiStrings.Assets_RestoreOfficial);
    }

    private AssetEntry? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as AssetEntry;

    /// <summary>종류 거르기 칸 번호의 에셋 종류: 0 = 효과(효과 레시피와 스크립트 효과), 1 = 그라데이션. 상점 탭도 같은 칸을 쓴다.</summary>
    private static string[] FilterKinds(int index) => index == 1 ? [AssetKinds.Gradient] : [AssetKinds.EffectRecipe, AssetKinds.EffectScript];

    /// <summary>에셋 종류가 보이는 종류 거르기 칸 번호(모르는 종류는 효과 칸).</summary>
    private static int FilterIndex(string? kind) => kind == AssetKinds.Gradient ? 1 : 0;

    /// <summary>패키지가 보이는 종류 거르기 칸 번호(처음 나오는 효과·그라데이션 에셋 기준).</summary>
    private static int FilterIndex(IEnumerable<string> kinds)
        => FilterIndex(kinds.FirstOrDefault(k => k is AssetKinds.Gradient or AssetKinds.EffectRecipe or AssetKinds.EffectScript));

    private string[] KindFilter => FilterKinds(_filter.SelectedIndex);

    /// <summary>종류 거르기에 맞고 검색어가 이름(보이는 이름·원래 이름), 작성자 이름, 상점 목록 이름 중 하나에 들었는지.</summary>
    private bool Matches(AssetEntry entry)
    {
        if (!KindFilter.Contains(entry.Kind)) return false;
        string query = _search.Text?.Trim() ?? "";
        return query.Length == 0 || entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                 || entry.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                 || (entry.Author?.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
                                 || (entry.Store?.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    /// <summary>목록에 보이는 에셋(거르기·검색에 맞는 것, 저장소 순서).</summary>
    private List<AssetEntry> Shown() => AppAssets.Catalog.All.Where(Matches).ToList();

    /// <summary>카탈로그에서 목록(내 에셋, 받은 에셋)을 다시 만들고 <see cref="_selectKey"/>를 고른다(없으면 첫 항목).</summary>
    private void Refresh()
    {
        string? handle = AppAssets.MyHandle;
        _refreshing = true;
        try
        {
            _list.Items.Clear();
            var shown = Shown();
            var mine = shown.Where(entry => entry.IsOwnedBy(handle)).ToList();
            var received = shown.Where(entry => !entry.IsOwnedBy(handle)).ToList();
            if (mine.Count > 0)
            {
                AddHeader(UiStrings.Assets_PersonalHeader);
                foreach (var entry in mine) AddRow(entry, AssetRow(entry, mine: true));
            }
            if (received.Count > 0)
            {
                AddHeader(UiStrings.Assets_ReceivedHeader);
                foreach (var entry in received) AddRow(entry, AssetRow(entry, mine: false));
            }
            if (_list.Items.Count == 0)
                _list.Items.Add(new ListBoxItem { Content = Ui.Label(UiStrings.Assets_EmptyInstalled), IsEnabled = false });

            var rows = _list.Items.OfType<ListBoxItem>().Where(item => item.Tag is AssetEntry).ToList();
            var target = rows.FirstOrDefault(item => AssetKey(((AssetEntry)item.Tag!).Id) == _selectKey) ?? rows.FirstOrDefault();
            _list.SelectedItem = target;
            _selectKey = target?.Tag is AssetEntry selected ? AssetKey(selected.Id) : null;
        }
        finally { _refreshing = false; }
        ShowDetail();
        if (_list.SelectedItem is { } selectedItem) _list.ScrollIntoView(selectedItem);
    }

    private void AddHeader(string text)
    {
        var label = Ui.Label(text, bold: true);
        label.Margin = new Thickness(0, 6, 0, 0);
        _list.Items.Add(new ListBoxItem { Content = label, IsEnabled = false, Focusable = false });
    }

    private void AddRow(AssetEntry entry, Control content)
        => _list.Items.Add(new ListBoxItem { Content = content, Tag = entry, HorizontalContentAlignment = HorizontalAlignment.Stretch });

    private static TextBlock Dim(string text) => new()
    {
        Text = text,
        Foreground = AppTheme.DimTextBrush,
        FontSize = AppTheme.SmallFontSize,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>
    /// 에셋 줄: 즐겨찾기 별, 종류 아이콘, 이름(그라데이션은 띠). 아래에는 작성자 배지(받은 에셋만 프로필 그림)와
    /// 받은 에셋의 상점 버전 또는 올린 내 에셋의 버전·고침 상태를 보인다.
    /// </summary>
    private static Control AssetRow(AssetEntry entry, bool mine)
    {
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (entry.Favorite) icons.Children.Add(Ui.Icon(VectorIcon.Star, 12, filled: true));
        icons.Children.Add(Ui.Icon(KindIcon(entry.Kind), 13));
        var name = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,6,*") };
        name.Children.Add(icons);
        var title = new TextBlock { Text = entry.DisplayName, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(title, 2);
        name.Children.Add(title);
        var panel = new StackPanel { Children = { name } };
        if (entry.Gradient is { } gradient)
            panel.Children.Add(new GradientSwatch { Preset = gradient, Height = 10, Margin = new Thickness(0, 3, 0, 1) });
        string authorName = entry.Author?.Name ?? (entry.Store is null ? UiStrings.Assets_SourceLocal : "");
        string?[] parts = mine
            ? [entry.Store is { } store ? F(UiStrings.Assets_PublishedVersion, store.Version) : null, entry.Modified ? UiStrings.Assets_ModifiedNote : null]
            : [entry.Store?.Version];
        panel.Children.Add(IdentityBadge.Author(entry.Store is not null ? entry.Author : null, 16, fallback: authorName, dim: true,
            fontSize: AppTheme.SmallFontSize, caption: string.Join(" · ", parts.Where(s => !string.IsNullOrEmpty(s)))));
        return panel;
    }

    /// <summary>에셋 종류 아이콘(그라데이션, 스크립트 효과, 효과 프리셋).</summary>
    internal static VectorIcon KindIcon(string kind) => kind switch
    {
        AssetKinds.Gradient => VectorIcon.SlidersHorizontal,
        AssetKinds.EffectScript => VectorIcon.Code,
        _ => VectorIcon.Sparkles,
    };
}
