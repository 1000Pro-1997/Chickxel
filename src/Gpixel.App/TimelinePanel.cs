using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// Aseprite식 타임라인: 행 = 레이어(위가 맨 위 레이어), 열 = 프레임, 위쪽 띠 = 태그(애니메이션).
/// 셀 썸네일은 실시간으로 갱신된다. 도구 단추 없이 우클릭 메뉴(프레임 머리글·셀·레이어 이름·빈 곳)와 마지막 프레임 뒤
/// "다음 프레임" 칸(⧉ 복사해서 추가 · ＋ 빈 프레임 추가)으로 편집한다. 셀·프레임 복사는 고른 뒤 Ctrl/⌘+C·V.
/// 프레임 칸을 끌면 범위가 골라지고, 우클릭 "태그 만들기"로 태그(애니메이션)로 묶는다. 재생은 미리보기에서(태그 안에서 반복).
/// <para>
/// 그룹 레이어 행은 ▾/▸로 접고 펼치며, 그 아래에 부위 행이 들여 써서 보인다(이름 + 부위 크기). 그룹 행의 셀은 그룹 합성 썸네일이다.
/// 행을 끌면 순서가 바뀐다: 일반 레이어·그룹은 맨 위 단계끼리(그룹은 부위와 함께), 부위는 같은 그룹 안에서만.
/// </para>
/// <para>
/// 그룹은 스킨 슬롯이다: 그룹 행 이름 칸 오른쪽 칩에 지금 스킨 이름이, 부위 행 칩에는 그 부위가 보이는 스킨 이름(부위만 따로 고르면 강조)이
/// 보이고, 칩을 누르면 스킨 관리 창(<see cref="SkinManagerWindow"/>)이 그 줄로 열린다. 우클릭 메뉴 "스킨"에서 추가·복제·이름·삭제·순서를,
/// 부위는 "이 부위 스킨"에서 부위만 따로 입힐 스킨을 고른다. 지금 스킨에 자기 그림이 없어 기본 스킨 셀을 빌려 보이는 부위 셀은 흐리게 그린다.
/// </para>
/// </summary>
public sealed class TimelinePanel : UserControl
{
    /// <summary>움직임 테스트와 연결되는 태그 이름 제안.</summary>
    public static readonly string[] SuggestedNames =
    [
        "idle_down", "idle_up", "idle_left", "idle_right",
        "walk_down", "walk_up", "walk_left", "walk_right",
        "idle", "run", "jump", "fall",
    ];

    private readonly EditorContext _ctx;
    private readonly PreviewPanel _preview;
    private readonly TimelineGrid _grid;
    private readonly TextBlock _info;

    public TimelinePanel(EditorContext ctx, PreviewPanel preview)
    {
        _ctx = ctx;
        _preview = preview;
        Background = AppTheme.PanelBrush;

        _info = Ui.Label("");
        _info.Foreground = AppTheme.DimTextBrush;

        _grid = new TimelineGrid(this);
        var root = new DockPanel { LastChildFill = true };
        // 제목 줄 오른쪽에 현재 프레임·레이어 정보(도구 줄을 한 줄 아껴 레이어 행이 더 보이게).
        _info.Margin = new Thickness(8, 0, 6, 0);
        _info.TextTrimming = TextTrimming.CharacterEllipsis;
        _info.HorizontalAlignment = HorizontalAlignment.Right;
        var headerRow = new DockPanel();
        var title = new TextBlock { Text = "타임라인", FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(title, Dock.Left);
        headerRow.Children.Add(title);
        // 스킨 관리 창은 이름 칸의 스킨 칩(클릭)·레이어 우클릭·프레임·레이어 메뉴에서 연다.
        headerRow.Children.Add(_info);
        var header = new Border { Height = 24, Background = AppTheme.BackBrush, Padding = new Thickness(6, 0, 0, 0), Child = headerRow };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_grid);
        Content = root;

        // 다른 파일을 열면 바로 새 배치로, 같은 프로젝트 안의 변경(실행취소 포함)은 행·열이 자라고 줄어드는 애니메이션으로.
        ctx.ProjectReplaced += (_, _) => { _grid.EndRename(false); _grid.ResetRange(); _grid.SyncLayout(animate: false); RefreshUi(); };
        ctx.ProjectChanged += (_, _) => { _grid.SyncLayout(animate: true); RefreshUi(); };
        ctx.CurrentChanged += (_, _) =>
        {
            ExpandToCurrent();
            _grid.ClampRange();
            RefreshUi();
            _grid.EnsureCurrentVisible();
        };
        ctx.ViewChanged += (_, _) => _grid.InvalidateVisual();
        preview.PlayingFrameChanged += (_, f) => _grid.PlayingFrame = f;
        _grid.SyncLayout(animate: false);
        RefreshUi();
    }

    private void RefreshUi()
    {
        IsEnabled = _ctx.HasProject;
        if (_ctx.Project is { } p)
        {
            var layer = p.Layers[_ctx.CurrentLayer];
            var tag = _ctx.ActiveTag;
            int total = p.Frames.Sum(f => f.DurationMs);
            string skin = SkinManagerWindow.SkinStatus(p, layer, _ctx.CurrentFrame);
            string kind = layer.Kind switch
            {
                LayerKind.Group => "그룹",
                LayerKind.Part => $"부위 {layer.PartSize.Width}×{layer.PartSize.Height} @{layer.PartOffset.X},{layer.PartOffset.Y}",
                _ => "",
            };
            _info.Text = $"프레임 {_ctx.CurrentFrame + 1}/{p.Frames.Count} · 레이어 \"{layer.Name}\"" + (kind.Length > 0 ? $" ({kind})" : "")
                         + (skin.Length > 0 ? " · " + skin : "")
                         + (tag is null ? "" : $" · 태그 {tag.Name}") + $" · 전체 {total}ms";
        }
        else _info.Text = "";
        _grid.UpdateScrollSize();
        _grid.InvalidateVisual();
    }

    // ───────────── 동작 ─────────────

    private SpriteProject? P => _ctx.Project;

    /// <summary>범위 선택이 있으면 그 프레임들, 없으면 현재 프레임.</summary>
    private List<int> SelectedFrames()
    {
        var (a, b) = _grid.Range;
        return a < 0 ? [_ctx.CurrentFrame] : Enumerable.Range(a, b - a + 1).ToList();
    }

    /// <summary>이전/다음 프레임으로, 끝에서는 반대쪽으로 돈다.</summary>
    public void StepFrame(int delta)
    {
        if (P is null) return;
        _preview.Stop();
        int n = _ctx.FrameCount;
        if (n == 0) return;
        _ctx.SetCurrentFrame(((_ctx.CurrentFrame + delta) % n + n) % n);
    }

    public void NewEmptyFrame()
    {
        if (P is not { } p) return;
        int at = _ctx.CurrentFrame + 1;
        _ctx.Structural(() => p.AddFrame(at));
        _ctx.SetCurrentFrame(at);
    }

    public void DuplicateFrame()
    {
        if (P is not { } p) return;
        int at = _ctx.CurrentFrame;
        _ctx.Structural(() => p.DuplicateFrame(at));
        _ctx.SetCurrentFrame(at + 1);
    }

    public void DeleteFrame()
    {
        if (P is not { } p) return;
        if (p.Frames.Count <= 1)
        {
            Ui.Info(this, "마지막 프레임은 지울 수 없습니다.");
            return;
        }
        var frames = SelectedFrames();
        if (frames.Count >= p.Frames.Count) frames.RemoveAt(0);
        _ctx.Structural(() => p.Edit("프레임 삭제", ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () =>
        {
            foreach (var f in frames.OrderByDescending(f => f)) p.DeleteFrame(f);
        }));
        _grid.ResetRange();
        _ctx.SetCurrentFrame(Math.Min(frames.Min(), p.Frames.Count - 1));
    }

    public void MoveFrame(int delta) => MoveFrameTo(_ctx.CurrentFrame, _ctx.CurrentFrame + delta);

    /// <summary>프레임 순서 바꾸기.</summary>
    private void MoveFrameTo(int from, int to)
    {
        if (P is not { } p) return;
        to = Math.Clamp(to, 0, p.Frames.Count - 1);
        if (from == to) return;
        _ctx.Structural(() => p.MoveFrame(from, to));
        _ctx.SetCurrentFrame(to);
    }

    /// <summary>마지막 프레임 뒤에 프레임을 하나 붙인다(<paramref name="copy"/>면 마지막 프레임을 그림까지 복사). 그 프레임의 <paramref name="layer"/>를 고른다.</summary>
    private void AppendFrame(bool copy, int layer)
    {
        if (P is not { } p) return;
        int last = p.Frames.Count - 1;
        if (copy) _ctx.Structural(() => p.DuplicateFrame(last));
        else _ctx.Structural(() => p.AddFrame(last + 1));
        _grid.ResetRange();
        _ctx.SetCurrent(Math.Clamp(layer, 0, p.Layers.Count - 1), last + 1);
    }

    /// <summary>앞쪽에서 가장 가까운, 그림이 있는 같은 레이어의 프레임(없으면 -1).</summary>
    private static int PreviousCelFrame(SpriteProject p, int layer, int frame)
    {
        if (p.Layers[layer].IsGroup) return -1;
        for (int f = frame - 1; f >= 0; f--)
            if (!p.IsCelEmpty(layer, f)) return f;
        return -1;
    }

    /// <summary>빈 셀에 앞 프레임의 같은 레이어 그림을 복사한다(실행취소 1단계).</summary>
    private void CopyCelFromPrevious(int layer, int frame)
    {
        if (P is not { } p) return;
        int from = PreviousCelFrame(p, layer, frame);
        if (from < 0 || p.GetCel(layer, from) is not { } cel) return;
        _ctx.Structural(() => p.SetCel(layer, frame, cel.Clone(), "이전 프레임 셀 복사"));
        _ctx.SetCurrent(layer, frame);
    }

    // ───────────── 셀 묶음(행 × 프레임) 옮기기·복사·붙여넣기 ─────────────

    /// <summary>복사한 셀 한 행: 일반·부위 레이어는 프레임별 셀, 그룹은 부위마다(위→아래) 프레임별 셀.</summary>
    private sealed record ClipRow(PixelImage?[] Cels, List<PixelImage?[]>? Children);

    /// <summary>
    /// 복사한 셀/프레임. <see cref="WholeFrames"/>면 프레임 전체(모든 레이어 + 길이, <see cref="Frames"/>),
    /// 아니면 고른 행 × 프레임 묶음(<see cref="Rows"/>, 붙일 때 클릭한 셀이 왼쪽 위).
    /// </summary>
    private sealed record CelClip(bool WholeFrames, int FrameCount, List<Dictionary<Layer, PixelImage>> Frames, List<int> Durations, List<ClipRow> Rows);

    private static CelClip? _clip;

    private static List<Layer> ChildrenTopDown(SpriteProject p, Layer group) => p.ChildrenOf(group).Reverse().ToList();

    /// <summary>행(레이어 번호, 위→아래) × 프레임의 셀을 복제해 담는다.</summary>
    private static List<ClipRow> CaptureCells(SpriteProject p, IReadOnlyList<int> rowLayers, IReadOnlyList<int> frames)
        => rowLayers.Select(l => p.Layers[l] is { IsGroup: true } g
            ? new ClipRow([], ChildrenTopDown(p, g).Select(c => frames.Select(f => p.GetCel(c, p.Frames[f])?.Clone()).ToArray()).ToList())
            : new ClipRow(frames.Select(f => p.GetCel(l, f)?.Clone()).ToArray(), null)).ToList();

    /// <summary>행 × 프레임의 셀을 비운다(그룹 행은 그 부위들). <see cref="SpriteProject.Edit"/> 안에서 부른다.</summary>
    private static void ClearCells(SpriteProject p, IReadOnlyList<int> rowLayers, IReadOnlyList<int> frames)
    {
        foreach (var l in rowLayers.SelectMany(l => p.Layers[l] is { IsGroup: true } g ? ChildrenTopDown(p, g).Select(p.IndexOf) : [l]).Distinct())
            foreach (var f in frames)
                if (!p.IsCelEmpty(l, f)) p.SetCel(l, f, null);
    }

    /// <summary>
    /// 담아 둔 셀 묶음을 보이는 행 <paramref name="topRow"/>·프레임 <paramref name="start"/>부터 쓴다(빈 셀은 비움, 모자라는 프레임은 붙임).
    /// 셀 크기가 다르거나 그룹/일반이 맞지 않는 칸은 건너뛴다. 쓴 칸 수를 돌려준다. <see cref="SpriteProject.Edit"/> 안에서 부른다.
    /// </summary>
    private static int WriteCells(SpriteProject p, IReadOnlyList<ClipRow> rows, int frameCount, IReadOnlyList<int> visibleRows, int topRow, int start)
    {
        while (p.Frames.Count < start + frameCount) p.AddFrame(p.Frames.Count);
        int written = 0;
        void Put(Layer target, PixelImage?[] cels)
        {
            int index = p.IndexOf(target);
            for (int k = 0; k < cels.Length; k++)
            {
                var image = cels[k];
                if (image is not null && image.Size != p.CelSize(target)) continue;
                if (image is null && p.IsCelEmpty(index, start + k)) { written++; continue; }
                p.SetCel(index, start + k, image?.Clone());
                written++;
            }
        }
        for (int i = 0; i < rows.Count; i++)
        {
            int ti = topRow + i;
            if (ti < 0 || ti >= visibleRows.Count) continue;
            var target = p.Layers[visibleRows[ti]];
            var row = rows[i];
            if (row.Children is null)
            {
                if (!target.IsGroup) Put(target, row.Cels);
            }
            else if (target.IsGroup)
            {
                var children = ChildrenTopDown(p, target);
                for (int c = 0; c < Math.Min(children.Count, row.Children.Count); c++) Put(children[c], row.Children[c]);
            }
        }
        return written;
    }

