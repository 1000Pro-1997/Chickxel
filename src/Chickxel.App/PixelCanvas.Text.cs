using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Media.Immutable;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Text;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 글씨 도구(Aseprite처럼 캔버스에서 바로 쓰기)와 이동 도구의 글씨 레이어 옮기기.
/// <para>
/// 글씨 도구로 끌면 그 사각형이 글 상자(상자 너비에서 줄 바꿈, 한 줄이 상자 높이에 드는 글자 크기)인, 누르기만 하면 그 자리에서 자동 너비인 새 글씨 레이어를
/// 현재 레이어(부위면 그 그룹) 바로 위에 만들고 바로 고치기 시작한다. 글씨가 있는 곳을 누르면 그 글씨를 고친다(누른 글자 틈에 커서).
/// 고치는 동안 친 글이 레이어 효과 미리보기로 바로 픽셀로 보이고, 커서·고른 글·점선 상자 테두리와 손잡이 8개(끌면 상자 크기 — 위·아래 변은 글자 크기도, 자동 너비는 상자가 됨)·
/// 테두리 끌기(옮기기)를 캔버스 위에 그린다. 입력기(한글 조합)는 캔버스의 입력기 연결(<see cref="CanvasTextInput"/>)로 조합 중인 글자를 그 자리에 보인다.
/// </para>
/// <para>
/// Esc·Ctrl(⌘)+Enter·상자 밖 누르기·도구/레이어/프레임/문서 바꾸기·창 비활성·편집 확정(<see cref="EditorContext.CommittingEdits"/> — 저장·실행취소 등)이면 끝낸다.
/// 고친 것은 세션 전체가 실행취소 한 단계(바뀐 것이 없으면 기록 없음)이고, 빈 글이면 새 글씨는 만들지 않고 고치던 글씨는 그대로 둔다.
/// 이동 도구는 선택이 없고 현재 셀이 비어 있는 글씨 레이어에서 셀 대신 글씨 효과의 X·Y(상자면 상자 왼쪽 위)를 옮긴다(끄는 동안 미리보기, 놓으면 한 단계).
/// </para>
/// </summary>
public sealed partial class PixelCanvas
{
    /// <summary>
    /// 캔버스에서 고치는 글씨 하나(편집 세션). <see cref="Original"/>은 세션 전 레이어 효과 목록(새 글씨는 만든 직후 — 빈 글씨 하나)이고,
    /// 고치는 동안 레이어에는 <see cref="Shown"/>을 넣은 목록을 미리 보인다(실행취소 기록 없음).
    /// </summary>
    private sealed class TextSession(SpriteProject project, Layer layer, int effectIndex, IReadOnlyList<LayerEffect> original, TextEffect effect, int frame)
    {
        public SpriteProject Project { get; } = project;
        public Layer Layer { get; } = layer;
        public int EffectIndex { get; } = effectIndex;
        public IReadOnlyList<LayerEffect> Original { get; } = original;
        /// <summary>시작한 프레임(다른 프레임으로 가면 끝낸다).</summary>
        public int Frame { get; } = frame;
        /// <summary>확정할 글씨(조합 중인 글자는 빼고).</summary>
        public TextEffect Effect { get; set; } = effect;
        /// <summary>레이어에 미리 보이는 글씨(조합 중인 글자 포함, 타자기 끔). 레이어 효과 목록에 이 참조가 그대로 있어야 세션이 살아 있다.</summary>
        public TextEffect? Shown { get; set; }
        /// <summary><see cref="Shown"/>의 배치(효과 좌표 = 지금 캔버스 좌표).</summary>
        public TextLayoutResult Layout { get; set; } = TextLayout.Measure(effect);
        /// <summary>커서와 고르기 시작점(UTF-16 위치, <see cref="Effect"/>의 글 기준). 같으면 고른 글이 없다.</summary>
        public int Caret { get; set; }
        public int Anchor { get; set; }
        /// <summary>입력기가 조합 중인 글자(커서 자리에 보임)와 그 안의 커서.</summary>
        public string Preedit { get; set; } = "";
        public int PreeditCursor { get; set; }
        /// <summary>위·아래 화살표로 줄을 옮길 때 지킬 가로 위치(효과 좌표). 다른 동작이면 지운다.</summary>
        public int? GoalX { get; set; }
        /// <summary>새 글씨면 만든 기록 정보, 이미 있던 글씨를 고치면 null.</summary>
        public NewTextInfo? New { get; init; }

        public int SelectionStart => Math.Min(Caret, Anchor);
        public int SelectionEnd => Math.Max(Caret, Anchor);
    }

    /// <summary>
    /// 새 글씨 세션: 빈 글씨 레이어를 만든 실행취소 기록(묶음 중이었으면 null), 넣은 자리, 시작 전 수정됨 표시, 시작 전 현재 레이어.
    /// 끝낼 때 만든 기록이 아직 맨 위면 거두고 최종 글씨를 한 단계로 다시 넣는다(빈 글이면 거두기만 — 기록·수정됨 표시가 남지 않음).
    /// </summary>
    private sealed record NewTextInfo(IUndoCommand? Created, int Slot, bool WasDirty, Layer Previous);

    /// <summary>글씨 상자에서 화면 위치가 가리키는 것.</summary>
    private enum TextBoxHit { Outside, Inside, Edge, Handle }

    private static readonly IPen TextFramePen = AppTheme.Pen(Color.FromRgb(90, 170, 255), 1, new ImmutableDashStyle([4.0, 3.0], 0));
    private static readonly IPen TextFrameBackPen = AppTheme.Pen(Color.FromArgb(150, 0, 0, 0), 3);
    private static readonly IBrush TextSelectionBrush = AppTheme.Brush(Color.FromArgb(110, 90, 170, 255));
    private static readonly IBrush TextHandleBrush = AppTheme.Brush(Colors.White);
    private static readonly IPen TextHandlePen = AppTheme.Pen(Color.FromRgb(30, 100, 190), 1.5);
    private static readonly IBrush TextCaretBrush = AppTheme.Brush(Colors.White);
    private static readonly IBrush TextCaretBackBrush = AppTheme.Brush(Color.FromArgb(220, 0, 0, 0));
    private static readonly IPen TextPreeditPen = AppTheme.Pen(Color.FromRgb(90, 170, 255), 2);
    /// <summary>글씨 상자 테두리를 잡는 띠(화면 px, 테두리 안팎)와 손잡이를 잡는 거리.</summary>
    private const double TextEdgeGrab = 5, TextHandleGrab = 5;

