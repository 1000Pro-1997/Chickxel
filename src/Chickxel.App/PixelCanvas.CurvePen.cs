using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 곡선 펜(포토샵 펜 같은 3차 베지어 경로 — <see cref="BezierLines"/>): 아직 그림에 칠하지 않은 경로(<see cref="_curvePen"/>)를 칠할 1px 픽셀(<see cref="BezierLines.Rasterize"/>),
/// 가는 벡터 선, 고정점(네모 — 고르거나 마우스가 올라가면 채움)과 손잡이(동그라미, 고정점까지 선)로 미리 보인다.
/// 그리기 방식(<see cref="EditorContext.CurvePenMode"/>):
/// 점 찍기 — 누름 = 경로 끝에 모서리 점, 누르고 끌기 = 매끈한 점과 마주 보는 두 손잡이, 첫 점을 누르면 닫힘, 경로에서 멀리 눌러도 이어 찍음(새 경로는 Enter 먼저).
/// 손으로 그리기 — 끈 획을 놓으면 <see cref="BezierLines.Fit"/>으로 경로를 만들고(Shift = 시작–끝 직선 하나, Alt = 모서리로 나누지 않음), 경로 밖에서 누르면 칠하고 새 획.
/// 고치기(두 방식 같음): 고정점 끌기 = 그 점과 제 손잡이 둘만 옮김(이웃 점은 그대로), 손잡이 끌기 = 매끈한 점이면 반대 손잡이가 제 길이로 한 줄을 지킴(Alt+끌기 = 모서리로 끊음),
/// Alt+클릭 고정점 = 모서리 ↔ 매끈한 점, 경로 두 번 누르기 = 점 더하기(모양 그대로), 고정점 두 번 누르기·마우스를 올리고 Delete/Backspace = 점 빼기.
/// Enter·도구/레이어/프레임 바꾸기·저장 등 편집 확정(<see cref="EditorContext.CommittingEdits"/>)이면 칠하고(실행취소 한 단계, 선택 영역·대칭·적용 프레임 따름), Esc·Ctrl+Z는 버린다.
/// 잡는 거리는 화면 픽셀이라 배율과 상관없다.
/// </summary>
public sealed partial class PixelCanvas
{
    /// <summary>손으로 그리기: 끝을 시작점에서 이만큼(px) 안에서 놓으면 닫힌 모양.</summary>
    private const double CurvePenCloseDistance = 1.5;
    /// <summary>닫힌 모양으로 볼 최소 획 길이(px — 짧은 점찍기가 닫히지 않게).</summary>
    private const double CurvePenMinClosedLength = 8;
    /// <summary>앞 점에서 이만큼(px) 넘게 움직여야 점을 더한다.</summary>
    private const double CurvePenSampleDistance = 0.2;
    /// <summary>고정점·손잡이를 잡는 거리와 경로를 잡는 거리(화면 px).</summary>
    private const double CurvePenHandleReach = 6, CurvePenPathReach = 5;
    /// <summary>고정점 네모 한 변과 손잡이 동그라미 반지름(화면 px).</summary>
    private const double CurvePenAnchorSize = 7, CurvePenKnobRadius = 3.5;
    /// <summary>점 찍기: 누르고 이만큼(화면 px) 넘게 끌어야 새 점의 손잡이를 뽑는다.</summary>
    private const double CurvePenPullDistance = 3;

    private static readonly IBrush CurvePenAnchorBrush = AppTheme.Brush(Colors.White);
    private static readonly IBrush CurvePenHotBrush = AppTheme.Brush(AppTheme.AccentColor);
    private static readonly IPen CurvePenOutlinePen = AppTheme.Pen(Color.FromArgb(230, 20, 20, 24), 1);
    private static readonly IPen CurvePenRawBackPen = AppTheme.Pen(Color.FromArgb(150, 0, 0, 0), 3);
    private static readonly IPen CurvePenPathBackPen = AppTheme.Pen(Color.FromArgb(120, 0, 0, 0), 2.5);
    private static readonly IPen CurvePenPathPen = AppTheme.Pen(AppTheme.AccentColor, 1);
    private static readonly IPen CurvePenStickPen = AppTheme.Pen(Color.FromArgb(220, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B), 1);

