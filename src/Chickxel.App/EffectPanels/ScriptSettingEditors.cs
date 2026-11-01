using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>
/// 레이어 고르기 설정(<c>type: "layer"</c>) 칸이 보일 레이어 이름(위→아래, 겹치지 않음)과, 고를 이름이 하나도 없을 때 고르기 상자 아래에 보일 흐린 안내(null이면 없음).
/// 효과 창은 지금 문서의 레이어, 효과 만들기는 미리보기 장면의 레이어를 준다.
/// </summary>
internal sealed record ScriptLayerChoices(IReadOnlyList<string> Names, string? EmptyHint = null);

public sealed partial class LayerEffectsDialog
{
    /// <summary>
    /// 스크립트 설정 칸들(효과 창의 설정 칸과 효과 만들기 창의 미리보기 설정 칸이 함께 씀): 선언 순서대로 설정마다 <paramref name="row"/>(이름, 편집기,
    /// 여러 줄 덩어리인지)를 부른다. 정수·실수는 슬라이더·숫자 칸(<c>widget: "angle"</c>이면 각도 다이얼도, <c>"point"</c>면 짝 Y 설정과 함께 X·Y 칸과
    /// 점 고르기 — Y 설정은 따로 줄이 없음), 켜고 끔, 색(단색·그라데이션 — 기본 효과 색 칸과 같고 슬롯은 설정 키), 고르기,
    /// 색 목록·색 쌍 목록(항목마다 단색·그라데이션, 더하기·빼기·순서 바꾸기 — 그라데이션은 항목을 따라 옮김),
    /// 레이어 고르기(<paramref name="layers"/>의 이름 — <see cref="ScriptLayerEditor"/>).
    /// 값과 그라데이션은 <paramref name="set"/>으로 효과를 바꿔 고치고, 항목 수·순서가 바뀌면 <paramref name="rebuild"/>로 칸을 다시 만든다.
    /// </summary>
    internal static void AddScriptSettingRows(ScriptEffectProgram program, ScriptEffect effect, Action<Func<LayerEffect, LayerEffect>> set,
        Action rebuild, ScriptLayerChoices layers, Action<string, Control, bool> row)
    {
        var values = program.NormalizeSettings(effect.Settings);
        var culture = CultureInfo.CurrentUICulture;
        // 점 고르기의 Y 설정은 X 칸에서 함께 고친다(앞의 X가 먼저 차지 — Y가 다시 다른 점의 X가 되지는 않음)
        var points = new Dictionary<string, ScriptSetting>(StringComparer.Ordinal);
        var paired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in program.Settings)
            if (!paired.Contains(setting.Key) && PointPair(program, setting) is { } y && !points.ContainsKey(y.Key))
            {
                points[setting.Key] = y;
                paired.Add(y.Key);
            }
        foreach (var setting in program.Settings)
        {
            if (paired.Contains(setting.Key)) continue;
            string key = setting.Key;
            string label = program.LabelFor(setting, culture);
            void SetValue(string k, object value)
                => set(o => o is ScriptEffect e ? e with { Settings = ScriptSettingValues.Normalized(program, e.Settings.With(k, value)) } : o);
            switch (setting.Type)
            {
                case ScriptSettingType.Int when points.TryGetValue(key, out var y):
                    string yKey = y.Key;
                    row(label, ScriptPointEditor(setting, y, Convert.ToInt32(values[key], CultureInfo.InvariantCulture),
                        Convert.ToInt32(values[yKey], CultureInfo.InvariantCulture), v => SetValue(key, v), v => SetValue(yKey, v)), false);
                    break;
                case ScriptSettingType.Int:
                case ScriptSettingType.Number:
                    double number = Convert.ToDouble(values[key], CultureInfo.InvariantCulture);
                    row(label, setting is { Type: ScriptSettingType.Int, Widget: ScriptSettingWidget.Angle }
                        ? ScriptAngleEditor(setting, number, v => SetValue(key, v))
                        : ScriptNumberEditor(setting, number, v => SetValue(key, v)), false);
                    break;
                case ScriptSettingType.Bool:
                    // 체크 상자 글로 이름을 보인다(이름 열에 두면 긴 이름이 모든 줄의 이름 열을 넓혀 색·그라데이션 줄이 잘림 — 예전 기본 효과 칸과 같은 모양)
                    row("", CheckEditor(label, (bool)values[key], v => SetValue(key, v)), false);
                    break;
                case ScriptSettingType.Color:
                    var field = new ColorField((Color32)values[key]) { Opaque = !setting.Alpha };
                    field.ColorChanged += (_, _) => SetValue(key, field.Color);
                    row(label, EffectColorEditor(effect, key, field, set), false);
                    break;
                case ScriptSettingType.ColorList:
                    row(label, ScriptColorListEditor(program, setting, effect, (IReadOnlyList<Color32>)values[key], set, rebuild), true);
                    break;
                case ScriptSettingType.ColorPairs:
                    row(label, ScriptColorPairsEditor(program, setting, effect, (IReadOnlyList<ScriptColorPair>)values[key], set, rebuild), true);
                    break;
                case ScriptSettingType.Layer:
                    row(label, ScriptLayerEditor(layers, (string)values[key], v => SetValue(key, v)), false);
                    break;
                default:
                    var choices = setting.Choices;
                    int index = Math.Max(0, choices.ToList().FindIndex(c => c.Value == (string)values[key]));
                    row(label, ChoiceEditor(choices.Select(c => program.ChoiceLabelFor(setting, c, culture)).ToArray(), index,
                        k => SetValue(key, choices[k].Value)), false);
                    break;
            }
        }
    }

    /// <summary><c>widget: "point"</c> 정수 설정의 Y 설정(<see cref="ScriptSetting.Pair"/>가 가리키는 다른 정수 설정). 아니면 null.</summary>
    private static ScriptSetting? PointPair(ScriptEffectProgram program, ScriptSetting setting)
        => setting is { Type: ScriptSettingType.Int, Widget: ScriptSettingWidget.Point, Pair: { } pair } && pair != setting.Key
            ? program.Settings.FirstOrDefault(s => s.Key == pair && s.Type == ScriptSettingType.Int)
            : null;

    /// <summary>
    /// 레이어 고르기 설정: 첫 항목 "(없음)"(빈 이름) 뒤에 <paramref name="layers"/>의 이름을 늘어놓은 고르기 상자. 저장된 이름이 목록에 없으면 버리지 않고
    /// "이름 (없음)" 항목으로 끝에 둔다(고르면 그 이름 그대로). 고를 이름이 없고 안내가 있으면 상자 아래에 흐리게 적는다.
    /// </summary>
    private static Control ScriptLayerEditor(ScriptLayerChoices layers, string value, Action<string> changed)
    {
        var names = new List<string> { "" };
        names.AddRange(layers.Names.Where(n => n.Length > 0));
        var items = new List<string> { UiStrings.ScriptEffect_LayerNone };
        items.AddRange(names.Skip(1));
        if (value.Length > 0 && !names.Contains(value))
        {
            names.Add(value);
            items.Add(UiStrings.FormatScriptEffect_LayerMissing(name: value));
        }
        var combo = ChoiceEditor([.. items], names.IndexOf(value), k => changed(names[k]));
        if (layers.Names.Count > 0 || layers.EmptyHint is not { } hint) return combo;
        var note = Ui.Label(hint);
        note.TextWrapping = TextWrapping.Wrap;
        note.MaxWidth = 220;
        note.Foreground = AppTheme.DimTextBrush;
        note.FontSize = AppTheme.SmallFontSize;
        return new StackPanel { Spacing = 2, Children = { combo, note } };
    }

    /// <summary>각도 설정: 다이얼(끌어 고름) + 슬라이더·숫자 칸. 다이얼 각도는 설정 범위 안으로 맞춘다(예: -180~180이면 270° → -90).</summary>
    private static Control ScriptAngleEditor(ScriptSetting setting, double value, Action<double> changed)
    {
        var dial = new AngleDial { Angle = value, Step = (int)Math.Max(1, Math.Round(setting.Step ?? 1)) };
        var editor = ScriptNumberEditor(setting, value, v =>
        {
            dial.Angle = v;
            changed(v);
        }, out var number);
        dial.Picked += angle => number.Value = FitAngle(setting, angle);
        return Ui.Row(dial.Tip(UiStrings.ScriptSetting_AngleTip), editor);
    }

    /// <summary><paramref name="degrees"/>(0~359)를 설정 범위에 든 같은 방향 값(±360)으로, 없으면 범위에 가장 가까운 값으로.</summary>
    private static int FitAngle(ScriptSetting setting, int degrees)
    {
        double min = setting.Min ?? int.MinValue, max = setting.Max ?? int.MaxValue;
        int best = degrees;
        double bestGap = double.MaxValue;
        foreach (int candidate in (int[])[degrees, degrees - 360, degrees + 360])
        {
            double gap = candidate < min ? min - candidate : candidate > max ? candidate - max : 0;
            if (gap >= bestGap) continue;
            best = candidate;
            bestGap = gap;
        }
        return (int)Math.Clamp(best, Math.Ceiling(min), Math.Floor(max));
    }

    /// <summary>
    /// 점 설정: X(이 설정)·Y(짝 설정) 숫자 칸. 두 설정 모두 범위가 있으면 왼쪽에 점 고르기 판(왼쪽 위가 가장 작은 값)을 두고 끌어 둘을 함께 고른다.
    /// </summary>
    private static Control ScriptPointEditor(ScriptSetting xSetting, ScriptSetting ySetting, int x, int y, Action<int> setX, Action<int> setY)
    {
        var nx = PointNumber(xSetting, x);
        var ny = PointNumber(ySetting, y);
        var numbers = Ui.Row(Ui.Label("X"), nx, Ui.Label("Y"), ny);
        numbers.VerticalAlignment = VerticalAlignment.Center;
        PointPad? pad = xSetting is { Min: { } x0, Max: { } x1 } && ySetting is { Min: { } y0, Max: { } y1 } && x1 > x0 && y1 > y0
            ? new PointPad(x0, x1, y0, y1) { X = x, Y = y }
            : null;
        nx.ValueChanged += (_, _) =>
        {
            int v = (int)nx.NumValue();
            if (pad is not null) pad.X = v;
            setX(v);
        };
        ny.ValueChanged += (_, _) =>
        {
            int v = (int)ny.NumValue();
            if (pad is not null) pad.Y = v;
            setY(v);
        };
        if (pad is null) return numbers;
        pad.Picked += (px, py) =>
        {
            if (nx.NumValue() != px) nx.Value = px;
            if (ny.NumValue() != py) ny.Value = py;
        };
        return Ui.Row(pad.Tip(UiStrings.ScriptSetting_PointTip), numbers);
    }

    private static NumericUpDown PointNumber(ScriptSetting setting, int value)
    {
        const int Limit = 1_000_000_000;
        int min = (int)Math.Max(-Limit, Math.Ceiling(setting.Min ?? -Limit)), max = (int)Math.Min(Limit, Math.Floor(setting.Max ?? Limit));
        return Ui.Num(min, max, Math.Clamp(value, min, max), 64, 0, (decimal)Math.Max(1, Math.Round(setting.Step ?? 1)));
    }

    /// <summary>
    /// 색 목록 설정: 항목마다 [번호][단색·그라데이션 색 칸][위로][아래로][빼기], 아래에 [색 추가]. 항목 수는 <see cref="ScriptSetting.Min"/>~<see cref="ScriptSetting.Max"/>,
    /// 항목 그라데이션 슬롯은 <see cref="ScriptColorSlots.Item"/>(옮기거나 빼면 함께 옮기고, 뺀 항목의 그라데이션은 버림).
    /// </summary>
    private static Control ScriptColorListEditor(ScriptEffectProgram program, ScriptSetting setting, ScriptEffect effect, IReadOnlyList<Color32> colors,
        Action<Func<LayerEffect, LayerEffect>> set, Action rebuild)
    {
        string key = setting.Key;
        void Change(Func<List<Color32>, bool> edit, Func<int, int>? reindex, bool structural)
        {
            set(o =>
            {
                if (o is not ScriptEffect e || program.NormalizeSettings(e.Settings)[key] is not IReadOnlyList<Color32> current) return o;
                var list = current.ToList();
                if (!edit(list)) return o;
                return e with
                {
                    Settings = ScriptSettingValues.Normalized(program, e.Settings.With(key, list)),
                    ColorGradients = reindex is null ? e.ColorGradients : ReindexGradients(e.ColorGradients, key, reindex),
                };
            });
            if (structural) rebuild();
        }

        var body = new StackPanel { Spacing = 2 };
        for (int k = 0; k < colors.Count; k++)
        {
            int at = k;
            var field = new ColorField(colors[k], 64) { Opaque = !setting.Alpha };
            field.ColorChanged += (_, _) => Change(list =>
            {
                if (at >= list.Count) return false;
                list[at] = field.Color;
                return true;
            }, null, false);
            body.Children.Add(ItemRow(ItemNumber(at), EffectColorEditor(effect, ScriptColorSlots.Item(key, at), field, set,
                ItemButtons(setting, at, colors.Count,
                    to => Change(list => Swap(list, at, to), i => i == at ? to : i == to ? at : i, true),
                    () => Change(list => RemoveAt(list, at), i => i == at ? -1 : i > at ? i - 1 : i, true)))));
        }
        if (colors.Count == 0) body.Children.Add(EmptyListNote());
        int count = colors.Count;
        body.Children.Add(AddItemButton(setting, count, UiStrings.ScriptSetting_AddColor, () => Change(list =>
        {
            list.Add(list.Count > 0 ? list[^1] : Color32.Black);
            return true;
        }, i => i >= count ? -1 : i, true)));
        return body;
    }

    /// <summary>
    /// 색 쌍 목록 설정: 항목마다 [번호][바꿀 색][위로·아래로][빼기], 그 아래 [꺾인 화살표][바뀐 색], 맨 아래 [쌍 추가]. 항목 수는 <see cref="ScriptSetting.Min"/>~<see cref="ScriptSetting.Max"/>,
    /// 그라데이션 슬롯은 <see cref="ScriptColorSlots.PairItem"/>(옮기거나 빼면 함께 옮기고, 뺀 쌍의 그라데이션은 버림).
    /// </summary>
    private static Control ScriptColorPairsEditor(ScriptEffectProgram program, ScriptSetting setting, ScriptEffect effect, IReadOnlyList<ScriptColorPair> pairs,
        Action<Func<LayerEffect, LayerEffect>> set, Action rebuild)
    {
        string key = setting.Key;
        void Change(Func<List<ScriptColorPair>, bool> edit, Func<int, int>? reindex, bool structural)
        {
            set(o =>
            {
                if (o is not ScriptEffect e || program.NormalizeSettings(e.Settings)[key] is not IReadOnlyList<ScriptColorPair> current) return o;
                var list = current.ToList();
                if (!edit(list)) return o;
                return e with
                {
                    Settings = ScriptSettingValues.Normalized(program, e.Settings.With(key, list)),
                    ColorGradients = reindex is null ? e.ColorGradients : ReindexGradients(e.ColorGradients, key, reindex),
                };
            });
            if (structural) rebuild();
        }

        var body = new StackPanel { Spacing = 4 };
        for (int k = 0; k < pairs.Count; k++)
        {
            int at = k;
            var from = new ColorField(pairs[k].From, 64) { Opaque = !setting.Alpha };
            var to = new ColorField(pairs[k].To, 64) { Opaque = !setting.Alpha };
            from.ColorChanged += (_, _) => Change(list =>
            {
                if (at >= list.Count) return false;
                list[at] = list[at] with { From = from.Color };
                return true;
            }, null, false);
            to.ColorChanged += (_, _) => Change(list =>
            {
                if (at >= list.Count) return false;
                list[at] = list[at] with { To = to.Color };
                return true;
            }, null, false);
            var arrow = Ui.Icon(VectorIcon.CornerDownRight, 10);
            arrow.Width = 10;
            arrow.Margin = new Thickness(0, 9, 0, 0);
            var item = new StackPanel { Spacing = 0 };
            item.Children.Add(ItemRow(ItemNumber(at), EffectColorEditor(effect, ScriptColorSlots.PairItem(key, at, to: false), from, set,
                ItemButtons(setting, at, pairs.Count,
                    target => Change(list => Swap(list, at, target), i => i == at ? target : i == target ? at : i, true),
                    () => Change(list => RemoveAt(list, at), i => i == at ? -1 : i > at ? i - 1 : i, true)))));
            item.Children.Add(ItemRow(arrow.Tip(UiStrings.ScriptSetting_PairTo), EffectColorEditor(effect, ScriptColorSlots.PairItem(key, at, to: true), to, set)));
            body.Children.Add(item);
        }
        if (pairs.Count == 0) body.Children.Add(EmptyListNote());
        int count = pairs.Count;
        body.Children.Add(AddItemButton(setting, count, UiStrings.ScriptSetting_AddPair, () => Change(list =>
        {
            list.Add(list.Count > 0 ? list[^1] : new ScriptColorPair(Color32.Black, Color32.White));
            return true;
        }, i => i >= count ? -1 : i, true)));
        return body;
    }

    private static bool Swap<T>(List<T> list, int a, int b)
    {
        if (a < 0 || b < 0 || a >= list.Count || b >= list.Count) return false;
        (list[a], list[b]) = (list[b], list[a]);
        return true;
    }

    private static bool RemoveAt<T>(List<T> list, int at)
    {
        if (at >= list.Count) return false;
        list.RemoveAt(at);
        return true;
    }

    /// <summary>목록 항목 한 줄: 왼쪽 좁은 머리(번호·화살표)를 색 칸 첫 줄 높이에 두고 오른쪽에 색 편집기(그라데이션이면 그 아래로 길어짐).</summary>
    private static StackPanel ItemRow(Control lead, Control editor)
    {
        var row = Ui.Row(lead, editor);
        row.Spacing = 0;
        lead.VerticalAlignment = VerticalAlignment.Top;
        editor.VerticalAlignment = VerticalAlignment.Top;
        return row;
    }

    /// <summary>목록 항목 번호(1부터).</summary>
    private static TextBlock ItemNumber(int index)
    {
        var label = Ui.Label((index + 1).ToString(CultureInfo.CurrentCulture));
        label.MinWidth = 10;
        label.Margin = new Thickness(0, 8, 0, 0);
        label.Foreground = AppTheme.DimTextBrush;
        return label;
    }

    /// <summary>
    /// 항목 단추: 위아래로 겹친 작은 [위로]·[아래로]와 [빼기](좁은 설정 칸에 색 칸·모드 고르기와 한 줄로 들어가게).
    /// 처음·끝 항목은 그쪽으로 못 옮기고, 최소 개수면 뺄 수 없다.
    /// </summary>
    private static StackPanel ItemButtons(ScriptSetting setting, int at, int count, Action<int> move, Action remove)
    {
        static Button Half(VectorIcon icon, string tip, Action click)
        {
            var button = Ui.IconButton(icon, tip, (_, _) => click(), 22, 10);
            ((Control)button.Content!).Margin = new Thickness(0);
            button.Height = 13;
            button.MinHeight = 0;
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(1, 0);
            return button;
        }
        var up = Half(VectorIcon.ChevronUp, UiStrings.ScriptSetting_MoveUp, () => move(at - 1));
        var down = Half(VectorIcon.ChevronDown, UiStrings.ScriptSetting_MoveDown, () => move(at + 1));
        var minus = Ui.IconButton(VectorIcon.Minus, UiStrings.ScriptSetting_RemoveItem, (_, _) => remove(), 24, 12);
        up.IsEnabled = at > 0;
        down.IsEnabled = at < count - 1;
        minus.IsEnabled = count > (int)(setting.Min ?? 0);
        var moves = new StackPanel { Spacing = 1 };
        moves.Children.Add(up);
        moves.Children.Add(down);
        var buttons = Ui.Row(moves, minus);
        buttons.Spacing = 0;
        return buttons;
    }

    /// <summary>[+ 색 추가]: 최대 개수면 꺼짐. 풍선에 항목 수 범위.</summary>
    private static Button AddItemButton(ScriptSetting setting, int count, string text, Action add)
    {
        int min = (int)(setting.Min ?? 0), max = (int)(setting.Max ?? ScriptSetting.MaxListCount);
        var button = Ui.Button(VectorIcon.Plus, text, (_, _) => add());
        button.IsEnabled = count < max;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button.Tip(UiStrings.FormatScriptSetting_ListRange(min: min, max: max));
    }

    private static TextBlock EmptyListNote()
    {
        var note = Ui.Label(UiStrings.ScriptSetting_ListEmpty);
        note.Foreground = AppTheme.DimTextBrush;
        return note;
    }
}