    private TextSession? _text;
    /// <summary>세션을 열거나 닫거나 미리보기를 바꾸는 중(그동안 오는 프로젝트·현재 레이어·설정 알림은 세션이 일으킨 것).</summary>
    private bool _textBusy;
    private readonly DispatcherTimer _caretTimer = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private bool _caretOn = true;
    private CanvasTextInput? _textInputClient;
    /// <summary>입력기에 마지막으로 알린 커서 사각형(화면 좌표) — 확대·화면 이동으로 바뀌면 다시 알린다.</summary>
    private Rect _reportedCaret;
    // 글씨 상자 끌기(손잡이·테두리): 잡은 손잡이(-1/0/1), 시작 때 틀(효과 좌표)과 글씨
    private int _textHx, _textHy;
    private Rectangle _textDragFrame;
    private TextEffect? _textDragEffect;

    /// <summary>이동 도구로 끄는 글씨 레이어와 끌기 전 효과 목록(끄는 중이 아니면 null).</summary>
    private Layer? _textMoveLayer;
    private IReadOnlyList<LayerEffect>? _textMoveOriginal;

    /// <summary>캔버스에서 글씨를 고치는 중인지(그동안 앱 단축키는 Ctrl/⌘ 전역 단축키만).</summary>
    public bool IsEditingText => _text is not null;

    /// <summary>캔버스 글씨 편집이 시작되거나 끝남.</summary>
    public event EventHandler? TextEditingChanged;

    /// <summary>생성자에서: 입력기 연결과 세션을 끝내는 알림들을 단다. 고치는 중이 아니면 입력기를 끈다(키가 단축키로 바로 온다).</summary>
    private void InitTextEditing()
    {
        InputMethod.SetIsInputMethodEnabled(this, false);
        TextInputMethodClientRequested += (_, e) =>
        {
            if (_text is not null) e.Client = _textInputClient ??= new CanvasTextInput(this);
        };
        _caretTimer.Tick += (_, _) =>
        {
            _caretOn = !_caretOn;
            InvalidateVisual();
        };
        _ctx.CommittingEdits += (_, _) => { if (!_textBusy) FinishTextEditing(); };
        _ctx.ToolChanged += (_, _) => { if (_ctx.Tool != ToolKind.Text) FinishTextEditing(); };
        _ctx.ProjectReplaced += (_, _) => { if (!_textBusy) FinishTextEditing(); };
        _ctx.CurrentChanged += (_, _) =>
        {
            if (_textBusy || _text is not { } s) return;
            if (!ReferenceEquals(_ctx.Project, s.Project) || !ReferenceEquals(_ctx.CurrentLayerObject, s.Layer) || _ctx.CurrentFrame != s.Frame)
                FinishTextEditing();
        };
        // 고치던 글씨 효과가 바깥에서 바뀌면(레이어 삭제 등) 프로젝트가 맞으므로 세션만 버린다.
        _ctx.ProjectChanged += (_, _) =>
        {
            if (_textBusy || _text is not { } s || TextSessionAlive(s)) return;
            CloseTextSession();
        };
        // 도구 설정(글꼴·크기 등)을 바꾸면 고치는 글씨에 바로 반영한다(같은 세션 — 실행취소 한 단계에 들어감).
        _ctx.TextStyleChanged += (_, _) =>
        {
            if (_textBusy || _text is not { } s) return;
            s.Effect = _ctx.TextStyle.ApplyTo(s.Effect);
            ApplyTextPreview(s);
        };
    }

    // ───────────── 글씨 도구: 누르기 ─────────────

    /// <summary>
    /// 글씨 도구 왼쪽 버튼 누르기(<paramref name="screen"/> = 화면, <paramref name="px"/> = 지금 캔버스 좌표).
    /// 고치는 중이면 손잡이(크기)·테두리(옮기기)·상자 안(커서·고르기, 두 번 = 낱말, 세 번 = 전부)을, 상자 밖이면 확정만 한다.
    /// 아니면 글씨가 있는 곳은 그 글씨를 고치고, 빈 곳은 새 상자 끌기를 시작한다.
    /// </summary>
    private void TextPointerPressed(Point screen, Point px, int clicks, bool shift)
    {
        if (_text is { } s && ReferenceEquals(_ctx.Project, s.Project))
        {
            var (kind, hx, hy) = TextBoxHitAt(s, screen);
            switch (kind)
            {
                case TextBoxHit.Handle:
                    _textHx = hx;
                    _textHy = hy;
                    _textDragFrame = s.Layout.Frame;
                    _textDragEffect = s.Effect;
                    _drag = DragMode.TextResize;
                    return;
                case TextBoxHit.Edge:
                    _textDragFrame = s.Layout.Frame;
                    _textDragEffect = s.Effect;
                    _drag = DragMode.TextMoveBox;
                    return;
                case TextBoxHit.Inside:
                    PlaceTextCaret(s, px, clicks, shift);
                    _drag = DragMode.TextSelect;
                    return;
            }
            // 상자 밖 누르기는 확정에만 쓴다.
            FinishTextEditing();
            return;
        }
        FinishTextEditing();
        if (_ctx.Project is not { } p || _ctx.CurrentLayerObject is not { IsGroup: false } current) return;
        _ctx.CommitFloating();
        if (TextAt(p, current, px) is { } hit)
        {
            BeginEditText(p, hit.Layer, hit.Index, px, clicks);
            if (_text is not null) _drag = DragMode.TextSelect;
            return;
        }
        _drag = DragMode.TextCreate;
    }

    /// <summary>
    /// 누른 곳(<paramref name="px"/>, 지금 캔버스 좌표)에 글씨가 있는 글씨 효과: 현재 레이어를 먼저, 현재 레이어가 일반 레이어면
    /// 보이는 다른 일반 레이어를 위에서부터 본다(부위 화면이면 그 부위만).
    /// </summary>
    private (Layer Layer, int Index)? TextAt(SpriteProject p, Layer current, Point px)
    {
        if (HitText(current, px) is { } own) return (current, own);
        if (current.IsPart) return null;
        for (int i = p.Layers.Count - 1; i >= 0; i--)
        {
            var layer = p.Layers[i];
            if (ReferenceEquals(layer, current) || layer.IsGroup || layer.IsPart || !p.IsLayerVisible(i)) continue;
            if (HitText(layer, px) is { } k) return (layer, k);
        }
        return null;
    }