    /// <summary>미리 보는 경로: 버튼(색), 그린 셀(레이어·프레임), 고정점, 닫힘, 손으로 그린 획과 수정자(설정이 바뀌면 다시 맞춤 — 고치면 null), 칠할 픽셀(경로가 바뀌면 다시 셈).</summary>
    private sealed class CurvePenDraft(MouseButton button)
    {
        public MouseButton Button { get; } = button;
        public (int Layer, int Frame) Cell { get; init; }
        public List<BezierNode> Nodes { get; set; } = [];
        public bool Closed { get; set; }
        /// <summary>손으로 그린 획(다시 맞추기용). 점 찍기로 만들었거나 손으로 고쳤으면 null.</summary>
        public List<(double X, double Y)>? Raw { get; set; }
        public bool Straight { get; init; }
        public bool NoCorners { get; init; }
        public List<Point>? Pixels { get; set; }

        /// <summary>손으로 고친 경로로 바꾼다(다시 맞추지 않음).</summary>
        public void Edit(List<BezierNode> nodes)
        {
            Nodes = nodes;
            Raw = null;
            Pixels = null;
        }
    }

    /// <summary>잡은 것: 고정점 자리, 들어오는 손잡이, 나가는 손잡이.</summary>
    private enum CurvePenPart { Anchor, In, Out }

    /// <summary>경로의 고정점 번호와 잡은 것.</summary>
    private readonly record struct CurvePenHit(int Node, CurvePenPart Part);

    /// <summary>끄는 중인 획의 점(캔버스 픽셀 좌표, 칸 안 위치까지).</summary>
    private readonly List<(double X, double Y)> _curvePenRaw = new();
    /// <summary>미리 보는 경로(없으면 null).</summary>
    private CurvePenDraft? _curvePen;
    /// <summary>끄는 고정점·손잡이, 누른 자리(캔버스 픽셀 좌표), 끌기 전 고정점과 닫힘(끄는 동안 늘 이것에서 다시 계산 — 오차가 쌓이지 않음).</summary>
    private CurvePenHit? _curvePenGrab;
    private (double X, double Y) _curvePenGrabStart;
    private (List<BezierNode> Nodes, bool Closed)? _curvePenBefore;
    /// <summary>점 찍기: 이번 누름이 새 점을 찍었다(끌면 두 손잡이를 마주 보게 뽑음).</summary>
    private bool _curvePenPull;
    /// <summary>이번 누름이 경로를 새로 만들었다(취소하면 경로째 버림).</summary>
    private bool _curvePenNewDraft;
    /// <summary>Alt를 누르고 손잡이를 잡았다(매끈한 점을 모서리로 끊음).</summary>
    private bool _curvePenBreak;
    /// <summary>마우스가 올라간 고정점·손잡이(밝게 그림)와 고른 고정점(채워 그림).</summary>
    private CurvePenHit? _curvePenHover;
    private int? _curvePenSelected;
    /// <summary>바로 앞 누름이 첫 점을 눌러 경로를 닫았다(곧바로 두 번 누르면 다시 열고 그 점을 뺌).</summary>
    private bool _curvePenClosedByClick;
    /// <summary>이번 누르기가 미리 보던 경로를 확정했는지(그러면 끌지 않고 뗀 클릭은 점을 찍지 않음).</summary>
    private bool _curvePenCommittedOnPress;

    /// <summary>화면 위치의 캔버스 픽셀 좌표(칸 안 위치까지, 화면 픽셀 가운데 기준 — 1배율이면 픽셀 가운데).</summary>
    private (double X, double Y) CurvePenPoint(Point screen)
        => ((screen.X + 0.5 - _offset.X) / _zoom, (screen.Y + 0.5 - _offset.Y) / _zoom);

