using Chickxel.App.Localization;
using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>
    /// 오른쪽 독(<see cref="ToolOptionsPanel"/>)의 효과 설정 줄: 효과 창과 같은 효과 고유 설정(글씨 설정, 스크립트 schema 설정 + 움직이는 스크립트는 빠르기 곡선·반복)과
    /// 움직이는 효과의 "링크된 뒤 칸에 끝 모습 이어 가기". 프레임 범위·클리핑·스크립트 원문 편집처럼 효과 목록 전체를 보는 설정은 효과 창에만 있다.
    /// 설명 글은 <see cref="EffectForm.TakeHints"/>로 모을 수 있게 넣는다.
    /// 고칠 수 없는 효과는 모르는 효과면 그 설명만 넣는다(실행할 수 없는 스크립트의 까닭은 독의 상태 줄이 보임).
    /// </summary>
    /// <param name="set">효과 값을 바꾼다(지금 값 → 새 값).</param>
    /// <param name="rebuild">설정 줄을 다시 만든다(색 목록 항목 더하기처럼 줄 수가 바뀔 때).</param>
    internal static void AddDockSettings(EffectForm form, SpriteProject project, Layer layer, LayerEffect effect,
        Action<Func<LayerEffect, LayerEffect>> set, Action rebuild)
    {
        switch (effect)
        {
            case TextEffect text:
                var size = layer.IsPart ? layer.PartSize : new Size(project.Width, project.Height);
                AddTextFields(form, text, set, CenterKind(size.Width, vertical: false), CenterKind(size.Height, vertical: true));
                AddHoldEndEditor(form, text, set);
                form.Add(Hint(UiStrings.TextEffect_Hint));
                return;
            case ScriptEffect script when ScriptLibrary.Runnable(script.ScriptHash) is { } program:
                if (program.Settings.Count == 0) form.Add(Hint(UiStrings.ScriptEffect_NoSettings));
                AddScriptSettingRows(program, script, set, rebuild, LayerChoices(project, layer), form.ScriptRow);
                if (program.Animated && program.Motion) AddTimingEditors(form, script, set);
                AddHoldEndEditor(form, script, set);
                form.Add(Hint(UiStrings.ScriptEffect_Hint));
                return;
            case MissingEffect missing:
                var note = Ui.Label(MissingNote(missing));
                note.TextWrapping = TextWrapping.Wrap;
                note.Foreground = AppTheme.DimTextBrush;
                form.Add(note);
                return;
        }
    }
}
