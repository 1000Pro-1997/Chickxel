using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace Chickxel.App;

// 피벗 도구 설정: 대상(지금 레이어 또는 캔버스 피벗 — <see cref="EditorContext.PivotTargetLayer"/>)의 X/Y(픽셀)·5×5 단추·"현재 프레임부터",
// 캔버스 대상이면 대표 레이어 고르기.
public sealed partial class ToolOptionsPanel
{
    /// <summary>대표 레이어 고르기 항목이 가리키는 레이어(0 = 없음, 그 뒤는 위 레이어부터).</summary>
    private readonly List<Layer?> _pivotLayerChoices = [];
    private readonly List<string> _pivotLayerLabels = [];

    /// <summary>피벗 대상의 픽셀 크기: 부위면 부위 크기, 그 밖에는 캔버스 크기.</summary>
    private int TargetWidth => Math.Max(1, _ctx.PivotTargetSize.Width);
    private int TargetHeight => Math.Max(1, _ctx.PivotTargetSize.Height);

    /// <summary>저장된 대상 피벗(현재 프레임, 대상의 공간).</summary>
    private Pivot? TargetPivot() => _ctx.HasProject ? _ctx.StoredPivot : null;

    /// <summary>
    /// 5×5 단추(<paramref name="col"/>, <paramref name="row"/> = 0~4, 위 왼쪽부터)를 정수 픽셀에 맞춘 피벗:
    /// 0 / 1/4 / 1/2 / 3/4 / 끝(가운데는 내림 15px → 7px, 1/4 = 16px → 4px, 3/4 = 너비 − 1/4 → 12px, 오른쪽 = 너비, 위 = 높이).
    /// </summary>
    private Pivot SnappedStep(int col, int row)
    {
        int w = TargetWidth, h = TargetHeight;
        static int Snap(int step, int size) => step switch
        {
            0 => 0,
            1 => size / 4,
            2 => size / 2,
            3 => size - size / 4,
            _ => size,
        };
        return new Pivot(Snap(col, w) / (float)w, Snap(PivotSteps - 1 - row, h) / (float)h);
    }

    /// <summary>피벗 대상을 화면에 보인다. 피벗 도구일 때만.</summary>
    private void RefreshPivot()
    {
        _pivotBox.IsEnabled = _ctx.HasProject;
        if (!_pivotBox.IsVisible || _ctx.Project is not { } p) return;
        bool wasUpdating = _updating;
        _updating = true;
        try
        {
            int f = _ctx.CurrentFrame;
            bool own = _ctx.PivotTargetOwnsFrame(f);
            _framePivot.IsChecked = own;
            _pivotTarget.Text = PivotTargetText(p, f, own);
            bool canvas = _ctx.IsPivotCanvasTarget;
            _pivotLayerRow.IsVisible = canvas;
            if (canvas) RefreshPivotLayerChoices(p);
            // 끄는 중이면 미리보기 값.
            var pivot = _ctx.Pivot;
            int w = TargetWidth, h = TargetHeight;
            _pivotX.Maximum = w;
            _pivotY.Maximum = h;
            _pivotX.Presets(Quarters(w));
            _pivotY.Presets(Quarters(h));
            _pivotX.Value = Math.Round((decimal)Math.Clamp(pivot.X * w, 0, w), 2);
            _pivotY.Value = Math.Round((decimal)Math.Clamp(pivot.Y * h, 0, h), 2);
            _pivotXMax.Text = $"/ {w} px";
            _pivotYMax.Text = $"/ {h} px";
            for (int i = 0; i < _presetButtons.Length; i++)
            {
                int col = i % PivotSteps, row = i / PivotSteps;
                var exact = new Pivot(col / 4f, (PivotSteps - 1 - row) / 4f);
                var snapped = SnappedStep(col, row);
                bool on = pivot == exact
                    || (Math.Abs((pivot.X - snapped.X) * w) < 0.01f && Math.Abs((pivot.Y - snapped.Y) * h) < 0.01f);
                _presetButtons[i].Background = on ? AppTheme.AccentBrush : AppTheme.InputBrush;
            }
        }
        finally
        {
            _updating = wasUpdating;
        }
    }

    /// <summary>
    /// 대상 글: 레이어(종류·이름, 공간 크기 — 부위는 부위 아틀라스), 캔버스 피벗(자신의 피벗), 또는 캔버스 = 대표 레이어(고치면 그 레이어의 피벗이 바뀜).
    /// <paramref name="own"/> = 대상이 이 프레임부터 따로 둔 피벗이 있음.
    /// </summary>
    private string PivotTargetText(SpriteProject p, int f, bool own)
    {
        var culture = CultureInfo.CurrentCulture;
        var layer = _ctx.PivotTargetLayer;
        if (_ctx.IsPivotCanvasTarget)
        {
            if (layer is not null) return string.Format(culture, UiStrings.Pivot_CanvasRepresentativeTarget, layer.Name);
            return own ? string.Format(culture, UiStrings.Pivot_CanvasFrameTarget, f + 1) : UiStrings.Pivot_CanvasTarget;
        }
        if (layer is null) return "";
        if (layer.IsPart)
            return own
                ? string.Format(culture, UiStrings.Pivot_PartFrameTarget, layer.Name, f + 1, layer.PartSize.Width, layer.PartSize.Height)
                : string.Format(culture, UiStrings.S_ToolOptionsPanel_RefreshPivot_7752FB81E5, layer.Name, layer.PartSize.Width, layer.PartSize.Height);
        string kind = layer.IsGroup ? UiStrings.Pivot_KindGroup : UiStrings.Pivot_KindLayer;
        return own
            ? string.Format(culture, UiStrings.Pivot_LayerFrameTarget, kind, layer.Name, f + 1, p.Width, p.Height)
            : string.Format(culture, UiStrings.Pivot_LayerTarget, kind, layer.Name, p.Width, p.Height);
    }

    /// <summary>대표 레이어 고르기 항목(없음 + 위 레이어부터, 부위는 들여 씀)을 레이어가 바뀐 때만 다시 만들고, 지금 대표 레이어를 고른다.</summary>
    private void RefreshPivotLayerChoices(SpriteProject p)
    {
        var choices = new List<Layer?>(p.Layers.Count + 1) { null };
        var labels = new List<string>(p.Layers.Count + 1) { UiStrings.Pivot_RepresentativeNone };
        for (int i = p.Layers.Count - 1; i >= 0; i--)
        {
            var layer = p.Layers[i];
            choices.Add(layer);
            labels.Add(layer.IsPart ? "    " + layer.Name : layer.Name);
        }
        if (!choices.SequenceEqual(_pivotLayerChoices) || !labels.SequenceEqual(_pivotLayerLabels))
        {
            _pivotLayerChoices.Clear();
            _pivotLayerChoices.AddRange(choices);
            _pivotLayerLabels.Clear();
            _pivotLayerLabels.AddRange(labels);
            _pivotLayer.Items.Clear();
            foreach (var label in labels) _pivotLayer.Items.Add(label);
        }
        _pivotLayer.SelectedIndex = Math.Max(0, _pivotLayerChoices.IndexOf(p.PivotLayer));
    }

    private void ApplyPivot(Pivot pivot)
    {
        if (_updating || !_ctx.HasProject) return;
        _ctx.ApplyPivot(pivot);
    }
}
