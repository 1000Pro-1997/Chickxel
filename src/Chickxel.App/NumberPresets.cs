using System.Globalization;
using System.Runtime.CompilerServices;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>숫자 상자 우클릭 목록 맨 위의 특수 항목(예: "화면에 맞추기"). 아래 값 목록과 구분선으로 나뉜다.</summary>
public sealed record NumberAction(string Label, Action Run, KeyGesture? Gesture = null);

/// <summary>
/// 모든 숫자 상자(<see cref="NumericUpDown"/>) 공통: 입력 중이 아닐 때 우클릭하면 목록이 떠서 골라 넣는다. 위에서부터
/// 특수 항목(<see cref="NumberAction"/>, 예: 화면에 맞추기) → <b>최근 값</b> 하나(이 종류 상자에서 마지막으로 정한 값) → 예시 값(지금 값 ●).
/// 값에는 단위(<see cref="Unit"/>: °, %, px, ms …)를 붙여 보인다. 입력 중(글자 커서가 있는 상자)이면 보통 글상자 메뉴.
/// 값 목록은 상자마다 <see cref="Presets(NumericUpDown, decimal[])"/>로 정하고, 정하지 않으면 범위로 만든다(<see cref="Generate"/>).
/// <see cref="NumberScrub.Install"/>이 모든 숫자 상자에 붙인다.
/// </summary>
public static class NumberPresets
{
    /// <summary>상자 단위(메뉴 글자에 붙임).</summary>
    private static readonly ConditionalWeakTable<NumericUpDown, string> Units = new();
    /// <summary>최근 값을 함께 쓰는 상자 묶음 이름(정하지 않으면 단위·범위로).</summary>
    private static readonly ConditionalWeakTable<NumericUpDown, string> Keys = new();
    /// <summary>묶음마다 마지막으로 정한 값(앱을 켜 둔 동안).</summary>
    private static readonly Dictionary<string, decimal> Recent = new();

    private const int MaxItems = 12;

    /// <summary>상자마다 정한 목록: 값(null이면 범위로 만든 기본 값), 맨 위 특수 항목, 값 글자 모양.</summary>
    private sealed record Spec(decimal[]? Values, NumberAction[] Actions, Func<decimal, string>? Format);

    private static readonly ConditionalWeakTable<NumericUpDown, Spec> Custom = new();

    /// <summary>1-2-5 수열(일반 범위의 기본 예시 후보).</summary>
    private static readonly decimal[] OneTwoFive =
        [0, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000];

    /// <summary>픽셀 크기에 흔한 수(최소 1 이상·최대 256 이상인 범위).</summary>
    private static readonly decimal[] PixelSizes = [1, 2, 4, 8, 16, 24, 32, 48, 64, 128, 256, 512, 1024, 2048, 4096];

    /// <summary>이 상자의 우클릭 예시 값을 정한다(범위 밖 값은 뺌).</summary>
    public static NumericUpDown Presets(this NumericUpDown n, params decimal[] values) => n.Presets(values, []);

    /// <summary>예시 값과 맨 위 특수 항목(구분선 위)을 정한다. <paramref name="values"/>가 null이면 범위로 만든 기본 값.</summary>
    public static NumericUpDown Presets(this NumericUpDown n, IEnumerable<decimal>? values, NumberAction[] actions, Func<decimal, string>? format = null)
    {
        Custom.AddOrUpdate(n, new Spec(values?.ToArray(), actions, format));
        return n;
    }

    /// <summary>메뉴 글자에 붙일 단위(예: "°", "%", "px", "ms", "프레임"). °·%는 붙여 쓰고 나머지는 한 칸 띄운다.</summary>
    public static NumericUpDown Unit(this NumericUpDown n, string unit)
    {
        Units.AddOrUpdate(n, unit);
        return n;
    }

    /// <summary>최근 값을 함께 쓸 상자 묶음 이름(예: "effect.angle"). 같은 이름의 상자끼리 최근 값을 나눈다.</summary>
    public static NumericUpDown RecentKey(this NumericUpDown n, string key)
    {
        Keys.AddOrUpdate(n, key);
        return n;
    }

    private static string KeyOf(NumericUpDown n)
        => Keys.TryGetValue(n, out var k) ? k : $"{(Units.TryGetValue(n, out var u) ? u : "")}:{n.Minimum}~{n.Maximum}";

    /// <summary>사용자가 정한 값을 최근 값으로 기억한다(메뉴에서 고름·입력을 마침·끌기를 마침).</summary>
    internal static void Remember(NumericUpDown n)
    {
        if (n.Value is { } v) Recent[KeyOf(n)] = v;
    }