    /// <summary>레이어에서 현재 프레임에 켜진 글씨 효과 중 상자(틀)나 글자 잉크가 <paramref name="px"/>를 덮는 맨 뒤(위에 그려지는) 것의 번호.</summary>
    private int? HitText(Layer layer, Point px)
    {
        for (int i = layer.Effects.Count - 1; i >= 0; i--)
        {
            if (layer.Effects[i] is not TextEffect { Enabled: true } t || t.Frames is { } r && !r.Contains(_ctx.CurrentFrame)) continue;
            var m = TextLayout.Measure(t);
            // 1px 둘레까지 눌러도 잡히게(얇은 글꼴).
            if (Rectangle.Inflate(m.Frame, 1, 1).Contains(px) || !m.Ink.IsEmpty && Rectangle.Inflate(m.Ink, 1, 1).Contains(px)) return i;
        }
        return null;
    }

    /// <summary>이미 있는 글씨 효과를 고치기 시작한다(잠겼으면 알림만). 도구 설정을 그 글씨의 설정으로 바꾸고, 커서는 누른 글자 틈.</summary>
    private void BeginEditText(SpriteProject p, Layer layer, int effectIndex, Point px, int clicks)
    {
        int index = p.IndexOf(layer);
        if (p.IsLayerLocked(index))
        {
            Message?.Invoke(this, UiStrings.TextTool_Locked);
            return;
        }
        if (index != _ctx.CurrentLayer) _ctx.SetCurrentLayer(index);
        var effect = (TextEffect)layer.Effects[effectIndex];
        _ctx.TextStyle = TextStyle.From(effect);
        var s = new TextSession(p, layer, effectIndex, layer.Effects, effect, _ctx.CurrentFrame);
        StartTextSession(s);
        PlaceTextCaret(s, px, clicks, shift: false);
    }

    /// <summary>
    /// 새 글씨 끌기를 놓음: 끈 사각형(2px 넘게 넓으면)은 상자이고 글자 크기는 한 줄이 상자 높이에 드는 크기(도구 설정 크기도 그 값으로),
    /// 아니면 누른 점에 자동 너비 글씨(크기는 도구 설정). 글꼴 등은 도구 설정, 색은 기본색(투명이면 검정).
    /// 부위 화면의 좌표는 부위 안 좌표이므로 새 레이어(프레임 캔버스)에서는 부위 위치만큼 옮긴다.
    /// </summary>
    private void EndTextCreate()
    {
        if (_ctx.Project is not { } p || _ctx.CurrentLayerObject is not { IsGroup: false } current) return;
        var r = TextCreateRect();
        bool box = _dragMoved && r.Width >= 2;
        var offset = current.IsPart ? current.PartOffset : Point.Empty;
        var effect = _ctx.TextStyle.ApplyTo(new TextEffect
        {
            Text = "",
            Color = _ctx.Primary.A == 0 ? Color32.Black : _ctx.Primary,
            X = (box ? r.X : _startPx.X) + offset.X,
            Y = (box ? r.Y : _startPx.Y) + offset.Y,
            BoxWidth = box ? r.Width : 0,
            BoxHeight = box ? r.Height : 0,
        });
        if (box)
        {
            effect = effect with { Size = TextLayout.FitSize(effect, r.Height) };
            _ctx.TextStyle = TextStyle.From(effect);
        }
        BeginNewText(p, current, effect);
    }

    /// <summary>새 글씨 끌기의 사각형(누른 픽셀과 지금 픽셀을 모두 덮음, 지금 캔버스 좌표).</summary>
    private Rectangle TextCreateRect()
        => Rectangle.FromLTRB(Math.Min(_startPx.X, _lastPx.X), Math.Min(_startPx.Y, _lastPx.Y),
            Math.Max(_startPx.X, _lastPx.X) + 1, Math.Max(_startPx.Y, _lastPx.Y) + 1);

    /// <summary>
    /// 빈 글씨 레이어를 <paramref name="anchor"/>(부위면 그 그룹) 바로 위에 만들어 고르고 고치기 시작한다.
    /// 만든 기록은 끝낼 때 최종 글씨 한 단계로 바뀐다(<see cref="NewTextInfo"/>).
    /// </summary>
    private void BeginNewText(SpriteProject p, Layer anchor, TextEffect effect)
    {
        int top = p.IndexOf(anchor.Parent ?? anchor);
        int slot = top < 0 ? p.Layers.Count : top + 1;
        bool wasDirty = p.IsDirty;
        Layer? added = null;
        _textBusy = true;
        try
        {
            _ctx.Structural(() => p.Edit(UiStrings.TextTool_AddLabel, ProjectChange.Layers, () =>
            {
                added = p.AddLayer(slot, UiStrings.TextTool_DefaultLayerName);
                p.SetLayerEffects(p.IndexOf(added), [effect]);
            }));
            if (added is null) return;
            var created = p.History.InGroup ? null : p.History.UndoTop;
            _ctx.SetCurrentLayer(p.IndexOf(added));
            StartTextSession(new TextSession(p, added, 0, added.Effects, effect, _ctx.CurrentFrame)
            {
                New = new NewTextInfo(created, slot, wasDirty, anchor),
            });
        }
        finally
        {
            _textBusy = false;
        }
    }

    // ───────────── 세션 열기·닫기 ─────────────

