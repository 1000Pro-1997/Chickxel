using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.Community;
using Chickxel.Core.IO;
using Chickxel.Core.Scripting;

namespace Chickxel.App;

/// <summary>
/// 에셋 게시 창(계획 8.4, 9.3, 14장 5단계): 에셋 하나(개인 에셋이나 내가 만든 설치 패키지의 에셋)를 공개 패키지로 Chickxel 커뮤니티 서비스에 올린다(로그인 필요).
/// 왼쪽은 작성자(로그인한 계정의 핸들·표시 이름, 고칠 수 없음), 패키지 묶음 맨 위의 모드 줄(서버에 물어 "새 패키지" / "이름 업데이트: 1.0.0 → 1.0.1"),
/// 이름·짧은 설명(언어 설정으로 언어별 이름·설명), 고칠 수 없는 패키지 ID(<see cref="AssetPublishCheck.ResolveTarget"/> — 내 패키지면 그 ID, 아니면 핸들.이름 조각),
/// 버전(이미 올린 버전에서 다음 수 버전 제안)·태그(<see cref="TagChipEditor"/>), 고정 라이선스(<see cref="AssetPublishCheck.License"/>),
/// 오른쪽은 올릴 에셋, 목록 미리보기(에셋 미리보기나 고른 PNG), 올리기 전 검사 결과.
/// 내 설치 패키지의 에셋은 그 패키지 정보로, 스크립트 효과는 선언의 이름·설명·번역·태그로, 그라데이션은 전에 올린 기록(없으면 에셋 이름)으로 채운다.
/// 입력이 바뀔 때마다 <see cref="AssetPublishCheck.Check"/>로 다시 검사하고, 오류가 없을 때만 올린다(진행 막대·취소).
/// 서버가 거절하면 그 까닭을 검사 목록에 보이고, 올리면 브라우저로 패키지 쪽(<c>/packages/&lt;ID&gt;</c>)을 연다.
/// </summary>
public sealed class AssetPublishDialog : DialogBase
{
    private const int FieldWidth = 300;

