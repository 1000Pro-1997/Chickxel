using Avalonia.Platform.Storage;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace Chickxel.App;

// 모양 펜 세트 모음은 설정(settings.json)과 따로 앱 설정 폴더의 ShapePen/shape-sets.json 한 파일에 둔다: 패널을 만들기 전에 읽고, 고칠 때마다 바로 저장.
// 상점·패키지 파일에서 받은 세트(에셋 저장소의 shape-set 에셋)는 그 파일에 넣지 않고 저장소가 바뀔 때마다 받은 세트 목록(읽기 전용)으로 넣는다.
// 세트 파일(.chickxel-shapes)을 창에 끌어 놓으면 모양 펜으로 바꾸고 새 세트로 가져온다.
public sealed partial class MainForm
{
    /// <summary>모양 펜 세트 파일(<see cref="Platform.AppFolder"/>/ShapePen/<see cref="StampLibrary.FileName"/>).</summary>
    private static string ShapeSetsPath => Path.Combine(Platform.AppFolder, "ShapePen", StampLibrary.FileName);

    /// <summary>
    /// 세트 모음을 읽어 넣고, 고치면 저장하게 한다(생성자에서 패널보다 먼저 한 번). 받은 세트는 에셋 저장소를 여는 시작 뒤(<see cref="DispatcherPriority.Background"/>)에
    /// 넣고 저장소가 바뀌면 다시 넣는다 — 그 전에 설정에서 고른 받은 세트는 넣는 순간 다시 골라진다(<see cref="EditorContext.ShapePenSet"/>).
    /// </summary>
    private void LoadShapeSets()
    {
        _ctx.ShapeSets = StampLibrary.Load(ShapeSetsPath);
        _ctx.ShapeSetsChanged += (_, _) => SaveShapeSets();
        Dispatcher.UIThread.Post(() =>
        {
            LoadInstalledShapeSets();
            AppAssets.Changed += () => Dispatcher.UIThread.Post(LoadInstalledShapeSets);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 받은 세트 목록을 저장소에서 다시 만든다: 상점·패키지 파일·앱 기본 설치에서 온 <c>shape-set</c> 에셋(이름표 = 에셋 ID, 이름 = 화면 언어의 이름 —
    /// <see cref="AssetEntry.DisplayName"/>), 기본 세트 다섯 가지가 먼저(<see cref="OfficialShapeSets.OrderOf"/>). 이 컴퓨터에서 만든 것(<see cref="AssetSources.Local"/>)은
    /// 게시할 때 떠 둔 내 세트의 사본이라 넣지 않는다 — 내 세트가 이미 목록에 있다.
    /// </summary>
    private void LoadInstalledShapeSets()
        => _ctx.InstalledShapeSets = AppAssets.Catalog.ShapeSets
            .Where(entry => entry.Source != AssetSources.Local && entry.ShapeSet is not null)
            .OrderBy(entry => OfficialShapeSets.OrderOf(entry.Id))
            .Select(entry =>
            {
                var set = ShapeSetAssetFormat.Copy(entry.ShapeSet!, entry.Id);
                set.Name = entry.DisplayName;
                return set;
            })
            .ToList();

    private void SaveShapeSets()
    {
        try { _ctx.ShapeSets.Save(ShapeSetsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage(UiStrings.FormatShapePen_SaveFailed(error: ex.Message));
        }
    }

    /// <summary>앱의 메인 창(없으면 null — 에셋 관리자·효과 만들기처럼 메인 창 밖에서 세트 모음에 닿을 때).</summary>
    internal static MainForm? Current
        => Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: MainForm main }
            ? main
            : null;

    /// <summary>모양 펜 세트 모음을 가진 편집 상태(에셋 관리자의 모양 펜 거르기가 내 세트를 보이고 고친다).</summary>
    internal EditorContext ShapeSetContext => _ctx;

    /// <summary>끌어 온 파일 중 모양 펜 세트 파일(<see cref="StampLibrary.SetFileExtension"/>).</summary>
    private static List<string> ShapeSetFiles(IDataTransfer data)
        => data.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>()
            .Where(p => p.EndsWith(StampLibrary.SetFileExtension, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];

    /// <summary>끌어 놓은 세트 파일을 가져온다: 모양 펜 도구로 바꿔 도구 설정에 새 세트가 보이게.</summary>
    private void ImportDroppedShapeSets(IReadOnlyList<string> paths)
    {
        _ctx.Tool = ToolKind.ShapePen;
        UpdateToolUi();
        if (ShapeSetActions.Import(this, _ctx, paths) is { Count: > 0 } imported) ShowMessage(ShapeSetActions.ImportedText(imported));
    }
}
