using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.App;

/// <summary>
/// 크게 보기의 색 판정 줄(색 칸 묶음 위): 누른 결과 픽셀의 색이 단계마다 어떻게 정해졌는지(<see cref="Pixelator.TracePixel"/>) —
/// 칸 색 → 팔레트 → 그 뒤 색을 바꾼 단계를 색 견본·HEX와 함께 차례로, 그 아래 팔레트 단계에서 칸 색에 가까웠던 팔레트 색과 거리.
/// 단계 이름에 마우스를 올리면 그 단계가 색을 정하는 규칙. 처음에는 숨어 있다.
/// </summary>
internal sealed class PixelTraceRow : StackPanel
{
    private const double SwatchSize = 14;
    private readonly TextBlock _title, _note;
    private readonly WrapPanel _steps, _candidates;

    public PixelTraceRow()
    {
        Spacing = 2;
        Margin = new Thickness(0, 4, 0, 0);
        IsVisible = false;
        _title = new TextBlock { Foreground = AppTheme.TextBrush, FontSize = AppTheme.SmallFontSize, FontWeight = FontWeight.Bold };
        _note = new TextBlock { Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, TextWrapping = TextWrapping.Wrap };
        _steps = new WrapPanel { Orientation = Orientation.Horizontal };
        _candidates = new WrapPanel { Orientation = Orientation.Horizontal };
        Children.Add(_title);
        Children.Add(_steps);
        Children.Add(_candidates);
        Children.Add(_note);
    }

    /// <summary>숨긴다(후보·원본이 바뀜).</summary>
    public void Clear()
    {
        IsVisible = false;
        _steps.Children.Clear();
        _candidates.Children.Clear();
    }

    /// <summary>계산하는 동안: 제목과 "계산 중".</summary>
    public void ShowBusy(int x, int y)
    {
        Clear();
        _title.Text = UiStrings.FormatPixelate_Trace_Title(x: x, y: y);
        _note.Text = UiStrings.Pixelate_Trace_Busy;
        _note.IsVisible = true;
        IsVisible = true;
    }

