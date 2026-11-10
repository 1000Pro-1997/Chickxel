using Avalonia.Platform.Storage;
using Chickxel.App.Localization;
using Chickxel.Core.IO;

namespace Chickxel.App;

// 모양 펜 세트 모음은 설정(settings.json)과 따로 앱 설정 폴더의 ShapePen/shape-sets.json 한 파일에 둔다: 패널을 만들기 전에 읽고, 고칠 때마다 바로 저장.
// 세트 파일(.chickxel-shapes)을 창에 끌어 놓으면 모양 펜으로 바꾸고 새 세트로 가져온다.
public sealed partial class MainForm
{
    /// <summary>모양 펜 세트 파일(<see cref="Platform.AppFolder"/>/ShapePen/<see cref="StampLibrary.FileName"/>).</summary>
    private static string ShapeSetsPath => Path.Combine(Platform.AppFolder, "ShapePen", StampLibrary.FileName);

    /// <summary>세트 모음을 읽어 넣고, 고치면 저장하게 한다(생성자에서 패널보다 먼저 한 번).</summary>
    private void LoadShapeSets()
    {
        _ctx.ShapeSets = StampLibrary.Load(ShapeSetsPath);
        _ctx.ShapeSetsChanged += (_, _) => SaveShapeSets();
    }

    private void SaveShapeSets()
    {
        try { _ctx.ShapeSets.Save(ShapeSetsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage(UiStrings.FormatShapePen_SaveFailed(error: ex.Message));
        }
    }

    /// <summary>끌어 온 파일 중 모양 펜 세트 파일(<see cref="StampLibrary.SetFileExtension"/>).</summary>
    private static List<string> ShapeSetFiles(IDataTransfer data)
        => data.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>()
            .Where(p => p.EndsWith(StampLibrary.SetFileExtension, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];

    /// <summary>끌어 놓은 세트 파일을 가져온다: 모양 펜 도구로 바꿔 도구 설정에 새 세트가 보이게.</summary>
    private void ImportDroppedShapeSets(IReadOnlyList<string> paths)
    {
        _ctx.Tool = ToolKind.ShapePen;
        UpdateToolUi();
        _toolOptions.ImportShapeSets(paths);
    }
}