    private void StartTextSession(TextSession s)
    {
        _text = s;
        s.Caret = s.Anchor = s.Effect.Text.Length;
        ApplyTextPreview(s);
        Focus();
        InputMethod.SetIsInputMethodEnabled(this, true);
        UpdateCursor();
        TextEditingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>고치던 글씨 효과가 아직 레이어에 미리 보이는지(실행취소·레이어 삭제 등으로 바뀌지 않았는지).</summary>
    private static bool TextSessionAlive(TextSession s)
        => s.Project.IndexOf(s.Layer) >= 0 && s.EffectIndex < s.Layer.Effects.Count && ReferenceEquals(s.Layer.Effects[s.EffectIndex], s.Shown);

    /// <summary>세션 상태만 닫는다(입력기 끔, 커서 깜빡임 멈춤). 프로젝트는 건드리지 않는다.</summary>
    private void CloseTextSession()
    {
        if (_text is null) return;
        _text = null;
        _caretTimer.Stop();
        InputMethod.SetIsInputMethodEnabled(this, false);
        if (_drag is DragMode.TextSelect or DragMode.TextResize or DragMode.TextMoveBox) _drag = DragMode.None;
        _textDragEffect = null;
        InvalidateVisual();
        UpdateCursor();
        TextEditingChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 고치던 글씨를 확정한다(실행취소 한 단계, 바뀐 것이 없으면 기록 없음). 조합 중인 글자도 넣는다.
    /// 빈 글(공백뿐)이면 새 글씨는 만들지 않고(만든 기록도 거둠) 고치던 글씨는 그대로 둔다.
    /// </summary>
    private void FinishTextEditing()
    {
        if (_text is not { } s) return;
        if (s.Preedit.Length > 0)
        {
            var preedit = s.Preedit;
            s.Preedit = "";
            ReplaceTextSelection(s, preedit, preview: false);
        }
        CloseTextSession();
        var p = s.Project;
        int index = p.IndexOf(s.Layer);
        bool alive = TextSessionAlive(s);
        var final = s.Effect;
        bool keep = !string.IsNullOrWhiteSpace(final.Text);
        var effects = s.Original.ToArray();
        effects[s.EffectIndex] = final;
        _textBusy = true;
        try
        {
            if (alive) p.EndLayerEffectsPreview(index, s.Original, commit: false);
            if (s.New is { } info) FinishNewText(p, s.Layer, info, alive, keep, effects, TextLayerName(final.Text));
            else if (alive && keep && !effects.SequenceEqual(s.Original))
                p.Edit(UiStrings.TextTool_EditLabel, ProjectChange.Layers, () => p.SetLayerEffects(index, effects));
        }
        finally
        {
            _textBusy = false;
        }
    }

    /// <summary>
    /// 새 글씨 끝내기: 빈 레이어를 만든 기록이 아직 맨 위면 거두고, 글이 있으면 글 첫 줄 이름의 글씨 레이어를 같은 자리에 한 단계로 다시 넣는다
    /// (빈 글이면 거두기만 — 다시 실행 목록·수정됨 표시도 시작 전으로). 그사이 다른 기록이 쌓였으면 그 레이어를 그대로 고치거나(글 있음) 지운다.
    /// </summary>
    private void FinishNewText(SpriteProject p, Layer layer, NewTextInfo info, bool alive, bool keep, LayerEffect[] effects, string name)
    {
        bool top = alive && info.Created is not null && ReferenceEquals(p.History.UndoTop, info.Created);
        bool current = ReferenceEquals(_ctx.Project, p);
        if (top)
        {
            p.Undo();
            if (keep)
            {
                Layer? added = null;
                p.Edit(UiStrings.TextTool_AddLabel, ProjectChange.Layers, () =>
                {
                    added = p.AddLayer(info.Slot, name);
                    p.SetLayerEffects(p.IndexOf(added), effects);
                });
                if (current && added is not null && p.IndexOf(added) != _ctx.CurrentLayer) _ctx.SetCurrentLayer(p.IndexOf(added));
                return;
            }
            p.History.ClearRedo();
            p.IsDirty = info.WasDirty;
            if (current && p.IndexOf(info.Previous) is var previous and >= 0 && previous != _ctx.CurrentLayer) _ctx.SetCurrentLayer(previous);
            return;
        }
        if (!alive) return;
        int index = p.IndexOf(layer);
        if (keep)
            p.Edit(UiStrings.TextTool_AddLabel, ProjectChange.Layers, () =>
            {
                p.SetLayerEffects(index, effects);
                p.SetLayerProperties(index, name: name);
            });
        else p.DeleteLayer(index);
    }

    /// <summary>새 글씨 레이어 이름: 글 첫 줄(앞뒤 공백 뺌)을 24자까지, 비었으면 "글씨".</summary>
    private static string TextLayerName(string text)
    {
        var first = text.Split('\n')[0].Trim();
        if (first.Length == 0) return UiStrings.TextTool_DefaultLayerName;
        return first.Length > 24 ? first[..24].TrimEnd() : first;
    }

    // ───────────── 미리보기·글 고치기 ─────────────

    /// <summary>지금 글(조합 중인 글자를 커서 자리에 넣고 타자기는 끔)을 레이어에 미리 보이고 배치를 다시 잰다(실행취소 기록 없음).</summary>
    private void ApplyTextPreview(TextSession s)
    {
        var text = s.Effect.Text;
        var shown = s.Effect with
        {
            Text = s.Preedit.Length == 0 ? text : string.Concat(text.AsSpan(0, s.Caret), s.Preedit, text.AsSpan(s.Caret)),
            Typewriter = false,
        };
        s.Shown = shown;
        s.Layout = TextLayout.Measure(shown);
        int index = s.Project.IndexOf(s.Layer);
        if (index >= 0)
        {
            var effects = s.Original.ToArray();
            effects[s.EffectIndex] = shown;
            _textBusy = true;
            try
            {
                s.Project.PreviewLayerEffects(index, effects);
            }
            finally
            {
                _textBusy = false;
            }
        }
        TextCaretChanged();
    }

    /// <summary>조합 중인 글자가 없을 때의 배치(마우스로 커서를 놓을 때 — 확정된 글 기준 위치).</summary>
    private static TextLayoutResult CommittedLayout(TextSession s) => s.Preedit.Length == 0 ? s.Layout : TextLayout.Measure(s.Effect);

    /// <summary>
    /// 고른 글을 <paramref name="insert"/>로 바꾼다(줄바꿈은 "\n"으로, <see cref="TextEffect.MaxLength"/>를 넘는 뒷부분은 버림). 커서는 넣은 글 뒤.
    /// </summary>
    private void ReplaceTextSelection(TextSession s, string insert, bool preview = true)
    {
        var text = s.Effect.Text;
        int a = s.SelectionStart, b = s.SelectionEnd;
        insert = insert.Replace("\r\n", "\n").Replace('\r', '\n');
        int room = Math.Max(0, TextEffect.MaxLength - (text.Length - (b - a)));
        if (insert.Length > room)
        {
            insert = insert[..room];
            if (insert.Length > 0 && char.IsHighSurrogate(insert[^1])) insert = insert[..^1];
        }
        if (a == b && insert.Length == 0) return;
        s.Effect = s.Effect with { Text = string.Concat(text.AsSpan(0, a), insert, text.AsSpan(b)) };
        s.Caret = s.Anchor = Math.Min(a + insert.Length, s.Effect.Text.Length);
        s.GoalX = null;
        if (preview) ApplyTextPreview(s);
    }

    /// <summary>커서를 옮긴다(<paramref name="extend"/>면 고르기 시작점은 그대로).</summary>
    private void MoveTextCaret(TextSession s, int index, bool extend, bool keepGoal = false)
    {
        s.Caret = Math.Clamp(index, 0, s.Effect.Text.Length);
        if (!extend) s.Anchor = s.Caret;
        if (!keepGoal) s.GoalX = null;
        TextCaretChanged();
    }

    /// <summary>커서·글이 바뀜: 커서를 다시 켜고 깜빡임을 처음부터, 다시 그린다(입력기 커서 위치는 그릴 때 알림).</summary>
    private void TextCaretChanged()
    {
        _caretOn = true;
        _caretTimer.Stop();
        if (_text is not null) _caretTimer.Start();
        InvalidateVisual();
    }

    /// <summary>마우스로 커서를 놓는다: 한 번 = 그 글자 틈(Shift = 고른 글 늘이기), 두 번 = 낱말, 세 번 이상 = 전부.</summary>
    private void PlaceTextCaret(TextSession s, Point px, int clicks, bool shift)
    {
        var text = s.Effect.Text;
        int i = Math.Clamp(CommittedLayout(s).HitTest(px), 0, text.Length);
        if (clicks >= 3)
        {
            s.Anchor = 0;
            MoveTextCaret(s, text.Length, extend: true);
        }
        else if (clicks == 2)
        {
            var (start, end) = WordAt(text, i);
            s.Anchor = start;
            MoveTextCaret(s, end, extend: true);
        }
        else MoveTextCaret(s, i, shift);
    }

    /// <summary>글 고르기 끌기: 커서를 마우스 아래 글자 틈으로.</summary>
    private void DragTextSelect(Point px)
    {
        if (_text is not { } s) return;
        MoveTextCaret(s, CommittedLayout(s).HitTest(px), extend: true);
    }

    /// <summary>
    /// 글씨 상자 끌기: 손잡이면 잡은 변을 옮겨 상자 크기를 바꾸고(자동 너비 글씨는 지금 틀 그대로의 상자가 됨 — X·Y = 상자 왼쪽 위),
    /// 위·아래 변을 옮기면 글 전체가 새 높이에 드는 크기로 글자 크기도 바꾼다(도구 설정 크기도 따라감). 테두리면 글씨를 옮긴다.
    /// 모두 같은 세션(실행취소 한 단계)에 들어간다.
    /// </summary>
    private void DragTextBox(Point px)
    {
        if (_text is not { } s || _textDragEffect is not { } e0) return;
        int dx = px.X - _startPx.X, dy = px.Y - _startPx.Y;
        if (_drag == DragMode.TextMoveBox)
        {
            s.Effect = e0 with { X = e0.X + dx, Y = e0.Y + dy };
        }
        else
        {
            var f = _textDragFrame;
            int l = f.Left, t = f.Top, r = f.Right, b = f.Bottom;
            if (_textHx < 0) l = Math.Min(l + dx, r - 1);
            if (_textHx > 0) r = Math.Max(r + dx, l + 1);
            if (_textHy < 0) t = Math.Min(t + dy, b - 1);
            if (_textHy > 0) b = Math.Max(b + dy, t + 1);
            s.Effect = e0 with
            {
                X = l,
                Y = e0.Y + (t - f.Top),
                BoxWidth = r - l,
                BoxHeight = _textHy != 0 ? b - t : e0.BoxHeight,
            };
            if (_textHy != 0)
            {
                s.Effect = s.Effect with { Size = TextLayout.FitSize(s.Effect, b - t) };
                // 세션이 바꾼 설정이므로 고치는 글씨에 다시 넣지 않는다(TextStyleChanged).
                _textBusy = true;
                try
                {
                    _ctx.TextStyle = TextStyle.From(s.Effect);
                }
                finally
                {
                    _textBusy = false;
                }
            }
        }
        ApplyTextPreview(s);
    }

    // ───────────── 키·글자 입력 ─────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _text is not { } s) return;
        if (HandleTextKey(s, e.Key, e.KeyModifiers)) e.Handled = true;
    }

    /// <summary>
    /// 고치는 중의 키: Esc·Ctrl(⌘)+Enter = 끝내기, Enter = 줄 바꿈, Tab = 탭, 지우기(Ctrl/⌥ = 낱말), 방향키·Home·End(Shift = 고르기,
    /// Ctrl/⌥ = 낱말, ⌘ = 줄 끝·글 끝), Ctrl(⌘)+A/C/X/V. 글자는 <see cref="OnTextInput"/>으로 온다. 처리했으면 true.
    /// </summary>
    private bool HandleTextKey(TextSession s, Key key, KeyModifiers mods)
    {
        // 입력기가 글자를 조합하는 중이면 키는 입력기가 처리한다.
        if (s.Preedit.Length > 0 || key == Key.ImeProcessed) return false;
        bool shift = (mods & KeyModifiers.Shift) != 0;
        bool command = Ui.HasCommand(mods);
        bool word = Platform.IsMac ? (mods & KeyModifiers.Alt) != 0 : (mods & KeyModifiers.Control) != 0;
        var text = s.Effect.Text;
        switch (key)
        {
            case Key.Escape:
            case Key.Enter when command:
                FinishTextEditing();
                return true;
            case Key.Enter:
                ReplaceTextSelection(s, "\n");
                return true;
            case Key.Tab when (mods & ~KeyModifiers.Shift) == 0 && !shift:
                ReplaceTextSelection(s, "\t");
                return true;
            case Key.Back or Key.Delete:
            {
                if (s.Caret == s.Anchor)
                {
                    bool back = key == Key.Back;
                    s.Anchor = back ? word ? WordLeft(text, s.Caret) : PrevTextIndex(text, s.Caret)
                        : word ? WordRight(text, s.Caret) : NextTextIndex(text, s.Caret);
                }
                ReplaceTextSelection(s, "");
                return true;
            }
            case Key.Left or Key.Right:
            {
                bool left = key == Key.Left;
                int target;
                if (Platform.IsMac && command) target = TextLineEdge(s, left);
                else if (word) target = left ? WordLeft(text, s.Caret) : WordRight(text, s.Caret);
                else if (s.Caret != s.Anchor && !shift) target = left ? s.SelectionStart : s.SelectionEnd;
                else target = left ? PrevTextIndex(text, s.Caret) : NextTextIndex(text, s.Caret);
                MoveTextCaret(s, target, shift);
                return true;
            }
            case Key.Up or Key.Down:
                if (Platform.IsMac && command) MoveTextCaret(s, key == Key.Up ? 0 : text.Length, shift);
                else MoveTextCaretLine(s, key == Key.Up ? -1 : 1, shift);
                return true;
            case Key.Home or Key.End:
                if (command) MoveTextCaret(s, key == Key.Home ? 0 : text.Length, shift);
                else MoveTextCaret(s, TextLineEdge(s, key == Key.Home), shift);
                return true;
            case Key.A when command:
                s.Anchor = 0;
                MoveTextCaret(s, text.Length, extend: true);
                return true;
            case Key.C when command:
                CopyTextSelection(s);
                return true;
            case Key.X when command:
                CopyTextSelection(s);
                ReplaceTextSelection(s, "");
                return true;
            case Key.V when command:
                PasteText(s);
                return true;
        }
        return false;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_text is not { } s || string.IsNullOrEmpty(e.Text)) return;
        e.Handled = true;
        // 조합이 끝나 확정된 글자가 온다 — 조합 중인 글자는 지운다. 제어 문자(Enter·Tab 등은 키로 처리)는 뺀다.
        s.Preedit = "";
        s.PreeditCursor = 0;
        var typed = string.Concat(e.Text.Where(c => !char.IsControl(c)));
        if (typed.Length > 0) ReplaceTextSelection(s, typed);
        else ApplyTextPreview(s);
    }