    /// <summary>판정 과정을 보인다(<paramref name="trace"/>가 null이면 다시 만들지 못했다는 안내).</summary>
    public void Show(PixelTrace? trace, PixelateSettings settings, int x, int y)
    {
        Clear();
        IsVisible = true;
        _title.Text = UiStrings.FormatPixelate_Trace_Title(x: x, y: y);
        if (trace is null)
        {
            _note.Text = UiStrings.Pixelate_Trace_Failed;
            _note.IsVisible = true;
            return;
        }
        _note.IsVisible = false;
        for (int i = 0; i < trace.Steps.Count; i++)
        {
            var e = trace.Steps[i];
            if (i > 0)
            {
                var arrow = Ui.Icon(VectorIcon.ChevronRight, 12);
                arrow.Foreground = AppTheme.DimTextBrush;
                arrow.VerticalAlignment = VerticalAlignment.Center;
                arrow.Margin = new Thickness(4, 0);
                _steps.Children.Add(arrow);
            }
            var (name, tip) = Describe(e.Step, trace, settings);
            _steps.Children.Add(Chip(e.Color, name, tip, i == trace.Steps.Count - 1));
        }
        if (trace.Candidates.Count == 0) return;
        _candidates.Children.Add(new TextBlock
        {
            Text = UiStrings.FormatPixelate_Trace_Candidates(distance: DistanceName(trace.Metric)),
            Foreground = AppTheme.DimTextBrush,
            FontSize = AppTheme.SmallFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        var chosen = trace.Steps.FirstOrDefault(s => s.Step == PixelTraceStep.Palette).Color;
        foreach (var c in trace.Candidates)
        {
            bool picked = chosen.A != 0 && (chosen.Argb & 0xFFFFFFu) == (c.Color.Argb & 0xFFFFFFu);
            string detail = UiStrings.FormatPixelate_Trace_Delta(
                l: c.DeltaL.ToString("+0.000;-0.000;0.000", CultureInfo.CurrentCulture),
                c: c.DeltaC.ToString("+0.000;-0.000;0.000", CultureInfo.CurrentCulture),
                h: c.DeltaH.ToString("0.000", CultureInfo.CurrentCulture));
            _candidates.Children.Add(Chip(c.Color, $"{c.Distance.ToString("0.000", CultureInfo.CurrentCulture)} ({detail})", UiStrings.Pixelate_Trace_CandidateTip, picked, hexFirst: true));
        }
    }

    /// <summary>견본 + 이름 + HEX(<paramref name="hexFirst"/>면 HEX + 이름). <paramref name="strong"/>이면 굵게(결과 색·고른 팔레트 색).</summary>
    private static Control Chip(Color32 color, string name, string tip, bool strong, bool hexFirst = false)
    {
        var swatch = new Border
        {
            Width = SwatchSize,
            Height = SwatchSize,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Background = color.A == 0 ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        string hex = color.A == 0 ? UiStrings.Pixelate_Trace_Transparent : color.WithAlpha(255).ToHex();
        var text = new TextBlock
        {
            Text = hexFirst ? $"{hex} {name}" : $"{name} {hex}",
            Foreground = AppTheme.TextBrush,
            FontSize = AppTheme.SmallFontSize,
            FontWeight = strong ? FontWeight.Bold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 1, 6, 1), Children = { swatch, text } };
        ToolTip.SetTip(chip, tip);
        return chip;
    }

    /// <summary>단계 이름과 그 단계가 색을 정하는 규칙.</summary>
    private static (string Name, string Tip) Describe(PixelTraceStep step, PixelTrace trace, PixelateSettings s) => step switch
    {
        PixelTraceStep.Cell => (UiStrings.FormatPixelate_Trace_Cell(mode: SamplingName(s.Downsample.Mode)), CellTip(trace.Steps[0].Color)),
        PixelTraceStep.Palette => (trace.Dithered ? UiStrings.Pixelate_Trace_PaletteDither
                : trace.ShadeFamilyChose ? UiStrings.Pixelate_Trace_PaletteShade
                : UiStrings.Pixelate_Trace_Palette,
            UiStrings.FormatPixelate_Trace_PaletteTip(distance: DistanceName(trace.Metric))),
        PixelTraceStep.SnapOutline => (UiStrings.Pixelate_Pal_SnapOutline, UiStrings.Pixelate_Trace_SnapOutlineTip),
        PixelTraceStep.VectorOutline => (UiStrings.Pixelate_Fit_Vector, UiStrings.Pixelate_Trace_VectorTip),
        PixelTraceStep.Orphans => (UiStrings.Pixelate_Clean_Orphans, UiStrings.Pixelate_Trace_OrphansTip),
        PixelTraceStep.Holes => (UiStrings.Pixelate_Clean_Holes, UiStrings.Pixelate_Trace_HolesTip),
        PixelTraceStep.ThinOutline => (UiStrings.Pixelate_Trace_ThinOutline, UiStrings.Pixelate_Trace_ThinOutlineTip),
        PixelTraceStep.Outline => (UiStrings.Pixelate_Clean_Outline, UiStrings.Pixelate_Trace_OutlineTip),
        PixelTraceStep.Jaggies => (UiStrings.Pixelate_Clean_Jaggies, UiStrings.Pixelate_Trace_JaggiesTip),
        PixelTraceStep.RingCorners => (UiStrings.Pixelate_Trace_RingCorners, UiStrings.Pixelate_Trace_RingCornersTip),
        PixelTraceStep.Temporal => (UiStrings.Pixelate_Cons_Temporal, UiStrings.Pixelate_Trace_TemporalTip),
        PixelTraceStep.Pinned => (UiStrings.Pixelate_Trace_Pinned, UiStrings.Pixelate_Trace_PinnedTip),
        _ => (UiStrings.Pixelate_Trace_Excluded, UiStrings.Pixelate_Trace_ExcludedTip),
    };

    /// <summary>칸 색 풍선: 규칙 + 그 색의 OKLCh 밝기·채도(가까운 팔레트 색의 차를 읽는 기준).</summary>
    private static string CellTip(Color32 cell)
    {
        if (cell.A == 0) return UiStrings.Pixelate_Trace_CellTip;
        var lab = OkLab.FromColor(cell);
        return UiStrings.Pixelate_Trace_CellTip + "\n" + UiStrings.FormatPixelate_Trace_CellLc(
            l: lab.L.ToString("0.000", CultureInfo.CurrentCulture),
            c: ColorDistance.Chroma(lab).ToString("0.000", CultureInfo.CurrentCulture));
    }

    private static string SamplingName(DownsampleMode mode) => mode switch
    {
        DownsampleMode.Median => UiStrings.Pixelate_Ds_Median,
        DownsampleMode.Center => UiStrings.Pixelate_Ds_Center,
        DownsampleMode.Mean => UiStrings.Pixelate_Ds_Mean,
        DownsampleMode.CenterWeighted => UiStrings.Pixelate_Ds_CenterWeighted,
        DownsampleMode.ClusterWeighted => UiStrings.Pixelate_Ds_ClusterWeighted,
        _ => UiStrings.Pixelate_Ds_Mode,
    };

    /// <summary>팔레트 색 거리 이름(OKLCh 색상 가중이면 색상 무게까지).</summary>
    private static string DistanceName(ColorMetric m) => m.Mode switch
    {
        ColorDistanceMode.OkLab => UiStrings.Pixelate_Pal_Distance_OkLab,
        ColorDistanceMode.Ciede2000 => UiStrings.Pixelate_Pal_Distance_Ciede2000,
        _ => $"{UiStrings.Pixelate_Pal_Distance_OkLchHue} H {m.HueWeight.ToString("0.##", CultureInfo.CurrentCulture)}",
    };
}
