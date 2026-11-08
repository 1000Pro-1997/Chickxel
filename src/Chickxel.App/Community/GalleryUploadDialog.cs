using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Community;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 파일 > 갤러리에 올리기: 지금 프레임을 PNG로, 또는 애니메이션(프레임 길이 그대로, 무한 반복)을 GIF로 만들어
/// 제목·설명·태그와 함께 Chickxel 갤러리에 올린다(그림은 프레임 시트·GIF 내보내기와 같이 합성 — 레이어 효과·지금 착장, 숨긴 레이어 제외).
/// GIF는 모든 프레임, 태그 하나(그 재생 방향대로), 프레임 번호 범위, 창을 열 때 타임라인에서 고른 프레임(둘 이상일 때) 중에서 고르고
/// (<see cref="GalleryFrames"/>), 미리보기가 그 순서로 재생한다.
/// 고르면 편집용 <c>.chickxel</c> 파일(이 컴퓨터의 경로를 뺀 것, <see cref="ProjectIO.SaveForSharing"/>)도 같이 올린다(기본은 끔).
/// 로그인하지 않았으면 올릴 때 계정 창을 연다. 올리는 동안 진행 막대와 취소, 끝나면 브라우저로 그 그림 쪽을 열고 내 정보를 다시 받아
/// 경험치·레벨이 올랐으면 알린다. 왼쪽 아래 "갤러리 보기"는 갤러리 쪽을 연다. 프로젝트는 바꾸지 않는다.
/// </summary>
public sealed class GalleryUploadDialog : DialogBase
{
    private const int FieldWidth = 320;