    /// <summary>입력기의 조합 중인 글자(<paramref name="text"/>, 비면 조합 끝)와 그 안의 커서. 조합을 시작하면 고른 글은 먼저 지운다.</summary>
    private void SetTextPreedit(string? text, int? cursor)
    {
        if (_text is not { } s) return;
        text ??= "";
        if (text.Length > 0 && s.Preedit.Length == 0 && s.Caret != s.Anchor) ReplaceTextSelection(s, "", preview: false);
        if (text == s.Preedit && (cursor ?? text.Length) == s.PreeditCursor) return;
        s.Preedit = text;
        s.PreeditCursor = Math.Clamp(cursor ?? text.Length, 0, text.Length);
        ApplyTextPreview(s);
    }

    private void CopyTextSelection(TextSession s)
    {
        if (s.Caret == s.Anchor || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        _ = clipboard.SetTextAsync(s.Effect.Text[s.SelectionStart..s.SelectionEnd]);
    }

    /// <summary>클립보드 글을 커서 자리(고른 글은 바꿈)에 넣는다(읽는 사이 세션이 바뀌었으면 버림).</summary>
    private async void PasteText(TextSession s)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        string? text;
        try
        {
            text = await clipboard.TryGetTextAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return;
        }
        if (string.IsNullOrEmpty(text) || !ReferenceEquals(_text, s)) return;
        ReplaceTextSelection(s, text);
    }

