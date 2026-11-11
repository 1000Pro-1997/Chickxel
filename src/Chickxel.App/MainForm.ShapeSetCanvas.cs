using Chickxel.App.Localization;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

// 모양 펜 세트 ↔ 캔버스.
// "캔버스에서 새 세트": 지금 문서의 프레임마다 보이는 레이어를 합친 그림(내보내기와 같은 합성)을 잘라 모양 하나로 — 빈 프레임은 건너뛰고, 너무 크거나 많으면 알리고 만들지 않는다.
// "캔버스에서 고치기": 세트를 프레임마다 모양 하나씩 가운데 둔 저장하지 않는 문서(탭)로 열고, 탭 아래 막대의 "세트에 저장"(또는 저장 단축키)으로 같은 규칙으로 되돌려 쓴다.
// 세트 이름표·이름·순서는 그대로다. 저장하지 않고 고친 채 닫으면(탭 닫기·모든 탭 닫기·끝내기) 세트에 저장할지 묻는다.
// 작성자가 내 계정인 받은 세트(개발자가 올린 세트)는 그 에셋에 그대로 저장한다(상점 연결은 그대로 — 고친 상태가 되어 새 버전으로 올림). 다른 받은 세트는 내 세트로 복사해 고친다.
public sealed partial class MainForm
{
    /// <summary>
    /// 캔버스에서 고치는 중인 세트 하나: 세트 이름표(내 세트 이름표, 또는 <paramref name="asset"/>이면 받은 세트의 에셋 ID), 마지막으로 본 이름·순서
    /// (고치는 동안 세트가 지워지면 다시 만들 때 씀), 문서, 막대 글.
    /// </summary>
    private sealed class ShapeSetEdit(string setId, string name, StampOrder order, bool asset = false)
    {
        public string SetId { get; } = setId;
        public string Name { get; set; } = name;
        public StampOrder Order { get; set; } = order;
        /// <summary>작성자가 내 계정인 받은 세트를 그 에셋에 저장한다.</summary>
        public bool Asset { get; } = asset;
        public OpenDocument Doc { get; set; } = null!;
        public TextBlock BarText { get; set; } = null!;
    }

    /// <summary>열린 세트 고치기 문서들(세트마다 하나).</summary>
    private readonly List<ShapeSetEdit> _shapeSetEdits = [];
    /// <summary>세트 모음이 바뀔 때 막대의 세트 이름을 맞추는 처리기를 달았는지(처음 고치기를 열 때 한 번).</summary>
    private bool _shapeSetEditsHooked;

    /// <summary><paramref name="project"/>가 세트를 고치는 문서면 그 고치기(아니면 null).</summary>
    private ShapeSetEdit? ShapeSetEditOf(SpriteProject project) => _shapeSetEdits.Find(e => ReferenceEquals(e.Doc.Project, project));

    /// <summary>
    /// 지금 문서로 새 세트를 만든다: 프레임마다 합성한 그림을 모양 하나로(<see cref="StampFrames.ToShapes"/>). 문제가 있으면 알리고 만들지 않으며,
    /// 이름을 물어(처음 값 = 문서 이름) 내 세트 끝에 더하고 고른다. 묻는 창·알림은 <paramref name="owner"/> 위에.
    /// </summary>
    internal void NewShapeSetFromCanvas(Window owner)
    {
        if (_ctx.Project is not { } p)
        {
            Ui.Info(owner, UiStrings.ShapeCanvas_NoDocument, UiStrings.ShapeCanvas_NewTitle);
            return;
        }
        _ctx.CommitFloating();
        var result = FrameShapes(p);
        if (!result.IsValid)
        {
            Ui.Error(owner, ShapeProblem(result), UiStrings.ShapeCanvas_ProblemTitle);
            return;
        }
        if (result.Shapes.Count == 0)
        {
            Ui.Info(owner, UiStrings.ShapeCanvas_Empty, UiStrings.ShapeCanvas_NewTitle);
            return;
        }
        var suggested = _ctx.ShapeSets.UniqueName(p.Path is null ? p.DisplayName : p.BaseName);
        if (InputDialog.Ask(owner, UiStrings.ShapeCanvas_NewTitle, UiStrings.ShapePen_SetNamePrompt, suggested) is not { } name) return;
        var set = new StampSet { Name = name };
        set.Shapes.AddRange(result.Shapes);
        _ctx.ShapeSets.Sets.Add(set);
        _ctx.ShapePenSetId = set.Id;
        _ctx.NotifyShapeSetsEdited();
        ShowMessage(UiStrings.FormatShapeCanvas_Created(set: set.DisplayName, count: set.Shapes.Count) + SkippedSuffix(result));
    }

