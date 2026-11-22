using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Community;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 파일 > 갤러리에 올리기: 그림 여러 장(<see cref="CommunityRules.MaxArtImages"/>장까지)을 한 글로 Chickxel 갤러리에 올린다.
/// 왼쪽 "올릴 그림" 목록(내보내기 목록과 같은 줄 모양: 번호·이름(누르면 고름) [위로] [아래로] [빼기], 제목 아래 [더하기] = 줄을 바로 더하고 종류는 가운데 칸에서 고름)의
/// 줄마다 프레임 한 장의 PNG(기본 지금 프레임, 번호를 고름)나 GIF(모든 프레임, 태그 하나(그 재생 방향대로), 프레임 번호 범위, 창을 열 때 타임라인에서 고른 프레임 —
/// <see cref="GalleryFrames"/>)다. 첫 줄이 대표 그림. 가운데 칸은 고른 줄의 설정과 목록 전체가 같이 쓰는 배율·GIF 속도(<see cref="GifTimingField"/>)·
/// 편집용 파일 공유·받기 허용, 제목·설명·태그, 계정. 같은 그림이 되는 줄(<see cref="GalleryUploadPlan"/>)은 막지 않고 목록 아래에 경고만 하며 올릴 때 뺀다.
/// 오른쪽 미리보기는 올릴 그림을 차례로(PNG는 2초, GIF는 한 바퀴) 계속 돌려 보이고, 목록의 줄을 누르면 그 그림부터 보인다.
/// 그림은 프레임 시트·GIF 내보내기와 같이 합성(레이어 효과·지금 착장, 숨긴 레이어 제외). 고르면 편집용 <c>.chickxel</c> 파일
/// (이 컴퓨터의 경로를 뺀 것, <see cref="ProjectIO.SaveForSharing"/>)도 같이 올린다(기본은 끔).
/// 로그인하지 않았으면 올릴 때 계정 창을 연다. 올리는 동안 진행 막대와 취소, 끝나면 브라우저로 그 그림 쪽을 열고 내 정보를 다시 받아
/// 경험치·레벨이 올랐으면 알린다. 왼쪽 아래 "갤러리 보기"는 갤러리 쪽을 연다. 프로젝트는 바꾸지 않는다.
/// </summary>
public sealed class GalleryUploadDialog : DialogBase
{
    private const int FieldWidth = 320, ListWidth = 250;
    /// <summary>미리보기에서 PNG 한 장을 보이는 시간(ms, 다음 그림으로 넘어가기 전).</summary>
    private const int StillMs = 2000;

    /// <summary>올릴 목록의 줄 하나(창 안에서만 고침).</summary>
    private sealed class UploadItem
    {
        public CommunityArtType Type;
        /// <summary>PNG로 올릴 프레임(0부터).</summary>
        public int Frame;
        public GalleryFrameMode Mode = GalleryFrameMode.All;
        /// <summary>GIF 태그 범위의 태그(프로젝트의 태그, 없으면 null).</summary>
        public AnimationTag? Tag;
        /// <summary>GIF 프레임 번호 범위(1부터).</summary>
        public int From = 1, To = 1;
    }

    private readonly EditorContext _ctx;
    private readonly SpriteProject _project;
    private readonly List<UploadItem> _items = [];
    /// <summary>가운데 칸에서 고치는 줄.</summary>
    private int _selected;
    /// <summary>줄마다 만들 그림(<see cref="Refresh"/>에서 다시 고름)과 같은 그림 찾기 결과.</summary>
    private List<GalleryImageSource> _sources = [];
    private GalleryUploadPlan _plan = GalleryUploadPlan.Create([]);
    /// <summary>창 바뀜 처리기가 고른 줄을 칸에 넣는 중(바뀜을 줄에 되쓰지 않게).</summary>
    private bool _loading;

