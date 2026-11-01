using Avalonia.Controls.Primitives.PopupPositioning;
using Chickxel.Core.Model;
using Chickxel.Core.Text;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 글씨 도구와 글씨 레이어 옮기기.
/// 글씨 도구로 캔버스를 누르면 그 자리에 글 입력 칸이 뜨고, 확정하면 글씨 효과(<see cref="TextEffect"/>)가 붙은 빈 일반 레이어를
/// 현재 레이어(부위면 그 그룹) 바로 위에 만든다(실행취소 한 단계). 글씨가 그려진 곳을 누르면 그 글씨 효과의 글을 고친다.
/// 이동 도구는 선택이 없고 현재 셀이 비어 있는 글씨 레이어에서 셀 대신 글씨 효과의 X·Y를 옮긴다(끄는 동안 미리보기, 놓으면 한 단계).
/// </summary>
public sealed partial class PixelCanvas
{
    /// <summary>
    /// 열린 글 입력 칸. <paramref name="Layer"/>가 있으면 그 레이어의 <paramref name="EffectIndex"/>번 글씨 효과를 고치고,
    /// 없으면 <paramref name="At"/>(프레임 캔버스 좌표)에 새 글씨 레이어를 <paramref name="Anchor"/>(부위면 그 그룹) 바로 위에 만든다.
    /// </summary>
    private sealed record TextEntry(SpriteProject Project, Popup Popup, TextBox Box, Layer? Layer, int EffectIndex, Point At, Layer Anchor);

    private TextEntry? _textInput;
    /// <summary>입력 칸이 포커스를 잃어 확정된 때(<see cref="Environment.TickCount64"/>, 없으면 null): 그 포커스를 가져간 캔버스 누르기를 새 입력으로 쓰지 않는다.</summary>
    private long? _textInputLostAt;
    /// <summary>입력 칸 밖 누르기로 확정한 직후 이 시간(ms) 안에 온 캔버스 누르기는 확정에만 쓴다.</summary>
    private const int TextInputClickGraceMs = 300;

    /// <summary>이동 도구로 끄는 글씨 레이어와 끌기 전 효과 목록(끄는 중이 아니면 null).</summary>
    private Layer? _textMoveLayer;
    private IReadOnlyList<LayerEffect>? _textMoveOriginal;

    /// <summary>캔버스 누르기가 열린 입력 칸을 확정하는 데 쓰였는지(쓰였으면 다른 일을 하지 않는다).</summary>
    private bool ConsumeTextInputClick()
    {
        if (_textInput is not null)
        {
            FinishTextInput(commit: true);
            return true;
        }
        if (_textInputLostAt is not { } lostAt) return false;
        _textInputLostAt = null;
        return Environment.TickCount64 - lostAt < TextInputClickGraceMs;
    }

    // ───────────── 글씨 도구 ─────────────

    /// <summary>글씨 도구 누르기: 그 자리의 글씨를 고치거나 새 글씨 입력 칸을 연다.</summary>
    private void BeginTextInput(Point px)
    {
        if (_ctx.Project is not { } p || _ctx.CurrentLayerObject is not { IsGroup: false } current) return;
        _ctx.CommitFloating();
        if (TextAt(p, current, px) is { } hit)
        {
            int index = p.IndexOf(hit.Layer);
            if (p.IsLayerLocked(index))
            {
                Message?.Invoke(this, UiStrings.TextTool_Locked);
                return;
            }
            if (index != _ctx.CurrentLayer) _ctx.SetCurrentLayer(index);
            var effect = (TextEffect)hit.Layer.Effects[hit.Index];
            var bounds = TextLayout.Bounds(effect);
            OpenTextInput(p, hit.Layer, hit.Index, bounds.Location, current, effect.Text);
            return;
        }
        // 부위 화면의 좌표는 부위 안 좌표 — 새 레이어는 프레임 캔버스 좌표로 놓는다.
        var at = current.IsPart ? new Point(px.X + current.PartOffset.X, px.Y + current.PartOffset.Y) : px;
        OpenTextInput(p, null, -1, px, current, "", at);
    }

    /// <summary>
    /// 누른 곳(<paramref name="px"/>, 지금 캔버스 좌표)에 글씨가 그려진 글씨 효과: 현재 레이어를 먼저, 현재 레이어가 일반 레이어면
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

    /// <summary>레이어에서 현재 프레임에 켜진 글씨 효과 중 <paramref name="px"/>를 덮는 맨 뒤(위에 그려지는) 것의 번호.</summary>
    private int? HitText(Layer layer, Point px)
    {
        for (int i = layer.Effects.Count - 1; i >= 0; i--)
        {
            if (layer.Effects[i] is not TextEffect { Enabled: true } t || t.Frames is { } r && !r.Contains(_ctx.CurrentFrame)) continue;
            var b = TextLayout.Bounds(t);
            // 1px 둘레까지 눌러도 잡히게(얇은 글꼴).
            if (!b.IsEmpty && Rectangle.Inflate(b, 1, 1).Contains(px)) return i;
        }
        return null;
    }

