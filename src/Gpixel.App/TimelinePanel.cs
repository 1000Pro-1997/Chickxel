using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// Aseprite식 타임라인: 행 = 레이어(위가 맨 위 레이어), 열 = 프레임, 위쪽 띠 = 태그(애니메이션).
/// 셀 썸네일은 실시간으로 갱신되고, 재생·한 프레임 이동·프레임/레이어/태그 편집·양파 껍질을 여기서 한다.
/// <para>
/// 아틀라스 프로젝트에서는 열 = 스프라이트(머리글에 이름·크기)인 스프라이트 목록이 된다: 고르기, 머리글 끌어서 순서 바꾸기,
/// 추가(크기 입력)·복제·삭제, 머리글 더블클릭으로 이름 바꾸기, 크기 바꾸기(기준점). 태그·재생·양파 껍질은 없다.
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
    private readonly Grid _grid;
    private readonly NumericUpDown _duration;
    private readonly CheckBox _onion;
    private readonly Button _play;
    private readonly Label _info, _header;
    private readonly ToolTip _tips = new();
    private readonly Button _first, _prev, _next, _last;
    private readonly Control[] _animationOnly, _atlasOnly;
    private bool _updating;

    public TimelinePanel(EditorContext ctx, PreviewPanel preview)
    {
        _ctx = ctx;
        _preview = preview;
        BackColor = Theme.PanelColor;

        _play = Ui.Button("▶", (_, _) => _preview.TogglePlay(), 30);
        _duration = Ui.Num(1, 10000, 100, 60);
        _duration.ValueChanged += (_, _) =>
        {
            if (_updating || _ctx.Project is not { } p || p.IsAtlas) return;
            int v = (int)_duration.Value;
            var frames = SelectedFrames();
            _ctx.Structural(() => p.Edit("프레임 길이", ProjectChange.Frames, () =>
            {
                foreach (var f in frames) p.SetFrameDuration(f, v);
            }));
        };
        _onion = Ui.Check("양파 껍질");
        _onion.CheckedChanged += (_, _) => { if (!_updating) _ctx.OnionSkin = _onion.Checked; };
        _info = Ui.Label("");
        _info.ForeColor = Theme.DimTextColor;

        // 이동 단추는 글자 폭에 맞춘다(고정 폭이면 "|◀"가 잘림).
        _first = Ui.Button("|◀", (_, _) => _ctx.SetCurrentColumn(0));
        _prev = Ui.Button("◀", (_, _) => StepFrame(-1));
        _next = Ui.Button("▶|", (_, _) => StepFrame(1));
        _last = Ui.Button("▶▶|", (_, _) => _ctx.SetCurrentColumn(_ctx.ColumnCount - 1));
        _animationOnly =
        [
            Tip(_play, "재생/정지 (Shift+Enter)"),
            Ui.Label(" 길이(ms)"), _duration,
            Tip(Ui.Button("＋빈 프레임", (_, _) => NewEmptyFrame()), "현재 프레임 뒤에 빈 프레임 (Alt+N)"),
            Tip(Ui.Button("프레임 복제", (_, _) => DuplicateFrame()), "현재 프레임을 그림까지 복사 (Alt+D)"),
            Tip(Ui.Button("삭제", (_, _) => DeleteFrame()), "현재 프레임 삭제"),
            Tip(Ui.Button("⇠", (_, _) => MoveFrame(-1), 26), "프레임을 앞으로"),
            Tip(Ui.Button("⇢", (_, _) => MoveFrame(1), 26), "프레임을 뒤로"),
        ];
        _atlasOnly =
        [
            Tip(Ui.Button("＋스프라이트", (_, _) => AddSprite()), "현재 스프라이트 뒤에 새 스프라이트(이름·크기 입력) (Alt+N)"),
            Tip(Ui.Button("복제", (_, _) => DuplicateSprite()), "현재 스프라이트를 그림까지 복사 (Alt+D)"),
            Tip(Ui.Button("삭제", (_, _) => DeleteSprite()), "현재 스프라이트 삭제 (Delete: 목록에 포커스)"),
            Tip(Ui.Button("이름", (_, _) => BeginRenameSprite()), "이름 바꾸기 (머리글 더블클릭)"),
            Tip(Ui.Button("크기…", (_, _) => ResizeSprite()), "스프라이트 크기 바꾸기(기준점)"),
            Tip(Ui.Button("⇠", (_, _) => MoveSprite(-1), 26), "스프라이트를 앞으로 (머리글을 끌어도 됨)"),
            Tip(Ui.Button("⇢", (_, _) => MoveSprite(1), 26), "스프라이트를 뒤로"),
        ];
        var tagButton = Tip(Ui.Button("＋태그", (_, _) => NewTag()), "고른 프레임 범위로 태그(애니메이션) 만들기 — Shift+클릭으로 범위 선택");
        var spacer = Ui.Label("  ");
        _animationOnly = [.. _animationOnly, spacer, tagButton, _onion];

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.PanelColor, WrapContents = true, Padding = new Padding(2, 0, 2, 0) };
        bar.Controls.AddRange([
            Tip(_first, "첫 프레임/스프라이트 (Home)"),
            Tip(_prev, "이전 프레임/스프라이트 (,)"),
            _animationOnly[0],
            Tip(_next, "다음 프레임/스프라이트 (.)"),
            Tip(_last, "마지막 프레임/스프라이트 (End)"),
            .. _animationOnly[1..^3],
            .. _atlasOnly,
            Ui.Label("  레이어"),
            Tip(Ui.Button("＋", (_, _) => AddLayer(), 26), "새 레이어 (Shift+N)"),
            Tip(Ui.Button("복제", (_, _) => DuplicateLayer()), "레이어 복제"),
            Tip(Ui.Button("삭제", (_, _) => DeleteLayer()), "레이어 삭제"),
            Tip(Ui.Button("▲", (_, _) => MoveLayer(1), 26), "레이어 위로"),
            Tip(Ui.Button("▼", (_, _) => MoveLayer(-1), 26), "레이어 아래로"),
            Tip(Ui.Button("병합", (_, _) => MergeDown()), "아래 레이어와 병합"),
            .. _animationOnly[^3..],
            _info,
        ]);

        _grid = new Grid(this) { Dock = DockStyle.Fill };
        _header = Ui.Header("타임라인");
        Controls.Add(_grid);
        Controls.Add(bar);
        Controls.Add(_header);

        ctx.ProjectReplaced += (_, _) => { _grid.EndRename(false); _grid.ResetRange(); RefreshUi(); };
        ctx.ProjectChanged += (_, _) => RefreshUi();
        ctx.CurrentChanged += (_, _) => { _grid.ClampRange(); RefreshUi(); _grid.EnsureCurrentVisible(); };
        ctx.ViewChanged += (_, _) => _grid.Invalidate();
        ctx.OnionSkinChanged += (_, _) => { _updating = true; _onion.Checked = _ctx.OnionSkin; _updating = false; };
        preview.PlayingFrameChanged += (_, f) =>
        {
            _play.Text = _preview.IsPlaying ? "⏸" : "▶";
            _grid.PlayingFrame = f;
        };
        RefreshUi();
    }

    private Control Tip(Control c, string text)
    {
        _tips.SetToolTip(c, text);
        return c;
    }

    private void RefreshUi()
    {
        _updating = true;
        try
        {
            Enabled = _ctx.HasProject;
            bool atlas = _ctx.IsAtlas;
            foreach (var c in _animationOnly) c.Visible = !atlas;
            foreach (var c in _atlasOnly) c.Visible = atlas;
            _header.Text = atlas ? "스프라이트 (아틀라스) — 머리글: 클릭 고르기 · 끌기 순서 · 더블클릭 이름 · 우클릭 메뉴" : "타임라인";
            _onion.Checked = _ctx.OnionSkin;
            if (_ctx.Project is { } p)
            {
                if (atlas && _ctx.CurrentAtlasSprite is { } s)
                {
                    _info.Text = $"  스프라이트 {_ctx.CurrentSprite + 1}/{p.Sprites.Count} \"{s.Name}\" {s.Width}×{s.Height}"
                                 + $" · 레이어 \"{p.Layers[_ctx.CurrentLayer].Name}\"";
                }
                else
                {
                    _duration.Value = Math.Clamp(_ctx.FrameDuration(_ctx.CurrentFrame), 1, 10000);
                    var tag = _ctx.ActiveTag;
                    int total = p.Frames.Sum(f => f.DurationMs);
                    _info.Text = $"  프레임 {_ctx.CurrentFrame + 1}/{p.Frames.Count} · 레이어 \"{p.Layers[_ctx.CurrentLayer].Name}\""
                                 + (tag is null ? "" : $" · 태그 {tag.Name}") + $" · 전체 {total}ms";
                }
            }
            else _info.Text = "";
            _grid.UpdateScrollSize();
            _grid.Invalidate();
        }
        finally
        {
            _updating = false;
        }
    }

    // ───────────── 동작 ─────────────

    private SpriteProject? P => _ctx.Project;

    /// <summary>범위 선택이 있으면 그 프레임들, 없으면 현재 프레임.</summary>
    private List<int> SelectedFrames()
    {
        var (a, b) = _grid.Range;
        return a < 0 ? [_ctx.CurrentFrame] : Enumerable.Range(a, b - a + 1).ToList();
    }

    /// <summary>이전/다음 열(애니메이션 = 프레임, 아틀라스 = 스프라이트)로, 끝에서는 반대쪽으로 돈다.</summary>
    public void StepFrame(int delta)
    {
        if (P is null) return;
        _preview.Stop();
        int n = _ctx.ColumnCount;
        if (n == 0) return;
        _ctx.SetCurrentColumn(((_ctx.CurrentColumn + delta) % n + n) % n);
    }

    public void NewEmptyFrame()
    {
        if (P is not { IsAtlas: false } p) return;
        int at = _ctx.CurrentFrame + 1;
        _ctx.Structural(() => p.AddFrame(at));
        _ctx.SetCurrentFrame(at);
    }

    public void DuplicateFrame()
    {
        if (P is not { IsAtlas: false } p) return;
        int at = _ctx.CurrentFrame;
        _ctx.Structural(() => p.DuplicateFrame(at));
        _ctx.SetCurrentFrame(at + 1);
    }

    public void DeleteFrame()
    {
        if (P is not { IsAtlas: false } p) return;
        if (p.Frames.Count <= 1)
        {
            Ui.Info(FindForm(), "마지막 프레임은 지울 수 없습니다.");
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

    public void MoveFrame(int delta) => MoveColumnTo(_ctx.CurrentFrame, _ctx.CurrentFrame + delta);

    /// <summary>열(프레임/스프라이트) 순서 바꾸기.</summary>
    private void MoveColumnTo(int from, int to)
    {
        if (P is not { } p) return;
        to = Math.Clamp(to, 0, _ctx.ColumnCount - 1);
        if (from == to) return;
        if (p.IsAtlas)
        {
            _ctx.Structural(() => p.MoveSprite(from, to));
            _ctx.SetCurrentSprite(_ctx.CurrentLayer, to);
        }
        else
        {
            _ctx.Structural(() => p.MoveFrame(from, to));
            _ctx.SetCurrentFrame(to);
        }
    }

    /// <summary>현재 열(프레임 또는 스프라이트) 삭제.</summary>
    private void DeleteColumn()
    {
        if (_ctx.IsAtlas) DeleteSprite();
        else DeleteFrame();
    }

    // ───────────── 아틀라스 스프라이트 ─────────────

    /// <summary>현재 스프라이트 뒤에 새 스프라이트(이름·크기 입력).</summary>
    public void AddSprite()
    {
        if (P is not { IsAtlas: true } p) return;
        var cur = _ctx.CurrentAtlasSprite;
        using var dlg = new NewSpriteDialog(p.UniqueSpriteName("sprite"), cur?.Width ?? 16, cur?.Height ?? 16);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        int at = _ctx.CurrentSprite + 1;
        _ctx.Structural(() => p.AddSprite(dlg.SpriteName, dlg.SpriteWidth, dlg.SpriteHeight, at));
        _ctx.SetCurrentSprite(_ctx.CurrentLayer, at);
    }

    public void DuplicateSprite()
    {
        if (P is not { IsAtlas: true } p) return;
        int at = _ctx.CurrentSprite;
        _ctx.Structural(() => p.DuplicateSprite(at));
        _ctx.SetCurrentSprite(_ctx.CurrentLayer, at + 1);
    }

    public void DeleteSprite()
    {
        if (P is not { IsAtlas: true } p) return;
        if (p.Sprites.Count <= 1)
        {
            Ui.Info(FindForm(), "마지막 스프라이트는 지울 수 없습니다.");
            return;
        }
        int at = _ctx.CurrentSprite;
        _ctx.Structural(() => p.DeleteSprite(at));
    }

    public void MoveSprite(int delta) => MoveColumnTo(_ctx.CurrentSprite, _ctx.CurrentSprite + delta);

    /// <summary>현재 스프라이트 이름을 목록 머리글에서 바로 고친다.</summary>
    public void BeginRenameSprite()
    {
        if (P is not { IsAtlas: true }) return;
        _grid.EnsureCurrentVisible();
        _grid.BeginRename(_ctx.CurrentSprite);
    }

    private void RenameSprite(int index, string name)
    {
        if (P is not { IsAtlas: true } p || index < 0 || index >= p.Sprites.Count) return;
        if (name.Trim() == p.Sprites[index].Name) return;
        try
        {
            _ctx.Structural(() => p.RenameSprite(index, name));
        }
        catch (ArgumentException ex)
        {
            Ui.Error(FindForm(), ex.Message);
        }
    }

    /// <summary>현재 스프라이트 크기 바꾸기(기준점, 피벗은 같은 그림 지점 유지).</summary>
    public void ResizeSprite()
    {
        if (P is not { IsAtlas: true } p || _ctx.CurrentAtlasSprite is not { } s) return;
        using var dlg = new ResizeDialog(s.Width, s.Height, $"스프라이트 크기 — {s.Name}");
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        int at = _ctx.CurrentSprite;
        _ctx.Structural(() => p.ResizeSprite(at, dlg.NewWidth, dlg.NewHeight, dlg.ImageAnchor));
    }

    // ───────────── 레이어 ─────────────

    public void AddLayer()
    {
        if (P is not { } p) return;
        int at = _ctx.CurrentLayer + 1;
        _ctx.Structural(() => p.AddLayer(at));
        _ctx.SetCurrentLayer(at);
    }

    private void DuplicateLayer()
    {
        if (P is not { } p) return;
        int at = _ctx.CurrentLayer;
        _ctx.Structural(() => p.DuplicateLayer(at));
        _ctx.SetCurrentLayer(at + 1);
    }

    private void DeleteLayer()
    {
        if (P is not { } p) return;
        if (p.Layers.Count <= 1)
        {
            Ui.Info(FindForm(), "마지막 레이어는 지울 수 없습니다.");
            return;
        }
        int at = _ctx.CurrentLayer;
        _ctx.Structural(() => p.DeleteLayer(at));
    }

    private void MoveLayer(int delta)
    {
        if (P is not { } p) return;
        int from = _ctx.CurrentLayer, to = Math.Clamp(from + delta, 0, p.Layers.Count - 1);
        if (from == to) return;
        _ctx.Structural(() => p.MoveLayer(from, to));
        _ctx.SetCurrentLayer(to);
    }

    private void MergeDown()
    {
        if (P is not { } p) return;
        int at = _ctx.CurrentLayer;
        if (at == 0)
        {
            Ui.Info(FindForm(), "맨 아래 레이어는 아래로 병합할 수 없습니다.");
            return;
        }
        _ctx.Structural(() => p.MergeLayerDown(at));
        _ctx.SetCurrentLayer(at - 1);
    }

    private void RenameLayer(int index)
    {
        if (P is not { } p) return;
        var name = InputDialog.Ask(FindForm()!, "레이어 이름", "레이어 이름", p.Layers[index].Name);
        if (name is null || name == p.Layers[index].Name) return;
        _ctx.Structural(() => p.SetLayerProperties(index, name: p.UniqueLayerName(name)));
    }

    private void SetLayerOpacity(int index)
    {
        if (P is not { } p) return;
        var text = InputDialog.Ask(FindForm()!, "레이어 불투명도", "불투명도(0~255)", p.Layers[index].Opacity.ToString(), ["255", "192", "128", "64"]);
        if (text is null) return;
        if (!byte.TryParse(text, out var v))
        {
            Ui.Error(FindForm(), "0~255 사이 숫자를 입력하세요.");
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

    // ───────────── 태그 ─────────────

    /// <summary>고른 프레임 범위(없으면 현재 프레임)로 태그를 만든다.</summary>
    public void NewTag()
    {
        if (P is not { IsAtlas: false } p) return;
        var frames = SelectedFrames();
        int from = frames.Min(), to = frames.Max();
        using var dlg = new TagDialog(p, null, from, to);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
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
            Ui.Error(FindForm(), ex.Message);
        }
    }

    private void EditTag(AnimationTag tag)
    {
        if (P is not { } p) return;
        int index = p.Tags.ToList().IndexOf(tag);
        if (index < 0) return;
        using var dlg = new TagDialog(p, tag, tag.From, tag.To);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            _ctx.Structural(() => p.SetTag(index, dlg.TagName, dlg.From, dlg.To, dlg.Direction, dlg.Loop, dlg.TagColor));
        }
        catch (ArgumentException ex)
        {
            Ui.Error(FindForm(), ex.Message);
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

    private static ToolStripMenuItem Item(string text, Action action) => new(text, null, (_, _) => action());

    private void ShowMenu(Point at, params ToolStripItem[] items)
    {
        var menu = new ContextMenuStrip { Renderer = Theme.Renderer };
        menu.Items.AddRange(items);
        foreach (ToolStripItem it in menu.Items) it.ForeColor = Theme.TextColor;
        menu.Show(_grid, at);
    }

    private void ShowFrameMenu(Point at, int frame) => ShowMenu(at,
        Item("뒤에 빈 프레임", NewEmptyFrame),
        Item("프레임 복제", DuplicateFrame),
        Item("삭제", DeleteFrame),
        new ToolStripSeparator(),
        Item("고른 범위로 태그 만들기…", NewTag),
        Item("피벗을 기본값으로", () => { if (P is { } p) _ctx.Structural(() => p.SetFramePivot(frame, null)); }),
        Item("이 프레임만 피벗 따로 쓰기", () => { if (P is { } p) _ctx.Structural(() => p.SetFramePivot(frame, p.GetFramePivot(frame))); }),
        Item("내보낼 칸 이름…", () =>
        {
            if (P is not { } p) return;
            var name = InputDialog.Ask(FindForm()!, "칸 이름", "내보낼 칸 이름(비우면 규칙: {태그}_{번호})", p.Frames[frame].Name ?? "");
            if (name is not null) _ctx.Structural(() => p.SetFrameName(frame, name));
        }));

    private void ShowSpriteMenu(Point at) => ShowMenu(at,
        Item("새 스프라이트…", AddSprite),
        Item("복제", DuplicateSprite),
        Item("삭제", DeleteSprite),
        new ToolStripSeparator(),
        Item("이름 바꾸기", BeginRenameSprite),
        Item("크기 바꾸기…", ResizeSprite),
        Item("앞으로", () => MoveSprite(-1)),
        Item("뒤로", () => MoveSprite(1)));

    private void ShowLayerMenu(Point at, int layer) => ShowMenu(at,
        Item("이름 바꾸기…", () => RenameLayer(layer)),
        Item("불투명도…", () => SetLayerOpacity(layer)),
        Item("보이기/숨기기", () => ToggleLayer(layer, false)),
        Item("잠금/풀기", () => ToggleLayer(layer, true)),
        new ToolStripSeparator(),
        Item("새 레이어", AddLayer),
        Item("복제", DuplicateLayer),
        Item("아래와 병합", MergeDown),
        Item("삭제", DeleteLayer));

    private void ShowTagMenu(Point at, AnimationTag tag) => ShowMenu(at,
        Item("태그 속성…", () => EditTag(tag)),
        Item("이 태그 재생", () => { _ctx.SelectedTag = tag; _ctx.SetCurrentFrame(tag.From); _preview.Play(); }),
        Item("삭제", () => DeleteTag(tag)));

    // ───────────── 격자 ─────────────

    private sealed class Grid : Control
    {
        private const int NameW = 170, TagH = 20, HeadH = 30, CellH = 36;

        private readonly TimelinePanel _owner;
        private readonly HScrollBar _h = new() { Dock = DockStyle.Bottom };
        private readonly VScrollBar _v = new() { Dock = DockStyle.Right };
        private TextBox? _rename;
        private int _renameIndex = -1;
        private int _rangeA = -1, _rangeB = -1;
        private int _dragFrom = -1, _dropAt = -1, _layerDragFrom = -1, _layerDropAt = -1;
        private Point _press;
        private int _playing = -1;

        public Grid(TimelinePanel owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.InputColor;
            Controls.Add(_h);
            Controls.Add(_v);
            _h.Scroll += (_, _) => { EndRename(true); Invalidate(); };
            _v.Scroll += (_, _) => Invalidate();
            TabStop = true;
        }

        private EditorContext Ctx => _owner._ctx;

        /// <summary>열 폭: 아틀라스는 스프라이트 이름이 보이도록 넓게.</summary>
        private int CellW => Ctx.IsAtlas ? 78 : 42;

        public (int A, int B) Range => _rangeA < 0 ? (-1, -1) : (Math.Min(_rangeA, _rangeB), Math.Max(_rangeA, _rangeB));

        public int PlayingFrame
        {
            get => _playing;
            set { _playing = value; Invalidate(); }
        }

        public void ResetRange() { _rangeA = _rangeB = -1; Invalidate(); }

        public void ClampRange()
        {
            if (_rangeA >= Ctx.ColumnCount || _rangeB >= Ctx.ColumnCount) ResetRange();
        }

        private int ViewW => ClientSize.Width - _v.Width - NameW;
        private int ViewH => ClientSize.Height - _h.Height - TagH - HeadH;

        public void UpdateScrollSize()
        {
            int contentW = Ctx.ColumnCount * CellW, contentH = Ctx.LayerCount * CellH;
            _h.Maximum = Math.Max(0, contentW);
            _h.LargeChange = Math.Max(1, ViewW);
            _h.SmallChange = CellW;
            _h.Enabled = contentW > ViewW;
            if (!_h.Enabled) _h.Value = 0;
            _v.Maximum = Math.Max(0, contentH);
            _v.LargeChange = Math.Max(1, ViewH);
            _v.SmallChange = CellH;
            _v.Enabled = contentH > ViewH;
            if (!_v.Enabled) _v.Value = 0;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollSize();
        }

        public void EnsureCurrentVisible()
        {
            if (!_h.Enabled) return;
            int x = Ctx.CurrentColumn * CellW;
            if (x < _h.Value) _h.Value = x;
            else if (x + CellW > _h.Value + ViewW) _h.Value = Math.Clamp(x + CellW - ViewW, 0, Math.Max(0, _h.Maximum - _h.LargeChange + 1));
            Invalidate();
        }

        private int FrameX(int f) => NameW + f * CellW - _h.Value;
        /// <summary>레이어 행 위치(맨 위 레이어가 첫 행).</summary>
        private int LayerY(int l) => TagH + HeadH + (Ctx.LayerCount - 1 - l) * CellH - _v.Value;

        private int FrameAt(int x)
        {
            if (x < NameW) return -1;
            int f = (x - NameW + _h.Value) / CellW;
            return f >= 0 && f < Ctx.ColumnCount ? f : -1;
        }

        private int LayerAt(int y)
        {
            if (y < TagH + HeadH) return -1;
            int row = (y - TagH - HeadH + _v.Value) / CellH;
            int l = Ctx.LayerCount - 1 - row;
            return l >= 0 && l < Ctx.LayerCount ? l : -1;
        }

        private AnimationTag? TagAt(Point p)
        {
            if (Ctx.Project is not { IsAtlas: false } proj || p.Y >= TagH || p.X < NameW) return null;
            int f = FrameAt(p.X);
            return f < 0 ? null : proj.Tags.LastOrDefault(t => t.Contains(f));
        }

        // ── 스프라이트 이름 바로 고치기

        public void BeginRename(int index)
        {
            EndRename(true);
            if (Ctx.Project is not { IsAtlas: true } p || index < 0 || index >= p.Sprites.Count) return;
            _renameIndex = index;
            _rename = new TextBox
            {
                Text = p.Sprites[index].Name,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.BackColor,
                ForeColor = Theme.TextColor,
                Font = Theme.UiFont,
                Bounds = new Rectangle(FrameX(index) + 1, TagH + 1, CellW - 2, 18),
            };
            _rename.KeyDown += (_, e) =>
            {
                if (e.KeyCode is Keys.Enter or Keys.Escape)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    EndRename(e.KeyCode == Keys.Enter, refocus: true);
                }
            };
            _rename.LostFocus += (_, _) => BeginInvoke(() => EndRename(true));
            Controls.Add(_rename);
            _rename.Focus();
            _rename.SelectAll();
        }

        /// <summary>이름 고치기를 끝낸다(<paramref name="commit"/>면 적용, <paramref name="refocus"/>면 목록에 포커스).</summary>
        public void EndRename(bool commit, bool refocus = false)
        {
            if (_rename is not { } box) return;
            _rename = null;
            int index = _renameIndex;
            _renameIndex = -1;
            var text = box.Text;
            Controls.Remove(box);
            box.Dispose();
            if (commit) _owner.RenameSprite(index, text);
            if (refocus) Focus();
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
            => (keyData & Keys.KeyCode) is Keys.F2 || base.IsInputKey(keyData);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.InputColor);
            if (Ctx.Project is not { } p) return;
            bool atlas = p.IsAtlas;
            int cols = Ctx.ColumnCount;
            int cur = Ctx.CurrentColumn, curLayer = Ctx.CurrentLayer;
            var (ra, rb) = Range;
            int cw = CellW;
            var cellsClip = Rectangle.FromLTRB(NameW, TagH + HeadH, ClientSize.Width - _v.Width, ClientSize.Height - _h.Height);
            var headerClip = Rectangle.FromLTRB(NameW, 0, ClientSize.Width - _v.Width, TagH + HeadH);
            var namesClip = Rectangle.FromLTRB(0, TagH + HeadH, NameW, ClientSize.Height - _h.Height);

            using var border = new Pen(Theme.BorderColor);
            using var accent = new Pen(Theme.AccentColor, 2);
            using var panel = new SolidBrush(Theme.PanelColor);
            using var hover = new SolidBrush(Theme.HoverColor);
            using var rangeBrush = new SolidBrush(Color.FromArgb(60, Theme.AccentColor));

            // 셀
            g.SetClip(cellsClip);
            for (int l = 0; l < p.Layers.Count; l++)
            {
                int y = LayerY(l);
                if (y + CellH < cellsClip.Top || y > cellsClip.Bottom) continue;
                for (int f = 0; f < cols; f++)
                {
                    int x = FrameX(f);
                    if (x + cw < cellsClip.Left || x > cellsClip.Right) continue;
                    var r = new Rectangle(x + 1, y + 1, cw - 2, CellH - 2);
                    g.FillRectangle(f == cur ? hover : panel, r);
                    if (f >= ra && f <= rb && ra >= 0) g.FillRectangle(rangeBrush, r);
                    if (Ctx.CelBitmap(l, f) is { } bmp)
                        ImageUtil.DrawFitted(g, bmp, new Rectangle(0, 0, bmp.Width, bmp.Height), Rectangle.Inflate(r, -3, -3), checker: true);
                    else
                    {
                        using var dot = new SolidBrush(Theme.BorderColor);
                        g.FillEllipse(dot, r.X + r.Width / 2 - 3, r.Y + r.Height / 2 - 3, 6, 6);
                    }
                    if (l == curLayer && f == cur) g.DrawRectangle(accent, r);
                    else g.DrawRectangle(border, r);
                }
            }
            if (_playing >= 0 && !atlas)
            {
                int x = FrameX(_playing);
                using var playPen = new Pen(Color.FromArgb(120, 230, 120), 2);
                g.DrawLine(playPen, x + cw / 2, cellsClip.Top, x + cw / 2, cellsClip.Bottom);
            }
            g.ResetClip();

            // 태그 띠(아틀라스: 스프라이트 이름) + 열 머리글
            g.SetClip(headerClip);
            g.FillRectangle(panel, headerClip);
            if (!atlas)
            {
                foreach (var tag in p.Tags)
                {
                    int x0 = FrameX(tag.From), x1 = FrameX(tag.To) + cw;
                    var r = new Rectangle(x0 + 1, 2, x1 - x0 - 2, TagH - 4);
                    using var tb = new SolidBrush(tag.Color.ToColor());
                    g.FillRectangle(tb, r);
                    if (ReferenceEquals(tag, Ctx.ActiveTag)) g.DrawRectangle(Pens.White, r);
                    var arrow = tag.Direction switch { TagDirection.Reverse => "◀ ", TagDirection.PingPong => "↔ ", _ => "" };
                    TextRenderer.DrawText(g, arrow + tag.Name + (tag.Loop ? "" : " (1회)"), Theme.SmallFont, r,
                        tag.Color.ToColor().GetBrightness() > 0.6f ? Color.Black : Color.White,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
            for (int f = 0; f < cols; f++)
            {
                int x = FrameX(f);
                if (x + cw < headerClip.Left || x > headerClip.Right) continue;
                var r = new Rectangle(x, atlas ? 0 : TagH, cw, atlas ? TagH + HeadH : HeadH);
                if (f == cur) g.FillRectangle(hover, r);
                if (f >= ra && f <= rb && ra >= 0) g.FillRectangle(rangeBrush, r);
                if (atlas)
                {
                    var s = p.Sprites[f];
                    TextRenderer.DrawText(g, s.Name, Theme.BoldFont, new Rectangle(x + 2, 2, cw - 4, 18),
                        f == cur ? Theme.AccentColor : Theme.TextColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, $"{f + 1} · {s.Width}×{s.Height}", Theme.SmallFont, new Rectangle(x, 22, cw, 14),
                        Theme.DimTextColor, TextFormatFlags.HorizontalCenter);
                    if (!s.Border.IsNone)
                        TextRenderer.DrawText(g, "▦", Theme.SmallFont, new Point(x + 2, 22), Color.FromArgb(90, 220, 120));
                    g.DrawLine(border, x + cw - 1, 0, x + cw - 1, TagH + HeadH);
                    continue;
                }
                TextRenderer.DrawText(g, (f + 1).ToString(), Theme.BoldFont, new Rectangle(x, TagH, cw, 16),
                    f == cur ? Theme.AccentColor : Theme.TextColor, TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, p.Frames[f].DurationMs + "ms", Theme.SmallFont, new Rectangle(x, TagH + 15, cw, 14),
                    Theme.DimTextColor, TextFormatFlags.HorizontalCenter);
                if (p.Frames[f].Pivot is not null)
                    TextRenderer.DrawText(g, "◆", Theme.SmallFont, new Point(x + cw - 12, TagH), Color.FromArgb(255, 120, 80));
            }
            if (_dropAt >= 0)
            {
                int x = NameW + _dropAt * cw - _h.Value;
                g.DrawLine(accent, x, 0, x, ClientSize.Height);
            }
            g.ResetClip();

            // 레이어 이름 열
            g.FillRectangle(panel, 0, 0, NameW, TagH + HeadH);
            TextRenderer.DrawText(g, atlas ? "레이어 \\ 스프라이트" : "레이어 \\ 프레임", Theme.SmallFont, new Rectangle(4, TagH, NameW - 8, HeadH),
                Theme.DimTextColor, TextFormatFlags.VerticalCenter);
            g.SetClip(namesClip);
            for (int l = 0; l < p.Layers.Count; l++)
            {
                int y = LayerY(l);
                var layer = p.Layers[l];
                var r = new Rectangle(0, y, NameW, CellH);
                g.FillRectangle(l == curLayer ? hover : panel, r);
                g.DrawLine(border, 0, y + CellH - 1, NameW, y + CellH - 1);
                TextRenderer.DrawText(g, layer.Visible ? "👁" : "—", Theme.UiFont, new Rectangle(2, y, 22, CellH),
                    layer.Visible ? Theme.TextColor : Theme.DimTextColor, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, layer.Locked ? "🔒" : "·", Theme.UiFont, new Rectangle(24, y, 22, CellH),
                    layer.Locked ? Theme.WarningColor : Theme.DimTextColor, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                var name = layer.Name + (layer.Opacity < 255 ? $" ({layer.Opacity * 100 / 255}%)" : "");
                TextRenderer.DrawText(g, name, l == curLayer ? Theme.BoldFont : Theme.UiFont, new Rectangle(48, y, NameW - 50, CellH),
                    layer.Visible ? Theme.TextColor : Theme.DimTextColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (_layerDropAt == l) g.DrawRectangle(accent, 1, y + 1, NameW - 3, CellH - 3);
            }
            g.ResetClip();
            g.DrawLine(border, NameW - 1, 0, NameW - 1, ClientSize.Height);
            g.DrawLine(border, 0, TagH + HeadH - 1, ClientSize.Width, TagH + HeadH - 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            EndRename(true);
            Focus();
            if (Ctx.Project is not { } p) return;
            bool atlas = p.IsAtlas;
            _press = e.Location;
            if (TagAt(e.Location) is { } tag)
            {
                Ctx.SelectedTag = tag;
                if (e.Button == MouseButtons.Right) { _owner.ShowTagMenu(e.Location, tag); return; }
                if (e.Clicks == 2) { _owner.EditTag(tag); return; }
                _rangeA = tag.From;
                _rangeB = tag.To;
                Ctx.SetCurrentFrame(tag.From);
                Invalidate();
                return;
            }
            int f = FrameAt(e.X), l = LayerAt(e.Y);
            // 아틀라스는 태그 띠가 없으므로 위쪽 띠도 머리글(이름)이다.
            bool inHeader = atlas ? e.Y < TagH + HeadH : e.Y >= TagH && e.Y < TagH + HeadH;
            if (e.X < NameW && l >= 0)
            {
                // 레이어 이름 열: 눈/자물쇠/이름
                if (e.Button == MouseButtons.Left && e.X < 24) { Ctx.Structural(() => p.SetLayerProperties(l, visible: !p.Layers[l].Visible)); return; }
                if (e.Button == MouseButtons.Left && e.X < 48) { Ctx.Structural(() => p.SetLayerProperties(l, locked: !p.Layers[l].Locked)); return; }
                Ctx.SetCurrentLayer(l);
                if (e.Button == MouseButtons.Right) { _owner.ShowLayerMenu(e.Location, l); return; }
                if (e.Clicks == 2) { _owner.RenameLayer(l); return; }
                _layerDragFrom = l;
                return;
            }
            if (f < 0) return;
            if (!atlas && (ModifierKeys & Keys.Shift) != 0)
            {
                if (_rangeA < 0) _rangeA = Ctx.CurrentFrame;
                _rangeB = f;
            }
            else if (e.Button == MouseButtons.Left) ResetRange();
            Ctx.SetCurrentColumn(l >= 0 ? l : Ctx.CurrentLayer, f);
            if (e.Button == MouseButtons.Right)
            {
                if (atlas) _owner.ShowSpriteMenu(e.Location);
                else _owner.ShowFrameMenu(e.Location, f);
                return;
            }
            if (atlas && inHeader && e.Clicks == 2) { BeginRename(f); return; }
            if (inHeader && e.Button == MouseButtons.Left) _dragFrom = f;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Math.Abs(e.X - _press.X) + Math.Abs(e.Y - _press.Y) < 6) return;
            if (_dragFrom >= 0)
            {
                int drop = Math.Clamp((int)Math.Round((e.X - NameW + _h.Value) / (double)CellW), 0, Ctx.ColumnCount);
                if (drop != _dropAt) { _dropAt = drop; Invalidate(); }
                Cursor = Cursors.SizeWE;
            }
            else if (_layerDragFrom >= 0)
            {
                int l = LayerAt(e.Y);
                if (l != _layerDropAt) { _layerDropAt = l; Invalidate(); }
                Cursor = Cursors.SizeNS;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragFrom >= 0 && _dropAt >= 0)
            {
                int to = _dropAt > _dragFrom ? _dropAt - 1 : _dropAt;
                if (to != _dragFrom) _owner.MoveColumnTo(_dragFrom, to);
            }
            if (_layerDragFrom >= 0 && _layerDropAt >= 0 && _layerDropAt != _layerDragFrom && Ctx.Project is { } p)
            {
                int from = _layerDragFrom, to = _layerDropAt;
                Ctx.Structural(() => p.MoveLayer(from, to));
                Ctx.SetCurrentLayer(to);
            }
            _dragFrom = _dropAt = _layerDragFrom = _layerDropAt = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var bar = (ModifierKeys & Keys.Shift) != 0 ? (ScrollBar)_v : _h;
            if (!bar.Enabled) return;
            int max = Math.Max(0, bar.Maximum - bar.LargeChange + 1);
            bar.Value = Math.Clamp(bar.Value - Math.Sign(e.Delta) * bar.SmallChange, 0, max);
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Delete) { _owner.DeleteColumn(); e.Handled = true; }
            else if (e.KeyCode == Keys.F2 && Ctx.IsAtlas) { BeginRename(Ctx.CurrentSprite); e.Handled = true; }
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
        _name = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 200, FlatStyle = FlatStyle.Flat };
        _name.Items.AddRange(TimelinePanel.SuggestedNames.Where(s => !project.HasTag(s) || s == tag?.Name).Cast<object>().ToArray());
        _name.Text = tag?.Name ?? TimelinePanel.SuggestedNames.FirstOrDefault(s => !project.HasTag(s)) ?? project.UniqueTagName("anim");
        _from = Ui.Num(1, n, from + 1, 60);
        _to = Ui.Num(1, n, to + 1, 60);
        _direction = Ui.Combo(110, "정방향", "역방향", "왕복");
        _direction.SelectedIndex = (int)(tag?.Direction ?? TagDirection.Forward);
        _loop = Ui.Check("반복", tag?.Loop ?? true);
        _color = new ColorField(tag?.Color ?? AnimationTag.DefaultColors[project.Tags.Count % AnimationTag.DefaultColors.Count]);
        Body.Controls.Add(Ui.Label("이름 (테스트 규칙: idle_down, walk_left, run, jump, fall …)"));
        Body.Controls.Add(_name);
        Body.Controls.Add(Ui.Row(Ui.Label("프레임"), _from, Ui.Label("~"), _to));
        Body.Controls.Add(Ui.Row(Ui.Label("방향"), _direction, _loop));
        Body.Controls.Add(Ui.Row(Ui.Label("색"), _color));
        OkButton.Click += (_, _) =>
        {
            if (TagName.Length == 0) { Ui.Error(this, "이름을 입력하세요."); DialogResult = DialogResult.None; return; }
            if (project.Tags.Any(t => t.Name == TagName && !ReferenceEquals(t, tag)))
            {
                Ui.Error(this, $"\"{TagName}\" 태그가 이미 있습니다.");
                DialogResult = DialogResult.None;
                return;
            }
            if (_to.Value < _from.Value) { Ui.Error(this, "끝 프레임이 시작보다 앞입니다."); DialogResult = DialogResult.None; }
        };
    }

    public string TagName => _name.Text.Trim();
    public int From => (int)_from.Value - 1;
    public int To => (int)_to.Value - 1;
    public TagDirection Direction => (TagDirection)_direction.SelectedIndex;
    public bool Loop => _loop.Checked;
    public Color32 TagColor => _color.Color;
}
