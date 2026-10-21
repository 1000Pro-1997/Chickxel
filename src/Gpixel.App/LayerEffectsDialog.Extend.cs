using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 효과 창의 프레임 늘리기: 고른 효과 범위의 "프레임 수"를 범위 길이보다 크게 하면(또는 프레임 줄에서 범위 끝을 마지막 칸 너머로 끌면)
/// 확인할 때 프로젝트에 프레임을 더한다(<see cref="SpriteProject.ExtendFrames(FrameExtension)"/> + 효과 확정 = 실행취소 한 단계).
/// 그때까지 미리보기·프레임 줄은 늘린 뒤의 차례(가상 프레임)를 보인다. 한 번에 한 범위만 늘린다.
/// 창 안의 효과 범위(<see cref="_effects"/>)는 원래 프레임 번호 그대로 두고, 보일 때·확정할 때 <see cref="FrameExtension.MapRange"/>로 옮긴다
/// (캔버스 미리보기는 원래 프레임에 그대로).
/// </summary>
public sealed partial class LayerEffectsDialog
{
    /// <summary>늘리기 상자 항목: 0 = 원래 프레임으로(범위 뒤에 있는 프레임으로 범위만 넓힘), 1~ = <see cref="FrameExtendMode"/>(프레임을 더함).</summary>
    private static readonly string[] ExtendModes =
        ["원래 프레임으로", .. Enum.GetValues<FrameExtendMode>().Select(FrameExtension.ModeName)];
    /// <summary>프레임을 더하는 늘리기 방법(앱을 켜 둔 동안 유지).</summary>
    private static FrameExtendMode s_extendMode = FrameExtendMode.Stretch;
    /// <summary>"원래 프레임으로"를 직접 골랐는지(null = 정하지 않음 → 범위 뒤에 프레임이 있으면 원래 프레임으로).</summary>
    private bool? _preferExisting;

    /// <summary>확인할 때 할 프레임 늘리기(null = 늘리지 않음, 원래 프레임 번호 기준).</summary>
    private FrameExtension? _extension;
    private NumericUpDown? _extendCount;
    private ComboBox? _extendModeBox;
    private TextBlock? _extendNote;
    /// <summary>프레임 수 칸이 보이는 효과 번호.</summary>
    private int _extendFor = -1;
    private bool _extendSyncing;
    /// <summary>프레임 줄을 누른 때의 늘리기와 칸 수(끄는 동안 칸 번호를 그때 차례로 읽는다).</summary>
    private (FrameExtension? Extension, int Cells) _dragBase;

    /// <summary>늘린 뒤(미리보기) 프레임 수.</summary>
    private int VirtualCount => _project.Frames.Count + (_extension?.Added ?? 0);

    /// <summary>미리보기 프레임 번호 → 원래 프레임 번호.</summary>
    private int OriginalOf(int frame) => _extension?.OriginalFrame(frame) ?? frame;

    /// <summary>원래 프레임 번호 → 미리보기 프레임 번호.</summary>
    private int VirtualOf(int original) => _extension?.ExtendedFrame(original) ?? original;

    private bool IsCopyFrame(int frame) => _extension?.IsCopy(frame) ?? false;

    /// <summary>효과의 범위(늘린 뒤 번호).</summary>
    private (int From, int To) VRangeOf(LayerEffect e)
    {
        var (from, to) = RangeOf(e);
        if (_extension is not { } x) return (from, to);
        var r = x.MapRange(new FrameRange(from, to));
        return (r.From, r.To);
    }

    /// <summary>효과 목록(범위를 늘린 뒤 번호로) — 미리보기와 확정에 쓴다.</summary>
    private IReadOnlyList<LayerEffect> VirtualEffects()
        => _extension is not { } x ? _effects : _effects.Select(e => e.Frames is { } r ? e with { Frames = x.MapRange(r) } : e).ToArray();

    private PixelImage? VirtualSourceAt(int frame) => SourceAt(OriginalOf(frame));

    /// <summary>미리보기 프레임 <paramref name="frame"/>의 원본과 그 프레임 범위 안에 있는 효과(<paramref name="effects"/> = <see cref="VirtualEffects"/>).</summary>
    private (PixelImage? Source, IReadOnlyList<LayerEffect> Effects) AnimateAt(int frame, IReadOnlyList<LayerEffect> effects)
    {
        int original = OriginalOf(frame);
        if (SourceAt(original) is not { } source) return (null, []);
        return (source, LayerEffects.At(effects, frame, VirtualCount, _project.EffectPivot(_layer, original),
            LayerEffects.UsesOtherFrames(effects) ? VirtualSourceAt : null));
    }

