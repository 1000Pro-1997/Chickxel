using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.Community;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>
/// 에셋 게시 창(계획 8.4, 9.3, 14장 5단계): 개인 에셋을 공개 패키지로 묶어 Chickxel 커뮤니티 서비스에 올린다(로그인 필요).
/// 왼쪽은 작성자 정보(작성자 ID = 계정 핸들, 표시 이름·주소는 설정에 기억), 패키지 ID(핸들.이름)·버전(전에 올린 버전에서 다음 버전 제안)·이름·짧은 설명·태그,
/// 라이선스(흔한 SPDX ID나 직접 입력한 ID + 글), 오른쪽은 포함할 에셋, 목록 미리보기(에셋 미리보기 중 고르거나 PNG로 바꿈), 올리기 전 검사 결과.
/// 입력이 바뀔 때마다 <see cref="AssetPublishCheck.Check"/>로 다시 검사하고, 오류가 없을 때만 올린다(진행 막대·취소).
/// 서버가 거절하면 그 까닭을 검사 목록에 보이고, 올리면 브라우저로 패키지 쪽(<c>/packages/&lt;ID&gt;</c>)을 연다.
/// </summary>
public sealed class AssetPublishDialog : DialogBase
{
    private const int FieldWidth = 300;

    private readonly AssetPublishSettings _settings = App.Settings.AssetPublish;
    private readonly IReadOnlyList<RemoteCatalogEntry> _catalog;
    private readonly List<(AssetEntry Entry, CheckBox Box)> _assets = [];
    private readonly Dictionary<string, byte[]?> _previews = new(StringComparer.Ordinal);
    private readonly TextBox _authorId, _authorName, _authorUrl, _id, _version, _name, _summary, _tags, _licenseId, _licenseText;
    private readonly ComboBox _license;
    private readonly ComboBox _previewChoice = Ui.Combo(220);
    private readonly TextBlock _versionHint = Note(""), _licenseNote = Note(""), _assetsTitle = Ui.Label("", bold: true), _checksSummary = Note(""), _previewNote = Note("");
    private readonly Button _useSuggestion, _previewAuto;
    private readonly Control _customLicense;
    private readonly StackPanel _checks = new() { Spacing = 4 };
    private readonly PngView _preview = new() { Height = 150 };
    private readonly DispatcherTimer _recheck = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4, MinHeight = 4, Width = 220, Foreground = AppTheme.AccentBrush };
    private readonly TextBlock _progressText = Note("");
    private readonly StackPanel _progressRow;
    private readonly Control _columns;
    /// <summary>미리보기 목록 항목 순서의 에셋(= 포함한 에셋).</summary>
    private List<AssetEntry> _previewEntries = [];
    private byte[]? _customPreview;
    private bool _idEdited, _versionEdited, _filling = true;
    private string _suggested = "1.0.0";
    private PublishResult? _result;
    private CancellationTokenSource? _upload;

    /// <summary>
    /// 게시 창을 연다. 로그인하지 않았으면 먼저 계정 창. <paramref name="preselected"/>는 처음 체크할 개인 에셋 ID
    /// (전에 같은 에셋으로 올린 기록이 있으면 그 패키지 정보와 에셋을 다시 채움).
    /// </summary>
    public static async Task RunAsync(Window owner, IEnumerable<string> preselected)
    {
        if (AppAssets.Catalog.Personal.Count == 0)
        {
            Ui.Info(owner, UiStrings.Publish_NoPersonal, UiStrings.Publish_Title);
            return;
        }
        var ids = preselected.ToHashSet(StringComparer.Ordinal);
        if (await AppCommunity.RequireUserAsync(owner, UiStrings.Publish_LoginReason) is not { } user) return;
        new AssetPublishDialog(ids, user).ShowDialog(owner);
    }

    private AssetPublishDialog(HashSet<string> preselected, CommunityUser user) : base(UiStrings.Publish_Title, UiStrings.Publish_Upload)
    {
        CanResize = true;
        SizeToContent = SizeToContent.Manual;
        Width = 1000;
        Height = 760;
        MinWidth = 820;
        MinHeight = 560;
        OkButton.Width = double.NaN;
        OkButton.MinWidth = 90;
        OkButton.Background = AppTheme.AccentBrush;
        OkButton.Foreground = Brushes.White;
        OkButton.FontWeight = FontWeight.Bold;
        _catalog = AppAssets.Remote.LoadCache()?.Packages ?? [];

        var personal = AppAssets.Catalog.Personal;
        var record = _settings.Saved.FirstOrDefault(r => r.Assets.Any(preselected.Contains));
        var check = new HashSet<string>(preselected, StringComparer.Ordinal);
        if (record is not null) check.UnionWith(record.Assets);

        // ── 왼쪽: 작성자·패키지·라이선스 ──
        // 작성자 ID = 계정 핸들(서버가 패키지 ID 첫 조각·작성자 ID를 핸들과 맞춰 봄). 핸들은 계정 창에서 바꾼다.
        _authorId = Field(user.Handle);
        _authorId.IsReadOnly = true;
        _authorName = Field(AssetManifest.IsValidName(_settings.AuthorName) ? _settings.AuthorName : user.Name);
        _authorUrl = Field(_settings.AuthorUrl, "https://");
        var single = personal.Where(e => check.Contains(e.Id)).ToList() is [var only] ? only : null;
        _name = Field(record?.Name ?? single?.Name ?? UiStrings.Assets_DefaultPackageName);
        _id = Field(record?.Id is { } recordId && recordId.StartsWith(user.Handle + ".", StringComparison.Ordinal) ? recordId : "");
        _version = Field("1.0.0");
        _version.Width = 110;
        _summary = new TextBox
        {
            Text = record?.Summary ?? "", Width = FieldWidth, Height = 56, Margin = new Thickness(3), TextWrapping = TextWrapping.Wrap,
            MaxLength = RemoteCatalogFormat.MaxSummaryLength, Watermark = UiStrings.Publish_SummaryHint,
        };
        _tags = Field(record is null ? "" : string.Join(", ", record.Tags), UiStrings.Publish_TagsHint);
        _useSuggestion = Ui.Button(UiStrings.Publish_UseSuggestion, (_, _) =>
        {
            _versionEdited = false;
            _version.Text = _suggested;
        });

        string[] licenseItems = [UiStrings.Publish_LicenseChoose, .. AssetPublishCheck.CommonLicenses, UiStrings.Publish_LicenseCustom];
        _license = Ui.Combo(FieldWidth, licenseItems);
        _licenseId = Field("", UiStrings.Publish_LicenseIdHint);
        _licenseText = new TextBox { Width = FieldWidth, Height = 110, Margin = new Thickness(3), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        _customLicense = Ui.Stack(Rows((UiStrings.Publish_LicenseId, _licenseId, null)), Ui.Label(UiStrings.Publish_LicenseText), _licenseText);
        if (record is { License.Length: > 0 } saved)
        {
            int common = AssetPublishCheck.CommonLicenses.ToList().IndexOf(saved.License);
            _license.SelectedIndex = common >= 0 ? common + 1 : licenseItems.Length - 1;
            if (common < 0)
            {
                _licenseId.Text = saved.License;
                _licenseText.Text = saved.LicenseText ?? "";
            }
        }
        if (string.IsNullOrEmpty(_id.Text)) _id.Text = DerivedId();
        _idEdited = _id.Text != DerivedId();

        var left = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Note(UiStrings.Publish_Intro),
                Ui.Group(UiStrings.Publish_Author, Ui.Stack(
                    Rows((UiStrings.Publish_AuthorId, _authorId, UiStrings.Publish_AuthorIdTip),
                        (UiStrings.Publish_AuthorName, _authorName, null),
                        (UiStrings.Publish_AuthorUrl, _authorUrl, null)),
                    Note(UiStrings.Publish_AuthorNote))),
                Ui.Group(UiStrings.Publish_Package, Ui.Stack(
                    Rows((UiStrings.Publish_Name, _name, null),
                        (UiStrings.Publish_Id, _id, UiStrings.Publish_IdTip),
                        (UiStrings.Publish_Version, Ui.Row(_version, _useSuggestion), null)),
                    _versionHint,
                    Rows((UiStrings.Publish_Summary, _summary, null),
                        (UiStrings.Publish_Tags, _tags, null)))),
                Ui.Group(UiStrings.Publish_License, Ui.Stack(_license, _licenseNote, _customLicense)),
            },
        };

        // ── 오른쪽: 에셋·미리보기·검사 ──
        var assetList = new StackPanel { Spacing = 0 };
        // 체크한 에셋을 위에(긴 목록에서도 무엇이 들어가는지 바로 보이게), 나머지는 개인 에셋 순서.
        foreach (var entry in personal.OrderBy(e => check.Contains(e.Id) ? 0 : 1))
        {
            string kind = AssetManagerWindow.KindText(entry.Kind);
            var box = Ui.Check($"{entry.Name}  ·  {kind}", check.Contains(entry.Id));
            box.IsCheckedChanged += (_, _) =>
            {
                RefreshPreviewChoices();
                Changed();
            };
            _assets.Add((entry, box));
            assetList.Children.Add(box);
        }
        var assetScroll = new ScrollViewer { Content = assetList, MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _previewChoice.SelectionChanged += (_, _) =>
        {
            ShowPreview();
            Changed();
        };
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
                Ui.Group(UiStrings.Publish_Assets, Ui.Stack(_assetsTitle, assetScroll)),
                new Border
                {
                    BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 4, 6, 6), Margin = new Thickness(2, 4, 2, 2),
                    Child = new StackPanel { Spacing = 2, Children = { previewHeader, _preview, new WrapPanel { Children = { _previewChoice, replace, _previewAuto } }, _previewNote } },
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

        foreach (var box in new[] { _authorId, _authorName, _authorUrl, _name, _summary, _tags, _licenseId, _licenseText })
            box.TextChanged += (_, _) => Changed();
        _authorId.TextChanged += (_, _) => UpdateId();
        _name.TextChanged += (_, _) => UpdateId();
        // TextChanged는 값을 넣은 뒤 늦게 올 수 있어(코드로 넣은 값 포함) "손댔는지"는 지금 값과 자동 값을 비교해 정한다.
        _id.TextChanged += (_, _) =>
        {
            _idEdited = (_id.Text ?? "") != DerivedId();
            UpdateVersion();
            Changed();
        };
        _version.TextChanged += (_, _) =>
        {
            _versionEdited = _version.Text != _suggested;
            _useSuggestion.IsVisible = _versionEdited;
            Changed();
        };
        _license.SelectionChanged += (_, _) =>
        {
            UpdateLicenseNote();
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
            RememberAuthor();
            App.Settings.Save();
        };

        RefreshPreviewChoices();
        UpdateLicenseNote();
        UpdateVersion();
        _filling = false;
        RunCheck();
    }

    // ───────────── 입력 ─────────────

    private static TextBox Field(string value, string? watermark = null)
        => new() { Text = value, Width = FieldWidth, Margin = new Thickness(3), Watermark = watermark };

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
            label.Margin = new Thickness(0, 3, 6, 0);
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

    private List<AssetEntry> Included => _assets.Where(a => a.Box.On()).Select(a => a.Entry).ToList();

    private void Changed()
    {
        if (_filling) return;
        _recheck.Stop();
        _recheck.Start();
    }

    /// <summary>패키지 ID를 손대지 않았으면 "작성자 ID.이름 조각"으로 맞춘다.</summary>
    private void UpdateId()
    {
        if (!_idEdited) _id.Text = DerivedId();
    }

    private string DerivedId()
    {
        string author = _authorId.Text?.Trim() ?? "";
        string slug = AssetPackageBuilder.Slug(_name.Text ?? "", "assets");
        return author.Length > 0 ? author + "." + slug : slug;
    }

    private IEnumerable<PublishedVersion> SavedVersions => _settings.Saved.Select(r => new PublishedVersion(r.Id, r.Version, r.Sha256));

    /// <summary>같은 ID로 전에 게시한 버전(온라인 목록·이 컴퓨터의 저장 기록)에서 다음 버전을 제안하고, 손대지 않았으면 채운다.</summary>
    private void UpdateVersion()
    {
        string id = _id.Text?.Trim() ?? "";
        string? latest = AssetPublishCheck.LatestVersion(id, _catalog, SavedVersions);
        _suggested = AssetPublishCheck.SuggestVersion(latest);
        _versionHint.Text = latest is null ? UiStrings.Publish_VersionFirst : F(UiStrings.Publish_VersionPrevious, latest, _suggested);
        if (!_versionEdited) _version.Text = _suggested;
        _useSuggestion.IsVisible = _version.Text != _suggested;
    }

    private bool CustomLicense => _license.SelectedIndex == _license.ItemCount - 1;

    private string LicenseId => _license.SelectedIndex switch
    {
        <= 0 => "",
        _ when CustomLicense => _licenseId.Text?.Trim() ?? "",
        var i => AssetPublishCheck.CommonLicenses[i - 1],
    };

    private void UpdateLicenseNote()
    {
        _customLicense.IsVisible = CustomLicense;
        _licenseNote.Text = _license.SelectedIndex switch
        {
            <= 0 => UiStrings.Publish_LicenseNone,
            _ when CustomLicense => UiStrings.Publish_LicenseCustomNote,
            var i => LicenseDescription(AssetPublishCheck.CommonLicenses[i - 1]) + " " + UiStrings.Publish_LicenseFile,
        };
    }

    private static string LicenseDescription(string spdx) => spdx switch
    {
        "CC0-1.0" => UiStrings.Publish_License_CC0,
        "CC-BY-4.0" => UiStrings.Publish_License_CCBY,
        "CC-BY-SA-4.0" => UiStrings.Publish_License_CCBYSA,
        "CC-BY-NC-4.0" => UiStrings.Publish_License_CCBYNC,
        "MIT" => UiStrings.Publish_License_MIT,
        "Apache-2.0" => UiStrings.Publish_License_Apache,
        _ => spdx,
    };

    // ───────────── 미리보기 ─────────────

    private byte[]? PreviewOf(AssetEntry entry)
    {
        if (!_previews.TryGetValue(entry.Id, out var png)) _previews[entry.Id] = png = AssetPreview.Png(entry.Data);
        return png;
    }

    /// <summary>미리보기 목록을 포함한 에셋으로 다시 만든다(고른 에셋이 남아 있으면 그대로).</summary>
    private void RefreshPreviewChoices()
    {
        var included = Included;
        int selected = _previewChoice.SelectedIndex;
        string? keep = selected >= 0 && selected < _previewEntries.Count ? _previewEntries[selected].Id : null;
        bool filling = _filling;
        _filling = true;
        _previewChoice.Items.Clear();
        foreach (var entry in included) _previewChoice.Items.Add(entry.Name);
        _previewEntries = included;
        _previewChoice.SelectedIndex = included.Count == 0 ? -1 : Math.Max(0, included.FindIndex(e => e.Id == keep));
        _filling = filling;
        _assetsTitle.Text = F(UiStrings.Publish_AssetsCount, included.Count, _assets.Count);
        ShowPreview();
    }

    private void ShowPreview()
    {
        var included = Included;
        int index = _previewChoice.SelectedIndex;
        _previewChoice.IsEnabled = _customPreview is null && included.Count > 0;
        _previewNote.IsVisible = _previewAuto.IsVisible = _customPreview is not null;
        _preview.Show(_customPreview ?? (index >= 0 && index < included.Count ? PreviewOf(included[index]) : null));
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
        string license = LicenseId;
        string name = _name.Text ?? "";
        string authorName = _authorName.Text ?? "";
        string? licenseText = CustomLicense ? _licenseText.Text
            : license.Length > 0 ? AssetPublishCheck.LicenseNotice(license, name, authorName, DateTime.Now.Year) : null;
        return new PublishRequest
        {
            Author = new AssetAuthor(_authorId.Text?.Trim() ?? "", authorName, _authorUrl.Text?.Trim()),
            Id = _id.Text?.Trim() ?? "",
            Version = _version.Text?.Trim() ?? "",
            Name = name,
            Summary = _summary.Text ?? "",
            Tags = AssetPublishCheck.ParseTags(_tags.Text),
            License = license,
            LicenseText = licenseText,
            Assets = Included.Select(entry => new PublishAsset(entry.Kind, entry.Data, PreviewOf(entry))).ToArray(),
            CustomPreview = _customPreview,
            PreviewAsset = Math.Max(0, _previewChoice.SelectedIndex),
            MinAppVersion = AppInfo.IsDevBuild ? "0.0.0" : AppInfo.VersionText,
        };
    }

    private void RunCheck()
    {
        _result = AssetPublishCheck.Check(BuildRequest(), _catalog, SavedVersions);
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
                _authorId.Text = user.Handle;
        }
        finally
        {
            _upload = null;
            SetUploading(false);
        }
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
        RememberAuthor();
        _settings.Saved.RemoveAll(r => r.Id == manifest.Id && r.Version == manifest.Version);
        _settings.Saved.Insert(0, new AssetPublishRecord
        {
            Id = manifest.Id,
            Version = manifest.Version,
            Sha256 = result.Sha256!,
            Name = manifest.Name,
            Summary = manifest.Description ?? "",
            Tags = [.. result.Tags],
            License = manifest.License,
            LicenseText = CustomLicense ? _licenseText.Text : null,
            Assets = [.. Included.Select(entry => entry.Id)],
            SavedUtc = DateTime.UtcNow,
        });
        if (_settings.Saved.Count > AssetPublishSettings.MaxSaved) _settings.Saved.RemoveRange(AssetPublishSettings.MaxSaved, _settings.Saved.Count - AssetPublishSettings.MaxSaved);
        App.Settings.Save();
    }

    private void RememberAuthor()
    {
        _settings.AuthorName = _authorName.Text?.Trim() ?? "";
        _settings.AuthorUrl = _authorUrl.Text?.Trim() ?? "";
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
