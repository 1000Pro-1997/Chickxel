using System.Drawing;
using Avalonia.Controls;
using Avalonia.Media;
using Chickxel.App.Localization;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 효과 하나의 '캔버스 늘리기…'(타임라인 효과 줄 우클릭·효과 칸): 그 효과 결과(모든 프레임, 앞 효과 포함·뒤 효과 빼고)가 캔버스 밖으로 나가는 만큼
/// 늘릴 방식(최소·비율 유지·중심 유지)을 고르고, 새 크기를 보고 확인하면 늘린다(<see cref="Plan"/>을 부르는 쪽이 적용). 이미 들어가면 확인을 끈다.
/// </summary>
internal sealed class EffectCanvasDialog : DialogBase
{
    /// <summary>늘리는 방식(앱을 켜 둔 동안 기억 — 효과 창의 공통 설정과 같은 값).</summary>
    internal static EffectCanvasExpandMode LastMode { get; set; } = EffectCanvasExpandMode.KeepCenter;

    private readonly SpriteProject _project;
    private readonly Rectangle _bounds;
    private readonly TextBlock _size, _note;

    /// <summary>확인했을 때 적용할 캔버스 확대(계산하지 못했거나 늘릴 것이 없으면 null).</summary>
    public EffectCanvasExpansion? Plan { get; private set; }

    /// <summary>
    /// 레이어 <paramref name="layerIndex"/>의 효과 <paramref name="effectIndex"/>에 맞춰 캔버스를 늘린다: 효과 범위를 재고(덮개), 창에서 방식을 골라 확인하면
    /// 늘린다(실행취소 한 단계). 상태 표시줄에 보일 글(늘렸으면 크기, 못 하면 까닭, 취소했으면 null)을 돌려준다.
    /// </summary>
    public static string? Run(Avalonia.Visual owner, EditorContext ctx, int layerIndex, int effectIndex)
    {
        if (ctx.Project is not { } p || layerIndex < 0 || layerIndex >= p.Layers.Count) return null;
        var layer = p.Layers[layerIndex];
        if (effectIndex < 0 || effectIndex >= layer.Effects.Count) return null;
        ctx.CommitFloating();
        var bounds = WorkOverlay.Run(owner, UiStrings.Busy_EffectBounds, _ => p.EffectOutputBounds(layer, effectIndex, expandingOnly: false));
        if (bounds is not { } b) return UiStrings.EffectCanvas_NoOutput;
        var dialog = new EffectCanvasDialog(p, b, layer.Effects[effectIndex].DisplayName);
        if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.Plan is not { } plan) return null;
        int oldWidth = p.Width, oldHeight = p.Height;
        ctx.Structural(() => p.ApplyEffectCanvas(plan));
        return string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_LayerEffectsDialog_Canvas_CanvasExpansionSummary_332F7D70DF, oldWidth, oldHeight, plan.Width, plan.Height);
    }

    public EffectCanvasDialog(SpriteProject project, Rectangle effectBounds, string effectName)
        : base(UiStrings.EffectCanvas_Title, UiStrings.EffectCanvas_Apply)
    {
        _project = project;
        _bounds = effectBounds;
        var intro = Ui.Label(UiStrings.FormatEffectCanvas_Intro(effect: effectName));
        intro.TextWrapping = TextWrapping.Wrap;
        intro.MaxWidth = 360;
        var mode = Ui.Combo(200, [UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_E64B4CB375, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_F7968CE40F, UiStrings.EffectCanvasExpand_KeepCenter]);
        mode.SelectedIndex = (int)LastMode;
        ToolTip.SetTip(mode, UiStrings.S_LayerEffectsDialog_LayerEffectsDialog_4DF95CBC10);
        _size = Ui.Label("");
        _note = Ui.Label("");
        _note.Foreground = AppTheme.DimTextBrush;
        _note.TextWrapping = TextWrapping.Wrap;
        _note.MaxWidth = 360;
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex < 0) return;
            LastMode = (EffectCanvasExpandMode)mode.SelectedIndex;
            Refresh();
        };
        Body.Children.Add(intro);
        Body.Children.Add(Ui.Row(Ui.Label(UiStrings.EffectCanvas_Mode), mode));
        Body.Children.Add(_size);
        Body.Children.Add(_note);
        Refresh();
    }

    /// <summary>고른 방식으로 새 크기를 다시 잰다. 늘릴 것이 없거나 너무 커지면 확인을 끈다.</summary>
    private void Refresh()
    {
        int w = _project.Width, h = _project.Height;
        try
        {
            var plan = _project.PlanEffectCanvas(_bounds, LastMode);
            bool none = plan.IsNone(w, h);
            Plan = none ? null : plan;
            _size.Text = none
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_BDEB632BC8, w, h)
                : $"{w}×{h} → {plan.Width}×{plan.Height}"
                  + (LastMode == EffectCanvasExpandMode.KeepRatio && !plan.RatioPreserved ? UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_6BBDE7B81F : "");
            _note.Text = none
                ? UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_EFFF97903C
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_AA4018CCC5, plan.OffsetX, plan.OffsetY);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            Plan = null;
            _size.Text = "";
            _note.Text = ex.Message;
        }
        OkButton.IsEnabled = Plan is not null;
    }
}