    /// <summary>Alt를 누른 채 이 자리를 누르면 스포이드 대신 곡선 펜이 받는지(미리 보는 경로의 고정점·손잡이 위).</summary>
    private bool CurvePenTakesAlt(Point screen) => _ctx.Tool == ToolKind.CurvePen && CurvePenHitAt(screen) is not null;

    /// <summary>
    /// 누름: 미리 보는 경로의 고정점·손잡이면 그것을 끌고(두 번 누르기 = 점 빼기, Alt = 모서리 바꾸기·손잡이 끊기), 경로 위 두 번 누르기면 점을 더한다.
    /// 아니면 점 찍기는 경로 끝에 점을 찍고(닫혔거나 다른 버튼이면 칠한 뒤 새 경로), 손으로 그리기는 미리 보던 경로를 칠한 뒤 새 획을 시작한다.
    /// </summary>
    private void BeginCurvePen(MouseButton button, Point screen, int clicks)
    {
        bool alt = (_mods & KeyModifiers.Alt) != 0;
        bool reopen = _curvePenClosedByClick;
        _curvePenClosedByClick = false;
        var p = CurvePenPoint(screen);
        if (_curvePen is { } draft && button == MouseButton.Left)
        {
            if (CurvePenHitAt(screen) is { } hit)
            {
                if (hit.Part == CurvePenPart.Anchor && clicks >= 2)
                {
                    if (reopen && hit.Node == 0) draft.Closed = false;
                    RemoveCurvePenAnchor(hit.Node);
                    return;
                }
                if (hit.Part == CurvePenPart.Anchor && alt)
                {
                    _altUsed = true;
                    draft.Edit(BezierLines.ToggleCorner(draft.Nodes, hit.Node, draft.Closed));
                    _curvePenSelected = hit.Node;
                    InvalidateVisual();
                    return;
                }
                if (alt) _altUsed = true;
                GrabCurvePen(hit, p, pull: false, breakCorner: alt && hit.Part != CurvePenPart.Anchor);
                return;
            }
            if (CurvePenPathAt(screen) is { } near)
            {
                // 경로 위 한 번 누르기는 두 번 누르기를 기다린다(점을 찍지 않음).
                if (clicks < 2) return;
                var (nodes, index) = BezierLines.Split(draft.Nodes, near.Segment, near.T, draft.Closed);
                if (index < 0) return;
                draft.Edit(nodes);
                _curvePenSelected = index;
                _curvePenHover = new CurvePenHit(index, CurvePenPart.Anchor);
                InvalidateVisual();
                return;
            }
        }
        if (_ctx.CurvePenMode == CurvePenMode.Points)
        {
            var at = CellCenter(p);
            if (_curvePen is { Closed: false } open && open.Button == button)
            {
                open.Edit([.. open.Nodes, BezierNode.At(at)]);
                GrabCurvePen(new CurvePenHit(open.Nodes.Count - 1, CurvePenPart.Out), p, pull: true, breakCorner: false);
                return;
            }
            CommitCurvePen();
            _curvePen = new CurvePenDraft(button) { Cell = (_ctx.CurrentLayer, _ctx.CurrentFrame), Nodes = [BezierNode.At(at)] };
            GrabCurvePen(new CurvePenHit(0, CurvePenPart.Out), p, pull: true, breakCorner: false);
            _curvePenNewDraft = true;
            Message?.Invoke(this, UiStrings.CurvePen_PendingPoints);
            return;
        }
        _curvePenCommittedOnPress = CommitCurvePen();
        _curvePenRaw.Clear();
        _curvePenRaw.Add(p);
        _drag = DragMode.CurvePen;
        InvalidateVisual();
    }

    /// <summary>고정점·손잡이 끌기를 시작한다(끌기 전 경로를 남겨 둠).</summary>
    private void GrabCurvePen(CurvePenHit hit, (double X, double Y) p, bool pull, bool breakCorner)
    {
        if (_curvePen is not { } draft) return;
        _curvePenGrab = hit;
        _curvePenGrabStart = p;
        _curvePenBefore = ([.. draft.Nodes], draft.Closed);
        _curvePenPull = pull;
        _curvePenBreak = breakCorner;
        _curvePenNewDraft = false;
        _curvePenSelected = hit.Node;
        _drag = DragMode.CurvePenHandle;
        InvalidateVisual();
    }

