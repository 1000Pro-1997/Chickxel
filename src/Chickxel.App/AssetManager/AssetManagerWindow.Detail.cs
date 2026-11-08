using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>에셋 관리자 오른쪽 상세와 동작(가져오기·내보내기, 복사, 올리기, 삭제, 내 에셋 고치기).</summary>
public sealed partial class AssetManagerWindow
{
    private static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private void ShowDetail()
    {
        _detail.Children.Clear();
        if (Selected is { } entry) AssetDetail(entry);
        else _detail.Children.Add(Dim(UiStrings.Assets_SelectHint));
    }

    private void AddTitle(string text) => _detail.Children.Add(TitleText(text));

    private static TextBlock TitleText(string text)
        => new() { Text = text, FontWeight = FontWeight.Bold, FontSize = 15, Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap };

    private void Facts(params (string Label, object? Value)[] facts) => _detail.Children.Add(FactsGrid(facts));

    /// <summary>이름: 값 표(값은 글이나 컨트롤 — 글이 비면 그 줄을 뺀다).</summary>
    private static Grid FactsGrid(params (string Label, object? Value)[] facts)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 2, 0, 4) };
        int row = 0;
        foreach (var (label, value) in facts.Where(f => f.Value is Control or string { Length: > 0 }))
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = label, Foreground = AppTheme.DimTextBrush, Margin = new Thickness(0, 1, 12, 1) };
            var text = value as Control
                       ?? new SelectableTextBlock { Text = (string)value!, Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap };
            text.Margin = new Thickness(0, 1);
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(name);
            grid.Children.Add(text);
            row++;
        }
        return grid;
    }

    private void Actions(params Control?[] buttons) => _detail.Children.Add(ActionPanel(buttons));

    private static WrapPanel ActionPanel(params Control?[] buttons)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var button in buttons.OfType<Control>())
        {
            button.Margin = new Thickness(0, 0, 6, 6);
            panel.Children.Add(button);
        }
        return panel;
    }

    /// <summary>목록·내용 글의 종류 차례.</summary>
    private static int KindOrder(string kind) => kind switch
    {
        AssetKinds.EffectRecipe => 0,
        AssetKinds.Gradient => 1,
        AssetKinds.EffectScript => 2,
        _ => 3,
    };

    internal static string KindText(string kind) => kind switch
    {
        AssetKinds.Gradient => UiStrings.Assets_KindGradient,
        AssetKinds.EffectScript => UiStrings.Assets_KindScript,
        _ => UiStrings.Assets_KindEffect,
    };

    /// <summary>스크립트가 든 에셋의 실행 허락 안내(주황 글씨).</summary>
    private void ScriptNote() => _detail.Children.Add(new TextBlock
    {
        Text = UiStrings.Assets_ScriptNote, Foreground = AppTheme.WarningBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4),
    });

    private void Paragraph(string text)
        => _detail.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.TextBrush });

    /// <summary>
    /// 에셋 상세: 미리보기, 설명, 정보 표(종류·작성자·라이선스·상점 ID·버전·고침·출처·들어온 시각·필요한 앱 버전·스크립트), 스크립트 설정·레시피 효과 목록, 동작.
    /// 내 에셋은 그 자리에서 고치고(이름, 매개변수, 효과 만들기) 올릴 수 있고, 받은 에셋은 내 에셋으로 복사해서 고친다.
    /// </summary>
    private void AssetDetail(AssetEntry entry)
    {
        var culture = CultureInfo.CurrentUICulture;
        string? handle = AppAssets.MyHandle;
        bool mine = entry.IsOwnedBy(handle);
        AddTitle(entry.DisplayName);
        if (entry.Gradient is { } gradient)
            _detail.Children.Add(new GradientSwatch { Preset = gradient, Height = 48 });
        if (entry.EffectTemplate is not null || entry.PreviewPath is not null)
            _detail.Children.Add(new AssetPreviewView(entry));
        string? description = entry.Script is { } described ? described.DescriptionFor(culture)
            : entry.Recipe is { } recipeText ? recipeText.Description
            : entry.Store?.DisplayDescription;
        if (!string.IsNullOrEmpty(description)) Paragraph(description);

        var store = entry.Store;
        var scripts = entry.ScriptHashes;
        bool allowed = scripts.All(hash => ScriptLibrary.TrustOf(hash) == ScriptTrust.Trusted);
        Facts(
            (UiStrings.Assets_Kind, KindText(entry.Kind)),
            (UiStrings.Assets_Author, IdentityBadge.Author(store is not null ? entry.Author : null, 16, fallback: entry.Author?.Name ?? UiStrings.Assets_SourceLocal)),
            (UiStrings.Assets_License, entry.License),
            (UiStrings.Assets_StoreId, store?.PackageId),
            (UiStrings.Assets_Version, store?.Version),
            (UiStrings.Assets_Status, entry.Modified ? UiStrings.Assets_ModifiedFact : null),
            (UiStrings.Assets_Source, AppAssets.SourceText(entry.Source)),
            (UiStrings.Assets_AddedAt, entry.AddedUtc == default ? null : entry.AddedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
            (UiStrings.Assets_MinApp, store is { MinAppVersion: not "0.0.0" } ? store.MinAppVersion : null),
            (UiStrings.Assets_ScriptHash, entry.Script is { } script ? script.Hash[..12] : null),
            (UiStrings.Assets_ScriptRun, scripts.Count == 0 ? null : allowed ? UiStrings.Assets_ScriptRunAllowed : UiStrings.Assets_ScriptRunNotAllowed));
        if (scripts.Count > 0 && !allowed) ScriptNote();
        if (entry.Script is { } declared)
            _detail.Children.Add(Dim(F(UiStrings.Assets_ScriptSettingCount, declared.Settings.Count)
                                     + (declared.Settings.Count > 0
                                         ? ": " + string.Join(", ", declared.Settings.Select(s => declared.LabelFor(s, culture)))
                                         : "")));

        if (entry.Recipe is { } recipe)
        {
            _detail.Children.Add(Ui.Label(F(UiStrings.Assets_EffectCount, recipe.Effects.Count), bold: true));
            foreach (var effect in recipe.Effects)
                _detail.Children.Add(Dim("  " + effect.DisplayName + (effect.ClipToPrevious ? UiStrings.EffectPreset_ClippedSuffix : "")));
            _detail.Children.Add(Dim(recipe.FrameRange == RecipeFrameRange.AllFrames ? UiStrings.Assets_FrameRangeAll : UiStrings.Assets_FrameRangeAdd));
            if (recipe.Parameters.Count > 0)
            {
                _detail.Children.Add(Ui.Label(F(UiStrings.RecipeParams_Count, recipe.Parameters.Count), bold: true));
                foreach (var parameter in recipe.Parameters)
                    _detail.Children.Add(Dim("  " + parameter.Name + " · " + RecipeParametersDialog.KindText(parameter)));
            }
        }

        var favorite = entry.Kind == AssetKinds.Gradient
            ? Ui.Button(VectorIcon.Star, entry.Favorite ? UiStrings.GradientPreset_RemoveFavorite : UiStrings.GradientPreset_AddFavorite,
                (_, _) => Act(() => AppAssets.Store.SetFavorite(entry.Id, !entry.Favorite)))
            : null;
        var export = Ui.Button(VectorIcon.Upload, UiStrings.Assets_Export, (_, _) => Export([entry]));
        var delete = Ui.Button(VectorIcon.Trash, UiStrings.Assets_Delete, (_, _) => Delete(entry));
        if (mine)
        {
            bool listed = AssetPublishCheck.IsOwnListed(entry, handle);
            Actions(
                favorite,
                entry.Kind == AssetKinds.EffectScript
                    ? Ui.Button(VectorIcon.Sparkles, UiStrings.EffectMaker_EditIn, (_, _) =>
                    {
                        // 에셋 관리자는 모달이라 닫고 효과 만들기(메인 창에 딸린 창)로 넘어간다
                        Close();
                        EffectMakerWindow.OpenFromAnywhere(entry.Id);
                    })
                    : null,
                entry.Script is not null ? null : Ui.Button(VectorIcon.Pencil, UiStrings.Assets_Rename, (_, _) => Rename(entry)),
                entry.Recipe is null ? null
                    : Ui.Button(VectorIcon.SlidersHorizontal, UiStrings.RecipeParams_Edit, (_, _) => RecipeParametersDialog.Edit(this, entry)).Tip(UiStrings.RecipeParams_EditTip),
                export,
                entry.Kind == AssetKinds.EffectRecipe ? null
                    : Ui.Button(VectorIcon.Globe, listed ? UiStrings.Assets_PublishNewVersion : UiStrings.Assets_Publish,
                        async (_, _) => await AssetPublishDialog.RunAsync(this, entry.Id))
                        .Tip(listed ? UiStrings.Assets_PublishNewVersionTip : UiStrings.Assets_PublishEntryTip),
                delete);
        }
        else
            Actions(
                favorite,
                Ui.Button(VectorIcon.Copy, UiStrings.Assets_CopyToPersonal, (_, _) => CopyAsMine(entry)).Tip(UiStrings.Assets_CopyToPersonalTip),
                export,
                entry.Author?.Url is { } url ? Ui.Button(VectorIcon.ExternalLink, UiStrings.Assets_WebPage, (_, _) => Updater.OpenUrl(this, url)).Tip(url) : null,
                entry.LicenseFilePath is { } license ? Ui.Button(VectorIcon.Info, UiStrings.Assets_ViewLicense, (_, _) => ShowLicense(entry, license)) : null,
                delete);
        // 동작 단추 줄: 내 에셋·받은 에셋 표의 줄로.
        _detail.Children[^1].HelpAt(HelpPage, anchor: mine ? "My assets" : "Received assets");
    }

    // ───────────── 동작 ─────────────

    /// <summary>저장소를 바꾸고 실패하면 알린다(목록은 <see cref="AppAssets.Changed"/>로 다시 만들어진다).</summary>
    private bool Act(Action action) => AppAssets.Try(this, action, UiStrings.Assets_ActionError);

    private static FileFilter PackageFilter => new(UiStrings.Assets_FileType, "*" + AssetPackage.Extension);

    private void Import()
    {
        if (Ui.OpenFile(this, UiStrings.Assets_ImportTitle, null,
                new FileFilter(UiStrings.Assets_FileType, "*" + AssetPackage.Extension, "*" + AssetPackage.LegacyExtension)) is not { } path) return;
        InstallFile(path);
    }

    /// <summary>패키지 파일 <paramref name="path"/>의 에셋을 가져오고(겹치면 묻고, 실패하면 알림) 설치됨 탭에서 처음 들어간 에셋을 고른다.</summary>
    private void InstallFile(string path)
    {
        if (AppAssets.InstallFile(this, path) is not { } installed) return;
        _tabs.SelectedIndex = (int)AssetManagerTab.Installed;
        if (installed.Count > 0)
        {
            _selectKey = AssetKey(installed[0].Id);
            _search.Text = "";
            _filter.SelectedIndex = FilterIndex(installed[0].Kind);
        }
        Refresh();
    }

    /// <summary>
    /// 에셋을 패키지 파일 하나로 내보낸다(백업·전달용, 온라인 목록에는 올릴 수 없음 — <see cref="AssetStore.Export"/>). 고치지 않은 받은 에셋 하나면 그 상점 패키지
    /// 그대로 쓰고, 그 밖에는 패키지 이름만 묻고 개인 묶음(ID <c>personal.이름</c>, 버전 1.0.0, 라이선스 <c>LicenseRef-Personal</c>, 작성자 개인
    /// <c>personal</c> + 계정 표시 이름) 아래에 에셋마다 작성자·라이선스·상점 연결을 적는다 — 이 컴퓨터에서 만든 에셋의 작성자는 내 계정(<see cref="LocalAuthor"/>).
    /// 공개 게시는 <see cref="PublishSelected"/>.
    /// </summary>
    private void Export(IReadOnlyList<AssetEntry> entries)
    {
        if (entries.Count == 0)
        {
            Ui.Info(this, UiStrings.Assets_ExportEmpty, UiStrings.Assets_ExportTitle);
            return;
        }
        string suggested, name;
        if (entries is [{ Store: { } store, Author: not null, License: not null, Modified: false }])
        {
            suggested = $"{store.PackageId}-{store.Version}";
            name = store.Name;
        }
        else
        {
            if (InputDialog.Ask(this, UiStrings.Assets_ExportTitle, UiStrings.Assets_ExportNamePrompt,
                    entries.Count == 1 ? entries[0].Name : UiStrings.Assets_DefaultPackageName) is not { } asked) return;
            name = asked;
            suggested = AssetPackageBuilder.Slug(name, "assets");
        }
        if (Ui.SaveFile(this, UiStrings.Assets_ExportTitle, null, suggested + AssetPackage.Extension, AssetPackage.Extension, PackageFilter) is not { } path) return;
        string profile = AccountName() ?? App.Settings.AssetPublish.AuthorName;
        string user = AssetManifest.IsValidName(profile) ? profile.Trim()
            : Environment.UserName is { Length: > 0 } u && AssetManifest.IsValidName(u) ? u : "Chickxel";
        var header = new AssetManifest
        {
            Id = AssetStore.LocalIdPrefix + AssetPackageBuilder.Slug(name, "assets"),
            Version = "1.0.0",
            Name = name.Length > AssetManifest.MaxNameLength ? name[..AssetManifest.MaxNameLength] : name,
            Author = new AssetAuthor(AssetStore.PersonalAuthorId, user),
            License = "LicenseRef-Personal",
            MinAppVersion = AppInfo.IsDevBuild ? "0.0.0" : AppInfo.VersionText,
        };
        Act(() =>
        {
            using var buffer = new MemoryStream();
            AppAssets.Store.Export(entries.Select(e => e.Id), header, buffer, LocalAuthor(), entry => AssetPreview.Package(entry.Data));
            File.WriteAllBytes(path, buffer.ToArray());
        });
    }

    /// <summary>로그인한(저장한) 계정의 표시 이름(모르면 null).</summary>
    private static string? AccountName() => AppCommunity.Client.User?.Name ?? AppCommunity.Client.Saved.Current?.User?.Name;

    /// <summary>
    /// 내보낼 때 이 컴퓨터에서 만든 에셋의 작성자: 내 핸들(<see cref="AppAssets.MyHandle"/>)과 계정 표시 이름(모르면 게시 창이 기억한 이름, 그것도 없으면 핸들).
    /// 핸들을 모르면 null(받는 쪽에서도 작성자 없이 보임).
    /// </summary>
    private static AssetAuthor? LocalAuthor()
    {
        if (AppAssets.MyHandle is not { } handle) return null;
        string? name = new[] { AccountName(), App.Settings.AssetPublish.AuthorName }.FirstOrDefault(AssetManifest.IsValidName);
        return new AssetAuthor(handle, name?.Trim() ?? handle);
    }

    /// <summary>
    /// 온라인에 올리기(게시 창, 로그인 필요): 목록에서 고른 내 에셋을(고른 것이 없고 목록에 보이는 올릴 수 있는 내 에셋이 하나뿐이면 그것을) 연다.
    /// 받은 에셋을 골랐으면 복사해서 올리라고, 그 밖에는 하나를 고르라고 알린다. 효과 프리셋은 올리지 않는다.
    /// </summary>
    private async void PublishSelected()
    {
        if (Selected is { } selected && !AppAssets.IsMine(selected))
        {
            Ui.Info(this, UiStrings.Publish_NotMine, UiStrings.Publish_Title);
            return;
        }
        var shown = Shown().Where(e => AppAssets.IsMine(e) && e.Kind != AssetKinds.EffectRecipe).ToList();
        var asset = Selected is { Kind: not AssetKinds.EffectRecipe } chosen ? chosen : shown.Count == 1 ? shown[0] : null;
        if (asset is null)
        {
            Ui.Info(this, shown.Count == 0 ? UiStrings.Publish_NoPersonal : UiStrings.Publish_ChooseAsset, UiStrings.Publish_Title);
            return;
        }
        await AssetPublishDialog.RunAsync(this, asset.Id);
    }

    /// <summary>에셋을 지운다(묻고). 공식 효과면 "기본 효과 되돌리기"로 다시 넣을 수 있다고 덧붙인다.</summary>
    private void Delete(AssetEntry entry)
    {
        string question = F(UiStrings.Assets_DeleteAssetQuestion, entry.DisplayName);
        if (OfficialEffectPackages.IsOfficial(entry)) question += "\n\n" + UiStrings.Assets_DeleteOfficialNote;
        if (Ui.Ask(this, question, UiStrings.Assets_Delete, MessageButtons.YesNo, MessageIcon.Question) != DialogResult.Yes) return;
        Act(() => AppAssets.Store.Remove(entry.Id));
    }

    /// <summary>받은 패키지에 딸린 라이선스 문서를 보인다(앞 4000자).</summary>
    private void ShowLicense(AssetEntry entry, string file)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text = ex.Message;
        }
        if (text.Length > 4000) text = text[..4000] + "…";
        Ui.Info(this, text, entry.DisplayName + " · " + UiStrings.Assets_License);
    }

    /// <summary>내 효과 프리셋·그라데이션의 이름을 바꾼다(같은 종류의 내 에셋과 이름이 겹치면 알림). 스크립트 이름은 원문의 선언이라 여기서 바꾸지 않는다.</summary>
    private void Rename(AssetEntry entry)
    {
        string? handle = AppAssets.MyHandle;
        var siblings = AppAssets.Catalog.Available(entry.Kind).Where(e => e.Id != entry.Id && e.IsOwnedBy(handle)).Select(e => e.Name).ToList();
        string? name = InputDialog.Ask(this, UiStrings.Assets_Rename, UiStrings.Assets_NamePrompt, entry.Name, siblings)?.Trim();
        if (string.IsNullOrEmpty(name) || name == entry.Name) return;
        if (siblings.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            Ui.Warn(this, F(UiStrings.Assets_DuplicateName, name), UiStrings.Assets_Rename);
            return;
        }
        Act(() =>
        {
            if (entry.Recipe is { } recipe) AppAssets.Store.SaveRecipe(recipe with { Name = name });
            else if (entry.Gradient is { } gradient) AppAssets.Store.SaveGradient(entry.Id, gradient with { Name = name });
        });
    }

    /// <summary>받은 에셋을 내 에셋으로 복사하고(원본은 그대로 — 고칠 때 쓰는 사본) 그 사본을 고른다.</summary>
    private void CopyAsMine(AssetEntry entry)
    {
        if (AppAssets.CopyAsMine(this, entry) is not { } saved) return;
        _selectKey = AssetKey(saved.Id);
        Refresh();
    }
}
