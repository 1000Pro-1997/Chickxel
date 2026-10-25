using Avalonia.VisualTree;
using Gpixel.App.Localization;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.App;

// 문서 탭과 좌우 나눔(VS Code 편집기 묶음처럼 최대 2개).
// 열린 문서(OpenDocument)마다 프로젝트 인스턴스 하나, 탭(DocumentTab)은 문서를 보는 창 하나 — 같은 문서를 두 묶음에서 보면 탭 두 개가
// 프로젝트(내용·실행취소 기록)를 같이 쓰고 보기 상태(확대율·위치·프레임·레이어·선택)는 탭마다 따로다.
// 편집기(EditorContext·캔버스·타임라인·미리보기·패널)는 하나: 포커스된 묶음이 고른 탭의 문서를 싣고, 캔버스는 그 묶음 칸으로 옮겨 간다.
// 다른 묶음 칸은 그 묶음이 고른 탭을 그리기만 한다(DocumentPaneView) — 누르면 그 묶음에 포커스가 간다.
public sealed partial class MainForm
{
    private enum TabDrop { None, SplitRight, OtherGroup }

    private readonly TabGroups<DocumentTab> _tabs = new();
    private readonly DocumentPane[] _panes = [new(), new()];
    private readonly Grid _paneArea = new() { Background = AppTheme.BackBrush };
    private readonly ColumnDefinition _paneCol0 = new(1, GridUnitType.Star) { MinWidth = 120 };
    private readonly ColumnDefinition _paneCol1 = new(0, GridUnitType.Pixel);
    private readonly GridSplitter _paneSplitter = Splitter(GridResizeDirection.Columns);
    /// <summary>탭을 캔버스 쪽으로 끄는 동안 놓을 자리(오른쪽 반 = 나누기, 다른 묶음 = 옮기기) 강조(<see cref="BuildPaneArea"/>가 만듦).</summary>
    private Border _tabDropHint = null!;
    private readonly TextBlock _tabDropText = new()
    {
        Foreground = AppTheme.TextBrush,
        FontWeight = FontWeight.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>편집기에 실린 탭(포커스된 묶음이 고른 탭). 탭이 없으면 null.</summary>
    private DocumentTab? _activeTab;
    /// <summary>캔버스 쪽으로 끄는 중인 탭(끌기를 시작할 때 잡아 둔다 — 놓기 전에 순서가 바뀌어도 같은 탭).</summary>
    private DocumentTab? _draggedTab;

    /// <summary>지금 문서(편집기에 실린 탭의 문서).</summary>
    private OpenDocument? CurrentDoc => _activeTab?.Doc;

    /// <summary>열린 문서들(탭 순서, 같은 문서는 한 번).</summary>
    private List<OpenDocument> OpenDocs => _tabs.All.Select(t => t.Doc).Distinct().ToList();

    /// <summary>캔버스 자리: 묶음 칸 둘(나누지 않으면 하나)과 사이 나눔 막대, 시작 화면, 탭 끌기 강조.</summary>
    private Control BuildPaneArea()
    {
        _paneArea.ColumnDefinitions.Add(_paneCol0);
        _paneArea.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _paneArea.ColumnDefinitions.Add(_paneCol1);
        for (int i = 0; i < _panes.Length; i++)
        {
            int g = i;
            var pane = _panes[i];
            var strip = pane.Strip;
            strip.TabPressed += index => { if (TabAt(g, index) is { } t) ActivateTab(t); };
            strip.CloseRequested += index => { if (TabAt(g, index) is { } t) Dispatcher.UIThread.Post(() => CloseTab(t)); };
            strip.NewRequested += () =>
            {
                FocusGroup(g);
                Dispatcher.UIThread.Post(() => NewProject(null));
            };
            strip.SplitRequested += () => { if (_tabs.Active(g) is { } t) SplitTab(t, move: false); };
            strip.Reordered += (from, to) =>
            {
                if (g >= _tabs.GroupCount) return;
                _tabs.Move(g, from, to);
                RefreshPanes();
            };
            strip.TabDragMoved += (index, e) => DragTabOver(g, index, e);
            strip.TabDragEnded += (_, e) => DropTab(e);
            strip.MenuFor = index => TabAt(g, index) is { } t ? TabMenu(t) : null;
            // 다른 묶음 칸을 누르면 그 묶음에 포커스(탭 줄은 탭을 누른 처리가 맡는다).
            pane.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.Source is Visual v && v.FindAncestorOfType<DocumentTabStrip>(includeSelf: true) is not null) return;
                FocusGroup(g);
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            Grid.SetColumn(pane, i * 2);
            _paneArea.Children.Add(pane);
        }
        _panes[1].IsVisible = false;
        _paneSplitter.IsVisible = false;
        Grid.SetColumn(_paneSplitter, 1);
        _paneArea.Children.Add(_paneSplitter);
        _panes[0].Content.Children.Add(_canvas);
        _panes[0].Passive.IsVisible = false;

        var accent = AppTheme.AccentColor;
        _tabDropHint = new Border
        {
            Background = AppTheme.Brush(Color.FromArgb(48, accent.R, accent.G, accent.B)),
            BorderBrush = AppTheme.AccentBrush,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ZIndex = 50,
            Child = _tabDropText,
        };
        Grid.SetColumnSpan(_tabDropHint, 3);
        _paneArea.Children.Add(_tabDropHint);
        Grid.SetColumnSpan(_start, 3);
        _start.ZIndex = 100;
        _paneArea.Children.Add(_start);
        AttachCanvasDrop(_paneArea);
        return _paneArea;
    }