    private readonly EditorContext _ctx;
    private readonly SpriteProject _project;
    private readonly RadioButton _png, _gif;
    private readonly RadioButton _rangeAll, _rangeTag, _rangeFrames, _rangeSelected;
    private readonly ComboBox _tagCombo;
    private readonly NumericUpDown _from, _to;
    private readonly Control _rangePanel;
    /// <summary>창을 열 때 타임라인에서 고른 프레임(0부터, 오름차순).</summary>
    private readonly IReadOnlyList<int> _selectedFrames;
    private readonly CheckBox _shareProject;
    private readonly CheckBox _allowDownload;
    private readonly TextBlock _scriptsNote;
    private readonly NumericUpDown _scale;
    private readonly TextBox _title, _description, _tags;
    private readonly TextBlock _info = Note(""), _account = Note(""), _error, _progressText = Note("");
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4, MinHeight = 4, Width = 260, Foreground = AppTheme.AccentBrush };
    /// <summary>올릴 계정의 프로필 배지(확인한 계정이 있을 때만 — 없으면 <see cref="_account"/> 안내).</summary>
    private readonly ContentControl _accountBadge = new() { IsVisible = false };
    private readonly StackPanel _progressRow;
    private readonly Control _form;
    private readonly PreviewView _preview = new() { Width = 220, Height = 220 };
    /// <summary>GIF 미리보기 재생(고른 프레임이 둘 이상이고 GIF일 때만 돈다).</summary>
    private readonly DispatcherTimer _play = new();
    /// <summary>미리보기 프레임 그림(프레임 번호 → 그림, 처음 보일 때 합성). 창을 닫으면 버린다.</summary>
    private readonly Dictionary<int, WriteableBitmap> _bitmaps = new();
    private Chickxel.Core.Edit.EffectRenderCache? _effects;
    /// <summary>GIF로 올릴 프레임 순서(<see cref="GalleryFrames.Select"/>).</summary>
    private List<int> _order = [];
    private int _playIndex;
    private CancellationTokenSource? _upload;

    /// <summary>갤러리에 올렸는지.</summary>
    public bool Uploaded { get; private set; }

    /// <summary>창을 띄운다. <paramref name="selectedFrames"/>는 타임라인에서 고른 프레임(0부터). 올렸으면 true.</summary>
    public static bool Run(EditorContext ctx, Window owner, IReadOnlyList<int>? selectedFrames = null)
    {
        if (!ctx.HasProject) return false;
        var dialog = new GalleryUploadDialog(ctx, selectedFrames ?? []);
        dialog.ShowDialog(owner);
        return dialog.Uploaded;
    }

    private GalleryUploadDialog(EditorContext ctx, IReadOnlyList<int> selectedFrames) : base(UiStrings.Gallery_Title, UiStrings.Gallery_Upload)
    {
        _ctx = ctx;
        _project = ctx.Project ?? throw new InvalidOperationException(nameof(ctx.Project));
        int frameCount = _project.Frames.Count;
        _selectedFrames = GalleryFrames.Select(_project, GalleryFrameMode.Selected, selected: selectedFrames);
        OkButton.Width = double.NaN;
        OkButton.MinWidth = 90;

        _png = new RadioButton { GroupName = "kind", Content = UiStrings.Gallery_KindPng, Foreground = AppTheme.TextBrush, IsChecked = true };
        _gif = new RadioButton { GroupName = "kind", Content = UiStrings.Gallery_KindGif, Foreground = AppTheme.TextBrush, IsEnabled = frameCount > 1 };

        // GIF 범위: 모든 프레임 / 태그 / 프레임 번호 범위 / 타임라인에서 고른 프레임(둘 이상일 때만)
        _rangeAll = Radio(F(UiStrings.Gallery_RangeAll, frameCount));
        _rangeTag = Radio(UiStrings.Gallery_RangeTag);
        _tagCombo = Ui.Combo(190, _project.Tags.Select(t => $"{t.Name}  [{t.From + 1}–{t.To + 1}]{DirectionText(t)}").ToArray());
        _rangeTag.IsEnabled = _tagCombo.IsEnabled = _project.Tags.Count > 0;
        var currentTag = _project.TagOfFrame(ctx.CurrentFrame);
        if (_project.Tags.Count > 0) _tagCombo.SelectedIndex = currentTag is not null ? _project.Tags.ToList().IndexOf(currentTag) : 0;
        _rangeFrames = Radio(UiStrings.Gallery_RangeFrames);
        _from = Ui.Num(1, Math.Max(1, frameCount), 1, width: 64);
        _to = Ui.Num(1, Math.Max(1, frameCount), Math.Max(1, frameCount), width: 64);
        _rangeSelected = Radio(F(UiStrings.Gallery_RangeSelected, _selectedFrames.Count));
        _rangeSelected.IsVisible = _selectedFrames.Count > 1;
        _rangeAll.IsChecked = true;
        var dash = Ui.Label("~");
        dash.VerticalAlignment = VerticalAlignment.Center;
        _rangePanel = new StackPanel
        {
            Spacing = 0, Margin = new Thickness(22, 0, 0, 2), IsEnabled = false,
            Children = { _rangeAll, Ui.Row(_rangeTag, _tagCombo), Ui.Row(_rangeFrames, _from, dash, _to), _rangeSelected },
        };

        int side = Math.Max(_project.Width, _project.Height);
        int maxScale = Math.Clamp(CommunityRules.MaxArtSide / Math.Max(1, side), 1, 10);
        _scale = Ui.Num(1, maxScale, 1, width: 60).Unit("×");
        _title = new TextBox
        {
            Text = Path.GetFileNameWithoutExtension(_project.DisplayName), Width = FieldWidth, MaxLength = CommunityRules.MaxTitleLength,
            Watermark = UiStrings.Gallery_TitleHint,
        };
        _description = new TextBox
        {
            Width = FieldWidth, Height = 90, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = CommunityRules.MaxDescriptionLength,
            Watermark = UiStrings.Gallery_DescriptionHint,
        };
        _tags = new TextBox { Width = FieldWidth, Watermark = UiStrings.Gallery_TagsHint };
        _shareProject = Ui.Check(UiStrings.Gallery_ShareProject);
        bool hasScripts = _project.ScriptsInUse().Count > 0;
        _scriptsNote = Note(UiStrings.Gallery_ShareProjectScripts);
        _scriptsNote.IsVisible = false;
        _shareProject.IsCheckedChanged += (_, _) => _scriptsNote.IsVisible = hasScripts && ShareProject;
        // 그림 쪽의 PNG/GIF 받기 버튼(기본 켬, 끄면 올린 사람과 관리자에게만 보임)
        _allowDownload = Ui.Check(UiStrings.Gallery_AllowDownload, value: true);
        _error = new TextBlock { Foreground = AppTheme.UnsavedBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, IsVisible = false };
        var cancelUpload = Ui.Button(VectorIcon.X, UiStrings.Gallery_CancelUpload, (_, _) => _upload?.Cancel());
        _progressRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, Children = { _progress, _progressText, cancelUpload } };
        _progress.VerticalAlignment = _progressText.VerticalAlignment = VerticalAlignment.Center;

        var accountButton = Ui.Button(VectorIcon.CircleUser, UiStrings.Account_Menu, (_, _) =>
        {
            AccountDialog.Show(this);
            UpdateAccount();
        });
        var settings = new StackPanel
        {
            Spacing = 2,
            Width = FieldWidth + 20,
            Children =
            {
                Ui.Group(UiStrings.Gallery_What, Ui.Stack(_png, Ui.Row(_gif, Ui.InfoBadge(UiStrings.Gallery_RangeTip)), _rangePanel,
                    Ui.Row(Ui.Label(UiStrings.Gallery_Scale), _scale), _info,
                    Ui.Row(_shareProject, Ui.InfoBadge(UiStrings.Gallery_ShareProjectTip)).HelpAt("screen-gallery", anchor: "Sharing the editable file"),
                    _scriptsNote.HelpAt("screen-gallery", anchor: "Sharing the editable file"),
                    Ui.Row(_allowDownload, Ui.InfoBadge(UiStrings.Gallery_AllowDownloadTip)))).HelpAt("screen-gallery", anchor: "Picture:"),
                Ui.Group(UiStrings.Gallery_Details, Ui.Stack(
                    Ui.Label(UiStrings.Gallery_TitleLabel), _title,
                    Ui.Label(UiStrings.Gallery_DescriptionLabel), _description,
                    Ui.Label(UiStrings.Gallery_TagsLabel), _tags)).HelpAt("screen-gallery", anchor: "Details:"),
                Ui.Group(UiStrings.Account_Title, Ui.Stack(_account, _accountBadge, accountButton)).HelpAt("screen-gallery", anchor: "The account to upload with"),
            },
        };
        _form = settings;
        var previewColumn = new StackPanel
        {
            Spacing = 4, Margin = new Thickness(10, 0, 0, 0),
            Children = { _preview, Note(UiStrings.Gallery_PublicNote) },
        };
        previewColumn.Width = 240;
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { settings, previewColumn.HelpAt("screen-gallery", anchor: "A still image of the current frame") } });
        Body.Children.Add(_error.HelpAt("screen-gallery", anchor: "Uploading"));
        Body.Children.Add(_progressRow.HelpAt("screen-gallery", anchor: "Uploading"));
        AddFooterLeft(Ui.Button(VectorIcon.ExternalLink, UiStrings.Gallery_OpenGallery,
            (_, _) => AppCommunity.Open(this, AppCommunity.Client.GalleryPageUrl())).Tip(UiStrings.Gallery_OpenGalleryTip));
        // 대상 도움말(F1)
        this.HelpAt("screen-gallery");
        OkButton.HelpAt("screen-gallery", anchor: "Uploading");

        _png.IsCheckedChanged += (_, _) => RangeChanged();
        foreach (var r in new[] { _rangeAll, _rangeTag, _rangeFrames, _rangeSelected })
            r.IsCheckedChanged += (_, _) => { if (r.On()) RangeChanged(); };
        _tagCombo.SelectionChanged += (_, _) =>
        {
            if (_tagCombo.SelectedIndex < 0) return;
            if (!_rangeTag.On()) _rangeTag.IsChecked = true; // 바뀜 처리기가 다시 고른다
            else RangeChanged();
        };
        foreach (var n in new[] { _from, _to })
            n.ValueChanged += (_, _) =>
            {
                if (!_rangeFrames.On()) _rangeFrames.IsChecked = true;
                else RangeChanged();
            };
        _scale.ValueChanged += (_, _) => UpdateInfo();
        _play.Tick += (_, _) => Advance();
        OkButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.None; // 올린 뒤에 닫는다
            Dispatcher.UIThread.Post(Upload);
        };
        CancelButtonControl.Click += (_, _) =>
        {
            if (_upload is null) return;
            _upload.Cancel();
            DialogResult = DialogResult.None; // 올리기만 멈추고 창은 둔다
        };
        // 시작할 때 백그라운드로 확인하는 계정 정보가 창을 연 뒤에 오면 계정 칸을 바로 바꾼다(요청을 마친 스레드에서 올 수 있음).
        Action onAccounts = () => Dispatcher.UIThread.Post(UpdateAccount);
        AppCommunity.Client.AccountsChanged += onAccounts;
        Closed += (_, _) =>
        {
            AppCommunity.Client.AccountsChanged -= onAccounts;
            _upload?.Cancel();
            _play.Stop();
            foreach (var bitmap in _bitmaps.Values) bitmap.Dispose();
            _bitmaps.Clear();
            _effects = null;
        };

        RangeChanged();
        UpdateAccount();
    }

    private static RadioButton Radio(string text) => new() { GroupName = "range", Content = text, Foreground = AppTheme.TextBrush };

    private static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static TextBlock Note(string text) => new() { Text = text, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap };

    private static string DirectionText(AnimationTag t) => t.Direction switch
    {
        TagDirection.Reverse => UiStrings.S_GifExportDialog_DirectionText_FE009F0D02,
        TagDirection.PingPong => UiStrings.S_GifExportDialog_DirectionText_46319C3ED7,
        _ => "",
    };

    private bool AsGif => _gif.On();

    private int Scale => (int)_scale.NumValue();

    private bool ShareProject => _shareProject.IsChecked == true;

    private GalleryFrameMode RangeMode
        => _rangeTag.On() ? GalleryFrameMode.Tag
            : _rangeFrames.On() ? GalleryFrameMode.Range
            : _rangeSelected.On() ? GalleryFrameMode.Selected
            : GalleryFrameMode.All;

    /// <summary>형식·범위가 바뀌면 올릴 프레임 순서를 다시 고르고 미리보기를 처음부터(PNG면 지금 프레임 한 장).</summary>
    private void RangeChanged()
    {
        _rangePanel.IsEnabled = AsGif;
        var tag = _tagCombo.SelectedIndex >= 0 && _tagCombo.SelectedIndex < _project.Tags.Count ? _project.Tags[_tagCombo.SelectedIndex] : null;
        _order = GalleryFrames.Select(_project, RangeMode, tag, (int)_from.NumValue(), (int)_to.NumValue(), _selectedFrames);
        _play.Stop();
        _playIndex = 0;
        if (!AsGif) _preview.Show(Bitmap(_ctx.CurrentFrame));
        else if (_order.Count == 0) _preview.Show(null);
        else
        {
            _preview.Show(Bitmap(_order[0]));
            if (_order.Count > 1) Schedule();
        }
        UpdateInfo();
    }

    /// <summary>지금 보이는 프레임 길이만큼 기다렸다 다음 프레임으로.</summary>
    private void Schedule()
    {
        _play.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _project.Frames[_order[_playIndex]].DurationMs));
        _play.Start();
    }

    private void Advance()
    {
        _play.Stop();
        if (!AsGif || _order.Count < 2) return;
        _playIndex = (_playIndex + 1) % _order.Count;
        _preview.Show(Bitmap(_order[_playIndex]));
        Schedule();
    }

    /// <summary>프레임 <paramref name="frame"/>의 합성 그림(처음이면 합성해 둔다).</summary>
    private WriteableBitmap Bitmap(int frame)
    {
        if (!_bitmaps.TryGetValue(frame, out var bitmap))
            _bitmaps[frame] = bitmap = ImageUtil.ToBitmap(ProjectExport.CompositeFrame(_project, frame, _effects ??= ProjectExport.NewCache()));
        return bitmap;
    }

    private void UpdateInfo()
    {
        int w = _project.Width * Scale, h = _project.Height * Scale;
        bool fits = w <= CommunityRules.MaxArtSide && h <= CommunityRules.MaxArtSide;
        _info.Text = AsGif ? F(UiStrings.Gallery_InfoGif, w, h, _order.Count) : F(UiStrings.Gallery_InfoPng, w, h, _ctx.CurrentFrame + 1);
        bool ok = fits;
        if (!fits) _info.Text += "\n" + F(UiStrings.Gallery_TooBig, CommunityRules.MaxArtSide);
        if (AsGif && _order.Count == 0)
        {
            _info.Text += "\n" + UiStrings.Gallery_NoFrames;
            ok = false;
        }
        else if (AsGif && _order.Count > CommunityRules.MaxArtFrames)
        {
            _info.Text += "\n" + F(UiStrings.Gallery_TooManyFrames, CommunityRules.MaxArtFrames);
            ok = false;
        }
        _info.Foreground = ok ? AppTheme.DimTextBrush : AppTheme.UnsavedBrush;
        OkButton.IsEnabled = ok && _upload is null;
    }

    private void UpdateAccount()
    {
        var client = AppCommunity.Client;
        var user = client.User;
        _accountBadge.Content = user is null ? null : IdentityBadge.User(user, 20, handle: IdentityHandle.Inline, maxWidth: FieldWidth);
        _accountBadge.IsVisible = user is not null;
        _account.IsVisible = user is null;
        _account.Text = user is not null ? "" : client.IsSignedIn ? UiStrings.Gallery_SignedInUnknown : UiStrings.Gallery_SignInLater;
    }

    private void ShowError(string? text)
    {
        _error.Text = text ?? "";
        _error.IsVisible = !string.IsNullOrEmpty(text);
    }

    // ───────────── 올리기 ─────────────

    private async void Upload()
    {
        if (_upload is not null) return;
        ShowError(null);
        string title = _title.Text?.Trim() ?? "", description = _description.Text?.Trim() ?? "";
        var tags = Chickxel.Core.Assets.AssetPublishCheck.ParseTags(_tags.Text);
        if (!CommunityRules.IsValidTitle(title))
        {
            ShowError(F(UiStrings.Gallery_TitleInvalid, CommunityRules.MaxTitleLength));
            return;
        }
        if (!CommunityRules.IsValidDescription(description))
        {
            ShowError(F(UiStrings.Gallery_DescriptionInvalid, CommunityRules.MaxDescriptionLength));
            return;
        }
        if (!CommunityRules.IsValidTags(tags))
        {
            ShowError(UiStrings.Gallery_TagsInvalid);
            return;
        }

        if (await AppCommunity.RequireUserAsync(this, UiStrings.Gallery_LoginReason) is null)
        {
            UpdateAccount();
            return;
        }
        UpdateAccount();

        byte[] data;
        byte[]? projectFile;
        try
        {
            (data, projectFile) = Encode();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            ShowError(ex.Message);
            return;
        }
        if (data.Length > CommunityRules.MaxArtBytes)
        {
            ShowError(F(UiStrings.Gallery_FileTooLarge, data.Length / (1024.0 * 1024), CommunityRules.MaxArtBytes >> 20));
            return;
        }

        using var cancel = new CancellationTokenSource();
        _upload = cancel;
        SetUploading(true);
        try
        {
            var before = AppCommunity.Client.User;
            var upload = new ArtUpload(data, AsGif ? CommunityArtType.Gif : CommunityArtType.Png, title, description, tags, projectFile,
                AllowDownload: _allowDownload.IsChecked == true);
            var art = await AppCommunity.Client.UploadArtAsync(upload, new Progress<double>(ReportProgress), cancel.Token);
            Uploaded = true;
            AppCommunity.Open(this, AppCommunity.Client.ArtPageUrl(art.Id));
            string done = F(UiStrings.Gallery_Done, art.Title);
            if (await LevelGainAsync(before) is { } gain) done += "\n\n" + gain;
            Ui.Info(this, done, UiStrings.Gallery_Title);
            _upload = null;
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException)
        {
            ShowError(UiStrings.Gallery_Cancelled);
        }
        catch (CommunityException ex)
        {
            ShowError(ex.DisplayText);
            if (ex.Code == CommunityErrorCode.Unauthorized)
            {
                AccountDialog.Show(this, UiStrings.Gallery_LoginReason);
                UpdateAccount();
            }
        }
        finally
        {
            _upload = null;
            SetUploading(false);
        }
    }

    /// <summary>
    /// 올린 뒤 내 정보를 다시 받아 레벨이 오르거나 경험치가 늘었으면 그 글("레벨이 올랐습니다! Lv 3", "+10 XP · Lv 3"), 아니면(예전 서버·받지 못함) null.
    /// </summary>
    private static async Task<string?> LevelGainAsync(CommunityUser? before)
    {
        CommunityUser? after;
        try
        {
            after = await AppCommunity.Client.GetMeAsync();
        }
        catch (Exception ex) when (ex is CommunityException or OperationCanceledException)
        {
            return null;
        }
        if (before is not { Level: > 0 } || after is not { Level: > 0 }) return null;
        int gained = before.HasXp && after.HasXp ? after.Xp - before.Xp : 0;
        bool levelUp = after.Level > before.Level;
        var lines = new List<string>(2);
        if (levelUp) lines.Add(F(UiStrings.Gallery_LevelUp, after.Level));
        if (gained > 0) lines.Add(F(UiStrings.Gallery_XpGained, gained, after.Level));
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    /// <summary>
    /// 고른 형식·범위·배율로 그림 파일 바이트를, 공유를 골랐으면 편집용 <c>.chickxel</c> 바이트도 만든다(작업 스레드, 오래 걸리면 덮개 — 그동안 프로젝트를
    /// 아무도 고치지 않으므로 두 파일이 같은 상태를 담는다). <c>.chickxel</c>이 한도를 넘으면 그림은 만들지 않고 알린다.
    /// </summary>
    private (byte[] Image, byte[]? Project) Encode()
    {
        var project = _project;
        int scale = Scale, frame = _ctx.CurrentFrame;
        bool gif = AsGif, share = ShareProject;
        var order = _order;
        return WorkOverlay.Run(this, UiStrings.Gallery_Encoding, progress =>
        {
            byte[]? projectFile = null;
            if (share)
            {
                using var file = new MemoryStream();
                ProjectIO.SaveForSharing(project, file, progress);
                if (file.Length > CommunityRules.MaxProjectBytes)
                    throw new InvalidOperationException(F(UiStrings.Gallery_ProjectTooLarge, file.Length / (1024.0 * 1024), CommunityRules.MaxProjectBytes >> 20));
                projectFile = file.ToArray();
            }
            using var buffer = new MemoryStream();
            if (gif)
            {
                if (order.Count == 0) throw new InvalidOperationException(UiStrings.Gallery_NoFrames);
                if (order.Count > CommunityRules.MaxArtFrames) throw new InvalidOperationException(F(UiStrings.Gallery_TooManyFrames, CommunityRules.MaxArtFrames));
                var frames = GifWriter.ProjectFramesAt(project, order, progress);
                GifWriter.Write(frames, buffer, new GifOptions { Scale = scale });
            }
            else
            {
                var image = ProjectExport.CompositeFrame(project, frame);
                if (scale > 1) image = Chickxel.Core.Edit.ImageTransform.ScaleNearest(image, image.Width * scale, image.Height * scale);
                PngIO.Save(image, buffer);
            }
            return (buffer.ToArray(), projectFile);
        });
    }

    private void ReportProgress(double fraction)
    {
        _progress.Value = fraction;
        _progressText.Text = F(UiStrings.Gallery_Uploading, (int)Math.Round(fraction * 100));
    }

    private void SetUploading(bool on)
    {
        _form.IsEnabled = !on;
        _progressRow.IsVisible = on;
        if (on) ReportProgress(0);
        UpdateInfo();
    }

    /// <summary>그림 하나를 정수 배로 키워(칸보다 크면 맞춤) 체커 위에 그린다. 그림은 창이 가지고 있다(여기서 버리지 않음).</summary>
    private sealed class PreviewView : Control
    {
        private WriteableBitmap? _bitmap;

        public void Show(WriteableBitmap? bitmap)
        {
            _bitmap = bitmap;
            InvalidateVisual();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
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