    /// <summary>
    /// 글 입력 칸을 캔버스 좌표 <paramref name="screenAt"/>(지금 캔버스 좌표의 왼쪽 위)에 띄운다.
    /// Enter = 확정, Shift+Enter = 줄 바꿈, Esc = 취소, 포커스를 잃으면 확정(빈 글이면 취소).
    /// </summary>
    private void OpenTextInput(SpriteProject p, Layer? layer, int effectIndex, Point screenAt, Layer anchor, string text, Point? at = null)
    {
        var box = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MaxLength = TextEffect.MaxLength,
            MinWidth = 160,
            MaxWidth = 480,
            MaxHeight = 240,
            Watermark = UiStrings.TextTool_Watermark,
        };
        var popup = new Popup
        {
            Child = box,
            PlacementTarget = this,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(ImageToScreen(screenAt.X, screenAt.Y), new Avalonia.Size(1, 1)),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
            IsLightDismissEnabled = false,
        };
        var input = new TextEntry(p, popup, box, layer, effectIndex, at ?? screenAt, anchor);
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!ReferenceEquals(_textInput, input)) return;
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    FinishTextInput(commit: false);
                    break;
                case Key.Enter when (e.KeyModifiers & KeyModifiers.Shift) != 0:
                    e.Handled = true;
                    InsertNewLine(box);
                    break;
                case Key.Enter:
                    e.Handled = true;
                    FinishTextInput(commit: true);
                    break;
            }
        }, RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) =>
        {
            if (!ReferenceEquals(_textInput, input)) return;
            _textInputLostAt = Environment.TickCount64;
            FinishTextInput(commit: true);
        };
        // 누르기 처리가 끝난 뒤 포커스를 옮긴다(누르기가 캔버스에 준 포커스에 바로 빼앗기지 않게).
        popup.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_textInput, input)) return;
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Background);
        _textInput = input;
        LogicalChildren.Add(popup);
        popup.IsOpen = true;
    }

    /// <summary>입력 칸 커서 자리(고른 글은 바꿈)에 줄 바꿈 하나(최대 길이 안에서).</summary>
    private static void InsertNewLine(TextBox box)
    {
        var text = box.Text ?? "";
        int start = Math.Min(box.SelectionStart, box.SelectionEnd), end = Math.Max(box.SelectionStart, box.SelectionEnd);
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        if (text.Length - (end - start) + 1 > TextEffect.MaxLength) return;
        box.Text = string.Concat(text.AsSpan(0, start), "\n", text.AsSpan(end));
        box.SelectionStart = box.SelectionEnd = box.CaretIndex = start + 1;
    }

    /// <summary>열린 입력 칸을 닫는다. <paramref name="commit"/>이면 글씨 레이어를 만들거나 고친다(실행취소 한 단계).</summary>
    private void FinishTextInput(bool commit)
    {
        if (_textInput is not { } input) return;
        _textInput = null;
        input.Popup.IsOpen = false;
        LogicalChildren.Remove(input.Popup);
        var text = (input.Box.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        if (commit && !string.IsNullOrWhiteSpace(text) && ReferenceEquals(_ctx.Project, input.Project))
        {
            if (input.Layer is { } layer) CommitTextEdit(input.Project, layer, input.EffectIndex, text);
            else CommitNewText(input.Project, input.Anchor, input.At, text);
        }
        Focus();
    }

    /// <summary>글씨 효과 하나의 글을 바꾼다(실행취소 한 단계, 그대로면 기록 없음).</summary>
    private void CommitTextEdit(SpriteProject p, Layer layer, int effectIndex, string text)
    {
        int index = p.IndexOf(layer);
        if (index < 0 || effectIndex >= layer.Effects.Count || layer.Effects[effectIndex] is not TextEffect old || p.IsLayerLocked(index)) return;
        var edited = old with { Text = text };
        if (edited.Text == old.Text) return;
        var effects = layer.Effects.ToArray();
        effects[effectIndex] = edited;
        _ctx.Structural(() => p.Edit(UiStrings.TextTool_EditLabel, ProjectChange.Layers, () => p.SetLayerEffects(index, effects)));
    }

    /// <summary>
    /// 새 글씨 레이어: <paramref name="anchor"/>(부위면 그 그룹) 바로 위에 빈 일반 레이어를 넣고 글씨 효과 하나를 붙인 뒤 고른다(실행취소 한 단계).
    /// 글은 <paramref name="at"/>(프레임 캔버스 좌표)이 첫 줄 왼쪽 위가 되게 놓고, 색은 기본색(투명이면 검정), 글꼴은 도구 설정.
    /// </summary>
    private void CommitNewText(SpriteProject p, Layer anchor, Point at, string text)
    {
        int top = p.IndexOf(anchor.Parent ?? anchor);
        int slot = top < 0 ? p.Layers.Count : top + 1;
        var effect = new TextEffect
        {
            Text = text,
            Font = _ctx.TextFont,
            Color = _ctx.Primary.A == 0 ? Color32.Black : _ctx.Primary,
            X = at.X,
            Y = at.Y,
        };
        Layer? added = null;
        _ctx.Structural(() => p.Edit(UiStrings.TextTool_AddLabel, ProjectChange.Layers, () =>
        {
            added = p.AddLayer(slot, TextLayerName(text));
            p.SetLayerEffects(p.IndexOf(added), [effect]);
        }));
        if (added is not null) _ctx.SetCurrentLayer(p.IndexOf(added));
    }

    /// <summary>새 글씨 레이어 이름: 글 첫 줄(앞뒤 공백 뺌)을 24자까지, 비었으면 "글씨".</summary>
    private static string TextLayerName(string text)
    {
        var first = text.Split('\n')[0].Trim();
        if (first.Length == 0) return UiStrings.TextTool_DefaultLayerName;
        return first.Length > 24 ? first[..24].TrimEnd() : first;
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

    /// <summary>모든 글씨 효과의 기준점을 (<paramref name="dx"/>, <paramref name="dy"/>)만큼 옮긴 효과 목록.</summary>
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