    private DocumentTab? TabAt(int group, int index)
        => group < _tabs.GroupCount && index >= 0 && index < _tabs.Tabs(group).Count ? _tabs.Tabs(group)[index] : null;

    /// <summary>탭 줄의 풍선 도움말(단축키 표기가 바뀌면 다시).</summary>
    private void ApplyTabTips()
    {
        foreach (var pane in _panes)
            pane.Strip.SetTips(KeyHint(MainStrings.Tabs_Close, "파일/닫기"), KeyHint(MainStrings.Tabs_New, "파일/새 프로젝트…"), MainStrings.Tabs_SplitTip);
    }

    // ───────────── 열기·고르기 ─────────────

    /// <summary>경로(프로젝트 파일 또는 연 PNG)의 열린 문서(없으면 null).</summary>
    private OpenDocument? FindDoc(string path) => OpenDocs.Find(d => d.Matches(path));

    /// <summary>문서를 포커스된 묶음의 새 탭으로 연다(고른 탭 바로 뒤).</summary>
    private void OpenInNewTab(OpenDocument doc)
    {
        if (!_tabs.All.Any(t => ReferenceEquals(t.Doc, doc))) doc.Project.DirtyChanged += OnDocumentDirtyChanged;
        _tabs.Add(new DocumentTab(doc), _tabs.Focused);
        ShowActiveTab();
    }

    /// <summary>열린 문서의 탭을 고른다(포커스된 묶음에 있으면 그것, 아니면 다른 묶음의 탭).</summary>
    private void ShowDocument(OpenDocument doc)
    {
        var tab = _tabs.Tabs(_tabs.Focused).FirstOrDefault(t => ReferenceEquals(t.Doc, doc))
                  ?? _tabs.All.FirstOrDefault(t => ReferenceEquals(t.Doc, doc));
        if (tab is null) OpenInNewTab(doc);
        else ActivateTab(tab);
    }

    private void ActivateTab(DocumentTab tab)
    {
        if (!_tabs.Activate(tab)) return;
        ShowActiveTab();
    }