    /// <summary>끄는 중: 앞 점에서 조금이라도 움직였으면 점을 더한다.</summary>
    private void ContinueCurvePen(Point screen)
    {
        var p = CurvePenPoint(screen);
        var last = _curvePenRaw[^1];
        if (Dist(p, last) <= CurvePenSampleDistance) return;
        _curvePenRaw.Add(p);
        InvalidateVisual();
    }

    /// <summary>놓음(손으로 그리기): 획을 맞춰 미리 보기로 넘긴다. 끌지 않고 뗐으면 점 하나를 칠한다(누르며 미리 보던 경로를 확정했으면 아무것도 안 함).</summary>
    private void FinishCurvePen()
    {
        var raw = new List<(double X, double Y)>(_curvePenRaw);
        _curvePenRaw.Clear();
        if (raw.Count == 0) return;
        bool straight = (_mods & KeyModifiers.Shift) != 0 || ShiftDown;
        bool noCorners = (_mods & KeyModifiers.Alt) != 0 || KeyState.IsDown(Key.LeftAlt) || KeyState.IsDown(Key.RightAlt);
        // 끄는 데 쓴 Alt를 떼도 창의 단축 글자 처리가 메뉴를 열지 않게.
        if (noCorners) _altUsed = true;
        if (PathLength(raw) < 0.5)
        {
            if (!_curvePenCommittedOnPress) PlotCurvePenDot(raw[0]);
            return;
        }
        var draft = new CurvePenDraft(_dragButton) { Cell = (_ctx.CurrentLayer, _ctx.CurrentFrame), Raw = raw, Straight = straight, NoCorners = noCorners };
        FitCurvePen(draft);
        if (draft.Nodes.Count == 0) return;
        _curvePen = draft;
        _curvePenSelected = null;
        Message?.Invoke(this, UiStrings.CurvePen_Pending);
        InvalidateVisual();
    }

    /// <summary>손으로 그린 획을 지금 설정(맞춤 허용치·모서리 각도)으로 경로에 맞춘다. Shift면 픽셀 가운데에 붙인 시작–끝 직선 하나.</summary>
    private void FitCurvePen(CurvePenDraft draft)
    {
        if (draft.Raw is not { } raw) return;
        draft.Pixels = null;
        if (draft.Straight)
        {
            var a = CellCenter(raw[0]);
            var b = CellCenter(raw[^1]);
            draft.Closed = false;
            draft.Nodes = [new BezierNode(a, a, Lerp(a, b, 1 / 3.0), false), new BezierNode(b, Lerp(a, b, 2 / 3.0), b, false)];
            return;
        }
        bool closed = raw.Count >= 4 && PathLength(raw) >= CurvePenMinClosedLength && Dist(raw[^1], raw[0]) <= CurvePenCloseDistance;
        var pts = raw;
        if (closed)
        {
            // 닫힌 모양: 시작점 가까이 돌아온 끝 점들은 빼고(맞추기가 끝 → 시작을 이음) 넘긴다.
            int n = raw.Count;
            while (n > 3 && Dist(raw[n - 1], raw[0]) <= CurvePenCloseDistance) n--;
            pts = raw.GetRange(0, n);
        }
        double corner = draft.NoCorners ? EditorContext.MaxCurvePenCornerAngle : _ctx.CurvePenCornerAngle;
        draft.Nodes = BezierLines.Fit(pts, _ctx.CurvePenTolerance, corner, closed);
        draft.Closed = closed && draft.Nodes.Count >= 2;
    }

    /// <summary>도구 설정이 바뀜: 손으로 그려 아직 고치지 않은 미리 보기면 새 설정으로 다시 맞춘다.</summary>
    private void RefitCurvePen()
    {
        if (_curvePen is not { Raw: not null } draft || _drag == DragMode.CurvePenHandle) return;
        FitCurvePen(draft);
        _curvePenSelected = null;
        _curvePenHover = null;
        InvalidateVisual();
    }