    // ───────────── 설정 칸 ─────────────

    /// <summary>
    /// 범위 줄 아래: 프레임 수와 늘리기 방법. "원래 프레임으로"면 프레임 수 = 범위 길이(범위 뒤에 이미 있는 프레임까지만 — 끝을 옮김, 프레임은 더하지 않음),
    /// 다른 방법이면 범위 길이보다 크게 했을 때 확인할 때 그만큼 프레임을 더한다.
    /// </summary>
    private void AddExtendEditor(int i)
    {
        var (from, to) = RangeOf(_effects[i]);
        int n = to - from + 1;
        var count = Ui.Num(1, Math.Max(n, FrameExtension.MaxCount), n, 56);
        var mode = Ui.Combo(170, ExtendModes);
        var note = Ui.Label("");
        note.Foreground = AppTheme.DimTextBrush;
        _extendCount = count;
        _extendModeBox = mode;
        _extendNote = note;
        _extendFor = i;
        SyncExtendEditor();
        count.ValueChanged += (_, _) =>
        {
            if (_extendSyncing || _extendFor < 0 || _extendFor >= _effects.Count) return;
            var e = _effects[_extendFor];
            var (f, t) = RangeOf(e);
            int c = (int)count.NumValue();
            if (UsesExisting(e)) SetRangeLength(_extendFor, c);
            // 칸은 다른 범위의 늘리기로 늘어 보이는 길이(MaxCount 넘을 수 있음)까지 보이므로, 이 범위의 새 늘리기는 늘릴 수 있는 수로 맞춘다
            else SetExtension(new FrameExtension(f, t, FrameExtension.ClampCount(t - f + 1, c), s_extendMode));
        };
        mode.SelectionChanged += (_, _) =>
        {
            if (_extendSyncing || mode.SelectedIndex < 0 || _extendFor < 0 || _extendFor >= _effects.Count) return;
            var e = _effects[_extendFor];
            var anchored = Anchored(e);
            if (mode.SelectedIndex == 0)
            {
                _preferExisting = true;
                // 더하려던 프레임 수만큼 뒤의 원래 프레임으로(있는 만큼)
                if (anchored is { } x) SetRangeLength(_extendFor, x.Count);
                else SyncExtendEditor();
                return;
            }
            _preferExisting = false;
            s_extendMode = (FrameExtendMode)(mode.SelectedIndex - 1);
            if (anchored is { } a) SetExtension(new FrameExtension(a.From, a.To, a.Count, s_extendMode));
            else SyncExtendEditor();
        };
        ToolTip.SetTip(count, "이 효과 범위의 프레임 수. 원래 프레임으로: 범위 끝을 뒤에 이미 있는 프레임까지 넓힙니다(프레임은 그대로)."
                              + " 다른 방법: 범위 길이보다 크게 하면 확인할 때 그만큼 프레임을 더해 애니메이션을 늘립니다(모든 프레임이면 애니메이션 전체)."
                              + " 미리보기는 늘린 뒤 모습입니다. 프레임 줄에서 범위 끝을 마지막 칸 너머로 끌어도 됩니다.");
        ToolTip.SetTip(mode, "늘리는 방법 — 원래 프레임으로: 범위 뒤에 있는 프레임을 그대로 씀(끝 프레임까지) · 같은 비율로: 프레임마다 고르게 되풀이 · "
                             + "첫 프레임으로: 첫 프레임을 앞에 더 둠 · 마지막 프레임으로: 마지막 프레임을 뒤에 더 둠 · 반복: 1, 2, …, n을 되풀이.");
        SettingsRow("프레임 수", count, Ui.Label("프레임"), note);
        SettingsSubRow("늘리기", mode);
    }

    /// <summary>늘리기가 이 효과 범위에 걸려 있으면 그 늘리기.</summary>
    private FrameExtension? Anchored(LayerEffect e) => _extension is { } x && RangeOf(e) == (x.From, x.To) ? x : null;

