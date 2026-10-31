using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Community;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 파일 > 갤러리에 올리기: 지금 프레임을 PNG로, 또는 애니메이션 전체(모든 프레임, 프레임 길이 그대로, 무한 반복)를 GIF로 만들어
/// 제목·설명·태그와 함께 Chickxel 갤러리에 올린다(그림은 프레임 시트·GIF 내보내기와 같이 합성 — 레이어 효과·지금 착장, 숨긴 레이어 제외).
/// 로그인하지 않았으면 올릴 때 계정 창을 연다. 올리는 동안 진행 막대와 취소, 끝나면 브라우저로 그 그림 쪽을 연다. 프로젝트는 바꾸지 않는다.
/// </summary>
public sealed class GalleryUploadDialog : DialogBase
{
    private const int FieldWidth = 320;

    private readonly EditorContext _ctx;
    private readonly SpriteProject _project;
    private readonly RadioButton _png, _gif;
    private readonly NumericUpDown _scale;
    private readonly TextBox _title, _description, _tags;
    private readonly TextBlock _info = Note(""), _account = Note(""), _error, _progressText = Note("");
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4, MinHeight = 4, Width = 260, Foreground = AppTheme.AccentBrush };
    private readonly StackPanel _progressRow;
    private readonly Control _form;
    private readonly PreviewView _preview = new() { Width = 220, Height = 220 };
    private CancellationTokenSource? _upload;

    public static void Run(EditorContext ctx, Window owner)
    {
        if (!ctx.HasProject) return;
        new GalleryUploadDialog(ctx).ShowDialog(owner);
    }

    private GalleryUploadDialog(EditorContext ctx) : base(UiStrings.Gallery_Title, UiStrings.Gallery_Upload)
    {
        _ctx = ctx;
        _project = ctx.Project ?? throw new InvalidOperationException(nameof(ctx.Project));
        OkButton.Width = double.NaN;
        OkButton.MinWidth = 90;

        _png = new RadioButton { GroupName = "kind", Content = UiStrings.Gallery_KindPng, Foreground = AppTheme.TextBrush, IsChecked = true };
        _gif = new RadioButton
        {
            GroupName = "kind", Content = F(UiStrings.Gallery_KindGif, _project.Frames.Count), Foreground = AppTheme.TextBrush,
            IsEnabled = _project.Frames.Count > 1,
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
                Ui.Group(UiStrings.Gallery_What, Ui.Stack(_png, _gif, Ui.Row(Ui.Label(UiStrings.Gallery_Scale), _scale), _info)),
                Ui.Group(UiStrings.Gallery_Details, Ui.Stack(
                    Ui.Label(UiStrings.Gallery_TitleLabel), _title,
                    Ui.Label(UiStrings.Gallery_DescriptionLabel), _description,
                    Ui.Label(UiStrings.Gallery_TagsLabel), _tags)),
                Ui.Group(UiStrings.Account_Title, Ui.Stack(_account, accountButton)),
            },
        };
        _form = settings;
        var previewColumn = new StackPanel
        {
            Spacing = 4, Margin = new Thickness(10, 0, 0, 0),
            Children = { _preview, Note(UiStrings.Gallery_PublicNote) },
        };
        previewColumn.Width = 240;
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { settings, previewColumn } });
        Body.Children.Add(_error);
        Body.Children.Add(_progressRow);

        _png.IsCheckedChanged += (_, _) => UpdateInfo();
        _scale.ValueChanged += (_, _) => UpdateInfo();
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
        Closed += (_, _) => _upload?.Cancel();

        _preview.Show(ProjectExport.CompositeFrame(_project, _ctx.CurrentFrame));
        UpdateInfo();
        UpdateAccount();
    }

    private static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static TextBlock Note(string text) => new() { Text = text, Foreground = AppTheme.DimTextBrush, TextWrapping = TextWrapping.Wrap };

    private bool AsGif => _gif.On();

    private int Scale => (int)_scale.NumValue();

    private void UpdateInfo()
    {
        int w = _project.Width * Scale, h = _project.Height * Scale;
        bool fits = w <= CommunityRules.MaxArtSide && h <= CommunityRules.MaxArtSide;
        _info.Text = AsGif ? F(UiStrings.Gallery_InfoGif, w, h, _project.Frames.Count) : F(UiStrings.Gallery_InfoPng, w, h, _ctx.CurrentFrame + 1);
        _info.Foreground = fits ? AppTheme.DimTextBrush : AppTheme.UnsavedBrush;
        if (!fits) _info.Text += "\n" + F(UiStrings.Gallery_TooBig, CommunityRules.MaxArtSide);
        OkButton.IsEnabled = fits && _upload is null;
    }

    private void UpdateAccount()
    {
        var client = AppCommunity.Client;
        _account.Text = client.User is { } user ? F(UiStrings.Gallery_SignedInAs, user.Name, user.Handle)
            : client.IsSignedIn ? UiStrings.Gallery_SignedInUnknown : UiStrings.Gallery_SignInLater;
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
        try
        {
            data = Encode();
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
            var upload = new ArtUpload(data, AsGif ? CommunityArtType.Gif : CommunityArtType.Png, title, description, tags);
            var art = await AppCommunity.Client.UploadArtAsync(upload, new Progress<double>(ReportProgress), cancel.Token);
            AppCommunity.Open(this, AppCommunity.Client.ArtPageUrl(art.Id));
            Ui.Info(this, F(UiStrings.Gallery_Done, art.Title), UiStrings.Gallery_Title);
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

    /// <summary>고른 형식·배율로 파일 바이트를 만든다(작업 스레드, 오래 걸리면 덮개).</summary>
    private byte[] Encode()
    {
        var project = _project;
        int scale = Scale, frame = _ctx.CurrentFrame;
        bool gif = AsGif;
        return WorkOverlay.Run(this, UiStrings.Gallery_Encoding, progress =>
        {
            using var buffer = new MemoryStream();
            if (gif)
            {
                var frames = GifWriter.ProjectFrames(project, null, progress);
                if (frames.Count > CommunityRules.MaxArtFrames) throw new InvalidOperationException(F(UiStrings.Gallery_TooManyFrames, CommunityRules.MaxArtFrames));
                GifWriter.Write(frames, buffer, new GifOptions { Scale = scale });
            }
            else
            {
                var image = ProjectExport.CompositeFrame(project, frame);
                if (scale > 1) image = Chickxel.Core.Edit.ImageTransform.ScaleNearest(image, image.Width * scale, image.Height * scale);
                PngIO.Save(image, buffer);
            }
            return buffer.ToArray();
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

    /// <summary>그림 하나를 정수 배로 키워(칸보다 크면 맞춤) 체커 위에 그린다.</summary>
    private sealed class PreviewView : Control
    {
        private WriteableBitmap? _bitmap;

        public void Show(PixelImage image)
        {
            _bitmap?.Dispose();
            _bitmap = ImageUtil.ToBitmap(image);
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