    /// <summary>미리 보는 경로가 칠할 픽셀(타일 감기·대칭 전).</summary>
    private static List<Point> CurvePenPixels(CurvePenDraft draft)
        => draft.Pixels ??= BezierLines.Rasterize(draft.Nodes, draft.Closed);

    /// <summary>미리 보던 경로를 칠한다(실행취소 한 단계). 미리 보던 경로가 없었으면 false.</summary>
    private bool CommitCurvePen()
    {
        if (_curvePen is not { } draft) return false;
        _curvePen = null;
        EndCurvePenHandle();
        // 빈 셀은 EditImage가 null이지만 획을 시작하면(BeginPixelStroke) 셀이 생긴다.
        if (_ctx.CanEdit && !_ctx.IsGroupSelected && !_ctx.IsTilemapSelected)
        {
            var pixels = CurvePenPixels(draft);
            BeginPixelStroke(ToolKind.CurvePen, draft.Button, pixels.Count > 0 ? pixels[0] : Point.Empty);
            Plot(_ctx.EditImage!, Targets(pixels), _strokeColor, _ctx.Selection.Clip);
            _ctx.NotifyPixels();
            EndPixelStroke();
        }
        InvalidateVisual();
        return true;
    }

    /// <summary>미리 보던 경로를 버린다. 없었으면 false.</summary>
    private bool CancelCurvePen()
    {
        if (_curvePen is null) return false;
        _curvePen = null;
        EndCurvePenHandle();
        Message?.Invoke(this, UiStrings.S_PixelCanvas_CancelPixelStroke_53972D288A);
        InvalidateVisual();
        return true;
    }

    /// <summary>미리 보던 경로를 알리지 않고 버린다(문서가 바뀌어 그릴 곳이 사라짐).</summary>
    private void DropCurvePen()
    {
        if (_curvePen is null) return;
        _curvePen = null;
        EndCurvePenHandle();
        InvalidateVisual();
    }

    /// <summary>지금 셀이 미리 보던 경로를 그린 셀과 달라졌으면(편집 확정을 거치지 않고 바뀜 — 레이어가 지워지는 등) 버린다.</summary>
    private void DropMovedCurvePen()
    {
        if (_curvePen is { } draft && draft.Cell != (_ctx.CurrentLayer, _ctx.CurrentFrame)) DropCurvePen();
    }

    /// <summary>고정점·손잡이 끌기 상태를 지운다(끈 모양은 그대로). 경로가 없어졌으면 고른 점·마우스가 올라간 것도 지운다.</summary>
    private void EndCurvePenHandle()
    {
        if (_drag == DragMode.CurvePenHandle) _drag = DragMode.None;
        _curvePenGrab = null;
        _curvePenBefore = null;
        _curvePenPull = _curvePenBreak = _curvePenNewDraft = false;
        if (_curvePen is null)
        {
            _curvePenHover = null;
            _curvePenSelected = null;
            _curvePenClosedByClick = false;
        }
    }

    /// <summary>놓음(고정점·손잡이): 점 찍기에서 열린 경로의 첫 점을 끌지 않고 눌렀다 떼면 경로를 닫는다.</summary>
    private void FinishCurvePenHandle()
    {
        if (_curvePen is { } draft && _curvePenGrab is { Part: CurvePenPart.Anchor, Node: 0 } && !_dragMoved && !draft.Closed
            && draft.Nodes.Count >= 2 && _ctx.CurvePenMode == CurvePenMode.Points)
        {
            draft.Closed = true;
            draft.Pixels = null;
            _curvePenClosedByClick = true;
            InvalidateVisual();
        }
        EndCurvePenHandle();
    }