    /// <summary>묶음에 포커스를 준다(그 묶음이 고른 탭이 편집기에 실리고 캔버스가 그 칸으로 간다).</summary>
    private void FocusGroup(int group)
    {
        if (group == _tabs.Focused || group >= _tabs.GroupCount) return;
        _tabs.Focus(group);
        ShowActiveTab();
        _canvas.Focus();
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab: 포커스된 묶음의 다음·이전 탭. 탭이 없으면 false.</summary>
    private bool CycleTab(int delta)
    {
        if (_tabs.Tabs(_tabs.Focused).Count == 0) return false;
        _tabs.Cycle(delta);
        ShowActiveTab();
        return true;
    }

    /// <summary>
    /// 편집기를 포커스된 묶음의 고른 탭에 맞춘다: 떠나는 탭의 보기 상태를 저장하고, 새 탭의 문서를 싣고 보기 상태를 되돌린 뒤
    /// 캔버스를 그 묶음 칸으로 옮긴다. 탭 줄·다른 묶음 칸·제목·상태 표시줄도 맞춘다.
    /// </summary>
    private void ShowActiveTab()
    {
        var target = _tabs.FocusedActive;
        bool changed = !ReferenceEquals(target, _activeTab);
        if (changed)
        {
            StoreActiveView();
            _activeTab = target;
        }
        MoveCanvasTo(_tabs.Focused);
        if (changed)
        {
            if (target is null) { if (_ctx.Project is not null) _ctx.SetProject(null); }
            else LoadTab(target);
            _browser.SelectFile(CurrentTilePath);
        }
        RefreshPanes();
        UpdateAll();
        SyncUnsaved();
    }

    /// <summary>편집기에 실린 탭의 보기 상태(레이어·프레임·확대율·위치·선택)를 탭에 저장한다. 떠 있는 선택은 먼저 내려놓는다.</summary>
    private void StoreActiveView()
    {
        if (_activeTab is not { } tab || !ReferenceEquals(_ctx.Project, tab.Doc.Project)) return;
        _ctx.CommitFloating();
        var (zoom, offset) = _canvas.View;
        tab.View = new DocumentView(_ctx.CurrentLayer, _ctx.CurrentFrame, zoom, offset);
        tab.Selection = _ctx.Selection.HasSelection ? _ctx.Selection.Save() : null;
        // 저장 안 한 문서는 파일 목록에 메모리 속 그림을 보인다.
        if (tab.Doc.Project.IsDirty) tab.Doc.Thumbnail = tab.Doc.Project.Flatten(0);
    }

    private static readonly SelectionState NoSelection = new(Rectangle.Empty, null, null);

    /// <summary>탭의 문서를 편집기에 싣고 탭의 보기 상태를 되돌린다(처음 보이는 탭은 화면에 맞춤).</summary>
    private void LoadTab(DocumentTab tab)
    {
        var doc = tab.Doc;
        // 안 보이는 동안 밖에서 바뀐 파일: 수정이 없으면 디스크에서 다시 읽는다.
        if (doc.ChangedOnDisk)
        {
            doc.ChangedOnDisk = false;
            if (!doc.Project.IsDirty && doc.DiskPath is { } disk && File.Exists(disk) && ReloadDocument(doc, disk, interactive: false, out _))
                ShowMessage(MainStrings.FormatTabs_Reloaded(name: Path.GetFileName(disk)));
        }
        if (!ReferenceEquals(_ctx.Project, doc.Project)) _ctx.SetProject(doc.Project);
        else _ctx.Selection.Restore(NoSelection);
        if (tab.View is { } v)
        {
            _ctx.SetCurrent(v.Layer, v.Frame);
            _canvas.RestoreView((v.Zoom, v.Offset));
        }
        if (tab.Selection is { } s && new Rectangle(0, 0, doc.Project.Width, doc.Project.Height).Contains(s.Bounds))
            _ctx.Selection.Restore(s);
    }

    /// <summary>캔버스를 묶음 칸으로 옮기고, 다른 묶음 칸에는 그 묶음이 고른 탭의 그림을 둔다.</summary>
    private void MoveCanvasTo(int group)
    {
        var host = _panes[group].Content;
        if (_canvas.Parent is Panel old && !ReferenceEquals(old, host))
        {
            bool focused = _canvas.IsFocused;
            old.Children.Remove(_canvas);
            host.Children.Add(_canvas);
            if (focused) _canvas.Focus();
        }
        else if (_canvas.Parent is null) host.Children.Add(_canvas);
    }

    /// <summary>탭 줄들(제목·저장 안 한 점·고른 탭·포커스)과 나눔 배치, 다른 묶음 칸의 그림을 맞춘다.</summary>
    private void RefreshPanes()
    {
        bool split = _tabs.IsSplit;
        if (_panes[1].IsVisible != split)
        {
            _panes[1].IsVisible = split;
            _paneSplitter.IsVisible = split;
            // 나눌 때마다 반씩.
            _paneCol1.Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _paneCol0.Width = new GridLength(1, GridUnitType.Star);
            _paneCol1.MinWidth = split ? 120 : 0;
        }
        for (int g = 0; g < _panes.Length; g++)
        {
            var pane = _panes[g];
            bool exists = g < _tabs.GroupCount;
            var tabs = exists ? _tabs.Tabs(g) : [];
            pane.Strip.SetTabs(tabs.Select(TabInfo).ToList(), exists ? _tabs.ActiveIndex(g) : -1);
            bool focused = g == _tabs.Focused;
            pane.Strip.SetFocus(focused, split && focused);
            pane.Passive.IsVisible = !focused;
            pane.Passive.Tab = focused || !exists ? null : _tabs.Active(g);
            if (!focused) pane.Passive.Refresh();
        }
    }

    private static DocumentTabInfo TabInfo(DocumentTab tab)
    {
        var doc = tab.Doc;
        bool dirty = doc.Project.IsDirty;
        var path = doc.DiskPath;
        var title = path is null ? MainStrings.Ui_286 : Path.GetFileName(path);
        var tip = path ?? title;
        if (dirty) tip += "\n" + MainStrings.Tabs_UnsavedTip;
        return new DocumentTabInfo(title, tip, dirty);
    }

    /// <summary>열린 문서의 수정됨 표시가 바뀜: 저장했으면 메모리 썸네일을 버리고 탭 점·파일 목록 점을 맞춘다.</summary>
    private void OnDocumentDirtyChanged(object? sender, EventArgs e)
    {
        if (sender is SpriteProject { IsDirty: false } p && OpenDocs.Find(d => ReferenceEquals(d.Project, p)) is { } doc) doc.Thumbnail = null;
        RefreshPanes();
        SyncUnsaved();
    }

    /// <summary>
    /// 문서의 프로젝트를 새 인스턴스로 바꾼다(되돌리기·다시 읽기·프레임 나누기). 편집기에 실린 문서면 바로 싣고,
    /// <paramref name="keepView"/>면 레이어·프레임·확대율·위치를 그대로 둔다.
    /// </summary>
    private void ReplaceProject(OpenDocument doc, SpriteProject project, bool keepView = true)
    {
        var old = doc.Project;
        old.DirtyChanged -= OnDocumentDirtyChanged;
        doc.Project = project;
        project.DirtyChanged += OnDocumentDirtyChanged;
        if (ReferenceEquals(_ctx.Project, old))
        {
            var (layer, frame, view) = (_ctx.CurrentLayer, _ctx.CurrentFrame, _canvas.View);
            _ctx.SetProject(project);
            if (keepView)
            {
                _ctx.SetCurrent(layer, frame);
                _canvas.RestoreView(view);
            }
        }
        RefreshPanes();
        SyncUnsaved();
    }

    /// <summary>
    /// 디스크 파일을 다시 읽어 문서의 프로젝트를 바꾼다(수정·실행취소 기록은 버림 — 남겨 둔 기록이 있으면 이어 받음). 못 읽으면 false(문서는 그대로).
    /// </summary>
    private bool ReloadDocument(OpenDocument doc, string path, bool interactive, out string message)
    {
        if (ReadProject(path, interactive, out message) is not { } result) return false;
        if (!result.Imported) _history.Restore(result.Project);
        doc.SourcePath = result.Imported ? result.SourcePngPath : null;
        doc.Thumbnail = null;
        doc.ChangedOnDisk = false;
        ReplaceProject(doc, result.Project);
        doc.DiskStamp = doc.DiskPath is { } disk ? SafeStamp(disk) : default;
        return true;
    }

    // ───────────── 닫기 ─────────────

    /// <summary>
    /// 탭을 닫는다. 그 문서를 보는 마지막 탭이고 저장 안 한 변경이 있으면 저장·저장 안 함·취소를 묻는다(<see cref="UnsavedChangesDialog"/>).
    /// 닫았으면 true.
    /// </summary>
    private bool CloseTab(DocumentTab tab)
    {
        if (_tabs.GroupOf(tab) < 0) return false;
        var doc = tab.Doc;
        bool lastView = !_tabs.All.Any(t => !ReferenceEquals(t, tab) && ReferenceEquals(t.Doc, doc));
        if (ReferenceEquals(tab, _activeTab)) _ctx.CommitFloating();
        if (lastView && doc.Project.IsDirty && !ConfirmCloseDocument(doc)) return false;
        if (ReferenceEquals(tab, _activeTab)) _activeTab = null;
        _tabs.Remove(tab);
        if (lastView) ForgetDocument(doc);
        ShowActiveTab();
        if (_activeTab is null) _browser.SelectFile(null);
        return true;
    }

    /// <summary>Ctrl+W: 지금 탭을 닫는다.</summary>
    private void CloseActiveTab()
    {
        if (_activeTab is { } tab) CloseTab(tab);
    }

    /// <summary>같은 묶음의 다른 탭을 모두 닫는다(저장 안 한 문서는 하나씩 묻고, 취소하면 멈춤).</summary>
    private void CloseOtherTabs(DocumentTab keep)
    {
        int g = _tabs.GroupOf(keep);
        if (g < 0) return;
        foreach (var tab in _tabs.Tabs(g).Where(t => !ReferenceEquals(t, keep)).ToList())
            if (!CloseTab(tab)) return;
        ActivateTab(keep);
    }

    /// <summary>모든 탭을 닫는다(저장 안 한 문서는 한 창에 모아 묻는다 — <see cref="ConfirmCloseAll"/>). 닫았으면 true.</summary>
    private bool CloseAllTabs()
    {
        if (!ConfirmCloseAll()) return false;
        _ctx.CommitFloating();
        var docs = OpenDocs;
        _activeTab = null;
        _tabs.Clear();
        foreach (var doc in docs) ForgetDocument(doc);
        ShowActiveTab();
        _browser.SelectFile(null);
        return true;
    }

    /// <summary>저장 안 한 문서를 닫기 전에 묻는다. 저장(실패하면 닫지 않음)·저장 안 함이면 true, 취소면 false.</summary>
    private bool ConfirmCloseDocument(OpenDocument doc)
    {
        var p = doc.Project;
        var dlg = new UnsavedChangesDialog([DescribeUnsaved(p, doc.SourcePath, doc.Thumbnail ?? p.Flatten(0))]);
        // 묻는 동안 자동 저장이 "저장 안 함"으로 고를 문서를 써 버리지 않게 멈춘다.
        _autoSave.Stop();
        switch (dlg.ShowDialog(this))
        {
            case DialogResult.No:
                return true;
            case DialogResult.Yes:
                if (dlg.Checked.Count == 0 || Save(p)) return true;
                RestartAutoSave();
                return false;
            default:
                RestartAutoSave();
                return false;
        }
    }

    /// <summary>마지막 탭을 닫은 문서: 저장한 그대로면 실행취소 기록을 남기고 수정됨 알림을 끊는다.</summary>
    private void ForgetDocument(OpenDocument doc)
    {
        doc.Project.DirtyChanged -= OnDocumentDirtyChanged;
        _history.Leaving(doc.Project);
    }

    // ───────────── 나누기 ─────────────

    /// <summary>
    /// 탭을 다른 묶음에서도 연다(나누지 않았으면 오른쪽에 새 묶음). <paramref name="move"/>면 옮기고(원래 묶음에 탭이 하나뿐이고 나누지 않았으면 복제),
    /// 아니면 같은 문서의 탭을 하나 더 만든다(보기 상태를 이어 받음). 다른 묶음에 이미 그 문서의 탭이 있으면 그 탭을 고른다.
    /// </summary>
    private void SplitTab(DocumentTab tab, bool move)
    {
        int from = _tabs.GroupOf(tab);
        if (from < 0) return;
        StoreActiveView();
        int target = _tabs.OtherGroup(from);
        var existing = target < _tabs.GroupCount ? _tabs.Tabs(target).FirstOrDefault(t => ReferenceEquals(t.Doc, tab.Doc)) : null;
        bool canMove = move && (_tabs.IsSplit || _tabs.Tabs(from).Count > 1);
        if (existing is not null)
        {
            if (canMove)
            {
                if (ReferenceEquals(tab, _activeTab)) _activeTab = null;
                _tabs.Remove(tab);
            }
            _tabs.Activate(existing);
        }
        else if (canMove) _tabs.MoveToGroup(tab, target);
        else _tabs.Add(new DocumentTab(tab.Doc) { View = tab.View }, target);
        ShowActiveTab();
    }

    /// <summary>탭 우클릭 메뉴: 닫기·다른 탭 닫기·오른쪽으로 분할(다른 쪽에도 열기)·다른 쪽으로 옮기기.</summary>
    private IEnumerable<object> TabMenu(DocumentTab tab)
    {
        int g = _tabs.GroupOf(tab);
        var close = MenuAction(KeyHint(MainStrings.Tabs_Close, "파일/닫기"), () => CloseTab(tab), VectorIcon.X);
        var others = MenuAction(MainStrings.Tabs_CloseOthers, () => CloseOtherTabs(tab));
        others.IsEnabled = g >= 0 && _tabs.Tabs(g).Count > 1;
        var split = MenuAction(_tabs.IsSplit && g == 1 ? MainStrings.Tabs_OpenInOther : MainStrings.Tabs_SplitRight, () => SplitTab(tab, move: false), VectorIcon.Columns2);
        var moveItem = MenuAction(MainStrings.Tabs_MoveToOther, () => SplitTab(tab, move: true));
        moveItem.IsEnabled = _tabs.IsSplit || (g >= 0 && _tabs.Tabs(g).Count > 1);
        return [close, others, new Separator(), split, moveItem];
    }

    private static MenuItem MenuAction(string text, Action action, VectorIcon? icon = null)
    {
        var mi = new MenuItem { Header = text };
        if (icon is { } i) mi.Icon = Ui.Icon(i, 14);
        // 메뉴가 닫힌 뒤 실행한다(저장 확인 창을 띄워도 메뉴가 남지 않게).
        mi.Click += (_, _) => Dispatcher.UIThread.Post(action);
        return mi;
    }

    // ───────────── 탭을 캔버스 쪽으로 끌어 놓기 ─────────────

    /// <summary>끄는 탭이 놓일 자리: 나누지 않았으면 캔버스 오른쪽 끝(탭 줄 아래) = 오른쪽으로 분할, 나눴으면 다른 묶음 칸 = 그리로 옮기기.</summary>
    private TabDrop TabDropAt(int group, Avalonia.Point p, out Rect area)
    {
        area = default;
        var size = _paneArea.Bounds.Size;
        if (p.X < 0 || p.Y < 0 || p.X > size.Width || p.Y > size.Height) return TabDrop.None;
        double stripBottom = _panes[0].Strip.Bounds.Height;
        if (_tabs.IsSplit)
        {
            var other = _panes[1 - group].Bounds;
            if (!other.Contains(p)) return TabDrop.None;
            area = new Rect(other.X, other.Y + stripBottom, other.Width, Math.Max(0, other.Height - stripBottom));
            return TabDrop.OtherGroup;
        }
        // 탭 줄 바로 아래(순서 바꾸기로 놓이는 띠)는 빼고, 오른쪽 40%.
        if (p.Y < stripBottom + 32 || p.X < size.Width * 0.6) return TabDrop.None;
        area = new Rect(size.Width / 2, stripBottom, size.Width / 2, Math.Max(0, size.Height - stripBottom));
        return TabDrop.SplitRight;
    }

    private void DragTabOver(int group, int index, PointerEventArgs e)
    {
        _draggedTab ??= TabAt(group, index);
        if (_draggedTab is null) return;
        var drop = TabDropAt(group, e.GetPosition(_paneArea), out var area);
        _tabDropHint.IsVisible = drop != TabDrop.None;
        if (drop == TabDrop.None) return;
        _tabDropHint.Margin = new Thickness(area.X, area.Y, 0, 0);
        _tabDropHint.Width = area.Width;
        _tabDropHint.Height = area.Height;
        _tabDropText.Text = drop == TabDrop.SplitRight ? MainStrings.Tabs_DropSplit : MainStrings.Tabs_DropMove;
    }

    private void DropTab(PointerEventArgs? e)
    {
        var tab = _draggedTab;
        _draggedTab = null;
        _tabDropHint.IsVisible = false;
        if (tab is null || e is null || _tabs.GroupOf(tab) is var g && g < 0) return;
        if (TabDropAt(g, e.GetPosition(_paneArea), out _) == TabDrop.None) return;
        // 끌기(순서 바꾸기 취소 애니메이션 포함)가 끝난 뒤에 옮긴다.
        Dispatcher.UIThread.Post(() => SplitTab(tab, move: true));
    }

    // ───────────── 다시 열기(세션) ─────────────

    /// <summary>지난번에 열어 둔 탭(묶음·순서·고른 탭·포커스)을 다시 연다. 없어진 파일은 건너뛴다. 저장한 적 없는 새 문서는 남기지 않는다.</summary>
    private void RestoreSession()
    {
        if (_settings.OpenTabs?.Normalized() is not { Groups.Count: > 0 } session) return;
        int opened = 0, missing = 0, group = 0, focus = 0;
        for (int g = 0; g < session.Groups.Count; g++)
        {
            var paths = session.Groups[g];
            DocumentTab? active = null;
            bool any = false;
            for (int i = 0; i < paths.Count; i++)
            {
                string full;
                try { full = Path.GetFullPath(paths[i]); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { missing++; continue; }
                if (!File.Exists(full)) { missing++; continue; }
                var doc = FindDoc(full) ?? LoadDocument(full, interactive: false, out _, out _, out _);
                if (doc is null) { missing++; continue; }
                if (group < _tabs.GroupCount && _tabs.Tabs(group).Any(t => ReferenceEquals(t.Doc, doc))) continue;
                if (!_tabs.All.Any(t => ReferenceEquals(t.Doc, doc))) doc.Project.DirtyChanged += OnDocumentDirtyChanged;
                var tab = new DocumentTab(doc);
                _tabs.Add(tab, group, index: group < _tabs.GroupCount ? _tabs.Tabs(group).Count : 0);
                opened++;
                any = true;
                if (i <= session.Active[g]) active = tab;
            }
            if (!any) continue;
            if (active is not null) _tabs.Activate(active);
            if (g == session.Focused) focus = group;
            group++;
        }
        if (opened == 0) return;
        _tabs.Focus(focus);
        // 지금 문서가 없던 처음 상태이므로 다른 묶음의 고른 탭도 먼저 실어 보기 상태(화면 맞춤)를 잡아 둘 필요는 없다(그리기만 하는 칸은 맞춰 그림).
        ShowActiveTab();
        ShowMessage(MainStrings.FormatTabs_Restored(count: opened) + (missing > 0 ? MainStrings.FormatTabs_RestoredMissing(count: missing) : ""));
    }
}