    /// <summary>
    /// 세트를 캔버스에서 고친다: 모양마다 프레임 하나(<see cref="StampFrames.ToProject"/>)인 저장하지 않는 문서를 새 탭으로 열고 탭 아래에 "세트에 저장" 막대를 단다.
    /// 그 세트를 이미 고치는 중이면 그 탭을 고른다. 받은 세트는 작성자가 내 계정이면 그 에셋을 그대로 고치고, 아니면 먼저 내 세트로 복사해 사본을 고친다.
    /// </summary>
    internal void EditShapeSetOnCanvas(string setId)
    {
        var set = _ctx.ShapeSets.Find(setId);
        bool asset = false;
        if (set is null && _ctx.InstalledShapeSets.FirstOrDefault(s => s.Id == setId) is { } installed)
        {
            asset = AppAssets.Catalog.Find(setId) is { } entry && AppAssets.IsMine(entry);
            set = asset ? installed : ShapeSetActions.CopyToMine(_ctx, installed);
        }
        if (set is null)
        {
            ShowMessage(UiStrings.ShapeCanvas_NotFound);
            return;
        }
        if (_shapeSetEdits.Find(e => e.SetId == set.Id) is { } open)
        {
            ShowDocument(open.Doc);
            Activate();
            return;
        }
        var edit = new ShapeSetEdit(set.Id, set.DisplayName, set.Order, asset);
        var project = StampFrames.ToProject(set.Shapes);
        edit.Doc = new OpenDocument(project, null)
        {
            DetachedTitle = UiStrings.FormatShapeCanvas_TabTitle(name: set.DisplayName),
            Closed = () => _shapeSetEdits.Remove(edit),
            ConfirmClose = () => ConfirmCloseShapeSetEdit(edit),
            Bar = ShapeSetEditBar(edit),
        };
        _shapeSetEdits.Add(edit);
        if (!_shapeSetEditsHooked)
        {
            _shapeSetEditsHooked = true;
            _ctx.ShapeSetsChanged += (_, _) => SyncShapeSetEdits();
        }
        OpenInNewTab(edit.Doc);
        Activate();
        ShowMessage(UiStrings.ShapeCanvas_EditHint);
    }