    /// <summary>숫자 상자 우클릭(터널): 입력 중이 아니면 특수 항목·최근 값·예시 값 목록을 띄운다.</summary>
    internal static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not NumericUpDown n || e.Handled || !n.IsEnabled || n.IsKeyboardFocusWithin) return;
        var values = ValuesFor(n);
        Custom.TryGetValue(n, out var spec);
        var items = new List<Control>();
        foreach (var a in spec?.Actions ?? [])
        {
            var item = new MenuItem { Header = new TextBlock { Text = a.Label, HorizontalAlignment = HorizontalAlignment.Left }, InputGesture = a.Gesture };
            item.Click += (_, _) => a.Run();
            items.Add(item);
        }
        string Label(decimal v) => spec?.Format?.Invoke(v) ?? Format(n, v);
        void Pick(decimal value)
        {
            n.Value = value;
            Remember(n);
        }
        if (Recent.TryGetValue(KeyOf(n), out var recent) && recent >= n.Minimum && recent <= n.Maximum)
        {
            if (items.Count > 0) items.Add(new Separator());
            var item = new MenuItem { Header = new TextBlock { Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_NumberPresets_OnContextRequested_246AFAFD23,Label(recent)), HorizontalAlignment = HorizontalAlignment.Left } };
            item.Click += (_, _) => Pick(recent);
            items.Add(item);
        }
        if (items.Count > 0 && values.Count > 0) items.Add(new Separator());
        foreach (var v in values)
        {
            // 지금 값은 라디오 점(●) — 확대율 목록과 같은 모양, 글자는 왼쪽 정렬
            var item = new MenuItem
            {
                Header = new TextBlock { Text = Label(v), HorizontalAlignment = HorizontalAlignment.Left },
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = n.Value == v,
            };
            decimal value = v;
            item.Click += (_, _) => Pick(value);
            items.Add(item);
        }
        if (items.Count == 0) return;
        PopupMenus.Show(n, items, e);
    }

    /// <summary>보여 줄 예시 값(오름차순, 범위 안, 최대 <see cref="MaxItems"/>개).</summary>
    public static IReadOnlyList<decimal> ValuesFor(NumericUpDown n)
    {
        decimal min = n.Minimum, max = n.Maximum;
        if (Custom.TryGetValue(n, out var spec) && spec.Values is { } custom)
            return custom.Where(v => v >= min && v <= max).Distinct().Order().ToList();
        return Generate(min, max, n.Increment);
    }

    /// <summary>
    /// 기본 예시 값(최대 12개, 오름차순): 칸이 12개 이하인 정수 범위는 전부, 소수 단위의 좁은 범위는 10등분, 0~255(색 채널)는 32 간격,
    /// 1 이상·256 이상인 범위(크기)는 2의 거듭제곱 위주, 그 밖에는 1-2-5 수열에 최소·최대를 더한다.
    /// </summary>
    public static IReadOnlyList<decimal> Generate(decimal min, decimal max, decimal increment)
    {
        if (max <= min) return [min];
        if (increment <= 0) increment = 1;
        if (increment >= 1 && (max - min) / increment <= MaxItems - 1)
            return Enumerable.Range(0, (int)((max - min) / increment) + 1).Select(i => min + i * increment).ToList();
        if (increment < 1 && max - min <= 10)
        {
            var even = new SortedSet<decimal>();
            for (int i = 0; i <= 10; i++) even.Add(Math.Round((min + (max - min) * i / 10) / increment) * increment);
            return [.. even];
        }
        IEnumerable<decimal> series = min == 0 && max == 255 ? [0, 32, 64, 96, 128, 160, 192, 224, 255]
            : min >= 1 && max >= 256 ? PixelSizes
            : OneTwoFive;
        var all = series.Where(v => v >= min && v <= max && v % increment == 0).Append(min).Append(max).Distinct().Order().ToList();
        // 많으면 큰 쪽(최대 바로 아래)부터 덜어 낸다 — 작은·중간 값이 자주 쓰이고, 최소·최대는 남긴다.
        while (all.Count > MaxItems) all.RemoveAt(all.Count - 2);
        return all;
    }

    private static string Format(NumericUpDown n, decimal v)
    {
        var text = v.ToString(string.IsNullOrEmpty(n.FormatString) ? "0.##" : n.FormatString, CultureInfo.CurrentCulture);
        if (!Units.TryGetValue(n, out var unit) || unit.Length == 0) return text;
        return unit[0] is '°' or '%' or '×' ? text + unit : $"{text} {unit}";
    }
}