    /// <summary>
    /// 효과의 프레임 수 칸이 "원래 프레임으로"인지: 그 범위에 늘리기가 걸려 있으면 아니고, 직접 고른 적이 있으면 그것,
    /// 아니면 범위 뒤에 프레임이 있을 때.
    /// </summary>
    private bool UsesExisting(LayerEffect e)
        => Anchored(e) is null && (_preferExisting ?? RangeOf(e).To < _project.Frames.Count - 1);

    /// <summary>"원래 프레임으로": 효과 범위를 처음 프레임부터 <paramref name="length"/>개로(마지막 프레임까지) — 범위에 걸린 늘리기는 없앤다.</summary>
    private void SetRangeLength(int i, int length)
    {
        var e = _effects[i];
        int n = _project.Frames.Count;
        var (from, _) = RangeOf(e);
        _preferExisting = true;
        // 늘리기를 없애면 칸 수가 바뀐다 — 범위가 그대로여도(Update가 아무것도 안 함) 프레임 줄·미리보기를 맞춘다.
        if (Anchored(e) is not null) SetExtension(null);
        Update(i, e with { Frames = FrameRange.Of(from, Math.Clamp(from + length - 1, from, n - 1), n) });
        ShowSettings(); // 범위 숫자 칸·범위 길이 안내 갱신
        SyncPlayback();
    }

    /// <summary>프레임 수 칸·방법 상자·안내를 지금 범위·늘리기에 맞춘다(효과 범위·늘리기가 바뀔 때).</summary>
    private void SyncExtendEditor()
    {
        if (_extendCount is not { } count || _extendModeBox is not { } mode || _extendNote is not { } note
            || _extendFor < 0 || _extendFor >= _effects.Count) return;
        var e = _effects[_extendFor];
        var (from, to) = RangeOf(e);
        var (vFrom, vTo) = VRangeOf(e);
        int n = to - from + 1, now = vTo - vFrom + 1, frames = _project.Frames.Count, after = frames - 1 - to;
        bool existing = UsesExisting(e);
        _extendSyncing = true;
        if (existing)
        {
            WithKind(count, CountKind(n, frames - from));
            // 최소를 먼저 1로: 최대를 먼저 줄이면 그때의 최소(다른 방법일 때의 범위 길이) 아래로 못 내려가 큰 최대가 남는다(NumericUpDown 강제 조정).
            count.Minimum = 1;
            count.Maximum = Math.Max(n, frames - from);
            count.Value = n;
            mode.SelectedIndex = 0;
            note.Text = after > 0 ? $"뒤에 프레임 {after}개 있음" : "뒤에 프레임 없음 — 더하려면 다른 방법";
        }
        else
        {
            WithKind(count, CountKind(n, null));
            count.Maximum = Math.Max(Math.Max(n, now), FrameExtension.MaxCount);
            count.Minimum = n;
            count.Value = now;
            mode.SelectedIndex = (int)(Anchored(e)?.Mode ?? s_extendMode) + 1;
            note.Text = Anchored(e) is { } x ? $"확인하면 프레임 {x.Added}개 더함" : "";
        }
        _extendSyncing = false;
    }

    /// <summary>프레임 수 칸 예시: 그대로(n)·×2·×3과 흔한 길이(8·12·16·24·32), "원래 프레임으로"면 끝 프레임까지(<paramref name="toEnd"/>)도.</summary>
    private static NumKind CountKind(int n, int? toEnd)
    {
        var values = new SortedSet<decimal> { n, n * 2, n * 3 };
        foreach (var v in new[] { 1, 8, 12, 16, 24, 32 })
            if (v > n || toEnd is not null) values.Add(v);
        if (toEnd is { } end) values.Add(end);
        values.RemoveWhere(v => v > (toEnd ?? Math.Max(n, FrameExtension.MaxCount)));
        return new("effect.frameCount", "프레임", [.. values],
            v => v == toEnd && v != n ? $"{v}프레임 (끝 프레임까지)" : v == n ? $"{v}프레임 (그대로)" : v == n * 2 ? $"{v}프레임 (×2)" : v == n * 3 ? $"{v}프레임 (×3)" : $"{v}프레임");
    }