/// <summary>
/// 각도 다이얼(0° = 오른쪽, 90° = 위, 시계 반대 방향): 원 안의 바늘이 지금 각도를 가리키고, 누르거나 끌면 그 방향 각도(0~359)를 <see cref="Picked"/>로 알린다.
/// <see cref="Step"/>도 단위로 맞추고, Shift를 누르면 15°씩.
/// </summary>
internal sealed class AngleDial : Control
{
    private static readonly IPen RimPen = AppTheme.Pen(AppTheme.BorderColor);
    private static readonly IPen NeedlePen = AppTheme.Pen(AppTheme.AccentColor, 2);
    private double _angle;
    private bool _dragging;

    public AngleDial()
    {
        Width = Height = 30;
        Margin = new Thickness(3, 0);
        VerticalAlignment = VerticalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>끌어 고를 때의 각도 단위(1 이상).</summary>
    public int Step { get; init; } = 1;

    /// <summary>바늘 각도(도).</summary>
    public double Angle
    {
        get => _angle;
        set
        {
            _angle = value;
            InvalidateVisual();
        }
    }

    public event Action<int>? Picked;

    public override void Render(DrawingContext g)
    {
        var bounds = new Rect(Bounds.Size).Deflate(1);
        var center = bounds.Center;
        double radius = Math.Min(bounds.Width, bounds.Height) / 2;
        g.DrawEllipse(AppTheme.InputBrush, RimPen, center, radius, radius);
        double rad = _angle * Math.PI / 180;
        var tip = new Avalonia.Point(center.X + Math.Cos(rad) * (radius - 3), center.Y - Math.Sin(rad) * (radius - 3));
        g.DrawLine(NeedlePen, center, tip);
        g.DrawEllipse(AppTheme.AccentBrush, null, center, 2, 2);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        e.Pointer.Capture(this);
        Pick(e.GetPosition(this), e.KeyModifiers);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) Pick(e.GetPosition(this), e.KeyModifiers);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    private void Pick(Avalonia.Point p, KeyModifiers modifiers)
    {
        var center = new Rect(Bounds.Size).Center;
        double dx = p.X - center.X, dy = center.Y - p.Y;
        // 가운데 바로 위는 방향이 흔들리므로 무시
        if (dx * dx + dy * dy < 9) return;
        double degrees = Math.Atan2(dy, dx) * 180 / Math.PI;
        int snap = modifiers.HasFlag(KeyModifiers.Shift) ? 15 : Math.Max(1, Step);
        int value = ((int)(Math.Round(degrees / snap) * snap) % 360 + 360) % 360;
        Picked?.Invoke(value);
    }
}

/// <summary>
/// 점 고르기 판: X·Y 범위를 정사각형에 펼쳐(왼쪽 위 = 가장 작은 값) 지금 점을 십자로 보이고, 누르거나 끌면 그 자리의 정수 X·Y를 <see cref="Picked"/>로 알린다.
/// </summary>
internal sealed class PointPad : Control
{
    private static readonly IPen BorderPen = AppTheme.Pen(AppTheme.BorderColor);
    private static readonly IPen GuidePen = AppTheme.Pen(AppTheme.DimTextColor);
    private static readonly IPen MarkPen = AppTheme.Pen(AppTheme.AccentColor, 2);
    private readonly double _minX, _maxX, _minY, _maxY;
    private int _x, _y;
    private bool _dragging;