    private readonly AssetPublishSettings _settings = App.Settings.AssetPublish;
    private readonly IReadOnlyList<RemoteCatalogEntry> _catalog;
    private readonly AssetEntry _asset;
    private readonly PackagePreview? _assetPreview;
    private CommunityUser _user;
    private readonly TextBox _version, _name, _summary;
    private readonly TagChipEditor _tags;
    private readonly SelectableTextBlock _authorId, _authorName, _idText;
    private readonly TextBlock _languages = Note(""), _checksSummary = Note(""), _previewNote = Note("");
    /// <summary>패키지 묶음 맨 위의 모드 줄(새 패키지 / 업데이트 / 확인 중 / 확인 못 함)과 그 아래 덧붙임.</summary>
    private readonly TextBlock _mode = new() { FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _modeNote = Note("");
    private readonly IconView _modeIcon = new(VectorIcon.LoaderCircle, AppTheme.DimTextBrush, 2.2) { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
    private readonly Button _useSuggestion, _previewAuto;
    private readonly StackPanel _checks = new() { Spacing = 4 };
    private readonly PngView _preview = new() { Height = 170 };
    private readonly DispatcherTimer _recheck = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4, MinHeight = 4, Width = 220, Foreground = AppTheme.AccentBrush };
    private readonly TextBlock _progressText = Note("");
    private readonly StackPanel _progressRow;
    private readonly Control _columns;
    /// <summary>언어별 이름·짧은 설명(언어 설정 창에서 고침, 언어 차례).</summary>
    private IReadOnlyList<KeyValuePair<string, AssetTranslation>> _translations;
    private byte[]? _customPreview;
    private bool _versionEdited, _filling = true;
    /// <summary>지금 이름으로 정한 패키지 ID와 그 출처(<see cref="AssetPublishCheck.ResolveTarget"/>).</summary>
    private PublishTarget _target = new("", PublishIdSource.Derived, null);
    /// <summary>ID별 서버 조회 결과: 끝나지 않았으면 없음, 실패면 <see cref="Lookup.Failed"/>.</summary>
    private readonly Dictionary<string, Lookup> _lookups = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _closing = new();
    private string _suggested = "1.0.0";
    private PublishResult? _result;
    private CancellationTokenSource? _upload;

    /// <summary>서버 조회 결과 하나: 올린 패키지(없으면 null — 새 패키지), 연결 실패면 <see cref="Failed"/>.</summary>
    private sealed record Lookup(CommunityPackage? Package, bool Failed);

    /// <summary>
    /// 에셋 <paramref name="assetId"/>(개인 에셋이나 내가 만든 설치 패키지의 에셋)의 게시 창을 연다. 로그인하지 않았으면 먼저 계정 창.
    /// 효과 레시피(기본 효과를 합친 것)는 올리지 않는다(<see cref="PublishIssueCode.RecipeNotPublishable"/>, 서버도 거부).
    /// </summary>
    public static async Task RunAsync(Window owner, string assetId)
    {
        if (AppAssets.Catalog.Find(assetId) is not { } asset)
        {
            Ui.Info(owner, UiStrings.Publish_NoPersonal, UiStrings.Publish_Title);
            return;
        }
        if (asset.Kind == AssetKinds.EffectRecipe)
        {
            Ui.Info(owner, Chickxel.Core.Localization.DomainStrings.Publish_RecipeNotPublishable, UiStrings.Publish_Title);
            return;
        }
        if (await AppCommunity.RequireUserAsync(owner, UiStrings.Publish_LoginReason) is not { } user) return;
        new AssetPublishDialog(asset, user).ShowDialog(owner);
    }

    private AssetPublishDialog(AssetEntry asset, CommunityUser user) : base(UiStrings.Publish_Title, UiStrings.Publish_Upload)
    {
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        Width = 980;
        Height = 740;
        MinWidth = 820;
        MinHeight = 560;
        OkButton.Width = double.NaN;
        OkButton.MinWidth = 90;
        OkButton.Background = AppTheme.AccentBrush;
        OkButton.Foreground = Brushes.White;
        OkButton.FontWeight = FontWeight.Bold;
        _catalog = AppAssets.Remote.LoadCache()?.Packages ?? [];
        _asset = asset;
        _user = user;
        _assetPreview = AssetPreview.Package(asset.Data);
        // 개인 내보내기의 작성자 이름(로그인하지 않았을 때)으로 계정 표시 이름을 기억한다.
        _settings.AuthorName = user.Name;

        // 채울 값: 내 설치 패키지의 에셋은 그 패키지 정보(이름·설명·번역), 스크립트 효과는 선언(이름·설명·번역·태그),
        // 그 밖은 전에 올린 기록이나 에셋 이름.
        var record = _settings.Saved.FirstOrDefault(r => r.Assets.Contains(asset.Id));
        string name, summary;
        IReadOnlyList<string> tags;
        if (asset.Origin == AssetOrigin.Package && AppAssets.Catalog.FindPackage(asset.PackageId ?? "")?.Manifest is { } own && own.Author.Id == user.Handle)
        {
            name = own.Name;
            summary = own.Description ?? "";
            _translations = own.Translations.ToList();
            tags = asset.Script?.Tags.Count > 0 ? asset.Script.Tags : record?.Tags ?? [];
        }
        else if (asset.Script is { } script)
        {
            name = script.Name;
            summary = script.Description ?? "";
            _translations = ScriptTranslationsOf(script);
            tags = script.Tags.Count > 0 ? script.Tags : record?.Tags ?? [];
        }
        else
        {
            name = record?.Name ?? asset.Name;
            summary = record?.Summary ?? "";
            _translations = record?.Translations.ToList() ?? [];
            tags = record?.Tags ?? [];
        }

        // ── 왼쪽: 작성자·패키지·라이선스 ──
        // 작성자 ID = 계정 핸들(서버가 패키지 ID 첫 조각·작성자 ID를 핸들과 맞춰 봄), 표시 이름 = 계정 이름. 둘 다 계정 창에서 바꾼다.
        _authorId = ReadOnlyText(user.Handle);
        _authorName = ReadOnlyText(user.Name);
        _name = Field(name);
        _name.MaxLength = AssetManifest.MaxNameLength;
        _summary = new TextBox
        {
            Text = summary, Width = FieldWidth, Height = 56, Margin = new Thickness(3), TextWrapping = TextWrapping.Wrap,
            MaxLength = RemoteCatalogFormat.MaxSummaryLength, Watermark = UiStrings.Publish_SummaryHint,
        };
        var languages = Ui.Button(VectorIcon.Languages, UiStrings.Publish_Languages, (_, _) => EditLanguages()).Tip(UiStrings.Publish_LanguagesTip);
        // 패키지 ID는 고칠 수 없다: 내 패키지(설치한 것이나 그 사본)면 그 ID, 아니면 "핸들.이름 조각"(UpdateTarget).
        _idText = new SelectableTextBlock { Foreground = AppTheme.DimTextBrush, FontSize = AppTheme.SmallFontSize, Margin = new Thickness(3, 6, 3, 3) };
        _version = Field("1.0.0");
        _version.Width = 110;
        _tags = new TagChipEditor(FieldWidth);
        _tags.SetTags(tags);
        _useSuggestion = Ui.Button(UiStrings.Publish_UseSuggestion, (_, _) =>
        {
            _versionEdited = false;
            _version.Text = _suggested;
        });
        var mode = new Border
        {
            Background = AppTheme.PanelBrush, BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 6), Margin = new Thickness(0, 0, 0, 4),
            Child = new StackPanel { Children = { new DockPanel { Children = { _modeIcon, _mode } }, _modeNote } },
        };
        DockPanel.SetDock(_modeIcon, Dock.Left);
        _modeNote.Margin = new Thickness(21, 2, 0, 0);

        var left = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Note(UiStrings.Publish_Intro),
                Ui.Group(UiStrings.Publish_Author, Ui.Stack(
                    Rows((UiStrings.Publish_AuthorId, _authorId, UiStrings.Publish_AuthorIdTip),
                        (UiStrings.Publish_AuthorName, _authorName, null)),
                    Note(UiStrings.Publish_AuthorFromAccount))),
                Ui.Group(UiStrings.Publish_Package, Ui.Stack(
                    mode,
                    Rows((UiStrings.Publish_Name, _name, null),
                        (UiStrings.Publish_Summary, _summary, null),
                        (UiStrings.Publish_LanguagesLabel, Ui.Stack(languages, _languages), null),
                        (UiStrings.Publish_Id, _idText, UiStrings.Publish_IdTip),
                        (UiStrings.Publish_Version, Ui.Row(_version, _useSuggestion), null),
                        (UiStrings.Publish_Tags, _tags, UiStrings.Publish_TagsTip)))),
                Ui.Group(UiStrings.Publish_License, Ui.Stack(
                    Ui.Row(Ui.Icon(VectorIcon.Check, 13), Ui.Label(AssetPublishCheck.License, bold: true)),
                    Note(UiStrings.Publish_LicenseFixed))),
            },
        };

        // ── 오른쪽: 에셋·미리보기·검사 ──
        var assetRow = Ui.Row(Ui.Icon(asset.Kind == AssetKinds.Gradient ? VectorIcon.SlidersHorizontal : VectorIcon.Code, 14),
            Ui.Label(asset.DisplayName, bold: true), Ui.Label("·  " + AssetManagerWindow.KindText(asset.Kind)));
        foreach (var child in assetRow.Children) child.VerticalAlignment = VerticalAlignment.Center;
        var replace = Ui.Button(VectorIcon.Image, UiStrings.Publish_PreviewReplace, (_, _) => ChoosePreview());
        _previewAuto = Ui.Button(UiStrings.Publish_PreviewAuto, (_, _) =>
        {
            _customPreview = null;
            ShowPreview();
            Changed();
        });
        var previewHeader = Ui.Row(Ui.Label(UiStrings.Publish_Preview, bold: true), Ui.InfoBadge(UiStrings.Publish_PreviewTip));

        var right = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Group(UiStrings.Publish_Asset, Ui.Stack(assetRow)),
                new Border
                {
                    BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 4, 6, 6), Margin = new Thickness(2, 4, 2, 2),
                    Child = new StackPanel { Spacing = 2, Children = { previewHeader, _preview, new WrapPanel { Children = { replace, _previewAuto } }, _previewNote } },
                },
                Ui.Group(UiStrings.Publish_Checks, Ui.Stack(_checksSummary, _checks)),
            },
        };

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var leftScroll = Ui.ScrollHost(left);
        var rightScroll = Ui.ScrollHost(right);
        rightScroll.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(rightScroll, 1);
        columns.Children.Add(leftScroll);
        columns.Children.Add(rightScroll);
        _columns = columns;
        var cancelUpload = Ui.Button(VectorIcon.X, UiStrings.Publish_CancelUpload, (_, _) => _upload?.Cancel());
        _progress.VerticalAlignment = _progressText.VerticalAlignment = VerticalAlignment.Center;
        _progressRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), IsVisible = false, Children = { _progress, _progressText, cancelUpload } };
        DockPanel.SetDock(_progressRow, Dock.Bottom);
        var root = (DockPanel)Content!;
        root.Children.Remove(Body);
        root.Children.Add(_progressRow);
        root.Children.Add(columns);

        _summary.TextChanged += (_, _) => Changed();
        _tags.TagsChanged += (_, _) => Changed();
        _name.TextChanged += (_, _) =>
        {
            UpdateTarget();
            Changed();
        };
        _version.TextChanged += (_, _) =>
        {
            _versionEdited = _version.Text != _suggested;
            _useSuggestion.IsVisible = _versionEdited;
            ShowMode();
            Changed();
        };
        _recheck.Tick += (_, _) =>
        {
            _recheck.Stop();
            RunCheck();
        };
        OkButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.None; // 올린 뒤에 닫는다
            Dispatcher.UIThread.Post(Submit);
        };
        CancelButtonControl.Click += (_, _) =>
        {
            if (_upload is null) return;
            _upload.Cancel();
            DialogResult = DialogResult.None; // 올리기만 멈추고 창은 둔다
        };
        Closed += (_, _) =>
        {
            _recheck.Stop();
            _upload?.Cancel();
            _closing.Cancel();
            App.Settings.Save();
        };

        ShowLanguages();
        ShowPreview();
        UpdateTarget();
        _filling = false;
        RunCheck();
    }

    /// <summary>스크립트 선언의 번역에서 이름·설명이 있는 언어만(설정 이름만 있는 언어는 뺌, 패키지 규칙에 맞지 않는 값도 뺌).</summary>
    private static List<KeyValuePair<string, AssetTranslation>> ScriptTranslationsOf(ScriptAsset script)
        => script.Translations
            .Select(pair => new KeyValuePair<string, AssetTranslation>(pair.Key, new AssetTranslation(
                AssetManifest.IsValidName(pair.Value.Name) ? pair.Value.Name!.Trim() : null,
                RemoteCatalogFormat.IsValidSummary(pair.Value.Description) ? pair.Value.Description!.Trim() : null)))
            .Where(pair => pair.Value.Name is not null || pair.Value.Summary is not null)
            .Take(AssetTranslations.MaxLanguages)
            .ToList();

    // ───────────── 입력 ─────────────

    private static TextBox Field(string value, string? watermark = null)
        => new() { Text = value, Width = FieldWidth, Margin = new Thickness(3), Watermark = watermark };

    private static SelectableTextBlock ReadOnlyText(string value) => new()
    {
        Text = value, Foreground = AppTheme.TextBrush, Margin = new Thickness(3, 6, 3, 3), FontWeight = FontWeight.SemiBold,
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 2, 3, 4),
    };

    /// <summary>이름표 | 칸 표(이름표 옆 풍선 도움말 선택).</summary>
    private static Grid Rows(params (string Label, Control Field, string? Tip)[] rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = Ui.Label(rows[i].Label);
            text.Margin = new Thickness(0);
            Control label = rows[i].Tip is { } tip ? Ui.Row(text, Ui.InfoBadge(tip)) : text;
            label.MinWidth = 96;
            label.VerticalAlignment = VerticalAlignment.Top;
            label.Margin = new Thickness(0, 6, 6, 0);
            rows[i].Field.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            grid.Children.Add(label);
            grid.Children.Add(rows[i].Field);
        }
        return grid;
    }

    private static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private void Changed()
    {
        if (_filling) return;
        _recheck.Stop();
        _recheck.Start();
    }

    /// <summary>
    /// 패키지 ID를 다시 정하고(이름·영어 이름·계정이 바뀔 때) 그 ID를 서버에 물어 모드(새 패키지 / 업데이트)와 다음 버전을 맞춘다.
    /// </summary>
    private void UpdateTarget()
    {
        string? english = _translations.FirstOrDefault(pair => string.Equals(pair.Key, "en", StringComparison.OrdinalIgnoreCase)).Value?.Name;
        _target = AssetPublishCheck.ResolveTarget(_user.Handle, _asset, AppAssets.Catalog, _name.Text, english, _asset.Name);
        _idText.Text = _target.Id;
        if (!_lookups.ContainsKey(_target.Id) && AssetManifest.IsValidId(_target.Id)) LookUp(_target.Id);
        UpdateVersion();
    }

    /// <summary>서버에 그 ID의 패키지를 묻는다(결과는 ID별로 기억, 지금 ID면 모드·버전을 다시 맞춤).</summary>
    private async void LookUp(string id)
    {
        Lookup result;
        try
        {
            result = new Lookup(await AppCommunity.Client.GetPackageAsync(id, _closing.Token), false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (CommunityException)
        {
            result = new Lookup(null, true);
        }
        _lookups[id] = result;
        if (id != _target.Id) return;
        UpdateVersion();
        Changed();
    }

    private IEnumerable<PublishedVersion> SavedVersions => _settings.Saved.Select(r => new PublishedVersion(r.Id, r.Version, r.Sha256));

    /// <summary>이미 올린 버전: 이 컴퓨터의 기록, 설치한 내 패키지, 서버가 알려 준 최신 버전.</summary>
    private IEnumerable<PublishedVersion> KnownVersions
        => SavedVersions
            .Concat(_target.Package is { } package ? [new PublishedVersion(package.Id, package.Version, "")] : [])
            .Concat(_lookups.Where(pair => pair.Value.Package is not null).Select(pair => new PublishedVersion(pair.Key, pair.Value.Package!.Version, "")));

    /// <summary>같은 ID로 전에 게시한 버전(서버·온라인 목록·이 컴퓨터)에서 다음 버전을 제안하고, 손대지 않았으면 채운다.</summary>
    private void UpdateVersion()
    {
        string? latest = AssetPublishCheck.LatestVersion(_target.Id, _catalog, KnownVersions);
        _suggested = AssetPublishCheck.SuggestVersion(latest);
        if (!_versionEdited) _version.Text = _suggested;
        _useSuggestion.IsVisible = _version.Text != _suggested;
        ShowMode();
    }

    /// <summary>
    /// 모드 줄: 서버 조회가 끝나지 않았으면 "확인하는 중", 실패면 "확인하지 못함", 없으면 "새 패키지", 있으면 "이름 업데이트: 이전 → 새 버전".
    /// 업데이트인데 ID가 이름에서 나왔으면(설치한 내 패키지의 에셋·사본이 아님) 이름이 같아 업데이트가 된다고 덧붙이고,
    /// 내 패키지의 사본으로 알아봤으면 그 패키지를 업데이트한다고 덧붙인다.
    /// </summary>
    private void ShowMode()
    {
        string note = "";
        if (!_lookups.TryGetValue(_target.Id, out var lookup))
        {
            _modeIcon.Icon = VectorIcon.LoaderCircle;
            _modeIcon.Foreground = AppTheme.DimTextBrush;
            _mode.Text = UiStrings.Publish_ModeChecking;
        }
        else if (lookup.Failed)
        {
            _modeIcon.Icon = VectorIcon.CircleAlert;
            _modeIcon.Foreground = AppTheme.WarningBrush;
            _mode.Text = UiStrings.Publish_ModeUnknown;
        }
        else if (lookup.Package is not { } package)
        {
            _modeIcon.Icon = VectorIcon.Plus;
            _modeIcon.Foreground = AppTheme.AccentBrush;
            _mode.Text = UiStrings.Publish_ModeNew;
        }
        else
        {
            _modeIcon.Icon = VectorIcon.Refresh;
            _modeIcon.Foreground = AppTheme.SavedBrush;
            _mode.Text = F(UiStrings.Publish_ModeUpdate, package.Name.Length > 0 ? package.Name : _target.Id, package.Version, _version.Text?.Trim() ?? "");
            note = _target.Source switch
            {
                PublishIdSource.Derived => UiStrings.Publish_ModeSameName,
                PublishIdSource.OwnPackageCopy => UiStrings.Publish_ModeOwnCopy,
                _ => "",
            };
        }
        _modeNote.Text = note;
        _modeNote.IsVisible = note.Length > 0;
    }

    /// <summary>언어 설정 창으로 언어별 이름·짧은 설명을 고친다.</summary>
    private void EditLanguages()
    {
        if (PackageTranslationsDialog.Ask(this, _name.Text ?? "", _summary.Text ?? "", _translations) is not { } edited) return;
        _translations = edited;
        ShowLanguages();
        UpdateTarget();
        Changed();
    }

    private void ShowLanguages()
        => _languages.Text = _translations.Count == 0 ? UiStrings.Publish_LanguagesNone
            : string.Join(", ", _translations.Select(pair => LanguagePicker.NativeName(pair.Key)));

    // ───────────── 미리보기 ─────────────

    private void ShowPreview()
    {
        _previewNote.IsVisible = _previewAuto.IsVisible = _customPreview is not null;
        _preview.Show(_customPreview ?? _assetPreview?.Png);
    }

    private void ChoosePreview()
    {
        if (Ui.OpenFile(this, UiStrings.Publish_PreviewReplace, null, new FileFilter(UiStrings.Preview_PngFilter, "*.png")) is not { } path) return;
        try
        {
            // 한도보다 큰 그림도 읽어 검사 목록에 까닭(크기 한도)을 보인다. 아주 큰 파일은 읽지 않는다.
            if (new FileInfo(path).Length > AssetPackageLimits.Default.MaxPackageBytes) throw new IOException(Path.GetFileName(path));
            _customPreview = File.ReadAllBytes(path);
            _previewNote.Text = F(UiStrings.Publish_PreviewCustom, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(this, F(UiStrings.Publish_PreviewError, ex.Message), UiStrings.Publish_Title);
            return;
        }
        ShowPreview();
        Changed();
    }

    // ───────────── 검사와 올리기 ─────────────

    private PublishRequest BuildRequest()
    {
        var translations = new Dictionary<string, AssetTranslation>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, translation) in _translations) translations.TryAdd(code, translation);
        return new PublishRequest
        {
            Author = new AssetAuthor(_user.Handle, _user.Name),
            Id = _target.Id,
            Version = _version.Text?.Trim() ?? "",
            Name = _name.Text ?? "",
            Summary = _summary.Text ?? "",
            Translations = translations,
            Tags = _tags.Tags,
            Asset = new PublishAsset(_asset.Kind, _asset.Data, _assetPreview),
            CustomPreview = _customPreview,
            MinAppVersion = AppInfo.IsDevBuild ? "0.0.0" : AppInfo.VersionText,
        };
    }

    private void RunCheck()
    {
        _result = AssetPublishCheck.Check(BuildRequest(), _catalog, KnownVersions);
        _checks.Children.Clear();
        foreach (var issue in _result.Issues.OrderBy(issue => issue.IsError ? 0 : 1))
            _checks.Children.Add(CheckRow(issue.IsError ? VectorIcon.X : VectorIcon.ExclamationMark,
                issue.IsError ? AppTheme.UnsavedBrush : AppTheme.WarningBrush, issue.Message));
        if (_result.Issues.Count == 0) _checks.Children.Add(CheckRow(VectorIcon.Check, AppTheme.AccentBrush, UiStrings.Publish_ChecksOk));
        _checksSummary.IsVisible = _result.Issues.Count > 0;
        _checksSummary.Text = _result.ErrorCount > 0 ? F(UiStrings.Publish_ChecksErrors, _result.ErrorCount, _result.WarningCount)
            : F(UiStrings.Publish_ChecksWarnings, _result.WarningCount);
        OkButton.IsEnabled = _result.Ok && _upload is null;
    }

    private static DockPanel CheckRow(VectorIcon icon, IBrush brush, string text)
    {
        var mark = new IconView(icon, brush, 2.6) { Width = 13, Height = 13, Margin = new Thickness(0, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(mark, Dock.Left);
        return new DockPanel
        {
            Children = { mark, new SelectableTextBlock { Text = text, Foreground = AppTheme.TextBrush, TextWrapping = TextWrapping.Wrap } },
        };
    }

    /// <summary>
    /// 다시 검사하고 오류가 없으면 패키지를 올린다(진행 막대·취소). 올리면 기록을 남기고 패키지 쪽을 연 뒤 닫는다.
    /// 서버가 거절한 까닭은 검사 목록 끝에, 세션이 끝났으면 계정 창을 연다.
    /// </summary>
    private async void Submit()
    {
        if (_upload is not null) return;
        _recheck.Stop();
        _tags.Commit(); // 입력 중이던 태그까지
        RunCheck(); // 입력 중이던 값까지 다시 검사
        if (_result is not { Ok: true } result) return;
        var manifest = result.Manifest!;
        using var cancel = new CancellationTokenSource();
        _upload = cancel;
        SetUploading(true);
        try
        {
            var package = await AppCommunity.Client.UploadPackageAsync(result.Package!, result.PackageFileName!, manifest.Description ?? manifest.Name,
                result.Tags, new Progress<double>(ReportProgress), cancel.Token);
            Remember(result);
            AppCommunity.Open(this, AppCommunity.Client.PackagePageUrl(package.Id));
            Ui.Info(this, F(UiStrings.Publish_Uploaded, package.Name.Length > 0 ? package.Name : manifest.Name, package.Version), UiStrings.Publish_Title);
            _upload = null;
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException)
        {
        }
        catch (CommunityException ex)
        {
            ShowServerIssues(ex);
            Ui.Error(this, ex.DisplayText, UiStrings.Publish_Title);
            if (ex.Code == CommunityErrorCode.Unauthorized && AccountDialog.Show(this, UiStrings.Publish_LoginReason)
                                                           && AppCommunity.Client.User is { } user)
                UseAccount(user);
        }
        finally
        {
            _upload = null;
            SetUploading(false);
        }
    }

    /// <summary>다시 로그인한 계정으로 작성자와 자동 패키지 ID를 바꾼다.</summary>
    private void UseAccount(CommunityUser user)
    {
        _user = user;
        _settings.AuthorName = user.Name;
        _authorId.Text = user.Handle;
        _authorName.Text = user.Name;
        UpdateTarget();
        Changed();
    }

    /// <summary>서버가 거절한 까닭을 검사 목록 끝에 오류 줄로 더한다(다음 검사에서 지워짐).</summary>
    private void ShowServerIssues(CommunityException ex)
    {
        _checks.Children.Add(CheckRow(VectorIcon.X, AppTheme.UnsavedBrush, F(UiStrings.Publish_ServerRejected, ex.Message)));
        foreach (var issue in ex.Issues) _checks.Children.Add(CheckRow(VectorIcon.X, AppTheme.UnsavedBrush, issue));
    }

    private void ReportProgress(double fraction)
    {
        _progress.Value = fraction;
        _progressText.Text = F(UiStrings.Publish_Uploading, (int)Math.Round(fraction * 100));
    }

    private void SetUploading(bool on)
    {
        _columns.IsEnabled = !on;
        _progressRow.IsVisible = on;
        if (on) ReportProgress(0);
        OkButton.IsEnabled = !on && _result is { Ok: true };
    }

    /// <summary>올린 기록을 남긴다(같은 에셋으로 다시 열 때 채우기, 다음 버전 제안, 같은 버전 내용 변경 경고).</summary>
    private void Remember(PublishResult result)
    {
        var manifest = result.Manifest!;
        _settings.Saved.RemoveAll(r => r.Id == manifest.Id && r.Version == manifest.Version);
        _settings.Saved.Insert(0, new AssetPublishRecord
        {
            Id = manifest.Id,
            Version = manifest.Version,
            Sha256 = result.Sha256!,
            Name = manifest.Name,
            Summary = manifest.Description ?? "",
            Tags = [.. result.Tags],
            Translations = new Dictionary<string, AssetTranslation>(manifest.Translations, StringComparer.OrdinalIgnoreCase),
            Assets = [_asset.Id],
            SavedUtc = DateTime.UtcNow,
        });
        if (_settings.Saved.Count > AssetPublishSettings.MaxSaved) _settings.Saved.RemoveRange(AssetPublishSettings.MaxSaved, _settings.Saved.Count - AssetPublishSettings.MaxSaved);
        App.Settings.Save();
    }

    /// <summary>PNG 바이트 하나를 정수 배로 키워 체커 위에 그린다(목록 미리보기).</summary>
    private sealed class PngView : Control
    {
        private WriteableBitmap? _bitmap;

        public void Show(byte[]? png)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            if (png is not null)
            {
                try
                {
                    using var stream = new MemoryStream(png, writable: false);
                    _bitmap = ImageUtil.ToBitmap(PngIO.Load(stream));
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or EndOfStreamException) { }
            }
            InvalidateVisual();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _bitmap?.Dispose();
            _bitmap = null;
        }

        public override void Render(DrawingContext g)
        {
            var b = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.PanelBrush, b);
            g.DrawRectangle(new Pen(AppTheme.BorderBrush, 1), b.Deflate(0.5));
            if (_bitmap is not { } bmp) return;
            var area = b.Deflate(8);
            var src = new Rectangle(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
            double fit = Math.Min(area.Width / src.Width, area.Height / src.Height);
            ImageUtil.DrawFitted(g, bmp, src, area, fit >= 1 ? (int)Math.Floor(fit) : 0, checker: true);
        }
    }
}
