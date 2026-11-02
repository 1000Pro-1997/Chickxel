using System.Globalization;
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
/// 에셋 관리자(계획 8장): 설치됨·상점·업데이트 탭. 설치됨 탭은 왼쪽에 개인 에셋, 설치한 패키지(와 그 에셋), 최근 삭제한 패키지 목록,
/// 오른쪽에 고른 항목의 상세(미리보기, 작성자, 버전, 호환 앱 버전, 출처)와 동작(켜기·끄기, 내보내기, 되돌리기, 파일 위치, 삭제 …)을 둔다.
/// 종류 거르기(전체·효과·그라데이션)와 이름 검색은 메모리 카탈로그에서 한다. 상점·업데이트는 온라인 카탈로그(<c>.Store</c>)를 쓴다.
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
    /// <summary>다시 만든 뒤 고를 항목(<see cref="RowKey"/>).</summary>
    private string? _selectKey;
    private bool _refreshing;

    /// <summary>목록 한 줄: 머리글, 패키지, 에셋, 삭제한 패키지.</summary>
    private sealed record Row(string Key, InstalledPackage? Package = null, AssetEntry? Asset = null, DeletedPackage? Deleted = null);

    /// <summary>열려 있는 에셋 관리자(모달이라 하나뿐). 없으면 null — 홈페이지 링크·패키지 파일이 열려 있는 창으로 가게.</summary>
    private static AssetManagerWindow? s_current;

    /// <summary>
    /// 에셋 관리자를 연다. <paramref name="kind"/>는 처음 종류 거르기(<see cref="AssetKinds"/>, null = 전체 — 설치됨·상점 탭이 같이 씀),
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
    /// 패키지 파일 더블클릭: 에셋 관리자 설치됨 탭에서 <paramref name="path"/>를 설치하고(가져오기와 같음) 그 패키지를 고른다.
    /// 에셋 관리자가 이미 열려 있으면 그 창에서 한다.
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
        // 창이 뜬 뒤에 설치해 오류 상자가 에셋 관리자 위에 뜨게 한다.
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => window.InstallFile(path));
        window.ShowDialog(owner);
    }

    private AssetManagerWindow(string? kind, string? selectAssetId, AssetManagerTab tab) : base(UiStrings.Assets_Title)
    {
        s_current = this;
        CloseOnly();
        CancelButtonControl.IsDefault = false;
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        Width = 980;
        Height = 660;
        MinWidth = 720;
        MinHeight = 460;
        _selectKey = selectAssetId is null ? null : "asset:" + selectAssetId;

        _filter = new TabStrip(TabStripKind.Segmented, UiStrings.Assets_FilterEffects, UiStrings.Assets_FilterGradients)
        {
            // 처음 종류: 준 종류, 없으면 고를 에셋의 종류, 그것도 없으면 효과.
            SelectedIndex = FilterIndex(kind ?? (selectAssetId is null ? null : AppAssets.Catalog.Find(selectAssetId)?.Kind)),
        };
        _filter.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
        _search = new TextBox { Width = 180, Watermark = UiStrings.Assets_Search };
        _search.TextChanged += (_, _) => Refresh();
        var import = Ui.Button(VectorIcon.Download, UiStrings.Assets_Import, (_, _) => Import());
        ToolTip.SetTip(import, UiStrings.Assets_ImportTip);
        var exportPersonal = Ui.Button(VectorIcon.Upload, UiStrings.Assets_ExportPersonal, (_, _) => ExportPersonal(null));
        ToolTip.SetTip(exportPersonal, UiStrings.Assets_ExportPersonalTip);
        var publish = Ui.Button(VectorIcon.Globe, UiStrings.Assets_Publish, (_, _) => PublishPersonal(null)).Tip(UiStrings.Assets_PublishTip);
        var restore = Ui.Button(VectorIcon.Undo2, UiStrings.Assets_RestoreOfficial, (sender, _) => RestoreOfficialEffects((Control)sender!))
            .Tip(UiStrings.Assets_RestoreOfficialTip);
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        toolbar.Children.AddRange([_filter, _search, import, exportPersonal, publish, restore]);
        foreach (var child in toolbar.Children) child.Margin = new Thickness(0, 0, 8, 0);

        _list.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _selectKey = Selected?.Key;
            ShowDetail();
        };
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("340,*"), Children = { _list, DetailScroller(_detail) } };
        var installed = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        installed.Children.Add(toolbar);
        installed.Children.Add(split);

        _updatesTab = new TabItem { Header = UiStrings.Assets_TabUpdates, Content = UpdatesTab() };
        _tabs.Items.Add(new TabItem { Header = UiStrings.Assets_TabInstalled, Content = installed });
        _tabs.Items.Add(new TabItem { Header = UiStrings.Assets_TabStore, Content = StoreTab(_filter.SelectedIndex) });
        _tabs.Items.Add(_updatesTab);
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
    /// 기본 효과 되돌리기: 지운 공식 효과 패키지를 다시 설치하고, 끈 것은 켜고, 낮은 버전은 앱에 든 버전으로 올린다(<see cref="AppAssets.SyncBundledAsync"/> —
    /// 패키지를 만들고 설치하는 동안 단추를 끄고 백그라운드에서, 목록은 <see cref="AppAssets.Changed"/>로 다시 만들어진다).
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
                ? F(UiStrings.Assets_RestoreOfficialDone, result.Installed.Count, result.Updated.Count + result.Enabled.Count)
                : UiStrings.Assets_RestoreOfficialNothing, UiStrings.Assets_RestoreOfficial);
    }

    private Row? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as Row;

    /// <summary>종류 거르기 칸 번호의 에셋 종류: 0 = 효과(효과 레시피와 스크립트 효과), 1 = 그라데이션. 상점 탭도 같은 칸을 쓴다.</summary>
    private static string[] FilterKinds(int index) => index == 1 ? [AssetKinds.Gradient] : [AssetKinds.EffectRecipe, AssetKinds.EffectScript];

    /// <summary>에셋 종류가 보이는 종류 거르기 칸 번호(모르는 종류는 효과 칸).</summary>
    private static int FilterIndex(string? kind) => kind == AssetKinds.Gradient ? 1 : 0;

    /// <summary>패키지가 보이는 종류 거르기 칸 번호(처음 나오는 효과·그라데이션 에셋 기준).</summary>
    private static int FilterIndex(IEnumerable<string> kinds)
        => FilterIndex(kinds.FirstOrDefault(k => k is AssetKinds.Gradient or AssetKinds.EffectRecipe or AssetKinds.EffectScript));

    private string[] KindFilter => FilterKinds(_filter.SelectedIndex);

    private bool Matches(AssetEntry entry)
    {
        if (!KindFilter.Contains(entry.Kind)) return false;
        string query = _search.Text?.Trim() ?? "";
        return query.Length == 0 || entry.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                 || entry.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                 || (entry.PackageName?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    /// <summary>
    /// 패키지 줄을 보일지: 보이는 에셋이 있거나, 종류 거르기에 맞고 이름이 검색어에 맞을 때.
    /// 읽지 못한 패키지와 효과·그라데이션이 하나도 없는 패키지는 어느 칸에서나 보인다(지우거나 고칠 수 있게).
    /// </summary>
    private bool PackageVisible(InstalledPackage package, int shownEntries)
    {
        if (shownEntries > 0) return true;
        bool kindOk = package.Entries.Any(e => KindFilter.Contains(e.Kind))
                      || !package.Entries.Any(e => e.Kind is AssetKinds.Gradient or AssetKinds.EffectRecipe or AssetKinds.EffectScript);
        string query = _search.Text?.Trim() ?? "";
        return kindOk && (query.Length == 0 || package.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                            || package.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>카탈로그에서 목록을 다시 만들고 <see cref="_selectKey"/>를 고른다(없으면 첫 항목).</summary>
    private void Refresh()
    {
        var catalog = AppAssets.Catalog;
        _refreshing = true;
        try
        {
            _list.Items.Clear();
            var personal = catalog.Personal.Where(Matches).ToList();
            if (personal.Count > 0)
            {
                AddHeader(UiStrings.Assets_PersonalHeader);
                foreach (var entry in personal) AddRow(new Row("asset:" + entry.Id, Asset: entry), AssetRow(entry, indent: false));
            }
            bool packagesHeader = false;
            foreach (var package in catalog.Packages)
            {
                var entries = package.Entries.Where(Matches).ToList();
                if (!PackageVisible(package, entries.Count)) continue;
                if (!packagesHeader)
                {
                    AddHeader(UiStrings.Assets_PackagesHeader);
                    packagesHeader = true;
                }
                AddRow(new Row("pkg:" + package.Id, Package: package), PackageRow(package));
                foreach (var entry in entries) AddRow(new Row("asset:" + entry.Id, Asset: entry), AssetRow(entry, indent: true));
            }
            var deleted = AppAssets.Store.DeletedPackages;
            if (deleted.Count > 0)
            {
                AddHeader(UiStrings.Assets_DeletedHeader);
                foreach (var item in deleted) AddRow(new Row("deleted:" + item.Folder, Deleted: item), DeletedRow(item));
            }
            if (_list.Items.Count == 0)
                _list.Items.Add(new ListBoxItem { Content = Ui.Label(UiStrings.Assets_EmptyInstalled), IsEnabled = false });

            var rows = _list.Items.OfType<ListBoxItem>().Where(item => item.Tag is Row).ToList();
            var target = rows.FirstOrDefault(item => ((Row)item.Tag!).Key == _selectKey) ?? rows.FirstOrDefault();
            _list.SelectedItem = target;
            _selectKey = (target?.Tag as Row)?.Key;
        }
        finally { _refreshing = false; }
        ShowDetail();
        if (_list.SelectedItem is { } selected) _list.ScrollIntoView(selected);
    }

    private void AddHeader(string text)
    {
        var label = Ui.Label(text, bold: true);
        label.Margin = new Thickness(0, 6, 0, 0);
        _list.Items.Add(new ListBoxItem { Content = label, IsEnabled = false, Focusable = false });
    }

    private void AddRow(Row row, Control content)
        => _list.Items.Add(new ListBoxItem { Content = content, Tag = row, HorizontalContentAlignment = HorizontalAlignment.Stretch });

    private static TextBlock Dim(string text) => new()
    {
        Text = text,
        Foreground = AppTheme.DimTextBrush,
        FontSize = AppTheme.SmallFontSize,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static Control AssetRow(AssetEntry entry, bool indent)
    {
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (entry.Favorite) name.Children.Add(Ui.Icon(VectorIcon.Star, 12, filled: true));
        name.Children.Add(Ui.Icon(entry.Kind switch
        {
            AssetKinds.Gradient => VectorIcon.SlidersHorizontal,
            AssetKinds.EffectScript => VectorIcon.Code,
            _ => VectorIcon.Sparkles,
        }, 13));
        name.Children.Add(new TextBlock { Text = entry.DisplayName, Foreground = AppTheme.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis });
        var panel = new StackPanel { Margin = new Thickness(indent ? 18 : 0, 0, 0, 0), Children = { name } };
        if (entry.Gradient is { } gradient)
            panel.Children.Add(new GradientSwatch { Preset = gradient, Height = 10, Margin = new Thickness(0, 3, 0, 1) });
        if (!entry.Enabled) panel.Opacity = 0.55;
        return panel;
    }

    private static Control PackageRow(InstalledPackage package)
    {
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        name.Children.Add(Ui.Icon(VectorIcon.Package, 14));
        name.Children.Add(Ui.Label(package.DisplayName, bold: true));
        string status = package.IsBroken ? UiStrings.Assets_Broken : package.Enabled ? "" : UiStrings.Assets_Disabled;
        var sub = Dim(string.Join(" · ", new[] { package.Version, package.Manifest?.Author.Name, status }.Where(s => !string.IsNullOrEmpty(s))));
        if (package.IsBroken) sub.Foreground = AppTheme.WarningBrush;
        var panel = new StackPanel { Children = { name, sub } };
        if (!package.Enabled) panel.Opacity = 0.7;
        return panel;
    }

    private static Control DeletedRow(DeletedPackage deleted)
    {
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        name.Children.Add(Ui.Icon(VectorIcon.Trash, 13));
        name.Children.Add(new TextBlock { Text = deleted.Name, Foreground = AppTheme.DimTextBrush, TextTrimming = TextTrimming.CharacterEllipsis });
        return new StackPanel
        {
            Children = { name, Dim(deleted.Version + " · " + deleted.DeletedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) },
        };
    }
}