    /// <summary>
    /// 셀 묶음(행 × 연속 프레임)을 보이는 행 <paramref name="toRow"/>·프레임 <paramref name="toFrame"/>로 옮기거나 복사한다(실행취소 1단계).
    /// 놓은 자리가 새 선택이 된다.
    /// </summary>
    private void MoveBlock(IReadOnlyList<int> rowLayers, int fromFrame, int frameCount, int toRow, int toFrame, bool copy)
    {
        if (P is not { } p) return;
        var visible = _grid.VisibleRows();
        var frames = Enumerable.Range(fromFrame, frameCount).ToList();
        // 행마다 놓을 자리와 맞아야 한다(일반·부위 ↔ 같은 크기, 그룹 ↔ 그룹). 하나라도 안 맞으면 옮기지 않는다(그림이 사라지지 않게).
        for (int i = 0; i < rowLayers.Count; i++)
        {
            int ti = toRow + i;
            var src = p.Layers[rowLayers[i]];
            if (ti >= visible.Count || !RowsCompatible(p, src, p.Layers[visible[ti]]))
            {
                Ui.Info(this, "셀 크기나 종류가 다른 행으로는 옮길 수 없습니다(부위는 같은 크기, 그룹은 그룹 행으로).");
                return;
            }
        }
        var data = CaptureCells(p, rowLayers, frames);
        // 놓을 자리의 레이어(편집 뒤에도 같은 객체)
        var topLayer = p.Layers[visible[toRow]];
        var endLayer = p.Layers[visible[Math.Min(visible.Count - 1, toRow + rowLayers.Count - 1)]];
        _ctx.Structural(() => p.Edit(copy ? "셀 복사" : "셀 옮기기", ProjectChange.Pixels | ProjectChange.Frames, () =>
        {
            if (!copy) ClearCells(p, rowLayers, frames);
            WriteCells(p, data, frameCount, visible, toRow, toFrame);
        }));
        _grid.SelectRect(p.IndexOf(topLayer), toFrame, endLayer, toFrame + frameCount - 1);
    }

    /// <summary>셀 행을 다른 행에 놓을 수 있는지: 일반·부위는 셀 크기가 같은 일반·부위, 그룹은 부위 수·크기가 같은 그룹.</summary>
    private static bool RowsCompatible(SpriteProject p, Layer source, Layer target)
    {
        if (!source.IsGroup) return !target.IsGroup && p.CelSize(source) == p.CelSize(target);
        if (!target.IsGroup) return false;
        if (ReferenceEquals(source, target)) return true;
        var a = ChildrenTopDown(p, source);
        var b = ChildrenTopDown(p, target);
        return a.Count == b.Count && a.Zip(b).All(x => x.First.PartSize == x.Second.PartSize);
    }

    /// <summary>고른 셀(행 × 프레임, 그룹 행은 그 부위들)의 그림을 지운다.</summary>
    private void ClearSelectedCels()
    {
        if (P is not { } p) return;
        var rows = _grid.SelectedRowLayers();
        var frames = SelectedFrames();
        _ctx.Structural(() => p.Edit("셀 지우기", ProjectChange.Pixels, () => ClearCells(p, rows, frames)));
    }

    // ───────────── 셀·프레임 복사·붙여넣기(타임라인에 포커스가 있을 때 Ctrl/⌘+C·X·V) ─────────────

    /// <summary>타임라인 격자에 키보드 포커스가 있는지(복사·붙여넣기를 셀/프레임 대상으로).</summary>
    public bool IsGridFocused => _grid.IsKeyboardFocusWithin;

    /// <summary>고른 셀 묶음(행 × 프레임) 또는 프레임(머리글 범위)을 복사한다. <paramref name="cut"/>이면 지운다.</summary>
    public void CopySelection(bool cut)
    {
        if (P is not { } p) return;
        var frames = SelectedFrames();
        var durations = frames.Select(f => p.Frames[f].DurationMs).ToList();
        if (_grid.HeaderRange)
        {
            var layers = Enumerable.Range(0, p.Layers.Count).Where(i => !p.Layers[i].IsGroup).ToList();
            var data = frames.Select(f => layers.Where(l => !p.IsCelEmpty(l, f)).ToDictionary(l => p.Layers[l], l => p.GetCel(l, f)!.Clone())).ToList();
            _clip = new CelClip(true, frames.Count, data, durations, []);
            if (cut) DeleteFrame();
            return;
        }
        _clip = new CelClip(false, frames.Count, [], durations, CaptureCells(p, _grid.SelectedRowLayers(), frames));
        if (cut) ClearSelectedCels();
    }

    /// <summary>
    /// 붙여넣기: 프레임 복사였으면 현재 프레임(범위 끝) 뒤에 그 프레임들을 끼워 넣고, 셀 복사였으면 클릭한 셀을 왼쪽 위로 해서
    /// 같은 모양(행 × 프레임)으로 덮어쓴다(셀 크기가 다른 칸은 건너뜀). 모자라는 프레임은 붙인다.
    /// </summary>
    /// <returns>타임라인에 복사해 둔 셀/프레임이 있어 붙여 넣었으면(또는 붙일 수 없다고 알렸으면) true, 없으면 false(그림 붙여넣기로 넘김).</returns>
    public bool PasteSelection()
    {
        if (P is not { } p || _clip is not { } clip || clip.FrameCount == 0) return false;
        if (clip.WholeFrames)
        {
            int at = SelectedFrames().Max() + 1;
            _ctx.Structural(() => p.Edit("프레임 붙여넣기", ProjectChange.Frames | ProjectChange.Pixels | ProjectChange.Tags, () =>
            {
                for (int i = 0; i < clip.Frames.Count; i++)
                {
                    p.AddFrame(at + i);
                    p.SetFrameDuration(at + i, clip.Durations[i]);
                    foreach (var (layer, image) in clip.Frames[i])
                    {
                        int l = p.IndexOf(layer);
                        if (l >= 0 && p.CelSize(layer) == image.Size) p.SetCel(l, at + i, image.Clone());
                    }
                }
            }));
            _grid.SetRange(at, at + clip.Frames.Count - 1, header: true);
            _ctx.SetCurrentFrame(at);
            return true;
        }
        var visible = _grid.VisibleRows();
        int top = visible.IndexOf(_ctx.CurrentLayer), start = _ctx.CurrentFrame;
        if (top < 0) return true;
        var topLayer = p.Layers[visible[top]];
        var endLayer = p.Layers[visible[Math.Min(visible.Count - 1, top + clip.Rows.Count - 1)]];
        int written = 0;
        _ctx.Structural(() => p.Edit("셀 붙여넣기", ProjectChange.Pixels | ProjectChange.Frames, () =>
            written = WriteCells(p, clip.Rows, clip.FrameCount, visible, top, start)));
        if (written == 0)
        {
            Ui.Info(this, "붙여 넣을 수 있는 칸이 없습니다. 셀 크기가 같은 레이어(그룹은 그룹 행)를 골라 주세요.");
            return true;
        }
        _grid.SelectRect(p.IndexOf(topLayer), start, endLayer, start + clip.FrameCount - 1);
        return true;
    }

    /// <summary>셀(레이어 × 프레임) 그림을 지운다.</summary>
    private void ClearCel(int layer, int frame)
    {
        if (P is not { } p || p.Layers[layer].IsGroup || p.IsCelEmpty(layer, frame) || p.IsCelInherited(layer, frame)) return;
        _ctx.Structural(() => p.SetCel(layer, frame, null, "셀 지우기"));
    }

    /// <summary>고른 프레임(범위)의 길이를 바꾼다.</summary>
    private void SetDuration()
    {
        if (P is not { } p) return;
        var frames = SelectedFrames();
        var text = InputDialog.Ask(this, "프레임 길이", frames.Count > 1 ? $"프레임 {frames.Count}개의 길이(ms)" : "프레임 길이(ms)",
            p.Frames[frames[0]].DurationMs.ToString(), ["50", "80", "100", "120", "150", "200"]);
        if (text is null) return;
        if (!int.TryParse(text, out var ms) || ms < 1 || ms > 60000)
        {
            Ui.Error(this, "1~60000 사이 숫자를 입력하세요.");
            return;
        }
        _ctx.Structural(() => p.Edit("프레임 길이", ProjectChange.Frames, () =>
        {
            foreach (var f in frames) p.SetFrameDuration(f, ms);
        }));
    }

    // ───────────── 레이어 ─────────────

    /// <summary>현재 레이어가 든 맨 위 단계 덩어리(부위면 그 그룹) 바로 위 자리.</summary>
    private int SlotAboveCurrent(SpriteProject p)
    {
        var current = p.Layers[_ctx.CurrentLayer];
        return p.IndexOf(current.Parent ?? current) + 1;
    }

    /// <summary>현재 레이어(그룹 안이면 그 그룹) 위에 새 일반 레이어.</summary>
    public void AddLayer()
    {
        if (P is not { } p) return;
        int at = SlotAboveCurrent(p);
        Layer? added = null;
        _ctx.Structural(() => added = p.AddLayer(at));
        if (added is not null) _ctx.SetCurrentLayer(p.IndexOf(added));
    }

    /// <summary>현재 레이어(그룹 안이면 그 그룹) 위에 새 그룹.</summary>
    public void AddGroup()
    {
        if (P is not { } p) return;
        int at = SlotAboveCurrent(p);
        Layer? added = null;
        _ctx.Structural(() => added = p.AddGroup(null, at));
        if (added is not null) _ctx.SetCurrentLayer(p.IndexOf(added));
    }

    /// <summary>현재 그룹(부위를 골랐으면 그 그룹)에 새 부위(이름·크기 입력). 그룹이 없으면 그룹도 함께 만든다(실행취소 한 단계).</summary>
    public void AddPart() => AddPartTo(_ctx.CurrentGroup);

    private void AddPartTo(Layer? group)
    {
        if (P is not { } p) return;
        var size = _ctx.CurrentPart?.PartSize ?? new Size(Math.Min(16, p.Width), Math.Min(16, p.Height));
        var dlg = new NewPartDialog(p.UniqueLayerName("부위"), size.Width, size.Height);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string? name = dlg.PartName.Length > 0 ? dlg.PartName : null;
        int at = SlotAboveCurrent(p);
        Layer? part = null;
        try
        {
            _ctx.Structural(() => p.Edit("부위 추가", ProjectChange.Layers | ProjectChange.Pixels, () =>
            {
                var target = group ?? p.AddGroup(null, at);
                part = p.AddPart(target, name, dlg.PartWidth, dlg.PartHeight);
            }));
        }
        catch (ArgumentException ex)
        {
            Ui.Error(this, ex.Message);
            return;
        }
        if (part is not null) _ctx.SetCurrentLayer(p.IndexOf(part));
    }

    /// <summary>현재 부위 크기 바꾸기(기준점; 그림은 프레임에서 같은 자리, 피벗은 같은 그림 지점 유지).</summary>
    public void ResizePart() => ResizePartAt(_ctx.CurrentLayer);