    /// <summary>커서가 있는 줄의 처음(<paramref name="start"/>) 또는 끝 위치.</summary>
    private static int TextLineEdge(TextSession s, bool start)
    {
        var lines = s.Layout.Lines;
        if (lines.Count == 0) return start ? 0 : s.Effect.Text.Length;
        var line = lines[TextLineOf(s.Layout, s.Caret)];
        return start ? line.Start : line.Start + line.Length;
    }

    /// <summary>위·아래 줄로 커서를 옮긴다(가로 위치는 처음 옮길 때의 커서 위치를 지킴). 첫 줄 위·끝 줄 아래는 글 처음·끝.</summary>
    private void MoveTextCaretLine(TextSession s, int dir, bool extend)
    {
        var layout = s.Layout;
        var lines = layout.Lines;
        int target = lines.Count == 0 ? -1 : TextLineOf(layout, s.Caret) + dir;
        if (target < 0 || target >= lines.Count)
        {
            MoveTextCaret(s, dir < 0 ? 0 : s.Effect.Text.Length, extend);
            return;
        }
        s.GoalX ??= layout.CaretRect(s.Caret).X;
        var line = lines[target];
        MoveTextCaret(s, layout.HitTest(new Point(s.GoalX.Value, line.Top + line.Height / 2)), extend, keepGoal: true);
    }

    /// <summary>커서(<paramref name="index"/>)가 그려지는 줄의 번호(커서 사각형의 위쪽 끝이 가장 가까운 줄).</summary>
    private static int TextLineOf(TextLayoutResult layout, int index)
    {
        var lines = layout.Lines;
        int y = layout.CaretRect(index).Y, best = 0;
        for (int i = 1; i < lines.Count; i++)
            if (Math.Abs(lines[i].Top - y) < Math.Abs(lines[best].Top - y)) best = i;
        return best;
    }

    private static int PrevTextIndex(string t, int i)
        => i <= 0 ? 0 : i >= 2 && char.IsLowSurrogate(t[i - 1]) && char.IsHighSurrogate(t[i - 2]) ? i - 2 : i - 1;

    private static int NextTextIndex(string t, int i)
        => i >= t.Length ? t.Length : i + 1 < t.Length && char.IsHighSurrogate(t[i]) && char.IsLowSurrogate(t[i + 1]) ? i + 2 : i + 1;

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || char.IsSurrogate(c);