    private readonly StackPanel _list = new() { Spacing = 1 };
    /// <summary>줄마다 "미리보기 중" 표시(줄 순서).</summary>
    private readonly List<Control> _markers = [];
    private readonly TextBlock _listWarning, _listError;
    private readonly RadioButton _png, _gif;
    private readonly NumericUpDown _frame;
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
    /// <summary>GIF 재생 속도(GIF 내보내기와 같은 칸, 앱 설정에 같이 기억).</summary>
    private readonly GifTimingField _timing = new(App.Settings);
    private readonly StackPanel _timingRow;
    private readonly TextBox _title, _description, _tags;
    private readonly TextBlock _info = Note(""), _account = Note(""), _error, _progressText = Note(""), _previewCaption = Note("");
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4, MinHeight = 4, Width = 260, Foreground = AppTheme.AccentBrush };
    /// <summary>올릴 계정의 프로필 배지(확인한 계정이 있을 때만 — 없으면 <see cref="_account"/> 안내).</summary>
    private readonly ContentControl _accountBadge = new() { IsVisible = false };
    private readonly StackPanel _progressRow;
    private readonly Control _form;
    private readonly PreviewView _preview = new() { Width = 220, Height = 220 };
    /// <summary>미리보기 재생(GIF 프레임, 그림이 여러 장이면 다음 그림으로).</summary>
    private readonly DispatcherTimer _play = new();
    /// <summary>미리보기 프레임 그림(프레임 번호 → 그림, 처음 보일 때 합성). 창을 닫으면 버린다.</summary>
    private readonly Dictionary<int, WriteableBitmap> _bitmaps = new();
    private Chickxel.Core.Edit.EffectRenderCache? _effects;
    /// <summary>미리보기 중인 줄과 그 GIF의 몇 번째 프레임인지.</summary>
    private int _previewItem, _previewFrame;
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

        // ── 고른 줄: PNG(프레임 번호) / GIF(모든 프레임 / 태그 / 프레임 번호 범위 / 타임라인에서 고른 프레임(둘 이상일 때만)) ──
        _png = new RadioButton { GroupName = "kind", Content = UiStrings.Gallery_KindPng, Foreground = AppTheme.TextBrush };
        _frame = Ui.Num(1, Math.Max(1, frameCount), 1, width: 64);
        _gif = new RadioButton { GroupName = "kind", Content = UiStrings.Gallery_KindGif, Foreground = AppTheme.TextBrush, IsEnabled = frameCount > 1 };
        _rangeAll = Radio(F(UiStrings.Gallery_RangeAll, frameCount));
        _rangeTag = Radio(UiStrings.Gallery_RangeTag);
        _tagCombo = Ui.Combo(190, _project.Tags.Select(t => $"{t.Name}  [{t.From + 1}–{t.To + 1}]{DirectionText(t)}").ToArray());
        _rangeTag.IsEnabled = _tagCombo.IsEnabled = _project.Tags.Count > 0;
        _rangeFrames = Radio(UiStrings.Gallery_RangeFrames);
        _from = Ui.Num(1, Math.Max(1, frameCount), 1, width: 64);
        _to = Ui.Num(1, Math.Max(1, frameCount), Math.Max(1, frameCount), width: 64);
        _rangeSelected = Radio(F(UiStrings.Gallery_RangeSelected, _selectedFrames.Count));
        _rangeSelected.IsVisible = _selectedFrames.Count > 1;
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
        _timingRow = Ui.Row(Ui.Label(UiStrings.GifTiming_Label), _timing);
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
        _error = new TextBlock { Foreground = AppTheme.UnsavedBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 800, IsVisible = false };
        var cancelUpload = Ui.Button(VectorIcon.X, UiStrings.Gallery_CancelUpload, (_, _) => _upload?.Cancel());
        _progressRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, Children = { _progress, _progressText, cancelUpload } };
        _progress.VerticalAlignment = _progressText.VerticalAlignment = VerticalAlignment.Center;
        _listWarning = new TextBlock { Foreground = AppTheme.WarningBrush, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(2, 6, 2, 0) };
        _listError = new TextBlock { Foreground = AppTheme.UnsavedBrush, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(2, 6, 2, 0) };

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
                Ui.Group(UiStrings.Gallery_Item, Ui.Stack(Ui.Row(_png, _frame), Ui.Row(_gif, Ui.InfoBadge(UiStrings.Gallery_RangeTip)), _rangePanel, _info))
                    .HelpAt("screen-gallery", anchor: "Picture:"),
                Ui.Group(UiStrings.Gallery_Options, Ui.Stack(Ui.Row(Ui.Label(UiStrings.Gallery_Scale), _scale), _timingRow,
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
        _form = new StackPanel { Orientation = Orientation.Horizontal, Children = { ListPane(frameCount).HelpAt("screen-gallery", anchor: "Picture:"), settings } };
        var previewColumn = new StackPanel
        {
            Spacing = 4, Margin = new Thickness(10, 0, 0, 0),
            Children = { _preview, _previewCaption, Note(UiStrings.Gallery_PublicNote) },
        };
        previewColumn.Width = 240;
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _form, previewColumn.HelpAt("screen-gallery", anchor: "A still image of the current frame") } });
        Body.Children.Add(_error.HelpAt("screen-gallery", anchor: "Uploading"));
        Body.Children.Add(_progressRow.HelpAt("screen-gallery", anchor: "Uploading"));
        AddFooterLeft(Ui.Button(VectorIcon.ExternalLink, UiStrings.Gallery_OpenGallery,
            (_, _) => AppCommunity.Open(this, AppCommunity.Client.GalleryPageUrl())).Tip(UiStrings.Gallery_OpenGalleryTip));
        // 대상 도움말(F1)
        this.HelpAt("screen-gallery");
        OkButton.HelpAt("screen-gallery", anchor: "Uploading");

        _png.IsCheckedChanged += (_, _) => { if (_png.On()) Edit(i => i.Type = CommunityArtType.Png); };
        _gif.IsCheckedChanged += (_, _) => { if (_gif.On()) Edit(i => i.Type = CommunityArtType.Gif); };
        _frame.ValueChanged += (_, _) => Edit(i => i.Frame = (int)_frame.NumValue() - 1);
        foreach (var (radio, mode) in new[] { (_rangeAll, GalleryFrameMode.All), (_rangeTag, GalleryFrameMode.Tag), (_rangeFrames, GalleryFrameMode.Range), (_rangeSelected, GalleryFrameMode.Selected) })
            radio.IsCheckedChanged += (_, _) => { if (radio.On()) Edit(i => i.Mode = mode); };
        _tagCombo.SelectionChanged += (_, _) =>
        {
            if (_loading || _tagCombo.SelectedIndex < 0 || _tagCombo.SelectedIndex >= _project.Tags.Count) return;
            Current.Tag = _project.Tags[_tagCombo.SelectedIndex];
            if (!_rangeTag.On()) _rangeTag.IsChecked = true; // 바뀜 처리기가 다시 고른다
            else Refresh();
        };
        foreach (var n in new[] { _from, _to })
            n.ValueChanged += (_, _) =>
            {
                if (_loading) return;
                Current.From = (int)_from.NumValue();
                Current.To = (int)_to.NumValue();
                if (!_rangeFrames.On()) _rangeFrames.IsChecked = true;
                else Refresh();
            };
        _scale.ValueChanged += (_, _) => UpdateInfo();
        _timing.Changed += () =>
        {
            if (_play.IsEnabled)
            {
                _play.Stop();
                Schedule();
            }
        };
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
            _timing.Save(App.Settings);
            _play.Stop();
            foreach (var bitmap in _bitmaps.Values) bitmap.Dispose();
            _bitmaps.Clear();
            _effects = null;
        };

        // 처음 목록: 지금 프레임 PNG 한 장
        _items.Add(NewItem(CommunityArtType.Png));
        Select(0);
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

    private UploadItem Current => _items[_selected];

    private int Scale => (int)_scale.NumValue();

    private bool ShareProject => _shareProject.IsChecked == true;

    // ───────────── 올릴 목록 ─────────────

    /// <summary>왼쪽 칸 "올릴 그림": 제목 줄 + [더하기] + 줄들 + 아래에 같은 그림 경고·오류.</summary>
    private Control ListPane(int frameCount)
    {
        var title = new TextBlock { Text = UiStrings.Gallery_What, FontWeight = FontWeight.Bold, Foreground = AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        var add = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5,
                Children = { Ui.Icon(VectorIcon.Plus), new TextBlock { Text = UiStrings.Gallery_Add, VerticalAlignment = VerticalAlignment.Center } },
            },
            Padding = new Thickness(8, 1),
        }.Tip(UiStrings.Gallery_AddTip);
        // 종류(PNG·GIF)와 프레임은 더한 뒤 가운데 칸에서 고른다. 프레임이 여럿이면 첫 줄(지금 프레임 PNG)과 겹치지 않게 GIF로 더한다.
        add.Click += (_, _) => AddItem(frameCount > 1 ? CommunityArtType.Gif : CommunityArtType.Png);
        var header = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 6),
            Children = { Ui.Row(title, Ui.InfoBadge(F(UiStrings.Gallery_ItemsTip, CommunityRules.MaxArtImages))), Ui.Row(add) },
        };
        DockPanel.SetDock(header, Dock.Top);
        var notes = new StackPanel { Children = { _listWarning, _listError } };
        DockPanel.SetDock(notes, Dock.Bottom);
        return new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4, 6, 6),
            Margin = new Thickness(2, 4, 6, 2),
            Width = ListWidth,
            Child = new DockPanel
            {
                Children = { header, notes, new ScrollViewer { Content = _list, MaxHeight = 380, VerticalAlignment = VerticalAlignment.Top, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } },
            },
        };
    }

    /// <summary>새 줄: PNG면 지금 프레임, GIF면 모든 프레임(태그 칸은 지금 프레임의 태그나 첫 태그, 범위는 처음~끝).</summary>
    private UploadItem NewItem(CommunityArtType type)
    {
        var tags = _project.Tags;
        return new UploadItem
        {
            Type = type,
            Frame = Math.Clamp(_ctx.CurrentFrame, 0, Math.Max(0, _project.Frames.Count - 1)),
            Tag = _project.TagOfFrame(_ctx.CurrentFrame) ?? (tags.Count > 0 ? tags[0] : null),
            From = 1,
            To = Math.Max(1, _project.Frames.Count),
        };
    }

    private void AddItem(CommunityArtType type)
    {
        _items.Add(NewItem(type));
        Select(_items.Count - 1);
    }

    private void RemoveItem(int index)
    {
        if (_items.Count <= 1) return;
        _items.RemoveAt(index);
        Select(_selected > index ? _selected - 1 : Math.Min(_selected, _items.Count - 1));
    }

    private void MoveItem(int index, int by)
    {
        int to = index + by;
        if (to < 0 || to >= _items.Count) return;
        (_items[index], _items[to]) = (_items[to], _items[index]);
        Select(_selected == index ? to : _selected == to ? index : _selected);
    }

    /// <summary>줄 <paramref name="index"/>를 고른다: 가운데 칸이 그 줄의 설정이 되고, 미리보기는 그 그림부터 돈다.</summary>
    private void Select(int index)
    {
        _selected = Math.Clamp(index, 0, _items.Count - 1);
        LoadItem();
        Refresh();
    }

    /// <summary>고른 줄의 설정을 칸에 넣는다(바뀜 처리기는 줄을 고치지 않음).</summary>
    private void LoadItem()
    {
        var item = Current;
        _loading = true;
        try
        {
            bool gif = item.Type == CommunityArtType.Gif;
            _png.IsChecked = !gif;
            _gif.IsChecked = gif;
            _frame.Value = item.Frame + 1;
            _from.Value = item.From;
            _to.Value = item.To;
            int tag = item.Tag is null ? -1 : _project.Tags.ToList().IndexOf(item.Tag);
            _tagCombo.SelectedIndex = tag;
            (item.Mode switch
            {
                GalleryFrameMode.Tag => _rangeTag,
                GalleryFrameMode.Range => _rangeFrames,
                GalleryFrameMode.Selected => _rangeSelected,
                _ => _rangeAll,
            }).IsChecked = true;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>칸이 바뀜: 고른 줄을 고치고 목록·미리보기를 다시.</summary>
    private void Edit(Action<UploadItem> change)
    {
        if (_loading) return;
        change(Current);
        Refresh();
    }

    /// <summary>줄 하나가 만들 그림(PNG는 프레임 한 장, GIF는 <see cref="GalleryFrames.Select"/>의 순서).</summary>
    private GalleryImageSource Source(UploadItem item)
        => item.Type == CommunityArtType.Png
            ? GalleryImageSource.Png(Math.Clamp(item.Frame, 0, Math.Max(0, _project.Frames.Count - 1)))
            : GalleryImageSource.Gif(GalleryFrames.Select(_project, item.Mode, item.Tag, item.From, item.To, _selectedFrames));

    /// <summary>줄 이름: "PNG · 프레임 3", "GIF · 태그 idle" 등.</summary>
    private string ItemLabel(int index)
    {
        var item = _items[index];
        if (item.Type == CommunityArtType.Png) return F(UiStrings.Gallery_ItemPng, _sources[index].Frames[0] + 1);
        string range = item.Mode switch
        {
            GalleryFrameMode.Tag => F(UiStrings.Gallery_ItemTag, item.Tag?.Name ?? ""),
            GalleryFrameMode.Range => F(UiStrings.Gallery_ItemRange, item.From, item.To),
            GalleryFrameMode.Selected => F(UiStrings.Gallery_RangeSelected, _selectedFrames.Count),
            _ => F(UiStrings.Gallery_RangeAll, _project.Frames.Count),
        };
        return F(UiStrings.Gallery_ItemGif, range);
    }

    /// <summary>줄들이 만들 그림과 같은 그림을 다시 찾고 목록·안내·미리보기(고른 줄부터)를 다시 그린다.</summary>
    private void Refresh()
    {
        _sources = _items.Select(Source).ToList();
        _plan = GalleryUploadPlan.Create(_sources);
        bool gif = Current.Type == CommunityArtType.Gif;
        _frame.IsEnabled = !gif;
        _rangePanel.IsEnabled = gif;
        RebuildList();
        UpdateInfo();
        PreviewItem(_selected);
    }

    private void RebuildList()
    {
        _list.Children.Clear();
        _markers.Clear();
        for (int i = 0; i < _items.Count; i++) _list.Children.Add(Row(i));
    }

    /// <summary>목록 줄 하나: [미리보기 중 표시] 번호·이름(누르면 고름) [위로] [아래로] [빼기]. 고른 줄은 배경색, 같은 그림이라 뺄 줄은 흐리게.</summary>
    private Control Row(int index)
    {
        string label = $"{index + 1}. {ItemLabel(index)}";
        bool duplicate = _plan.SameAs[index] is not null;
        var marker = Ui.Icon(VectorIcon.Play, 9, filled: true);
        marker.Margin = new Thickness(0, 0, 4, 0);
        marker.Opacity = 0;
        _markers.Add(marker);
        var text = new TextBlock
        {
            Text = label, Foreground = duplicate ? AppTheme.DimTextBrush : AppTheme.TextBrush, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTip.SetTip(text, duplicate ? label + "\n" + DuplicateText(index) : label);
        var up = Ui.IconButton(VectorIcon.ChevronUp, UiStrings.Gallery_MoveUp, (_, _) => Dispatcher.UIThread.Post(() => MoveItem(index, -1)), size: 12);
        var down = Ui.IconButton(VectorIcon.ChevronDown, UiStrings.Gallery_MoveDown, (_, _) => Dispatcher.UIThread.Post(() => MoveItem(index, 1)), size: 12);
        var del = Ui.IconButton(VectorIcon.X, UiStrings.Gallery_Remove, (_, _) => Dispatcher.UIThread.Post(() => RemoveItem(index)), size: 12);
        up.IsEnabled = index > 0;
        down.IsEnabled = index < _items.Count - 1;
        del.IsEnabled = _items.Count > 1;
        DockPanel.SetDock(marker, Dock.Left);
        foreach (var b in new[] { del, down, up }) DockPanel.SetDock(b, Dock.Right);
        var row = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1),
            Background = index == _selected ? AppTheme.HoverBrush : Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new DockPanel { Children = { marker, del, down, up, text } },
        };
        // 단추는 누르기를 처리해(Handled) 여기 오지 않는다
        row.PointerPressed += (_, _) => Dispatcher.UIThread.Post(() => Select(index));
        return row;
    }

    private string DuplicateText(int index) => F(UiStrings.Gallery_Duplicate, index + 1, _plan.SameAs[index]!.Value + 1);

    // ───────────── 미리보기 ─────────────

    /// <summary>줄 <paramref name="index"/>의 그림을 처음부터 보이고 재생을 이어 간다.</summary>
    private void PreviewItem(int index)
    {
        _play.Stop();
        _previewItem = index;
        _previewFrame = 0;
        ShowPreviewFrame();
        Schedule();
    }

    private void ShowPreviewFrame()
    {
        var frames = _sources[_previewItem].Frames;
        _preview.Show(frames.Count == 0 ? null : Bitmap(frames[_previewFrame]));
        _previewCaption.Text = $"{_previewItem + 1}. {ItemLabel(_previewItem)}";
        _previewCaption.IsVisible = _items.Count > 1;
        for (int i = 0; i < _markers.Count; i++) _markers[i].Opacity = i == _previewItem && _items.Count > 1 ? 1 : 0;
    }

    /// <summary>다음으로 넘길 다른 그림이 있는지(올릴 그림이 둘 이상이거나 지금 보이는 것이 뺄 줄).</summary>
    private bool CyclesItems => _plan.Unique.Count > 1 || _plan.SameAs[_previewItem] is not null;

    /// <summary>
    /// 지금 보이는 것의 길이만큼 기다렸다 <see cref="Advance"/>: GIF 프레임은 고른 속도로 바꾼 길이(GIF 최소 지연 이상), PNG는 <see cref="StillMs"/>
    /// (다른 그림이 없으면 멈춘 채).
    /// </summary>
    private void Schedule()
    {
        var source = _sources[_previewItem];
        int ms;
        if (source.Type == CommunityArtType.Gif && source.Frames.Count > 0)
        {
            if (source.Frames.Count < 2 && !CyclesItems) return;
            ms = Math.Max(GifWriter.MinDelayCs * 10, _timing.Timing.DurationMs(_project.Frames[source.Frames[_previewFrame]].DurationMs));
        }
        else
        {
            if (!CyclesItems) return;
            ms = StillMs;
        }
        _play.Interval = TimeSpan.FromMilliseconds(ms);
        _play.Start();
    }

    /// <summary>GIF면 다음 프레임, 마지막 프레임(또는 PNG)이면 다음 올릴 그림(끝이면 처음으로) — 그림이 하나뿐인 GIF는 처음 프레임으로.</summary>
    private void Advance()
    {
        _play.Stop();
        var source = _sources[_previewItem];
        if (source.Type == CommunityArtType.Gif && _previewFrame + 1 < source.Frames.Count) _previewFrame++;
        else if (CyclesItems)
        {
            _previewItem = _plan.Unique.FirstOrDefault(i => i > _previewItem, _plan.Unique[0]);
            _previewFrame = 0;
        }
        else _previewFrame = 0;
        ShowPreviewFrame();
        Schedule();
    }

    /// <summary>프레임 <paramref name="frame"/>의 합성 그림(처음이면 합성해 둔다).</summary>
    private WriteableBitmap Bitmap(int frame)
    {
        if (!_bitmaps.TryGetValue(frame, out var bitmap))
            _bitmaps[frame] = bitmap = ImageUtil.ToBitmap(ProjectExport.CompositeFrame(_project, frame, _effects ??= ProjectExport.NewCache()));
        return bitmap;
    }

    // ───────────── 안내 ─────────────

    /// <summary>줄 <paramref name="index"/>를 올릴 수 없는 까닭(올릴 수 있으면 null).</summary>
    private string? ItemProblem(int index)
    {
        var source = _sources[index];
        if (source.Type != CommunityArtType.Gif) return null;
        if (source.Frames.Count == 0) return UiStrings.Gallery_NoFrames;
        if (source.Frames.Count > CommunityRules.MaxArtFrames) return F(UiStrings.Gallery_TooManyFrames, CommunityRules.MaxArtFrames);
        return null;
    }

    /// <summary>고른 줄의 크기·프레임 안내, 목록 아래 같은 그림 경고와 올릴 수 없는 줄·그림 수 오류, 올리기 단추.</summary>
    private void UpdateInfo()
    {
        int w = _project.Width * Scale, h = _project.Height * Scale;
        bool fits = w <= CommunityRules.MaxArtSide && h <= CommunityRules.MaxArtSide;
        var source = _sources[_selected];
        _info.Text = source.Type == CommunityArtType.Gif ? F(UiStrings.Gallery_InfoGif, w, h, source.Frames.Count) : F(UiStrings.Gallery_InfoPng, w, h, source.Frames[0] + 1);
        if (!fits) _info.Text += "\n" + F(UiStrings.Gallery_TooBig, CommunityRules.MaxArtSide);
        _info.Foreground = fits ? AppTheme.DimTextBrush : AppTheme.UnsavedBrush;

        var warnings = new List<string>();
        var errors = new List<string>();
        for (int i = 0; i < _items.Count; i++)
        {
            if (_plan.SameAs[i] is not null) warnings.Add(DuplicateText(i));
            else if (ItemProblem(i) is { } problem) errors.Add(F(UiStrings.Gallery_ItemProblem, i + 1, problem));
        }
        if (_plan.Unique.Count > CommunityRules.MaxArtImages) errors.Add(F(UiStrings.Gallery_TooManyImages, CommunityRules.MaxArtImages));
        _listWarning.Text = string.Join("\n", warnings);
        _listWarning.IsVisible = warnings.Count > 0;
        _listError.Text = string.Join("\n", errors);
        _listError.IsVisible = errors.Count > 0;
        OkButton.IsEnabled = fits && errors.Count == 0 && _upload is null;
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

        // 같은 그림은 앞 줄만(목록 순서, 첫 줄 = 대표)
        var unique = _plan.Unique;
        int skipped = _plan.DuplicateCount;
        List<ArtUploadImage> images;
        byte[]? projectFile;
        try
        {
            (images, projectFile) = Encode([.. unique.Select(i => _sources[i])]);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            ShowError(ex.Message);
            return;
        }
        for (int k = 0; k < images.Count; k++)
        {
            int length = images[k].Data.Length;
            if (length <= CommunityRules.MaxArtBytes) continue;
            ShowError(_items.Count == 1
                ? F(UiStrings.Gallery_FileTooLarge, length / (1024.0 * 1024), CommunityRules.MaxArtBytes >> 20)
                : F(UiStrings.Gallery_ImageTooLarge, unique[k] + 1, length / (1024.0 * 1024), CommunityRules.MaxArtBytes >> 20));
            return;
        }
        long total = images.Sum(i => (long)i.Data.Length);
        if (total > CommunityRules.MaxArtTotalBytes)
        {
            ShowError(F(UiStrings.Gallery_TotalTooLarge, total / (1024.0 * 1024), CommunityRules.MaxArtTotalBytes >> 20));
            return;
        }

        using var cancel = new CancellationTokenSource();
        _upload = cancel;
        SetUploading(true);
        try
        {
            var before = AppCommunity.Client.User;
            var upload = new ArtUpload(images, title, description, tags, projectFile, AllowDownload: _allowDownload.IsChecked == true);
            var art = await AppCommunity.Client.UploadArtAsync(upload, new Progress<double>(ReportProgress), cancel.Token);
            Uploaded = true;
            AppCommunity.Open(this, AppCommunity.Client.ArtPageUrl(art.Id));
            string done = F(UiStrings.Gallery_Done, art.Title);
            if (skipped > 0) done += "\n" + F(UiStrings.Gallery_Skipped, skipped);
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
    /// 올릴 그림 <paramref name="sources"/>를 고른 배율(·GIF 속도)로 파일 바이트로, 공유를 골랐으면 편집용 <c>.chickxel</c> 바이트도 한 번 만든다
    /// (작업 스레드, 오래 걸리면 덮개 — 그동안 프로젝트를 아무도 고치지 않으므로 모든 파일이 같은 상태를 담는다). 여러 그림이 쓰는 프레임은 한 번만 합성한다.
    /// <c>.chickxel</c>이 한도를 넘으면 그림은 만들지 않고 알린다.
    /// </summary>
    private (List<ArtUploadImage> Images, byte[]? Project) Encode(IReadOnlyList<GalleryImageSource> sources)
    {
        var project = _project;
        int scale = Scale;
        bool share = ShareProject;
        var timing = _timing.Timing;
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
            foreach (var source in sources)
            {
                if (source.Type != CommunityArtType.Gif) continue;
                if (source.Frames.Count == 0) throw new InvalidOperationException(UiStrings.Gallery_NoFrames);
                if (source.Frames.Count > CommunityRules.MaxArtFrames) throw new InvalidOperationException(F(UiStrings.Gallery_TooManyFrames, CommunityRules.MaxArtFrames));
            }
            var composed = new Dictionary<int, PixelImage>();
            var needed = sources.SelectMany(s => s.Frames).Distinct().ToList();
            var effects = ProjectExport.NewCache();
            using (var stage = progress.Begin(needed.Count))
                foreach (int frame in needed)
                {
                    composed[frame] = ProjectExport.CompositeFrame(project, frame, effects);
                    stage.Step(1);
                }
            var images = new List<ArtUploadImage>(sources.Count);
            foreach (var source in sources)
            {
                using var buffer = new MemoryStream();
                if (source.Type == CommunityArtType.Gif)
                {
                    var frames = source.Frames.Select(f => new GifFrame(composed[f], project.Frames[f].DurationMs)).ToList();
                    GifWriter.Write(frames, buffer, new GifOptions { Scale = scale, Timing = timing });
                }
                else
                {
                    var image = composed[source.Frames[0]];
                    if (scale > 1) image = Chickxel.Core.Edit.ImageTransform.ScaleNearest(image, image.Width * scale, image.Height * scale);
                    PngIO.Save(image, buffer);
                }
                images.Add(new ArtUploadImage(buffer.ToArray(), source.Type));
            }
            return (images, projectFile);
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