    private void ResizePartAt(int index)
    {
        if (P is not { } p || index < 0 || index >= p.Layers.Count) return;
        var part = p.Layers[index];
        if (!part.IsPart)
        {
            Ui.Info(this, "부위 레이어를 고르세요.");
            return;
        }
        var dlg = new ResizeDialog(part.PartSize.Width, part.PartSize.Height, $"부위 크기 — {part.Name}");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.NewWidth == part.PartSize.Width && dlg.NewHeight == part.PartSize.Height) return;
        _ctx.Structural(() => p.ResizePart(index, dlg.NewWidth, dlg.NewHeight, dlg.ImageAnchor));
    }

    /// <summary>부위 위치(프레임 안 왼쪽 위 X·Y) 입력.</summary>
    private void SetPartPosition(int index)
    {
        if (P is not { } p || index < 0 || index >= p.Layers.Count || !p.Layers[index].IsPart) return;
        var part = p.Layers[index];
        var dlg = new PartOffsetDialog(part.Name, part.PartOffset);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Offset == part.PartOffset) return;
        var offset = dlg.Offset;
        _ctx.Structural(() => p.SetPartOffset(index, offset));
    }

    /// <summary>그룹 접기/펼치기(기록·수정 표시 없음). 접는 그룹 안 부위를 고르고 있었으면 그룹을 고른다.</summary>
    private void ToggleCollapsed(int index)
    {
        if (P is not { } p || index < 0 || index >= p.Layers.Count || !p.Layers[index].IsGroup) return;
        var group = p.Layers[index];
        bool collapse = !group.Collapsed;
        if (collapse && ReferenceEquals(_ctx.CurrentLayerObject?.Parent, group)) _ctx.SetCurrentLayer(index);
        p.SetLayerCollapsed(index, collapse);
    }

    /// <summary>현재 레이어가 접힌 그룹 안의 부위면 그 그룹을 펼친다(타임라인에 보이게).</summary>
    private void ExpandToCurrent()
    {
        if (P is { } p && _ctx.CurrentPart?.Parent is { Collapsed: true } group)
            p.SetLayerCollapsed(p.IndexOf(group), false);
    }

    private void DuplicateLayer()
    {
        if (P is not { } p) return;
        int at = _ctx.CurrentLayer;
        Layer? copy = null;
        _ctx.Structural(() => copy = p.DuplicateLayer(at));
        if (copy is not null) _ctx.SetCurrentLayer(p.IndexOf(copy));
    }

    private void DeleteLayer()
    {
        if (P is not { } p) return;
        int at = _ctx.CurrentLayer;
        try
        {
            _ctx.Structural(() => p.DeleteLayer(at));
        }
        catch (InvalidOperationException ex)
        {
            Ui.Info(this, ex.Message);
        }
    }

    /// <summary>▲(+1)/▼(-1): 일반 레이어·그룹은 맨 위 단계끼리 덩어리째, 부위는 같은 그룹 안에서 한 칸.</summary>
    private void MoveLayer(int delta)
    {
        if (P is not { } p) return;
        var layer = p.Layers[_ctx.CurrentLayer];
        int at = _ctx.CurrentLayer;
        _ctx.Structural(() =>
        {
            if (delta > 0) p.MoveLayerUp(at);
            else p.MoveLayerDown(at);
        });
        _ctx.SetCurrentLayer(p.IndexOf(layer));
    }

    /// <summary>
    /// 끌어 놓기: <paramref name="from"/> 레이어를 <paramref name="target"/> 자리로 옮긴다(실행취소 한 단계).
    /// 부위는 같은 그룹의 부위끼리, 일반 레이어·그룹은 맨 위 단계끼리 순서를 바꾼다.
    /// </summary>
    private void MoveLayerTo(int from, int target)
    {
        if (P is not { } p) return;
        var layer = p.Layers[from];
        var dest = p.Layers[target];
        var order = layer.Parent is { } group ? p.ChildrenOf(group).ToList() : p.Layers.Where(l => l.Parent is null).ToList();
        int steps = order.IndexOf(dest) - order.IndexOf(layer);
        if (order.IndexOf(dest) < 0 || steps == 0) return;
        _ctx.Structural(() => p.Edit("레이어 이동", ProjectChange.Layers, () =>
        {
            for (int i = 0; i < Math.Abs(steps); i++)
            {
                if (steps > 0) p.MoveLayerUp(p.IndexOf(layer));
                else p.MoveLayerDown(p.IndexOf(layer));
            }
        }));
        _ctx.SetCurrentLayer(p.IndexOf(layer));
    }

    /// <summary>일반 레이어를 바로 아래 일반 레이어에만 병합할 수 있다.</summary>
    private static bool CanMergeDown(SpriteProject p, int index)
        => index > 0 && index < p.Layers.Count && p.Layers[index].Kind == LayerKind.Normal && p.Layers[index - 1].Kind == LayerKind.Normal;

    private void MergeDown()
    {
        if (P is not { } p) return;
        int at = _ctx.CurrentLayer;
        if (!CanMergeDown(p, at))
        {
            Ui.Info(this, at == 0 ? "맨 아래 레이어는 아래로 병합할 수 없습니다." : "부위·그룹 레이어는 아래로 병합할 수 없습니다.");
            return;
        }
        try
        {
            _ctx.Structural(() => p.MergeLayerDown(at));
            _ctx.SetCurrentLayer(at - 1);
        }
        catch (InvalidOperationException ex)
        {
            Ui.Info(this, ex.Message);
        }
    }

    /// <summary>현재 레이어 이름을 타임라인 이름 칸에서 바로 고친다(F2).</summary>
    public void BeginRenameLayer()
    {
        if (P is null) return;
        ExpandToCurrent();
        _grid.EnsureCurrentVisible();
        _grid.BeginRename(_ctx.CurrentLayer);
    }

    private void RenameLayer(Layer layer, string name)
    {
        if (P is not { } p) return;
        int index = p.IndexOf(layer);
        name = name.Trim();
        if (index < 0 || name.Length == 0 || name == layer.Name) return;
        _ctx.Structural(() => p.SetLayerProperties(index, name: p.UniqueLayerName(name)));
    }

    private void SetLayerOpacity(int index)
    {
        if (P is not { } p) return;
        var text = InputDialog.Ask(this, "레이어 불투명도", "불투명도(0~255)", p.Layers[index].Opacity.ToString(), ["255", "192", "128", "64"]);
        if (text is null) return;
        if (!byte.TryParse(text, out var v))
        {
            Ui.Error(this, "0~255 사이 숫자를 입력하세요.");
            return;
        }
        _ctx.Structural(() => p.SetLayerProperties(index, opacity: v));
    }

    private void ToggleLayer(int index, bool lockToggle)
    {
        if (P is not { } p) return;
        var l = p.Layers[index];
        _ctx.Structural(() =>
        {
            if (lockToggle) p.SetLayerProperties(index, locked: !l.Locked);
            else p.SetLayerProperties(index, visible: !l.Visible);
        });
    }

    // ───────────── 스킨(그룹 = 스킨 슬롯) ─────────────

    private static int SkinIndex(Layer group)
    {
        var skins = group.Skins;
        for (int i = 0; i < skins.Count; i++)
            if (ReferenceEquals(skins[i], group.ActiveSkin)) return i;
        return 0;
    }

    /// <summary>스킨 관리 창을 연다(<paramref name="focus"/> = 강조·스크롤할 그룹/부위, null이면 그대로).</summary>
    public void OpenSkinManager(Layer? focus) => SkinManagerWindow.Open(_ctx, this, focus);

    /// <summary>메뉴 글자를 그대로 보이는 머리글(스킨·그룹 이름의 '_'가 접근 키로 바뀌지 않게).</summary>
    private static TextBlock Plain(string text) => new() { Text = text };

    /// <summary>그룹 스킨 메뉴 항목: 스킨 목록(고르면 바뀜, 지금 스킨에 표시) + 추가·복제·이름·삭제·순서(지금 스킨 대상) + 부위 되돌리기·관리 창.</summary>
    private List<Control> SkinMenuItems(Layer group)
    {
        var skins = group.Skins;
        var active = group.ActiveSkin!;
        int at = SkinIndex(group);
        var items = new List<Control>();
        for (int i = 0; i < skins.Count; i++)
        {
            var skin = skins[i];
            var item = new MenuItem
            {
                Header = Plain(i == 0 ? $"{skin.Name} (기본)" : skin.Name),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = ReferenceEquals(skin, active),
            };
            item.Click += (_, _) => Dispatcher.UIThread.Post(() => _ctx.SetActiveSkin(group, skin));
            items.Add(item);
        }
        items.Add(new Separator());
        items.Add(Item("새 스킨(빈)", () => SkinOps.AddSkin(_ctx, group, null)));
        items.Add(Item("지금 스킨 복제", () => SkinOps.AddSkin(_ctx, group, active)));
        items.Add(Item("스킨 이름 바꾸기…", () => SkinOps.RenameSkin(_ctx, this, group, active)));
        items.Add(Item("스킨 삭제", () => SkinOps.DeleteSkin(_ctx, this, group, active), skins.Count > 1));
        items.Add(Item("스킨 앞으로", () => SkinOps.MoveSkin(_ctx, group, at, at - 1), at > 0));
        items.Add(Item("스킨 뒤로", () => SkinOps.MoveSkin(_ctx, group, at, at + 1), at < skins.Count - 1));
        items.Add(new Separator());
        bool anyOverride = P is { } p && p.ChildrenOf(group).Any(x => x.SkinOverride is not null);
        items.Add(Item("모든 부위를 그룹 스킨으로", () => _ctx.ResetPartSkins(group), anyOverride));
        items.Add(Item("스킨 관리…", () => OpenSkinManager(group)));
        return items;
    }

    /// <summary>부위 "이 부위 스킨" 메뉴 항목: 그룹 따름 + 고를 수 있는 스킨(그룹 스킨 + 이 부위 전용 "(개별)", 지금 고른 것에 표시) + "＋ 이 부위 스킨…".</summary>
    private List<Control> PartSkinMenuItems(Layer part, Layer group)
    {
        var own = part.SkinOverride;
        var follow = new MenuItem
        {
            Header = Plain($"없음 (그룹 스킨 {group.ActiveSkin!.Name} 그대로)"),
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = own is null,
        };
        follow.Click += (_, _) => Dispatcher.UIThread.Post(() => _ctx.SetPartSkin(part, null));
        var items = new List<Control> { follow, new Separator() };
        var skins = part.SkinChoices;
        for (int i = 0; i < skins.Count; i++)
        {
            var skin = skins[i];
            var item = new MenuItem
            {
                Header = Plain(skin.IsPartOnly ? $"{skin.Name} (개별)" : ReferenceEquals(skin, group.BaseSkin) ? $"{skin.Name} (기본)" : skin.Name),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = ReferenceEquals(skin, own),
            };
            item.Click += (_, _) => Dispatcher.UIThread.Post(() => _ctx.SetPartSkin(part, skin));
            items.Add(item);
        }
        items.Add(new Separator());
        items.Add(Item("＋ 이 부위 스킨…", () => SkinOps.AddPartSkin(_ctx, this, part, null, prompt: true)));
        items.Add(Item("스킨 관리…", () => OpenSkinManager(part)));
        return items;
    }

    // ───────────── 태그 ─────────────

    /// <summary>고른 프레임 범위(없으면 현재 프레임)로 태그를 만든다.</summary>
    public void NewTag()
    {
        if (P is not { } p) return;
        var frames = SelectedFrames();
        int from = frames.Min(), to = frames.Max();
        var dlg = new TagDialog(p, null, from, to);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            AnimationTag? tag = null;
            _ctx.Structural(() => p.Edit("태그 추가", ProjectChange.Tags, () =>
            {
                tag = p.AddTag(dlg.TagName, dlg.From, dlg.To, dlg.Direction, dlg.Loop);
                p.SetTag(p.Tags.ToList().IndexOf(tag), color: dlg.TagColor);
            }));
            _ctx.SelectedTag = tag;
        }
        catch (ArgumentException ex)
        {
            Ui.Error(this, ex.Message);
        }
    }

    private void EditTag(AnimationTag tag)
    {
        if (P is not { } p) return;
        int index = p.Tags.ToList().IndexOf(tag);
        if (index < 0) return;
        var dlg = new TagDialog(p, tag, tag.From, tag.To);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _ctx.Structural(() => p.SetTag(index, dlg.TagName, dlg.From, dlg.To, dlg.Direction, dlg.Loop, dlg.TagColor));
        }
        catch (ArgumentException ex)
        {
            Ui.Error(this, ex.Message);
        }
    }

    /// <summary>태그 범위만 바꾼다(끝 손잡이 끌기, 실행취소 1단계).</summary>
    private void ResizeTag(AnimationTag tag, int from, int to)
    {
        if (P is not { } p) return;
        int index = p.Tags.ToList().IndexOf(tag);
        if (index < 0) return;
        try
        {
            _ctx.Structural(() => p.SetTag(index, from: from, to: to));
            _grid.SetRange(from, to, header: true);
        }
        catch (ArgumentException ex)
        {
            Ui.Error(this, ex.Message);
        }
    }

    private void DeleteTag(AnimationTag tag)
    {
        if (P is not { } p) return;
        int index = p.Tags.ToList().IndexOf(tag);
        if (index < 0) return;
        _ctx.Structural(() => p.RemoveTag(index));
    }

    // ───────────── 우클릭 메뉴 ─────────────

    private static MenuItem Item(string text, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = Ui.Shortcut(text), IsEnabled = enabled };
        // 메뉴가 닫힌 뒤 실행해 모달 대화상자가 메뉴와 겹치지 않게 한다.
        item.Click += (_, _) => Dispatcher.UIThread.Post(action);
        return item;
    }

    private void ShowMenu(params Control[] items)
    {
        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(_grid);
    }

    /// <summary>프레임 머리글·셀 우클릭 메뉴. 셀이면(<paramref name="layer"/> ≥ 0) 셀 동작도 넣는다.</summary>
    private void ShowFrameMenu(int frame, int layer = -1)
    {
        if (P is not { } p) return;
        var frames = SelectedFrames();
        string range = frames.Count > 1 ? $"프레임 {frames.Min() + 1}~{frames.Max() + 1}" : $"프레임 {frame + 1}";
        var items = new List<Control>
        {
            Item($"태그 만들기… ({range})", NewTag),
            new Separator(),
            Item("뒤에 빈 프레임 (Alt+N)", NewEmptyFrame),
            Item("프레임 복제 (Alt+D)", DuplicateFrame),
            Item(frames.Count > 1 ? $"삭제 ({range})" : "삭제", DeleteFrame, p.Frames.Count > 1),
            Item("앞으로 옮기기", () => MoveFrame(-1), frame > 0),
            Item("뒤로 옮기기", () => MoveFrame(1), frame < p.Frames.Count - 1),
            Item($"길이… ({p.Frames[frame].DurationMs}ms)", SetDuration),
            new Separator(),
            Item(layer >= 0 ? "셀 복사 (Ctrl+C)" : "프레임 복사 (Ctrl+C)", () => CopySelection(false)),
            Item(layer >= 0 ? "셀 잘라내기 (Ctrl+X)" : "프레임 잘라내기 (Ctrl+X)", () => CopySelection(true)),
            Item(_clip is { WholeFrames: true } ? "프레임 붙여넣기 (Ctrl+V)" : "셀 붙여넣기 (Ctrl+V)", () => PasteSelection(), _clip is not null),
        };
        if (layer >= 0 && !p.Layers[layer].IsGroup)
        {
            items.Add(new Separator());
            bool empty = p.IsCelEmpty(layer, frame), inherited = p.IsCelInherited(layer, frame);
            items.Add(Item("이전 프레임 그림 복사", () => CopyCelFromPrevious(layer, frame), empty && PreviousCelFrame(p, layer, frame) >= 0));
            // 빌린 셀은 이 스킨 자신의 그림이 없어 지울 것이 없다(기본 스킨 셀은 기본 스킨에서 지움).
            items.Add(Item(inherited ? "셀 지우기 (기본 스킨 그림을 빌려 보이는 중)" : "셀 지우기", () => ClearCel(layer, frame), !empty && !inherited));
        }
        items.Add(new Separator());
        items.Add(Item("피벗을 기본값으로", () => _ctx.Structural(() => p.SetFramePivot(frame, null))));
        items.Add(Item("이 프레임만 피벗 따로 쓰기", () => _ctx.Structural(() => p.SetFramePivot(frame, p.GetFramePivot(frame)))));
        items.Add(Item("내보낼 칸 이름…", () =>
        {
            var name = InputDialog.Ask(this, "칸 이름", "내보낼 칸 이름(비우면 규칙: {태그}_{번호})", p.Frames[frame].Name ?? "");
            if (name is not null) _ctx.Structural(() => p.SetFrameName(frame, name));
        }));
        ShowMenu([.. items]);
    }

    /// <summary>레이어 이름 열 빈 곳·왼쪽 위 칸 우클릭: 새로 만들기.</summary>
    private void ShowEmptyMenu() => ShowMenu(
        Item("새 레이어 (Shift+N)", AddLayer),
        Item("새 그룹", AddGroup),
        Item("새 부위… (Shift+P)", AddPart),
        new Separator(),
        Item("끝에 빈 프레임", () => AppendFrame(false, _ctx.CurrentLayer)),
        Item("끝에 마지막 프레임 복사", () => AppendFrame(true, _ctx.CurrentLayer)));

    private void ShowLayerMenu(int index)
    {
        if (P is not { } p) return;
        var layer = p.Layers[index];
        var items = new List<Control>
        {
            Item("이름 바꾸기", BeginRenameLayer),
            Item("불투명도…", () => SetLayerOpacity(index)),
            Item("보이기/숨기기", () => ToggleLayer(index, false)),
            Item("잠금/풀기", () => ToggleLayer(index, true)),
        };
        if (layer.IsPart)
        {
            items.Add(new Separator());
            items.Add(Item($"부위 크기… ({layer.PartSize.Width}×{layer.PartSize.Height})", () => ResizePartAt(index)));
            items.Add(Item($"위치… ({layer.PartOffset.X}, {layer.PartOffset.Y})", () => SetPartPosition(index)));
            if (layer.Parent is { IsGroup: true } owner)
            {
                items.Add(new MenuItem
                {
                    Header = Plain(layer.SkinOverride is { } own ? $"이 부위 스킨: {own.Name} ({(own.IsPartOnly ? "개별" : "부위만 따로")})" : $"이 부위 스킨: 없음 (그룹 스킨 {owner.ActiveSkin!.Name} 그대로)"),
                    ItemsSource = PartSkinMenuItems(layer, owner),
                });
                items.Add(new MenuItem { Header = Plain($"그룹 스킨: {owner.ActiveSkin!.Name}"), ItemsSource = SkinMenuItems(owner) });
            }
        }
        else if (layer.IsGroup)
        {
            items.Add(new Separator());
            items.Add(Item("부위 추가…", () => AddPartTo(layer)));
            items.Add(Item(layer.Collapsed ? "펼치기" : "접기", () => ToggleCollapsed(index)));
            items.Add(new MenuItem { Header = Plain($"스킨: {layer.ActiveSkin!.Name}"), ItemsSource = SkinMenuItems(layer) });
        }
        items.Add(new Separator());
        items.Add(Item("새 레이어", AddLayer));
        items.Add(Item("새 그룹", AddGroup));
        if (!layer.IsGroup) items.Add(Item("새 부위…", AddPart));
        items.Add(Item("복제", DuplicateLayer));
        items.Add(Item("위로", () => MoveLayer(1)));
        items.Add(Item("아래로", () => MoveLayer(-1)));
        items.Add(Item("아래와 병합", MergeDown, CanMergeDown(p, index)));
        items.Add(Item("삭제", DeleteLayer));
        ShowMenu([.. items]);
    }

    private void ShowTagMenu(AnimationTag tag) => ShowMenu(
        Item("태그 속성…", () => EditTag(tag)),
        Item("이 태그 재생", () => { _ctx.SelectedTag = tag; _ctx.SetCurrentFrame(tag.From); _preview.Play(); }),
        Item("삭제", () => DeleteTag(tag)));

    // ───────────── 격자 ─────────────

    /// <summary>
    /// 직접 그리는 격자. 가로/세로 스크롤 막대와 이름 고치기 상자를 자식으로 두고, 그 아래에 셀을 그린다.
    /// 행은 위 레이어부터이고, 접힌 그룹의 부위 행은 빠진다.
    /// </summary>
    private sealed class TimelineGrid : Avalonia.Controls.Grid
    {
        private const int NameW = 270, TagH = 20, HeadH = 30, CellH = 36, CellW = 42;
        /// <summary>이름 칸: 눈(0~24) · 자물쇠(24~48) · 그룹 접기 단추(48~64) · 이름(64~, 부위는 들여 씀) · 스킨 칩 · (부위) 크기.</summary>
        private const int ToggleX = 48, NameX = 64, Indent = 16;
        /// <summary>스킨 칩 폭: 최대(긴 스킨 이름은 …), 최소(이름이 길어도 이만큼은 보임). 이름이 먼저 자리를 차지한다.</summary>
        private const int SkinChipMaxW = 96, SkinChipMinW = 34;
        /// <summary>기본 스킨 그림을 빌려 보이는 셀 썸네일의 불투명도.</summary>
        private const double InheritedOpacity = 0.35;
        private static readonly IPen ChipAccentPen = AppTheme.Pen(AppTheme.AccentColor);
        private static readonly IBrush ChipAccentBrush = AppTheme.Brush(Color.FromArgb(90, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));

        private static readonly IPen BorderPen = AppTheme.Pen(AppTheme.BorderColor);
        private static readonly IPen AccentPen = AppTheme.Pen(AppTheme.AccentColor, 2);
        private static readonly IPen WhitePen = AppTheme.Pen(Colors.White);
        private static readonly IPen PlayPen = AppTheme.Pen(Color.FromRgb(120, 230, 120), 2);
        private static readonly IBrush RangeBrush = AppTheme.Brush(Color.FromArgb(60, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
        private static readonly IBrush PivotBrush = AppTheme.Brush(Color.FromRgb(255, 120, 80));

        private readonly TimelinePanel _owner;
        private readonly ScrollBar _h = new() { Orientation = Orientation.Horizontal, AllowAutoHide = false, Minimum = 0 };
        private readonly ScrollBar _v = new() { Orientation = Orientation.Vertical, AllowAutoHide = false, Minimum = 0 };
        private TextBox? _rename;
        private Layer? _renameLayer;
        private int _rangeA = -1, _rangeB = -1;
        private int _dragFrom = -1, _dropAt = -1, _layerDragFrom = -1, _layerDropAt = -1;
        private Avalonia.Point _press;
        /// <summary>마지막 범위를 프레임 머리글에서 골랐는지(복사·붙여넣기·삭제가 프레임 전체 대상).</summary>
        private bool _headerRange;
        /// <summary>셀·머리글 끌어서 범위 고르기 중인 시작 프레임(-1 = 아님).</summary>
        private int _selectFrom = -1;
        /// <summary>셀 선택의 끝 행(시작 행 = 현재 레이어). null이면 현재 레이어 한 행.</summary>
        private Layer? _rowEnd;
        /// <summary>셀 묶음 끌기: 옮길 행(레이어 번호, 위→아래)·프레임 범위, 잡은 칸(묶음 왼쪽 위 기준), 놓을 자리(보이는 행·프레임).</summary>
        private List<int>? _blockRows;
        private int _blockFrom, _blockCount, _grabRow, _grabFrame;
        /// <summary>묶음 안을 누른 칸(끌지 않고 놓으면 그 칸만 고른다).</summary>
        private (int Layer, int Frame) _blockPress = (-1, -1);
        private (int Row, int Frame) _blockDrop = (-1, -1);
        private static readonly IPen SelPen = AppTheme.Pen(Color.FromArgb(150, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
        /// <summary>마우스가 올라간 셀(Frame == 프레임 수면 "다음 프레임" 칸)과 왼쪽 반(true)/오른쪽 반.</summary>
        private (int Layer, int Frame, bool Left) _hover = (-1, -1, false);
        /// <summary>태그 범위 끌기: 대상 태그, 오른쪽 끝(true)/왼쪽 끝, 끄는 중인 범위.</summary>
        private AnimationTag? _tagEdit;
        private bool _tagEditRight;
        private int _tagFrom, _tagTo;
        /// <summary>끝 손잡이에 마우스가 올라간 태그(손잡이 강조용).</summary>
        private AnimationTag? _tagHover;
        private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
        private static readonly IBrush GhostBrush = AppTheme.Brush(Color.FromRgb(38, 38, 43));
        private int _playing = -1;
        private readonly PaintSurface _surface;
        /// <summary>행(모든 레이어, 위→아래)·열(프레임) 배치 애니메이션. 그리기와 클릭 위치 찾기가 함께 쓴다.</summary>
        private readonly Track _rowT = new(CellH), _colT = new(CellW);
        private readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

        /// <summary>
        /// 한 축(행 또는 열) 칸들의 배치. 칸은 프로젝트의 레이어/프레임 객체(참조)로 알아본다. 새 칸은 0에서 제 크기로 자라고,
        /// 숨는 칸(접힌 그룹의 부위)은 0으로 줄며, 지운 칸은 유령으로 남아 0까지 줄어든 뒤 빠진다.
        /// 멈춰 있으면 칸 위치 = (앞에 있는 보이는 칸 수 × 칸 크기)로 애니메이션 없는 배치와 정확히 같다.
        /// </summary>
        private sealed class Track(int size)
        {
            private static readonly long Duration = System.Diagnostics.Stopwatch.Frequency * 180 / 1000;

            /// <summary>한 값(0~1)의 ease-out cubic 이동.</summary>
            private struct Anim
            {
                public double From, To;
                public long Start;

                public readonly double At(long now)
                {
                    if (From == To) return To;
                    double t = (double)(now - Start) / Duration;
                    if (t >= 1) return To;
                    double u = 1 - t;
                    return From + (To - From) * (1 - u * u * u);
                }

                public void Go(double target, long now, bool animate)
                {
                    if (!animate) { From = To = target; return; }
                    if (target == To) return;
                    From = At(now);
                    To = target;
                    Start = now;
                }

                /// <summary>끝났으면 목표값에 고정하고 true.</summary>
                public bool Settle(long now)
                {
                    if (From != To && now - Start >= Duration) From = To;
                    return From == To;
                }
            }

            private sealed class Slot(object item)
            {
                public readonly object Item = item;
                /// <summary>칸 번호(-1 = 지워져 줄어드는 유령).</summary>
                public int Index = -1;
                public bool Shown;
                public Anim Size, Open;
                public int Px, Pos, TargetPos;
                public double OpenNow;
            }

            /// <summary>칸 하나의 목표 상태: 보임 여부와 (그룹 행) 펼침 여부.</summary>
            public readonly record struct Entry(object Item, bool Shown, bool Open);

            /// <summary>그릴 때 보는 칸: 칸 번호(-1 = 유령), 현재 위치·크기(px), 펼침 정도(0~1).</summary>
            public readonly record struct View(object Item, int Index, int Pos, int Px, double Open);

            private List<Slot> _slots = new();
            private Slot[] _live = [];

            public int Count => _live.Length;
            /// <summary>모든 칸의 현재 크기 합.</summary>
            public int Total { get; private set; }
            /// <summary>애니메이션이 끝났을 때의 크기 합.</summary>
            public int TargetTotal { get; private set; }
            public bool Animating { get; private set; }
            public int SlotCount => _slots.Count;

            public View Get(int k)
            {
                var s = _slots[k];
                return new View(s.Item, s.Index, s.Pos, s.Px, s.OpenNow);
            }

            /// <summary>칸 <paramref name="i"/>가 이미 이 상태인지(바뀐 게 없으면 다시 맞추지 않으려고).</summary>
            public bool Matches(int i, object item, bool shown, bool open)
                => _live[i] is var s && ReferenceEquals(s.Item, item) && s.Shown == shown && s.Open.To == (open ? 1 : 0);

            /// <summary>
            /// 칸 목록을 새 상태에 맞춘다. <paramref name="animate"/>면 크기·펼침이 지금 값에서 목표로 움직이고, 빠진 칸은 원래 자리(앞 칸 뒤)에
            /// 유령으로 남아 줄어든다. 아니면 바로 목표 배치.
            /// </summary>
            public void Sync(Entry[] items, bool animate, long now)
            {
                var old = _slots;
                var byItem = new Dictionary<object, Slot>(old.Count, ReferenceEqualityComparer.Instance);
                foreach (var s in old)
                {
                    s.Index = -1;
                    byItem.TryAdd(s.Item, s);
                }
                var next = new List<Slot>(items.Length);
                var live = new Slot[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    var e = items[i];
                    double open = e.Open ? 1 : 0;
                    if (!byItem.TryGetValue(e.Item, out var s))
                    {
                        s = new Slot(e.Item);
                        s.Size.Go(0, now, false);
                        s.Open.Go(open, now, false);
                    }
                    s.Index = i;
                    s.Shown = e.Shown;
                    s.Size.Go(e.Shown ? 1 : 0, now, animate);
                    s.Open.Go(open, now, animate);
                    live[i] = s;
                    next.Add(s);
                }
                if (animate)
                {
                    Slot? anchor = null;
                    foreach (var s in old)
                    {
                        if (s.Index >= 0) { anchor = s; continue; }
                        if (s.Size.At(now) <= 0) continue;
                        s.Shown = false;
                        s.Size.Go(0, now, true);
                        next.Insert(anchor is null ? 0 : next.IndexOf(anchor) + 1, s);
                        anchor = s;
                    }
                }
                _slots = next;
                _live = live;
                Update(now);
            }

            /// <summary>지금 시각의 크기·위치를 계산한다(다 줄어든 유령은 뺀다).</summary>
            public void Update(long now)
            {
                int pos = 0, target = 0;
                bool animating = false;
                for (int k = 0; k < _slots.Count; k++)
                {
                    var s = _slots[k];
                    bool done = s.Size.Settle(now) & s.Open.Settle(now);
                    if (s.Index < 0 && done) { _slots.RemoveAt(k--); continue; }
                    animating |= !done;
                    s.Px = (int)Math.Round(s.Size.At(now) * size);
                    s.OpenNow = s.Open.At(now);
                    s.Pos = pos;
                    s.TargetPos = target;
                    pos += s.Px;
                    if (s.Index >= 0 && s.Shown) target += size;
                }
                Total = pos;
                TargetTotal = target;
                Animating = animating;
            }

            /// <summary>칸 <paramref name="i"/>의 현재 시작 위치(범위 밖은 칸 크기로 이어서).</summary>
            public int Pos(int i) => i < 0 ? i * size : i < _live.Length ? _live[i].Pos : Total + (i - _live.Length) * size;

            /// <summary>애니메이션이 끝났을 때의 시작 위치.</summary>
            public int TargetPos(int i) => i < 0 ? i * size : i < _live.Length ? _live[i].TargetPos : TargetTotal + (i - _live.Length) * size;

            /// <summary>칸 <paramref name="i"/>의 현재 크기(범위 밖은 칸 크기).</summary>
            public int Px(int i) => (uint)i < (uint)_live.Length ? _live[i].Px : size;

            /// <summary><paramref name="pos"/>를 덮는 칸(크기가 있는 칸 중 시작 위치가 pos 이하인 마지막 칸).</summary>
            private int SlotAt(double pos)
            {
                int lo = 0, hi = _slots.Count - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (_slots[mid].Pos <= pos) lo = mid;
                    else hi = mid - 1;
                }
                return lo;
            }

            /// <summary>
            /// <paramref name="pos"/> 위 칸 번호(앞뒤 바깥은 칸 크기로 이어서 센다). 숨거나 지워져 줄어드는 칸 위면
            /// <paramref name="exact"/>일 때 -1, 아니면 그다음 보이는 칸(없으면 <see cref="Count"/>).
            /// </summary>
            public int IndexAt(double pos, bool exact)
            {
                if (pos < 0) return (int)Math.Floor(pos / size);
                if (pos >= Total) return _live.Length + (int)Math.Floor((pos - Total) / size);
                int k = SlotAt(pos);
                if (_slots[k] is { Index: >= 0, Shown: true } hit) return hit.Index;
                if (exact) return -1;
                for (k++; k < _slots.Count; k++)
                    if (_slots[k] is { Index: >= 0, Shown: true } s) return s.Index;
                return _live.Length;
            }

            /// <summary><paramref name="pos"/>에서 가장 가까운 칸 경계(칸 i 앞 = i).</summary>
            public int BoundaryAt(double pos)
            {
                if (pos < 0) return (int)Math.Round(pos / size);
                if (pos >= Total) return _live.Length + (int)Math.Round((pos - Total) / size);
                var s = _slots[SlotAt(pos)];
                return s is { Index: >= 0, Shown: true } ? (int)Math.Round(s.Index + (pos - s.Pos) / s.Px) : IndexAt(pos, false);
            }
        }

        /// <summary>
        /// 행·열 배치를 프로젝트에 맞춘다(바뀐 게 없으면 할 일 없음). <paramref name="animate"/>면 새·지운·접고 펼친 행과 새·지운 열이
        /// 부드럽게 자라고 줄며, 아니면(다른 파일 열기) 바로 맞춘다.
        /// </summary>
        public void SyncLayout(bool animate)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            var p = Ctx.Project;
            int n = p?.Layers.Count ?? 0, frames = p?.Frames.Count ?? 0;
            static bool Shown(Layer l) => l.Parent is not { Collapsed: true };
            static bool Open(Layer l) => l.IsGroup && !l.Collapsed;
            bool same = animate && n == _rowT.Count;
            for (int i = 0; same && i < n; i++)
            {
                var l = p!.Layers[n - 1 - i];
                same = _rowT.Matches(i, l, Shown(l), Open(l));
            }
            if (!same)
            {
                var rows = new Track.Entry[n];
                for (int i = 0; i < n; i++)
                {
                    var l = p!.Layers[n - 1 - i];
                    rows[i] = new Track.Entry(l, Shown(l), Open(l));
                }
                _rowT.Sync(rows, animate, now);
            }
            same = animate && frames == _colT.Count;
            for (int f = 0; same && f < frames; f++) same = _colT.Matches(f, p!.Frames[f], true, false);
            if (!same)
            {
                var cols = new Track.Entry[frames];
                for (int f = 0; f < frames; f++) cols[f] = new Track.Entry(p!.Frames[f], true, false);
                _colT.Sync(cols, animate, now);
            }
            if (_rowT.Animating || _colT.Animating)
            {
                if (!_animTimer.IsEnabled) _animTimer.Start();
            }
            else _animTimer.Stop();
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            _rowT.Update(now);
            _colT.Update(now);
            if (!_rowT.Animating && !_colT.Animating) _animTimer.Stop();
            UpdateScrollSize();
            InvalidateVisual();
        }

        /// <summary>Panel.Render는 봉인돼 있으므로 그리기는 맨 아래 자식 컨트롤이 맡는다(같은 원점).</summary>
        private sealed class PaintSurface(TimelineGrid grid) : Control
        {
            public override void Render(DrawingContext g) => grid.Paint(g);
        }

        /// <summary>그림을 다시 그린다(그리기 면을 무효화).</summary>
        public new void InvalidateVisual() => _surface.InvalidateVisual();

        public TimelineGrid(TimelinePanel owner)
        {
            _owner = owner;
            _surface = new PaintSurface(this) { ClipToBounds = true, IsHitTestVisible = false };
            Children.Add(_surface);
            Background = AppTheme.InputBrush;
            ClipToBounds = true;
            Focusable = true;
            RowDefinitions = new RowDefinitions("*,Auto");
            ColumnDefinitions = new ColumnDefinitions("*,Auto");
            SetRow(_h, 1);
            SetColumn(_v, 1);
            Children.Add(_h);
            Children.Add(_v);
            _h.ValueChanged += (_, _) => InvalidateVisual();
            _v.ValueChanged += (_, _) => { EndRename(true); InvalidateVisual(); };
            SizeChanged += (_, _) => UpdateScrollSize();
            _v.SizeChanged += (_, _) => UpdateScrollSize();
            _h.SizeChanged += (_, _) => UpdateScrollSize();
            _animTimer.Tick += OnAnimTick;
        }

        private EditorContext Ctx => _owner._ctx;

        private int HValue => (int)_h.Value;
        private int VValue => (int)_v.Value;

        public (int A, int B) Range => _rangeA < 0 ? (-1, -1) : (Math.Min(_rangeA, _rangeB), Math.Max(_rangeA, _rangeB));

        public int PlayingFrame
        {
            get => _playing;
            set { _playing = value; InvalidateVisual(); }
        }

        public void ResetRange() { _rangeA = _rangeB = -1; _headerRange = false; _rowEnd = null; InvalidateVisual(); }

        /// <summary>보이는 행의 레이어 번호(위→아래).</summary>
        public List<int> VisibleRows() => Rows();

        /// <summary>셀 선택의 행(레이어 번호, 위→아래): 현재 레이어 행부터 끝 행까지.</summary>
        public List<int> SelectedRowLayers()
        {
            var rows = Rows();
            int a = rows.IndexOf(Ctx.CurrentLayer);
            if (a < 0) return [Ctx.CurrentLayer];
            int b = _rowEnd is not null && Ctx.Project is { } p ? rows.IndexOf(p.IndexOf(_rowEnd)) : -1;
            if (b < 0) b = a;
            return rows.GetRange(Math.Min(a, b), Math.Abs(a - b) + 1);
        }

        /// <summary>셀 묶음을 고른다: 시작 칸(현재) = (<paramref name="layer"/>, <paramref name="frameA"/>), 끝 = (<paramref name="end"/>, <paramref name="frameB"/>).</summary>
        public void SelectRect(int layer, int frameA, Layer? end, int frameB)
        {
            Ctx.SetCurrent(layer, frameA);
            bool single = frameA == frameB && (end is null || Ctx.Project?.IndexOf(end) == layer);
            if (single) { ResetRange(); return; }
            _rangeA = frameA;
            _rangeB = frameB;
            _headerRange = false;
            _rowEnd = end;
            InvalidateVisual();
        }

        /// <summary>범위를 프레임 머리글에서 골랐는지.</summary>
        public bool HeaderRange => _headerRange && _rangeA >= 0;

        /// <summary>범위를 정한다(셀 옮긴 뒤 등).</summary>
        public void SetRange(int a, int b, bool header) { _rangeA = a; _rangeB = b; _headerRange = header; _rowEnd = null; InvalidateVisual(); }

        private bool InRange(int f) => _rangeA >= 0 && f >= Math.Min(_rangeA, _rangeB) && f <= Math.Max(_rangeA, _rangeB);

        public void ClampRange()
        {
            if (_rangeA >= Ctx.FrameCount || _rangeB >= Ctx.FrameCount) ResetRange();
            else if (_rowEnd is not null && Ctx.Project is { } p && p.IndexOf(_rowEnd) < 0) _rowEnd = null;
        }

        /// <summary>보이는 행의 레이어 번호(맨 위 레이어부터, 접힌 그룹의 부위는 뺌).</summary>
        private List<int> Rows()
        {
            var rows = new List<int>();
            if (Ctx.Project is not { } p) return rows;
            for (int l = p.Layers.Count - 1; l >= 0; l--)
                if (p.Layers[l].Parent is not { Collapsed: true }) rows.Add(l);
            return rows;
        }

        /// <summary>스크롤 막대를 뺀 그리기 영역 크기.</summary>
        private double ClientW => Bounds.Width - (_v.IsVisible ? _v.Bounds.Width : 0);
        private double ClientH => Bounds.Height - (_h.IsVisible ? _h.Bounds.Height : 0);
        private int ViewW => (int)ClientW - NameW;
        private int ViewH => (int)ClientH - TagH - HeadH;

        public void UpdateScrollSize()
        {
            // 마지막 프레임 뒤 "다음 프레임" 칸까지. 애니메이션 중에는 지금·끝 배치 중 큰 쪽(줄어들 때 스크롤이 튀지 않게).
            int contentW = Math.Max(_colT.Total, _colT.TargetTotal) + CellW, contentH = Math.Max(_rowT.Total, _rowT.TargetTotal);
            int viewW = Math.Max(1, ViewW), viewH = Math.Max(1, ViewH);
            _h.Maximum = Math.Max(0, contentW - viewW);
            _h.ViewportSize = viewW;
            _h.LargeChange = viewW;
            _h.SmallChange = CellW;
            _h.IsEnabled = contentW > viewW;
            if (!_h.IsEnabled) _h.Value = 0;
            _v.Maximum = Math.Max(0, contentH - viewH);
            _v.ViewportSize = viewH;
            _v.LargeChange = viewH;
            _v.SmallChange = CellH;
            _v.IsEnabled = contentH > viewH;
            if (!_v.IsEnabled) _v.Value = 0;
        }

        /// <summary>현재 프레임 열과 현재 레이어 행이 보이도록 스크롤한다(애니메이션이 끝났을 때의 자리 기준).</summary>
        public void EnsureCurrentVisible()
        {
            if (_h.IsEnabled)
            {
                int x = _colT.TargetPos(Ctx.CurrentFrame);
                if (x < HValue) _h.Value = x;
                else if (x + CellW > HValue + ViewW) _h.Value = Math.Clamp(x + CellW - ViewW, 0, _h.Maximum);
            }
            if (_v.IsEnabled && Rows().Contains(Ctx.CurrentLayer))
            {
                int y = _rowT.TargetPos(RowItem(Ctx.CurrentLayer));
                if (y < VValue) _v.Value = y;
                else if (y + CellH > VValue + ViewH) _v.Value = Math.Clamp(y + CellH - ViewH, 0, _v.Maximum);
            }
            InvalidateVisual();
        }

        /// <summary>레이어 번호 → 행 배치의 칸 번호(행은 맨 위 레이어부터).</summary>
        private int RowItem(int layer) => _rowT.Count - 1 - layer;

        /// <summary>프레임 열의 왼쪽 x와 현재 폭(애니메이션 중이면 자라거나 줄어드는 중).</summary>
        private int FrameX(int f) => NameW + _colT.Pos(f) - HValue;
        private int FrameW(int f) => _colT.Px(f);

        /// <summary>레이어 행의 위쪽 y.</summary>
        private int LayerY(int layer) => TagH + HeadH + _rowT.Pos(RowItem(layer)) - VValue;

        /// <summary>격자 내용 좌표(스크롤 포함).</summary>
        private double ContentX(double x) => x - NameW + HValue;
        private double ContentY(double y) => y - TagH - HeadH + VValue;

        private int FrameAt(double x)
        {
            if (x < NameW) return -1;
            int f = _colT.IndexAt(ContentX(x), exact: true);
            return f >= 0 && f < Ctx.FrameCount ? f : -1;
        }

        /// <summary>범위 밖이면 가장 가까운 프레임.</summary>
        private int FrameAtClamped(double x)
            => Math.Clamp(_colT.IndexAt(ContentX(x), exact: false), 0, Math.Max(0, Ctx.FrameCount - 1));

        /// <summary>"다음 프레임" 칸 위인지.</summary>
        private bool IsGhostColumn(double x)
            => x >= NameW && ContentX(x) >= _colT.Total && ContentX(x) < _colT.Total + CellW;

        /// <summary>프레임 칸의 왼쪽 반 위인지.</summary>
        private bool LeftHalf(double x, int f) => x < FrameX(f) + FrameW(f) / 2.0;

        /// <summary>가장 가까운 보이는 행 번호.</summary>
        private int RowAtClamped(double y)
        {
            var rows = Rows();
            if (rows.Count == 0) return 0;
            int item = _rowT.IndexAt(ContentY(y), exact: false);
            if (item < 0) return 0;
            if (item >= _rowT.Count) return rows.Count - 1;
            return Math.Max(0, rows.IndexOf(_rowT.Count - 1 - item));
        }

        /// <summary>y 위 레이어(숨거나 지워져 줄어드는 중인 행 위면 -1).</summary>
        private int LayerAt(double y)
        {
            if (y < TagH + HeadH) return -1;
            int item = _rowT.IndexAt(ContentY(y), exact: true);
            return item >= 0 && item < _rowT.Count ? _rowT.Count - 1 - item : -1;
        }

        /// <summary>그릴 때 쓰는 태그 범위(끄는 중이면 미리보기 범위).</summary>
        private (int From, int To) TagRange(AnimationTag tag)
            => ReferenceEquals(tag, _tagEdit) ? (_tagFrom, _tagTo) : (tag.From, tag.To);

        /// <summary>
        /// 태그 상자 안쪽 양 끝의 손잡이(상자 폭의 1/3, 최대 10px) 위면 그 태그와 끝(오른쪽 = true).
        /// 태그가 겹쳐 있으면 고른 태그 → 위에 그려진 태그 순으로 고른다.
        /// </summary>
        private (AnimationTag Tag, bool Right)? TagEdgeAt(Avalonia.Point pos)
        {
            if (Ctx.Project is not { } proj || pos.Y >= TagH || pos.X < NameW) return null;
            var order = proj.Tags.Reverse().OrderByDescending(t => ReferenceEquals(t, Ctx.SelectedTag));
            foreach (var tag in order)
            {
                double x0 = FrameX(tag.From) + 1, x1 = FrameX(tag.To) + FrameW(tag.To) - 1;
                double grip = Math.Min(10, (x1 - x0) / 3);
                if (pos.X >= x0 && pos.X <= x0 + grip) return (tag, false);
                if (pos.X >= x1 - grip && pos.X <= x1) return (tag, true);
            }
            return null;
        }

        private AnimationTag? TagAt(Avalonia.Point p)
        {
            if (Ctx.Project is not { } proj || p.Y >= TagH || p.X < NameW) return null;
            int f = FrameAt(p.X);
            return f < 0 ? null : proj.Tags.LastOrDefault(t => t.Contains(f));
        }

        /// <summary>레이어 이름이 시작하는 x(부위는 들여 씀).</summary>
        private static int NameLeft(Layer layer) => layer.IsPart ? NameX + Indent : layer.IsGroup ? NameX : ToggleX;

        /// <summary>이름 칸에 보이는 레이어 이름(불투명도가 255가 아니면 % 붙임).</summary>
        private static string LayerTitle(Layer layer) => layer.Name + (layer.Opacity < 255 ? $" ({layer.Opacity * 100 / 255}%)" : "");

        /// <summary>부위 크기 글자("16×16")와 그 폭(이름 칸 맨 오른쪽).</summary>
        private static (string Text, int W) PartSizeText(Layer part)
        {
            var text = $"{part.PartSize.Width}×{part.PartSize.Height}";
            return (text, (int)Math.Ceiling(AppTheme.MeasureText(text, AppTheme.SmallFontSize).Width + 4));
        }

        /// <summary>
        /// 그룹·부위 행 이름 칸 오른쪽 끝의 스킨 칩(x, 폭) — 그룹·부위가 같은 열에 맞춰진다(부위 크기 글자는 칩 왼쪽).
        /// 그룹은 지금 스킨, 부위는 보이는 스킨 이름. 레이어 이름(부위는 크기 글자까지)이 먼저 자리를 차지하고,
        /// 칩은 남은 자리(최소 <see cref="SkinChipMinW"/>, 최대 <see cref="SkinChipMaxW"/>)에 맞춰 이름이 …로 줄어든다.
        /// 폭 0 = 칩 없음(일반 레이어·그룹 없는 부위).
        /// </summary>
        private static (int X, int W) SkinChip(Layer layer, int right)
        {
            if (layer.ShownSkin is not { } skin) return (0, 0);
            int want = (int)Math.Ceiling(Math.Min(SkinChipMaxW, AppTheme.MeasureText(skin.Name, AppTheme.SmallFontSize).Width + 10));
            int nameEnd = NameLeft(layer) + (int)Math.Ceiling(AppTheme.MeasureText(LayerTitle(layer), AppTheme.FontSize, bold: true).Width) + 6
                          + (layer.IsPart ? PartSizeText(layer).W + 4 : 0);
            int w = Math.Max(Math.Min(want, SkinChipMinW), Math.Min(want, right - nameEnd));
            return (right - w, w);
        }

        /// <summary>스킨 칩(<see cref="SkinChip(Layer, int)"/>), 그리기와 같은 자리(클릭 찾기용).</summary>
        private static (int X, int W) SkinChip(Layer layer) => SkinChip(layer, NameW - 4);

        /// <summary>
        /// 스킨 칩 그리기: 그룹 = 테두리 + 지금 스킨 이름, 부위 = 부위만 따로 고른 스킨이면 강조(파랑), 그룹을 따르면 흐린 테두리·글자.
        /// </summary>
        private static void DrawSkinChip(DrawingContext g, Layer layer, Rect r)
        {
            var name = layer.ShownSkin?.Name ?? "";
            IBrush text;
            if (layer.IsPart && layer.SkinOverride is not null)
            {
                g.DrawRectangle(ChipAccentBrush, ChipAccentPen, r.Deflate(0.5), 3, 3);
                text = AppTheme.TextBrush;
            }
            else if (layer.IsPart)
            {
                g.DrawRectangle(null, BorderPen, r.Deflate(0.5), 3, 3);
                text = AppTheme.DimTextBrush;
            }
            else
            {
                g.DrawRectangle(AppTheme.BackBrush, BorderPen, r.Deflate(0.5), 3, 3);
                text = AppTheme.TextBrush;
            }
            Text(g, name, r.Deflate(new Thickness(4, 0)), text, small: true, center: true);
        }

        // ── 레이어 이름 바로 고치기

        public void BeginRename(int index)
        {
            EndRename(true);
            if (Ctx.Project is not { } p || index < 0 || index >= p.Layers.Count) return;
            int row = Rows().IndexOf(index);
            if (row < 0) return;
            var layer = p.Layers[index];
            _renameLayer = layer;
            int x = NameLeft(layer);
            var box = new TextBox
            {
                Text = layer.Name,
                BorderThickness = new Thickness(1),
                BorderBrush = AppTheme.BorderBrush,
                Background = AppTheme.BackBrush,
                Foreground = AppTheme.TextBrush,
                FontFamily = AppTheme.FontFamily,
                FontSize = AppTheme.FontSize,
                Padding = new Thickness(2, 0),
                MinHeight = 0,
                Height = 20,
                Width = NameW - x - 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(x - 2, LayerY(index) + (CellH - 20) / 2, 0, 0),
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Escape)
                {
                    e.Handled = true;
                    EndRename(e.Key == Key.Enter, refocus: true);
                }
            };
            box.LostFocus += (_, _) => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_rename, box)) EndRename(true); });
            _rename = box;
            Children.Add(box);
            box.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }

        /// <summary>이름 고치기를 끝낸다(<paramref name="commit"/>면 적용, <paramref name="refocus"/>면 목록에 포커스).</summary>
        public void EndRename(bool commit, bool refocus = false)
        {
            if (_rename is not { } box) return;
            _rename = null;
            var layer = _renameLayer;
            _renameLayer = null;
            var text = box.Text ?? "";
            Children.Remove(box);
            if (commit && layer is not null) _owner.RenameLayer(layer, text);
            if (refocus) Focus();
            InvalidateVisual();
        }

        private static bool IsBright(Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
            return (max + min) / 510.0 > 0.6;
        }

        private static void Text(DrawingContext g, string text, Rect r, IBrush brush, bool small = false, bool bold = false, bool center = false)
            => AppTheme.DrawTextIn(g, text, r, brush, small ? AppTheme.SmallFontSize : AppTheme.FontSize, bold, center: center);

        /// <summary>
        /// 그룹 접기/펼치기 삼각형: ▸(접힘)을 펼친 정도 <paramref name="open"/>(0~1)만큼 시계 방향으로 90° 돌린다(1이면 ▾).
        /// 글꼴마다 기호 크기가 달라 도형으로 그린다.
        /// </summary>
        private static void DrawDisclosure(DrawingContext g, double cx, double cy, double open)
        {
            double cos = open <= 0 ? 1 : open >= 1 ? 0 : Math.Cos(open * Math.PI / 2);
            double sin = open <= 0 ? 0 : open >= 1 ? 1 : Math.Sin(open * Math.PI / 2);
            Avalonia.Point At(double x, double y) => new(cx + x * cos - y * sin, cy + x * sin + y * cos);
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(At(-3, -5), true);
                c.LineTo(At(4, 0));
                c.LineTo(At(-3, 5));
                c.EndFigure(true);
            }
            g.DrawGeometry(AppTheme.TextBrush, null, geo);
        }

        private static readonly IBrush SplitHoverBrush = AppTheme.Brush(Color.FromRgb(74, 74, 84));

        /// <summary>칸을 반으로 나눠 왼쪽 ⧉(복사)·오른쪽 ＋ 아이콘을 그린다. <paramref name="hoverLeft"/>: 올라간 반(없으면 null).</summary>
        private static void DrawSplit(DrawingContext g, Rect r, bool copyEnabled, bool? hoverLeft, bool dim = false)
        {
            var left = new Rect(r.X, r.Y, r.Width / 2, r.Height);
            var right = new Rect(r.X + r.Width / 2, r.Y, r.Width / 2, r.Height);
            if (hoverLeft == true && copyEnabled) g.FillRectangle(SplitHoverBrush, left);
            if (hoverLeft == false) g.FillRectangle(SplitHoverBrush, right);
            g.DrawLine(BorderPen, new Avalonia.Point(Math.Round(r.Center.X) + 0.5, r.Y + 6), new Avalonia.Point(Math.Round(r.Center.X) + 0.5, r.Bottom - 6));
            var on = dim ? AppTheme.DimTextBrush : AppTheme.TextBrush;
            if (copyEnabled) VectorIcons.Draw(g, VectorIcon.Copy, left, 12, on);
            VectorIcons.Draw(g, VectorIcon.Plus, right, 12, on);
        }

        /// <summary>배치의 행이 가리키는 레이어 번호(지워져 줄어드는 유령 행은 -1).</summary>
        private int LayerOf(SpriteProject p, Track.View row)
        {
            if (row.Index < 0) return -1;
            int l = RowItem(row.Index);
            return (uint)l < (uint)p.Layers.Count && ReferenceEquals(p.Layers[l], row.Item) ? l : p.IndexOf((Layer)row.Item);
        }

        /// <summary>배치의 열이 가리키는 프레임 번호(지워져 줄어드는 유령 열은 -1).</summary>
        private static int FrameOf(SpriteProject p, Track.View col)
            => (uint)col.Index < (uint)p.Frames.Count && ReferenceEquals(p.Frames[col.Index], col.Item) ? col.Index : -1;

        /// <summary>
        /// 행·열은 배치(<see cref="Track"/>)를 따라 그린다. 자라거나 줄어드는 칸은 그 크기만큼 잘라 보이고, 내용은 칸의 아래·오른쪽 끝에
        /// 붙여 그려 앞 칸 밑에서 밀려 나오고 들어가듯 보인다.
        /// </summary>
        private void Paint(DrawingContext g)
        {
            double width = ClientW, height = ClientH;
            g.FillRectangle(AppTheme.InputBrush, new Rect(Bounds.Size));
            if (Ctx.Project is not { } p) return;
            int frames = p.Frames.Count;
            int cur = Ctx.CurrentFrame, curLayer = Ctx.CurrentLayer;
            var (ra, rb) = Range;
            int top = TagH + HeadH - VValue, left = NameW - HValue;
            var cellsClip = new Rect(NameW, TagH + HeadH, Math.Max(0, width - NameW), Math.Max(0, height - TagH - HeadH));
            var headerClip = new Rect(NameW, 0, Math.Max(0, width - NameW), TagH + HeadH);
            var namesClip = new Rect(0, TagH + HeadH, NameW, Math.Max(0, height - TagH - HeadH));

            // 셀 선택(행 × 프레임): 시작 칸은 파란 테두리, 끌어서 넓힌 칸은 옅은 칸
            var selRows = _headerRange ? null : new HashSet<int>(SelectedRowLayers());
            bool Selected(int layer, int frame) => _headerRange ? frame >= ra && frame <= rb && ra >= 0
                : selRows!.Contains(layer) && (ra >= 0 ? frame >= ra && frame <= rb : frame == cur);

            // 셀 하나(그룹 행은 그룹 합성 썸네일, 지워져 줄어드는 행·열은 빈 칸)
            void DrawCell(int x, int y, int l, int f)
            {
                var r = new Rect(x + 1, y + 1, CellW - 2, CellH - 2);
                if (l < 0 || f < 0)
                {
                    g.FillRectangle(AppTheme.PanelBrush, r);
                    g.DrawRectangle(null, BorderPen, r.Deflate(0.5));
                    return;
                }
                g.FillRectangle(f == cur ? AppTheme.HoverBrush : AppTheme.PanelBrush, r);
                bool selected = Selected(l, f);
                if (selected && !(l == curLayer && f == cur && ra < 0 && !_headerRange)) g.FillRectangle(RangeBrush, r);
                if (Ctx.CelBitmap(l, f) is { } bmp)
                {
                    var src = new Rectangle(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
                    // 지금 스킨에 자기 그림이 없어 기본 스킨 셀을 물려받은 칸은 흐리게
                    if (p.IsCelInherited(l, f))
                    {
                        using (g.PushOpacity(InheritedOpacity)) ImageUtil.DrawFitted(g, bmp, src, r.Deflate(3), checker: true);
                    }
                    else ImageUtil.DrawFitted(g, bmp, src, r.Deflate(3), checker: true);
                }
                else
                    g.DrawEllipse(AppTheme.BorderBrush, null, new Rect(r.X + r.Width / 2 - 3, r.Y + r.Height / 2 - 3, 6, 6));
                if (l == curLayer && f == cur) g.DrawRectangle(null, AccentPen, r.Deflate(1));
                else if (selected) g.DrawRectangle(null, SelPen, r.Deflate(1));
                else g.DrawRectangle(null, BorderPen, r.Deflate(0.5));
            }

            using (g.PushClip(cellsClip))
            {
                for (int rk = 0; rk < _rowT.SlotCount; rk++)
                {
                    var row = _rowT.Get(rk);
                    int y = top + row.Pos;
                    if (row.Px <= 0 || y + row.Px < cellsClip.Top || y > cellsClip.Bottom) continue;
                    int l = LayerOf(p, row), cy = y + row.Px - CellH;
                    for (int ck = 0; ck < _colT.SlotCount; ck++)
                    {
                        var col = _colT.Get(ck);
                        int x = left + col.Pos;
                        if (col.Px <= 0 || x + col.Px < cellsClip.Left || x > cellsClip.Right) continue;
                        int cx = x + col.Px - CellW, f = FrameOf(p, col);
                        if (row.Px < CellH || col.Px < CellW)
                        {
                            using (g.PushClip(new Rect(x, y, col.Px, row.Px))) DrawCell(cx, cy, l, f);
                        }
                        else DrawCell(cx, cy, l, f);
                    }
                    // "다음 프레임" 칸: ⧉ 마지막 프레임 복사해서 추가 · ＋ 빈 프레임 추가
                    int gx = left + _colT.Total;
                    if (gx < cellsClip.Right)
                    {
                        using (g.PushClip(new Rect(gx, y, CellW, row.Px)))
                        {
                            var r = new Rect(gx + 1, cy + 1, CellW - 2, CellH - 2);
                            g.FillRectangle(GhostBrush, r);
                            bool hovered = l >= 0 && _hover.Layer == l && _hover.Frame == frames;
                            DrawSplit(g, r, true, hovered ? _hover.Left : null, dim: !hovered);
                        }
                    }
                }
                // 셀 묶음 끌기: 놓을 자리
                if (_blockRows is not null && _blockDrop.Row >= 0)
                {
                    var rows = Rows();
                    for (int i = 0; i < _blockRows.Count && _blockDrop.Row + i < rows.Count; i++)
                        for (int k = 0; k < _blockCount; k++)
                        {
                            var r = new Rect(FrameX(_blockDrop.Frame + k) + 2, LayerY(rows[_blockDrop.Row + i]) + 2, CellW - 4, CellH - 4);
                            g.DrawRectangle(RangeBrush, AccentPen, r);
                        }
                }
                if (_playing >= 0)
                {
                    double x = FrameX(_playing) + FrameW(_playing) / 2;
                    g.DrawLine(PlayPen, new Avalonia.Point(x, cellsClip.Top), new Avalonia.Point(x, cellsClip.Bottom));
                }
            }

            // 프레임 머리글 한 칸(지워져 줄어드는 열은 비움)
            void DrawHeader(int x, int f)
            {
                if (f < 0) return;
                var r = new Rect(x, TagH, CellW, HeadH);
                if (f == cur) g.FillRectangle(AppTheme.HoverBrush, r);
                if (f >= ra && f <= rb && ra >= 0) g.FillRectangle(RangeBrush, r);
                Text(g, (f + 1).ToString(), new Rect(x, TagH, CellW, 16), f == cur ? AppTheme.AccentBrush : AppTheme.TextBrush, bold: true, center: true);
                Text(g, p.Frames[f].DurationMs + "ms", new Rect(x, TagH + 15, CellW, 14), AppTheme.DimTextBrush, small: true, center: true);
                if (p.Frames[f].Pivot is not null)
                    AppTheme.DrawText(g, "◆", new Avalonia.Point(x + CellW - 12, TagH), PivotBrush, AppTheme.SmallFontSize);
            }

            // 태그 띠 + 프레임 머리글
            using (g.PushClip(headerClip))
            {
                g.FillRectangle(AppTheme.PanelBrush, headerClip);
                foreach (var tag in p.Tags)
                {
                    var (tf, tt) = TagRange(tag);
                    int x0 = FrameX(tf), x1 = FrameX(tt) + FrameW(tt);
                    var r = new Rect(x0 + 1, 2, Math.Max(0, x1 - x0 - 2), TagH - 4);
                    var color = tag.Color.ToColor();
                    g.FillRectangle(AppTheme.Brush(color), r);
                    if (ReferenceEquals(tag, Ctx.ActiveTag) || ReferenceEquals(tag, _tagEdit)) g.DrawRectangle(null, WhitePen, r.Deflate(0.5));
                    if (ReferenceEquals(tag, _tagHover) || ReferenceEquals(tag, _tagEdit))
                    {
                        // 끝 손잡이(끌어서 범위 바꾸기)
                        var grip = IsBright(color) ? Brushes.Black : Brushes.White;
                        g.FillRectangle(grip, new Rect(r.X + 2, r.Y + 3, 2, r.Height - 6));
                        g.FillRectangle(grip, new Rect(r.Right - 4, r.Y + 3, 2, r.Height - 6));
                    }
                    var arrow = tag.Direction switch { TagDirection.Reverse => "◀ ", TagDirection.PingPong => "↔ ", _ => "" };
                    Text(g, arrow + tag.Name + (tag.Loop ? "" : " (1회)"), r.Deflate(new Thickness(2, 0)),
                        IsBright(color) ? Brushes.Black : Brushes.White, small: true);
                }
                for (int ck = 0; ck < _colT.SlotCount; ck++)
                {
                    var col = _colT.Get(ck);
                    int x = left + col.Pos;
                    if (col.Px <= 0 || x + col.Px < headerClip.Left || x > headerClip.Right) continue;
                    int f = FrameOf(p, col);
                    if (col.Px < CellW)
                    {
                        using (g.PushClip(new Rect(x, TagH, col.Px, HeadH))) DrawHeader(x + col.Px - CellW, f);
                    }
                    else DrawHeader(x, f);
                }
                {
                    var r = new Rect(FrameX(frames) + 1, TagH + 1, CellW - 2, HeadH - 2);
                    g.FillRectangle(GhostBrush, r);
                    bool hovered = _hover.Layer == -1 && _hover.Frame == frames;
                    DrawSplit(g, r, true, hovered ? _hover.Left : null, dim: !hovered);
                }
                // 태그는 머리글 칸까지 내려오는 괄호로 묶어 보인다.
                foreach (var tag in p.Tags)
                {
                    var pen = AppTheme.Pen(tag.Color.ToColor(), 2);
                    var (tf, tt) = TagRange(tag);
                    double x0 = FrameX(tf) + 1.5, x1 = FrameX(tt) + FrameW(tt) - 1.5, top0 = TagH - 2, bottom = TagH + HeadH - 1;
                    g.DrawLine(pen, new Avalonia.Point(x0, top0), new Avalonia.Point(x0, bottom));
                    g.DrawLine(pen, new Avalonia.Point(x1, top0), new Avalonia.Point(x1, bottom));
                    g.DrawLine(pen, new Avalonia.Point(x0, bottom), new Avalonia.Point(x0 + 5, bottom));
                    g.DrawLine(pen, new Avalonia.Point(x1 - 5, bottom), new Avalonia.Point(x1, bottom));
                }
            }
            if (_dropAt >= 0)
            {
                double x = FrameX(_dropAt);
                using (g.PushClip(new Rect(NameW, 0, Math.Max(0, width - NameW), height)))
                    g.DrawLine(AccentPen, new Avalonia.Point(x, 0), new Avalonia.Point(x, height));
            }

            // 레이어 이름 한 줄(지워져 줄어드는 유령 행은 흐리게)
            void DrawName(int y, int l, Layer layer, double open)
            {
                bool shown = l >= 0 && p.IsLayerVisible(l);
                var r = new Rect(0, y, NameW, CellH);
                g.FillRectangle(l == curLayer ? AppTheme.HoverBrush : AppTheme.PanelBrush, r);
                g.DrawLine(BorderPen, new Avalonia.Point(0, y + CellH - 0.5), new Avalonia.Point(NameW, y + CellH - 0.5));
                VectorIcons.Draw(g, layer.Visible ? VectorIcon.Eye : VectorIcon.EyeOff, new Rect(2, y, 22, CellH), 15,
                    shown ? AppTheme.TextBrush : AppTheme.DimTextBrush);
                VectorIcons.Draw(g, layer.Locked ? VectorIcon.Lock : VectorIcon.LockOpen, new Rect(24, y, 22, CellH), 13,
                    layer.Locked ? AppTheme.WarningBrush : AppTheme.BorderBrush);
                if (layer.IsGroup)
                    DrawDisclosure(g, (ToggleX + NameX) / 2.0, y + CellH / 2.0, open);
                else if (layer.IsPart)
                {
                    // 그룹에서 내려오는 안내선
                    double gx = ToggleX + (NameX - ToggleX) / 2.0 + 0.5;
                    g.DrawLine(BorderPen, new Avalonia.Point(gx, y), new Avalonia.Point(gx, y + CellH));
                    g.DrawLine(BorderPen, new Avalonia.Point(gx, y + CellH / 2.0), new Avalonia.Point(NameX + Indent - 4, y + CellH / 2.0));
                }
                int nameX = NameLeft(layer), right = NameW - 2;
                // 스킨 칩은 그룹·부위 모두 이름 칸 오른쪽 끝(같은 열), 부위 크기 글자는 그 왼쪽.
                if (SkinChip(layer, right - 2) is { W: > 0 } chip)
                {
                    DrawSkinChip(g, layer, new Rect(chip.X, y + 9, chip.W, CellH - 18));
                    right = chip.X - 4;
                }
                if (layer.IsPart)
                {
                    var (size, sw) = PartSizeText(layer);
                    right -= sw;
                    Text(g, size, new Rect(right, y, sw, CellH), AppTheme.DimTextBrush, small: true);
                }
                var name = LayerTitle(layer);
                Text(g, name, new Rect(nameX, y, Math.Max(1, right - nameX), CellH), shown ? AppTheme.TextBrush : AppTheme.DimTextBrush,
                    bold: l == curLayer || layer.IsGroup);
                if (l >= 0 && _layerDropAt == l) g.DrawRectangle(null, AccentPen, new Rect(2, y + 2, NameW - 4, CellH - 4));
            }

            // 레이어 이름 열
            g.FillRectangle(AppTheme.PanelBrush, new Rect(0, 0, NameW, TagH + HeadH));
            Text(g, "레이어 \\ 프레임", new Rect(4, TagH, NameW - 8, HeadH), AppTheme.DimTextBrush, small: true);
            using (g.PushClip(namesClip))
            {
                for (int rk = 0; rk < _rowT.SlotCount; rk++)
                {
                    var row = _rowT.Get(rk);
                    int y = top + row.Pos;
                    if (row.Px <= 0 || y + row.Px < namesClip.Top || y > namesClip.Bottom) continue;
                    int l = LayerOf(p, row);
                    var layer = (Layer)row.Item;
                    if (row.Px < CellH)
                    {
                        using (g.PushClip(new Rect(0, y, NameW, row.Px))) DrawName(y + row.Px - CellH, l, layer, row.Open);
                    }
                    else DrawName(y, l, layer, row.Open);
                }
            }
            g.DrawLine(BorderPen, new Avalonia.Point(NameW - 0.5, 0), new Avalonia.Point(NameW - 0.5, height));
            g.DrawLine(BorderPen, new Avalonia.Point(0, TagH + HeadH - 0.5), new Avalonia.Point(width, TagH + HeadH - 0.5));
        }

        /// <summary>스크롤 막대·이름 상자에서 온 이벤트는 격자가 처리하지 않는다.</summary>
        private bool FromChild(RoutedEventArgs e) => !ReferenceEquals(e.Source, this);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (FromChild(e)) return;
            EndRename(true);
            Focus();
            if (Ctx.Project is not { } p) return;
            e.Handled = true;
            var pt = e.GetCurrentPoint(this);
            var pos = pt.Position;
            bool left = pt.Properties.IsLeftButtonPressed, right = pt.Properties.IsRightButtonPressed;
            _press = pos;
            if (left && e.ClickCount == 1 && TagEdgeAt(pos) is { } edge)
            {
                _tagEdit = edge.Tag;
                _tagEditRight = edge.Right;
                (_tagFrom, _tagTo) = (edge.Tag.From, edge.Tag.To);
                Ctx.SelectedTag = edge.Tag;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }
            if (TagAt(pos) is { } tag)
            {
                Ctx.SelectedTag = tag;
                if (right) { _owner.ShowTagMenu(tag); return; }
                if (e.ClickCount == 2) { _owner.EditTag(tag); return; }
                _rangeA = tag.From;
                _rangeB = tag.To;
                Ctx.SetCurrentFrame(tag.From);
                InvalidateVisual();
                return;
            }
            int f = FrameAt(pos.X), l = LayerAt(pos.Y);
            bool inHeader = pos.Y >= TagH && pos.Y < TagH + HeadH;
            if (pos.X < NameW && l >= 0)
            {
                // 레이어 이름 열: 눈/자물쇠/접기/이름
                var nameLayer = p.Layers[l];
                if (left && pos.X < 24) { Ctx.Structural(() => p.SetLayerProperties(l, visible: !nameLayer.Visible)); return; }
                if (left && pos.X < ToggleX) { Ctx.Structural(() => p.SetLayerProperties(l, locked: !nameLayer.Locked)); return; }
                if (left && nameLayer.IsGroup && pos.X < NameX) { _owner.ToggleCollapsed(l); return; }
                if (left && SkinChip(nameLayer) is { W: > 0 } chip && pos.X >= chip.X && pos.X < chip.X + chip.W)
                {
                    // 스킨 칩: 이 그룹/부위로 스킨 관리 창 열기(우클릭은 레이어 메뉴의 "스킨"·"이 부위 스킨")
                    Ctx.SetCurrentLayer(l);
                    Dispatcher.UIThread.Post(() => _owner.OpenSkinManager(nameLayer));
                    return;
                }
                Ctx.SetCurrentLayer(l);
                if (right) { _owner.ShowLayerMenu(l); return; }
                if (e.ClickCount == 2) { BeginRename(l); return; }
                _layerDragFrom = l;
                e.Pointer.Capture(this);
                return;
            }
            if (pos.X < NameW)
            {
                // 레이어 이름 열의 빈 곳·왼쪽 위 칸
                if (right) _owner.ShowEmptyMenu();
                return;
            }
            if (IsGhostColumn(pos.X) && pos.Y >= TagH)
            {
                if (left) _owner.AppendFrame(LeftHalf(pos.X, Ctx.FrameCount), l >= 0 ? l : Ctx.CurrentLayer);
                else if (right) _owner.ShowEmptyMenu();
                return;
            }
            if (f < 0)
            {
                if (right) _owner.ShowEmptyMenu();
                return;
            }
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            int layerIndex = l >= 0 ? l : Ctx.CurrentLayer;
            if (right)
            {
                // 고른 묶음 안을 우클릭하면 선택을 그대로 두고 메뉴만 연다.
                bool inSelection = inHeader
                    ? InRange(f) && _headerRange
                    : !_headerRange && (_rangeA >= 0 ? InRange(f) : f == Ctx.CurrentFrame) && l >= 0 && SelectedRowLayers().Contains(l);
                if (!inSelection)
                {
                    ResetRange();
                    Ctx.SetCurrent(layerIndex, f);
                }
                _owner.ShowFrameMenu(f, inHeader ? -1 : l);
                return;
            }
            if (!left) return;
            if (shift)
            {
                // 시작 칸은 그대로 두고 끝 칸(행·프레임)만 넓힌다.
                if (_rangeA < 0) _rangeA = Ctx.CurrentFrame;
                _rangeB = f;
                _headerRange = inHeader;
                if (!inHeader && l >= 0) _rowEnd = l == Ctx.CurrentLayer ? null : p.Layers[l];
                InvalidateVisual();
                return;
            }
            if (inHeader)
            {
                if (e.ClickCount == 2) { _owner.SetDuration(); return; }
                bool selected = InRange(f) ? _headerRange : _rangeA < 0 && f == Ctx.CurrentFrame;
                Ctx.SetCurrentFrame(f);
                if (selected && !(InRange(f) && Math.Abs(_rangeB - _rangeA) > 0))
                {
                    // 이미 고른 한 프레임: 끌어서 순서 바꾸기
                    _dragFrom = f;
                }
                else
                {
                    _rangeA = _rangeB = f;
                    _headerRange = true;
                    _selectFrom = f;
                }
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }
            if (l < 0) return;
            var layer = p.Layers[l];
            var selRowsNow = SelectedRowLayers();
            bool inFrames = _rangeA >= 0 ? InRange(f) : f == Ctx.CurrentFrame;
            // 여러 칸을 골라 둔 경우에만 끌어서 옮긴다(한 칸에서 끌면 범위 고르기).
            bool multi = (_rangeA >= 0 && _rangeA != _rangeB) || _rowEnd is not null;
            if (!_headerRange && multi && inFrames && selRowsNow.Contains(l) && BlockHasContent(p, selRowsNow))
            {
                // 고른 셀 묶음을 끌어 옮기기(⌥/Alt 또는 Ctrl/⌘ = 복사)
                var (a, b) = _rangeA >= 0 ? Range : (Ctx.CurrentFrame, Ctx.CurrentFrame);
                var rowsNow = Rows();
                _blockRows = selRowsNow;
                _blockFrom = a;
                _blockCount = b - a + 1;
                _grabRow = rowsNow.IndexOf(l) - rowsNow.IndexOf(selRowsNow[0]);
                _grabFrame = f - a;
                _blockDrop = (-1, -1);
                _blockPress = (l, f);
                e.Pointer.Capture(this);
                return;
            }
            ResetRange();
            Ctx.SetCurrent(l, f);
            _selectFrom = f;
            e.Pointer.Capture(this);
            InvalidateVisual();
        }

        /// <summary>고른 셀 묶음에 그림이 하나라도 있는지(그룹 행은 그 부위들).</summary>
        private bool BlockHasContent(SpriteProject p, List<int> rowLayers)
        {
            var (a, b) = _rangeA >= 0 ? Range : (Ctx.CurrentFrame, Ctx.CurrentFrame);
            foreach (var l in rowLayers)
            {
                var layers = p.Layers[l] is { IsGroup: true } g ? p.ChildrenOf(g).Select(p.IndexOf) : [l];
                foreach (var li in layers)
                    for (int f = a; f <= b; f++)
                        if (!p.IsCelEmpty(li, f)) return true;
            }
            return false;
        }

        /// <summary>
        /// 레이어 행 끌기의 놓을 자리: 부위는 같은 그룹의 다른 부위, 일반 레이어·그룹은 다른 맨 위 단계 레이어
        /// (부위 행 위면 그 그룹). 놓을 수 없으면 -1.
        /// </summary>
        private int LayerDropTarget(int from, int over)
        {
            if (Ctx.Project is not { } p || from < 0 || over < 0 || from >= p.Layers.Count) return -1;
            var src = p.Layers[from];
            var dst = p.Layers[over];
            if (src.Parent is { } group) return ReferenceEquals(dst.Parent, group) && over != from ? over : -1;
            if (dst.Parent is { } owner) dst = owner;
            int target = p.IndexOf(dst);
            return target == from ? -1 : target;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pos = e.GetPosition(this);
            if (_tagEdit is not null)
            {
                int f = FrameAtClamped(pos.X);
                if (_tagEditRight) _tagTo = Math.Max(_tagFrom, f);
                else _tagFrom = Math.Min(_tagTo, f);
                Cursor = ResizeCursor;
                InvalidateVisual();
                return;
            }
            if (_selectFrom >= 0)
            {
                int f2 = FrameAtClamped(pos.X);
                Layer? end = null;
                if (!_headerRange && Ctx.Project is { } proj)
                {
                    var rows = Rows();
                    int l2 = rows.Count > 0 ? rows[RowAtClamped(pos.Y)] : Ctx.CurrentLayer;
                    end = l2 == Ctx.CurrentLayer ? null : proj.Layers[l2];
                }
                bool single = f2 == _selectFrom && end is null;
                if (single && _rangeA < 0) return;
                if (f2 == _rangeB && _rangeA == _selectFrom && ReferenceEquals(end, _rowEnd)) return;
                _rangeA = _selectFrom;
                _rangeB = f2;
                _rowEnd = end;
                InvalidateVisual();
                return;
            }
            if (_blockRows is not null)
            {
                if (Math.Abs(pos.X - _press.X) + Math.Abs(pos.Y - _press.Y) < 6) return;
                int rowCount = Rows().Count;
                int top = Math.Clamp(RowAtClamped(pos.Y) - _grabRow, 0, Math.Max(0, rowCount - _blockRows.Count));
                int start = Math.Max(0, _colT.IndexAt(ContentX(pos.X), exact: false) - _grabFrame);
                if ((top, start) != _blockDrop) { _blockDrop = (top, start); InvalidateVisual(); }
                Cursor = new Cursor(IsCopyModifier(e.KeyModifiers) ? StandardCursorType.DragCopy : StandardCursorType.DragMove);
                return;
            }
            if (_dragFrom < 0 && _layerDragFrom < 0)
            {
                UpdateHover(pos);
                var edge = TagEdgeAt(pos);
                Cursor = edge is null ? Cursor.Default : ResizeCursor;
                if (!ReferenceEquals(edge?.Tag, _tagHover)) { _tagHover = edge?.Tag; InvalidateVisual(); }
                return;
            }
            if (Math.Abs(pos.X - _press.X) + Math.Abs(pos.Y - _press.Y) < 6) return;
            if (_dragFrom >= 0)
            {
                int drop = Math.Clamp(_colT.BoundaryAt(ContentX(pos.X)), 0, Ctx.FrameCount);
                if (drop != _dropAt) { _dropAt = drop; InvalidateVisual(); }
                Cursor = new Cursor(StandardCursorType.SizeWestEast);
            }
            else
            {
                int target = LayerDropTarget(_layerDragFrom, LayerAt(pos.Y));
                if (target != _layerDropAt) { _layerDropAt = target; InvalidateVisual(); }
                Cursor = new Cursor(target >= 0 ? StandardCursorType.SizeNorthSouth : StandardCursorType.No);
            }
        }

        private static bool IsCopyModifier(KeyModifiers m) => (m & KeyModifiers.Alt) != 0 || Ui.HasCommand(m) || (m & KeyModifiers.Control) != 0;

        /// <summary>마우스가 올라간 "다음 프레임" 칸(반쪽 아이콘 강조용).</summary>
        private void UpdateHover(Avalonia.Point pos)
        {
            (int, int, bool) hover = (-1, -1, false);
            if (Ctx.Project is not null && pos.X >= NameW && pos.Y >= TagH)
            {
                bool ghost = IsGhostColumn(pos.X);
                int f = ghost ? Ctx.FrameCount : FrameAt(pos.X);
                bool header = pos.Y < TagH + HeadH;
                int l = header ? -1 : LayerAt(pos.Y);
                if (ghost) hover = (l, f, LeftHalf(pos.X, f));
            }
            if (hover == _hover) return;
            _hover = hover;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_tagHover is not null) { _tagHover = null; InvalidateVisual(); }
            if (_hover.Frame < 0) return;
            _hover = (-1, -1, false);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_tagEdit is { } edited)
            {
                var (from, to) = (_tagFrom, _tagTo);
                _tagEdit = null;
                Cursor = Cursor.Default;
                e.Pointer.Capture(null);
                if (from != edited.From || to != edited.To) _owner.ResizeTag(edited, from, to);
                InvalidateVisual();
                return;
            }
            if (_selectFrom >= 0)
            {
                _selectFrom = -1;
                if (_rangeA == _rangeB && !_headerRange && _rowEnd is null) ResetRange();
                e.Pointer.Capture(null);
                return;
            }
            if (_blockRows is { } blockRows)
            {
                var (from, count, drop) = (_blockFrom, _blockCount, _blockDrop);
                _blockRows = null;
                _blockDrop = (-1, -1);
                Cursor = Cursor.Default;
                int sourceTop = Rows().IndexOf(blockRows[0]);
                if (drop.Row < 0)
                {
                    // 끌지 않고 놓음 = 그 칸 클릭(묶음 선택을 풀고 그 칸만 고름)
                    var (pl, pf) = _blockPress;
                    ResetRange();
                    if (pl >= 0) Ctx.SetCurrent(pl, pf);
                }
                else if (drop.Row != sourceTop || drop.Frame != from)
                    _owner.MoveBlock(blockRows, from, count, drop.Row, drop.Frame, IsCopyModifier(e.KeyModifiers));
                e.Pointer.Capture(null);
                InvalidateVisual();
                return;
            }
            if (_dragFrom < 0 && _layerDragFrom < 0) return;
            // 먼저 적용한다: 캡처를 풀면 OnPointerCaptureLost가 끌기 상태를 취소한다.
            FinishDrag(true);
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _selectFrom = -1;
            if (_tagEdit is not null)
            {
                _tagEdit = null;
                Cursor = Cursor.Default;
                InvalidateVisual();
            }
            if (_blockRows is not null)
            {
                _blockRows = null;
                _blockDrop = (-1, -1);
                Cursor = Cursor.Default;
                InvalidateVisual();
            }
            if (_dragFrom >= 0 || _layerDragFrom >= 0) FinishDrag(false);
        }

        private void FinishDrag(bool apply)
        {
            int dragFrom = _dragFrom, dropAt = _dropAt, layerFrom = _layerDragFrom, layerTo = _layerDropAt;
            _dragFrom = _dropAt = _layerDragFrom = _layerDropAt = -1;
            Cursor = Cursor.Default;
            InvalidateVisual();
            if (!apply) return;
            if (dragFrom >= 0 && dropAt >= 0)
            {
                int to = dropAt > dragFrom ? dropAt - 1 : dropAt;
                if (to != dragFrom) _owner.MoveFrameTo(dragFrom, to);
            }
            if (layerFrom >= 0 && layerTo >= 0 && layerTo != layerFrom) _owner.MoveLayerTo(layerFrom, layerTo);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (FromChild(e) && e.Source is TextBox) return;
            // 기본 휠은 위아래(레이어), Ctrl(또는 Shift)+휠이나 트랙패드 가로 스크롤은 좌우(프레임).
            bool sideways = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0 || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y);
            var bar = sideways ? _h : _v;
            e.Handled = true;
            if (!bar.IsEnabled) return;
            double delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
            if (delta == 0) return;
            bar.Value = Math.Clamp(bar.Value - Math.Sign(delta) * bar.SmallChange, 0, bar.Maximum);
            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || e.Source is TextBox) return;
            // macOS 키보드의 "delete"는 Back 키다.
            if (e.Key == Key.Delete || (Platform.IsMac && e.Key == Key.Back))
            {
                // 고른 프레임(범위)을 지운다. 셀 그림만 지우기는 우클릭 "셀 지우기".
                _owner.DeleteFrame();
                e.Handled = true;
            }
            else if (e.Key == Key.F2) { _owner.BeginRenameLayer(); e.Handled = true; }
        }
    }
}

