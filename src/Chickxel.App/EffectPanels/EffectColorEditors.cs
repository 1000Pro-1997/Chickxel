using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>
    /// Solid color picker plus spatial gradient mode for one effect color slot. <paramref name="trailing"/> (e.g. list item buttons)
    /// stays on the first row next to the mode picker in both modes.
    /// </summary>
    internal static Control EffectColorEditor(LayerEffect effect, string slot, ColorField field,
        Action<Func<LayerEffect, LayerEffect>> set, Control? trailing = null)
    {
        bool gradientMode = effect.ColorGradients.ContainsKey(slot);
        EffectColorGradient? savedGradient = effect.ColorGradients.TryGetValue(slot, out var currentGradient)
            ? currentGradient : null;
        var mode = Ui.Combo(104, UiStrings.S_EffectColorEditors_EffectColorEditor_87B10DCF76, UiStrings.S_EffectColorEditors_EffectColorEditor_14AD8463A1);
        var editor = new GradientEditor(spatialAngle: true) { Width = 280 };
        var body = Ui.Stack(trailing is null ? Ui.Row(field, mode) : Ui.Row(field, mode, trailing), editor);
        body.Margin = new Thickness(0);

        EffectColorGradient InitialGradient()
            => new()
            {
                Stops = [new GradientStop(0, field.Color), new GradientStop(1, field.Color)],
                AlphaStops = EditorContext.DefaultGradientAlphaStops
            };

        EffectColorGradient EditedGradient() => new()
        {
            Stops = editor.Stops,
            AlphaStops = editor.AlphaStops,
            Angle = editor.Angle,
            Shape = editor.Shape,
            Animated = editor.Animated,
            Reverse = editor.Reverse,
            Cycles = editor.Cycles,
            Easing = editor.Easing,
            PingPong = editor.PingPong,
            Interpolation = editor.Interpolation,
            ColorSpace = editor.ColorSpace,
            Dither = editor.Dither,
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

        editor.Changed += () =>
        {
            savedGradient = EditedGradient();
            ChangeGradient(savedGradient);
        };

        mode.SelectedIndex = gradientMode ? 1 : 0;
        field.IsVisible = !gradientMode;
        editor.IsVisible = gradientMode;
        if (savedGradient is { } existing)
            editor.Load(existing.Stops, existing.AlphaStops, existing.Interpolation, existing.Dither,
                existing.Angle, existing.Shape, existing.Animated, existing.Reverse, existing.Cycles, existing.Easing, existing.PingPong,
                existing.ColorSpace);
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex < 0) return;
            bool enabled = mode.SelectedIndex == 1;
            if (enabled == gradientMode) return;
            field.IsVisible = !enabled;
            editor.IsVisible = enabled;
            if (enabled)
            {
                savedGradient ??= InitialGradient();
                editor.Load(savedGradient.Stops, savedGradient.AlphaStops, savedGradient.Interpolation, savedGradient.Dither,
                    savedGradient.Angle, savedGradient.Shape, savedGradient.Animated, savedGradient.Reverse, savedGradient.Cycles,
                    savedGradient.Easing, savedGradient.PingPong, savedGradient.ColorSpace);
                gradientMode = true;
                ChangeGradient(savedGradient);
            }
            else
            {
                gradientMode = false;
                ChangeGradient(null);
            }
        };
        return body;
    }

    /// <summary>
    /// Move indexed gradient slots (<c>prefix[i]…</c>) to <paramref name="newIndex"/>(i); a negative result drops the slot.
    /// Other slots are kept as they are.
    /// </summary>
    internal static IReadOnlyDictionary<string, EffectColorGradient> ReindexGradients(
        IReadOnlyDictionary<string, EffectColorGradient> gradients, string prefix, Func<int, int> newIndex)
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
            if (close < 0 || !int.TryParse(key.AsSpan(start.Length, close - start.Length), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int index))
            {
                result[key] = value;
                continue;
            }
            int moved = newIndex(index);
            if (moved < 0) continue;
            result[string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{start}{moved}{key[close..]}")] = value;
        }
        return result;
    }
}
