using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>Solid color picker plus spatial gradient mode for one effect color slot.</summary>
    private Control EffectColorEditor(LayerEffect effect, string slot, ColorField field,
        Action<Func<LayerEffect, LayerEffect>> set)
    {
        bool gradientMode = effect.ColorGradients.ContainsKey(slot);
        var mode = Ui.Combo(104, "일반색", "그라데이션");
        var editor = new GradientEditor(spatialAngle: true);
        var flyout = new Flyout { Content = new Border { Width = 340, Padding = new Thickness(8), Child = editor } };
        void ShowEditor() => Dispatcher.UIThread.Post(() => PopupMenus.Show(mode, flyout));

        EffectColorGradient InitialGradient()
            => new()
            {
                Stops = [new GradientStop(0, field.Color), new GradientStop(1, field.Color)],
                AlphaStops = EditorContext.DefaultGradientAlphaStops
            };

        void ChangeGradient(EffectColorGradient? gradient)
        {
            set(value =>
            {
                var colors = new Dictionary<string, EffectColorGradient>(StringComparer.Ordinal);
                foreach (var (key, current) in value.ColorGradients) colors[key] = current;
                if (gradient is null) colors.Remove(slot);
                else colors[slot] = gradient;
                return value with { ColorGradients = colors };
            });
        }

        editor.Changed += () => ChangeGradient(new EffectColorGradient
        {
            Stops = editor.Stops,
            AlphaStops = editor.AlphaStops,
            Angle = editor.Angle,
            Interpolation = editor.Interpolation,
            Dither = editor.Dither
        });

        mode.SelectedIndex = gradientMode ? 1 : 0;
        field.IsVisible = !gradientMode;
        if (gradientMode && effect.ColorGradients.TryGetValue(slot, out var existing))
            editor.Load(existing.Stops, existing.AlphaStops, existing.Interpolation, existing.Dither, existing.Angle);
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex < 0) return;
            bool enabled = mode.SelectedIndex == 1;
            bool wasGradient = gradientMode;
            field.IsVisible = !enabled;
            if (enabled)
            {
                var gradient = wasGradient
                    ? new EffectColorGradient
                    {
                        Stops = editor.Stops, AlphaStops = editor.AlphaStops,
                        Angle = editor.Angle, Interpolation = editor.Interpolation, Dither = editor.Dither
                    }
                    : InitialGradient();
                editor.Load(gradient.Stops, gradient.AlphaStops, gradient.Interpolation, gradient.Dither, gradient.Angle);
                gradientMode = true;
                ChangeGradient(gradient);
                ShowEditor();
            }
            else
            {
                gradientMode = false;
                ChangeGradient(null);
            }
        };
        // 이미 그라데이션인 칸도 같은 항목을 다시 누르면 별도 \"정지점 편집\" 단추 없이 곧바로 연다.
        mode.DropDownClosed += (_, _) =>
        {
            if (mode.SelectedIndex == 1) ShowEditor();
        };
        return Ui.Row(field, mode);
    }

    /// <summary>Keep gradient slots aligned with surviving entries after deleting an indexed color.</summary>
    private static IReadOnlyDictionary<string, EffectColorGradient> RemoveIndexedGradient(
        IReadOnlyDictionary<string, EffectColorGradient> gradients, string prefix, int removed)
    {
        var result = new Dictionary<string, EffectColorGradient>(StringComparer.Ordinal);
        string start = prefix + "[";
        foreach (var (key, value) in gradients)
        {
            if (!key.StartsWith(start, StringComparison.Ordinal))
            {
                result[key] = value;
                continue;
            }
            int close = key.IndexOf(']', start.Length);
            if (close < 0 || !int.TryParse(key.AsSpan(start.Length, close - start.Length), out int index))
            {
                result[key] = value;
                continue;
            }
            if (index == removed) continue;
            result[index > removed ? $"{start}{index - 1}{key[close..]}" : key] = value;
        }
        return result;
    }
}