    /// <summary>늘리기를 바꾸고(같은 수면 늘리지 않음) 미리보기·프레임 줄·설정 칸을 맞춘다.</summary>
    private void SetExtension(FrameExtension? extension)
    {
        var next = extension is { Added: > 0 } ? extension : null;
        if (next == _extension) return;
        int original = OriginalOf(_previewFrame);
        _extension = next;
        _previewFrame = Math.Clamp(VirtualOf(original), 0, VirtualCount - 1);
        SyncExtendEditor();
        InvalidateStrip();
        RefreshPreview();
        SyncPlayback();
    }

    /// <summary>
    /// 범위 숫자 칸으로 효과 범위를 바꾸기 전: 늘리기가 그 효과 범위에 걸려 있었으면 새 범위로 옮긴다(프레임 수 그대로, 새 범위보다 작으면 늘리기 없음).
    /// </summary>
    private void FollowRange(LayerEffect old, FrameRange? next)
    {
        if (_extension is not { } x || RangeOf(old) != (x.From, x.To)) return;
        int n = _project.Frames.Count;
        var (from, to) = next is { } r ? (r.From, Math.Min(r.To, n - 1)) : (0, n - 1);
        _extension = x.Count > to - from + 1 ? new FrameExtension(from, to, x.Count, x.Mode) : null;
        _previewFrame = Math.Clamp(_previewFrame, 0, VirtualCount - 1);
    }

    /// <summary>
    /// 프레임 줄에서 칸 <paramref name="a"/> ~ <paramref name="b"/>를 끔(누른 때의 칸 차례 기준, <paramref name="b"/>가 칸 수 이상 = 마지막 칸 너머의 빈 칸).
    /// 원래 프레임 범위로 바꿔 고른 효과(없으면 추가 범위)에 넣고, 마지막 칸 너머까지 끌었거나 늘어난 칸(사본)을 덮으면 고른 칸 수만큼 늘린다.
    /// </summary>
    private void DragRange(int a, int b)
    {
        var (ext, cells) = _dragBase;
        bool selected = Selected >= 0 && Selected < _effects.Count;
        int last = Math.Min(b, cells - 1);
        int lo = int.MaxValue, hi = -1;
        bool copies = false;
        for (int v = a; v <= last; v++)
        {
            int o = ext?.OriginalFrame(v) ?? v;
            lo = Math.Min(lo, o);
            hi = Math.Max(hi, o);
            copies |= ext?.IsCopy(v) ?? false;
        }
        if (hi < 0) return;
        var range = FrameRange.Of(lo, hi, _project.Frames.Count);
        if (!selected)
        {
            SetRangeFromStrip(range);
            return;
        }
        int n = hi - lo + 1;
        var next = _extension;
        if (b >= cells || copies)
            next = new FrameExtension(lo, hi, FrameExtension.ClampCount(n, b - a + 1), s_extendMode);
        else if (_extension is { } x && RangeOf(_effects[Selected]) == (x.From, x.To))
            next = null; // 늘린 범위를 원래 칸 안으로 다시 고름
        SetRangeFromStrip(range);
        SetExtension(next);
    }

    // ───────────── 확정 ─────────────

    /// <summary>
    /// 미리보기 끝(창이 닫힘): 확인이고 늘리기가 있으면 프레임을 늘리고 효과(늘린 뒤 범위)를 넣는다(실행취소 한 단계), 아니면 효과만 확정·취소.
    /// </summary>
    private void EndPreview(int index, bool commit)
    {
        if (!commit || _extension is not { } x)
        {
            _project.EndLayerEffectsPreview(index, _original, commit);
            return;
        }
        var list = VirtualEffects();
        _project.EndLayerEffectsPreview(index, _original, commit: false);
        _project.Edit("레이어 효과 · 프레임 늘리기", ProjectChange.Layers | ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () =>
        {
            _project.ExtendFrames(x);
            _project.SetLayerEffects(index, list);
        });
        ResultMessage = ExtendSummary(x) + " — Ctrl+Z로 되돌립니다.";
    }

    /// <summary>"프레임 1~4: 4개 → 12개로 늘림 (같은 비율로)".</summary>
    internal static string ExtendSummary(FrameExtension x)
        => $"프레임 {(x.From == x.To ? $"{x.From + 1}" : $"{x.From + 1}~{x.To + 1}")}: {x.Length}개 → {x.Count}개로 늘림 ({FrameExtension.ModeName(x.Mode)})";
}