/// <summary>태그(애니메이션) 만들기/고치기.</summary>
public sealed class TagDialog : DialogBase
{
    private readonly ComboBox _name;
    private readonly NumericUpDown _from, _to;
    private readonly ComboBox _direction;
    private readonly CheckBox _loop;
    private readonly ColorField _color;

    public TagDialog(SpriteProject project, AnimationTag? tag, int from, int to) : base(tag is null ? "새 태그(애니메이션)" : "태그 속성")
    {
        int n = project.Frames.Count;
        _name = new ComboBox { IsEditable = true, Width = 200, Margin = new Thickness(3) };
        foreach (var s in TimelinePanel.SuggestedNames.Where(s => !project.HasTag(s) || s == tag?.Name)) _name.Items.Add(s);
        _name.Text = tag?.Name ?? TimelinePanel.SuggestedNames.FirstOrDefault(s => !project.HasTag(s)) ?? project.UniqueTagName("anim");
        _from = Ui.Num(1, n, from + 1, 60);
        _to = Ui.Num(1, n, to + 1, 60);
        _direction = Ui.Combo(110, "정방향", "역방향", "왕복");
        _direction.SelectedIndex = (int)(tag?.Direction ?? TagDirection.Forward);
        _loop = Ui.Check("반복", tag?.Loop ?? true);
        _color = new ColorField(tag?.Color ?? AnimationTag.DefaultColors[project.Tags.Count % AnimationTag.DefaultColors.Count]);
        Body.Children.Add(Ui.Label("이름 (테스트 규칙: idle_down, walk_left, run, jump, fall …)"));
        Body.Children.Add(_name);
        Body.Children.Add(Ui.Row(Ui.Label("프레임"), _from, Ui.Label("~"), _to));
        Body.Children.Add(Ui.Row(Ui.Label("방향"), _direction, _loop));
        Body.Children.Add(Ui.Row(Ui.Label("색"), _color));
        OkButton.Click += (_, _) =>
        {
            if (TagName.Length == 0) { Ui.Error(this, "이름을 입력하세요."); DialogResult = DialogResult.None; return; }
            if (project.Tags.Any(t => t.Name == TagName && !ReferenceEquals(t, tag)))
            {
                Ui.Error(this, $"\"{TagName}\" 태그가 이미 있습니다.");
                DialogResult = DialogResult.None;
                return;
            }
            if (_to.NumValue() < _from.NumValue()) { Ui.Error(this, "끝 프레임이 시작보다 앞입니다."); DialogResult = DialogResult.None; }
        };
    }

    public string TagName => (_name.Text ?? "").Trim();
    public int From => (int)_from.NumValue() - 1;
    public int To => (int)_to.NumValue() - 1;
    public TagDirection Direction => (TagDirection)_direction.SelectedIndex;
    public bool Loop => _loop.On();
    public Color32 TagColor => _color.Color;
}