    public PointPad(double minX, double maxX, double minY, double maxY)
    {
        (_minX, _maxX, _minY, _maxY) = (minX, maxX, minY, maxY);
        Width = Height = 56;
        Margin = new Thickness(3);
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public int X
    {
        get => _x;
        set
        {
            _x = value;
            InvalidateVisual();
        }
    }

    public int Y
    {
        get => _y;
        set
        {
            _y = value;
            InvalidateVisual();
        }
    }

    public event Action<int, int>? Picked;

    public override void Render(DrawingContext g)
    {
        var r = new Rect(Bounds.Size);
        g.FillRectangle(AppTheme.InputBrush, r);
        var inner = r.Deflate(3);
        double px = inner.X + Math.Clamp((_x - _minX) / (_maxX - _minX), 0, 1) * inner.Width;
        double py = inner.Y + Math.Clamp((_y - _minY) / (_maxY - _minY), 0, 1) * inner.Height;
        g.DrawLine(GuidePen, new Avalonia.Point(r.X, py), new Avalonia.Point(r.Right, py));
        g.DrawLine(GuidePen, new Avalonia.Point(px, r.Y), new Avalonia.Point(px, r.Bottom));
        g.DrawEllipse(null, MarkPen, new Avalonia.Point(px, py), 3, 3);
        g.DrawRectangle(null, BorderPen, r.Deflate(0.5));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        e.Pointer.Capture(this);
        Pick(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) Pick(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    private void Pick(Avalonia.Point p)
    {
        var inner = new Rect(Bounds.Size).Deflate(3);
        double fx = Math.Clamp((p.X - inner.X) / Math.Max(1, inner.Width), 0, 1);
        double fy = Math.Clamp((p.Y - inner.Y) / Math.Max(1, inner.Height), 0, 1);
        int x = (int)Math.Clamp(Math.Round(_minX + fx * (_maxX - _minX)), Math.Ceiling(_minX), Math.Floor(_maxX));
        int y = (int)Math.Clamp(Math.Round(_minY + fy * (_maxY - _minY)), Math.Ceiling(_minY), Math.Floor(_maxY));
        Picked?.Invoke(x, y);
    }
}