    /// <summary>반대 버튼으로 곡선 펜 끌기를 취소한다: 획이면 버리고, 고정점·손잡이면 끌기 전으로(새로 만든 경로면 경로째). 곡선 펜 끌기가 아니었으면 false.</summary>
    private bool CancelCurvePenDrag()
    {
        if (_drag == DragMode.CurvePen)
        {
            _drag = DragMode.None;
            _curvePenRaw.Clear();
            Message?.Invoke(this, UiStrings.S_PixelCanvas_CancelPixelStroke_53972D288A);
        }
        else if (_drag == DragMode.CurvePenHandle)
        {
            if (_curvePenNewDraft) _curvePen = null;
            else if (_curvePen is { } draft && _curvePenBefore is { } before)
            {
                // 새로 찍은 점(끝에 더함)은 끌기 전 목록에서도 뺀다.
                draft.Nodes = _curvePenPull ? before.Nodes[..^1] : before.Nodes;
                draft.Closed = before.Closed;
                draft.Pixels = null;
            }
            _curvePenSelected = null;
            EndCurvePenHandle();
        }
        else return false;
        InvalidateVisual();
        return true;
    }

    /// <summary>끌지 않고 누른 곳에 점 하나를 칠한다(실행취소 한 단계).</summary>
    private void PlotCurvePenDot((double X, double Y) p)
    {
        if (!_ctx.CanEdit) return;
        var cell = new Point((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
        BeginPixelStroke(ToolKind.CurvePen, _dragButton, cell);
        Plot(_ctx.EditImage!, Targets([cell]), _strokeColor, _ctx.Selection.Clip);
        _ctx.NotifyPixels();
        EndPixelStroke();
    }

    // ───────────── 고치기 ─────────────

    /// <summary>고정점 하나를 뺀다(양옆 조각은 하나로 합침). 마지막 점이면 경로를 버린다.</summary>
    private void RemoveCurvePenAnchor(int index)
    {
        if (_curvePen is not { } draft) return;
        if (draft.Nodes.Count <= 1)
        {
            DropCurvePen();
            return;
        }
        draft.Edit(BezierLines.Remove(draft.Nodes, index, draft.Closed));
        if (draft.Closed && draft.Nodes.Count < 2) draft.Closed = false;
        _curvePenSelected = null;
        _curvePenHover = null;
        InvalidateVisual();
    }

    /// <summary>Delete·Backspace: 마우스가 올라간 고정점을 뺀다. 뺄 점이 없으면 false(키는 다른 곳이 받음 — 선택 지우기 등).</summary>
    public bool DeleteCurvePenAnchor()
    {
        if (_curvePen is not { } draft || _drag != DragMode.None) return false;
        if (_curvePenHover is not { Part: CurvePenPart.Anchor, Node: var i } || i >= draft.Nodes.Count) return false;
        RemoveCurvePenAnchor(i);
        return true;
    }

    /// <summary>
    /// 잡은 것을 <paramref name="screen"/>으로 끈다. 고정점: 누른 자리에서 움직인 만큼(픽셀 단위) 그 점과 제 손잡이 둘만 옮김. 새로 찍은 점: 끈 쪽으로 나가는 손잡이,
    /// 반대쪽에 같은 길이의 들어오는 손잡이(조금만 끌면 모서리 점 그대로). 손잡이: <see cref="BezierLines.DragHandle"/>(매끈한 점은 반대 손잡이가 한 줄).
    /// </summary>
    private void DragCurvePenHandle(Point screen)
    {
        if (_curvePen is not { } draft || _curvePenGrab is not { } grab || _curvePenBefore is not { } before) return;
        var p = CurvePenPoint(screen);
        var nodes = new List<BezierNode>(before.Nodes);
        if (grab.Node >= nodes.Count) return;
        var node = nodes[grab.Node];
        if (_curvePenPull)
        {
            bool pulled = Math.Abs(screen.X - _dragStartScreen.X) + Math.Abs(screen.Y - _dragStartScreen.Y) > CurvePenPullDistance;
            nodes[grab.Node] = pulled
                ? new BezierNode(node.Point, (2 * node.Point.X - p.X, 2 * node.Point.Y - p.Y), p, false)
                : BezierNode.At(node.Point);
        }
        else if (grab.Part == CurvePenPart.Anchor)
            nodes[grab.Node] = node.Offset(Math.Round(p.X - _curvePenGrabStart.X), Math.Round(p.Y - _curvePenGrabStart.Y));
        else nodes[grab.Node] = BezierLines.DragHandle(node, grab.Part == CurvePenPart.Out, p, _curvePenBreak);
        draft.Edit(nodes);
        InvalidateVisual();
    }

    /// <summary>
    /// 화면 위치에서 가장 가까운 고정점·손잡이(잡는 거리 안, 고정점이 손잡이보다 먼저). 접힌 손잡이는 잡지 않는다. 없으면 null.
    /// </summary>
    private CurvePenHit? CurvePenHitAt(Point screen)
    {
        if (_curvePen is not { } draft) return null;
        CurvePenHit? best = null;
        double bestDist = CurvePenHandleReach;
        for (int i = 0; i < draft.Nodes.Count; i++)
        {
            var n = draft.Nodes[i];
            var a = ImageToScreen(n.Point.X, n.Point.Y);
            double d = Math.Max(Math.Abs(a.X - screen.X), Math.Abs(a.Y - screen.Y));
            if (d <= bestDist) (best, bestDist) = (new CurvePenHit(i, CurvePenPart.Anchor), d);
        }
        for (int i = 0; i < draft.Nodes.Count; i++)
        {
            var n = draft.Nodes[i];
            foreach (var (part, at, retracted) in new[] { (CurvePenPart.In, n.In, n.InRetracted), (CurvePenPart.Out, n.Out, n.OutRetracted) })
            {
                if (retracted) continue;
                var s = ImageToScreen(at.X, at.Y);
                double d = Math.Sqrt((s.X - screen.X) * (s.X - screen.X) + (s.Y - screen.Y) * (s.Y - screen.Y)) + 0.5;
                if (d < bestDist) (best, bestDist) = (new CurvePenHit(i, part), d);
            }
        }
        return best;
    }

    /// <summary>화면 위치가 경로 위(잡는 거리 안)면 가장 가까운 조각과 매개값. 아니면 null.</summary>
    private (int Segment, double T)? CurvePenPathAt(Point screen)
    {
        if (_curvePen is not { } draft) return null;
        var (segment, t, distance) = BezierLines.Nearest(draft.Nodes, draft.Closed, CurvePenPoint(screen));
        return segment >= 0 && distance * _zoom <= CurvePenPathReach ? (segment, t) : null;
    }

    /// <summary>마우스가 올라간 고정점·손잡이가 바뀌면 다시 그린다(멈춰 있을 때).</summary>
    private void UpdateCurvePenHover(Point screen)
    {
        var hover = _ctx.Tool == ToolKind.CurvePen ? CurvePenHitAt(screen) : null;
        if (hover == _curvePenHover) return;
        _curvePenHover = hover;
        InvalidateVisual();
    }

    // ───────────── 그리기 ─────────────

    /// <summary>
    /// 곡선 펜 화면: 끄는 중이면 그린 자국(Shift면 시작–끝 직선)을 가는 선으로, 미리 보는 경로면 칠할 픽셀(선택 영역·대칭 포함), 가는 벡터 선,
    /// 손잡이(고정점까지 선과 동그라미)와 고정점(네모 — 고른 점·마우스가 올라간 점은 채움)을, 그 밖에는 커서 아래 칸 윤곽(대칭 자리는 회색)을 그린다.
    /// </summary>
    private void DrawCurvePen(DrawingContext g)
    {
        if (_drag == DragMode.CurvePen && _curvePenRaw.Count > 0)
        {
            var c = StrokePreviewColor(_dragButton);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(ImageToScreen(_curvePenRaw[0].X, _curvePenRaw[0].Y), false);
                if ((_mods & KeyModifiers.Shift) != 0 || ShiftDown) ctx.LineTo(ImageToScreen(_curvePenRaw[^1].X, _curvePenRaw[^1].Y));
                else for (int i = 1; i < _curvePenRaw.Count; i++) ctx.LineTo(ImageToScreen(_curvePenRaw[i].X, _curvePenRaw[i].Y));
                ctx.EndFigure(false);
            }
            g.DrawGeometry(null, CurvePenRawBackPen, geo);
            g.DrawGeometry(null, new Pen(new SolidColorBrush(c.A == 0 ? Colors.White : Color.FromRgb(c.R, c.G, c.B)), 1.5), geo);
            return;
        }
        if (_curvePen is { } draft)
        {
            var clip = _ctx.Selection.Clip;
            FillCells(g, Targets(CurvePenPixels(draft)).Where(m => clip is null || clip.Value.Contains(m)), StrokePreviewBrush(draft.Button));
            DrawCurvePenPath(g, draft);
            if (_curvePenHover is not null || _curvePenGrab is not null) return;
        }
        if (_ctx.Tool != ToolKind.CurvePen || _cursorPx is not { } p || _drag != DragMode.None
            || _ctx.IsGroupSelected || _ctx.IsTilemapSelected || _ctx.IsTransforming || _ctx.IsCanvasResizing) return;
        var cells = new HashSet<Point> { p };
        DrawMirrorGhosts(g, cells);
        DrawCellOutline(g, cells, CursorOutlineBackPen, CursorOutlineFrontPen);
    }

    /// <summary>경로의 벡터 선, 손잡이, 고정점(포토샵 펜 모양).</summary>
    private void DrawCurvePenPath(DrawingContext g, CurvePenDraft draft)
    {
        var nodes = draft.Nodes;
        int count = BezierLines.SegmentCount(nodes.Count, draft.Closed);
        if (count > 0)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(ImageToScreen(nodes[0].Point.X, nodes[0].Point.Y), false);
                for (int k = 0; k < count; k++)
                {
                    var (_, p1, p2, p3) = BezierLines.Segment(nodes, k);
                    ctx.CubicBezierTo(ImageToScreen(p1.X, p1.Y), ImageToScreen(p2.X, p2.Y), ImageToScreen(p3.X, p3.Y));
                }
                ctx.EndFigure(draft.Closed);
            }
            g.DrawGeometry(null, CurvePenPathBackPen, geo);
            g.DrawGeometry(null, CurvePenPathPen, geo);
        }
        var hot = _curvePenGrab ?? _curvePenHover;
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var a = ImageToScreen(n.Point.X, n.Point.Y);
            foreach (var (part, at, retracted) in new[] { (CurvePenPart.In, n.In, n.InRetracted), (CurvePenPart.Out, n.Out, n.OutRetracted) })
            {
                if (retracted) continue;
                var s = ImageToScreen(at.X, at.Y);
                g.DrawLine(CurvePenStickPen, a, s);
                var fill = hot == new CurvePenHit(i, part) ? CurvePenHotBrush : CurvePenAnchorBrush;
                g.DrawEllipse(fill, CurvePenOutlinePen, s, CurvePenKnobRadius, CurvePenKnobRadius);
            }
        }
        for (int i = 0; i < nodes.Count; i++)
        {
            var a = ImageToScreen(nodes[i].Point.X, nodes[i].Point.Y);
            bool filled = _curvePenSelected == i || hot == new CurvePenHit(i, CurvePenPart.Anchor);
            double r = CurvePenAnchorSize / 2;
            g.DrawRectangle(filled ? CurvePenHotBrush : CurvePenAnchorBrush, CurvePenOutlinePen,
                new Rect(Math.Round(a.X - r) + 0.5, Math.Round(a.Y - r) + 0.5, CurvePenAnchorSize - 1, CurvePenAnchorSize - 1));
        }
    }

    // ───────────── 좌표 도우미 ─────────────

    private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double PathLength(List<(double X, double Y)> pts)
    {
        double sum = 0;
        for (int i = 1; i < pts.Count; i++) sum += Dist(pts[i - 1], pts[i]);
        return sum;
    }

    private static (double X, double Y) Lerp((double X, double Y) a, (double X, double Y) b, double t) => (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>그 점이 든 픽셀의 가운데.</summary>
    private static (double X, double Y) CellCenter((double X, double Y) p) => (Math.Floor(p.X) + 0.5, Math.Floor(p.Y) + 0.5);
}