    /// <summary>앞 낱말의 처음(사이 공백·기호는 건너뜀).</summary>
    private static int WordLeft(string t, int i)
    {
        while (i > 0 && !IsWordChar(t[i - 1])) i--;
        while (i > 0 && IsWordChar(t[i - 1])) i--;
        return i;
    }

    /// <summary>뒤 낱말의 끝(사이 공백·기호는 건너뜀).</summary>
    private static int WordRight(string t, int i)
    {
        while (i < t.Length && !IsWordChar(t[i])) i++;
        while (i < t.Length && IsWordChar(t[i])) i++;
        return i;
    }

    /// <summary>위치 <paramref name="i"/>의 낱말 범위(낱말 글자가 아니면 그 글자 하나, 글 끝이면 빈 범위).</summary>
    private static (int Start, int End) WordAt(string t, int i)
    {
        bool after = i < t.Length && IsWordChar(t[i]), before = i > 0 && IsWordChar(t[i - 1]);
        if (!after && !before) return (i, NextTextIndex(t, i));
        int start = i, end = i;
        while (start > 0 && IsWordChar(t[start - 1])) start--;
        while (end < t.Length && IsWordChar(t[end])) end++;
        return (start, end);
    }

    // ───────────── 그리기·커서 모양 ─────────────

    /// <summary>원본 기준 손잡이(-1/0/1)의 화면 위치(<paramref name="frame"/> = 화면 사각형).</summary>
    private static Avalonia.Point TextHandleScreen(Rect frame, int hx, int hy)
        => new(frame.X + (hx + 1) * frame.Width / 2, frame.Y + (hy + 1) * frame.Height / 2);

    /// <summary>쓸 수 있는 손잡이: 모서리 넷은 늘, 변 가운데는 그 변이 화면에서 24px 넘을 때만(작은 글씨에서 글자 위를 덮지 않게).</summary>
    private static bool TextHandleShown(Rect frame, int hx, int hy)
        => hx != 0 && hy != 0 || hx == 0 && frame.Width > 24 || hy == 0 && frame.Height > 24;

    private Rect TextFrameScreen(TextSession s) => ToRect(ImageToScreen(s.Layout.Frame));

    /// <summary>화면 위치가 가리키는 글씨 상자 부분: 손잡이 → 상자 안(작은 상자는 전부, 큰 상자는 테두리 띠 안쪽) → 테두리 띠 → 밖.</summary>
    private (TextBoxHit Kind, int Hx, int Hy) TextBoxHitAt(TextSession s, Point screen)
    {
        var r = TextFrameScreen(s);
        var at = new Avalonia.Point(screen.X, screen.Y);
        foreach (var (hx, hy) in TransformHandles)
        {
            if (!TextHandleShown(r, hx, hy)) continue;
            var h = TextHandleScreen(r, hx, hy);
            if (Math.Abs(at.X - h.X) <= TextHandleGrab && Math.Abs(at.Y - h.Y) <= TextHandleGrab) return (TextBoxHit.Handle, hx, hy);
        }
        if (!r.Inflate(TextEdgeGrab).Contains(at)) return (TextBoxHit.Outside, 0, 0);
        var inner = r.Width > 4 * TextEdgeGrab && r.Height > 4 * TextEdgeGrab ? r.Deflate(TextEdgeGrab) : r;
        return (inner.Contains(at) ? TextBoxHit.Inside : TextBoxHit.Edge, 0, 0);
    }

    /// <summary>글씨 도구 커서: 고치는 중이면 손잡이는 크기 조절 화살표, 테두리는 옮기기, 그 밖은 글자 커서. 글씨 도구가 아니면 null.</summary>
    private Cursor? TextToolCursor(Point screen)
    {
        if (_ctx.Tool != ToolKind.Text) return null;
        if (_text is not { } s || !ReferenceEquals(_ctx.Project, s.Project)) return IbeamCursor;
        var (kind, hx, hy) = _drag switch
        {
            DragMode.TextResize => (TextBoxHit.Handle, _textHx, _textHy),
            DragMode.TextMoveBox => (TextBoxHit.Edge, 0, 0),
            _ => TextBoxHitAt(s, screen),
        };
        return kind switch
        {
            TextBoxHit.Handle when hx == 0 => SizeNsCursor,
            TextBoxHit.Handle when hy == 0 => SizeWeCursor,
            TextBoxHit.Handle => hx == hy ? SizeNwseCursor : SizeNeswCursor,
            TextBoxHit.Edge => SizeAllCursor,
            _ => IbeamCursor,
        };
    }

    /// <summary>입력기 후보 창 위치: 커서(조합 중이면 조합 글자 안 커서) 사각형의 화면 좌표(이 컨트롤 기준).</summary>
    private Rect TextCaretScreenRect()
    {
        if (_text is not { } s) return default;
        var c = s.Layout.CaretRect(s.Caret + s.PreeditCursor);
        var tl = ImageToScreen(c.X, c.Y);
        return new Rect(tl.X, tl.Y, Math.Max(1, c.Width * _zoom), Math.Max(1, c.Height * _zoom));
    }

    /// <summary>새 글씨 끌기 사각형, 고치는 글씨의 고른 글·조합 밑줄·점선 상자·손잡이·커서.</summary>
    private void DrawTextEditing(DrawingContext g)
    {
        if (_drag == DragMode.TextCreate && _dragMoved)
        {
            var c = ToRect(ImageToScreen(TextCreateRect()));
            g.DrawRectangle(null, TextFrameBackPen, c);
            g.DrawRectangle(null, TextFramePen, c);
        }
        if (_text is not { } s || !ReferenceEquals(_ctx.Project, s.Project)) return;
        var layout = s.Layout;
        if (s.Caret != s.Anchor)
            foreach (var r in layout.SelectionRects(s.SelectionStart, s.SelectionEnd))
                g.FillRectangle(TextSelectionBrush, ToRect(ImageToScreen(r)));
        if (s.Preedit.Length > 0)
            foreach (var r in layout.SelectionRects(s.Caret, s.Caret + s.Preedit.Length))
            {
                var a = ImageToScreen(r.X, r.Bottom);
                var b = ImageToScreen(r.Right, r.Bottom);
                g.DrawLine(TextPreeditPen, new Avalonia.Point(a.X, a.Y - 1), new Avalonia.Point(b.X, b.Y - 1));
            }
        var frame = TextFrameScreen(s);
        var outline = new Rect(frame.X - 0.5, frame.Y - 0.5, frame.Width + 1, frame.Height + 1);
        g.DrawRectangle(null, TextFrameBackPen, outline);
        g.DrawRectangle(null, TextFramePen, outline);
        foreach (var (hx, hy) in TransformHandles)
        {
            if (!TextHandleShown(frame, hx, hy)) continue;
            var h = TextHandleScreen(frame, hx, hy);
            g.DrawRectangle(TextHandleBrush, TextHandlePen, new Rect(h.X - 3.5, h.Y - 3.5, 7, 7));
        }
        var caret = TextCaretScreenRect();
        if (_caretOn && IsFocused)
        {
            double w = Math.Clamp(_zoom / 4.0, 1, 3);
            g.FillRectangle(TextCaretBackBrush, new Rect(caret.X - 1, caret.Y - 1, w + 2, caret.Height + 2));
            g.FillRectangle(TextCaretBrush, new Rect(caret.X, caret.Y, w, caret.Height));
        }
        if (caret != _reportedCaret)
        {
            _reportedCaret = caret;
            Dispatcher.UIThread.Post(() => _textInputClient?.NotifyCaretMoved());
        }
    }

