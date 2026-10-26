using Gpixel.App.Localization;
using Gpixel.Core.IO;

namespace Gpixel.App;

/// <summary>Shared persistence for the gradient preset catalog (editing UI: <see cref="PresetsWindow"/>).</summary>
public static class GradientPresetUi
{
    public static string Path => System.IO.Path.Combine(Platform.AppDataDirectory, "Gpixel", GradientPresets.FileName);

    /// <summary>Returns null on error so callers never replace a damaged catalog with an empty one.</summary>
    public static List<GradientPreset>? Load(Window owner)
    {
        try { return GradientPresets.Load(Path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Ui.Error(owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.GradientPreset_LoadError, ex.Message));
            return null;
        }
    }

    public static bool Save(Window owner, IReadOnlyList<GradientPreset> presets)
    {
        try
        {
            GradientPresets.Save(Path, presets);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Ui.Error(owner, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,
                UiStrings.GradientPreset_SaveError, ex.Message));
            return false;
        }
    }
}