    /// <summary>탭 아래 막대: 고치는 세트 안내 + "세트에 저장"(강조)·닫기.</summary>
    private Control ShapeSetEditBar(ShapeSetEdit edit)
    {
        edit.BarText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 0),
        };
        ShowShapeSetEditName(edit);
        var save = Ui.Button(VectorIcon.Save, UiStrings.ShapeCanvas_SaveToSet, (_, _) => SaveShapeSetEdit(edit))
            .Tip(KeyHint(UiStrings.ShapeCanvas_SaveToSetTip, "파일/저장(+ 시트 내보내기)"));
        save.Background = AppTheme.AccentBrush;
        save.Foreground = Brushes.White;
        save.FontWeight = FontWeight.Bold;
        var close = Ui.Button(VectorIcon.X, UiStrings.S_Ui_CloseOnly_11312F9A81, (_, _) => CloseShapeSetEdit(edit)).Tip(UiStrings.ShapeCanvas_CloseTip);
        save.Focusable = close.Focusable = false;
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(save, Dock.Right);
        var icon = Ui.Icon(VectorIcon.Pencil, 14);
        icon.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(close);
        row.Children.Add(save);
        row.Children.Add(icon);
        row.Children.Add(edit.BarText);
        var accent = AppTheme.AccentColor;
        return new Border
        {
            Background = AppTheme.Brush(Color.FromArgb(56, accent.R, accent.G, accent.B)),
            BorderBrush = AppTheme.AccentBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(2),
            Child = row,
        };
    }

    /// <summary>막대 글(과 풍선)에 세트 이름을 적는다.</summary>
    private static void ShowShapeSetEditName(ShapeSetEdit edit)
    {
        edit.BarText.Text = UiStrings.FormatShapeCanvas_Bar(name: edit.Name);
        ToolTip.SetTip(edit.BarText, edit.BarText.Text);
    }

    /// <summary>세트 모음이 바뀜: 고치는 세트의 이름·순서를 따라 적는다(지워진 세트는 마지막 값 그대로 — 저장하면 다시 만듦).</summary>
    private void SyncShapeSetEdits()
    {
        foreach (var edit in _shapeSetEdits)
        {
            if (_ctx.ShapeSets.Find(edit.SetId) is not { } set || set.DisplayName == edit.Name && set.Order == edit.Order) continue;
            (edit.Name, edit.Order) = (set.DisplayName, set.Order);
            ShowShapeSetEditName(edit);
        }
    }

    /// <summary>막대의 닫기: 그 문서의 탭을 닫는다(고친 채면 <see cref="ConfirmCloseShapeSetEdit"/>가 묻는다).</summary>
    private void CloseShapeSetEdit(ShapeSetEdit edit)
    {
        if (TabOf(edit.Doc) is { } tab) Dispatcher.UIThread.Post(() => CloseTab(tab));
    }

    /// <summary>
    /// 고친 문서를 세트에 쓴다: 프레임마다 합성한 그림을 모양으로(새 세트와 같은 규칙 — 빈 프레임 건너뜀, 너무 크거나 많으면 알리고 쓰지 않음).
    /// 세트의 모양만 바꾸고 이름표·이름·순서는 그대로다. 고치는 동안 세트가 지워졌으면 같은 이름표로 다시 만들어 끝에 더한다. 썼으면 true(문서는 수정됨이 꺼짐).
    /// 작성자가 내 계정인 받은 세트는 그 에셋에 저장한다(저장소가 바뀌어 받은 세트 목록이 다시 만들어짐 — 지워졌으면 내 세트로 다시 만듦).
    /// </summary>
    private bool SaveShapeSetEdit(ShapeSetEdit edit)
    {
        var p = edit.Doc.Project;
        if (ReferenceEquals(p, _ctx.Project)) _ctx.CommitFloating();
        var result = FrameShapes(p);
        if (!result.IsValid)
        {
            Ui.Error(this, ShapeProblem(result), UiStrings.ShapeCanvas_ProblemTitle);
            return false;
        }
        string prefix = "";
        if (edit.Asset && AppAssets.Catalog.Find(edit.SetId) is { ShapeSet: { } stored } entry)
        {
            var updated = Chickxel.Core.IO.ShapeSetAssetFormat.Copy(stored, entry.Id);
            updated.Shapes.Clear();
            updated.Shapes.AddRange(result.Shapes);
            if (!AppAssets.Try(this, () => AppAssets.Store.SaveShapeSet(entry.Id, updated), UiStrings.Assets_ActionError)) return false;
            p.IsDirty = false;
            ShowMessage(UiStrings.FormatShapeCanvas_Saved(set: entry.DisplayName, count: updated.Shapes.Count) + SkippedSuffix(result));
            return true;
        }
        if (_ctx.ShapeSets.Find(edit.SetId) is not { } set)
        {
            set = new StampSet { Id = edit.SetId, Name = _ctx.ShapeSets.UniqueName(edit.Name), Order = edit.Order };
            _ctx.ShapeSets.Sets.Add(set);
            prefix = UiStrings.ShapeCanvas_Recreated;
        }
        set.Shapes.Clear();
        set.Shapes.AddRange(result.Shapes);
        _ctx.NotifyShapeSetsEdited();
        p.IsDirty = false;
        ShowMessage(prefix + UiStrings.FormatShapeCanvas_Saved(set: set.DisplayName, count: set.Shapes.Count) + SkippedSuffix(result));
        return true;
    }

    /// <summary>세트 고치기 문서를 닫기 전: 저장 뒤 고쳤으면 세트에 저장할지 묻는다(저장·저장 안 함·취소). 닫아도 되면 true.</summary>
    private bool ConfirmCloseShapeSetEdit(ShapeSetEdit edit)
    {
        if (!edit.Doc.Project.IsDirty) return true;
        if (TabOf(edit.Doc) is { } tab) ActivateTab(tab);
        var name = _ctx.ShapeSets.Find(edit.SetId)?.DisplayName ?? edit.Name;
        return Ui.Ask(this, UiStrings.FormatShapeCanvas_ApplyConfirm(name: name), UiStrings.ShapeCanvas_SaveToSet, MessageButtons.YesNoCancel,
                MessageIcon.Question, okText: UiStrings.ShapeCanvas_SaveToSet, noText: UiStrings.ShapeCanvas_DontSave) switch
            {
                DialogResult.Yes => SaveShapeSetEdit(edit),
                DialogResult.No => true,
                _ => false,
            };
    }

    /// <summary>문서의 프레임마다 내보내기와 같이 합성한 그림(보이는 레이어, 효과 포함)을 모양으로 바꾼다.</summary>
    private static StampFrames.Result FrameShapes(SpriteProject p)
    {
        var cache = ProjectExport.NewCache();
        return StampFrames.ToShapes(Enumerable.Range(0, p.Frames.Count).Select(f => ProjectExport.CompositeFrame(p, f, cache)));
    }

    /// <summary>세트로 쓸 수 없는 까닭(너무 큰 프레임 — 앞 몇 개만 적음, 모양이 너무 많음).</summary>
    private static string ShapeProblem(StampFrames.Result result)
    {
        const int listed = 5;
        var lines = new List<string>();
        foreach (var big in result.TooBig.Take(listed))
            lines.Add(UiStrings.FormatShapeCanvas_TooBigFrame(frame: big.Frame + 1, width: big.Width, height: big.Height));
        if (result.TooBig.Count > listed) lines.Add(UiStrings.FormatShapeCanvas_TooBigMore(count: result.TooBig.Count - listed));
        if (result.TooBig.Count > 0) lines.Add(UiStrings.FormatShapeCanvas_TooBigLimit(max: StampLibrary.MaxShapeSize));
        if (result.TooMany)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add(UiStrings.FormatShapeCanvas_TooMany(count: result.Shapes.Count + result.TooBig.Count, max: StampFrames.MaxShapes));
        }
        return string.Join("\n", lines);
    }

    private static string SkippedSuffix(StampFrames.Result result)
        => result.EmptyFrames > 0 ? UiStrings.FormatShapeCanvas_Skipped(count: result.EmptyFrames) : "";
}