    /// <summary>
    /// 캔버스 글씨 편집의 입력기(IME) 연결: 조합 중인 글자를 캔버스 글씨에 그대로 보이고(<see cref="SetTextPreedit"/>),
    /// 후보 창이 커서 옆에 뜨도록 커서 사각형을 알린다. 확정된 글자는 글자 입력 이벤트(<see cref="OnTextInput"/>)로 온다.
    /// </summary>
    private sealed class CanvasTextInput(PixelCanvas canvas) : TextInputMethodClient
    {
        public override Visual TextViewVisual => canvas;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => false;
        public override string SurroundingText => "";
        public override Rect CursorRectangle => canvas.TextCaretScreenRect();

        public override TextSelection Selection
        {
            get => canvas._text is { } s ? new TextSelection(s.Caret, s.Caret) : default;
            set { }
        }

        public override void SetPreeditText(string? preeditText) => canvas.SetTextPreedit(preeditText, null);

        public override void SetPreeditText(string? preeditText, int? cursorPos) => canvas.SetTextPreedit(preeditText, cursorPos);

        public void NotifyCaretMoved() => RaiseCursorRectangleChanged();
    }

    // ───────────── 이동 도구: 글씨 레이어 ─────────────

    /// <summary>
    /// 현재 레이어가 글씨 레이어(그룹이 아니고, 현재 셀이 없거나 모두 투명하며, 글씨 효과가 있음)면 그 효과 목록, 아니면 null.
    /// 이때 이동 도구는 셀 대신 글씨 효과의 X·Y를 옮긴다.
    /// </summary>
    private IReadOnlyList<LayerEffect>? TextLayerEffects(SpriteProject p)
    {
        int index = _ctx.CurrentLayer;
        if (index < 0 || index >= p.Layers.Count) return null;
        var layer = p.Layers[index];
        if (layer.IsGroup || !layer.Effects.Any(e => e is TextEffect) || !p.IsCelEmpty(index, _ctx.CurrentFrame)) return null;
        return layer.Effects;
    }

    /// <summary>모든 글씨 효과의 기준점(상자면 상자 왼쪽 위)을 (<paramref name="dx"/>, <paramref name="dy"/>)만큼 옮긴 효과 목록.</summary>
    private static LayerEffect[] ShiftText(IReadOnlyList<LayerEffect> effects, int dx, int dy)
        => effects.Select(e => e is TextEffect t ? t with { X = t.X + dx, Y = t.Y + dy } : e).ToArray();

    /// <summary>이동 도구 누르기가 글씨 레이어 옮기기로 시작했으면 true(잠겨서 알림만 보였어도 true).</summary>
    private bool BeginMoveText()
    {
        if (_ctx.Project is not { } p || TextLayerEffects(p) is not { } effects) return false;
        if (!_ctx.CanEdit)
        {
            Message?.Invoke(this, UiStrings.S_PixelCanvas_BeginMoveLayer_95920E31EA);
            return true;
        }
        _ctx.CommitFloating();
        _textMoveLayer = p.Layers[_ctx.CurrentLayer];
        _textMoveOriginal = effects;
        _drag = DragMode.MoveText;
        return true;
    }

    /// <summary>끄는 동안: 실행취소 기록 없이 옮긴 글씨를 미리 보인다.</summary>
    private void PreviewTextMove(int dx, int dy)
    {
        if (_ctx.Project is not { } p || _textMoveLayer is null || _textMoveOriginal is not { } original) return;
        int index = p.IndexOf(_textMoveLayer);
        if (index >= 0) p.PreviewLayerEffects(index, ShiftText(original, dx, dy));
    }

    /// <summary>놓으면: 미리보기를 되돌리고 옮긴 만큼을 실행취소 한 단계로 기록한다(움직이지 않았으면 기록 없음).</summary>
    private void EndTextMove(int dx, int dy)
    {
        var layer = _textMoveLayer;
        var original = _textMoveOriginal;
        _textMoveLayer = null;
        _textMoveOriginal = null;
        if (_ctx.Project is not { } p || layer is null || original is null) return;
        int index = p.IndexOf(layer);
        if (index < 0) return;
        p.EndLayerEffectsPreview(index, original, commit: false);
        if (dx == 0 && dy == 0) return;
        var moved = ShiftText(original, dx, dy);
        _ctx.Structural(() => p.Edit(UiStrings.TextTool_MoveLabel, ProjectChange.Layers, () => p.SetLayerEffects(index, moved)));
    }

    /// <summary>이동 도구 방향키: 글씨 레이어면 글씨를 옮기고 true(실행취소 한 단계).</summary>
    private bool NudgeText(int dx, int dy)
    {
        if (_ctx.Project is not { } p || TextLayerEffects(p) is not { } effects) return false;
        int index = _ctx.CurrentLayer;
        var moved = ShiftText(effects, dx, dy);
        _ctx.Structural(() => p.Edit(UiStrings.TextTool_MoveLabel, ProjectChange.Layers, () => p.SetLayerEffects(index, moved)));
        return true;
    }
}
